using System;

namespace EOS.Friends
{
    public enum FriendSignalType
    {
        FriendRequest = 0,
        RequestAccepted = 1,
        RequestDeclined = 2,
        Unfriended = 3,
        Ack = 4,
        PresencePing = 5,
        PresencePong = 6,
        JoinRequest = 7,
        JoinRequestAccepted = 8,
        JoinRequestDeclined = 9,
        GameInvite = 10,
        GameInviteAccepted = 11,
        GameInviteDeclined = 12
    }

    [Serializable]
    public class FriendSignal
    {
        public string id;
        public FriendSignalType type;
        public string senderName;
        public int senderPlatform; // LoginProvider enum value
        public string targetPuid;
        public long createdTimestamp;
        public int presenceState; // FriendPresenceState enum value
        public string lobbyId;
        public string hostAddress;
        public string joinCode;
    }
}
