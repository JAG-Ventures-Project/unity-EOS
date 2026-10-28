using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.Platform;
using Epic.OnlineServices.PlayerDataStorage;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS
{
    ///https://dev.epicgames.com/docs/epic-online-services/player-and-game-data/player-data-storage-interface
    public class PlayerDataStorageManager : IPlayerDataStorage
    {
        private const uint DefaultChunkSizeBytes = 4096;

        private readonly PlatformInterface _platformHandle;
        private readonly PlayerDataStorageInterface _storageHandle;

        private readonly HashSet<string> _activeWrites = new HashSet<string>();
        private readonly Dictionary<string, byte[]> _pendingWrites = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, Action<bool, string>> _pendingCallbacks = new Dictionary<string, Action<bool, string>>();

        public PlayerDataStorageManager(PlatformInterface platformInterface)
        {
            _platformHandle = platformInterface;
            Assert.IsNotNull(_platformHandle, "[PlayerDataStorageManager] PlatformInterface must not be null.");

            _storageHandle = _platformHandle.GetPlayerDataStorageInterface();
            Assert.IsNotNull(_storageHandle, "[PlayerDataStorageManager] PlayerDataStorageInterface failed to initialize.");
        }

        private ProductUserId GetLocalUserId()
        {
            return EOSSDKManager.Instance?.Service?.GetLocalProductUserId();
        }

        public void SaveFile(string filename, byte[] data, Action<bool, string> onComplete = null)
        {
            // If an upload is already in flight for this file, queue the latest data.
            // It will automatically execute once the current upload completes.
            if (_activeWrites.Contains(filename))
            {
                _pendingWrites[filename] = data;
                _pendingCallbacks[filename] = onComplete;
                return;
            }

            ExecuteWriteFile(filename, data, onComplete);
        }

        private void ExecuteWriteFile(string filename, byte[] data, Action<bool, string> onComplete)
        {
            ProductUserId localUserId = GetLocalUserId();
            _activeWrites.Add(filename);

            int bytesWritten = 0;
            uint chunkSize = DefaultChunkSizeBytes;

            var writeOptions = new WriteFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename,
                ChunkLengthBytes = chunkSize,
                WriteFileDataCallback = (WriteFileDataCallbackInfo cbData, out byte[] outDataBuffer) =>
                {
                    // AI Implemntation
                    int remaining = data.Length - bytesWritten;
                    if (remaining <= 0)
                    {
                        outDataBuffer = new byte[0];
                        return WriteResult.CompleteRequest;
                    }

                    int toWrite = (int)Math.Min((long)remaining, Math.Min((long)cbData.DataBufferLengthBytes, (long)chunkSize));
                    outDataBuffer = new byte[toWrite];
                    Buffer.BlockCopy(data, bytesWritten, outDataBuffer, 0, toWrite);
                    bytesWritten += toWrite;

                    return bytesWritten >= data.Length ? WriteResult.CompleteRequest : WriteResult.ContinueWriting;
                }
            };

            _storageHandle.WriteFile(writeOptions, null, (WriteFileCallbackInfo info) =>
            {
                _activeWrites.Remove(filename);

                if (info.ResultCode == Result.Success)
                {
                    onComplete?.Invoke(true, null);
                }
                else
                {
                    string error = $"Failed to write cloud file '{filename}': {info.ResultCode}";
                    Debug.LogError($"[PlayerDataStorageManager] {error}");
                    onComplete?.Invoke(false, error);
                }

                // If newer changes were queued while this upload was in flight, upload the latest state now
                if (_pendingWrites.TryGetValue(filename, out byte[] nextData))
                {
                    _pendingWrites.Remove(filename);
                    _pendingCallbacks.TryGetValue(filename, out Action<bool, string> nextCb);
                    _pendingCallbacks.Remove(filename);

                    ExecuteWriteFile(filename, nextData, nextCb);
                }
            });
        }

        public void SaveJson<T>(string filename, T data, Action<bool, string> onComplete = null)
        {
            string json = JsonUtility.ToJson(data);
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            SaveFile(filename, bytes, onComplete);
        }

        public void LoadFile(string filename, Action<bool, byte[], string> onComplete)
        {
            ProductUserId localUserId = GetLocalUserId();

            var memoryStream = new MemoryStream();

            var readOptions = new ReadFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename,
                ReadChunkLengthBytes = DefaultChunkSizeBytes,
                ReadFileDataCallback = (ReadFileDataCallbackInfo data) =>
                {
                    if (data.DataChunk != null && data.DataChunk.Length > 0)
                    {
                        memoryStream.Write(data.DataChunk, 0, data.DataChunk.Length);
                    }
                    return ReadResult.ContinueReading;
                }
            };

            _storageHandle.ReadFile(readOptions, null, (ReadFileCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    byte[] fileBytes = memoryStream.ToArray();
                    memoryStream.Dispose();
                    onComplete(true, fileBytes, null);
                }
                else if (info.ResultCode == Result.NotFound)
                {
                    memoryStream.Dispose();
                    onComplete(false, null, "NotFound");
                }
                else
                {
                    memoryStream.Dispose();
                    string error = $"Failed to read cloud file '{filename}': {info.ResultCode}";
                    Debug.LogError($"[PlayerDataStorageManager] {error}");
                    onComplete(false, null, error);
                }
            });
        }

        public void LoadJson<T>(string filename, Action<bool, T, string> onComplete)
        {
            LoadFile(filename, (success, bytes, error) =>
            {
                if (success && bytes != null && bytes.Length > 0)
                {
                    T data = default;
                    bool parsedSuccessfully = false;
                    string parseError = null;

                    try
                    {
                        string json = Encoding.UTF8.GetString(bytes);
                        data = JsonUtility.FromJson<T>(json);
                        parsedSuccessfully = true;
                    }
                    catch (Exception ex)
                    {
                        parseError = ex.Message;
                        Debug.LogError($"[PlayerDataStorageManager] Failed to parse JSON for file '{filename}': {ex.Message}");
                    }

                    if (parsedSuccessfully)
                    {
                        onComplete?.Invoke(true, data, null);
                    }
                    else
                    {
                        onComplete?.Invoke(false, default, parseError);
                    }
                }
                else
                {
                    onComplete?.Invoke(false, default, error);
                }
            });
        }

        public void DeleteFile(string filename, Action<bool, string> onComplete = null)
        {
            ProductUserId localUserId = GetLocalUserId();

            var deleteOptions = new DeleteFileOptions
            {
                LocalUserId = localUserId,
                Filename = filename
            };

            _storageHandle.DeleteFile(deleteOptions, null, (DeleteFileCallbackInfo info) =>
            {
                if (info.ResultCode == Result.Success)
                {
                    onComplete?.Invoke(true, null);
                }
                else
                {
                    string error = $"Failed to delete cloud file '{filename}': {info.ResultCode}";
                    Debug.LogError($"[PlayerDataStorageManager] {error}");
                    onComplete?.Invoke(false, error);
                }
            });
        }
    }
}
