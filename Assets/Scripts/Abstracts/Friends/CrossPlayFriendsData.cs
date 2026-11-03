using System;
using System.Collections.Generic;

namespace EOS
{
    // AI implementation: Individual serialized friend entry saved in EOS Player Data Storage
    [Serializable]
    public class CrossPlayFriendEntry
    {
        public string productUserId;
        public string displayName;
        public int platform; // LoginProvider enum value
        public long addedTimestamp;
    }

    // AI implementation: Root JSON schema for cloud-persisted friends and blocked users (friends_data.json)
    [Serializable]
    public class CrossPlayFriendsData
    {
        public int version = 1;
        public List<CrossPlayFriendEntry> friends = new();
        public List<CrossPlayFriendEntry> blocked = new();
        public List<CrossPlayFriendEntry> pendingIncoming = new();
        public List<CrossPlayFriendEntry> pendingOutgoing = new();
    }
}

