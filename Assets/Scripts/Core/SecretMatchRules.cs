using System;
using System.Collections.Generic;

namespace Worthless.Lobby
{
    public enum GoalGroup : byte { High = 1, Low = 2 }

    public sealed class SecretGoals
    {
        private readonly GoalGroup[] groups;
        public int HighRank { get; }
        public int LowRank { get; }

        public SecretGoals(int seats, IRandomSource random)
        {
            if (!LobbySeatingRules.IsValidTier(seats)) throw new ArgumentOutOfRangeException(nameof(seats));
            HighRank = 1 + random.Next(2);
            LowRank = 1 + random.Next(2);
            var highCount = seats / 2 + (seats % 2 == 0 ? 0 : random.Next(2));
            groups = new GoalGroup[seats];
            for (var i = 0; i < seats; i++) groups[i] = i < highCount ? GoalGroup.High : GoalGroup.Low;
            for (var i = seats - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var previous = groups[i];
                groups[i] = groups[j];
                groups[j] = previous;
            }
        }

        public GoalGroup GroupOf(int seat) => groups[seat];
        public int RankFor(GoalGroup group) => group == GoalGroup.High ? HighRank : LowRank;

        public int[] Winners(long[] values, GoalGroup group)
        {
            var groupPrices = new List<long>();
            for (var i = 0; i < groups.Length; i++) if (groups[i] == group) groupPrices.Add(values[i]);
            groupPrices.Sort();
            var index = group == GoalGroup.High ? groupPrices.Count - HighRank : LowRank - 1;
            var winningValue = groupPrices[index];
            var winners = new List<int>();
            for (var i = 0; i < groups.Length; i++)
                if (groups[i] == group && values[i] == winningValue) winners.Add(i);
            return winners.ToArray();
        }
    }

    public sealed class SecretSelections
    {
        public const int Seconds = 30;
        public const int Change = 5;

        private readonly int[] targets;
        private readonly int[] changes;
        private readonly double deadline;
        private int count;
        private bool locked;

        public SecretSelections(int seats, double deadline)
        {
            targets = new int[seats];
            changes = new int[seats];
            this.deadline = deadline;
        }

        public bool Complete => count == targets.Length;

        public bool TrySubmit(int seat, int target, int change, double now)
        {
            if (locked || now >= deadline || seat < 0 || seat >= targets.Length || target < 0 || target >= targets.Length || seat == target) return false;
            if (changes[seat] != 0 || (change != Change && change != -Change)) return false;
            Set(seat, target, change);
            return true;
        }

        public bool TryGet(int seat, out int target, out int change)
        {
            target = targets[seat];
            change = changes[seat];
            return change != 0;
        }

        public void FillRandom(int seat, IRandomSource random)
        {
            if (locked || changes[seat] != 0) return;
            var target = random.Next(targets.Length - 1);
            if (target >= seat) target++;
            Set(seat, target, random.Next(2) == 0 ? -Change : Change);
        }

        private void Set(int seat, int target, int change)
        {
            targets[seat] = target;
            changes[seat] = change;
            count++;
        }

        public int[] LockAndSum(IRandomSource random)
        {
            if (locked) throw new InvalidOperationException("Secret choices already applied");
            var deltas = new int[targets.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                FillRandom(i, random);
                deltas[targets[i]] += changes[i];
            }
            locked = true;
            return deltas;
        }
    }
}
