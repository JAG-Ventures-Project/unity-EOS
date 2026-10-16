using UnityEngine;

namespace EOS
{
    public enum LoginProvider
    {
        None = 0,
        Guest = 1,
        Epic = 2,
        Steam = 3
    }

    public static class AuthPreferences
    {
        private const string KeyLastProvider = "EOS_LastLoginProvider";
        private const string KeyRememberEpic = "EOS_RememberEpicAccount";
        private const string KeyLastGuestName = "EOS_LastGuestName";

        public static LoginProvider LastLoginProvider
        {
            get => (LoginProvider)PlayerPrefs.GetInt(KeyLastProvider, (int)LoginProvider.None);
            set
            {
                PlayerPrefs.SetInt(KeyLastProvider, (int)value);
                PlayerPrefs.Save();
            }
        }

        public static bool RememberEpicAccount
        {
            get => PlayerPrefs.GetInt(KeyRememberEpic, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(KeyRememberEpic, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        public static string LastGuestName
        {
            get => PlayerPrefs.GetString(KeyLastGuestName, string.Empty);
            set
            {
                PlayerPrefs.SetString(KeyLastGuestName, value);
                PlayerPrefs.Save();
            }
        }

        /// <summary>
        /// Clears all auto-login flags when the user explicitly logs out.
        /// </summary>
        public static void ClearSession()
        {
            PlayerPrefs.DeleteKey(KeyLastProvider);
            PlayerPrefs.DeleteKey(KeyRememberEpic);
            PlayerPrefs.Save();
        }
    }
}
