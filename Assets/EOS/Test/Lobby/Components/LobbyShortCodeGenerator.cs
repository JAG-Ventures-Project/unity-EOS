using System;
using System.Text;
using UnityEngine;
using EOS.Lobby;

namespace EOS.Test.Lobby
{
    /// <summary>
    /// Tester component that provides 5-character short room code generation for EOS Lobbies.
    /// This is a test utility and can be safely removed without breaking the core EOS Lobby system.
    /// When removed, LobbyManager will default to using the native EOS Lobby ID.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyShortCodeGenerator : MonoBehaviour
    {
        [SerializeField] private bool useShortCode = true;
        [SerializeField] private int codeLength = 5;
        private const string CodeCharacters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        private void Awake()
        {
            ApplySetting();
        }

        private void OnValidate()
        {
            if (Application.isPlaying)
            {
                ApplySetting();
            }
        }

        private void ApplySetting()
        {
            if (useShortCode)
            {
                LobbyManager.CustomJoinCodeGenerator = GenerateShortCode;
            }
            else if (LobbyManager.CustomJoinCodeGenerator == GenerateShortCode ||
                     LobbyManager.CustomJoinCodeGenerator == GenerateDefaultShortCode)
            {
                LobbyManager.CustomJoinCodeGenerator = null;
            }

            if (LobbyManager.Instance != null)
            {
                LobbyManager.Instance.UseShortJoinCode = useShortCode;
            }
        }

        private void OnDestroy()
        {
            if (LobbyManager.CustomJoinCodeGenerator == GenerateShortCode)
            {
                LobbyManager.CustomJoinCodeGenerator = null;
            }
        }

        public string GenerateShortCode()
        {
            return GenerateCode(codeLength);
        }

        public static string GenerateCode(int length = 5)
        {
            var builder = new StringBuilder(length);
            var random = new System.Random();
            for (int i = 0; i < length; i++)
            {
                builder.Append(CodeCharacters[random.Next(CodeCharacters.Length)]);
            }
            return builder.ToString();
        }

        private static string GenerateDefaultShortCode() => GenerateCode(5);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegisterTesterCodeGenerator()
        {
            LobbyManager.CustomJoinCodeGenerator = GenerateDefaultShortCode;
        }
    }
}