using System;

namespace Worthless.Lobby
{
    /// <summary>回合节奏配置（可序列化数据；Unity 侧由 ScriptableObject 承载）。</summary>
    [Serializable]
    public sealed class RoundConfigData
    {
        public float RoundStartSeconds = 1f;
        public float OpeningGoalSeconds = 6f;
        public float FirstTradeSeconds = 120f;
        public float BroadcastSeconds = 12f;
        public float SecondTradeSeconds = 120f;
        public float LieRevealSeconds = 5f;
        public float ThirdTradeSeconds = 60f;
        public const int DefaultRounds = 3;
        public const int MaxRounds = 10;
        public float MarketDisplaySeconds = 3f;

        public RoundConfigData Copy() => (RoundConfigData)MemberwiseClone();

        public float GetPhaseSeconds(RoundPhase phase, bool earlyTrade)
        {
            var trade = earlyTrade ? 1f : 0f;
            switch (phase)
            {
                case RoundPhase.RoundStart: return RoundStartSeconds;
                case RoundPhase.FirstTrade: return earlyTrade ? trade : FirstTradeSeconds;
                case RoundPhase.Broadcast: return BroadcastSeconds;
                case RoundPhase.SecondTrade: return earlyTrade ? trade : SecondTradeSeconds;
                case RoundPhase.LieReveal: return LieRevealSeconds;
                case RoundPhase.ThirdTrade: return earlyTrade ? trade : ThirdTradeSeconds;
                case RoundPhase.SecretChoice:
                case RoundPhase.TieBreak: return SecretSelections.Seconds;
                case RoundPhase.MarketUpdate: return MarketDisplaySeconds;
                default: return 0f;
            }
        }

        public static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public bool Validate(out string error)
        {
            error = null;
            if (!IsFinite(RoundStartSeconds) || !IsFinite(OpeningGoalSeconds) || !IsFinite(FirstTradeSeconds) || !IsFinite(BroadcastSeconds)
                || !IsFinite(SecondTradeSeconds) || !IsFinite(LieRevealSeconds) || !IsFinite(ThirdTradeSeconds)
                || !IsFinite(MarketDisplaySeconds))
            {
                error = "阶段时长必须是有限数值";
                return false;
            }

            if (RoundStartSeconds < 0f || RoundStartSeconds > 30f) error = "RoundStart 0..30s";
            else if (OpeningGoalSeconds < 0.5f || OpeningGoalSeconds > 30f) error = "开局目标播报 0.5..30s";
            else if (FirstTradeSeconds < 1f || FirstTradeSeconds > 600f) error = "第一交易 1..600s";
            else if (BroadcastSeconds < 0f || BroadcastSeconds > 60f) error = "广播 0..60s";
            else if (SecondTradeSeconds < 1f || SecondTradeSeconds > 600f) error = "第二交易 1..600s";
            else if (LieRevealSeconds < 0f || LieRevealSeconds > 60f) error = "谎言揭示 0..60s";
            else if (ThirdTradeSeconds < 1f || ThirdTradeSeconds > 600f) error = "第三交易 1..600s";
            else if (MarketDisplaySeconds < 0.5f || MarketDisplaySeconds > 30f) error = "市场展示 0.5..30s";
            return error == null;
        }
    }
}
