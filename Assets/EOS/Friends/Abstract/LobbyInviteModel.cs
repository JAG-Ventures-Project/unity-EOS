using System;
using Epic.OnlineServices;

namespace EOS.Friends
{
    public class LobbyInviteModel
    {
        public string InviteId { get; set; }
        public string LobbyId { get; set; }
        public string HostAddress { get; set; }
        public string JoinCode { get; set; }
        public string InviterDisplayName { get; set; }
        public ProductUserId InviterUserId { get; set; }
        public uint AvailableSlots { get; set; }
        public uint MaxMembers { get; set; }
        public DateTime ReceivedAt { get; set; }
        public bool IsDirectP2P => !string.IsNullOrEmpty(HostAddress) && string.IsNullOrEmpty(LobbyId);

        public bool IsFull => AvailableSlots == 0;
    }
}
