using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Worthless
{
    /// <summary>
    /// 仅编辑器/Development 的命令行入口：从默认 MainMenu 走正常入口 → LanLobby → 本地/房主流程。
    /// 所有自动化都调用真实按钮回调，不直接切场景、不伪造网络。
    /// </summary>
    public static class LanLobbyBootstrap
    {
        private const string HostArgument = "-worthless-lobby-host";
        private const string JoinArgument = "-worthless-lobby-join";
        private const string PortArgument = "-worthless-lobby-port";
        private const string AutoStartArgument = "-worthless-auto-start";
        private const string AutoWaitArgument = "-worthless-auto-wait";
        private const string AutoReadyArgument = "-worthless-auto-ready";
        private const string AutoMoveArgument = "-worthless-auto-move";
        private const string AutoMoveSecondsArgument = "-worthless-auto-move-seconds";
        private const string AutoDropArgument = "-worthless-auto-drop";
        private const string AutoReconnectArgument = "-worthless-auto-reconnect";
        private const string DevCredentialOutArgument = "-worthless-dev-cred-out";
        private const string DevCredentialInArgument = "-worthless-dev-cred-in";
        private const string Move2Argument = "-worthless-auto-move2";
        private const string Move2YArgument = "-worthless-auto-move2-y";
        private const string Move2AtArgument = "-worthless-auto-move2-at";
        private const string Move2SecondsArgument = "-worthless-auto-move2-seconds";
        private const string AutoEndArgument = "-worthless-auto-end";
        private const string AutoRehostArgument = "-worthless-auto-rehost";
        private const string AutoExitMenuArgument = "-worthless-auto-exit-menu";
        private const string AutoLeaveArgument = "-worthless-auto-leave";
        private const string AutoLeaveLoadingArgument = "-worthless-auto-leave-loading";
        private const string DevLoadHoldArgument = "-worthless-load-hold";

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplyCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            var isHost = System.Array.IndexOf(args, HostArgument) >= 0;
            var joinTarget = ReadValue(args, JoinArgument);
            var credentialIn = ReadValue(args, DevCredentialInArgument);
            var autoSearch = System.Array.IndexOf(args, "-worthless-auto-search") >= 0;
            if (!isHost && string.IsNullOrEmpty(joinTarget) && string.IsNullOrEmpty(credentialIn) && !autoSearch)
            {
                return;
            }

            var runnerObject = new GameObject("LanLobbyBootstrap");
            Object.DontDestroyOnLoad(runnerObject);
            var runner = runnerObject.AddComponent<LanLobbyBootstrapRunner>();
            runner.Configure(
                isHost,
                joinTarget,
                ReadValue(args, PortArgument),
                ReadInt(args, AutoStartArgument, 0),
                ReadInt(args, AutoReadyArgument, 1),
                ReadFloat(args, AutoWaitArgument, 4f),
                ReadValue(args, AutoMoveArgument),
                ReadFloat(args, AutoMoveSecondsArgument, 2f),
                ReadFloat(args, AutoDropArgument, -1f),
                ReadFloat(args, AutoReconnectArgument, -1f),
                ReadValue(args, DevCredentialOutArgument),
                credentialIn,
                ReadInt(args, Move2Argument, 0),
                ReadInt(args, Move2YArgument, 0),
                ReadFloat(args, Move2AtArgument, -1f),
                ReadFloat(args, Move2SecondsArgument, 1f),
                ReadFloat(args, AutoEndArgument, -1f),
                ReadInt(args, AutoRehostArgument, 0),
                ReadFloat(args, AutoLeaveArgument, -1f),
                System.Array.IndexOf(args, AutoLeaveLoadingArgument) >= 0,
                System.Array.IndexOf(args, AutoExitMenuArgument) >= 0);
        }

        private static string ReadValue(string[] args, string key)
        {
            var index = System.Array.IndexOf(args, key);
            if (index < 0 || index + 1 >= args.Length) return null;
            return args[index + 1];
        }

        private static float ReadFloat(string[] args, string key, float fallback)
        {
            var raw = ReadValue(args, key);
            float value;
            return float.TryParse(raw, out value) ? value : fallback;
        }

        private static int ReadInt(string[] args, string key, int fallback)
        {
            var raw = ReadValue(args, key);
            int value;
            return int.TryParse(raw, out value) ? value : fallback;
        }
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public sealed class LanLobbyBootstrapRunner : MonoBehaviour
    {
        private bool isHost;
        private string joinTarget;
        private string port;
        private int autoStartTier;
        private int autoReady;
        private float autoWait;
        private string autoMove;
        private float autoMoveSeconds;
        private float autoDrop;
        private float autoReconnect;
        private string credentialOut;
        private string credentialIn;
        private int move2Value;
        private int move2Y;
        private float move2At;
        private float move2Seconds;
        private float autoEnd;
        private int autoRehost;
        private bool exitMenu;
        private float autoLeave;
        private bool leaveDuringLoading;

        public void Configure(bool host, string target, string portValue, int startTier, int readyReal, float wait,
            string move, float moveSeconds, float drop, float reconnect, string credOut, string credIn,
            int move2, int move2YValue, float secondAt, float secondSeconds, float endAt, int rehostTier, float leaveAt, bool leaveLoading, bool toMenu)
        {
            isHost = host;
            joinTarget = target;
            port = portValue;
            autoStartTier = startTier;
            autoReady = readyReal;
            autoWait = wait;
            autoMove = move;
            autoMoveSeconds = moveSeconds;
            autoDrop = drop;
            autoReconnect = reconnect;
            credentialOut = credOut;
            credentialIn = credIn;
            move2Value = move2;
            move2Y = move2YValue;
            move2At = secondAt;
            move2Seconds = secondSeconds;
            autoEnd = endAt;
            autoRehost = rehostTier;
            autoLeave = leaveAt;
            leaveDuringLoading = leaveLoading;
            exitMenu = toMenu;
        }

        private IEnumerator Start()
        {
            yield return null;

            // 默认从 MainMenu 起跑：走真实按钮回调进入联机房间。
            var loadDeadline = Time.unscaledTime + 15f;
            while (SceneManager.GetActiveScene().name != SceneNames.LanLobby && Time.unscaledTime < loadDeadline)
            {
                var menu = Object.FindObjectOfType<MainMenuController>();
                if (!TransitionOverlay.IsPlaying && menu != null && !string.IsNullOrEmpty(menu.gameObject.scene.name))
                {
                    var lanButton = menu.LanButton;
                    if (lanButton != null && lanButton.isActiveAndEnabled && lanButton.interactable)
                    {
                        Debug.Log("[LanLobby] 入口：MainMenu.LanButton.onClick active=" + lanButton.isActiveAndEnabled + " interactable=" + lanButton.interactable);
                        lanButton.onClick.Invoke();
                    }
                    else
                    {
                        Debug.Log("[LanLobby] 入口回退：MainMenu.RequestLanLobby（按钮不可用）");
                        menu.RequestLanLobby();
                    }
                }

                yield return null;
            }

            LanLobbyController controller = null;
            var controllerDeadline = Time.unscaledTime + 5f;
            while (controller == null && Time.unscaledTime < controllerDeadline)
            {
                controller = Object.FindObjectOfType<LanLobbyController>();
                yield return null;
            }

            if (controller == null)
            {
                Debug.LogWarning("[LanLobby] 命令行入口未找到 LanLobbyController。");
                yield break;
            }

            var args = System.Environment.GetCommandLineArgs();
            MatchRoundDirector.DevShortRounds = System.Array.IndexOf(args, "-worthless-short-rounds") >= 0;
            var valuesIndex = System.Array.IndexOf(args, "-worthless-dev-values");
            MatchRoundDirector.DevForcedValues = valuesIndex >= 0 && valuesIndex + 1 < args.Length ? args[valuesIndex + 1] : null;
            var seedIndex = System.Array.IndexOf(args, "-worthless-dev-seed");
            var seedValue = 0;
            if (seedIndex >= 0 && seedIndex + 1 < args.Length) int.TryParse(args[seedIndex + 1], out seedValue);
            MatchRoundDirector.DevSeedOverride = seedValue;
            if (System.Array.IndexOf(args, "-worthless-trade-send") >= 0 ||
                System.Array.IndexOf(args, "-worthless-trade-respond") >= 0)
                controller.gameObject.AddComponent<TradeDevDriver>();

            yield return null;
            yield return null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var holdArgs = System.Environment.GetCommandLineArgs();
            var holdIndex = System.Array.IndexOf(holdArgs, "-worthless-load-hold");
            var holdSeconds = 0f;
            if (holdIndex >= 0 && holdIndex + 1 < holdArgs.Length) float.TryParse(holdArgs[holdIndex + 1], out holdSeconds);
            Worthless.MatchAuthority.DevLoadHoldSeconds = holdSeconds;
#endif
            if (isHost)
            {
                Debug.Log("[LanLobby] 命令行入口：开房。");
                controller.RequestHost();
            }
            else if (!string.IsNullOrEmpty(credentialIn))
            {
                var takeoverPort = string.IsNullOrEmpty(port) ? controller.DefaultTargetPort : ushort.Parse(port);
                controller.DevSetReconnectTarget(joinTarget, takeoverPort);
                Debug.Log("[LanLobby] 命令行入口：同席接管（凭证注入，不进入大厅） target=" + joinTarget);
            }
            else if (!string.IsNullOrEmpty(joinTarget))
            {
                var targetPort = string.IsNullOrEmpty(port) ? controller.DefaultTargetPort.ToString() : port;
                Debug.Log("[LanLobby] 命令行入口：加入 " + joinTarget + ":" + targetPort);
                controller.RequestJoin(joinTarget, targetPort);
            }

            var hasAuto = autoStartTier > 0 || !string.IsNullOrEmpty(autoMove) || autoDrop >= 0f
                || !string.IsNullOrEmpty(credentialIn) || !string.IsNullOrEmpty(credentialOut)
                || leaveDuringLoading || autoLeave >= 0f
                || autoEnd >= 0f || autoRehost > 0 || exitMenu
                || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-wait-reconnect") >= 0;
            if (!hasAuto) yield break;

            yield return new WaitForSeconds(1f);

            var direction = Vector2.zero;
            if (!string.IsNullOrEmpty(autoMove))
            {
                var parts = autoMove.Split(',');
                float x;
                float y;
                if (parts.Length == 2 && float.TryParse(parts[0], out x) && float.TryParse(parts[1], out y)) direction = new Vector2(x, y);
            }

            var secondDirection = Vector2.zero;
            if (move2Value != 0 || move2Y != 0)
            {
                secondDirection = new Vector2(move2Value == 0 ? 0f : Mathf.Sign(move2Value), move2Y == 0 ? 0f : Mathf.Sign(move2Y));
            }

            var auto = gameObject.AddComponent<LanLobbyAutoPilot>();
            auto.Configure(controller, isHost, autoStartTier, autoReady, autoWait, direction, autoMoveSeconds,
                autoDrop, autoReconnect, credentialOut, credentialIn, secondDirection, move2At, move2Seconds,
                autoEnd, autoRehost, exitMenu, autoLeave, leaveDuringLoading);
            Debug.Log("[Auto] 自动驾驶已启用 host=" + isHost + " dir=" + direction + " startTier=" + autoStartTier
                + " drop=" + autoDrop + " reconnect=" + autoReconnect + " end=" + autoEnd + " rehost=" + autoRehost);
        }
    }
#endif
}
