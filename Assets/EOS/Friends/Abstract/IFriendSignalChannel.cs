using System;

namespace EOS.Friends
{
    // Transport contract for delivering friend signals between two online players.
    // Delivery is best-effort; reliability (retry until acknowledged) is owned by the caller via the persisted outbox.
    public interface IFriendSignalChannel
    {
        bool IsActive { get; }

        /// <summary>Raised for every non-ack signal. Arguments: authenticated sender ProductUserId, signal.</summary>
        event Action<string, FriendSignal> OnSignalReceived;

        /// <summary>Raised when the remote player confirmed receipt of a signal we sent. Argument: signal id.</summary>
        event Action<string> OnSignalAcknowledged;

        void Start();
        void Stop();
        void Send(string targetProductUserId, FriendSignal signal);
        void Tick();
    }
}
