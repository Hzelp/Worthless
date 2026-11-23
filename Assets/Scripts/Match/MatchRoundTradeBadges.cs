using Unity.Netcode;

namespace Worthless
{
    public sealed partial class MatchRoundDirector
    {
        private uint returnBadgeGame;
        private int returnBadgeRound, returnBadgeSeat = -1, returnBadgeInstance, returnBadgeRevision;
        private int returnBadgeMask;

        // Pair eligibility only. No other player's inventory history or price is sent.
        public bool IsLocalReturnTradeBlocked(int targetSeat)
        {
            if (authority == null || !authority.IsLocalPlayerSeated || targetSeat == authority.LocalSeat ||
                targetSeat < 0 || targetSeat >= MatchAuthority.SeatCount) return false;
            if (IsServer)
                return (BuildReturnBadgeMask(authority.LocalSeat) & (1 << targetSeat)) != 0;
            return returnBadgeGame == authority.GameId && returnBadgeRound == Round &&
                returnBadgeSeat == authority.LocalSeat && TryGetPublicItem(authority.LocalSeat, out var held) &&
                held.InstanceId == returnBadgeInstance && held.ValueVersion <= returnBadgeRevision &&
                (returnBadgeMask & (1 << targetSeat)) != 0;
        }

        private int BuildReturnBadgeMask(int seat)
        {
            if (itemInstance == null || seat < 0 || seat >= tier) return 0;
            var mask = 0;
            for (var other = 0; other < tier; other++)
                if (other != seat && (tradedOutInstances.Contains((seat, itemInstance[other])) ||
                    tradedOutInstances.Contains((other, itemInstance[seat])))) mask |= 1 << other;
            return mask;
        }

        private void SendReturnBadges(int seat, ulong clientId)
        {
            if (!IsServer || machine == null || itemInstance == null) return;
            ReturnBadgesClientRpc(authority.GameId, machine.Round, seat, itemInstance[seat], valueBatchVersion,
                BuildReturnBadgeMask(seat), new ClientRpcParams
                { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } });
        }

        private void SendReturnBadgesToAll()
        {
            for (var seat = 0; seat < tier; seat++)
                if (TryGetSeatClient(seat, out var clientId)) SendReturnBadges(seat, clientId);
        }

        [ClientRpc]
        private void ReturnBadgesClientRpc(uint game, int round, int seat, int instance, int revision, int mask,
            ClientRpcParams rpcParams = default)
        {
            // Keep early packets until the public snapshot/seat binding catches up.
            if (returnBadgeGame == game && (returnBadgeRound > round ||
                (returnBadgeRound == round && returnBadgeRevision > revision))) return;
            returnBadgeGame = game;
            returnBadgeRound = round;
            returnBadgeSeat = seat;
            returnBadgeInstance = instance;
            returnBadgeRevision = revision;
            returnBadgeMask = mask;
        }
    }
}
