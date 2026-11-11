namespace EOS
{
    public readonly struct PlayerLookupResult
    {
        public string Query { get; }
        public bool Found { get; }
        public string Error { get; }
        public FriendModel Player { get; }

        private PlayerLookupResult(string query, bool found, string error, FriendModel player)
        {
            Query = query;
            Found = found;
            Error = error;
            Player = player;
        }

        public static PlayerLookupResult Success(string query, FriendModel player)
        {
            return new PlayerLookupResult(query, true, null, player);
        }

        public static PlayerLookupResult Failure(string query, string error)
        {
            return new PlayerLookupResult(query, false, error, null);
        }
    }
}

