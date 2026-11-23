using System;
using System.Collections.Generic;

namespace Worthless.Lobby
{
    public struct RankEntry
    {
        public int Seat;
        public long Value;

        public RankEntry(int seat, long value)
        {
            Seat = seat;
            Value = value;
        }
    }

    public interface IRandomSource
    {
        int Next(int maxExclusive);
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random random;
        public SystemRandomSource(int seed) { random = new Random(seed); }
        public int Next(int maxExclusive) { return maxExclusive <= 1 ? 0 : random.Next(maxExclusive); }
    }

    public static class EliminationRules
    {
        /// <summary>
        /// 按价值最低淘汰 count 人；只在跨越淘汰名额边界的同价组内随机补足（无偏、不改排序）。
        /// 返回值按席位升序；不足 count 时全淘汰。
        /// </summary>
        public static int[] Select(IReadOnlyList<RankEntry> alive, int count, IRandomSource random)
        {
            if (alive == null || alive.Count == 0 || count <= 0) return Array.Empty<int>();
            var sorted = new List<RankEntry>(alive);
            sorted.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : a.Seat.CompareTo(b.Seat));
            var take = Math.Min(count, sorted.Count);
            var cutoff = sorted[take - 1].Value;

            var below = new List<int>();
            var boundary = new List<int>();
            for (var i = 0; i < sorted.Count; i++)
            {
                if (sorted[i].Value < cutoff) below.Add(sorted[i].Seat);
                else if (sorted[i].Value == cutoff) boundary.Add(sorted[i].Seat);
            }

            var eliminated = new List<int>(below);
            var remaining = take - below.Count;
            // 无偏洗牌边界组，取前 remaining 个
            for (var i = boundary.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var tmp = boundary[i];
                boundary[i] = boundary[j];
                boundary[j] = tmp;
            }

            for (var i = 0; i < remaining && i < boundary.Count; i++) eliminated.Add(boundary[i]);
            eliminated.Sort();
            return eliminated.ToArray();
        }
    }
}