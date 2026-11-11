using Epic.OnlineServices;

namespace EOS
{
    public class LobbyInviteCallbackData
    {
        public string InviteId { get; set; }
        public string LobbyId { get; set; }
        public ProductUserId InviterUserId { get; set; }
        public string InviterDisplayName { get; set; }
        public string JoinCode { get; set; }
        public string HostAddress { get; set; }
        public uint AvailableSlots { get; set; }
        public uint MaxMembers { get; set; }
    }
}

