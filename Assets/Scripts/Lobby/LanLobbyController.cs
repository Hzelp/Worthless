using System.Collections;
using System.Collections.Generic;
using System.Net;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>
    /// 入口大厅：连接生命周期 + 对局 UI/重连入口。网络规则在 MatchAuthority 中。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager))]
    public sealed class LanLobbyController : MonoBehaviour
    {
        [Header("监听（房主开房 / 客机本地绑定）")]
        [SerializeField] private GameObject matchPrefab;

        [SerializeField] private string listenAddress = "0.0.0.0";
        [SerializeField, Range(1, 65535)] private ushort listenPort = 7777;

        [Header("默认目标（客机加入）")]
        [SerializeField] private string defaultTargetAddress = "127.0.0.1";
        [SerializeField, Range(1, 65535)] private ushort defaultTargetPort = 7777;

        [Header("连接")]
        [SerializeField, Min(1f)] private float connectTimeoutSeconds = 6f;
        [SerializeField, Min(1f)] private float startTimeoutSeconds = 3f;

        private const int DisconnectTimeoutMilliseconds = 8000;
        private const int ConnectTimeoutMilliseconds = 3000;
        private const float ResultDisplaySeconds = 12f;

        private static readonly Color ColorIdle = ArtTheme.Text;
        private static readonly Color ColorOk = ArtTheme.Teal;
        private static readonly Color ColorFail = new Color(0.95f, 0.42f, 0.38f, 1f);

        private NetworkManager networkManager;
        private UnityTransport transport;
        private LanLobbyView view;
        private MatchAuthority match;
        private SeatInputSource inputSource;
        private CameraFollow2D cameraFollow;
        private Camera lobbyCamera;
        private bool returningToMenu;
        private MatchHudView boundHud;
        private readonly LanLobbyRoster roster = new LanLobbyRoster();

        private LanLobbyState state = LanLobbyState.Idle;
        private float connectDeadline;
        private float startDeadline;
        private bool subscribed;
        private bool shutdownRequested;
        private string pendingFailure;
        private string lastMessage;
        private string lastFailure;
        private string lastTargetAddress;
        private ushort lastTargetPort;
        private bool reconnectAvailable;
        private bool preserveSessionForReconnect;
        private LanDiscoveryService discovery;
        private string pendingRoomName = RoomNameRules.Default;
        private bool hostDiscoveryStarted;
        private float nextDiscoveryHostAttempt;
        private string pendingPlayerName = PlayerNameRules.Default;
        private bool localNameSent;
        private bool reconnectingSeat;
        private bool soloMode;
        private bool presentingGameplay;
        private bool observingGameplayTransition;
        private bool returningToLobby;
        private float returnToMenuAt;

        public LanLobbyState State { get { return state; } }
        public string ListenAddress { get { return listenAddress; } }
        public ushort ListenPort { get { return listenPort; } }
        public string DefaultTargetAddress { get { return defaultTargetAddress; } }
        public ushort DefaultTargetPort { get { return defaultTargetPort; } }
        public NetworkManager Manager { get { return networkManager; } }
        public MatchAuthority Match { get { return match; } }
        public bool ReconnectAvailable { get { return reconnectAvailable; } }

        private void Awake()
        {
            Application.runInBackground = true;
            networkManager = GetComponent<NetworkManager>();
            transport = GetComponent<UnityTransport>();
            discovery = GetComponent<LanDiscoveryService>();
            if (discovery == null) discovery = gameObject.AddComponent<LanDiscoveryService>();
            if (transport == null && networkManager != null)
            {
                transport = networkManager.gameObject.AddComponent<UnityTransport>();
            }

            if (networkManager != null && networkManager.NetworkConfig != null)
            {
                if (networkManager.NetworkConfig.NetworkTransport == null && transport != null)
                {
                    networkManager.NetworkConfig.NetworkTransport = transport;
                }

                networkManager.NetworkConfig.ConnectionApproval = true;
                RegisterMatchPrefab();
            }

            if (transport != null)
            {
                transport.DisconnectTimeoutMS = DisconnectTimeoutMilliseconds;
                transport.ConnectTimeoutMS = ConnectTimeoutMilliseconds;
            }
        }

        private void Start()
        {
            if (networkManager == null)
            {
                Debug.LogError("[LanLobby] 缺少同物体的 NetworkManager。", this);
                return;
            }

            match = FindObjectOfType<MatchAuthority>();
            EnsureInputSource();
            BindHud();

            lobbyCamera = Camera.main;
            cameraFollow = null;
            Subscribe();

            if (view == null)
            {
                view = LanLobbyView.Create(transform);
                view.HostRequested += HandleHostRequested;
                view.CreateRoomRequested += RequestHost;
                view.SearchRoomsRequested += HandleSearchRequested;
                view.JoinRoomRequested += HandleJoinRoomRequested;
                view.TradeTimesRequested += HandleTradeTimesRequested;
                view.TotalRoundsRequested += HandleTotalRoundsRequested;
                view.PlayerNameEdited += SetLocalPlayerName;
                view.JoinRequested += HandleJoinRequested;
                view.ExitRequested += HandleExitRequested;
                view.RefreshRequested += HandleRefreshRequested;
                view.TargetSeatsRequested += HandleTargetSeatsRequested;
                view.StartMatchRequested += HandleStartMatchRequested;
                view.MainMenuRequested += ReturnToMainMenu;
                view.ReconnectRequested += RequestReconnect;
                view.SetEndpointFields(defaultTargetAddress, defaultTargetPort.ToString());
                pendingPlayerName = PlayerNameRules.Normalize(PlayerNameStore.Load());
                view.SetPlayerName(pendingPlayerName);
                view.SetPages(true, false);
                if (discovery != null) discovery.Changed += RefreshDiscoveryResults;
                if (SoloLaunch.Pending) { SoloLaunch.Pending = false; RequestSolo(); }
                if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-auto-search") >= 0 && !networkManager.IsServer)
                    StartCoroutine(DevSearchJoin());
            }

            if (state == LanLobbyState.Idle && !networkManager.IsListening)
            {
                SetState(LanLobbyState.Idle, string.Empty);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (discovery != null) { discovery.Changed -= RefreshDiscoveryResults; discovery.StopHost(); discovery.StopBrowse(); }
        }

        private void Update()
        {
            if (networkManager == null || returningToMenu) return;
            if (match == null) match = FindObjectOfType<MatchAuthority>();
            EnsureInputSource();
            if (MatchFinished && returnToMenuAt <= 0f)
                returnToMenuAt = Time.unscaledTime + ResultDisplaySeconds;
            if (returnToMenuAt > 0f && Time.unscaledTime >= returnToMenuAt)
            {
                ReturnToMainMenu();
                return;
            }

            PublishLocalPlayerName();

            if (networkManager.IsServer && networkManager.IsListening && match != null)
            {
                if (match.RoomName != pendingRoomName) match.ServerSetRoomName(pendingRoomName);
                if (!soloMode && !hostDiscoveryStarted && discovery != null && Time.unscaledTime >= nextDiscoveryHostAttempt)
                {
                    hostDiscoveryStarted = discovery.StartHost(pendingRoomName, listenPort);
                    nextDiscoveryHostAttempt = Time.unscaledTime + 5f;
                }
                if (!soloMode && discovery != null)
                {
                    discovery.HostName = match.RoomName;
                    discovery.Tier = match.TargetSeats;
                    discovery.Real = match.RealCount;
                    discovery.Started = match.Started;
                }
            }

            if (state == LanLobbyState.Stopping)
            {
                if (!networkManager.IsListening && !networkManager.ShutdownInProgress) CompleteShutdown();
            }
            else if (state == LanLobbyState.StartingHost && Time.unscaledTime >= startDeadline && !networkManager.IsListening)
            {
                BeginFailureShutdown("开房失败：端口可能被占用，或监听地址不可用。");
            }
            else if (state == LanLobbyState.Connecting && Time.unscaledTime >= connectDeadline)
            {
                BeginFailureShutdown("连接超时：请确认房主已开房、IP 与端口正确。");
            }

            RefreshMatchUi();
        }

        // ---------------- 显式 API ----------------

        public void RequestHost()
        {
            var args = System.Environment.GetCommandLineArgs();
            var i = System.Array.IndexOf(args, "-worthless-room-name");
            RequestHost(i >= 0 && i + 1 < args.Length ? args[i + 1] : null);
        }

        public void RequestSolo()
        {
            if (networkManager == null || networkManager.IsListening) return;
            soloMode = true;
            returnToMenuAt = 0f;
            listenAddress = "127.0.0.1";
            listenPort = FreeLoopbackPort();
            pendingRoomName = "单人游戏";
            if (view != null)
            {
                view.SetSoloMode();
                view.SetPages(false, true);
            }
            Debug.Log("[Solo] loopback host port=" + listenPort);
            RequestHost("单人游戏");
        }

        private static ushort FreeLoopbackPort()
        {
            try
            {
                var probe = new System.Net.Sockets.UdpClient(0);
                var port = (ushort)((System.Net.IPEndPoint)probe.Client.LocalEndPoint).Port;
                probe.Close();
                if (port == LanDiscoveryService.DiscoveryPort) return 7801;
                return port;
            }
            catch { return 7801; }
        }

        public void RequestHost(string roomName)
        {
            CapturePlayerName();
            pendingRoomName = RoomNameRules.Normalize(roomName);
            if (!LanLobbyStateRules.CanStartHost(state)) return;
            localNameSent = reconnectingSeat = false;
            nextDiscoveryHostAttempt = 0f;
            if (networkManager == null || transport == null)
            {
                SetState(LanLobbyState.Failed, "场景缺少 NetworkManager 或 UnityTransport。");
                return;
            }

            string bindAddress;
            string endpoint;
            string error;
            if (!TryGetListenEndpoint(out bindAddress, out endpoint, out error))
            {
                SetState(LanLobbyState.Failed, "监听配置无效：" + error);
                return;
            }

            if (match != null) match.PrepareServerSession();
            discovery?.StopBrowse();
            transport.SetConnectionData(bindAddress, listenPort, bindAddress);
            roster.Clear();
            GameSessionCache.Clear();
            reconnectAvailable = false;
            preserveSessionForReconnect = false;
            shutdownRequested = false;
            pendingFailure = null;
            lastFailure = null;
            startDeadline = Time.unscaledTime + startTimeoutSeconds;

            SetState(LanLobbyState.StartingHost, "正在开房，监听 " + endpoint + " …");
            if (!networkManager.StartHost())
            {
                BeginFailureShutdown("启动房主失败，请查看 Console。");
            }
        }

        public void RequestJoin(string addressText, string portText)
        {
            CapturePlayerName();
            if (!LanLobbyStateRules.CanJoin(state)) return;
            localNameSent = reconnectingSeat = false;
            if (networkManager == null || transport == null)
            {
                SetState(LanLobbyState.Failed, "场景缺少 NetworkManager 或 UnityTransport。");
                return;
            }

            string address;
            ushort port;
            string error;
            if (!EndpointRules.TryValidateJoinTarget(addressText, portText, out address, out port, out error))
            {
                SetState(LanLobbyState.Failed, error);
                return;
            }

            string bindAddress;
            string endpoint;
            string bindError;
            if (!TryGetListenEndpoint(out bindAddress, out endpoint, out bindError))
            {
                SetState(LanLobbyState.Failed, "监听配置无效：" + bindError);
                return;
            }

            if (discovery != null) discovery.StopBrowse();
            transport.SetConnectionData(address, port, bindAddress);
            networkManager.NetworkConfig.ConnectionData = new byte[0];
            lastTargetAddress = address;
            lastTargetPort = port;
            GameSessionCache.Clear();
            reconnectAvailable = false;
            preserveSessionForReconnect = false;
            lastFailure = null;
            connectDeadline = Time.unscaledTime + connectTimeoutSeconds;
            shutdownRequested = false;
            pendingFailure = null;

            SetState(LanLobbyState.Connecting, "正在连接 " + EndpointRules.Format(address, port) + " …");
            if (!networkManager.StartClient())
            {
                BeginFailureShutdown("启动客机失败，请查看 Console。");
            }
        }

        /// <summary>用进程内缓存的席位凭证回到同一席位。</summary>
        public void RequestReconnect()
        {
            if (!GameSessionCache.HasCredential)
            {
                SetState(LanLobbyState.Failed, "没有可用的席位凭证，请返回大厅重新加入。");
                return;
            }

            if (!LanLobbyStateRules.CanJoin(state)) return;
            if (networkManager == null || transport == null) return;
            if (string.IsNullOrEmpty(lastTargetAddress))
            {
                SetState(LanLobbyState.Failed, "缺少上次连接的地址，请返回大厅重新加入。");
                return;
            }

            string bindAddress;
            string endpoint;
            string bindError;
            if (!TryGetListenEndpoint(out bindAddress, out endpoint, out bindError))
            {
                SetState(LanLobbyState.Failed, "监听配置无效：" + bindError);
                return;
            }

            transport.SetConnectionData(lastTargetAddress, lastTargetPort, bindAddress);
            networkManager.NetworkConfig.ConnectionData = GameSessionCache.Payload;
            reconnectingSeat = true;
            connectDeadline = Time.unscaledTime + connectTimeoutSeconds;
            shutdownRequested = false;
            pendingFailure = null;

            SetState(LanLobbyState.Connecting, "正在重连到 " + EndpointRules.Format(lastTargetAddress, lastTargetPort) + " …");
            if (!networkManager.StartClient())
            {
                BeginFailureShutdown("重连启动失败，请查看 Console。");
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>仅验收使用：为外部凭证指定重连目标（正常路径由上次加入地址提供）。</summary>
        public void DevSetReconnectTarget(string address, ushort port)
        {
            if (string.IsNullOrEmpty(address) || port == 0) return;
            lastTargetAddress = address;
            lastTargetPort = port;
        }

        /// <summary>仅验收使用：把外部提供的席位凭证注入本进程缓存；不提供任何读取他人凭证的接口。</summary>
        public void DevInjectCredential(byte[] payload)
        {
            if (payload == null || payload.Length == 0) return;
            GameSessionCache.StoreRaw(payload);
        }
#endif

        public void RequestShutdown()
        {
            if (!LanLobbyStateRules.CanShutdown(state)) return;

            pendingFailure = null;
            shutdownRequested = true;

            if (match != null && match.Started && networkManager != null && networkManager.IsServer)
            {
                match.AnnounceHostLeaving("房主结束了本局");
                SetState(LanLobbyState.Stopping, "正在结束对局…");
                StartCoroutine(ShutdownAfterDelay(0.25f));
                return;
            }

            SetState(LanLobbyState.Stopping, "正在退出…");
            ShutdownNetwork();
            if (networkManager == null || (!networkManager.IsListening && !networkManager.ShutdownInProgress))
            {
                CompleteShutdown();
            }
        }

        public void ReturnToLobby()
        {
            if (soloMode) { ReturnToMainMenu(); return; }
            if (returningToLobby) return;
            returningToLobby = true;
            TransitionOverlay.Cover(SceneNames.Gameplay, ReturnToLobbyCovered());
        }

        private IEnumerator ReturnToLobbyCovered()
        {
            if (this == null) yield break;
            GameSessionCache.Clear();
            reconnectAvailable = false;
            preserveSessionForReconnect = false;
            hostDiscoveryStarted = false;
            if (discovery != null) { discovery.StopHost(); discovery.StopBrowse(); }
            if (match != null) match.ResetClientView();
            var unload = StartCoroutine(UnloadGameplayLocal());

            if (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress))
            {
                pendingFailure = null;
                shutdownRequested = true;
                SetState(LanLobbyState.Stopping, "正在退出对局…");
                ShutdownNetwork();
                var deadline = Time.unscaledTime + 3f;
                while (networkManager != null && (networkManager.IsListening || networkManager.ShutdownInProgress) && Time.unscaledTime < deadline)
                    yield return null;
            }
            else CompleteShutdown();
            yield return unload;
            returningToLobby = false;
        }

        public void RequestSetTargetSeats(int tier)
        {
            if (match == null || networkManager == null || !networkManager.IsServer) return;
            match.RequestSetTargetSeats(tier);
        }

        public void RequestStartMatch()
        {
            if (match == null || networkManager == null || !networkManager.IsServer) return;
            CapturePlayerName();
            PublishLocalPlayerName();
            match.RequestStartMatch();
        }

        public void RefreshLocalAddressDisplay()
        {
            if (view != null) view.SetDetails(BuildDetails());
        }

        private IEnumerator ShutdownAfterDelay(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            ShutdownNetwork();
            if (networkManager == null || (!networkManager.IsListening && !networkManager.ShutdownInProgress))
            {
                CompleteShutdown();
            }
        }

        // ---------------- 内部 ----------------

        private bool TryGetListenEndpoint(out string addressText, out string endpoint, out string error)
        {
            addressText = string.IsNullOrEmpty(listenAddress) ? EndpointRules.WildcardAddress : listenAddress.Trim();
            endpoint = null;
            error = null;

            IPAddress parsed;
            if (!EndpointRules.TryParseIPv4(addressText, out parsed, out error))
            {
                error = "监听地址 \"" + addressText + "\" 不是合法 IPv4";
                return false;
            }

            if (listenPort < EndpointRules.MinPort)
            {
                error = "监听端口必须非零（1-65535）";
                return false;
            }

            addressText = parsed.ToString();
            endpoint = EndpointRules.Format(addressText, listenPort);
            return true;
        }

        private string CurrentEndpointText()
        {
            string addressText;
            string endpoint;
            string error;
            if (TryGetListenEndpoint(out addressText, out endpoint, out error)) return endpoint;
            return listenAddress + ":" + listenPort + "（无效：" + error + "）";
        }

        private void BeginFailureShutdown(string message)
        {
            pendingFailure = message;
            shutdownRequested = true;
            SetState(LanLobbyState.Stopping, message + " 正在关闭连接…");
            ShutdownNetwork();
        }

        private void CompleteShutdown()
        {
            roster.Clear();
            StartCoroutine(UnloadGameplayLocal());
            if (lobbyCamera != null) lobbyCamera.enabled = true;
            hostDiscoveryStarted = false;
            if (discovery != null) { discovery.StopHost(); discovery.StopBrowse(); }
            if (match != null) match.ResetForNewSession();
            if (preserveSessionForReconnect && GameSessionCache.HasCredential) reconnectAvailable = true;
            else { GameSessionCache.Clear(); reconnectAvailable = false; }
            if (!reconnectAvailable) lastFailure = null;
            if (boundHud != null) boundHud.SetVisible(false);
            if (view != null) view.SetLobbyPanelVisible(true);

            var failure = pendingFailure;
            pendingFailure = null;
            shutdownRequested = false;

            if (!string.IsNullOrEmpty(failure))
            {
                SetState(LanLobbyState.Failed, failure);
            }
            else
            {
                SetState(LanLobbyState.Idle, "已断开。");
            }
        }

        private void ShutdownNetwork()
        {
            if (networkManager != null && networkManager.IsListening) networkManager.Shutdown();
        }

        private void Subscribe()
        {
            if (subscribed || networkManager == null) return;
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnServerStopped += HandleServerStopped;
            networkManager.OnClientStopped += HandleClientStopped;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || networkManager == null) return;
            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnServerStopped -= HandleServerStopped;
            networkManager.OnClientStopped -= HandleClientStopped;
            networkManager.OnClientConnectedCallback -= HandleClientConnected;
            networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            subscribed = false;
        }

        private void CapturePlayerName()
        {
            if (view != null) SetLocalPlayerName(view.PlayerName);
        }

        private void SetLocalPlayerName(string value)
        {
            var name = PlayerNameRules.Normalize(value);
            view?.SetPlayerName(name);
            if (name == pendingPlayerName) return;
            pendingPlayerName = name;
            PlayerNameStore.Save(name);
            localNameSent = false;
            PublishLocalPlayerName();
        }

        private void PublishLocalPlayerName()
        {
            if (localNameSent || reconnectingSeat || match == null || !match.IsSpawned || networkManager == null ||
                !networkManager.IsListening || networkManager.ShutdownInProgress) return;
            if (networkManager.IsServer) match.ServerSetHostName(pendingPlayerName);
            else if (networkManager.IsConnectedClient) match.SendLocalPlayerName(pendingPlayerName);
            else return;
            localNameSent = true;
            Debug.Log("[Name] local seat=" + match.LocalSeat + " name=" + pendingPlayerName);
        }

        private void HandleHostRequested() { RequestHost(); }

        private void HandleTotalRoundsRequested(int rounds)
        {
            if (networkManager != null && networkManager.IsServer && match != null && match.RoundDirector != null)
                match.RoundDirector.ServerSetTotalRounds(rounds);
        }

        private void HandleTradeTimesRequested(int first, int second, int third)
        {
            if (networkManager == null || !networkManager.IsServer || match == null || match.Started) return;
            var director = match.RoundDirector;
            if (director == null) return;
            director.ServerSetTradeTimes(first, second, third);
            Debug.Log("[Config] trade times " + director.FirstTradeSeconds + "/" + director.SecondTradeSeconds + "/" + director.ThirdTradeSeconds);
        }

        private void HandleSearchRequested(string filter)
        {
            if (discovery == null || (state != LanLobbyState.Idle && state != LanLobbyState.Failed)) return;
            discovery.StartBrowse();
            RefreshDiscoveryResults();
            SetState(LanLobbyState.Idle, discovery.Browsing ? "正在搜索局域网房间…" : "无法启动房间搜索，可直接输入房主 IPv4 加入。");
        }

        private void HandleJoinRoomRequested(string address, ushort port)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (discovery != null) discovery.StopBrowse();
            RequestJoin(address, port.ToString());
        }

        private System.Collections.IEnumerator DevSearchJoin()
        {
            yield return new WaitForSecondsRealtime(2f);
            HandleSearchRequested(string.Empty);
            yield return new WaitForSecondsRealtime(3f);
            RefreshDiscoveryResults();
            foreach (var room in discovery.Results)
                Debug.Log("[SearchProbe] found name=" + room.n + " address=" + room.Address + " port=" + room.p + " tier=" + room.t);
            if (view != null && view.ClickFirstResult()) Debug.Log("[SearchProbe] clicked first ResultButton");
            else Debug.Log("[SearchProbe] no rooms or no sized result button");
        }

        private void RefreshDiscoveryResults()
        {
            if (view == null || discovery == null) return;
            var rooms = new List<LanRoomInfo>(discovery.Results);
            rooms.Sort((a, b) => string.Compare(a.n, b.n, System.StringComparison.Ordinal));
            view.SetDiscoveryResults(rooms, view.SearchFilter);
        }

        private void HandleJoinRequested()
        {
            if (view != null) RequestJoin(view.TargetAddress, view.TargetPort);
        }

        private void HandleExitRequested()
        {
            if (soloMode) ReturnToMainMenu();
            else RequestShutdown();
        }

        private void HandleRefreshRequested() { RefreshLocalAddressDisplay(); }

        private void HandleTargetSeatsRequested(int tier) { RequestSetTargetSeats(tier); }

        private void HandleStartMatchRequested() { RequestStartMatch(); }

        private void BindHud()
        {
            var target = MatchHudHost.Hud;
            if (target == boundHud) return;

            if (boundHud != null)
            {
                boundHud.ActionRequested -= HandleHudAction;
                boundHud.ReconnectRequested -= RequestReconnect;
                boundHud.BackToLobbyRequested -= ReturnToLobby;
            }

            boundHud = target;
            if (boundHud != null)
            {
                boundHud.ActionRequested += HandleHudAction;
                boundHud.ReconnectRequested += RequestReconnect;
                boundHud.BackToLobbyRequested += ReturnToLobby;
            }
        }

        private bool MatchFinished => match != null && match.Started &&
            (match.GameOver || (match.RoundDirector != null && match.RoundDirector.MatchOver));

        private void HandleHudAction()
        {
            if (soloMode || MatchFinished || returnToMenuAt > 0f)
            {
                ReturnToMainMenu();
                return;
            }
            if (match != null && match.Started && networkManager != null && networkManager.IsServer)
            {
                match.RequestEndMatch();
                return;
            }

            if (networkManager != null && networkManager.IsServer) RequestShutdown();
            else ReturnToLobby();
        }

        /// <summary>离场回主菜单：断开连接并拆掉持久 NetworkManager，再单场景加载 MainMenu。</summary>
        public void ReturnToMainMenu()
        {
            if (returningToMenu) return;
            returningToMenu = true;
            soloMode = false;
            reconnectAvailable = false;
            preserveSessionForReconnect = false;
            GameSessionCache.Clear();

            SetState(LanLobbyState.Stopping, "正在返回主菜单…");
            // NetworkManager 在 Awake 即进入 DontDestroyOnLoad；无论是否在监听都必须显式销毁，
            // 否则 MainMenu→LanLobby 再次进入会出现第二个 NetworkManager 与第二套 Canvas。
            if (networkManager != null)
            {
                var manager = networkManager;
                SceneFlowLoader.LoadAfterNetworkTeardown(manager, SceneNames.MainMenu);
                return;
            }

            TransitionOverlay.LoadScene(SceneNames.MainMenu);
        }

        private System.Collections.IEnumerator UnloadGameplayLocal()
        {
            var scene = SceneManager.GetSceneByName(SceneNames.Gameplay);
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var operation = SceneManager.UnloadSceneAsync(scene);
            while (operation != null && !operation.isDone) yield return null;
        }

        private void RegisterMatchPrefab()
        {
            if (matchPrefab == null || networkManager == null || networkManager.NetworkConfig == null) return;
            var prefabs = networkManager.NetworkConfig.Prefabs;
            if (prefabs == null) return;
            foreach (var existing in prefabs.Prefabs)
            {
                if (existing != null && existing.Prefab == matchPrefab) return;
            }

            prefabs.Add(new Unity.Netcode.NetworkPrefab { Prefab = matchPrefab });
            Debug.Log("[LanLobby] MatchNet prefab 已注册");
        }

        private void EnsureInputSource()
        {
            if (match == null) return;
            if (inputSource == null) inputSource = gameObject.AddComponent<SeatInputSource>();
            inputSource.Configure(match);
        }

        private void SpawnMatchAuthority()
        {
            if (networkManager == null || !networkManager.IsServer) return;
            if (FindObjectOfType<MatchAuthority>() != null) return;
            if (matchPrefab == null)
            {
                Debug.LogError("[LanLobby] 缺少 MatchNet prefab，无法创建权威状态。");
                return;
            }

            var instance = Instantiate(matchPrefab);
            var networkObject = instance.GetComponent<Unity.Netcode.NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError("[LanLobby] MatchNet prefab 缺少 NetworkObject。");
                Destroy(instance);
                return;
            }

            networkObject.Spawn();
            match = instance.GetComponent<MatchAuthority>();
            EnsureInputSource();
            if (match != null) match.OnServerSessionStarted();
        }

        private void HandleServerStarted()
        {
            if (networkManager == null) return;
            roster.Clear();
            SpawnMatchAuthority();
            if (match != null) match.OnServerSessionStarted();
            SetState(LanLobbyState.Listening, "已开房，正在监听 " + CurrentEndpointText() + "；等待客机加入。");
        }

        private void HandleServerStopped(bool wasHost)
        {
            if (shutdownRequested || state == LanLobbyState.Stopping)
            {
                CompleteShutdown();
                return;
            }

            roster.Clear();
            if (match != null) match.ResetForNewSession();
            SetState(LanLobbyState.Failed, "服务端已停止，可重试。");
        }

        private void HandleClientStopped(bool wasHost)
        {
            if (wasHost) return;

            if (shutdownRequested || state == LanLobbyState.Stopping)
            {
                CompleteShutdown();
                return;
            }

            if (state == LanLobbyState.Connecting || state == LanLobbyState.Joined)
            {
                MarkConnectionLost("连接已停止");
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (networkManager == null) return;

            if (networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId) return;
                roster.Add(clientId);
                if (match != null && match.Started) return;

                var total = roster.Count + (networkManager.IsHost ? 1 : 0);
                SetState(LanLobbyState.HostWithClient, "客机已加入（ClientId=" + clientId + "），当前 " + total + " 人在线。");
                return;
            }

            if (clientId != networkManager.LocalClientId) return;

            reconnectAvailable = false;
            localNameSent = reconnectingSeat;
            reconnectingSeat = false;
            var reconnected = match != null && match.Started && match.IsLocalPlayerSeated;
            SetState(LanLobbyState.Joined, reconnected
                ? "已重连回席位（ClientId=" + clientId + "）。"
                : "已加入房间（ClientId=" + clientId + "）。");
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (networkManager == null) return;

            if (networkManager.IsServer)
            {
                if (clientId == networkManager.LocalClientId) return;

                var wasKnown = roster.Remove(clientId);
                if (!wasKnown) return;
                if (shutdownRequested || state == LanLobbyState.Stopping) return;
                if (match != null && match.Started) return;

                var total = roster.Count + (networkManager.IsHost ? 1 : 0);
                if (roster.Count > 0)
                {
                    SetState(LanLobbyState.HostWithClient, "客机已离开（ClientId=" + clientId + "），当前 " + total + " 人在线。");
                }
                else
                {
                    SetState(LanLobbyState.Listening, "客机已离开（ClientId=" + clientId + "），等待客机加入。");
                }
                return;
            }

            if (shutdownRequested || state == LanLobbyState.Stopping) return;
            if (clientId != networkManager.LocalClientId) return;

            if (state == LanLobbyState.Connecting || state == LanLobbyState.Joined)
            {
                var reason = networkManager.DisconnectReason;
                MarkConnectionLost(string.IsNullOrEmpty(reason) ? "连接结束：服务端未响应或已关闭" : reason);
            }
        }

        private void MarkConnectionLost(string reason)
        {
            if (MatchFinished || returnToMenuAt > 0f)
            {
                ReturnToMainMenu();
                return;
            }
            StartCoroutine(UnloadGameplayLocal());
            if (match != null)
            {
                var serverReason = match.TakeServerReason();
                if (!string.IsNullOrEmpty(serverReason)) reason = serverReason;
            }
            lastFailure = reason;
            var sessionEnded = match != null && match.GameOver;
            var inMatch = GameSessionCache.HasCredential && !string.IsNullOrEmpty(lastTargetAddress) && !sessionEnded;
            if (inMatch) preserveSessionForReconnect = true;
            reconnectAvailable = inMatch;
            SetState(LanLobbyState.Failed, inMatch
                ? "连接中断：" + reason + "。可重试旧会话，或返回大厅重新加入。"
                : "连接结束：" + reason + "。可重试加入。");
        }

        private void SetState(LanLobbyState next, string message)
        {
            state = next;
            lastMessage = message;
            if (view == null) return;

            var color = ColorIdle;
            if (next == LanLobbyState.Failed) color = ColorFail;
            else if (LanLobbyStateRules.IsConnected(next)) color = ColorOk;

            view.SetStatus(LanLobbyStateRules.Describe(next), message, color);
            view.SetButtonsEnabled(
                LanLobbyStateRules.CanStartHost(next),
                LanLobbyStateRules.CanJoin(next),
                LanLobbyStateRules.CanShutdown(next));
            view.SetDetails(BuildDetails());
            Debug.Log("[LanLobby] state=" + next + " :: " + message);
        }

        private string BuildDetails()
        {
            var online = "未连接";
            switch (state)
            {
                case LanLobbyState.StartingHost:
                case LanLobbyState.Listening:
                case LanLobbyState.HostWithClient:
                    var total = roster.Count + (networkManager != null && networkManager.IsHost ? 1 : 0);
                    online = "在线人数: " + total;
                    break;
                case LanLobbyState.Joined:
                    online = "已作为客机连接（ClientId=" + (networkManager != null ? networkManager.LocalClientId : 0) + "）";
                    break;
                case LanLobbyState.Connecting:
                    online = "正在连接…";
                    break;
                case LanLobbyState.Stopping:
                    online = "正在关闭连接…";
                    break;
                default:
                    online = "在线人数: 0";
                    break;
            }

            return LocalAddresses.DescribeLanAddresses()
                + "\n监听地址: " + CurrentEndpointText()
                + "\n" + online;
        }

        private void RefreshMatchUi()
        {
            if (view == null) return;
            BindHud();
            var listening = networkManager != null && networkManager.IsListening;
            var gameplayReady = SceneManager.GetSceneByName(SceneNames.Gameplay).isLoaded;
            var inMatch = match != null && gameplayReady && match.Started && listening;
            if (listening && !networkManager.IsServer && !returningToLobby && match != null && !observingGameplayTransition &&
                (match.GameplayLoading || presentingGameplay != inMatch))
            {
                observingGameplayTransition = true;
                TransitionOverlay.Cover(SceneNames.Gameplay, ObserveGameplayTransition(match));
            }
            if (!listening || TransitionOverlay.Covers(SceneNames.Gameplay)) presentingGameplay = inMatch;
            inMatch = presentingGameplay;
            if (lobbyCamera != null) lobbyCamera.enabled = !inMatch;
            view.SetLobbyPanelVisible(!inMatch);
            var roomVisible = !inMatch && (soloMode || (listening &&
                (state == LanLobbyState.Listening || state == LanLobbyState.HostWithClient || state == LanLobbyState.Joined)));
            view.SetPages(!inMatch && !roomVisible, roomVisible);

            if (!inMatch && roomVisible)
            {
                var isHost = networkManager != null && networkManager.IsServer;
                var roomName = match != null && !string.IsNullOrEmpty(match.RoomName) ? match.RoomName : pendingRoomName;
                var address = isHost ? HostAdvertisedAddress() : EndpointRules.Format(lastTargetAddress, lastTargetPort);
                var status = match != null ? match.Notice : lastMessage;
                if (match != null && match.GameplayLoading) status = match.Notice;
                else if (match != null && listening) status = soloMode ? string.Empty : BuildRoomRoster();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (Time.frameCount % 60 == 0) Debug.Log("[RoomUI] isHost=" + isHost + " name=" + roomName + " address=" + address + " members=" + status);
#endif
                view.SetRoom(roomName, isHost, address, status, match != null ? match.TargetSeats : 5,
                    match != null ? match.RealCount : (roster.Count + (isHost ? 1 : 0)),
                    isHost && match != null && match.CanStartNow);
                if (match != null && match.RoundDirector != null)
                {
                    view.SetTradeTimes(match.RoundDirector.FirstTradeSeconds, match.RoundDirector.SecondTradeSeconds,
                        match.RoundDirector.ThirdTradeSeconds, isHost && !match.Started);
                    view.SetTotalRounds(match.RoundDirector.TotalRounds, isHost && !match.Started && !match.GameplayLoading);
                }
                view.SetReconnectVisible(false);
            }
            else if (!inMatch)
            {
                if (match != null)
                {
                    view.SetLobbyInfo(match.BuildLobbySummary());
                    var isHost = networkManager != null && networkManager.IsServer;
                    view.SetMatchControls(isHost, match.TargetSeats, match.CanStartNow, match.Notice);
                }
                else
                {
                    view.SetLobbyInfo(reconnectAvailable
                        ? "连接已中断，可尝试重连原席位。"
                        : state == LanLobbyState.Failed ? "连接已结束，请返回大厅重新加入。"
                        : "加入已有房间时，可搜索房间名称或输入房主地址。");
                    view.SetMatchControls(false, 5, false, lastFailure ?? string.Empty);
                }
                view.SetReconnectVisible(reconnectAvailable && !listening);
            }

            var hud = MatchHudHost.Hud;
            if (hud != null)
            {
                hud.SetVisible(inMatch);
                if (match == null || !inMatch) return;
                hud.SetNotice(reconnectAvailable && !string.IsNullOrEmpty(lastFailure) ? lastFailure : match.Notice);
                var host = networkManager != null && networkManager.IsServer;
                hud.SetActions(soloMode || MatchFinished ? "返回主菜单" : host ? "结束对局" : "退出对局", listening, false);
            }

            if (match == null) return;
            if (inMatch)
            {
                if (cameraFollow == null) cameraFollow = match.CameraFollow;
                if (cameraFollow != null)
                {
                    if (match.IsLocalSpectator)
                    {
                        var target = match.SpectatorTargetTransform;
                        if (target == null) { match.CycleSpectatorTarget(1); target = match.SpectatorTargetTransform; }
                        if (target != null) cameraFollow.SetTarget(target);
                    }
                    else if (match.IsLocalPlayerSeated)
                    {
                        var actor = match.GetSeatActor(match.LocalSeat);
                        if (actor != null) cameraFollow.SetTarget(actor.transform);
                    }
                }
            }
        }

        private IEnumerator ObserveGameplayTransition(MatchAuthority observed)
        {
            while (observed != null && networkManager != null && networkManager.IsListening && observed.GameplayLoading)
                yield return null;
            yield return null;
            observingGameplayTransition = false;
        }

        private string HostAdvertisedAddress()
        {
            if (!string.IsNullOrEmpty(listenAddress) && listenAddress != "0.0.0.0" && listenAddress != "::")
                return EndpointRules.Format(listenAddress, listenPort);
            var list = LocalAddresses.CollectIPv4(false);
            return list.Count > 0 ? EndpointRules.Format(list[0], listenPort) : EndpointRules.Format("127.0.0.1", listenPort);
        }

        private string BuildRoomRoster()
        {
            if (match == null) return lastMessage;
            var names = match.LobbyMemberNames;
            var parts = new List<string>();
            for (var i = 0; i < names.Count; i++) parts.Add(names[i]);
            return (parts.Count == 0 ? "等待真人加入" : string.Join(" · ", parts.ToArray())) + (match.Started ? " · 对局中" : " · 可开始");
        }

    }
}
