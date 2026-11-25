using System;
using System.IO;
using Epic.OnlineServices.PlayerDataStorage;
using UnityEngine;

namespace EOS.Core
{
    // Internal transfer state objects and JSON load continuation supporting zero-closure EOS SDK callbacks

    internal struct PendingCloudWrite
    {
        public string Filename;
        public string UserIdKey;
        public byte[] Data;
        public bool SavedLocally;
        public Action<bool, string> OnComplete;
    }

    internal sealed class CloudReadTransfer
    {
        public readonly PlayerDataStorageManager Owner;
        public readonly string Filename;
        public readonly MemoryStream Buffer = new MemoryStream();
        public Action<bool, byte[], string> OnComplete;
        public PlayerDataStorageFileTransferRequest TransferHandle;
        public bool IsCompleted;

        public CloudReadTransfer(PlayerDataStorageManager owner, string filename, Action<bool, byte[], string> onComplete)
        {
            Owner = owner;
            Filename = filename;
            OnComplete = onComplete;
        }

        /// <summary>
        /// Releases the native EOS transfer handle and disposes the chunk buffer once a read finishes or shuts down.
        /// </summary>
        public void DisposeResources()
        {
            IsCompleted = true;
            TransferHandle?.Release();
            TransferHandle = null;
            Buffer.Dispose();
        }
    }

    internal sealed class CloudWriteTransfer
    {
        public readonly PlayerDataStorageManager Owner;
        public readonly string Filename;
        public readonly string UserIdKey;
        public readonly byte[] Data;
        public readonly bool SavedLocally;
        public readonly Action<bool, string> OnComplete;
        public int BytesWritten;
        public PlayerDataStorageFileTransferRequest TransferHandle;
        public bool IsCompleted;

        public CloudWriteTransfer(PlayerDataStorageManager owner, string filename, string userIdKey, byte[] data, bool savedLocally, Action<bool, string> onComplete)
        {
            Owner = owner;
            Filename = filename;
            UserIdKey = userIdKey;
            Data = data;
            SavedLocally = savedLocally;
            OnComplete = onComplete;
        }

        /// <summary>
        /// Releases the native EOS WriteFile transfer handle so unmanaged SDK resources never leak.
        /// </summary>
        public void ReleaseHandle()
        {
            IsCompleted = true;
            TransferHandle?.Release();
            TransferHandle = null;
        }
    }

    internal sealed class CloudDeleteTransfer
    {
        public readonly string Filename;
        public readonly Action<bool, string> OnComplete;

        public CloudDeleteTransfer(string filename, Action<bool, string> onComplete)
        {
            Filename = filename;
            OnComplete = onComplete;
        }
    }

    internal sealed class JsonLoadContinuation<T>
    {
        private readonly PlayerDataStorageManager _owner;
        private readonly string _filename;
        private readonly string _userIdKey;
        private readonly Action<bool, T, string> _onComplete;

        public JsonLoadContinuation(PlayerDataStorageManager owner, string filename, string userIdKey, Action<bool, T, string> onComplete)
        {
            _owner = owner;
            _filename = filename;
            _userIdKey = userIdKey;
            _onComplete = onComplete;
        }

