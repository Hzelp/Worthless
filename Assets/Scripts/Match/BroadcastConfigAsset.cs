using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    [CreateAssetMenu(menuName = "Worthless/BroadcastConfig", fileName = "BroadcastConfig")]
    public sealed class BroadcastConfigAsset : ScriptableObject
    {
        [Min(1)] public int ItemCount = 4;
        [Min(0)] public int FakeMin = 0;
        [Min(0)] public int FakeMax = 2;
        [Min(1)] public int IntervalSeconds = 3;
        [Min(1)] public int LastHoldSeconds = 3;
        [Range(0f, 1f)] public float RumorChance = 0.1f;
        [Tooltip("每批广播允许优先抽取玩家精确名次的百分比。候选不足时可补位。")]
        [Range(0, 100)] public int ExactRankPercent = BroadcastBatchOptions.DefaultExactRankPercent;
        [Header("谎言数量权重（0 / 1 / 2 条）")]
        [Min(0)] public int ZeroLieWeight = 10;
        [Min(0)] public int OneLieWeight = 55;
        [Min(0)] public int TwoLieWeight = 35;
        [Header("涂黑概率（百分比；其余为涂黑一处）")]
        [Range(0, 100)] public int NoRedactionPercent = 2;
        [Range(0, 100)] public int DoubleRedactionPercent = 10;
        [Range(0, 100)] public int FullRedactionPercent = 1;

        public bool Validate(out string error)
        {
            error = null;
            if (ItemCount < 1 || ItemCount > 6) { error = "广播条数 1..6"; return false; }
            if (FakeMin < 0 || FakeMax < FakeMin) { error = "假数范围非法"; return false; }
            if (FakeMax > ItemCount) { error = "假数不能超过条数"; return false; }
            if (IntervalSeconds != 3) { error = "广播间隔固定为 3 秒"; return false; }
            if (LastHoldSeconds != 3) { error = "末条停留固定为 3 秒"; return false; }
            if (RumorChance < 0f || RumorChance > 1f) { error = "风声概率 0..1"; return false; }
            if (ExactRankPercent < 0 || ExactRankPercent > 100) { error = "精确名次概率 0..100"; return false; }
            if (FakeMax > 2 || ZeroLieWeight < 0 || OneLieWeight < 0 || TwoLieWeight < 0) { error = "支持 0..2 条谎言及非负权重"; return false; }
            var sum = (FakeMin == 0 ? ZeroLieWeight : 0) + (FakeMin <= 1 && FakeMax >= 1 ? OneLieWeight : 0) + (FakeMax == 2 ? TwoLieWeight : 0);
            if (sum <= 0) { error = "允许范围内的谎言权重必须大于零"; return false; }
            if (NoRedactionPercent < 0 || DoubleRedactionPercent < 0 || FullRedactionPercent < 0 || NoRedactionPercent + DoubleRedactionPercent + FullRedactionPercent > 100) { error = "涂黑概率非法"; return false; }
            return true;
        }
    }
}
