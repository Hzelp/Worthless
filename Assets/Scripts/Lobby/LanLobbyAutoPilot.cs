#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.IO;
using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>
    /// 仅 Development Build / 编辑器可用的验收自动驾驶。方向通过 SeatInputSource 注入，
    /// 与正常键盘输入走同一采样/发送调度；日志用于第一手量化（每进程 -logFile 独立文件）。
    /// </summary>
    public sealed class LanLobbyAutoPilot : MonoBehaviour
    {
        private LanLobbyController controller;
        private SeatInputSource inputSource;
        private bool hostMode;
        private int startTier;
        private int requiredReal;
        private float waitSeconds;
        private Vector2 moveDirection;
        private float moveSeconds;
        private float dropAfterSeconds = -1f;
        private float reconnectAfterSeconds = -1f;
        private string credOutPath;
        private string credInPath;
        private Vector2 secondDirection = Vector2.zero;
        private float secondAtSeconds = -1f;
        private float secondSeconds = 1f;
        private float endAtSeconds = -1f;
        private float leaveAtSeconds = -1f;
        private int rehostTier;
        private double endElapsed = -1.0;

        private float elapsed;
        private float startedAt = -1f;
        private bool startRequested;
        private bool moveApplied;
        private bool moveStopped;
        private bool secondApplied;
        private bool controllerRehost;
        private bool rehostStarted;
        private bool secondStopped;
        private bool dropped;
        private bool reconnected;
        private bool credLoaded;
        private bool credWritten;
        private bool exitMenu;
        private bool exitMenuRequested;
        private bool exitClickedAfterEnd;
        private bool reconnectStopped;
        private readonly bool waitRealReconnect = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-wait-reconnect") >= 0;
        private readonly string reconnectReadyFile = ReadyFile();
        private float waitClickAt = -1f;
        private bool waitMoveStarted;
        private bool waitMoveStopped;
        private bool leaveRequested;
        private bool leaveOnLoading;
        private bool leftOnLoading;
        private float logTimer;

        private static string ReadyFile()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-worthless-wait-reconnect");
            if (i < 0 || i + 1 >= args.Length) return string.Empty;
            var value = args[i + 1];
            return string.IsNullOrEmpty(value) || value.StartsWith("-") ? string.Empty : value;
        }

        public void Configure(LanLobbyController lobby, bool host, int tier, int readyReal, float wait, Vector2 direction,
            float moveTime, float dropAt, float reconnectAfter, string credentialOutPath, string credentialInPath,
            Vector2 direction2, float secondAt, float secondDuration, float endAt, int rehostToTier, bool toMenu, float leaveAt, bool leaveDuringLoading)
        {
            controller = lobby;
            hostMode = host;
            startTier = tier;
            requiredReal = readyReal < 1 ? 1 : readyReal;
            waitSeconds = wait;
            moveDirection = direction;
            moveSeconds = moveTime;
            dropAfterSeconds = dropAt;
            reconnectAfterSeconds = reconnectAfter;
            credOutPath = credentialOutPath;
            credInPath = credentialInPath;
            secondDirection = direction2;
            secondAtSeconds = secondAt;
            secondSeconds = secondDuration;
            endAtSeconds = endAt;
            rehostTier = rehostToTier;
            exitMenu = toMenu;
            leaveAtSeconds = leaveAt;
            leaveOnLoading = leaveDuringLoading;
        }

        private void Update()
        {
            if (controller == null) return;
            if (inputSource == null) inputSource = Object.FindObjectOfType<SeatInputSource>();

            if (waitRealReconnect && !reconnected)
            {
                var lobbyUi = Object.FindObjectOfType<LanLobbyView>();
                var ready = string.IsNullOrEmpty(reconnectReadyFile) || System.IO.File.Exists(reconnectReadyFile);
                if (ready && controller.ReconnectAvailable && lobbyUi != null && lobbyUi.ReconnectButton != null
                    && lobbyUi.ReconnectButton.gameObject.activeInHierarchy && lobbyUi.ReconnectButton.interactable)
                {
                    reconnected = true; waitClickAt = Time.unscaledTime;
                    Debug.Log("[Auto] 传输真实失败后点击大厅重连按钮 available=" + controller.ReconnectAvailable);
                    lobbyUi.ReconnectButton.onClick.Invoke();
                }
            }
            if (waitRealReconnect && reconnected && waitClickAt >= 0f)
            {
                if (!waitMoveStarted && Time.unscaledTime >= waitClickAt + 4f) { waitMoveStarted = true; if (inputSource != null) inputSource.SetDevDirection(Vector2.right); Debug.Log("[Auto] 重连后恢复移动 right"); }
                if (waitMoveStarted && !waitMoveStopped && Time.unscaledTime >= waitClickAt + 7f) { waitMoveStopped = true; if (inputSource != null) inputSource.SetDevDirection(Vector2.zero); Debug.Log("[Auto] 重连后停止移动"); }
            }

            var match = controller.Match;
            elapsed += Time.unscaledDeltaTime;
            if (match == null)
            {
                HandleOfflineReconnect();
                return;
            }

            if (!credLoaded && !string.IsNullOrEmpty(credInPath) && File.Exists(credInPath))
            {
                credLoaded = true;
                var payload = File.ReadAllBytes(credInPath);
                controller.DevInjectCredential(payload);
                Debug.Log("[DevCred] 注入凭证并尝试同席接管 bytes=" + payload.Length);
                controller.RequestReconnect();
            }

            if (!credWritten && !hostMode && !string.IsNullOrEmpty(credOutPath) && GameSessionCache.HasCredential)
            {
                try
                {
                    File.WriteAllBytes(credOutPath, GameSessionCache.Payload);
                    credWritten = true;
                    Debug.Log("[DevCred] 已导出本机凭证 bytes=" + GameSessionCache.Payload.Length);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("[DevCred] 导出失败：" + exception.Message);
                }
            }

            if (hostMode && !startRequested && (match.RealCount >= requiredReal || elapsed >= waitSeconds))
            {
                startRequested = true;
                if (startTier > 0) controller.RequestSetTargetSeats(startTier);
                var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                if (lobbyView != null && lobbyView.StartButton != null) lobbyView.StartButton.onClick.Invoke();
                else controller.RequestStartMatch();
                Debug.Log("[Auto] 房主点击开赛按钮 tier=" + startTier + " real=" + match.RealCount + " required=" + requiredReal);
            }

            if (hostMode && endAtSeconds >= 0f && startedAt >= 0f && endElapsed < 0.0 && elapsed >= startedAt + endAtSeconds)
            {
                endElapsed = elapsed;
                Debug.Log("[Auto] 房主点击结束对局按钮（保留连接，回联机房间）");
                var hud = Object.FindObjectOfType<MatchHudView>();
                if (match.Started && hud != null && hud.ActionButton != null) hud.ActionButton.onClick.Invoke();
                else if (match.Started) match.RequestEndMatch();
                else controller.RequestShutdown();
            }

            if (hostMode && endElapsed >= 0.0 && !exitClickedAfterEnd && !match.Started && !match.GameplayLoading && elapsed >= endElapsed + 3f)
            {
                exitClickedAfterEnd = true;
                var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                if (lobbyView != null && lobbyView.ExitButton != null && lobbyView.ExitButton.gameObject.activeInHierarchy && lobbyView.ExitButton.interactable)
                {
                    Debug.Log("[Auto] 结束对局回房后点击退出/断开真实按钮");
                    lobbyView.ExitButton.onClick.Invoke();
                }
                else Debug.Log("[Auto] 退出按钮不可用，保留连接等待人工");
            }

            if (hostMode && endElapsed >= 0.0 && rehostTier > 0)
            {
                if (elapsed >= endElapsed + 4f && !rehostStarted && !match.Started && !match.GameplayLoading)
                {
                    rehostStarted = true;
                    controller.RequestSetTargetSeats(rehostTier);
                    controller.RequestStartMatch();
                    Debug.Log("[Auto] 新局开赛 tier=" + rehostTier);
                }
            }

            if (match.Started)
            {
                if (startedAt < 0f) startedAt = elapsed;

                if (!moveApplied && elapsed >= startedAt + 1f && moveDirection != Vector2.zero)
                {
                    moveApplied = true;
                    if (inputSource != null) inputSource.SetDevDirection(moveDirection);
                    Debug.Log("[Auto] 开始移动 dir=" + moveDirection + " seat=" + match.LocalSeat);
                }

                if (moveApplied && !moveStopped && elapsed >= startedAt + 1f + moveSeconds)
                {
                    moveStopped = true;
                    if (inputSource != null) inputSource.SetDevDirection(Vector2.zero);
                    Debug.Log("[Auto] 停止移动 seat=" + match.LocalSeat + " " + Snapshots(match));
                }

                if (secondDirection != Vector2.zero && secondAtSeconds >= 0f)
                {
                    if (!secondApplied && elapsed >= startedAt + secondAtSeconds)
                    {
                        secondApplied = true;
                        if (inputSource != null) inputSource.SetDevDirection(secondDirection);
                        Debug.Log("[Auto] 第二段移动 dir=" + secondDirection);
                    }

                    if (secondApplied && !secondStopped && elapsed >= startedAt + secondAtSeconds + secondSeconds)
                    {
                        secondStopped = true;
                        if (inputSource != null) inputSource.SetDevDirection(Vector2.zero);
                        Debug.Log("[Auto] 第二段停止 " + Snapshots(match));
                    }
                }

            }

            logTimer += Time.unscaledDeltaTime;
            if (logTimer >= 0.5f)
            {
                logTimer = 0f;
                var notice = match.Notice ?? string.Empty;
                if (notice.Length > 18) notice = notice.Substring(0, 18);
                Debug.Log("[Auto] tick t=" + elapsed.ToString("0.000") + " seat=" + match.LocalSeat
                    + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                    + " gameplay=" + UnityEngine.SceneManagement.SceneManager.GetSceneByName(SceneNames.Gameplay).isLoaded
                    + " started=" + match.Started + " rooms=" + match.RoomCount
                    + " activeRooms=" + match.ActiveRoomCount + " notice=" + notice + " " + Snapshots(match));
            }

            if (leaveAtSeconds >= 0f && !leaveRequested && match.Started && startedAt >= 0f && elapsed >= startedAt + leaveAtSeconds)
            {
                leaveRequested = true;
                Debug.Log("[Auto] 客机点击退出对局按钮");
                var hud = Object.FindObjectOfType<MatchHudView>();
                if (hud != null && hud.ActionButton != null) hud.ActionButton.onClick.Invoke();
                else controller.ReturnToLobby();
            }

            var listening = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (leaveOnLoading && !leftOnLoading && match.GameplayLoading)
            {
                leftOnLoading = true;
                Debug.Log("[Auto] 加载窗口内离开（真实断开流程）");
                controller.ReturnToLobby();
            }

            if (exitMenu && !exitMenuRequested && !listening && !match.GameplayLoading && startedAt >= 0f)
            {
                exitMenuRequested = true;
                Debug.Log("[Auto] 点击返回主菜单按钮");
                var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                if (lobbyView != null && lobbyView.MainMenuButton != null) lobbyView.MainMenuButton.onClick.Invoke();
                else controller.ReturnToMainMenu();
            }

            if (string.IsNullOrEmpty(credInPath))
            {
                if (!dropped && dropAfterSeconds >= 0f && startedAt >= 0f && elapsed >= startedAt + dropAfterSeconds)
                {
                    dropped = true;
                    if (inputSource != null) inputSource.SetDevDirection(Vector2.zero);
                    Debug.Log("[Auto] 模拟掉线（传输中断，保留席位凭证）");
                    if (Unity.Netcode.NetworkManager.Singleton != null) Unity.Netcode.NetworkManager.Singleton.Shutdown();
                }

                if (dropped && !reconnected && reconnectAfterSeconds >= 0f && startedAt >= 0f
                    && elapsed >= startedAt + dropAfterSeconds + reconnectAfterSeconds)
                {
                    reconnected = true;
                    Debug.Log("[Auto] 点击重连按钮（HUD/大厅真实按钮）");
                    var hud = Object.FindObjectOfType<MatchHudView>();
                    var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                    if (hud != null && hud.ReconnectButton != null && hud.ReconnectButton.gameObject.activeInHierarchy)
                    {
                        hud.ReconnectButton.onClick.Invoke();
                    }
                    else if (lobbyView != null && lobbyView.ReconnectButton != null && lobbyView.ReconnectButton.gameObject.activeInHierarchy)
                    {
                        lobbyView.ReconnectButton.onClick.Invoke();
                    }
                    else
                    {
                        controller.RequestReconnect();
                    }
                }

                if (reconnected && inputSource != null && startedAt >= 0f
                    && elapsed >= startedAt + dropAfterSeconds + reconnectAfterSeconds + 3f
                    && elapsed < startedAt + dropAfterSeconds + reconnectAfterSeconds + 5f)
                {
                    inputSource.SetDevDirection(moveDirection);
                }

                if (reconnected && !reconnectStopped && startedAt >= 0f
                    && elapsed >= startedAt + dropAfterSeconds + reconnectAfterSeconds + 5f)
                {
                    reconnectStopped = true;
                    if (inputSource != null) inputSource.SetDevDirection(Vector2.zero);
                    Debug.Log("[Auto] 重连后停止 " + Snapshots(match));
                }
            }
        }

        /// <summary>权威对象随客户端断线被 NGO 销毁后，仍要能从大厅用真实按钮重连。</summary>
        private void HandleOfflineReconnect()
        {
            if (!dropped && dropAfterSeconds >= 0f && startedAt >= 0f && elapsed >= startedAt + dropAfterSeconds)
            {
                dropped = true;
                if (inputSource != null) inputSource.SetDevDirection(Vector2.zero);
                Debug.Log("[Auto] 模拟掉线（传输中断，保留席位凭证）");
                if (Unity.Netcode.NetworkManager.Singleton != null) Unity.Netcode.NetworkManager.Singleton.Shutdown();
            }

            if (dropped && !reconnected && reconnectAfterSeconds >= 0f && startedAt >= 0f
                && elapsed >= startedAt + dropAfterSeconds + reconnectAfterSeconds)
            {
                reconnected = true;
                Debug.Log("[Auto] 点击重连按钮（HUD/大厅真实按钮）");
                var hud = Object.FindObjectOfType<MatchHudView>();
                var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                if (hud != null && hud.ReconnectButton != null && hud.ReconnectButton.gameObject.activeInHierarchy) hud.ReconnectButton.onClick.Invoke();
                else if (lobbyView != null && lobbyView.ReconnectButton != null && lobbyView.ReconnectButton.gameObject.activeInHierarchy) lobbyView.ReconnectButton.onClick.Invoke();
                else controller.RequestReconnect();
            }

            var listening = Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening;
            if (exitMenu && !exitMenuRequested && !listening && startedAt >= 0f)
            {
                exitMenuRequested = true;
                Debug.Log("[Auto] 点击返回主菜单按钮");
                var lobbyView = Object.FindObjectOfType<LanLobbyView>();
                if (lobbyView != null && lobbyView.MainMenuButton != null) lobbyView.MainMenuButton.onClick.Invoke();
                else controller.ReturnToMainMenu();
            }
        }

        private static string Snapshots(MatchAuthority match)
        {
            var text = string.Empty;
            for (var i = 0; i < match.TargetSeats; i++)
            {
                var s = match.GetSeatSnapshot(i);
                text += "P" + (i + 1) + "=" + s.X.ToString("0.00") + "," + s.Y.ToString("0.00")
                    + ",vx" + s.VX.ToString("0.00") + (s.IsOnline ? "+" : "-") + " ";
            }

            return text;
        }
    }
}
#endif