using System;
using System.Collections.Generic;
using EOS.Core;

namespace EOS.Friends
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
    public class CrossPlayFriendsData : IMergeableCloudData<CrossPlayFriendsData>
    {
        public int version = 1;
        public List<CrossPlayFriendEntry> friends = new();
        public List<CrossPlayFriendEntry> blocked = new();
        public List<CrossPlayFriendEntry> pendingIncoming = new();
        public List<CrossPlayFriendEntry> pendingOutgoing = new();

        // Friend signals not yet acknowledged by the remote player (retried until acked or expired)
        public List<FriendSignal> outbox = new();

        public void EnsureInitialized()
        {
            friends ??= new List<CrossPlayFriendEntry>();
            blocked ??= new List<CrossPlayFriendEntry>();
            pendingIncoming ??= new List<CrossPlayFriendEntry>();
            pendingOutgoing ??= new List<CrossPlayFriendEntry>();
            outbox ??= new List<FriendSignal>();
        }

        public bool IsSimilarTo(CrossPlayFriendsData other)
        {
            if (other == null || version != other.version)
            {
                return false;
            }

            EnsureInitialized();
            other.EnsureInitialized();

            return AreEntryListsSimilar(friends, other.friends) &&
                   AreEntryListsSimilar(blocked, other.blocked) &&
                   AreEntryListsSimilar(pendingIncoming, other.pendingIncoming) &&
                   AreEntryListsSimilar(pendingOutgoing, other.pendingOutgoing) &&
                   AreSignalListsSimilar(outbox, other.outbox);
        }

        public void MergeFromServer(CrossPlayFriendsData serverData)
        {
            if (serverData == null)
            {
                return;
            }

            EnsureInitialized();
            serverData.EnsureInitialized();

            version = serverData.version;

            var localFriends = new List<CrossPlayFriendEntry>(friends);
            var localBlocked = new List<CrossPlayFriendEntry>(blocked);
            var localIncoming = new List<CrossPlayFriendEntry>(pendingIncoming);
            var localOutgoing = new List<CrossPlayFriendEntry>(pendingOutgoing);
            var localOutbox = new List<FriendSignal>(outbox);

            friends.Clear();
            blocked.Clear();
            pendingIncoming.Clear();
            pendingOutgoing.Clear();
            outbox.Clear();

            // 1. Blocked players: server entries win on conflict, then append offline local blocks
            AppendUniqueEntries(blocked, serverData.blocked, null, null);
            AppendUniqueEntries(blocked, localBlocked, null, null);

            // 2. Friends: server entries win on conflict (unless unfriended locally after server timestamp), then append offline local additions
            for (int i = 0; i < serverData.friends.Count; i++)
            {
                CrossPlayFriendEntry entry = serverData.friends[i];
                if (entry == null || string.IsNullOrEmpty(entry.productUserId)) continue;
                if (ContainsPuid(friends, entry.productUserId)) continue;
                if (HasOutboxSignalSince(localOutbox, entry.productUserId, FriendSignalType.Unfriended, entry.addedTimestamp)) continue;

                friends.Add(entry);
            }

            for (int i = 0; i < localFriends.Count; i++)
            {
                CrossPlayFriendEntry entry = localFriends[i];
                if (entry == null || string.IsNullOrEmpty(entry.productUserId)) continue;
                if (ContainsPuid(friends, entry.productUserId)) continue;
                if (ContainsPuid(serverData.blocked, entry.productUserId)) continue;

                friends.Add(entry);
            }

            // 3. Pending incoming: exclude anyone already in friends or blocked, or declined locally
            for (int i = 0; i < serverData.pendingIncoming.Count; i++)
            {
                CrossPlayFriendEntry entry = serverData.pendingIncoming[i];
                if (entry == null || string.IsNullOrEmpty(entry.productUserId)) continue;
                if (ContainsPuid(pendingIncoming, entry.productUserId) ||
                    ContainsPuid(friends, entry.productUserId) ||
                    ContainsPuid(blocked, entry.productUserId))
                {
                    continue;
                }
                if (HasOutboxSignalSince(localOutbox, entry.productUserId, FriendSignalType.RequestDeclined, entry.addedTimestamp)) continue;

                pendingIncoming.Add(entry);
            }
            AppendUniqueEntries(pendingIncoming, localIncoming, friends, blocked);

            // 4. Pending outgoing: exclude anyone already in friends or blocked
            AppendUniqueEntries(pendingOutgoing, serverData.pendingOutgoing, friends, blocked);
            AppendUniqueEntries(pendingOutgoing, localOutgoing, friends, blocked);

            // 5. Outbox signals: server signals first, then unacknowledged offline local signals
            for (int i = 0; i < serverData.outbox.Count; i++)
            {
                FriendSignal signal = serverData.outbox[i];
                if (signal != null && !string.IsNullOrEmpty(signal.id) && !ContainsSignalId(outbox, signal.id))
                {
                    outbox.Add(signal);
                }
            }
            for (int i = 0; i < localOutbox.Count; i++)
            {
                FriendSignal signal = localOutbox[i];
                if (signal != null && !string.IsNullOrEmpty(signal.id) && !ContainsSignalId(outbox, signal.id))
                {
                    outbox.Add(signal);
                }
            }
        }

        private static void AppendUniqueEntries(
            List<CrossPlayFriendEntry> target,
            List<CrossPlayFriendEntry> source,
            List<CrossPlayFriendEntry> excludeA,
            List<CrossPlayFriendEntry> excludeB)
        {
            for (int i = 0; i < source.Count; i++)
            {
                CrossPlayFriendEntry entry = source[i];
                if (entry == null || string.IsNullOrEmpty(entry.productUserId)) continue;
                if (ContainsPuid(target, entry.productUserId)) continue;
                if (excludeA != null && ContainsPuid(excludeA, entry.productUserId)) continue;
                if (excludeB != null && ContainsPuid(excludeB, entry.productUserId)) continue;

                target.Add(entry);
            }
        }

        private static bool ContainsPuid(List<CrossPlayFriendEntry> list, string productUserId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].productUserId == productUserId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsSignalId(List<FriendSignal> list, string signalId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].id == signalId)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasOutboxSignalSince(List<FriendSignal> outboxList, string targetPuid, FriendSignalType type, long minTimestamp)
        {
            for (int i = 0; i < outboxList.Count; i++)
            {
                FriendSignal sig = outboxList[i];
                if (sig != null &&
                    sig.type == type &&
                    sig.targetPuid == targetPuid &&
                    sig.createdTimestamp >= minTimestamp)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AreEntryListsSimilar(List<CrossPlayFriendEntry> a, List<CrossPlayFriendEntry> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                CrossPlayFriendEntry left = a[i];
                if (left == null) return false;

                CrossPlayFriendEntry right = FindByPuid(b, left.productUserId);
                if (right == null ||
                    left.displayName != right.displayName ||
                    left.platform != right.platform ||
                    left.wasFriend != right.wasFriend)
                {
                    return false;
                }
            }
            return true;
        }

        private static CrossPlayFriendEntry FindByPuid(List<CrossPlayFriendEntry> list, string productUserId)
        {
            for (int i = 0; i < list.Count; i++)
            {
                CrossPlayFriendEntry candidate = list[i];
                if (candidate != null && candidate.productUserId == productUserId)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static bool AreSignalListsSimilar(List<FriendSignal> a, List<FriendSignal> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                FriendSignal left = a[i];
                if (left == null || !ContainsMatchingSignal(b, left))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool ContainsMatchingSignal(List<FriendSignal> list, FriendSignal target)
        {
            for (int i = 0; i < list.Count; i++)
            {
                FriendSignal candidate = list[i];
                if (candidate != null &&
                    candidate.id == target.id &&
                    candidate.type == target.type &&
                    candidate.targetPuid == target.targetPuid)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
