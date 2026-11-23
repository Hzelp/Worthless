namespace Worthless.Lobby
{
    public enum PrivateMergeDecision
    {
        Discard = 0,
        Apply = 1,
        Stage = 2
    }

    /// <summary>定向私有值合并规则：局/轮/席位/实例/公开版本对齐才显示；未来版本等待，旧/异席丢弃。</summary>
    public static class PrivateValueRules
    {
        public static PrivateMergeDecision Decide(
            uint payloadGameId, int payloadRound, int payloadSeat, int payloadInstance, int payloadVersion,
            uint gameId, int localSeat, int currentRound, int itemInstance, bool itemEliminated,
            int publicVersion, int currentPrivateVersion, int currentPrivateRound)
        {
            if (payloadGameId != gameId || itemEliminated) return PrivateMergeDecision.Discard;
            if (localSeat < 0 || currentRound <= 0 || itemInstance <= 0) return PrivateMergeDecision.Stage;
            if (payloadSeat != localSeat) return PrivateMergeDecision.Discard;
            if (payloadRound != currentRound) return payloadRound > currentRound ? PrivateMergeDecision.Stage : PrivateMergeDecision.Discard;
            if (payloadInstance != itemInstance)
            {
                // 成交的新实例与新版本都可能先于公开包到达；较新的合法定向包必须保留。
                if (payloadVersion >= publicVersion) return PrivateMergeDecision.Stage;
                return PrivateMergeDecision.Discard;
            }
            if (payloadVersion < publicVersion) return PrivateMergeDecision.Discard;
            if (payloadVersion > publicVersion) return PrivateMergeDecision.Stage;
            if (payloadVersion < currentPrivateVersion) return PrivateMergeDecision.Discard;
            if (payloadVersion == currentPrivateVersion && payloadRound == currentPrivateRound) return PrivateMergeDecision.Discard;
            return PrivateMergeDecision.Apply;
        }
    }
}
