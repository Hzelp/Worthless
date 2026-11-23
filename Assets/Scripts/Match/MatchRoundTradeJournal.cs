using System;
using Unity.Collections;
using Unity.Netcode;
using Worthless.Lobby;

namespace Worthless
{
    public struct PublicMarketRecord : INetworkSerializable, IEquatable<PublicMarketRecord>
    {
        public uint Game;
        public int Round, EventIndex;
        public FixedList128Bytes<short> BasisPoints;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Game);
            serializer.SerializeValue(ref Round);
            serializer.SerializeValue(ref EventIndex);
            var count = (byte)BasisPoints.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader) BasisPoints.Length = count;
            for (var i = 0; i < count; i++)
            {
                var rate = BasisPoints[i];
                serializer.SerializeValue(ref rate);
                if (serializer.IsReader) BasisPoints[i] = rate;
            }
        }

        public bool Equals(PublicMarketRecord other) => Game == other.Game && Round == other.Round
            && EventIndex == other.EventIndex && BasisPoints.Equals(other.BasisPoints);
    }
    // Only already-public names, items and timing. Never add prices or secret fields here.
    public struct PublicTradeRecord : INetworkSerializable, IEquatable<PublicTradeRecord>
    {
        public uint Game;
        public int Sequence, Round, Phase, Ordinal, FromSeat, ToSeat, FromDef, ToDef;
        public FixedString64Bytes FromName, ToName;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Game); serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Round); serializer.SerializeValue(ref Phase); serializer.SerializeValue(ref Ordinal);
            serializer.SerializeValue(ref FromSeat); serializer.SerializeValue(ref ToSeat);
            serializer.SerializeValue(ref FromDef); serializer.SerializeValue(ref ToDef);
            serializer.SerializeValue(ref FromName); serializer.SerializeValue(ref ToName);
        }
        public bool Equals(PublicTradeRecord other) => Game == other.Game && Sequence == other.Sequence && Round == other.Round && Phase == other.Phase
            && Ordinal == other.Ordinal && FromSeat == other.FromSeat && ToSeat == other.ToSeat && FromDef == other.FromDef && ToDef == other.ToDef
            && FromName.Equals(other.FromName) && ToName.Equals(other.ToName);
    }

    public sealed partial class MatchRoundDirector
    {
        private readonly NetworkList<PublicTradeRecord> publicTradeHistory = new NetworkList<PublicTradeRecord>();
        private readonly NetworkList<PublicMarketRecord> publicMarketHistory = new NetworkList<PublicMarketRecord>();
        private TradeJournalIndex tradeJournal = new TradeJournalIndex();
        private uint announcedGame;
        private int announcedSequence;
        public event Action<PublicTradeRecord> TradeCommitted;
        public int PublicTradeCount => publicTradeHistory.Count;
        public bool TryGetPublicTrade(int index, out PublicTradeRecord record)
        {
            record = default;
            if (index < 0 || index >= publicTradeHistory.Count || authority == null) return false;
            record = publicTradeHistory[index];
            return record.Game == authority.GameId;
        }
        public string DescribePublicTrade(PublicTradeRecord record)
            => "第 " + record.Round + " 轮 · 第 " + HistoryLabels.TradeStage((RoundPhase)record.Phase) + " 次交易  P" + (record.FromSeat + 1)
                + " " + record.FromName + "「" + GetItemName(record.FromDef) + "」 ↔ P" + (record.ToSeat + 1) + " " + record.ToName + "「" + GetItemName(record.ToDef) + "」";

        public string HistoryPriceText(HeldItemState entry)
        {
            var text = entry.ValueAtAcquisition.ToString("N0");
            if (authority == null || !TryGetPublicItem(authority.LocalSeat, out var held) || held.InstanceId == entry.InstanceId) return text;
            if (catalog == null || entry.DefIndex < 0 || entry.DefIndex >= catalog.Definitions.Length) return text;
            var estimate = new KnownPriceEstimate(entry.ValueAtAcquisition);
            var category = catalog.Definitions[entry.DefIndex].Category;
            for (var i = 0; i < publicMarketHistory.Count; i++)
            {
                var market = publicMarketHistory[i];
                if (market.Game != authority.GameId) continue;
                if (!entry.PrecedesMarket(market.Round)) continue;
                if (market.EventIndex < 0 || market.EventIndex >= economyData.Events.Length) continue;
                var categoryIndex = Array.IndexOf(economyData.Events[market.EventIndex].Categories, category);
                if (categoryIndex < 0 || categoryIndex >= market.BasisPoints.Length) continue;
                estimate.ApplyMarket(market.BasisPoints[categoryIndex], economyData.MinValue, economyData.MaxValue);
            }
            return text + (estimate.Arrow.Length > 0 ? " " + estimate.Arrow : "");
        }

        private void ResetTradeJournal()
        {
            publicTradeHistory.Clear();
            publicMarketHistory.Clear();
            tradeJournal = new TradeJournalIndex();
            announcedGame = 0; announcedSequence = 0;
        }
        private int RecordCommittedTrade(TradeRequest request, int fromDefinition, int toDefinition)
        {
            if (!tradeJournal.TryRecord(request.Id, request.Round, (RoundPhase)request.Phase, out var sequence, out var ordinal)) return 0;
            var record = new PublicTradeRecord { Game = authority.GameId, Sequence = sequence, Round = request.Round, Phase = request.Phase,
                Ordinal = ordinal, FromSeat = request.From, ToSeat = request.To, FromDef = fromDefinition, ToDef = toDefinition,
                FromName = new FixedString64Bytes(authority.PlayerNameForSeat(request.From)), ToName = new FixedString64Bytes(authority.PlayerNameForSeat(request.To)) };
            publicTradeHistory.Add(record);
            LiveTradeClientRpc(record);
            return sequence;
        }
        [ClientRpc]
        private void LiveTradeClientRpc(PublicTradeRecord record)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || record.Game != authority.GameId) return;
            if (announcedGame != record.Game) { announcedGame = record.Game; announcedSequence = 0; }
            if (record.Sequence <= announcedSequence) return;
            announcedSequence = record.Sequence;
            TradeCommitted?.Invoke(record);
        }
    }
}
