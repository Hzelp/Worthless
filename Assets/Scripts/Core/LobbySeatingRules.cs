using System;

namespace Worthless.Lobby
{
    /// <summary>参赛人数、补位和大厅校验。</summary>
    public static class LobbySeatingRules
    {
        public const int MinRealPlayers = 1;
        public const int MinSeats = 5;
        public const int MaxRealPlayers = 10;

        public static readonly int[] Tiers = { 5, 6, 7, 8, 9, 10 };

        public static bool IsValidTier(int tier)
        {
            return tier >= MinSeats && tier <= MaxRealPlayers;
        }

        /// <summary>容纳当前真人数的最小档位；无有效真人数时返回 0。</summary>
        public static int DefaultTier(int realCount)
        {
            if (realCount < MinRealPlayers || realCount > MaxRealPlayers) return 0;
            return Math.Max(MinSeats, realCount);
        }

        /// <summary>预览 NPC 数量（大厅阶段按已选档位与真人数量计算）。</summary>
        public static int NpcPreview(int tier, int realCount)
        {
            if (!IsValidTier(tier)) return 0;
            var filled = Math.Min(realCount, tier);
            return Math.Max(0, tier - filled);
        }

        /// <summary>大厅是否还能接受新的真人玩家。</summary>
        public static bool CanAcceptJoin(int realCount)
        {
            return realCount < MaxRealPlayers;
        }

        /// <summary>权威开局校验；reason 与 UI/日志使用同一句文案。</summary>
        public static bool CanStart(int tier, int realCount, out string reason)
        {
            reason = null;
            if (!IsValidTier(tier))
            {
                reason = "目标席位必须在 5 至 10 之间";
                return false;
            }

            if (realCount < MinRealPlayers)
            {
                reason = "至少需要 1 名真人玩家才能开赛";
                return false;
            }

            if (realCount > MaxRealPlayers)
            {
                reason = "真人玩家超过 10 人，无法开赛";
                return false;
            }

            if (realCount > tier)
            {
                reason = "真人玩家（" + realCount + "）超过目标席位（" + tier + "），请选择更大的档位";
                return false;
            }

            return true;
        }
    }
}
