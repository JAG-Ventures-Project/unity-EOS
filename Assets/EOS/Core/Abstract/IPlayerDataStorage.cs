using System;

namespace EOS.Core
{
    public interface IPlayerDataStorage
    {
        bool IsFileLoaded(string filename);
        void SaveJson<T>(string filename, T data, Action<bool, string> onComplete = null);
        void LoadJson<T>(string filename, Action<bool, T, string> onComplete);
        void DeleteFile(string filename, Action<bool, string> onComplete = null);
    }
}
