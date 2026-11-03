using UnityEngine;

namespace EOS
{
    // AI implementation: Unified friend presentation model used across all platforms and UI
    public class FriendModel
    {
        public string ProductUserId { get; set; }
        public string PlatformUserId { get; set; }
        public string DisplayName { get; set; }
        public LoginProvider Platform { get; set; }
        public FriendRelationStatus Status { get; set; }
        public FriendPresenceState Presence { get; set; }
        public Texture2D AvatarTexture { get; set; }

        public bool CanInvite => Presence != FriendPresenceState.Offline && Status != FriendRelationStatus.Blocked;
    }
}

