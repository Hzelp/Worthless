using UnityEngine;

namespace Worthless
{
    /// <summary>Gameplay 场景托管回合 HUD（随地图卸载，不寄居大厅）。</summary>
    public sealed class RoundHudHost : MonoBehaviour
    {
        [SerializeField, Min(1)] private int chatMaxLines = 6;
        [SerializeField, Min(8)] private int chatFontSize = 16;
        [SerializeField, Min(80f)] private float chatHeight = 180f;
        private void Awake()
        {
            // 独立预览（无网络会话）不创建对局 HUD，避免空面板遮挡地图。
            if (FindObjectOfType<Unity.Netcode.NetworkManager>() == null) { Debug.Log("[HUD] host skipped: no NetworkManager"); return; }
            Debug.Log("[HUD] host awake");
            RoundHudView.Create(transform);
            try { TradeHudView.Create(transform); Debug.Log("[HUD] TradeHud created"); } catch (System.Exception e) { Debug.LogException(e); }
            try { BroadcastHudView.Create(transform); Debug.Log("[HUD] BroadcastHud created"); } catch (System.Exception e) { Debug.LogException(e); }
            gameObject.AddComponent<NameplateController>();
            ChatHudView.Create(transform, FindObjectOfType<MatchRoundDirector>(), FindObjectOfType<MatchAuthority>(), chatMaxLines, chatFontSize, chatHeight);
            KeyBindingPanel.Ensure();
            Debug.Log("[HUD] NameplateController mounted");
        }
    }
}
