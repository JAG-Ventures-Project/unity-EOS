using UnityEngine;
using EOS.Auth;

namespace EOS.Friends
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
        public bool IsJoinRequest { get; set; }
        public string LobbyId { get; set; }
        public string HostAddress { get; set; }

        public bool CanInvite => Presence != FriendPresenceState.Offline && Status != FriendRelationStatus.Blocked;

        /// <summary>
        /// The key this friend is stored/blocked under. Native platform friends are keyed by their platform id
        /// (that is what the blocklist filter checks); every other relationship is keyed by ProductUserId.
        /// </summary>
        public string IdentityKey => Status == FriendRelationStatus.Platform ? PlatformUserId : ProductUserId;
    }
}
