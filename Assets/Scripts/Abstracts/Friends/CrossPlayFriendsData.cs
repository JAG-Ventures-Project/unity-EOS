using System;
using System.Collections.Generic;

namespace EOS
{
    [Serializable]
    public class CrossPlayFriendEntry
    {
        public string productUserId;
        public string displayName;
        public int platform; // LoginProvider enum value
        public long addedTimestamp;
        public bool wasFriend;
    }

    [Serializable]
    public class CrossPlayFriendsData
    {
        public int version = 1;
        public List<CrossPlayFriendEntry> friends = new();
        public List<CrossPlayFriendEntry> blocked = new();
        public List<CrossPlayFriendEntry> pendingIncoming = new();
        public List<CrossPlayFriendEntry> pendingOutgoing = new();

        // Friend signals not yet acknowledged by the remote player (retried until acked or expired)
        public List<FriendSignal> outbox = new();
    }
}

