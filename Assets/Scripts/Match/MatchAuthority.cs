using System.Collections;
using System.Collections.Generic;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>
    /// 权威大厅与对局：席位档位、NPC 补位、开局事务、席位凭证、断线重连、权威移动与公开快照。
    /// 持久部分与内容场景解耦：本组件常驻 LanLobby 场景（NetworkManager 同场景），
    /// 正式地图通过 NGO NetworkSceneManager 以 Additive 方式加载/卸载，并在此绑定 GameplayBinder。
    /// </summary>
    public sealed partial class MatchAuthority : NetworkBehaviour
    {
        public const int SeatCount = LobbySeatingRules.MaxRealPlayers;
        public const int TokenBytes = SeatCredentialCodec.TokenBytes;
        private const float ReservationTimeoutSeconds = 8f;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>仅验收：加载完成后保持 loading 窗口的秒数（0 = 关闭）。</summary>
        public static float DevLoadHoldSeconds;
#endif

        [Header("权威移动")]
        [SerializeField, Min(0.5f)] private float moveSpeed = 5f;
        [SerializeField, Min(1f)] private float runMultiplier = 1.6f;
        [SerializeField, Min(0.02f)] private float snapshotInterval = 0.1f;

        private readonly NetworkVariable<int> targetSeats = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> realPlayers = new NetworkVariable<int>(0);
        private readonly NetworkVariable<bool> started = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<int> roomCount = new NetworkVariable<int>(0);
        private readonly NetworkVariable<uint> gameId = new NetworkVariable<uint>(0);
        private readonly NetworkVariable<bool> loadingSync = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<FixedString128Bytes> notice = new NetworkVariable<FixedString128Bytes>(default);
        private readonly NetworkVariable<FixedString64Bytes> roomName = new NetworkVariable<FixedString64Bytes>(default);
        private readonly NetworkList<FixedString64Bytes> playerNames = new NetworkList<FixedString64Bytes>();
        private readonly NetworkList<MatchLobbyMember> lobbyMembers = new NetworkList<MatchLobbyMember>();
        private readonly Dictionary<ulong, string> lobbyNames = new Dictionary<ulong, string>();
        private readonly NetworkList<SeatSnapshot> seats = new NetworkList<SeatSnapshot>();

        private struct ServerSeat
        {
            public byte[] Token;
            public ulong ClientId;
            public bool Npc;
            public bool Online;
            public Vector2 Direction;
            public bool Running;
            public float LastInput;
            public float LastAccepted;
        }

        private readonly ServerSeat[] serverSeats = new ServerSeat[SeatCount];
        private readonly List<ulong> lobbyClients = new List<ulong>();
        private readonly Dictionary<ulong, byte> pendingReconnects = new Dictionary<ulong, byte>();
        private readonly Dictionary<ulong, float> pendingApprovals = new Dictionary<ulong, float>();
        private readonly HashSet<ulong> pendingKicks = new HashSet<ulong>();

        private SeatActor[] seatActors;
        private Transform[] spawnPoints;
        private GameObject[] privateRooms;
        private CameraFollow2D cameraFollow;
        private Scene gameplayScene;
        private GameplayBinder binder;

        private float snapshotTimer;
        private bool hostPickedTier;
        private int localSeat = -1;
        private bool hooksInstalled;
        private bool needsSessionReset;
        private string pendingServerReason;
        private bool leavingMatch;
        private bool rosterLocked;
        private bool gameplayLoading;
        private bool endingMatch;
        private int pendingTier;
        private readonly List<ulong> pendingRoster = new List<ulong>();
        private readonly HashSet<int> spectatorSeats = new HashSet<int>();
        private bool gameOver;
        private bool localSpectator;
        private int spectatorTargetSeat = -1;
        private readonly Dictionary<ulong, int> spectatorTargets = new Dictionary<ulong, int>();
        private string pendingLobbyReason = "房主结束了本局";
        private MatchRoundDirector roundDirector;

        public bool IsLocalSpectator { get { return localSpectator; } }
        public int SpectatorTargetSeat { get { return spectatorTargetSeat; } }
        public string SpectatorTargetName { get { return spectatorTargetSeat >= 0 ? PlayerNameForSeat(spectatorTargetSeat) : "无目标"; } }
        public int SpectatorTargetRoom
        {
            get
            {
                if (spectatorTargetSeat < 0) return -1;
                var actor = seatActors != null && spectatorTargetSeat < seatActors.Length ? seatActors[spectatorTargetSeat] : null;
                if (actor == null) return -1;
                var director = RoundDirector;
                return director != null ? director.AuthoritativeRoomAt(actor.transform.position) : -1;
            }
        }

        public Transform SpectatorTargetTransform
        {
            get
            {
                if (spectatorTargetSeat < 0 || seatActors == null || spectatorTargetSeat >= seatActors.Length) return null;
                var actor = seatActors[spectatorTargetSeat];
                return actor != null && actor.gameObject.activeInHierarchy ? actor.transform : null;
            }
        }

        public void CycleSpectatorTarget(int direction)
        {
            if (!localSpectator) return;
            var start = spectatorTargetSeat < 0 ? 0 : spectatorTargetSeat;
            for (var step = 1; step <= SeatCount; step++)
            {
                var seat = (start + step * (direction >= 0 ? 1 : -1) + SeatCount * 2) % SeatCount;
                if (RoundDirector != null && RoundDirector.IsSeatAlive(seat) && !spectatorSeats.Contains(seat))
                {
                    SetSpectatorTarget(seat); return;
                }
            }
        }

        public void SetSpectatorTarget(int seat)
        {
            spectatorTargetSeat = seat;
            if (IsServer) { spectatorTargets[NetworkManager.LocalClientId] = seat; return; }
            SetSpectatorTargetServerRpc(seat);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SetSpectatorTargetServerRpc(int seat, ServerRpcParams rpcParams = default)
        {
            var spectator = FindSeatByClient(rpcParams.Receive.SenderClientId);
            if (spectator < 0 || !spectatorSeats.Contains(spectator)) return;
            if (RoundDirector == null || !RoundDirector.IsSeatAlive(seat)) return;
            spectatorTargets[rpcParams.Receive.SenderClientId] = seat;
        }

        public int SpectatorTargetForClient(ulong clientId)
        {
            int seat;
            return spectatorTargets.TryGetValue(clientId, out seat) ? seat : -1;
        }
        public bool GameOver { get { return gameOver; } }

        public bool IsSpectator(int seat)
        {
            return seat >= 0 && spectatorSeats.Contains(seat);
        }

        public void ClearSpectators()
        {
            spectatorSeats.Clear();
            localSpectator = false;
        }

        public void SetGameOver(bool over)
        {
            gameOver = over;
            if (!IsServer) return;
            if (over)
            {
                for (var i = 0; i < SeatCount; i++)
                {
                    var seat = serverSeats[i];
                    seat.Direction = Vector2.zero;
                    seat.LastInput = 0f;
                    seat.LastAccepted = 0f;
                    serverSeats[i] = seat;
                    SetActorFrozen(i, true);
                }
            }

            Debug.Log("[Match] GameOver=" + over + "（停止新移动）");
        }

        [ClientRpc]
        private void SetSpectatorClientRpc(int seat, bool spectator, ClientRpcParams rpcParams = default)
        {
            if (seat != localSeat) return;
            localSpectator = spectator;
            if (spectator && spectatorTargetSeat < 0)
            {
                for (var i = 0; i < SeatCount; i++)
                    if (RoundDirector != null && RoundDirector.IsSeatAlive(i) && !spectatorSeats.Contains(i)) { SetSpectatorTarget(i); break; }
            }
        }

        public MatchRoundDirector RoundDirector
        {
            get
            {
                if (roundDirector == null) roundDirector = GetComponent<MatchRoundDirector>();
                return roundDirector;
            }
        }

        /// <summary>供回合权威读取当前服务端席位（npc/online/clientId）。</summary>
        public bool TryGetSeatInfo(int index, out bool isNpc, out bool isOnline, out ulong clientId)
        {
            isNpc = false;
            isOnline = false;
            clientId = SeatBindingRules.Unbound;
            if (!IsServer || index < 0 || index >= SeatCount || index >= targetSeats.Value) return false;
            var seat = serverSeats[index];
            isNpc = seat.Npc;
            isOnline = seat.Online;
            clientId = seat.ClientId;
            return true;
        }

        public event System.Action<int> LocalSeatAssigned;

        public int TargetSeats { get { return targetSeats.Value; } }
        public int RealCount { get { return realPlayers.Value; } }
        public bool Started { get { return started.Value; } }
        public int RoomCount { get { return roomCount.Value; } }
        public uint GameId { get { return gameId.Value; } }
        public int LocalSeat { get { return localSeat; } }

        public bool TryGetLocalPlayerPosition(out Vector2 position)
        {
            position = default;
            if (seatActors == null || localSeat < 0 || localSeat >= seatActors.Length) return false;
            var actor = seatActors[localSeat];
            if (actor == null || !actor.gameObject.activeInHierarchy) return false;
            position = actor.transform.position;
            return true;
        }
        public string Notice { get { return notice.Value.ToString(); } }
        public float MoveSpeed { get { return moveSpeed; } }
        public string RoomName { get { return roomName.Value.ToString(); } }

        public IReadOnlyList<string> LobbyMemberNames
        {
            get
            {
                var list = new List<string>();
                for (var i = 0; i < lobbyMembers.Count; i++)
                {
                    var value = lobbyMembers[i].Name.ToString();
                    list.Add((lobbyMembers[i].IsHost ? "[房主] " : "") + (string.IsNullOrEmpty(value) ? "玩家" : value));
                }
                return list;
            }
        }

        public string PlayerNameForSeat(int seat)
        {
            if (seat < 0 || seat >= SeatCount) return "玩家";
            if (GetSeatSnapshot(seat).IsNpc) return "NPC " + (seat + 1);
            if (seat < playerNames.Count)
            {
                var value = playerNames[seat].ToString();
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return "P" + (seat + 1);
        }

        public void SendLocalPlayerName(string value)
        {
            if (!IsClient || !IsSpawned) return;
            SubmitPlayerNameServerRpc(PlayerNameRules.Normalize(value));
        }

        private void SyncLobbyMembers()
        {
            if (!IsServer || !IsSpawned || NetworkManager == null || NetworkManager.ShutdownInProgress) return;
            var ids = NetworkManager.ConnectedClientsIds;
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (!lobbyClients.Contains(id)) continue;
                var host = id == NetworkManager.LocalClientId;
                var found = -1;
                for (var j = 0; j < lobbyMembers.Count; j++) if (lobbyMembers[j].ClientId == id) { found = j; break; }
                var member = new MatchLobbyMember { ClientId = id, Name = new FixedString64Bytes(LobbyNameForClient(id)), IsHost = host };
                if (found < 0) lobbyMembers.Add(member);
                else if (!lobbyMembers[found].Equals(member)) lobbyMembers[found] = member;
            }
            for (var j = lobbyMembers.Count - 1; j >= 0; j--)
            {
                var id = lobbyMembers[j].ClientId;
                if (!lobbyClients.Contains(id) || !ContainsClient(ids, id)) lobbyMembers.RemoveAt(j);
            }
        }

        private string LobbyNameForClient(ulong clientId)
        {
            if (lobbyNames.TryGetValue(clientId, out var name)) return name;
            return clientId == NetworkManager.LocalClientId ? "房主" : PlayerNameRules.Default;
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitPlayerNameServerRpc(string value, ServerRpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            SetClientName(sender, value);
        }

        public void ServerSetHostName(string value)
        {
            if (NetworkManager != null) SetClientName(NetworkManager.LocalClientId, value);
        }

        private void SetClientName(ulong clientId, string value)
        {
            if (!IsServer || !IsSpawned || NetworkManager == null || !lobbyClients.Contains(clientId)) return;
            var name = PlayerNameRules.Normalize(value);
            lobbyNames[clientId] = name;
            var seat = FindSeatByClient(clientId);
            if (seat >= 0 && seat < playerNames.Count) playerNames[seat] = new FixedString64Bytes(name);
            SyncLobbyMembers();
        }

        public void ServerSetRoomName(string value)
        {
            if (!IsServer) return;
            var safe = RoomNameRules.Normalize(value);
            roomName.Value = new FixedString64Bytes(safe);
            Debug.Log("[Room] name=" + safe);
        }
        public int ConnectedSeatCount { get { return CountOnlineSeats(); } }
        public bool IsLocalPlayerSeated { get { return localSeat >= 0 && localSeat < SeatCount; } }
        public bool IsStartedForLocal { get { return started.Value && IsLocalPlayerSeated; } }
        public bool GameplayLoading { get { return gameplayLoading || loadingSync.Value; } }
        public CameraFollow2D CameraFollow { get { return cameraFollow; } }

        public int ActiveRoomCount
        {
            get
            {
                var count = 0;
                if (privateRooms == null) return 0;
                for (var i = 0; i < privateRooms.Length; i++)
                {
                    if (privateRooms[i] != null && privateRooms[i].activeSelf) count++;
                }

                return count;
            }
        }

        public bool CanStartNow
        {
            get
            {
                if (started.Value || gameplayLoading || rosterLocked || endingMatch) return false;
                string reason;
                return LobbySeatingRules.CanStart(targetSeats.Value, realPlayers.Value, out reason);
            }
        }

        public int PreviewNpcCount
        {
            get { return LobbySeatingRules.NpcPreview(targetSeats.Value, realPlayers.Value); }
        }

        /// <summary>取走服务端最近一次“被请出”的原因（供 UI 显示）。</summary>
        public string TakeServerReason()
        {
            var reason = pendingServerReason;
            pendingServerReason = null;
            return reason;
        }

        // ---------------- 生命周期 ----------------

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                EnsureApprovalHook();
                if (needsSessionReset)
                {
                    ApplySessionReset();
                    needsSessionReset = false;
                }

                SubscribeSceneEvents();
                ConfigureClientSceneSync();
                RebuildLobbyFromConnected();
                RefreshLobbyTier();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer) DetachServerCallbacks();
        }

        public override void OnDestroy()
        {
            DetachServerCallbacks();
            base.OnDestroy();
        }

        public void PrepareServerSession()
        {
            leavingMatch = false;
            rosterLocked = false;
            gameplayLoading = false;
            endingMatch = false;
            localSeat = -1;
            lobbyClients.Clear();
            pendingReconnects.Clear();
            pendingApprovals.Clear();
            hostPickedTier = false;
            needsSessionReset = true;
            EnsureApprovalHook();
        }

        public void OnServerSessionStarted()
        {
            if (!IsServer) return;
            EnsureApprovalHook();
            EnsurePlayerNames();
            if (needsSessionReset)
            {
                ApplySessionReset();
                needsSessionReset = false;
            }

            SubscribeSceneEvents();
            ConfigureClientSceneSync();
            RebuildLobbyFromConnected();
            RefreshLobbyTier();
        }

        private void EnsurePlayerNames()
        {
            if (!IsServer) return;
            for (var i = playerNames.Count; i < SeatCount; i++) playerNames.Add(default);
        }

        public void ResetForNewSession()
        {
            leavingMatch = false;
            rosterLocked = false;
            gameplayLoading = false;
            endingMatch = false;
            DetachServerCallbacks();
            localSeat = -1;
            lobbyClients.Clear();
            pendingReconnects.Clear();
            pendingApprovals.Clear();
            hostPickedTier = false;
            needsSessionReset = true;
            ClearBinding();
        }

        public void ResetClientView()
        {
            localSeat = -1;
        }

        private void EnsureApprovalHook()
        {
            if (hooksInstalled) return;
            var manager = NetworkManager != null ? NetworkManager : FindObjectOfType<NetworkManager>();
            if (manager == null) return;
            if (manager.ConnectionApprovalCallback == null) manager.ConnectionApprovalCallback = ApproveConnection;
            manager.OnClientConnectedCallback += OnClientConnectedServer;
            manager.OnClientDisconnectCallback += OnClientDisconnectedServer;
            hooksInstalled = true;
        }

        private void DetachServerCallbacks()
        {
            if (!hooksInstalled) return;
            hooksInstalled = false;
            var manager = NetworkManager != null ? NetworkManager : FindObjectOfType<NetworkManager>();
            if (manager == null) return;
            UnsubscribeSceneEvents();
            if (manager.ConnectionApprovalCallback != null) manager.ConnectionApprovalCallback = null;
            manager.OnClientConnectedCallback -= OnClientConnectedServer;
            manager.OnClientDisconnectCallback -= OnClientDisconnectedServer;
        }

        private void ConfigureClientSceneSync()
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SceneManager == null) return;
            // 客户端已常驻 LanLobby；同步必须 Additive，避免 Single 同步把大厅场景卸载/重载成第二个 NetworkManager。
            NetworkManager.SceneManager.SetClientSynchronizationMode(LoadSceneMode.Additive);
            NetworkManager.SceneManager.PostSynchronizationSceneUnloading = false;
        }

        private void SubscribeSceneEvents()
        {
            if (!IsServer || NetworkManager == null || NetworkManager.SceneManager == null) return;
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            NetworkManager.SceneManager.OnLoadEventCompleted += HandleLoadEventCompleted;
            NetworkManager.SceneManager.OnUnloadEventCompleted -= HandleUnloadEventCompleted;
            NetworkManager.SceneManager.OnUnloadEventCompleted += HandleUnloadEventCompleted;
        }

        private void UnsubscribeSceneEvents()
        {
            if (NetworkManager == null || NetworkManager.SceneManager == null) return;
            NetworkManager.SceneManager.OnLoadEventCompleted -= HandleLoadEventCompleted;
            NetworkManager.SceneManager.OnUnloadEventCompleted -= HandleUnloadEventCompleted;
        }

        private void ApplySessionReset()
        {
            lobbyNames.Clear();
            lobbyMembers.Clear();
            EnsurePlayerNames();
            for (var i = 0; i < playerNames.Count; i++)
                if (playerNames[i].Length > 0) playerNames[i] = default;
            started.Value = false;
            loadingSync.Value = false;
            targetSeats.Value = 0;
            realPlayers.Value = 0;
            roomCount.Value = 0;
            gameId.Value = 0;
            SetNotice(string.Empty);
            gameplayLoading = false;
            endingMatch = false;
            rosterLocked = false;
            ClearBinding();

            for (var i = 0; i < SeatCount; i++)
            {
                serverSeats[i] = new ServerSeat { ClientId = SeatBindingRules.Unbound };
            }

            if (seats.Count == 0)
            {
                for (var i = 0; i < SeatCount; i++) seats.Add(new SeatSnapshot { Seat = (byte)i, Flags = 0 });

            }
            else
            {
                for (var i = 0; i < SeatCount && i < seats.Count; i++)
                {
                    var empty = new SeatSnapshot { Seat = (byte)i, Flags = 0 };
                    if (!seats[i].Equals(empty)) seats[i] = empty;
                }
            }
        }

        private void RebuildLobbyFromConnected()
        {
            lobbyClients.Clear();
            var connected = NetworkManager.ConnectedClientsIds;
            for (var i = 0; i < connected.Count; i++)
            {
                if (pendingKicks.Contains(connected[i])) continue;
                if (!lobbyClients.Contains(connected[i])) lobbyClients.Add(connected[i]);
            }

            if (!lobbyClients.Contains(NetworkManager.LocalClientId)) lobbyClients.Add(NetworkManager.LocalClientId);
        }

        private int lobbyClientCountForLog()
        {
            return lobbyClients.Count;
        }

        private static bool ContainsClient(IReadOnlyList<ulong> list, ulong clientId)
        {
            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == clientId) return true;
            }

            return false;
        }

        private void RefilterLobbyFromConnected()
        {
            var connected = NetworkManager.ConnectedClientsIds;
            var hostId = NetworkManager.LocalClientId;
            for (var i = lobbyClients.Count - 1; i >= 0; i--)
            {
                var id = lobbyClients[i];
                if (id == hostId) continue;
                if (!ContainsClient(connected, id) || pendingKicks.Contains(id)) lobbyClients.RemoveAt(i);
            }

            if (!lobbyClients.Contains(hostId)) lobbyClients.Insert(0, hostId);
        }

        private void ClearBinding()
        {
            if (seatActors != null)
            {
                for (var i = 0; i < seatActors.Length; i++)
                {
                    if (seatActors[i] != null) seatActors[i].gameObject.SetActive(false);
                }
            }

            seatActors = null;
            spawnPoints = null;
            privateRooms = null;
            cameraFollow = null;
            binder = null;
            gameplayScene = default(Scene);
            RestoreLobbyActiveScene();
        }

        private void BindGameplay(GameplayBinder source)
        {
            if (source == null || binder == source) return;
            binder = source;
            seatActors = source.SeatActors;
            spawnPoints = source.SpawnPoints;
            privateRooms = source.PrivateRooms;
            cameraFollow = source.CameraFollow;
            gameplayScene = source.gameObject.scene;
            SceneManager.SetActiveScene(gameplayScene);

            for (var i = 0; i < seatActors.Length; i++)
            {
                if (seatActors[i] == null) continue;
                seatActors[i].ConfigureAuthority(IsServer);
                seatActors[i].gameObject.SetActive(false);
            }

            if (privateRooms != null)
            {
                for (var i = 0; i < privateRooms.Length; i++)
                {
                    if (privateRooms[i] != null) privateRooms[i].SetActive(false);
                }
            }

            Debug.Log("[Match] Gameplay 已绑定 seats=" + seatActors.Length + " server=" + IsServer);
        }

        // ---------------- 连接审批 ----------------

        private void ApproveConnection(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            if (leavingMatch || endingMatch || (NetworkManager != null && NetworkManager.ShutdownInProgress))
            {
                Reject(response, "房主正在结束对局，请稍后再试");
                return;
            }

            PruneReservations();

            if (!started.Value)
            {
                if (rosterLocked || gameplayLoading)
                {
                    Reject(response, "对局正在准备，暂不接受新连接");
                    return;
                }

                if (request.Payload != null && request.Payload.Length > 0)
                {
                    Reject(response, "旧局席位凭证已失效，请返回大厅重新加入");
                    return;
                }

                if (lobbyClients.Count + pendingApprovals.Count >= LobbySeatingRules.MaxRealPlayers)
                {
                    Reject(response, "大厅已满（最多 10 名真人玩家）");
                    return;
                }

                pendingApprovals[request.ClientNetworkId] = Time.unscaledTime;
                response.Approved = true;
                return;
            }

            SeatCredential credential;
            if (request.Payload == null || request.Payload.Length == 0)
            {
                Reject(response, "对局已开始，新玩家不能加入（缺少席位凭证）");
                return;
            }

            if (!SeatCredentialCodec.TryDecode(request.Payload, out credential))
            {
                Reject(response, "席位凭证无效");
                return;
            }

            if (credential.GameId != gameId.Value)
            {
                Reject(response, "席位凭证已失效（旧对局或错误局 ID）");
                return;
            }

            if (!SeatBindingRules.IsSeatIndexActive(credential.Seat, targetSeats.Value))
            {
                Reject(response, "席位凭证无效（不存在该席位）");
                return;
            }

            var seat = serverSeats[credential.Seat];
            if (seat.Npc)
            {
                Reject(response, "该席位由 NPC 占用，不能重连");
                return;
            }

            if (!SeatCredentialCodec.Matches(credential, gameId.Value, credential.Seat, seat.Token))
            {
                Reject(response, "席位凭证无效");
                return;
            }

            pendingReconnects[request.ClientNetworkId] = credential.Seat;
            response.Approved = true;
        }

        private void PruneReservations()
        {
            var now = Time.unscaledTime;
            var expired = new List<ulong>();
            foreach (var pair in pendingApprovals)
            {
                if (now - pair.Value > ReservationTimeoutSeconds) expired.Add(pair.Key);
            }

            for (var i = 0; i < expired.Count; i++) pendingApprovals.Remove(expired[i]);
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Reason = reason;
            Debug.Log("[Match] 拒绝连接：" + reason);
        }

        private void OnClientConnectedServer(ulong clientId)
        {
            if (!IsServer || NetworkManager == null) return;
            if (NetworkManager.ShutdownInProgress || !IsSpawned) return;
            if (leavingMatch)
            {
                NetworkManager.DisconnectClient(clientId, "房主正在结束对局");
                return;
            }

            pendingApprovals.Remove(clientId);

            byte seat;
            if (pendingReconnects.TryGetValue(clientId, out seat))
            {
                pendingReconnects.Remove(clientId);
                BindReconnect(seat, clientId);
                return;
            }

            if (started.Value || rosterLocked)
            {
                NetworkManager.DisconnectClient(clientId, "对局已开始，本连接未分配席位");
                return;
            }

            if (lobbyClients.Contains(clientId)) return;
            if (lobbyClients.Count >= LobbySeatingRules.MaxRealPlayers)
            {
                NetworkManager.DisconnectClient(clientId, "大厅已满（最多 10 名真人玩家）");
                return;
            }

            lobbyClients.Add(clientId);
            RefreshLobbyTier();
        }

        private void BindReconnect(byte seatIndex, ulong clientId)
        {
            var seat = serverSeats[seatIndex];
            var previousClient = seat.ClientId;
            var wasOnline = seat.Online;

            seat.ClientId = clientId;
            seat.Online = true;
            seat.Direction = Vector2.zero;
            seat.LastInput = 0f;
            seat.LastAccepted = 0f;
            serverSeats[seatIndex] = seat;
            pendingKicks.Remove(clientId);
            if (previousClient != SeatBindingRules.Unbound) lobbyClients.Remove(previousClient);
            if (!lobbyClients.Contains(clientId)) lobbyClients.Add(clientId);
            if (seatIndex < playerNames.Count && playerNames[seatIndex].Length > 0)
                lobbyNames[clientId] = playerNames[seatIndex].ToString();
            SyncLobbyMembers();
            SetActorFrozen(seatIndex, gameOver || IsSpectator(seatIndex));

            if (SeatBindingRules.NeedsOldDisconnect(wasOnline, previousClient, clientId))
            {
                SendServerReason(previousClient, "该席位已由新连接接管");
                if (IsServer) StartCoroutine(KickLater(previousClient, "该席位已由新连接接管"));
            }

            SendSeatAssigned(seatIndex, clientId);
            if (IsSpectator(seatIndex))
            {
                var spectatorTarget = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
                SetSpectatorClientRpc(seatIndex, true, spectatorTarget);
            }
            if (RoundDirector != null) RoundDirector.OnSeatReconnected(seatIndex);
            PublishSnapshot();
            SetNotice("客机已回到席位 " + (seatIndex + 1));
        }

        private void OnClientDisconnectedServer(ulong clientId)
        {
            if (!IsServer || NetworkManager == null) return;
            pendingKicks.Remove(clientId);
            // 关房时 NGO 已拆 BehaviourUpdater：此时再写 NetworkVariable 会 NRE。
            if (NetworkManager.ShutdownInProgress || !IsSpawned) return;

            pendingReconnects.Remove(clientId);
            pendingApprovals.Remove(clientId);
            lobbyNames.Remove(clientId);

            if (!started.Value)
            {
                lobbyClients.Remove(clientId);
                if (!rosterLocked) RefreshLobbyTier();
                else SyncLobbyMembers();
                return;
            }

            for (var i = 0; i < SeatCount; i++)
            {
                var seat = serverSeats[i];
                if (!SeatBindingRules.ShouldClearOnDisconnect(seat.Online, seat.ClientId, clientId)) continue;

                seat.Online = false;
                seat.ClientId = SeatBindingRules.Unbound;
                seat.Direction = Vector2.zero;
                seat.LastInput = 0f;
                seat.LastAccepted = 0f;
                serverSeats[i] = seat;
                if (RoundDirector != null) RoundDirector.OnSeatDisconnected(i);
                SetActorFrozen(i, true);
                SetNotice("席位 " + (i + 1) + " 掉线，位置已冻结，等待重连");
            }

            PublishSnapshot();
        }

        private void SetActorFrozen(int seatIndex, bool frozen)
        {
            if (seatActors == null || seatIndex < 0 || seatIndex >= seatActors.Length) return;
            var actor = seatActors[seatIndex];
            if (actor == null || actor.Body == null) return;

            actor.Body.velocity = Vector2.zero;
            actor.Body.angularVelocity = 0f;
            actor.Body.constraints = frozen ? RigidbodyConstraints2D.FreezeAll : RigidbodyConstraints2D.FreezeRotation;
        }

        // ---------------- 大厅档位与开局 ----------------

        private void RefreshLobbyTier()
        {
            SyncLobbyMembers();
            realPlayers.Value = lobbyClients.Count;
            if (!hostPickedTier || targetSeats.Value < lobbyClients.Count)
            {
                targetSeats.Value = LobbySeatingRules.DefaultTier(lobbyClients.Count);
            }

            if (lobbyClients.Count == 0)
            {
                SetNotice("等待真人玩家加入");
            }
            else if (targetSeats.Value > 0 && lobbyClients.Count > targetSeats.Value)
            {
                SetNotice("真人玩家（" + lobbyClients.Count + "）超过当前档位（" + targetSeats.Value + "），请选择更大的档位再开赛");
            }
            else
            {
                SetNotice(string.Empty);
            }
        }

        public void RequestSetTargetSeats(int tier)
        {
            if (!IsServer || started.Value || rosterLocked) return;
            if (!LobbySeatingRules.CanStart(tier, lobbyClients.Count, out var reason))
            {
                SetNotice(reason);
                return;
            }

            hostPickedTier = true;
            targetSeats.Value = tier;
            SetNotice(string.Empty);
        }

        public void RequestStartMatch()
        {
            if (!IsServer) return;
            if (started.Value)
            {
                SetNotice("对局已经开始，不能重复开局");
                return;
            }

            if (gameplayLoading || rosterLocked || endingMatch)
            {
                SetNotice("正在准备对局，请稍候");
                return;
            }

            string reason;
            var tier = targetSeats.Value;
            var count = lobbyClients.Count;
            if (!LobbySeatingRules.CanStart(tier, count, out reason))
            {
                SetNotice(reason);
                Debug.Log("[Match] 开赛被拒绝：" + reason);
                return;
            }

            var director = RoundDirector;
            string configError;
            if (director != null && !director.TryValidateConfig(out configError))
            {
                SetNotice("回合配置非法，无法开赛：" + configError);
                Debug.LogError("[Match] 回合配置非法：" + configError);
                return;
            }

            rosterLocked = true;
            gameplayLoading = true;
            loadingSync.Value = true;
            pendingTier = tier;
            pendingRoster.Clear();
            pendingRoster.AddRange(lobbyClients);
            pendingLobbyReason = "地图加载已取消";
            SetNotice("正在加载正式地图…");

            TransitionOverlay.Cover(SceneNames.Gameplay, LoadGameplayCovered());
        }

        private IEnumerator LoadGameplayCovered()
        {
            if (!IsServer || !gameplayLoading || NetworkManager == null || NetworkManager.ShutdownInProgress) yield break;
            var status = NetworkManager.SceneManager.LoadScene(SceneNames.Gameplay, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                rosterLocked = false;
                gameplayLoading = false;
                loadingSync.Value = false;
                pendingTier = 0;
                pendingRoster.Clear();
                SetNotice(status == SceneEventProgressStatus.SceneEventInProgress
                    ? "另一个场景事件正在进行，请稍后重试开赛"
                    : "地图加载启动失败：" + status);
                Debug.LogWarning("[Match] LoadScene 返回 " + status + "（仅接受 Started）");
                yield break;
            }

            while (IsServer && gameplayLoading && NetworkManager != null && !NetworkManager.ShutdownInProgress)
                yield return null;
        }

        private void HandleLoadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer || loadSceneMode != LoadSceneMode.Additive || sceneName != SceneNames.Gameplay) return;

            if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            {
                Debug.LogWarning("[Match] Gameplay 加载超时 clients=" + clientsTimedOut.Count);
                AbortGameplayLoad("部分客户端地图加载超时，已取消开赛");
                return;
            }

            var loadedBinder = FindObjectOfType<GameplayBinder>();
            if (loadedBinder == null)
            {
                Debug.LogError("[Match] GameplayBinder 缺失");
                AbortGameplayLoad("正式地图缺少 GameplayBinder，已取消开赛");
                return;
            }

            BindGameplay(loadedBinder);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DevLoadHoldSeconds > 0f)
            {
                StartCoroutine(HoldThenStart(pendingTier, clientsCompleted, DevLoadHoldSeconds));
                return;
            }
