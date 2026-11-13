using System;
using EOS.Auth;
using EOS.Core;
using System.Text;
using Epic.OnlineServices;
using Epic.OnlineServices.P2P;
using UnityEngine;
using UnityEngine.Assertions;

namespace EOS.Friends
{
    // peer-to-peer transport for friend signals (requests, accepts, declines, unfriending).
    // Operates over a dedicated EOS P2P socket and channel so it never conflicts with gameplay Mirror network traffic.
    public sealed class EOSFriendSignalChannel : IFriendSignalChannel
    {
        private const string SocketName = "EOSFriendSignal";
        private const byte SignalChannel = 250;
        private const int MaxPacketsPerTick = 16;

        private readonly SocketId _socketId = new() { SocketName = SocketName };
        private ulong _incomingNotificationId;
        private bool _isActive;
        private P2PInterface _p2p;
        private ProductUserId _localPuid;
        private ReceivePacketOptions _receiveOptions;

        public bool IsActive => _isActive;

        public event Action<string, FriendSignal> OnSignalReceived;
        public event Action<string> OnSignalAcknowledged;

        public void Start()
        {
            if (_isActive) return;

            _p2p = EOSSDKManager.PlatformInterface?.GetP2PInterface();
            _localPuid = ConnectManager.Instance?.LocalProductUserId;
            try
            {
                var relayOptions = new SetRelayControlOptions { RelayControl = RelayControl.AllowRelays };
                _p2p.SetRelayControl(relayOptions);

                var options = new AddNotifyPeerConnectionRequestOptions
                {
                    LocalUserId = _localPuid,
                    SocketId = _socketId
                };
                _incomingNotificationId = _p2p.AddNotifyPeerConnectionRequest(options, this, OnIncomingPeerConnection);
                _isActive = _incomingNotificationId != 0;

                if (_isActive)
                {
                    _receiveOptions = new ReceivePacketOptions
                    {
                        LocalUserId = _localPuid,
                        MaxDataSizeBytes = P2PInterface.MaxPacketSize,
                        RequestedChannel = SignalChannel
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSFriendSignalChannel] Failed to register connection notify: {ex.Message}");
                _isActive = false;
            }
        }

        public void Stop()
        {
            if (!_isActive) return;

            if (_incomingNotificationId != 0)
            {
                try
                {
                    _p2p.RemoveNotifyPeerConnectionRequest(_incomingNotificationId);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[EOSFriendSignalChannel] Error unregistering notify: {ex.Message}");
                }
            }

            _incomingNotificationId = 0;
            _isActive = false;
            _receiveOptions = null;
            _p2p = null;
            _localPuid = null;
        }

        // AI Implementation Sending Signals
        public void Send(string targetProductUserId, FriendSignal signal)
        {
            Assert.IsFalse(string.IsNullOrEmpty(targetProductUserId), "[EOSFriendSignalChannel] Target PUID must not be empty.");
            Assert.IsNotNull(signal, "[EOSFriendSignalChannel] Signal must not be null.");

            if (!_isActive)
            {
                Start();
            }

            if (_localPuid == null || _p2p == null)
            {
                Debug.LogWarning("[EOSFriendSignalChannel] Cannot send signal: EOS P2P not ready.");
                return;
            }

            ProductUserId targetPuid = ProductUserId.FromString(targetProductUserId);
            if (targetPuid == null || !targetPuid.IsValid())
            {
                Debug.LogWarning($"[EOSFriendSignalChannel] Cannot send signal: Invalid target PUID '{targetProductUserId}'.");
                return;
            }

            string json = JsonUtility.ToJson(signal);
            byte[] bytes = Encoding.UTF8.GetBytes(json);

            try
            {
                var options = new SendPacketOptions
                {
                    LocalUserId = _localPuid,
                    RemoteUserId = targetPuid,
                    SocketId = _socketId,
                    Channel = SignalChannel,
                    Reliability = PacketReliability.ReliableOrdered,
                    AllowDelayedDelivery = true,
                    Data = bytes
                };

                Result result = _p2p.SendPacket(options);
                if (result != Result.Success)
                {
                    Debug.LogWarning($"[EOSFriendSignalChannel] SendPacket failed: {result}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EOSFriendSignalChannel] Exception in Send: {ex.Message}");
            }
        }

        public void Tick()
        {
            int processed = 0;
            while (processed < MaxPacketsPerTick)
            {
                Result result;
                ProductUserId remoteUserId;
                byte[] outData;

                try
                {
                    result = _p2p.ReceivePacket(_receiveOptions, out remoteUserId, out SocketId remoteSocketId, out byte outChannel, out outData);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[EOSFriendSignalChannel] Exception in ReceivePacket: {ex.Message}");
                    break;
                }

                if (result != Result.Success || outData == null || outData.Length == 0)
                {
                    break;
                }

                processed++;
                ProcessPacket(remoteUserId, outData);
            }
        }

        // AI Implementation for processing incoming packets and responding with acknowledgment
        private void ProcessPacket(ProductUserId remoteUserId, byte[] data)
        {
            try
            {
                string json = Encoding.UTF8.GetString(data);
                FriendSignal signal = JsonUtility.FromJson<FriendSignal>(json);
                if (signal == null) return;

                string senderPuid = remoteUserId.ToString();

                if (signal.type == FriendSignalType.Ack)
                {
                    OnSignalAcknowledged?.Invoke(signal.id);
                }
                else
                {
                    OnSignalReceived?.Invoke(senderPuid, signal);
                    SendAck(senderPuid, signal.id);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EOSFriendSignalChannel] Failed to process packet: {ex.Message}");
            }
        }

        private void SendAck(string targetProductUserId, string originalSignalId)
        {
            var ack = new FriendSignal
            {
                id = originalSignalId,
                type = FriendSignalType.Ack,
                senderName = string.Empty,
                senderPlatform = 0,
                targetPuid = targetProductUserId,
                createdTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            Send(targetProductUserId, ack);
        }

        private static void OnIncomingPeerConnection(OnIncomingConnectionRequestInfo info)
        {
            if (info.ClientData is not EOSFriendSignalChannel channel || !channel._isActive) return;
            if (info.SocketId == null || info.SocketId.SocketName != SocketName) return;
            if (channel._p2p == null || channel._localPuid == null) return;

            try
            {
                var options = new AcceptConnectionOptions
                {
                    LocalUserId = channel._localPuid,
                    RemoteUserId = info.RemoteUserId,
                    SocketId = info.SocketId
                };
                Result result = channel._p2p.AcceptConnection(options);
                if (result != Result.Success)
                {
                    Debug.LogWarning($"[EOSFriendSignalChannel] AcceptConnection result: {result}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EOSFriendSignalChannel] AcceptConnection exception: {ex.Message}");
            }
        }
    }
}
