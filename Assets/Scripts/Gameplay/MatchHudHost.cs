using UnityEngine;

namespace Worthless
{
    /// <summary>
    /// Gameplay 场景自带的 HUD 宿主：HUD 根随地图加载/卸载，不寄居在大厅持久对象上。
    /// 大厅只负责断线提示与重连入口。
    /// </summary>
    public sealed class MatchHudHost : MonoBehaviour
    {
        private static MatchHudView current;

        public static MatchHudView Hud
        {
            get
            {
                if (current == null) current = null;
                return current;
            }
        }

        private void Awake()
        {
            current = MatchHudView.Create(transform);
            current.SetVisible(false);
        }

        private void OnDestroy()
        {
            if (current != null) current = null;
        }
    }
}