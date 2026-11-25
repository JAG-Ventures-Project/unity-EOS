namespace EOS.Friends
{
    public enum FriendsTabType
    {
        Friends = 0,
        Notifications = 1,
        AddFriend = 2,
        Blocked = 3
    }

    public enum FriendsFilterType
    {
        Platform = 0,
        CrossPlay = 1
    }

    public enum FriendActionType
    {
        None = 0,
        Join = 1,
        Invite = 2,
        Remove = 3,
        Block = 4,
        Unblock = 5,
        AddFriend = 6,
        Accept = 7,
        Decline = 8
    }
}
