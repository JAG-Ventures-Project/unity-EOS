namespace EOS.Friends
{
    public enum FriendRelationStatus
    {
        Platform,        // Native platform friend (Steam, Epic, PSN, etc.)
        CrossPlay,       // Custom cross-platform friend saved in EOS cloud storage
        PendingIncoming, // Incoming friend request
        PendingOutgoing, // Outgoing friend request
        Blocked,         // Blocked user
        None             // No relationship (search result for a stranger). Appended last to keep existing values stable.
    }

    public enum FriendPresenceState
    {
        Offline = 0,
        Online = 1,
        InLobby = 2,
        InMatch = 3
    }
}
