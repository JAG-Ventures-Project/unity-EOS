using UnityEngine;

namespace EOS
{
    // AI implementation: Raw friend data retrieved from a platform-specific provider (Steam, Epic, PSN, etc.)
    public class PlatformFriend
    {
        public string PlatformUserId { get; set; }
        public string DisplayName { get; set; }
        public LoginProvider Platform { get; set; }
        public FriendPresenceState Presence { get; set; }
        public Texture2D AvatarTexture { get; set; }
    }
}

