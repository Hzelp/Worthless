using System;

namespace Worthless.Lobby
{
    /// <summary>
    /// 权威阶段机：单调时间推进、有限步 advance、可快照恢复。
    /// 只负责阶段与截止时间；结算/市场/胜负由调用方在阶段切换回调里执行一次。
    /// </summary>
    public sealed class RoundStateMachine
    {
        public const int MaxTransitionsPerAdvance = 64;

        private readonly RoundConfigData config;
        private RoundPhase phase = RoundPhase.Idle;
        private int round;
        private double phaseEndsAt;
        private bool earlyTrade;
        private bool active;
        private int totalRounds = RoundConfigData.DefaultRounds;
        private bool needsTieBreak;

        public RoundStateMachine(RoundConfigData config)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public RoundConfigData Config { get { return config; } }
        public RoundPhase Phase { get { return phase; } }
        public int Round { get { return round; } }
        public double PhaseEndsAt { get { return phaseEndsAt; } }
        public bool Active { get { return active; } }
        public bool EarlyTrade { get { return earlyTrade; } }
        public int TotalRounds { get { return totalRounds; } }

        public void Start(double now, int rounds, bool earlyTradeTradePhases = false)
        {
            active = true;
            totalRounds = Math.Max(1, Math.Min(RoundConfigData.MaxRounds, rounds));
            needsTieBreak = false;
            round = 1;
            earlyTrade = earlyTradeTradePhases;
            phase = RoundPhase.RoundStart;
            phaseEndsAt = now + config.OpeningGoalSeconds;
        }

        public void Restore(int restoredRound, RoundPhase restoredPhase, double deadline, bool earlyTradePhases, int rounds = RoundConfigData.DefaultRounds)
        {
            active = true;
            totalRounds = Math.Max(1, Math.Min(RoundConfigData.MaxRounds, rounds));
            needsTieBreak = false;
            round = restoredRound;
            phase = restoredPhase;
            phaseEndsAt = deadline;
            earlyTrade = earlyTradePhases;
        }

        public void SetEarlyTrade(bool value)
        {
            earlyTrade = value;
        }

        /// <summary>提前结束当前阶段（用于交易阶段资格不足），最短保留 0.1s 以避免同帧跳过。</summary>
        public void RequestEarlyEnd(double now, float seconds)
        {
            if (!active) return;
            var target = now + (seconds < 0.1f ? 0.1f : seconds);
            if (target < phaseEndsAt) phaseEndsAt = target;
        }

        public void SetPhaseEnd(double deadline)
        {
            if (active) phaseEndsAt = deadline;
        }

        public void RequireTieBreak()
        {
            if (phase == RoundPhase.FinalSettlement && round == totalRounds) needsTieBreak = true;
        }

        public void Stop()
        {
            active = false;
            phase = RoundPhase.Idle;
            round = 0;
            phaseEndsAt = 0;
        }

        public double RemainingSeconds(double now)
        {
            if (!active || phase == RoundPhase.GameOver) return 0;
            var remaining = phaseEndsAt - now;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>
        /// 推进到 now；返回本次跨越的阶段数（有限上限）。每跨一个阶段，调用方执行该阶段开始动作。
        /// </summary>
        public int Advance(double now, Action<RoundPhase, int> onPhaseEntered = null)
        {
            if (!active) return 0;
            var transitions = 0;
            while (now >= phaseEndsAt && transitions < MaxTransitionsPerAdvance)
            {
                if (phase == RoundPhase.GameOver) { active = false; break; }
                var next = NextPhase(phase, out var nextRound);
                phase = next;
                round = nextRound;
                transitions++;
                if (phase == RoundPhase.GameOver)
                {
                    active = false;
                    phaseEndsAt = now;
                    onPhaseEntered?.Invoke(phase, round);
                    break;
                }

                var duration = phase == RoundPhase.LieReveal ? 5f : config.GetPhaseSeconds(phase, earlyTrade);
                var chained = phaseEndsAt + duration;
                // 展示阶段从实际进入时刻起算，不能因服务器迟到而缩短。
                if (phase == RoundPhase.Broadcast || phase == RoundPhase.LieReveal
                    || IsSecretPhase(phase) || phase == RoundPhase.MarketUpdate)
                {
                    phaseEndsAt = now + duration;
                }
                else
                {
                    phaseEndsAt = chained;
                }
                onPhaseEntered?.Invoke(phase, round);
            }

            return transitions;
        }

        private RoundPhase NextPhase(RoundPhase current, out int nextRound)
        {
            nextRound = round;
            switch (current)
            {
                case RoundPhase.RoundStart: return RoundPhase.FirstTrade;
                case RoundPhase.FirstTrade: return RoundPhase.Broadcast;
                case RoundPhase.Broadcast: return RoundPhase.SecondTrade;
                case RoundPhase.SecondTrade: return RoundPhase.LieReveal;
                case RoundPhase.LieReveal: return RoundPhase.ThirdTrade;
                case RoundPhase.ThirdTrade: return RoundPhase.SecretChoice;
                case RoundPhase.SecretChoice: return round == totalRounds ? RoundPhase.FinalSettlement : RoundPhase.MarketUpdate;
                case RoundPhase.MarketUpdate:
                    nextRound = round + 1;
                    return RoundPhase.RoundStart;
                case RoundPhase.FinalSettlement: return needsTieBreak ? RoundPhase.TieBreak : RoundPhase.GameOver;
                case RoundPhase.TieBreak: return RoundPhase.GameOver;
                default: return RoundPhase.GameOver;
            }
        }

        public static bool IsTradePhase(RoundPhase value)
        {
            return value == RoundPhase.FirstTrade || value == RoundPhase.SecondTrade || value == RoundPhase.ThirdTrade;
        }

        public static bool IsSecretPhase(RoundPhase value) => value == RoundPhase.SecretChoice || value == RoundPhase.TieBreak;

        public static bool IsRevealPhase(RoundPhase value)
        {
            return value == RoundPhase.Broadcast || value == RoundPhase.LieReveal;
        }
    }
}