        /// <summary>
        /// AI Implementation
        /// Executes the UML LoadJson flow: validates JSON, compares against local backup, merges unsaved local edits with server priority, or falls back to local on error.
        /// </summary>
        public void HandleCloudReadCompleted(bool cloudSuccess, byte[] cloudBytes, string cloudError)
        {
            if (cloudSuccess)
            {
                if (!PlayerDataStorageManager.TryDeserializeJson(cloudBytes, _filename, out T serverData))
                {
                    _owner.SetFileLoaded(_filename, false);
                    FallbackToLocal($"Cloud file '{_filename}' contained empty or invalid JSON.");
                    return;
                }

                _owner.SetFileLoaded(_filename, true);

                if (!_owner.LocalStorage.TryLoad(_userIdKey, _filename, out byte[] localBytes, out bool hasPendingUpload))
                {
                    _owner.LocalStorage.Save(_userIdKey, _filename, cloudBytes, hasPendingUpload: false);
                    _onComplete?.Invoke(true, serverData, null);
                    return;
                }

                if (LocalEncryptedStorage.ByteArraysEqual(cloudBytes, localBytes))
                {
                    if (hasPendingUpload)
                    {
                        _owner.LocalStorage.SetPendingUpload(_userIdKey, _filename, false);
                    }
                    _onComplete?.Invoke(true, serverData, null);
                    return;
                }

                if (!hasPendingUpload || !PlayerDataStorageManager.TryDeserializeJson(localBytes, _filename, out T localData))
                {
                    _owner.LocalStorage.Save(_userIdKey, _filename, cloudBytes, hasPendingUpload: false);
                    _onComplete?.Invoke(true, serverData, null);
                    return;
                }

                if (localData is IMergeableCloudData<T> mergeable)
                {
                    if (mergeable.IsSimilarTo(serverData))
                    {
                        _owner.LocalStorage.Save(_userIdKey, _filename, cloudBytes, hasPendingUpload: false);
                        _onComplete?.Invoke(true, serverData, null);
                        return;
                    }

                    mergeable.MergeFromServer(serverData);
                    byte[] mergedBytes = PlayerDataStorageManager.SerializeJson(localData);

                    if (LocalEncryptedStorage.ByteArraysEqual(mergedBytes, cloudBytes))
                    {
                        _owner.LocalStorage.Save(_userIdKey, _filename, cloudBytes, hasPendingUpload: false);
                    }
                    else
                    {
                        _owner.LocalStorage.Save(_userIdKey, _filename, mergedBytes, hasPendingUpload: true);
                        _owner.WriteRawToCloud(_filename, mergedBytes, true, null);
                    }

                    _onComplete?.Invoke(true, localData, null);
                    return;
                }

                // Conflicting scalar fields on non-mergeable models resolve in favor of server data
                _owner.LocalStorage.Save(_userIdKey, _filename, cloudBytes, hasPendingUpload: false);
                _onComplete?.Invoke(true, serverData, null);
                return;
            }

            if (cloudError == PlayerDataStorageManager.NotFoundError)
            {
                _owner.SetFileLoaded(_filename, true);

                if (_owner.LocalStorage.TryLoad(_userIdKey, _filename, out byte[] localBytes, out bool hasPendingUpload))
                {
                    if (hasPendingUpload && PlayerDataStorageManager.TryDeserializeJson(localBytes, _filename, out T localData))
                    {
                        _owner.WriteRawToCloud(_filename, localBytes, true, null);
                        _onComplete?.Invoke(true, localData, null);
                        return;
                    }

                    _owner.LocalStorage.Delete(_userIdKey, _filename);
                }

                _onComplete?.Invoke(false, default, PlayerDataStorageManager.NotFoundError);
                return;
            }

            _owner.SetFileLoaded(_filename, false);
            FallbackToLocal(cloudError);
        }

        /// <summary>
        /// Decrypts and parses the local encrypted backup when cloud download or JSON parsing fails, so the player keeps seeing last-known data.
        /// </summary>
        private void FallbackToLocal(string error)
        {
            if (_owner.LocalStorage.TryLoad(_userIdKey, _filename, out byte[] localBytes, out _) &&
                PlayerDataStorageManager.TryDeserializeJson(localBytes, _filename, out T localData))
            {
                Debug.LogWarning($"[PlayerDataStorageManager] {error} Loaded local encrypted backup for '{_filename}'.");
                _onComplete?.Invoke(true, localData, error);
            }
            else
            {
                Debug.LogError($"[PlayerDataStorageManager] {error}");
                _onComplete?.Invoke(false, default, error);
            }
        }
    }
}
