using System;

namespace Worthless.Lobby
{
    public enum TradeResultCode
    {
        Ok = 0,
        Committed = 1,
        Rejected = 2,
        Withdrawn = 3,
        Expired = 4,
        Locked = 5,
        NoEligibility = 6,
        NotTradePhase = 7,
        InvalidTarget = 8,
        Stale = 9,
        Busy = 10,
        Conflict = 11,
        Disconnected = 12,
        ReturnBlocked = 13
    }

    /// <summary>原子交易规则（纯 C#）：阶段窗口、资格、请求校验与回换限制。</summary>
    public static class TradeRules
    {
        public const int MaxFailuresPerSegment = 2;

        public static bool IsWindowOpen(bool started, bool isTradePhase, float remainingSeconds)
        {
            return started && isTradePhase && remainingSeconds > 0f;
        }

        public static bool CanUseEligibility(int failures, bool successUsed)
        {
            return failures < MaxFailuresPerSegment && !successUsed;
        }

        public static bool CanInitiate(bool started, bool isTradePhase, float remaining, bool alive, bool spectator,
            int failures, bool successUsed, int targetSeat, int fromSeat, bool targetAlive)
        {
            if (!IsWindowOpen(started, isTradePhase, remaining)) return false;
            if (!alive || spectator || !targetAlive) return false;
            if (targetSeat == fromSeat || targetSeat < 0) return false;
            return CanUseEligibility(failures, successUsed);
        }

        public static bool IsStaleVersion(int expectedInstance, int actualInstance, int expectedVersion, int actualVersion)
        {
            return expectedInstance != actualInstance || expectedVersion != actualVersion;
        }

        /// <summary>P0-04 试跑：NPC 每次请求只采样一次；0/100 可用于确定性验收。</summary>
        public static bool IsValidProbability(float probability)
        {
            return !float.IsNaN(probability) && probability >= 0f && probability <= 1f;
        }

        public static bool NpcAccepts(float probability, int roll)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return roll < probability * 10000f;
        }

        public static bool IsReturnTradeBlocked(bool instancePreviouslySentByReceiver)
        {
            return instancePreviouslySentByReceiver;
        }
    }
}
