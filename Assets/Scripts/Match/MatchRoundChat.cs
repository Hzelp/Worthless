using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Worthless
{
    public enum ChatChannel { Public = 0, Spectator = 1, Nearby = 2, Room = 3 }

    public sealed partial class MatchRoundDirector
    {
        public const int ChatMaxLength = 128;
        private const double ChatMinInterval = 0.75;
        private readonly Dictionary<int, double> chatLastAt = new Dictionary<int, double>();
        private uint chatRateGameId;

        /// <summary>Client-side delivery event: seat, channel, text, server time.</summary>
        public event Action<int, ChatChannel, int, string, double> ChatReceived;

        public void SubmitChat(string text, ChatChannel expected)
        {
            SubmitChat(text, expected, -1);
        }

        public void SubmitChat(string text, ChatChannel expected, int expectedRoomId)
        {
            if (!IsSpawned || authority == null) return;
            SubmitChatServerRpc(text ?? string.Empty, authority.GameId, (int)expected, expectedRoomId);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitChatServerRpc(string text, uint gameId, int expectedChannel, int expectedRoomId, ServerRpcParams rpcParams = default)
        {
            if (!IsServer || authority == null || machine == null || !initialized || !authority.Started) return;
            if (gameId != authority.GameId) return;
            if (expectedChannel < 0 || expectedChannel > 3) return;
            var sender = rpcParams.Receive.SenderClientId;
            var seat = SeatForClient(sender);
            if (seat < 0) return;
            var body = (text ?? string.Empty).Trim();
            if (body.Length == 0 || body.Length > ChatMaxLength) return;
            var expected = (ChatChannel)expectedChannel;
            var spectator = authority.IsSpectator(seat);
            if (spectator)
            {
                if (expected != ChatChannel.Spectator) return;
            }
            else
            {
                if (expected == ChatChannel.Spectator) return;
                if ((expected == ChatChannel.Nearby || expected == ChatChannel.Room) && !alive[seat]) return;
            }
            var roomId = -1;
            if (expected == ChatChannel.Room)
            {
                if (!TryResolveRoomNow(seat, out roomId))
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[Room] reject chat seat=" + (seat + 1) + " expectedRoom=" + expectedRoomId);
#endif
                    return;
                }
                if (expectedRoomId >= 0 && expectedRoomId != roomId) return;
            }
            if (chatRateGameId != gameId) { chatRateGameId = gameId; chatLastAt.Clear(); }
            var now = ServerNow();
            double last;
            if (chatLastAt.TryGetValue(seat, out last) && now - last < ChatMinInterval) return;
            chatLastAt[seat] = now;
            Vector2 origin = Vector2.zero;
            var hasOrigin = expected == ChatChannel.Nearby && authority.TryGetSeatPosition(seat, out origin);
            var radius = ChatNearbyRadius;
            var sent = 0;
            for (var i = 0; i < tier; i++)
            {
                bool isNpc; bool isOnline; ulong bound;
                if (!authority.TryGetSeatInfo(i, out isNpc, out isOnline, out bound)) continue;
                if (isNpc || !isOnline) continue;
                if (expected == ChatChannel.Spectator) { if (!authority.IsSpectator(i)) continue; }
                else
                {
                    if (authority.IsSpectator(i) || !alive[i]) continue;
                    if (expected == ChatChannel.Room && (!TryResolveRoomNow(i, out var targetRoom) || targetRoom != roomId)) continue;
                    if (expected == ChatChannel.Nearby)
                    {
                        Vector2 other;
                        if (!hasOrigin || !authority.TryGetSeatPosition(i, out other)) continue;
                        var distance = Vector2.Distance(origin, other);
                        if (!(distance <= radius)) continue;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        Debug.Log("[ChatNearby] from=" + (seat + 1) + " to=" + (i + 1) + " pos=" + origin + " other=" + other + " dist=" + distance.ToString("0.00") + " radius=" + radius.ToString("0.00"));
#endif
                    }
                }
                if (!TryGetSeatClient(i, out var clientId)) continue;
                var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
                ChatDeliveryClientRpc(gameId, seat, expectedChannel, roomId, body, now, target);
                sent++;
            }
            if (expected == ChatChannel.Room && NetworkManager != null)
            {
                foreach (var client in NetworkManager.ConnectedClientsIds)
                {
                    var spectatorSeat = SeatForClient(client);
                    if (spectatorSeat < 0 || spectatorSeat == seat || !authority.IsSpectator(spectatorSeat)) continue;
                    var followed = authority.SpectatorTargetForClient(client);
                    bool followedNpc; bool followedOnline; ulong followedClient;
                    if (followed < 0 || !alive[followed] || authority.IsSpectator(followed)
                        || !authority.TryGetSeatInfo(followed, out followedNpc, out followedOnline, out followedClient) || !followedOnline) continue;
                    if (authority.TryGetSeatPosition(followed, out var followedPos) == false) continue;
                    if (RoomAt(followedPos) != roomId) continue;
                    var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { client } } };
                    ChatDeliveryClientRpc(gameId, seat, expectedChannel, roomId, body, now, target);
                    sent++;
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (expected == ChatChannel.Nearby)
                Debug.Log("[ChatNearby] result seat=" + (seat + 1) + " radius=" + radius.ToString("0.00") + " targets=" + sent);
#endif
        }

        [ClientRpc]
        private void ChatDeliveryClientRpc(uint gameId, int seat, int channel, int roomId, string text, double serverTime, ClientRpcParams rpcParams = default)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || gameId != authority.GameId) return;
            if (channel < 0 || channel > 3) return;
            var actual = (ChatChannel)channel;
            if (actual == ChatChannel.Spectator) { if (!authority.IsLocalSpectator) return; }
            else if (authority.IsLocalSpectator && actual != ChatChannel.Room) return;
            var handler = ChatReceived;
            if (handler != null) handler(seat, actual, roomId, text, serverTime);
        }
    }
}