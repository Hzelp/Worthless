namespace Worthless.Lobby
{
    /// <summary>席位绑定与断线清理的纯规则，保证旧连接的迟到回调不会清掉新绑定。</summary>
    public static class SeatBindingRules
    {
        /// <summary>NGO 的 ServerClientId 是 0，哨兵必须避开一切合法 ClientId。</summary>
        public const ulong Unbound = ulong.MaxValue;

        public static bool IsBound(ulong boundClientId)
        {
            return boundClientId != Unbound;
        }

        public static bool IsSeatIndexActive(int seatIndex, int targetSeats)
        {
            return seatIndex >= 0 && seatIndex < targetSeats && targetSeats > 0;
        }

        public static bool NeedsOldDisconnect(bool seatOnline, ulong boundClientId, ulong candidateClientId)
        {
            return seatOnline && IsBound(boundClientId) && boundClientId != candidateClientId;
        }

        public static bool ShouldClearOnDisconnect(bool seatOnline, ulong boundClientId, ulong disconnectedClientId)
        {
            return seatOnline && IsBound(boundClientId) && boundClientId == disconnectedClientId;
        }

        public static bool CanMove(bool matchStarted, bool seatOnline, ulong boundClientId, ulong senderClientId)
        {
            return matchStarted && seatOnline && IsBound(boundClientId) && boundClientId == senderClientId;
        }
    }
}