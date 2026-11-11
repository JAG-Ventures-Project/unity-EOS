using UnityEngine;

namespace EOS
{
    // Read-only description of the signed-in local player (shared by friends logic and UI)
    public interface ILocalPlayerIdentity
    {
        bool IsLoggedIn { get; }

        /// <summary>The shareable user code (local ProductUserId) other players use to add this player.</summary>
        string UserCode { get; }

        string DisplayName { get; }

        /// <summary>The platform used for the CURRENT session (not the persisted "remember me" preference).</summary>
        LoginProvider Platform { get; }

        /// <summary>Platform avatar of the local player (Steam). Null when the platform exposes none.</summary>
        Texture2D AvatarTexture { get; }
    }
}

