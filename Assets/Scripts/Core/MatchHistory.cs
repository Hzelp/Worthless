using System;
using System.Collections.Generic;

namespace Worthless.Lobby
{
    public enum ValueHistoryReason { Initial, Trade, Settlement, TieBreak, RoundOpening }

    public struct HeldItemState
    {
        public int InstanceId, DefIndex, Round, Phase, TradeNumber;
        // A frozen price known while holding this item, never a live lookup after disposal.
        public long ValueAtAcquisition;
        public ValueHistoryReason Reason;

        public bool PrecedesMarket(int marketRound)
        {
            if (marketRound != Round) return marketRound > Round;
            return Reason != ValueHistoryReason.Settlement && Reason != ValueHistoryReason.TieBreak;
        }
    }

    public sealed class ValueHistoryLedger
    {
        private readonly List<HeldItemState>[] entries;
        public ValueHistoryLedger(int seats)
        {
            if (seats < 1 || seats > 10) throw new ArgumentOutOfRangeException(nameof(seats));
            entries = new List<HeldItemState>[seats];
            for (var i = 0; i < seats; i++) entries[i] = new List<HeldItemState>();
        }
        public IReadOnlyList<HeldItemState> ForSeat(int seat) => entries[seat].AsReadOnly();

        public void RecordSettlement(int seat, int round, int totalRounds, bool tieBreak, int instance, int definition, long value)
        {
            if (tieBreak)
                Record(seat, round, RoundPhase.TieBreak, ValueHistoryReason.TieBreak, instance, definition, value);
            else if (round < totalRounds)
                Record(seat, round + 1, RoundPhase.RoundStart, ValueHistoryReason.RoundOpening, instance, definition, value);
            else
                Record(seat, round, RoundPhase.FinalSettlement, ValueHistoryReason.Settlement, instance, definition, value);
        }

        public bool Record(int seat, int round, RoundPhase phase, ValueHistoryReason reason, int instance, int definition, long value, int tradeNumber = 0)
        {
            if (seat < 0 || seat >= entries.Length || round < 1) return false;
            var list = entries[seat];
            foreach (var previous in list)
                if (previous.Round == round && previous.Phase == (int)phase && previous.Reason == reason && previous.TradeNumber == tradeNumber && previous.InstanceId == instance)
                    return false;
            list.Add(new HeldItemState { Round = round, Phase = (int)phase, Reason = reason, TradeNumber = tradeNumber,
                InstanceId = instance, DefIndex = definition, ValueAtAcquisition = value });
            return true;
        }
    }

    public sealed class TradeJournalIndex
    {
        private readonly HashSet<uint> committed = new HashSet<uint>();
        private readonly Dictionary<(int, RoundPhase), int> ordinals = new Dictionary<(int, RoundPhase), int>();
        public bool TryRecord(uint request, int round, RoundPhase phase, out int sequence, out int ordinal)
        {
            sequence = ordinal = 0;
            if (round < 1 || !RoundStateMachine.IsTradePhase(phase) || !committed.Add(request)) return false;
            var key = (round, phase);
            ordinals.TryGetValue(key, out ordinal);
            ordinal++;
            ordinals[key] = ordinal;
            sequence = committed.Count;
            return true;
        }
    }

    public static class HistorySnapshotRules
    {
        public static bool CanApply(uint game, int seat, int snapshotRound, int count, uint currentGame, int localSeat, int currentRound, int currentCount)
            => game != 0 && game == currentGame && seat >= 0 && seat == localSeat && snapshotRound > 0
                && snapshotRound <= currentRound && count >= currentCount;
    }

    public static class HistoryLabels
    {
        public static int TradeStage(RoundPhase phase) => phase == RoundPhase.FirstTrade ? 1 : phase == RoundPhase.SecondTrade ? 2 : phase == RoundPhase.ThirdTrade ? 3 : 0;
        public static string Event(HeldItemState entry)
        {
            var prefix = "第 " + entry.Round + " 轮 · ";
            switch (entry.Reason)
            {
                case ValueHistoryReason.Initial: return prefix + "初始物品";
                case ValueHistoryReason.RoundOpening: return prefix + "开场价值";
                case ValueHistoryReason.Trade: return prefix + "第 " + TradeStage((RoundPhase)entry.Phase) + " 次交易";
                case ValueHistoryReason.TieBreak: return prefix + "并列加赛结算";
                default: return prefix + "轮末结算";
            }
        }
    }

    public struct KnownPriceEstimate
    {
        public long LastKnownValue { get; private set; }
        public long MarketOnlyValue { get; private set; }
        public KnownPriceEstimate(long value) { LastKnownValue = MarketOnlyValue = value; }
        public void ApplyMarket(int basisPoints, long min, long max)
            => MarketOnlyValue = EconomyRules.ApplyBasisPoints(MarketOnlyValue, basisPoints, min, max);
        public string Arrow => MarketOnlyValue > LastKnownValue ? "↑" : MarketOnlyValue < LastKnownValue ? "↓" : "";
    }
}
