namespace EOS
{
    // AI implementation: Relationship status for friends across platform and cross-play layers
    public enum FriendRelationStatus
    {
        Platform,        // Native platform friend (Steam, Epic, PSN, etc.)
        CrossPlay,       // Custom cross-platform friend saved in EOS cloud storage
        PendingIncoming, // Incoming friend request
        PendingOutgoing, // Outgoing friend request
        Blocked          // Blocked user
    }

    // AI implementation: In-game presence state of a friend
    public enum FriendPresenceState
    {
        Offline = 0,
        Online = 1,
        InLobby = 2,
        InMatch = 3
    }
}

