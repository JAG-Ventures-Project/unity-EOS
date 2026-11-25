using System;
using System.Collections.Generic;
using System.Text;
using EOS.Auth;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.PlayerDataStorage;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Core
{
    /// <summary>
    /// Cloud PlayerDataStorage manager with encrypted local backup, offline save deferral, and server-authoritative merge on load
    /// https://dev.epicgames.com/docs/epic-online-services/player-and-game-data/player-data-storage-interface
    /// </summary>
    public class PlayerDataStorageManager : IPlayerDataStorage
    {
        public const string NotFoundError = "NotFound";
        private const uint DefaultChunkSizeBytes = 4096;

        private static readonly OnReadFileDataCallback s_OnReadFileData = HandleReadFileDataStatic;
        private static readonly OnReadFileCompleteCallback s_OnReadFileCompleted = HandleReadFileCompletedStatic;
        private static readonly OnWriteFileDataCallback s_OnWriteFileData = HandleWriteFileDataStatic;
        private static readonly OnWriteFileCompleteCallback s_OnWriteFileCompleted = HandleWriteFileCompletedStatic;
        private static readonly OnDeleteFileCompleteCallback s_OnDeleteFileCompleted = HandleDeleteFileCompletedStatic;

        private readonly PlayerDataStorageInterface _storageHandle;
        private readonly LocalEncryptedStorage _localStorage;

        private readonly HashSet<string> _loadedFiles = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, CloudReadTransfer> _activeReads = new Dictionary<string, CloudReadTransfer>(StringComparer.Ordinal);
        private readonly Dictionary<string, CloudWriteTransfer> _activeWrites = new Dictionary<string, CloudWriteTransfer>(StringComparer.Ordinal);
        private readonly Dictionary<string, PendingCloudWrite> _pendingWrites = new Dictionary<string, PendingCloudWrite>(StringComparer.Ordinal);

        private ProductUserId _localUserId;
        private string _localUserIdKey;

        internal LocalEncryptedStorage LocalStorage => _localStorage;

        private static PlayerDataStorageManager _instance;
        public static PlayerDataStorageManager Instance => _instance ??= new PlayerDataStorageManager(EOSSDKManager.PlatformInterface);

        public PlayerDataStorageManager(PlatformInterface platformInterface = null, LocalEncryptedStorage localStorage = null)
        {
            PlatformInterface platform = platformInterface ?? EOSSDKManager.PlatformInterface;
            Assert.IsNotNull(platform, "[PlayerDataStorageManager] PlatformInterface must not be null.");

            _storageHandle = platform.GetPlayerDataStorageInterface();
            Assert.IsNotNull(_storageHandle, "[PlayerDataStorageManager] PlayerDataStorageInterface failed to initialize.");

            _localStorage = localStorage ?? new LocalEncryptedStorage(null, EOSSDKManager.Instance.EosApiKey.encryptionKey);

            CacheUserId(ConnectManager.Instance?.LocalProductUserId);

            ConnectManager.OnLoginSuccess += HandleLoginSuccess;
            SessionAuthCoordinator.OnLoggedOut += HandleLoggedOut;
            EOSSDKManager.OnShutdown += HandleShutdown;
        }

        private void CacheUserId(ProductUserId userId)
        {
            if (userId.IsValid())
            {
                _localUserId = userId;
                _localUserIdKey = userId.ToString();
            }
        }

        private void HandleLoginSuccess(ProductUserId userId)
        {
            _loadedFiles.Clear();
            CacheUserId(userId);
        }

        /// <summary>
        /// Clears cached user credentials and loaded-file state on logout so one account's session state never leaks into the next.
        /// </summary>
        private void HandleLoggedOut()
        {
            _localUserId = null;
            _localUserIdKey = null;
            _loadedFiles.Clear();
        }

        private void HandleShutdown()
        {
            ConnectManager.OnLoginSuccess -= HandleLoginSuccess;
            SessionAuthCoordinator.OnLoggedOut -= HandleLoggedOut;
            EOSSDKManager.OnShutdown -= HandleShutdown;

            foreach (KeyValuePair<string, CloudReadTransfer> pair in _activeReads)
            {
                pair.Value.DisposeResources();
            }
            foreach (KeyValuePair<string, CloudWriteTransfer> pair in _activeWrites)
            {
                pair.Value.ReleaseHandle();
            }

            _activeReads.Clear();
            _activeWrites.Clear();
            _pendingWrites.Clear();
            _loadedFiles.Clear();
            _localUserId = null;
            _localUserIdKey = null;

            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        public bool IsFileLoaded(string filename)
        {
            return _loadedFiles.Contains(filename);
        }

        /// <summary>
        /// Updates whether a file's cloud state is considered cleanly loaded (Success or NotFound) so SaveJson knows whether it is safe to upload.
        /// </summary>
        internal void SetFileLoaded(string filename, bool isLoaded)
        {
            if (isLoaded)
            {
                _loadedFiles.Add(filename);
            }
            else
            {
                _loadedFiles.Remove(filename);
            }
        }

        public void SaveJson<T>(string filename, T data, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(filename), "[PlayerDataStorageManager] filename must not be null or empty.");
            Assert.IsTrue(data != null, "[PlayerDataStorageManager] data must not be null.");
            Assert.IsNotNull(_localUserId, "[PlayerDataStorageManager] LocalProductUserId must be set before saving.");

            byte[] payload = SerializeJson(data);
            bool savedLocally = _localStorage.Save(_localUserIdKey, filename, payload, hasPendingUpload: true);

            // If cloud load failed earlier (_isLoaded == false), keep edits encrypted locally until the next Load merges with server
            if (!_loadedFiles.Contains(filename))
            {
                onComplete?.Invoke(savedLocally, savedLocally ? null : $"Failed to save '{filename}' to local encrypted storage.");
                return;
            }

            WriteRawToCloud(filename, payload, savedLocally, onComplete);
        }

        public void LoadJson<T>(string filename, Action<bool, T, string> onComplete)
        {
            Assert.IsFalse(string.IsNullOrEmpty(filename), "[PlayerDataStorageManager] filename must not be null or empty.");
            Assert.IsNotNull(_localUserId, "[PlayerDataStorageManager] LocalProductUserId must be set before loading.");

            var continuation = new JsonLoadContinuation<T>(this, filename, _localUserIdKey, onComplete);
            ReadRawFromCloud(filename, continuation.HandleCloudReadCompleted);
        }

        public void DeleteFile(string filename, Action<bool, string> onComplete = null)
        {
            Assert.IsFalse(string.IsNullOrEmpty(filename), "[PlayerDataStorageManager] filename must not be null or empty.");
            Assert.IsNotNull(_localUserId, "[PlayerDataStorageManager] LocalProductUserId must be set before deleting.");

            _localStorage.Delete(_localUserIdKey, filename);
            _loadedFiles.Remove(filename);

            var deleteOptions = new DeleteFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename
            };

            _storageHandle.DeleteFile(deleteOptions, new CloudDeleteTransfer(filename, onComplete), s_OnDeleteFileCompleted);
        }

        #region Cloud Transport

        /// <summary>
        /// Starts an EOS ReadFile transfer for the given filename, or attaches to an already in-flight read
        /// so concurrent requests don't fail with PlayerDataStorageRequestInProgress.
        /// </summary>
        private void ReadRawFromCloud(string filename, Action<bool, byte[], string> onComplete)
        {
            if (_activeReads.TryGetValue(filename, out CloudReadTransfer activeRead))
            {
                activeRead.OnComplete += onComplete;
                return;
            }

            var transfer = new CloudReadTransfer(this, filename, onComplete);
            _activeReads[filename] = transfer;

            var readOptions = new ReadFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename,
                ReadChunkLengthBytes = DefaultChunkSizeBytes,
                ReadFileDataCallback = s_OnReadFileData
            };

            PlayerDataStorageFileTransferRequest handle = _storageHandle.ReadFile(readOptions, transfer, s_OnReadFileCompleted);
            if (transfer.IsCompleted)
            {
                handle?.Release();
            }
            else
            {
                transfer.TransferHandle = handle;
            }
        }

        /// <summary>
        /// Uploads bytes to EOS or queues the latest payload if a read/write on the same file is already in flight,
        /// preventing EOS from rejecting overlapping transfers.
        /// </summary>
        internal void WriteRawToCloud(string filename, byte[] data, bool savedLocally, Action<bool, string> onComplete)
        {
            if (_activeWrites.ContainsKey(filename) || _activeReads.ContainsKey(filename))
            {
                if (_pendingWrites.TryGetValue(filename, out PendingCloudWrite existing))
                {
                    existing.Data = data;
                    existing.SavedLocally |= savedLocally;
                    existing.OnComplete += onComplete;
                    _pendingWrites[filename] = existing;
                }
                else
                {
                    _pendingWrites[filename] = new PendingCloudWrite
                    {
                        Filename = filename,
                        UserIdKey = _localUserIdKey,
                        Data = data,
                        SavedLocally = savedLocally,
                        OnComplete = onComplete
                    };
                }
                return;
            }

            ExecuteCloudWrite(filename, _localUserIdKey, data, savedLocally, onComplete);
        }

        /// <summary>
        /// Begins the actual EOS WriteFile chunked upload and stores its native transfer handle for deterministic release.
        /// </summary>
        private void ExecuteCloudWrite(string filename, string userIdKey, byte[] data, bool savedLocally, Action<bool, string> onComplete)
        {
            var transfer = new CloudWriteTransfer(this, filename, userIdKey, data, savedLocally, onComplete);
            _activeWrites[filename] = transfer;

            var writeOptions = new WriteFileOptions
            {
                LocalUserId = _localUserId,
                Filename = filename,
                ChunkLengthBytes = DefaultChunkSizeBytes,
                WriteFileDataCallback = s_OnWriteFileData
            };

            PlayerDataStorageFileTransferRequest handle = _storageHandle.WriteFile(writeOptions, transfer, s_OnWriteFileCompleted);
            if (transfer.IsCompleted)
            {
                handle?.Release();
            }
            else
            {
                transfer.TransferHandle = handle;
            }
        }

        /// <summary>
        /// Finishes an EOS read, translates the EOS Result into callback arguments, and triggers any write queued behind it.
        /// </summary>
        private void CompleteCloudRead(string filename, Result resultCode, byte[] fileBytes, Action<bool, byte[], string> callback)
        {
            _activeReads.Remove(filename);

            if (resultCode == Result.Success)
            {
                callback?.Invoke(true, fileBytes ?? Array.Empty<byte>(), null);
            }
            else if (resultCode == Result.NotFound)
            {
                callback?.Invoke(false, null, NotFoundError);
            }
            else
            {
                callback?.Invoke(false, null, $"Failed to read cloud file '{filename}': {resultCode}");
            }

            FlushPendingWrite(filename);
        }

        /// <summary>
        /// Finishes an EOS write, clears the local pending-upload flag in-place once confirmed by the server, and flushes any newer queued write.
        /// </summary>
        private void CompleteCloudWrite(string filename, string userIdKey, bool savedLocally, Result resultCode, Action<bool, string> callback)
        {
            _activeWrites.Remove(filename);

            if (resultCode == Result.Success)
            {
                if (!_pendingWrites.ContainsKey(filename))
                {
                    _localStorage.SetPendingUpload(userIdKey, filename, false);
                }
                callback?.Invoke(true, null);
            }
            else
            {
                string error = $"Failed to write cloud file '{filename}': {resultCode}";
                Debug.LogWarning($"[PlayerDataStorageManager] {error}");
                callback?.Invoke(savedLocally, error);
            }

            FlushPendingWrite(filename);
        }

        /// <summary>
        /// Starts the latest queued write for a file if another Save arrived while the previous transfer was still running.
        /// </summary>
        private void FlushPendingWrite(string filename)
        {
            if (_pendingWrites.TryGetValue(filename, out PendingCloudWrite nextWrite))
            {
                _pendingWrites.Remove(filename);
                ExecuteCloudWrite(nextWrite.Filename, nextWrite.UserIdKey, nextWrite.Data, nextWrite.SavedLocally, nextWrite.OnComplete);
            }
        }

        /// <summary>
        /// Static EOS chunk callback that appends each downloaded chunk into the transfer's buffer without allocating a closure.
        /// </summary>
        private static ReadResult HandleReadFileDataStatic(ReadFileDataCallbackInfo data)
        {
            if (data.ClientData is CloudReadTransfer transfer && data.DataChunk != null && data.DataChunk.Length > 0)
            {
                transfer.Buffer.Write(data.DataChunk, 0, data.DataChunk.Length);
            }
            return ReadResult.ContinueReading;
        }

        /// <summary>
        /// Static EOS completion callback for ReadFile; extracts bytes, releases native/buffer resources, and routes to CompleteCloudRead.
        /// </summary>
        private static void HandleReadFileCompletedStatic(ReadFileCallbackInfo info)
        {
            if (!(info.ClientData is CloudReadTransfer transfer))
            {
                return;
            }

            byte[] bytes = info.ResultCode == Result.Success ? transfer.Buffer.ToArray() : null;
            transfer.DisposeResources();
            transfer.Owner.CompleteCloudRead(transfer.Filename, info.ResultCode, bytes, transfer.OnComplete);
        }

        /// <summary>
        /// Static EOS chunk callback that feeds payload bytes to WriteFile (zero-copy fast path when the file fits in one chunk).
        /// </summary>
        private static WriteResult HandleWriteFileDataStatic(WriteFileDataCallbackInfo cbData, out byte[] outDataBuffer)
        {
            if (!(cbData.ClientData is CloudWriteTransfer transfer))
            {
                outDataBuffer = Array.Empty<byte>();
                return WriteResult.FailRequest;
            }

            int remaining = transfer.Data.Length - transfer.BytesWritten;
            if (remaining <= 0)
            {
                outDataBuffer = Array.Empty<byte>();
                return WriteResult.CompleteRequest;
            }

            int maxChunk = (int)Math.Min((long)cbData.DataBufferLengthBytes, DefaultChunkSizeBytes);
            if (transfer.BytesWritten == 0 && remaining <= maxChunk)
            {
                outDataBuffer = transfer.Data;
                transfer.BytesWritten = remaining;
                return WriteResult.CompleteRequest;
            }

            int toWrite = Math.Min(remaining, maxChunk);
            outDataBuffer = new byte[toWrite];
            Buffer.BlockCopy(transfer.Data, transfer.BytesWritten, outDataBuffer, 0, toWrite);
            transfer.BytesWritten += toWrite;

            return transfer.BytesWritten >= transfer.Data.Length ? WriteResult.CompleteRequest : WriteResult.ContinueWriting;
        }

        /// <summary>
        /// Static EOS completion callback for WriteFile; releases the native transfer handle and routes to CompleteCloudWrite.
        /// </summary>
        private static void HandleWriteFileCompletedStatic(WriteFileCallbackInfo info)
        {
            if (!(info.ClientData is CloudWriteTransfer transfer))
            {
                return;
            }

            transfer.ReleaseHandle();
            transfer.Owner.CompleteCloudWrite(transfer.Filename, transfer.UserIdKey, transfer.SavedLocally, info.ResultCode, transfer.OnComplete);
        }

        /// <summary>
        /// Static EOS completion callback for DeleteFile; logs any server error and notifies the caller.
        /// </summary>
        private static void HandleDeleteFileCompletedStatic(DeleteFileCallbackInfo info)
        {
            if (!(info.ClientData is CloudDeleteTransfer transfer))
            {
                return;
            }

            if (info.ResultCode == Result.Success)
            {
                transfer.OnComplete?.Invoke(true, null);
            }
            else
            {
                string error = $"Failed to delete cloud file '{transfer.Filename}': {info.ResultCode}";
                Debug.LogError($"[PlayerDataStorageManager] {error}");
                transfer.OnComplete?.Invoke(false, error);
            }
        }

        #endregion

        #region Serialization Helpers

        /// <summary>
        /// Serializes a Unity-serializable object into UTF-8 JSON bytes for both cloud and local encrypted storage.
        /// </summary>
        internal static byte[] SerializeJson<T>(T data)
        {
            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(data));
        }

        /// <summary>
        /// Safely parses UTF-8 JSON bytes into T, returning false on empty, whitespace, or corrupt payloads instead of throwing.
        /// </summary>
        internal static bool TryDeserializeJson<T>(byte[] bytes, string filename, out T data)
        {
            data = default;
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }

            try
            {
                string json = Encoding.UTF8.GetString(bytes);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                data = JsonUtility.FromJson<T>(json);
                return data != null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PlayerDataStorageManager] Failed to parse JSON for file '{filename}': {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
