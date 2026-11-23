using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    public sealed partial class MatchRoundDirector
    {
        private static readonly IReadOnlyList<HeldItemState> EmptyHeldHistory = new HeldItemState[0];
        private readonly List<HeldItemState> localHeldHistory = new List<HeldItemState>();
        private uint heldHistoryGameId;
        private int heldHistoryRound;
        private int heldHistorySeat = -1;
        private bool hasPendingHeld;
        private uint heldPendingGameId;
        private int heldPendingRound, heldPendingSeat = -1;
        private int[] pendingDefs, pendingInstances, pendingRounds, pendingPhases, pendingReasons, pendingTrades;
        private long[] pendingValues;

        public void ServerSendInitialHeldHistory()
        {
            if (!IsServer || !initialized || authority == null) return;
            for (var i = 0; i < tier; i++) SendHistoryToSeat(i);
        }

        private void ReceiveLocalHeldHistory(uint gameId, int round, int seat, int[] defs, int[] instances, long[] values,
            int[] rounds, int[] phases, int[] reasons, int[] trades)
        {
            if (defs == null || instances == null || values == null || rounds == null || phases == null || reasons == null || trades == null) return;
            var count = defs.Length;
            if (instances.Length != count || values.Length != count || rounds.Length != count || phases.Length != count || reasons.Length != count || trades.Length != count) return;
            if (authority != null && authority.GameId != 0 && gameId != authority.GameId) return;
            if (authority != null && authority.LocalSeat >= 0 && seat != authority.LocalSeat) return;
            if (heldHistoryGameId == gameId && heldHistorySeat == seat && count < localHeldHistory.Count) return;
            if (hasPendingHeld && heldPendingGameId == gameId && heldPendingSeat == seat && count < pendingDefs.Length) return;
            hasPendingHeld = true;
            heldPendingGameId = gameId; heldPendingRound = round; heldPendingSeat = seat;
            pendingDefs = defs; pendingInstances = instances; pendingValues = values;
            pendingRounds = rounds; pendingPhases = phases; pendingReasons = reasons; pendingTrades = trades;
        }

        private void TickHeldHistory()
        {
            if (!hasPendingHeld || authority == null || authority.GameId == 0 || authority.LocalSeat < 0) return;
            if (heldPendingGameId != authority.GameId || heldPendingSeat != authority.LocalSeat) { ClearPendingHeld(); return; }
            if (Round < heldPendingRound) return;
            if (HistorySnapshotRules.CanApply(heldPendingGameId, heldPendingSeat, heldPendingRound, pendingDefs.Length,
                authority.GameId, authority.LocalSeat, Round, localHeldHistory.Count))
            {
                heldHistoryGameId = heldPendingGameId; heldHistoryRound = heldPendingRound; heldHistorySeat = heldPendingSeat;
                localHeldHistory.Clear();
                for (var i = 0; i < pendingDefs.Length; i++)
                    localHeldHistory.Add(new HeldItemState { DefIndex = pendingDefs[i], InstanceId = pendingInstances[i], ValueAtAcquisition = pendingValues[i],
                        Round = pendingRounds[i], Phase = pendingPhases[i], Reason = (ValueHistoryReason)pendingReasons[i], TradeNumber = pendingTrades[i] });
                var text = new StringBuilder();
                foreach (var item in localHeldHistory)
                    text.Append(HistoryLabels.Event(item)).Append("  ").Append(GetItemName(item.DefIndex)).Append("  ·  ").Append(item.ValueAtAcquisition).AppendLine();
                localTradeHistory = text.ToString().TrimEnd();
            }
            ClearPendingHeld();
        }

        private void ClearPendingHeld()
        {
            hasPendingHeld = false;
            pendingDefs = pendingInstances = pendingRounds = pendingPhases = pendingReasons = pendingTrades = null;
            pendingValues = null;
        }

        private void ClearHeldHistoryClient(uint pendingGameToKeep)
        {
            localHeldHistory.Clear(); localTradeHistory = string.Empty;
            if (pendingGameToKeep == 0 || pendingGameToKeep != heldPendingGameId) ClearPendingHeld();
            heldHistoryGameId = 0; heldHistoryRound = 0; heldHistorySeat = -1;
        }
    }
}
