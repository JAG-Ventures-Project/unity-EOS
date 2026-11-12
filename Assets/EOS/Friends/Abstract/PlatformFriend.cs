using UnityEngine;
using EOS.Auth;

namespace EOS.Friends
{
    public class PlatformFriend
    {
        public string ProductUserId { get; set; }
        public string PlatformUserId { get; set; }
        public string DisplayName { get; set; }
        public LoginProvider Platform { get; set; }
        public FriendPresenceState Presence { get; set; }
        public Texture2D AvatarTexture { get; set; }
    }
}