#endif
            StartMatchServer(pendingTier, clientsCompleted);
        }

        private IEnumerator HoldThenStart(int tier, List<ulong> completed, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (!IsServer || started.Value || !gameplayLoading) yield break;
            SetNotice("正式地图已加载，等待开局提交…");
            StartMatchServer(tier, completed);
        }

        private void HandleUnloadEventCompleted(string sceneName, LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer || sceneName != SceneNames.Gameplay) return;
            ResetToLobbyState(pendingLobbyReason);
        }

        private void StartMatchServer(int tier, List<ulong> clientsCompleted)
        {
            if (seatActors == null || spawnPoints == null || tier > seatActors.Length || tier > spawnPoints.Length)
            {
                AbortGameplayLoad("正式地图未绑定足够的席位和出生点，无法开赛");
                return;
            }
            for (var seat = 0; seat < tier; seat++)
            {
                if (seatActors[seat] != null && spawnPoints[seat] != null) continue;
                AbortGameplayLoad("正式地图缺少第 " + (seat + 1) + " 席的角色或出生点");
                return;
            }

            // 只承认“开赛时已在大厅名单、仍在真实连接、且本次 Gameplay 同步完成”的客机；
            // approval 会先把尚未完成同步的连接放进 ConnectedClientsIds，不能据此计入开局。
            var hostId = NetworkManager.LocalClientId;
            var connected = NetworkManager.ConnectedClientsIds;
            if (!ContainsClient(connected, hostId))
            {
                AbortGameplayLoad("房主连接状态异常，已取消开赛");
                return;
            }

            var roster = new List<ulong>();
            var hostInCompleted = clientsCompleted != null && clientsCompleted.Contains(hostId);
            for (var i = 0; i < pendingRoster.Count; i++)
            {
                var id = pendingRoster[i];
                if (id == hostId) continue;
                if (clientsCompleted == null || !clientsCompleted.Contains(id)) continue;
                if (!lobbyClients.Contains(id) || !ContainsClient(connected, id) || pendingKicks.Contains(id)) continue;
                roster.Add(id);
            }

            if (!roster.Contains(hostId)) roster.Insert(0, hostId);
            var count = roster.Count;
            string reason;
            if (!LobbySeatingRules.CanStart(tier, count, out reason))
            {
                AbortGameplayLoad("开局前真人玩家变化：" + reason);
                return;
            }

            lobbyClients.Clear();
            lobbyClients.AddRange(roster);
            Debug.Log("[Match] 开局名单 host=" + hostId + " hostInCompleted=" + hostInCompleted
                + " completed=" + (clientsCompleted != null ? clientsCompleted.Count : 0)
                + " roster=" + string.Join(",", lobbyClients));
            gameId.Value = GenerateGameId();
            targetSeats.Value = tier;
            realPlayers.Value = count;
            roomCount.Value = (tier - LobbySeatingRules.MinSeats) / 2 + 1;
            ResetNpcWalks();
            EnsurePlayerNames();

            for (var i = 0; i < SeatCount; i++)
            {
                var seat = new ServerSeat { ClientId = SeatBindingRules.Unbound };
                var active = i < tier;
                // 已断开的人不再占用真人席：只给当前连接发凭证，其余由 NPC 补位。
                if (active)
                {
                    if (i < count)
                    {
                        seat.Npc = false;
                        seat.ClientId = lobbyClients[i];
                        seat.Online = true;
                        seat.Token = GenerateToken();
                    }
                    else
                    {
                        seat.Npc = true;
                        seat.Online = true;
                    }
                }

                serverSeats[i] = seat;
                playerNames[i] = active && !seat.Npc ? new FixedString64Bytes(LobbyNameForClient(seat.ClientId)) : default;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (active && !seat.Npc) Debug.Log("[Name] game=" + gameId.Value + " seat=" + (i + 1) + " name=" + playerNames[i]);
#endif

                var actor = i < seatActors.Length ? seatActors[i] : null;
                if (actor == null) continue;
                actor.gameObject.SetActive(active);
                if (!active) continue;

                actor.ConfigureAuthority(true);
                var spawn = spawnPoints != null && i < spawnPoints.Length && spawnPoints[i] != null
                    ? spawnPoints[i].position
                    : new Vector3(i * 1.5f, 0f, 0f);
                if (actor.Body != null)
                {
                    actor.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
                    actor.Body.position = new Vector2(spawn.x, spawn.y);
                    actor.Body.velocity = Vector2.zero;
                }

                actor.transform.position = new Vector3(spawn.x, spawn.y, 0f);
            }

            if (privateRooms != null)
            {
                for (var i = 0; i < privateRooms.Length; i++)
                {
                    if (privateRooms[i] != null) privateRooms[i].SetActive(i < roomCount.Value);
                }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var activeRoomTilemaps = 0;
                for (var i = 0; i < privateRooms.Length; i++)
                {
                    if (privateRooms[i] == null || !privateRooms[i].activeInHierarchy) continue;
                    foreach (var renderer in privateRooms[i].GetComponentsInChildren<UnityEngine.Tilemaps.TilemapRenderer>(true))
                        if (renderer.enabled) activeRoomTilemaps++;
                }
                Debug.Log("[Match] 房间 Tilemap Renderer=" + activeRoomTilemaps + " rooms=" + roomCount.Value);
#endif
            }

            var director = RoundDirector;
            if (director != null && !director.ServerBeginMatch(tier))
            {
                AbortGameplayLoad("回合权威初始化失败，已取消开赛");
                return;
            }

            started.Value = true;
            gameplayLoading = false;
            loadingSync.Value = false;
            PublishSnapshot();

            for (var i = 0; i < tier; i++)
            {
                if (!serverSeats[i].Npc) SendSeatAssigned(i, serverSeats[i].ClientId);
            }
            if (director != null) director.ServerSendInitialHeldHistory();

            SetNotice("对局开始：" + tier + " 席（" + count + " 真人 + " + (tier - count) + " NPC）");
            Debug.Log("[Match] 开局 gameId=" + gameId.Value + " tier=" + tier + " real=" + count + " rooms=" + roomCount.Value);
        }

        /// <summary>房主结束对局：保留连接，卸载正式地图并回到联机房间。</summary>
        public void RequestEndMatch()
        {
            if (!IsServer || endingMatch) return;

            if (!started.Value && !gameplayLoading)
            {
                SetNotice("当前没有进行中的对局");
                return;
            }

            endingMatch = true;
            loadingSync.Value = true;
            pendingLobbyReason = "房主结束了本局";
            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == NetworkManager.LocalClientId) continue;
                SendServerReason(ids[i], "房主结束了本局");
            }

            TransitionOverlay.Cover(SceneNames.Gameplay, EndGameplayCovered());
        }

        private IEnumerator EndGameplayCovered()
        {
            if (!IsServer || !endingMatch || NetworkManager == null || NetworkManager.ShutdownInProgress) yield break;
            if (gameplayScene.IsValid() && gameplayScene.isLoaded)
            {
                var status = NetworkManager.SceneManager.UnloadScene(gameplayScene);
                if (status != SceneEventProgressStatus.Started)
                {
                    // 场景事件占用中：保持对局与在线状态，允许稍后重试；不做本地卸载以免与客机分叉。
                    endingMatch = false;
                    loadingSync.Value = false;
                    SetNotice("场景事件占用中，请稍后重试结束对局");
                    Debug.LogWarning("[Match] UnloadScene 返回 " + status + "（仅接受 Started）");
                }
            }
            else
            {
                ResetToLobbyState();
            }
            while (IsServer && endingMatch && NetworkManager != null && !NetworkManager.ShutdownInProgress)
                yield return null;
        }

        private void AbortGameplayLoad(string reason)
        {
            // 取消期间继续保持“不可开赛”，直到地图真正卸载完成或安全结束会话。
            rosterLocked = true;
            gameplayLoading = true;
            loadingSync.Value = true;
            pendingLobbyReason = reason;
            SetNotice(reason + "（正在卸载地图…）");
            Debug.LogWarning("[Match] 取消开赛：" + reason);
            StartCoroutine(CancelGameplayLoad(reason));
        }

        private IEnumerator CancelGameplayLoad(string reason)
        {
            // 必须离开当前 scene 事件回调后再卸载，否则 NGO 会把卸载当作进行中的同一事件。
            yield return null;
            for (var attempt = 0; attempt < 12; attempt++)
            {
                if (!IsServer) yield break;
                var scene = SceneManager.GetSceneByName(SceneNames.Gameplay);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    ResetToLobbyState(reason);
                    yield break;
                }

                if (NetworkManager != null && !NetworkManager.ShutdownInProgress && NetworkManager.SceneManager != null)
                {
                    var status = NetworkManager.SceneManager.UnloadScene(scene);
                    Debug.Log("[Match] 取消开赛卸载尝试 " + attempt + " -> " + status);
                }

                var waited = 0f;
                while (waited < 0.5f && SceneManager.GetSceneByName(SceneNames.Gameplay).isLoaded)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!SceneManager.GetSceneByName(SceneNames.Gameplay).isLoaded)
                {
                    ResetToLobbyState(reason);
                    yield break;
                }
            }

            Debug.LogError("[Match] Gameplay 无法卸载，安全结束会话：" + reason);
            SafeEndSession("地图状态异常，已安全结束会话：" + reason);
        }

        private void SafeEndSession(string reason)
        {
            SetNotice(reason);
            if (NetworkManager == null) return;
            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == NetworkManager.LocalClientId) continue;
                SendServerReason(ids[i], reason);
            }

            StartCoroutine(ShutdownSoon());
        }

        private IEnumerator ShutdownSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            if (NetworkManager != null && NetworkManager.IsServer) NetworkManager.Shutdown();
        }

        private void RestoreLobbyActiveScene()
        {
            var lobby = SceneManager.GetSceneByName(SceneNames.LanLobby);
            if (lobby.IsValid() && lobby.isLoaded) SceneManager.SetActiveScene(lobby);
        }

        private void ResetToLobbyState(string lobbyReason = "房主结束了本局")
        {
            if (!IsServer) return;

            endedLastMatch = true;
            started.Value = false;
            roomCount.Value = 0;
            gameId.Value = 0;
            rosterLocked = false;
            gameplayLoading = false;
            loadingSync.Value = false;
            endingMatch = false;
            pendingTier = 0;
            pendingRoster.Clear();

            for (var i = 0; i < SeatCount; i++)
            {
                serverSeats[i] = new ServerSeat { ClientId = SeatBindingRules.Unbound };
            }

            PublishSnapshot();
            ClearBinding();

            // 回到大厅前只保留“已在大厅名单且仍连接”的人；被踢/未完成入场的连接不进入已准备真人名单。
            pendingReconnects.Clear();
            pendingApprovals.Clear();
            RefilterLobbyFromConnected();
            if (!hostPickedTier) targetSeats.Value = LobbySeatingRules.DefaultTier(lobbyClients.Count);
            realPlayers.Value = lobbyClients.Count;
            RefreshLobbyTier();
            if (RoundDirector != null) RoundDirector.ServerReset();
            ClearSpectators();
            SetGameOver(false);
            SetNotice(lobbyReason);
            Debug.Log("[Match] 已回到大厅（" + lobbyReason + "），当前真人=" + lobbyClientCountForLog() + "，等待重新开赛");
        }

        private bool endedLastMatch;

        private void SendSeatAssigned(int seatIndex, ulong clientId)
        {
            var token = serverSeats[seatIndex].Token;
            if (token == null) return;

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
            };
            SendSeatAssignedClientRpc(gameId.Value, (byte)seatIndex, token, target);
        }

        [ClientRpc]
        private void SendSeatAssignedClientRpc(uint assignedGameId, byte seat, byte[] tokenPayload, ClientRpcParams rpcParams = default)
        {
            localSeat = seat;
            var credential = new SeatCredential { Seat = seat, GameId = assignedGameId, Token = tokenPayload };
            var payload = SeatCredentialCodec.Encode(credential);
            if (payload != null) GameSessionCache.Store(assignedGameId, seat, payload);
            if (LocalSeatAssigned != null) LocalSeatAssigned(seat);
        }

        private static byte[] GenerateToken()
        {
            var token = new byte[TokenBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(token);
            }

            return token;
        }

        private static uint GenerateGameId()
        {
            var bytes = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
            return value == 0 ? 1u : value;
        }

        // ---------------- 权威移动 ----------------

        public void SendLocalMoveInput(Vector2 direction)
        {
            SendLocalMoveInput(direction, false);
        }

        public void SendLocalMoveInput(Vector2 direction, bool run)
        {
            if (!IsSpawned || !IsClient) return;
            SubmitMoveInputServerRpc(direction, run);
        }

        // 高频输入走 Unreliable：丢包由 0.25s 过期兜底，避免旧方向在可靠队列里排队续走。
        [ServerRpc(RequireOwnership = false, Delivery = RpcDelivery.Unreliable)]
        private void SubmitMoveInputServerRpc(Vector2 direction, bool run, ServerRpcParams rpcParams = default)
        {
            if (!started.Value) return;

            var sender = rpcParams.Receive.SenderClientId;
            var index = FindSeatByClient(sender);
            if (index < 0) return;

            var seat = serverSeats[index];
            if (gameOver || IsSpectator(index))
            {
                seat.Direction = Vector2.zero;
                seat.Running = false;
                seat.LastInput = 0f;
                serverSeats[index] = seat;
                Debug.Log("[Match] 拒绝观战/终局移动 seat=" + (index + 1));
                return;
            }

            if (!SeatBindingRules.CanMove(started.Value, seat.Online, seat.ClientId, sender)) return;

            var now = Time.unscaledTime;
            float x;
            float y;
            if (!MoveInputRules.TrySanitize(direction.x, direction.y, out x, out y))
            {
                seat.Direction = Vector2.zero;
                seat.Running = false;
                seat.LastInput = now;
                serverSeats[index] = seat;
                return;
            }

            var isZero = x == 0f && y == 0f;
            if (isZero)
            {
                seat.Direction = Vector2.zero;
                seat.Running = false;
                seat.LastInput = now;
                serverSeats[index] = seat;
                return;
            }

            if (MoveInputRules.IsRateLimited(now, seat.LastAccepted)) return;

            seat.LastAccepted = now;
            seat.LastInput = now;
            seat.Direction = new Vector2(x, y);
            seat.Running = run;
            serverSeats[index] = seat;
        }

        private int FindSeatByClient(ulong clientId)
        {
            for (var i = 0; i < targetSeats.Value && i < SeatCount; i++)
            {
                var seat = serverSeats[i];
                if (!seat.Npc && seat.ClientId == clientId) return i;
            }

            return -1;
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || !started.Value || gameOver) return;
            if (NetworkManager == null || NetworkManager.ShutdownInProgress) return;
            if (seatActors == null) return;

            var now = Time.unscaledTime;
            for (var i = 0; i < targetSeats.Value && i < SeatCount; i++)
            {
                if (i >= seatActors.Length) break;
                var actor = seatActors[i];
                if (actor == null || !actor.gameObject.activeSelf) continue;

                var seat = serverSeats[i];
                if (seat.Npc)
                {
                    UpdateNpcWalk(i, actor, now);
                    continue;
                }
                if (!seat.Online)
                {
                    if (actor.Body != null) actor.Body.velocity = Vector2.zero;
                    continue;
                }

                var velocity = MoveInputRules.IsFresh(now, seat.LastInput) ? seat.Direction * (moveSpeed * (seat.Running ? runMultiplier : 1f)) : Vector2.zero;
                if (actor.Body != null) actor.Body.velocity = velocity;
            }

            if (RoundDirector != null) RoundDirector.TickRoomsForAuthority();

            snapshotTimer += Time.fixedDeltaTime;
            if (snapshotTimer >= snapshotInterval)
            {
                snapshotTimer = 0f;
                PublishSnapshot();
            }
        }

        private void PublishSnapshot()
        {
            if (!IsServer || NetworkManager == null || NetworkManager.ShutdownInProgress || !IsSpawned) return;

            for (var i = 0; i < SeatCount; i++)
            {
                var snapshot = new SeatSnapshot { Seat = (byte)i, Flags = 0 };
                if (started.Value && SeatBindingRules.IsSeatIndexActive(i, targetSeats.Value))
                {
                    var seat = serverSeats[i];
                    snapshot.Flags = seat.Npc ? SeatSnapshot.FlagNpc : SeatSnapshot.FlagReal;
                    if (seat.Npc || seat.Online) snapshot.Flags |= SeatSnapshot.FlagOnline;
                    if (seat.Running) snapshot.Flags |= SeatSnapshot.FlagRunning;
                    if (spectatorSeats.Contains(i)) snapshot.Flags |= SeatSnapshot.FlagEliminated;

                    var actor = seatActors != null && i < seatActors.Length ? seatActors[i] : null;
                    if (actor != null)
                    {
                        var position = actor.Body != null ? actor.Body.position : (Vector2)actor.transform.position;
                        snapshot.X = position.x;
                        snapshot.Y = position.y;
                        if (actor.Body != null)
                        {
                            snapshot.VX = actor.Body.velocity.x;
                            snapshot.VY = actor.Body.velocity.y;
                        }
                    }
                }

                if (i >= seats.Count) seats.Add(snapshot);
                else if (!seats[i].Equals(snapshot)) seats[i] = snapshot;
            }
        }

        private int CountOnlineSeats()
        {
            var count = 0;
            for (var i = 0; i < seats.Count && i < SeatCount; i++)
            {
                if (i < targetSeats.Value && seats[i].IsOnline) count++;
            }

            return count;
        }

        private void SetNotice(string message)
        {
            if (NetworkManager != null && NetworkManager.ShutdownInProgress) return;
            notice.Value = new FixedString128Bytes(message ?? string.Empty);
        }

        // ---------------- 视图 ----------------

        private void Update()
        {
            if (!IsSpawned) return;

            if (binder == null)
            {
                var found = FindObjectOfType<GameplayBinder>();
                if (found != null) BindGameplay(found);
            }
            else if (binder != null && !binder)
            {
                seatActors = null;
                spawnPoints = null;
                privateRooms = null;
                cameraFollow = null;
                binder = null;
                gameplayScene = default(Scene);
                RestoreLobbyActiveScene();
            }

            if (seatActors != null)
            {
                for (var i = 0; i < seatActors.Length; i++)
                {
                    var actor = seatActors[i];
                    if (actor == null) continue;

                    var snapshot = i < seats.Count ? seats[i] : new SeatSnapshot { Seat = (byte)i, Flags = 0 };
                    var visible = started.Value && SeatBindingRules.IsSeatIndexActive(i, targetSeats.Value) && !snapshot.IsEliminated;
                    var isSelf = IsClient && i == localSeat;
                    actor.ApplyView(snapshot, isSelf, started.Value, visible);

                    if (visible && !IsServer)
                    {
                        actor.transform.position = Vector3.Lerp(
                            actor.transform.position,
                            new Vector3(snapshot.X, snapshot.Y, 0f),
                            Mathf.Clamp01(12f * Time.deltaTime));
                    }
                }
            }

            if (privateRooms != null)
            {
                for (var i = 0; i < privateRooms.Length; i++)
                {
                    if (privateRooms[i] == null) continue;
                    var roomVisible = started.Value && i < roomCount.Value;
                    if (privateRooms[i].activeSelf != roomVisible) privateRooms[i].SetActive(roomVisible);
                }
            }
        }

        public SeatActor GetSeatActor(int index)
        {
            return seatActors != null && index >= 0 && index < seatActors.Length ? seatActors[index] : null;
        }

        public bool TryGetSeatPosition(int index, out Vector2 position)
        {
            position = Vector2.zero;
            if (!IsServer) return false;
            var actor = GetSeatActor(index);
            if (actor == null) return false;
            position = actor.Body != null ? actor.Body.position : (Vector2)actor.transform.position;
            return !float.IsNaN(position.x) && !float.IsNaN(position.y)
                && !float.IsInfinity(position.x) && !float.IsInfinity(position.y);
        }

        public SeatSnapshot GetSeatSnapshot(int index)
        {
            if (index < 0 || index >= seats.Count) return new SeatSnapshot { Seat = (byte)index, Flags = 0 };
            return seats[index];
        }

        public string BuildLobbySummary()
        {
            if (!started.Value && TargetSeats == 0) return "真人: " + RealCount + " / 等待真人加入";
            return "真人: " + RealCount + " / 目标席位: " + TargetSeats + " / NPC: " + PreviewNpcCount + " / 房间: " + RoomCount;
        }

        public string BuildSeatLine()
        {
            if (!started.Value) return "席位: 未开赛";
            if (localSeat < 0) return "席位: 观察（未分配）";

            var snapshot = GetSeatSnapshot(localSeat);
            return "席位: P" + (localSeat + 1) + (snapshot.IsOnline ? "（在线）" : "（离线）")
                + " / 在线 " + ConnectedSeatCount + "/" + TargetSeats
                + " / 房间 " + RoomCount;
        }

        public void AnnounceHostLeaving(string reason)
        {
            if (!IsServer || NetworkManager == null) return;
            leavingMatch = true;
            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            for (var i = 0; i < ids.Count; i++)
            {
                if (ids[i] == NetworkManager.LocalClientId) continue;
                SendServerReason(ids[i], reason);
            }
        }

        private void SendServerReason(ulong clientId, string reason)
        {
            if (!IsServer) return;
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            NotifyServerReasonClientRpc(reason, target);
        }

        private IEnumerator KickLater(ulong clientId, string reason)
        {
            pendingKicks.Add(clientId);
            yield return new WaitForSecondsRealtime(0.25f);
            pendingKicks.Remove(clientId);
            if (IsServer && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            {
                NetworkManager.DisconnectClient(clientId, reason);
            }
        }

        [ClientRpc]
        private void NotifyServerReasonClientRpc(string reason, ClientRpcParams rpcParams = default)
        {
            pendingServerReason = reason;
        }
    }
}
