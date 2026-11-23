using UnityEngine;

namespace Worthless
{
    /// <summary>交易可调配置（Inspector 可编辑；NPC 接受率默认 50%）。</summary>
    [CreateAssetMenu(menuName = "Worthless/TradeConfig", fileName = "TradeConfig")]
    public sealed class TradeConfigAsset : ScriptableObject
    {
        [Header("NPC 固定话术")]
        public string NpcFriendlyText = "客气协商";
        public string NpcNeutralText = "直接询问";
        public string NpcHostileText = "施压";

        [Header("NPC 接受概率")]
        [Range(0f, 1f)] public float NpcFriendlyProbability = 0.65f;
        [Range(0f, 1f)] public float NpcAcceptProbability = 0.5f;
        [Range(0f, 1f)] public float NpcHostileProbability = 0.35f;

        public bool Validate(out string error)
        {
            error = null;
            if (!Worthless.Lobby.TradeRules.IsValidProbability(NpcFriendlyProbability)
                || !Worthless.Lobby.TradeRules.IsValidProbability(NpcAcceptProbability)
                || !Worthless.Lobby.TradeRules.IsValidProbability(NpcHostileProbability))
            {
                error = "NPC 三档接受率必须在 0..1";
                return false;
            }
            if (NpcFriendlyProbability < NpcAcceptProbability || NpcAcceptProbability < NpcHostileProbability)
            {
                error = "NPC 接受率必须满足 有利 ≥ 中性 ≥ 不利";
                return false;
            }
            if (string.IsNullOrWhiteSpace(NpcFriendlyText) || string.IsNullOrWhiteSpace(NpcNeutralText) || string.IsNullOrWhiteSpace(NpcHostileText))
            {
                error = "NPC 三档话术不能为空";
                return false;
            }
            return true;
        }

        private void OnValidate()
        {
            NpcFriendlyProbability = Mathf.Clamp01(NpcFriendlyProbability);
            NpcAcceptProbability = Mathf.Clamp01(NpcAcceptProbability);
            NpcHostileProbability = Mathf.Clamp01(NpcHostileProbability);
        }
    }
}