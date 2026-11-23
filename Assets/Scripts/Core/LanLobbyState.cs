namespace Worthless.Lobby
{
    /// <summary>大厅连接状态。所有状态迁移由 LanLobbyController 依据 NGO 生命周期回调驱动。</summary>
    public enum LanLobbyState
    {
        Idle = 0,
        StartingHost = 1,
        Listening = 2,
        Connecting = 3,
        Joined = 4,
        HostWithClient = 5,
        Stopping = 6,
        Failed = 7
    }

    /// <summary>状态到可用操作的纯规则，保证关停完成前不能发起新会话。</summary>
    public static class LanLobbyStateRules
    {
        public static bool CanStartHost(LanLobbyState state)
        {
            return state == LanLobbyState.Idle || state == LanLobbyState.Failed;
        }

        public static bool CanJoin(LanLobbyState state)
        {
            return state == LanLobbyState.Idle || state == LanLobbyState.Failed;
        }

        public static bool CanShutdown(LanLobbyState state)
        {
            return state == LanLobbyState.StartingHost
                || state == LanLobbyState.Listening
                || state == LanLobbyState.Connecting
                || state == LanLobbyState.Joined
                || state == LanLobbyState.HostWithClient;
        }

        public static bool IsBusy(LanLobbyState state)
        {
            return state == LanLobbyState.StartingHost
                || state == LanLobbyState.Connecting
                || state == LanLobbyState.Stopping;
        }

        public static bool IsConnected(LanLobbyState state)
        {
            return state == LanLobbyState.Listening
                || state == LanLobbyState.Joined
                || state == LanLobbyState.HostWithClient;
        }

        public static string Describe(LanLobbyState state)
        {
            switch (state)
            {
                case LanLobbyState.Idle: return "未连接";
                case LanLobbyState.StartingHost: return "正在开房";
                case LanLobbyState.Listening: return "已监听（等待客机）";
                case LanLobbyState.Connecting: return "正在连接";
                case LanLobbyState.Joined: return "已加入房间";
                case LanLobbyState.HostWithClient: return "房主（已有客机）";
                case LanLobbyState.Stopping: return "正在关闭";
                case LanLobbyState.Failed: return "连接失败";
                default: return "未知状态";
            }
        }
    }
}