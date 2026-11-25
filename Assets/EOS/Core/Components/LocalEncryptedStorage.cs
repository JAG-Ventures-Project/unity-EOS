using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace EOS.Core
{
    // AI implementation: Authenticated local AES-256-CBC + HMAC-SHA256 cache with in-place authenticated pending-upload flag updates
    /// <summary>
    /// Encrypts and persists per-user cloud cache files on local disk using AES-256-CBC + HMAC-SHA256.
    /// Format: [Version (1B)] [Flags (1B)] [IV (16B)] [Ciphertext (N*16B)] [HMAC-SHA256 (32B)].
    /// Keeping the authenticated flag in the header lets SetPendingUpload flip it in-place without re-encrypting or recreating the file.
    /// </summary>
    public sealed class LocalEncryptedStorage
    {
        private const byte EnvelopeVersion = 2;
        private const byte PendingUploadFlag = 1;
        private const int HeaderSizeBytes = 2;
        private const int IvSizeBytes = 16;
        private const int HmacSizeBytes = 32;
        private const int MinBlockSizeBytes = 16;
        private const int MinPackageSizeBytes = HeaderSizeBytes + IvSizeBytes + MinBlockSizeBytes + HmacSizeBytes;

        private static readonly char[] s_InvalidFileNameChars = Path.GetInvalidFileNameChars();

        private readonly string _baseDirectory;
        private readonly byte[] _encKey;
        private readonly byte[] _macKey;

        public LocalEncryptedStorage(string baseDirectory, string hexEncryptionKey)
        {
            _baseDirectory = string.IsNullOrEmpty(baseDirectory)
                ? Path.Combine(Application.persistentDataPath, "eos_local_cache")
                : baseDirectory;

            byte[] masterKey = ResolveMasterKey(hexEncryptionKey);
            _encKey = DeriveSubKey(masterKey, "EOS_LOCAL_ENC_V1");
            _macKey = DeriveSubKey(masterKey, "EOS_LOCAL_MAC_V1");
        }

        public bool Save(string userId, string filename, byte[] data, bool hasPendingUpload)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            byte[] payload = data ?? Array.Empty<byte>();
            byte flags = hasPendingUpload ? PendingUploadFlag : (byte)0;

            try
            {
                byte[] encryptedPackage = EncryptAndAuthenticate(payload, flags);
                string filePath = GetFilePath(userId, filename);
                string directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string tempPath = filePath + ".tmp";
                File.WriteAllBytes(tempPath, encryptedPackage);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                File.Move(tempPath, filePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalEncryptedStorage] Failed to write local encrypted cache for '{filename}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Updates only the authenticated pending-upload flag byte and trailing HMAC tag in-place
        /// without re-encrypting the payload or deleting/recreating the file.
        /// </summary>
        public bool SetPendingUpload(string userId, string filename, bool hasPendingUpload)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string filePath = GetFilePath(userId, filename);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    if (fs.Length < MinPackageSizeBytes || fs.Length > int.MaxValue)
                    {
                        return false;
                    }

                    int length = (int)fs.Length;
                    byte[] package = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int n = fs.Read(package, read, length - read);
                        if (n <= 0)
                        {
                            return false;
                        }
                        read += n;
                    }

                    if (package[0] != EnvelopeVersion)
                    {
                        return false;
                    }

                    byte desiredFlags = hasPendingUpload ? PendingUploadFlag : (byte)0;
                    if (package[1] == desiredFlags)
                    {
                        return true;
                    }

                    int authLength = length - HmacSizeBytes;
                    if (!VerifyHmac(package, authLength))
                    {
                        Debug.LogWarning($"[LocalEncryptedStorage] Refused to update flag on tampered cache for '{filename}'.");
                        return false;
                    }

                    package[1] = desiredFlags;
                    byte[] newTag = ComputeHmac(package, authLength);

                    fs.Position = 1;
                    fs.WriteByte(desiredFlags);
                    fs.Position = authLength;
                    fs.Write(newTag, 0, HmacSizeBytes);
                    fs.Flush();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalEncryptedStorage] Failed to update pending flag for '{filename}': {ex.Message}");
                return false;
            }
        }

        public bool TryLoad(string userId, string filename, out byte[] data, out bool hasPendingUpload)
        {
            data = null;
            hasPendingUpload = false;

            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string filePath = GetFilePath(userId, filename);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                byte[] package = File.ReadAllBytes(filePath);
                    if (!VerifyAndDecrypt(package, out byte[] payload, out hasPendingUpload))
                    {
                        Debug.LogWarning($"[LocalEncryptedStorage] Local encrypted cache failed integrity check for '{filename}'.");
                        return false;
                    }

                data = payload;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalEncryptedStorage] Failed to read local encrypted cache for '{filename}': {ex.Message}");
                return false;
            }
        }

        public bool Delete(string userId, string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string filePath = GetFilePath(userId, filename);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                File.Delete(filePath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalEncryptedStorage] Failed to delete local encrypted cache for '{filename}': {ex.Message}");
                return false;
            }
        }

        public bool HasPendingUpload(string userId, string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            string filePath = GetFilePath(userId, filename);
            if (!File.Exists(filePath))
            {
                return false;
            }

            try
            {
                byte[] package = File.ReadAllBytes(filePath);
                if (package.Length < MinPackageSizeBytes || package[0] != EnvelopeVersion)
                {
                    return false;
                }

                int authLength = package.Length - HmacSizeBytes;
                return VerifyHmac(package, authLength) && (package[1] & PendingUploadFlag) != 0;
            }
            catch
            {
                return false;
            }
        }

        public string GetFilePath(string userId, string filename)
        {
            string safeUser = SanitizeSegment(string.IsNullOrEmpty(userId) ? "_anonymous" : userId);
            string safeFile = SanitizeSegment(filename) + ".enc";
            return Path.Combine(_baseDirectory, safeUser, safeFile);
        }

        private byte[] EncryptAndAuthenticate(byte[] payload, byte flags)
        {
            byte[] iv = new byte[IvSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(iv);
            }

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encKey;
                aes.IV = iv;

                using (var encryptor = aes.CreateEncryptor())
                {
                    ciphertext = encryptor.TransformFinalBlock(payload, 0, payload.Length);
                }
            }

            int authLength = HeaderSizeBytes + IvSizeBytes + ciphertext.Length;
            byte[] package = new byte[authLength + HmacSizeBytes];
            package[0] = EnvelopeVersion;
            package[1] = flags;
            Buffer.BlockCopy(iv, 0, package, HeaderSizeBytes, IvSizeBytes);
            Buffer.BlockCopy(ciphertext, 0, package, HeaderSizeBytes + IvSizeBytes, ciphertext.Length);

            byte[] tag = ComputeHmac(package, authLength);
            Buffer.BlockCopy(tag, 0, package, authLength, HmacSizeBytes);

            return package;
        }

        private bool VerifyAndDecrypt(byte[] package, out byte[] payload, out bool hasPendingUpload)
        {
                payload = null;
            hasPendingUpload = false;

            if (package == null || package.Length < MinPackageSizeBytes || package[0] != EnvelopeVersion)
            {
                return false;
            }

            int authLength = package.Length - HmacSizeBytes;
                if (!VerifyHmac(package, authLength))
                {
                return false;
            }

            hasPendingUpload = (package[1] & PendingUploadFlag) != 0;

            byte[] iv = new byte[IvSizeBytes];
            Buffer.BlockCopy(package, HeaderSizeBytes, iv, 0, IvSizeBytes);
            int cipherOffset = HeaderSizeBytes + IvSizeBytes;
            int cipherLength = authLength - cipherOffset;

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = _encKey;
                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor())
                {
                    payload = decryptor.TransformFinalBlock(package, cipherOffset, cipherLength);
                }
            }

            return true;
        }

        private byte[] ComputeHmac(byte[] buffer, int count)
        {
            using (var hmac = new HMACSHA256(_macKey))
            {
                return hmac.ComputeHash(buffer, 0, count);
            }
        }

        private bool VerifyHmac(byte[] package, int authLength)
        {
            byte[] expectedTag = ComputeHmac(package, authLength);
            return ConstantTimeEquals(expectedTag, package, authLength, HmacSizeBytes);
        }

        private static bool ConstantTimeEquals(byte[] a, byte[] b, int bOffset, int length)
        {
            if (a == null || b == null || a.Length != length || b.Length < bOffset + length)
            {
                return false;
            }

            int diff = 0;
            for (int i = 0; i < length; i++)
            {
                diff |= a[i] ^ b[bOffset + i];
            }
            return diff == 0;
        }

        private static byte[] ResolveMasterKey(string hexKey)
        {
            if (!string.IsNullOrEmpty(hexKey) && hexKey.Length == 64 && TryParseHex(hexKey, out byte[] parsed))
            {
                return parsed;
            }

            if (!string.IsNullOrEmpty(hexKey))
            {
                Debug.LogWarning("[LocalEncryptedStorage] Encryption key is not a valid 64-character hex string; deriving SHA-256 key.");
            }

            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(hexKey ?? "EOS_DEFAULT_LOCAL_FALLBACK_KEY"));
            }
        }

        private static byte[] DeriveSubKey(byte[] masterKey, string domain)
        {
            byte[] domainBytes = Encoding.UTF8.GetBytes(domain);
            byte[] input = new byte[domainBytes.Length + masterKey.Length];
            Buffer.BlockCopy(domainBytes, 0, input, 0, domainBytes.Length);
            Buffer.BlockCopy(masterKey, 0, input, domainBytes.Length, masterKey.Length);

            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(input);
            }
        }

        private static bool TryParseHex(string hex, out byte[] bytes)
        {
            bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                int hi = ParseHexNibble(hex[i * 2]);
                int lo = ParseHexNibble(hex[i * 2 + 1]);
                if (hi < 0 || lo < 0)
                {
                    bytes = null;
                    return false;
                }
                bytes[i] = (byte)((hi << 4) | lo);
            }
            return true;
        }

        private static int ParseHexNibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        public static bool ByteArraysEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static string SanitizeSegment(string value)
        {
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool isInvalid = c == '/' || c == '\\' || c == ':' || Array.IndexOf(s_InvalidFileNameChars, c) >= 0;
                sb.Append(isInvalid ? '_' : c);
            }

            string sanitized = sb.ToString();
            if (sanitized == "." || sanitized == "..")
            {
                return "_" + sanitized;
            }
            return sanitized;
        }
    }
}
