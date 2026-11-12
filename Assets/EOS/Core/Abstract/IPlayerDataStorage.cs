using System;

namespace EOS.Core
{
    public interface IPlayerDataStorage
    {
        void SaveFile(string filename, byte[] data, Action<bool, string> onComplete = null);
        void SaveJson<T>(string filename, T data, Action<bool, string> onComplete = null);
        void LoadFile(string filename, Action<bool, byte[], string> onComplete);
        void LoadJson<T>(string filename, Action<bool, T, string> onComplete);
        void DeleteFile(string filename, Action<bool, string> onComplete = null);
    }
}