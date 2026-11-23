using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>
    /// 权威回合/物品/私有价值/秘密选择/市场/胜负。
    /// 公开信息走 NetworkVariable/NetworkList；真实价值只进服务端内存，并按目标 clientId 定向发送私有载荷。
    /// </summary>
    public sealed partial class MatchRoundDirector : NetworkBehaviour
    {
        [SerializeField] private RoundConfigAsset roundConfig;
        [SerializeField] private ItemCatalogAsset itemCatalog;
        [SerializeField] private EconomyConfigAsset economy;
        [SerializeField] private TradeConfigAsset tradeConfig;
        [SerializeField] private BroadcastConfigAsset broadcastConfig;

        private readonly NetworkVariable<int> roundNumber = new NetworkVariable<int>(0);
        private readonly NetworkVariable<int> phase = new NetworkVariable<int>((int)RoundPhase.Idle);
        private readonly NetworkVariable<double> phaseDeadline = new NetworkVariable<double>(0);
        private readonly NetworkVariable<int> cfgFirstTrade = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> cfgSecondTrade = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> cfgThirdTrade = new NetworkVariable<int>(-1);
        private readonly NetworkVariable<int> aliveCount = new NetworkVariable<int>(0);
        private readonly NetworkVariable<bool> matchOver = new NetworkVariable<bool>(false);
        private readonly NetworkVariable<int> marketVersion = new NetworkVariable<int>(0);
        private readonly NetworkVariable<FixedString512Bytes> marketBanner = new NetworkVariable<FixedString512Bytes>(default);
        private readonly NetworkVariable<FixedString4096Bytes> resultBanner = new NetworkVariable<FixedString4096Bytes>(default);
        private readonly NetworkList<PublicItemState> publicItems = new NetworkList<PublicItemState>();
        private readonly NetworkList<TradePublicState> tradePublic = new NetworkList<TradePublicState>();

        private RoundStateMachine machine;
        private RoundConfigData activeConfig;
        private ItemCatalogData catalog;
        private EconomyConfigData economyData;
        private MatchAuthority authority;

        [SerializeField, Min(0f)] private float chatNearbyRadius = 4f;
        public float ChatNearbyRadius
        {
            get
            {
                if (float.IsNaN(chatNearbyRadius) || float.IsInfinity(chatNearbyRadius)) return 0f;
                return Mathf.Max(0f, chatNearbyRadius);
            }
        }

        private int tier;
        private int seed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private SystemRandomSource devRandom;
#endif
        private long[] values;
        private int[] defIndex;
        private int[] itemInstance;
        private int[] valueVersion;
        private bool[] alive;
        private bool[] npc;

        private bool initialized;
        private int valueBatchVersion = 1;
        private bool tieBreakInProgress;
        private int marketAppliedRound;
        private float variableTimer;

        private int lastLoggedPhase = -1;
        private int lastLoggedRound = -1;
        private int[] tradeFailures;
        private bool[] tradeSuccessUsed;
        private ValueHistoryLedger heldHistory;
        private string localTradeHistory = string.Empty;
        private readonly List<BroadcastRules.BroadcastItem> broadcastBatch = new List<BroadcastRules.BroadcastItem>();
        private BroadcastSeatViews broadcastSeatViews; // server-only, discarded between rounds
        private int broadcastPublished;
        private int broadcastFakeCount;
        private bool broadcastRevealed;
        private double broadcastNextAt;
        private int plannedMarketEvent = -1;
        private int plannedMarketRound = -1;
        private MarketEventDefinition plannedMarket;
        private readonly List<string> clientBroadcast = new List<string>();
        private int clientBroadcastTotal = -1;
        private uint clientBroadcastGame;
        private int clientBroadcastRound;
        private readonly Dictionary<uint, TradeRequest> tradeRequests = new Dictionary<uint, TradeRequest>();
        private readonly Dictionary<int, uint> pairRequests = new Dictionary<int, uint>();
        private struct TradeOpKey : System.IEquatable<TradeOpKey>
        {
            public uint GameId;
            public int Round;
            public int Phase;
            public ulong ClientId;
            public uint OpId;
            public bool Equals(TradeOpKey other)
            {
                return GameId == other.GameId && Round == other.Round && Phase == other.Phase
                    && ClientId == other.ClientId && OpId == other.OpId;
            }
            public override bool Equals(object obj) { return obj is TradeOpKey other && Equals(other); }
            public override int GetHashCode()
            {
                unchecked { var h = (int)GameId; h = h * 397 ^ Round; h = h * 397 ^ Phase; h = h * 397 ^ ClientId.GetHashCode(); h = h * 397 ^ (int)OpId; return h; }
            }
        }

        private readonly Dictionary<TradeOpKey, int> opResults = new Dictionary<TradeOpKey, int>();
        private readonly HashSet<(int Seat, int Instance)> tradedOutInstances = new HashSet<(int Seat, int Instance)>();
        private uint nextTradeRequestId;
        private readonly List<TradeClientRequest> clientTradeRequests = new List<TradeClientRequest>();
        private int localTradeFailures;
        private bool localTradeLocked;
        private bool localTradeExhausted;
        private string localTradeMessage = string.Empty;

        private sealed class TradeRequest
        {
            public uint Id;
            public int Round;
            public int From;
            public int To;
            public int FromInstance;
            public int ToInstance;
            public int FromDef;
            public int ToDef;
            public int FromVersion;
            public int ToVersion;
            public uint GameId;
            public int Phase;
            public bool FromNpc;
            public bool ToNpc;
            public int Approach;
        }

        private struct TradeClientRequest
        {
            public uint Id;
            public int From;
            public int To;
            public int FromDef;
            public int ToDef;
            public int FromInstance;
            public int ToInstance;
            public bool Incoming;
        }
        private long privateValue;
        private int privateInstance;
        private int privateRound;
        private int privateVersion;

        private bool hasPendingPrivate;
        private uint pendingGameId;
        private int pendingRound;
        private int pendingSeat;
        private int pendingInstance;
        private long pendingValue;
        private int pendingVersion;
        private uint lastGameId;

        public int Round { get { return roundNumber.Value; } }
        public RoundPhase Phase { get { return (RoundPhase)phase.Value; } }
        public float Remaining
        {
            get
            {
                if (matchOver.Value || !IsSpawned) return 0f;
                var remaining = phaseDeadline.Value - ServerNow();
                return remaining > 0 ? (float)remaining : 0f;
            }
        }

        private double ServerNow()
        {
            return NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        }
        public int Alive { get { return aliveCount.Value; } }
        public bool MatchOver { get { return matchOver.Value; } }
        public int MarketVersion { get { return marketVersion.Value; } }
        public string MarketBanner { get { return marketBanner.Value.ToString(); } }
        public int FirstTradeSeconds { get { return cfgFirstTrade.Value > 0 ? cfgFirstTrade.Value : activeConfig != null ? Mathf.RoundToInt(activeConfig.FirstTradeSeconds) : 120; } }
        public int SecondTradeSeconds { get { return cfgSecondTrade.Value > 0 ? cfgSecondTrade.Value : activeConfig != null ? Mathf.RoundToInt(activeConfig.SecondTradeSeconds) : 120; } }
        public int ThirdTradeSeconds { get { return cfgThirdTrade.Value > 0 ? cfgThirdTrade.Value : activeConfig != null ? Mathf.RoundToInt(activeConfig.ThirdTradeSeconds) : 60; } }
        public void ServerSetTradeTimes(int first, int second, int third)
        {
            if (!IsServer || initialized) return;
            cfgFirstTrade.Value = Mathf.Clamp(first, 10, 600);
            cfgSecondTrade.Value = Mathf.Clamp(second, 10, 600);
            cfgThirdTrade.Value = Mathf.Clamp(third, 10, 600);
        }
        public string ResultBanner { get { return resultBanner.Value.ToString(); } }
        private bool hasPrivate;
        public bool HasPrivate { get { return hasPrivate; } }

        /// <summary>公开版本已前进但本人私有值尚未对齐时，HUD 显示“同步中”而不是旧价。</summary>
        public bool IsPrivateStale
        {
            get
            {
                if (!hasPrivate) return false;
                if (authority == null) authority = FindObjectOfType<MatchAuthority>();
                if (authority == null || authority.LocalSeat < 0) return false;
                return TryGetPublicItem(authority.LocalSeat, out var item) && item.ValueVersion > privateVersion;
            }
        }
        public long PrivateValue { get { return privateValue; } }
        public int PrivateRound { get { return privateRound; } }
        public int PrivateVersion { get { return privateVersion; } }

        public bool TryValidateConfig(out string error)
        {
            error = null;
            Debug.Log("[Round] 配置资产检查 round=" + (roundConfig != null) + " items=" + (itemCatalog != null)
                + " eco=" + (economy != null) + " go=" + name + " prefab=" + (gameObject.scene.name == null ? "?" : gameObject.scene.name));
            if (roundConfig == null) { error = "缺少回合配置资产"; return false; }
            if (itemCatalog == null) { error = "缺少物品目录资产"; return false; }
            if (economy == null) { error = "缺少经济配置资产"; return false; }
            if (!roundConfig.Data.Validate(out error)) return false;
            if (!itemCatalog.Data.Validate(out error)) return false;
            if (!economyDataValid(economy.Data, out error)) return false;
            if (!economy.Data.ValidateForCatalog(itemCatalog.Data, out error)) return false;
            if (tradeConfig == null) { error = "缺少交易配置"; return false; }
            if (!tradeConfig.Validate(out error)) return false;
            if (broadcastConfig == null) { error = "缺少广播配置"; return false; }
            if (!broadcastConfig.Validate(out error)) return false;
            return true;
        }

        private static bool economyDataValid(EconomyConfigData data, out string error)
        {
            error = null;
            if (data == null) { error = "经济配置为空"; return false; }
            return data.Validate(out error);
        }

        public bool TryGetPublicItem(int seat, out PublicItemState item)
        {
            for (var i = 0; i < publicItems.Count; i++)
            {
                if (publicItems[i].Seat == seat) { item = publicItems[i]; return true; }
            }

            item = default;
            return false;
        }

        public string GetItemName(int defIndexValue)
        {
            if (catalog == null || catalog.Definitions == null) return "?";
            if (defIndexValue < 0 || defIndexValue >= catalog.Definitions.Length) return "?";
            return catalog.Definitions[defIndexValue].DisplayName;
        }

        public string GetItemGlyph(int defIndexValue)
        {
            if (catalog == null || catalog.Definitions == null) return "?";
            if (defIndexValue < 0 || defIndexValue >= catalog.Definitions.Length) return "?";
            return catalog.Definitions[defIndexValue].Glyph;
        }

        private void Awake()
        {
            activeConfig = roundConfig != null ? roundConfig.Data : new RoundConfigData();
            catalog = itemCatalog != null ? itemCatalog.Data : new ItemCatalogData();
            economyData = economy != null ? economy.Data : new EconomyConfigData();
            machine = new RoundStateMachine(activeConfig);
        }

        private void Update()
        {
            if (!IsSpawned) return;
            if (IsServer)
            {
                if (!initialized) return;
                var now = ServerNow();
                if (RoundStateMachine.IsTradePhase(machine.Phase) && !TradePossible())
                {
                    machine.RequestEarlyEnd(now, 1f);
                }
                machine.Advance(now, OnPhaseEntered);
                if (IsServer) CleanSkipVotes();
                if (initialized && machine.Phase == RoundPhase.Broadcast) TickBroadcast(now);
                if (tradeRequests.Count > 0 && !IsTradeWindowOpen())
                {
                    ExpireTradeRequests(TradeResultCode.Expired);
                }
                variableTimer += Time.unscaledDeltaTime;
                if (variableTimer >= 0.1f)
                {
                    variableTimer = 0f;
                    roundNumber.Value = machine.Round;
                    phase.Value = (int)machine.Phase;
                    phaseDeadline.Value = machine.PhaseEndsAt;
                    aliveCount.Value = CountAlive();
                    if (machine.Phase == RoundPhase.GameOver) matchOver.Value = true;
                }
                // 房主同样走客户端合并路径（Listen Host）。
            }

            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (roundNumber.Value != lastLoggedRound || phase.Value != lastLoggedPhase)
            {
                lastLoggedRound = roundNumber.Value;
                lastLoggedPhase = phase.Value;
                Debug.Log("[Round] 客户端阶段 round=" + lastLoggedRound + " phase=" + ((RoundPhase)lastLoggedPhase)
                    + " remaining=" + Remaining.ToString("0.0") + " alive=" + aliveCount.Value);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if ((RoundPhase)lastLoggedPhase == RoundPhase.MarketUpdate) Debug.Log("[Round] 市场公告 " + MarketBanner);
#endif
            }

            var currentGame = authority != null ? authority.GameId : 0;
            if (currentGame != lastGameId)
            {
                lastGameId = currentGame;
                Debug.Log("[Round] 客户端对局 gameId=" + currentGame);
                ClearPrivateCache();
                ClearTradeClientState(currentGame);
                if (clientBroadcastGame != currentGame) ClearClientBroadcast();
            }
            else if (roundNumber.Value == 0)
            {
                // 新局/重置只清已展示状态；同一局明确匹配的待合并包保留。
                ClearDisplayedPrivate();
                if (hasPendingPrivate && (pendingGameId != currentGame || pendingRound <= 0))
                {
                    hasPendingPrivate = false;
                }
            }

            if (hasPrivate && authority != null && authority.IsLocalPlayerSeated && privateInstance >= 0)
            {
                if (!TryGetPublicItem(authority.LocalSeat, out var own) || own.InstanceId != privateInstance)
                {
                    ClearDisplayedPrivate(); // 保留 pending，等公开新身份到达后合并
                }
            }

            TickHeldHistory();
            TryMergePendingPrivate();
        }

        private void ClearPrivateCache()
        {
            ClearDisplayedPrivate();
            hasPendingPrivate = false;
            pendingValue = 0;
        }

        private void ClearTradeClientState(uint pendingGameToKeep = 0)
        {
            clientTradeRequests.Clear();
            localTradeFailures = 0;
            localTradeLocked = false;
            localTradeExhausted = false;
            localTradeMessage = string.Empty;
            localTradeHistory = string.Empty;
            ClearHeldHistoryClient(pendingGameToKeep);
        }

        private void ClearDisplayedPrivate()
        {
            hasPrivate = false;
            privateInstance = -1;
            privateRound = 0;
            privateVersion = 0;
        }

        public override void OnNetworkDespawn()
        {
            ClearPrivateCache();
            ClearTradeClientState();
            ClearClientBroadcast();
            base.OnNetworkDespawn();
        }

        private void TryMergePendingPrivate()
        {
            if (!hasPendingPrivate) return;
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || !authority.IsLocalPlayerSeated) return;
            if (!TryGetPublicItem(authority.LocalSeat, out var item)) return;
            var decision = PrivateValueRules.Decide(pendingGameId, pendingRound, pendingSeat, pendingInstance, pendingVersion,
                authority.GameId, authority.LocalSeat, roundNumber.Value, item.InstanceId, false,
                item.ValueVersion, privateVersion, privateRound);
            if (decision == PrivateMergeDecision.Apply)
            {
                ApplyPrivate(pendingValue, pendingInstance, pendingRound, pendingVersion, true);
                hasPendingPrivate = false;
            }
            else if (decision == PrivateMergeDecision.Discard)
            {
                hasPendingPrivate = false;
            }
        }

        // ---------------- 服务端 ----------------

        public bool ServerBeginMatch(int seats)
        {
            authority = FindObjectOfType<MatchAuthority>();
            if (authority == null) return false;
            if (machine == null) return false;
            if (!TryValidateConfig(out var error))
            {
                Debug.LogError("[Round] 配置非法：" + error);
                return false;
            }

            tier = Mathf.Clamp(seats, 1, MatchAuthority.SeatCount);
            activeConfig = roundConfig.Data.Copy();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (DevShortRounds)
            {
                activeConfig = new RoundConfigData
                {
                    RoundStartSeconds = 0.5f,
                    OpeningGoalSeconds = roundConfig.Data.OpeningGoalSeconds,
                    FirstTradeSeconds = 6f,
                    BroadcastSeconds = 12f,
                    SecondTradeSeconds = 6f,
                    LieRevealSeconds = 5f,
                    ThirdTradeSeconds = 6f,
                    MarketDisplaySeconds = roundConfig.Data.MarketDisplaySeconds
                };
            }
            seed = DevSeedOverride != 0 ? DevSeedOverride : Environment.TickCount;
#else
            seed = Environment.TickCount;
#endif
            if (cfgFirstTrade.Value > 0) activeConfig.FirstTradeSeconds = cfgFirstTrade.Value;
            if (cfgSecondTrade.Value > 0) activeConfig.SecondTradeSeconds = cfgSecondTrade.Value;
            if (cfgThirdTrade.Value > 0) activeConfig.ThirdTradeSeconds = cfgThirdTrade.Value;
            machine = new RoundStateMachine(activeConfig);
            values = new long[tier];
            defIndex = new int[tier];
            itemInstance = new int[tier];
            valueVersion = new int[tier];
            alive = new bool[tier];
            npc = new bool[tier];
            tradeFailures = new int[tier];
            tradeSuccessUsed = new bool[tier];
            heldHistory = new ValueHistoryLedger(tier);
            ResetTradeJournal();
            tradeRequests.Clear();
            pairRequests.Clear();
            opResults.Clear();
            tradedOutInstances.Clear();
            nextTradeRequestId = 0;

            var order = new List<int>(catalog.Definitions.Length);
            for (var i = 0; i < catalog.Definitions.Length; i++) order.Add(i);
            for (var i = order.Count - 1; i > 0; i--)
            {
                var j = NextRandom(i + 1);
                var tmp = order[i]; order[i] = order[j]; order[j] = tmp;
            }

            for (var i = 0; i < tier; i++)
            {
                bool isNpc;
                bool isOnline;
                ulong clientId;
                authority.TryGetSeatInfo(i, out isNpc, out isOnline, out clientId);
                npc[i] = isNpc;
                alive[i] = true;
                defIndex[i] = order[i % order.Count];
                itemInstance[i] = 1000 + i;
                valueVersion[i] = 1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var forced = DevForcedValue(i);
                values[i] = forced >= 0
                    ? forced
                    : EconomyRules.InitialValue(NextRandom(1000000), itemInstance[i], economyData.InitialMin, economyData.InitialMax);
#else
                values[i] = economyData.InitialMin + NextRandom(economyData.InitialMax - economyData.InitialMin + 1);
#endif
                heldHistory.Record(i, 1, RoundPhase.RoundStart, ValueHistoryReason.Initial, itemInstance[i], defIndex[i], values[i]);
            }

            publicItems.Clear();
            tradePublic.Clear();
            for (var i = 0; i < tier; i++)
            {
                bool seatNpc; bool seatOnline; ulong seatClient;
                authority.TryGetSeatInfo(i, out seatNpc, out seatOnline, out seatClient);
                tradePublic.Add(new TradePublicState { Seat = i, Exhausted = 0, Tradable = (seatNpc || seatOnline) ? 1 : 0 });
                publicItems.Add(new PublicItemState
                {
                    Seat = i, InstanceId = itemInstance[i], DefIndex = defIndex[i],
                    IsNpc = npc[i] ? 1 : 0, ValueVersion = 1
                });
            }

            tieBreakInProgress = false;
            marketAppliedRound = 0;
            plannedMarketRound = -1;
            plannedMarketEvent = -1;
            plannedMarket = default;
            valueBatchVersion = 1;
            hasPrivate = false;
            hasPendingPrivate = false;
            matchOver.Value = false;
            marketVersion.Value = 0;
            marketBanner.Value = default;
            resultBanner.Value = default;

            machine.Stop();
            machine.Start(ServerNow(), TotalRounds, !TradePossible());
            secretGoals = new SecretGoals(tier, new BroadcastRandom(NextRandom));
            secretSelections = null;
            initialized = true;
            InitializeRoomState();
            clientTradeRequests.Clear();
            localTradeFailures = 0;
            localTradeLocked = false;
            localTradeExhausted = false;
            localTradeMessage = string.Empty;
            SyncVariables();
            for (var seat = 0; seat < tier; seat++) SendSecretState(seat);
            SendPrivateToAll();
            ClearBroadcast();
            Debug.Log("[Round] 对局初始化 tier=" + tier + " round=1 alive=" + CountAlive() + " seedSet=" + (seed != 0));
            return true;
        }

        public void ServerReset()
        {
            ResetTradeJournal();
            heldHistory = null;
            if (IsServer) InitializeRoomState();
            initialized = false;
            secretGoals = null;
            secretSelections = null;
            if (machine != null) machine.Stop();
            publicItems.Clear();
            tier = 0;
            roundNumber.Value = 0;
            phase.Value = (int)RoundPhase.Idle;
            phaseDeadline.Value = 0;
            aliveCount.Value = 0;
            matchOver.Value = false;
            marketVersion.Value = 0;
            marketBanner.Value = default;
            resultBanner.Value = default;
            hasPrivate = false;
            hasPendingPrivate = false;
            valueBatchVersion = 1;
            ClearClientBroadcast();
            tradeRequests.Clear();
            pairRequests.Clear();
            tradedOutInstances.Clear();
            tradePublic.Clear();
            clientTradeRequests.Clear();
            localTradeFailures = 0;
            localTradeLocked = false;
            localTradeExhausted = false;
            if (authority != null) authority.SetGameOver(false);
            Debug.Log("[Round] 已清理对局回合状态");
        }

        public void OnSeatDisconnected(int seat)
        {
            if (!IsServer || seat < 0 || seat >= tier) return;
            CancelRequestsForSeat(seat, TradeResultCode.Disconnected);
            PublishTradeState(seat);
        }

        private void CancelRequestsForSeat(int seat, TradeResultCode code)
        {
            var ids = new List<uint>();
            foreach (var pair in tradeRequests)
            {
                if (pair.Value.From == seat || pair.Value.To == seat) ids.Add(pair.Key);
            }
            foreach (var id in ids)
            {
                if (tradeRequests.TryGetValue(id, out var request)) FinishRequest(request, code, false);
            }
        }

        public void OnSeatReconnected(int seat)
        {
            if (!IsServer || !initialized || seat < 0 || seat >= tier) return;
            CancelRequestsForSeat(seat, TradeResultCode.Disconnected);
            PublishTradeState(seat);
            SendTradeStateToSeat(seat);
            SendHistoryToSeat(seat);
            SendBroadcastCatchUp(seat);
            SendSecretState(seat);
            bool isNpc;
            bool isOnline;
            ulong clientId;
            if (!authority.TryGetSeatInfo(seat, out isNpc, out isOnline, out clientId)) return;
            if (isNpc || !isOnline || clientId == SeatBindingRules.Unbound) return;
            if (NetworkManager == null || !HasConnectedClient(clientId)) return;
            SendPrivate(seat, clientId);
        }

        private void OnPhaseEntered(RoundPhase entered, int round)
        {
            ResetSkipVotes();
            switch (entered)
            {
                case RoundPhase.RoundStart:
                    ClearBroadcast();
                    tieBreakInProgress = false;
                    if (tradeFailures != null)
                    {
                        for (var i = 0; i < tier; i++)
                        {
                            tradeFailures[i] = 0;
                            tradeSuccessUsed[i] = false;
                            PublishTradeState(i);
                        }
                        tradedOutInstances.Clear();
                        ExpireTradeRequests(TradeResultCode.Expired);
                        for (var hs = 0; hs < tier; hs++) SendHistoryToSeat(hs);
                    }
                    machine.SetEarlyTrade(!TradePossible());
                    marketBanner.Value = default;
                    SendPrivateToAll();
                    break;
                case RoundPhase.FirstTrade:
                    ResetTradeSegment();
                    SendPrivateToAll();
                    if (!TradePossible()) machine.RequestEarlyEnd(ServerNow(), 1f);
                    break;
                case RoundPhase.SecondTrade:
                case RoundPhase.ThirdTrade:
                    ResetTradeSegment();
                    if (!TradePossible()) machine.RequestEarlyEnd(ServerNow(), 1f);
                    break;
                case RoundPhase.Broadcast:
                    StartBroadcast();
                    break;
                case RoundPhase.LieReveal:
                    RevealBroadcastTotal();
                    break;
                case RoundPhase.SecretChoice:
                case RoundPhase.TieBreak:
                    ExpireTradeRequests(TradeResultCode.Expired);
                    BeginSecretChoices();
                    break;
                case RoundPhase.MarketUpdate:
                    var deltas = secretSelections.LockAndSum(new BroadcastRandom(NextRandom));
                    ApplyMarket(round);
                    ApplySecretDeltas(deltas);
                    break;
                case RoundPhase.FinalSettlement:
                    ApplySecretDeltas(secretSelections.LockAndSum(new BroadcastRandom(NextRandom)));
                    CheckFinalTie();
                    // The state machine proceeds to results or a tiebreak in the same advance.
                    return;
                case RoundPhase.GameOver:
                    if (secretSelections != null && tieBreakInProgress)
                        ApplySecretDeltas(secretSelections.LockAndSum(new BroadcastRandom(NextRandom)));
                    if (authority != null) authority.SetGameOver(true);
                    PublishFinalResults();
                    matchOver.Value = true;
                    Debug.Log("[Round] GameOver");
                    break;
            }
            tieBreakInProgress = entered == RoundPhase.TieBreak;
            SyncVariables();
        }

        private void PlanMarket(int round)
        {
            plannedMarketRound = round;
            var previous = publicMarketHistory.Count == 0 ? -1 : publicMarketHistory[publicMarketHistory.Count - 1].EventIndex;
            var random = new BroadcastRandom(NextRandom);
            plannedMarketEvent = EconomyRules.PickMarketEvent(economyData.Events.Length, previous, random);
            plannedMarket = economyData.RollMarketEvent(plannedMarketEvent, random);
        }

        private void ApplyMarket(int round)
        {
            if (marketAppliedRound == round) return;
            marketAppliedRound = round;
            var planned = plannedMarketRound == round;
            if (!planned) PlanMarket(round);
            var pick = plannedMarket;
            var record = new PublicMarketRecord { Game = authority.GameId, Round = round, EventIndex = plannedMarketEvent };
            plannedMarketEvent = -1;
            plannedMarketRound = -1;
            plannedMarket = default;
            var banner = new StringBuilder();
            banner.Append(pick.DisplayName).Append(" / 第 ").Append(round).Append(" 轮 / ");
            for (var c = 0; c < pick.Categories.Length; c++)
            {
                banner.Append(pick.Categories[c]).Append(" ");
                var bp = pick.BasisPoints[c];
                record.BasisPoints.Add((short)bp);
                banner.Append(bp >= 0 ? "+" : "").Append(bp / EconomyConfigData.BasisPointsPerPercent).Append("%  ");
            }
            for (var i = 0; i < tier; i++)
            {
                var category = catalog.Definitions[defIndex[i]].Category;
                values[i] = EconomyRules.ApplyBasisPoints(values[i], pick.BasisPointsFor(category), economyData.MinValue, economyData.MaxValue);
            }

            publicMarketHistory.Add(record);
            marketVersion.Value++;
            marketBanner.Value = new FixedString512Bytes(banner.ToString());
            Debug.Log("[Round] 市场 round=" + round + " event=" + pick.Id + " reused=" + planned + " version=" + marketVersion.Value + " alive=" + CountAlive());
        }

        private void SendPrivateToAll()
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            for (var i = 0; i < tier; i++)
            {
                if (!alive[i] || npc[i]) continue;
                bool isNpc;
                bool isOnline;
                ulong clientId;
                if (authority == null || !authority.TryGetSeatInfo(i, out isNpc, out isOnline, out clientId)) continue;
                if (isNpc || !isOnline || clientId == SeatBindingRules.Unbound) continue;
                if (NetworkManager == null || !HasConnectedClient(clientId)) continue;
                SendPrivate(i, clientId);
            }
        }

        private bool HasConnectedClient(ulong clientId)
        {
            var ids = NetworkManager.ConnectedClientsIds;
            for (var i = 0; i < ids.Count; i++) if (ids[i] == clientId) return true;
            return false;
        }

        private void SendPrivate(int seat, ulong clientId)
        {
            SendReturnBadges(seat, clientId);
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            var currentRound = machine.Round;
            SendPrivateValueClientRpc(
                authority != null ? authority.GameId : 0,
                currentRound,
                itemInstance[seat],
                seat,
                values[seat],
                valueVersion[seat],
                target);
            Debug.Log("[Round] 私有值发送 seat=" + seat + " client=" + clientId + " round=" + currentRound + " version=" + valueVersion[seat]);
        }

        [ClientRpc]
        private void SendPrivateValueClientRpc(uint payloadGameId, int payloadRound, int payloadInstance, int payloadSeat,
            long payloadValue, int payloadVersion, ClientRpcParams rpcParams = default)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null) return;
            if (!authority.IsLocalPlayerSeated || roundNumber.Value <= 0 || !TryGetPublicItem(authority.LocalSeat, out var item))
            {
                StagePendingPrivate(payloadGameId, payloadRound, payloadSeat, payloadInstance, payloadValue, payloadVersion);
                return;
            }
            var decision = PrivateValueRules.Decide(payloadGameId, payloadRound, payloadSeat, payloadInstance, payloadVersion,
                authority.GameId, authority.LocalSeat, roundNumber.Value, item.InstanceId, false,
                item.ValueVersion, privateVersion, privateRound);
            if (decision == PrivateMergeDecision.Apply)
            {
                ApplyPrivate(payloadValue, payloadInstance, payloadRound, payloadVersion, false);
                hasPendingPrivate = false;
            }
            else if (decision == PrivateMergeDecision.Stage)
            {
                StagePendingPrivate(payloadGameId, payloadRound, payloadSeat, payloadInstance, payloadValue, payloadVersion);
            }
            else
            {
                Debug.Log("[Round] 私有值丢弃 seat=" + payloadSeat + " round=" + payloadRound + " version=" + payloadVersion);
            }
        }

        private void ApplyPrivate(long value, int instance, int round, int version, bool merged)
        {
            hasPrivate = true;
            privateValue = value;
            privateInstance = instance;
            privateRound = round;
            privateVersion = version;
            Debug.Log("[Round] 私有值应用" + (merged ? "（合并）" : "") + " slot="
                + (authority != null ? authority.LocalSeat + 1 : 0) + " round=" + round + " version=" + version);
        }
        public int PublicItemCount { get { return publicItems.Count; } }

        private static string TradeMessage(TradeResultCode code)
        {
            switch (code)
            {
                case TradeResultCode.Committed: return "成交";
                case TradeResultCode.Rejected: return "对方拒绝";
                case TradeResultCode.Withdrawn: return "已撤回";
                case TradeResultCode.Expired: return "已过期";
                case TradeResultCode.Locked: return "本段已成交";
                case TradeResultCode.NoEligibility: return "交易资格不足";
                case TradeResultCode.NotTradePhase: return "不在交易阶段";
                case TradeResultCode.InvalidTarget: return "目标不可交易";
                case TradeResultCode.Stale: return "物品已变化";
                case TradeResultCode.Busy: return "已有同对请求";
                case TradeResultCode.Conflict: return "并发冲突";
                case TradeResultCode.Disconnected: return "对方掉线";
                case TradeResultCode.ReturnBlocked: return "不可换回";
                default: return "等待中";
            }
        }

        private void StagePendingPrivate(uint gameId, int round, int seat, int instance, long value, int version)
        {
            hasPendingPrivate = true;
            pendingGameId = gameId;
            pendingRound = round;
            pendingSeat = seat;
            pendingInstance = instance;
            pendingValue = value;
            pendingVersion = version;
            Debug.Log("[Round] 私有值暂存 seat=" + seat + " round=" + round + " version=" + version);
        }

        /// <summary>交易资格：在线存活真人 + 存活可用 NPC（P0-04 接点）。</summary>
        public int TradableCount()
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || tradeFailures == null) return 0;
            var count = 0;
            for (var i = 0; i < tier; i++)
            {
                if (!alive[i]) continue;
                bool isNpc;
                bool isOnline;
                ulong clientId;
                if (!authority.TryGetSeatInfo(i, out isNpc, out isOnline, out clientId)) continue;
                if (!isNpc && !isOnline) continue;
                if (!TradeRules.CanUseEligibility(tradeFailures[i], tradeSuccessUsed[i])) continue;
                count++;
            }

            return count;
        }

        public bool TradePossible()
        {
            return TradableCount() >= 2;
        }

        public bool IsTradeOpen
        {
            get
            {
                if (IsServer) return initialized && TradePossible() && RoundStateMachine.IsTradePhase(machine.Phase) && Remaining > 0f;
                return RoundStateMachine.IsTradePhase(Phase) && Remaining > 0f && aliveCount.Value > 1;
            }
        }

        public bool TryGetTradePublic(int seat, out TradePublicState state)
        {
            for (var i = 0; i < tradePublic.Count; i++)
            {
                if (tradePublic[i].Seat == seat) { state = tradePublic[i]; return true; }
            }
            state = default;
            return false;
        }

        public bool IsSeatAlive(int seat)
        { 
            return IsServer ? seat >= 0 && seat < tier && alive[seat] : TryGetPublicItem(seat, out _);
        }

        private void SendPrivate(int seat)
        {
            if (!alive[seat]) return;
            if (!TryGetSeatClient(seat, out var clientId)) return;
            SendPrivate(seat, clientId);
        }

        public int LocalFailures { get { return localTradeFailures; } }
        public bool LocalTradeLocked { get { return localTradeLocked; } }
        public bool LocalTradeExhausted { get { return localTradeExhausted; } }
        private uint clientTradeOpId;
        public static bool DevShortRounds;
        public static int DevSeedOverride;
        public static string DevForcedValues;

        private bool LocalHeldHistoryAllowed => authority != null && !authority.IsLocalSpectator && Round > 0 && authority.GameId == heldHistoryGameId && heldHistorySeat == authority.LocalSeat && heldHistoryRound <= Round;
        public string LocalTradeHistory => LocalHeldHistoryAllowed ? localTradeHistory : string.Empty;
        public IReadOnlyList<HeldItemState> LocalHeldHistory => LocalHeldHistoryAllowed ? localHeldHistory : EmptyHeldHistory;
        public int LocalSeat => authority != null ? authority.LocalSeat : -1;
        public int BroadcastCount => clientBroadcast.Count;
        public int BroadcastTotal => clientBroadcastTotal;
        public int BroadcastRound => clientBroadcastRound;
        public bool BroadcastRevealed => clientBroadcastTotal >= 0;

        public string LocalTradeMessage { get { return localTradeMessage; } }
        public int TradeRequestCount { get { return clientTradeRequests.Count; } }

        public bool TryGetTradeRequest(int index, out uint id, out int fromSeat, out int toSeat, out int fromDef, out int toDef, out bool incoming)
        {
            id = 0; fromSeat = 0; toSeat = 0; fromDef = 0; toDef = 0; incoming = false;
            if (index < 0 || index >= clientTradeRequests.Count) return false;
            var r = clientTradeRequests[index];
            id = r.Id; fromSeat = r.From; toSeat = r.To; fromDef = r.FromDef; toDef = r.ToDef; incoming = r.Incoming;
            return true;
        }

        private int PairKey(int a, int b)
        {
            return a < b ? a * 100 + b : b * 100 + a;
        }

        private bool IsTradeWindowOpen()
        {
            return initialized && RoundStateMachine.IsTradePhase(machine.Phase) && Remaining > 0f && TradePossible();
        }

        private int SeatForClient(ulong clientId)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null) return -1;
            for (var i = 0; i < tier; i++)
            {
                bool isNpc; bool isOnline; ulong bound;
                if (!authority.TryGetSeatInfo(i, out isNpc, out isOnline, out bound)) continue;
                if (!isNpc && isOnline && bound == clientId) return i;
            }
            return -1;
        }

        private TradeOpKey OpKey(ulong clientId, uint opId)
        {
            return new TradeOpKey
            {
                GameId = authority != null ? authority.GameId : 0,
                Round = machine != null ? machine.Round : 0,
                Phase = machine != null ? (int)machine.Phase : 0,
                ClientId = clientId,
                OpId = opId
            };
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitTradeRequestServerRpc(uint opId, uint gameId, int round, int phaseCode, int targetSeat, int ownInstance, int targetInstance, int ownVersion, int targetVersion, int approachId, ServerRpcParams rpcParams = default)
        {
            var sender = rpcParams.Receive.SenderClientId;
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || gameId != authority.GameId || machine == null
                || round != machine.Round || phaseCode != (int)machine.Phase)
            {
                SendTradeState(sender, TradeResultCode.Expired);
                return;
            }

            var key = OpKey(sender, opId);
            if (opResults.TryGetValue(key, out var cached))
            {
                SendTradeState(sender, cached);
                return;
            }

            if (!IsTradeWindowOpen() || machine.PhaseEndsAt <= ServerNow()) { RecordOp(key, TradeResultCode.Expired); SendTradeState(sender, TradeResultCode.Expired); return; }
            var from = SeatForClient(sender);
            if (from < 0 || !alive[from]) { RecordOp(key, TradeResultCode.NoEligibility); SendTradeState(sender, TradeResultCode.NoEligibility); return; }
            if (tradeSuccessUsed[from] || tradeFailures[from] >= TradeRules.MaxFailuresPerSegment)
            {
                var code = tradeSuccessUsed[from] ? TradeResultCode.Locked : TradeResultCode.NoEligibility;
                RecordOp(key, code); SendTradeState(sender, code); return;
            }
            if (targetSeat < 0 || targetSeat >= tier || targetSeat == from || !alive[targetSeat])
            {
                RecordOp(key, TradeResultCode.InvalidTarget); SendTradeState(sender, TradeResultCode.InvalidTarget); return;
            }
            bool targetNpc; bool targetOnline; ulong targetClient;
            if (!authority.TryGetSeatInfo(targetSeat, out targetNpc, out targetOnline, out targetClient) || (!targetNpc && !targetOnline))
            {
                RecordOp(key, TradeResultCode.InvalidTarget); SendTradeState(sender, TradeResultCode.InvalidTarget); return;
            }
            if (!TradeRules.CanUseEligibility(tradeFailures[targetSeat], tradeSuccessUsed[targetSeat]))
            {
                RecordOp(key, TradeResultCode.NoEligibility); SendTradeState(sender, TradeResultCode.NoEligibility); return;
            }
            if (targetNpc && (approachId < 0 || approachId > 2))
            {
                RecordOp(key, TradeResultCode.Rejected); SendTradeState(sender, TradeResultCode.Rejected); return;
            }
            if (ownInstance != itemInstance[from] || targetInstance != itemInstance[targetSeat]
                || ownVersion != valueVersion[from] || targetVersion != valueVersion[targetSeat])
            {
                RecordOp(key, TradeResultCode.Stale); SendTradeState(sender, TradeResultCode.Stale); return;
            }
            if (pairRequests.ContainsKey(PairKey(from, targetSeat)))
            {
                RecordOp(key, TradeResultCode.Busy); SendTradeState(sender, TradeResultCode.Busy); return;
            }
            if (tradedOutInstances.Contains((from, itemInstance[targetSeat])))
            {
                RecordOp(key, TradeResultCode.ReturnBlocked); SendTradeState(sender, TradeResultCode.ReturnBlocked); return;
            }
            if (tradedOutInstances.Contains((targetSeat, itemInstance[from])))
            {
                RecordOp(key, TradeResultCode.ReturnBlocked); SendTradeState(sender, TradeResultCode.ReturnBlocked); return;
            }

            var terminal = CreateTradeRequest(from, targetSeat, gameId, phaseCode, targetNpc ? approachId : 1);
            RecordOp(key, terminal);
        }

        private TradeResultCode CreateTradeRequest(int from, int to, uint gameId, int phaseCode, int approach)
        {
            var request = new TradeRequest
            {
                Id = ++nextTradeRequestId,
                Round = machine.Round,
                GameId = gameId,
                Phase = phaseCode,
                From = from,
                To = to,
                FromInstance = itemInstance[from],
                ToInstance = itemInstance[to],
                FromDef = defIndex[from],
                ToDef = defIndex[to],
                FromVersion = valueVersion[from],
                ToVersion = valueVersion[to],
                FromNpc = npc[from],
                ToNpc = npc[to],
                Approach = approach
            };
            tradeRequests[request.Id] = request;
            pairRequests[PairKey(from, to)] = request.Id;

            if (request.ToNpc)
            {
                if (tradeSuccessUsed[request.To] || tradeFailures[request.To] >= TradeRules.MaxFailuresPerSegment)
                {
                    FinishRequest(request, TradeResultCode.NoEligibility, committed: false);
                    return TradeResultCode.NoEligibility;
                }
                var roll = NextRandom(10000);
                var probability = CurrentNpcProbability(request.Approach);
                var accept = TradeRules.NpcAccepts(probability, roll);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[Trade] NPC 响应 request=" + request.Id + " from=" + (from + 1) + " to=" + (to + 1) + " approach=" + request.Approach + " p=" + probability.ToString("0.00") + " roll=" + roll + " accepted=" + accept);
#endif
                if (accept) { CommitTrade(request); return TradeResultCode.Committed; }
                ResolveTradeRejection(request);
                return TradeResultCode.Rejected;
            }

            NotifyRequest(request, true);
            NotifyRequest(request, false);
            return TradeResultCode.Ok;
        }

        private float CurrentNpcProbability(int approach)
        {
            if (tradeConfig == null) return 0.5f;
            if (approach == 0) return tradeConfig.NpcFriendlyProbability;
            if (approach == 2) return tradeConfig.NpcHostileProbability;
            return tradeConfig.NpcAcceptProbability;
        }

        public string NpcApproachLabel(int approach)
        {
            if (tradeConfig == null) return approach == 0 ? "客气协商" : approach == 2 ? "施压" : "直接询问";
            return approach == 0 ? tradeConfig.NpcFriendlyText : approach == 2 ? tradeConfig.NpcHostileText : tradeConfig.NpcNeutralText;
        }



        private void NotifyRequest(TradeRequest request, bool incoming)
        {
            var targetSeat = incoming ? request.To : request.From;
            if (!TryGetSeatClient(targetSeat, out var clientId)) return;
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            TradeRequestClientRpc(request.Id, request.From, request.To, request.FromDef, request.ToDef,
                request.FromInstance, request.ToInstance, incoming, target);
        }

        private bool TryGetSeatClient(int seat, out ulong clientId)
        {
            clientId = SeatBindingRules.Unbound;
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (authority == null || seat < 0 || seat >= tier) return false;
            bool isNpc; bool isOnline; ulong bound;
            if (!authority.TryGetSeatInfo(seat, out isNpc, out isOnline, out bound)) return false;
            if (isNpc || !isOnline || bound == SeatBindingRules.Unbound) return false;
            if (NetworkManager == null || !HasConnectedClient(bound)) return false;
            clientId = bound;
            return true;
        }

        private void SendHistoryToSeat(int seat)
        {
            if (heldHistory == null || seat < 0 || seat >= tier || !alive[seat]) return;
            if (authority == null || !TryGetSeatClient(seat, out var clientId)) return;
            var history = heldHistory.ForSeat(seat);
            var defs = new int[history.Count];
            var instances = new int[history.Count];
            var historyValues = new long[history.Count];
            var rounds = new int[history.Count];
            var phases = new int[history.Count];
            var reasons = new int[history.Count];
            var trades = new int[history.Count];
            for (var i = 0; i < defs.Length; i++)
            {
                var entry = history[i];
                defs[i] = entry.DefIndex;
                instances[i] = entry.InstanceId;
                historyValues[i] = entry.ValueAtAcquisition;
                rounds[i] = entry.Round; phases[i] = entry.Phase;
                reasons[i] = (int)entry.Reason; trades[i] = entry.TradeNumber;
            }
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            TradeHistoryClientRpc(authority.GameId, machine != null && machine.Round > 0 ? machine.Round : 1, seat, defs, instances, historyValues, rounds, phases, reasons, trades, target);
        }

        [ClientRpc]
        private void TradeHistoryClientRpc(uint gameId, int round, int seat, int[] defs, int[] instances, long[] historyValues, int[] rounds, int[] phases, int[] reasons, int[] trades, ClientRpcParams rpcParams = default)
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            ReceiveLocalHeldHistory(gameId, round, seat, defs, instances, historyValues, rounds, phases, reasons, trades);
        }

        private void RecordOp(TradeOpKey key, TradeResultCode code)
        {
            opResults[key] = (int)code;
        }

        private void SendTradeState(ulong clientId, int code)
        {
            SendTradeState(clientId, (TradeResultCode)code);
        }

        private void SendTradeState(ulong clientId, TradeResultCode code)
        {
            var seat = SeatForClient(clientId);
            var failures = seat >= 0 && tradeFailures != null ? tradeFailures[seat] : 0;
            var locked = seat >= 0 && tradeSuccessUsed != null && tradeSuccessUsed[seat];
            var exhausted = seat >= 0 && tradeFailures != null && tradeFailures[seat] >= TradeRules.MaxFailuresPerSegment;
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            TradeStateClientRpc((int)code, failures, locked, exhausted, target);
        }

        [ClientRpc]
        private void TradeStateClientRpc(int code, int failures, bool locked, bool exhausted, ClientRpcParams rpcParams = default)
        {
            localTradeFailures = failures;
            localTradeLocked = locked;
            localTradeExhausted = exhausted;
            if (code == (int)TradeResultCode.Ok) return;
            localTradeMessage = exhausted && code == (int)TradeResultCode.NoEligibility
                ? TradeResultMessage(TradeResultCode.Rejected, incoming: false, failures, exhausted)
                : TradeMessage((TradeResultCode)code);
            Debug.Log("[Trade] 状态回执 code=" + ((TradeResultCode)code) + " failures=" + failures + " locked=" + locked);
        }

        [ClientRpc]
        private void TradeRequestClientRpc(uint requestId, int fromSeat, int toSeat, int fromDef, int toDef,
            int fromInstance, int toInstance, bool incoming, ClientRpcParams rpcParams = default)
        {
            for (var i = 0; i < clientTradeRequests.Count; i++)
            {
                if (clientTradeRequests[i].Id == requestId) return;
            }
            clientTradeRequests.Add(new TradeClientRequest
            {
                Id = requestId, From = fromSeat, To = toSeat, FromDef = fromDef, ToDef = toDef,
                FromInstance = fromInstance, ToInstance = toInstance, Incoming = incoming
            });
            Debug.Log("[Trade] 请求通知 request=" + requestId + " from=" + (fromSeat + 1) + " to=" + (toSeat + 1) + " incoming=" + incoming);
        }

    [ServerRpc(RequireOwnership = false)]
    private void RespondTradeServerRpc(uint opId, uint gameId, int round, int phaseCode, uint requestId, bool accept, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (authority == null)
        {
            authority = FindObjectOfType<MatchAuthority>();
        }
        if (authority == null || gameId != authority.GameId || machine == null || round != machine.Round || phaseCode != (int)machine.Phase)
        {
            SendTradeState(senderClientId, TradeResultCode.Expired);
            return;
        }
        TradeOpKey key = OpKey(senderClientId, opId);
        if (opResults.TryGetValue(key, out var value))
        {
            SendTradeState(senderClientId, (TradeResultCode)value);
            return;
        }
        int num = SeatForClient(senderClientId);
        TradeRequest value2;
        bool isNpc;
        bool isOnline;
        ulong clientId;
        bool isNpc2;
        bool isOnline2;
        ulong clientId2;
        if (num < 0)
        {
            RecordOp(key, TradeResultCode.NoEligibility);
            SendTradeState(senderClientId, TradeResultCode.NoEligibility);
        }
        else if (!tradeRequests.TryGetValue(requestId, out value2))
        {
            RecordOp(key, TradeResultCode.Expired);
            SendTradeState(senderClientId, TradeResultCode.Expired);
        }
        else if (value2.To != num)
        {
            RecordOp(key, TradeResultCode.InvalidTarget);
            SendTradeState(senderClientId, TradeResultCode.InvalidTarget);
        }
        else if (value2.GameId != gameId || value2.Round != round || value2.Phase != phaseCode)
        {
            RecordOp(key, TradeResultCode.Expired);
            FinishRequest(value2, TradeResultCode.Expired, committed: false);
        }
        else if (!initialized || !RoundStateMachine.IsTradePhase(machine.Phase) || machine.PhaseEndsAt <= ServerNow())
        {
            RecordOp(key, TradeResultCode.Expired);
            FinishRequest(value2, TradeResultCode.Expired, committed: false);
        }
        else if (!authority.TryGetSeatInfo(value2.From, out isNpc, out isOnline, out clientId) || !authority.TryGetSeatInfo(value2.To, out isNpc2, out isOnline2, out clientId2) || !alive[value2.From] || !alive[value2.To] || (!isNpc && !isOnline) || (!isNpc2 && !isOnline2))
        {
            RecordOp(key, TradeResultCode.NoEligibility);
            FinishRequest(value2, TradeResultCode.InvalidTarget, committed: false);
        }
        else if (!accept)
        {
            // Declining a live request doesn't require its proposed swap to still be committable.
            RecordOp(key, TradeResultCode.Rejected);
            ResolveTradeRejection(value2);
        }
        else if (!TradeRules.CanUseEligibility(tradeFailures[value2.From], tradeSuccessUsed[value2.From]) ||
                 !TradeRules.CanUseEligibility(tradeFailures[value2.To], tradeSuccessUsed[value2.To]))
        {
            RecordOp(key, TradeResultCode.Conflict);
            FinishRequest(value2, TradeResultCode.Conflict, committed: false);
        }
        else if (itemInstance[value2.From] != value2.FromInstance || itemInstance[value2.To] != value2.ToInstance || valueVersion[value2.From] != value2.FromVersion || valueVersion[value2.To] != value2.ToVersion)
        {
            RecordOp(key, TradeResultCode.Stale);
            FinishRequest(value2, TradeResultCode.Stale, committed: false);
        }
        else if (tradedOutInstances.Contains((value2.From, value2.ToInstance)) ||
            tradedOutInstances.Contains((value2.To, value2.FromInstance)))
        {
            RecordOp(key, TradeResultCode.ReturnBlocked);
            FinishRequest(value2, TradeResultCode.ReturnBlocked, committed: false);
        }
        else
        {
            RecordOp(key, TradeResultCode.Committed);
            CommitTrade(value2);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void WithdrawTradeServerRpc(uint opId, uint gameId, int round, int phaseCode, uint requestId, ServerRpcParams rpcParams = default)
    {
        ulong senderClientId = rpcParams.Receive.SenderClientId;
        if (authority == null)
        {
            authority = FindObjectOfType<MatchAuthority>();
        }
        if (authority == null || gameId != authority.GameId || machine == null || round != machine.Round || phaseCode != (int)machine.Phase)
        {
            SendTradeState(senderClientId, TradeResultCode.Expired);
            return;
        }
        TradeOpKey key = OpKey(senderClientId, opId);
        if (opResults.TryGetValue(key, out var value))
        {
            SendTradeState(senderClientId, (TradeResultCode)value);
            return;
        }
        int num = SeatForClient(senderClientId);
        if (num >= 0)
        {
            if (!tradeRequests.TryGetValue(requestId, out var value2) || value2.From != num || value2.GameId != gameId || value2.Round != round || value2.Phase != phaseCode)
            {
                RecordOp(key, TradeResultCode.Expired);
                SendTradeState(senderClientId, TradeResultCode.Expired);
            }
            else
            {
                RecordOp(key, TradeResultCode.Withdrawn);
                FinishRequest(value2, TradeResultCode.Withdrawn, committed: false);
            }
        }
    }

    private void CommitTrade(TradeRequest request)
    {
        if (!tradeRequests.ContainsKey(request.Id)) return;
        tradeRequests.Remove(request.Id);
        pairRequests.Remove(PairKey(request.From, request.To));
        int num = itemInstance[request.From];
        int num2 = itemInstance[request.To];
        int num3 = defIndex[request.From];
        int num4 = defIndex[request.To];
        long num5 = values[request.From];
        long num6 = values[request.To];
        itemInstance[request.From] = num2;
        itemInstance[request.To] = num;
        defIndex[request.From] = num4;
        defIndex[request.To] = num3;
        values[request.From] = num6;
        values[request.To] = num5;
        valueBatchVersion++;
        valueVersion[request.From] = valueBatchVersion;
        valueVersion[request.To] = valueBatchVersion;
        tradedOutInstances.Add((request.From, num));
        tradedOutInstances.Add((request.To, num2));
        tradeSuccessUsed[request.From] = true;
        tradeSuccessUsed[request.To] = true;
        Debug.Log("[Trade] 换回校验 from=" + (request.From + 1) + " item=" + itemInstance[request.From] + " to=" + (request.To + 1) + " item=" + itemInstance[request.To]);
        UpdatePublicTradeItem(request.From);
        UpdatePublicTradeItem(request.To);
        SendReturnBadgesToAll();
        var tradeSequence = RecordCommittedTrade(request, num3, num4);
        AppendHistory(request.From, tradeSequence);
        AppendHistory(request.To, tradeSequence);
        TerminateOtherRequests(request.From, request.Id);
        TerminateOtherRequests(request.To, request.Id);
        SendPrivate(request.From);
        SendPrivate(request.To);
        PublishTradeState(request.From);
        PublishTradeState(request.To);
        SendTradeResult(request, TradeResultCode.Committed, committed: true);
        Debug.Log("[Trade] 成交 request=" + request.Id + " from=" + (request.From + 1) + " to=" + (request.To + 1) + " fromItem=" + itemInstance[request.From] + " toItem=" + itemInstance[request.To]);
    }

    private void AppendHistory(int seat, int tradeSequence)
    {
        if (heldHistory != null && seat >= 0 && seat < tier)
        {
            heldHistory.Record(seat, machine.Round, machine.Phase, ValueHistoryReason.Trade,
                itemInstance[seat], defIndex[seat], values[seat], tradeSequence);
            SendHistoryToSeat(seat);
        }
    }

    private string BuildHeldHistoryText(int seat)
    {
        StringBuilder stringBuilder = new StringBuilder();
        var list = heldHistory.ForSeat(seat);
        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                stringBuilder.Append(" → ");
            }
            stringBuilder.Append(GetItemName(list[i].DefIndex));
        }
        return stringBuilder.ToString();
    }

    private void UpdatePublicTradeItem(int seat)
    {
        for (int i = 0; i < publicItems.Count; i++)
        {
            if (publicItems[i].Seat == seat)
            {
                PublicItemState value = publicItems[i];
                value.InstanceId = itemInstance[seat];
                value.DefIndex = defIndex[seat];
                value.ValueVersion = valueVersion[seat];
                publicItems[i] = value;
                break;
            }
        }
    }

    private void TerminateOtherRequests(int seat, uint exceptId)
    {
        List<uint> list = new List<uint>();
        foreach (KeyValuePair<uint, TradeRequest> tradeRequest in tradeRequests)
        {
            if (tradeRequest.Key != exceptId && (tradeRequest.Value.From == seat || tradeRequest.Value.To == seat))
            {
                list.Add(tradeRequest.Key);
            }
        }
        foreach (uint item in list)
        {
            if (tradeRequests.TryGetValue(item, out var value))
            {
                FinishRequest(value, TradeResultCode.Conflict, committed: false);
            }
        }
    }

    private void ResolveTradeRejection(TradeRequest request)
    {
        if (!tradeRequests.Remove(request.Id)) return;
        pairRequests.Remove(PairKey(request.From, request.To));
        if (tradeFailures[request.From] < TradeRules.MaxFailuresPerSegment)
        {
            tradeFailures[request.From]++;
        }
        PublishTradeState(request.From);
        Debug.Log("[Trade] 拒绝 request=" + request.Id + " from=" + (request.From + 1) + " failures=" + tradeFailures[request.From]);
        if (tradeFailures[request.From] >= TradeRules.MaxFailuresPerSegment)
        {
            TerminateOtherRequests(request.From, 0u);
            Debug.Log("[Trade] 发起席本段资格耗尽 seat=" + (request.From + 1));
        }
        SendTradeResult(request, TradeResultCode.Rejected, committed: false);
    }

    private void FinishRequest(TradeRequest request, TradeResultCode code, bool committed)
    {
        tradeRequests.Remove(request.Id);
        pairRequests.Remove(PairKey(request.From, request.To));
        Debug.Log("[Trade] 终结 request=" + request.Id + " code=" + code.ToString() + " from=" + (request.From + 1) + " to=" + (request.To + 1));
        SendTradeResult(request, code, committed);
    }

    private void SendTradeResult(TradeRequest request, TradeResultCode code, bool committed)
    {
        SendTradeResultTo(request.From, request, code, committed, incoming: false);
        SendTradeResultTo(request.To, request, code, committed, incoming: true);
    }

    private void SendTradeResultTo(int seat, TradeRequest request, TradeResultCode code, bool committed, bool incoming)
    {
        if (TryGetSeatClient(seat, out var clientId))
        {
            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[1] { clientId }
                }
            };
            int myNewInstance = (committed ? itemInstance[seat] : request.FromInstance);
            int myNewDef = (committed ? defIndex[seat] : request.FromDef);
            int myNewVersion = (committed ? valueVersion[seat] : request.FromVersion);
            TradeResultClientRpc(request.Id, (int)code, request.From, request.To, myNewInstance, myNewDef, myNewVersion, tradeFailures[seat], tradeSuccessUsed[seat], tradeFailures[seat] >= 2, incoming, rpcParams);
        }
    }

    [ClientRpc]
    private void TradeResultClientRpc(uint requestId, int code, int fromSeat, int toSeat, int myNewInstance, int myNewDef, int myNewVersion, int failures, bool locked, bool exhausted, bool incoming, ClientRpcParams rpcParams = default)
    {
            uint requestId2 = requestId;
            clientTradeRequests.RemoveAll((TradeClientRequest r) => r.Id == requestId2);
            localTradeFailures = failures;
            localTradeLocked = locked;
            localTradeExhausted = exhausted;
            localTradeMessage = TradeResultMessage((TradeResultCode)code, incoming, failures, exhausted);
            string[] obj = new string[8]
            {
                "[Trade] 客户端结果 request=",
                requestId2.ToString(),
                " code=",
                null,
                null,
                null,
                null,
                null
            };
            TradeResultCode tradeResultCode = (TradeResultCode)code;
            obj[3] = tradeResultCode.ToString();
            obj[4] = " seat=";
            obj[5] = (incoming ? (toSeat + 1) : (fromSeat + 1)).ToString();
            obj[6] = " failures=";
            obj[7] = failures.ToString();
            Debug.Log(string.Concat(obj) + " exhausted=" + exhausted + " message=" + localTradeMessage);
            if (code == 1)
            {
                Debug.Log("[Trade] 回执成交 request=" + requestId2 + " seat=" + (incoming ? (toSeat + 1) : (fromSeat + 1)));
            }
    }

    private static string TradeResultMessage(TradeResultCode code, bool incoming, int failures, bool exhausted)
    {
        if (code != TradeResultCode.Rejected) return TradeMessage(code);
        if (incoming) return "已拒绝交易";
        var count = $"{failures}/{TradeRules.MaxFailuresPerSegment}";
        return exhausted ? $"交易用尽（{count}）" : $"对方拒绝（{count}）";
    }

    public void SendTradeRequest(int targetSeat)
    {
        SendTradeRequest(targetSeat, 1);
    }

    public void SendTradeRequest(int targetSeat, int approachId)
    {
        if (base.IsSpawned && base.IsClient)
        {
            if (authority == null)
            {
                authority = FindObjectOfType<MatchAuthority>();
            }
            if (!(authority == null) && !authority.IsLocalSpectator && IsTradeOpen && authority.LocalSeat >= 0 && TryGetPublicItem(authority.LocalSeat, out var item) && TryGetPublicItem(targetSeat, out var item2))
            {
                clientTradeOpId++;
                var opId = clientTradeOpId;
                var gameId = authority.GameId;
                var round = Round;
                var phase = (int)Phase;
                var ownInstance = item.InstanceId;
                var targetInstance = item2.InstanceId;
                var ownVersion = item.ValueVersion;
                var targetVersion = item2.ValueVersion;
                var effectiveApproach = Mathf.Clamp(approachId, 0, 2);
                localTradeMessage = "发送中…";
                SubmitTradeRequestServerRpc(opId, gameId, round, phase, targetSeat, ownInstance, targetInstance, ownVersion, targetVersion, effectiveApproach);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (item2.IsNpc == 1 && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-trade-replay-op") >= 0)
                {
                    Debug.Log("[Trade] Dev 重放同 opId=" + opId);
                    SubmitTradeRequestServerRpc(opId, gameId, round, phase, targetSeat, ownInstance, targetInstance, ownVersion, targetVersion, effectiveApproach);
                }
#endif
            }
        }
    }

    public void ConfirmTrade(uint requestId)
    {
        if (base.IsClient)
        {
            clientTradeOpId++;
            RespondTradeServerRpc(clientTradeOpId, authority.GameId, Round, (int)Phase, requestId, accept: true);
        }
    }

    public void RejectTrade(uint requestId)
    {
        if (base.IsClient)
        {
            clientTradeOpId++;
            RespondTradeServerRpc(clientTradeOpId, authority.GameId, Round, (int)Phase, requestId, accept: false);
        }
    }

    public void WithdrawTrade(uint requestId)
    {
        if (base.IsClient)
        {
            clientTradeOpId++;
            WithdrawTradeServerRpc(clientTradeOpId, authority.GameId, Round, (int)Phase, requestId);
        }
    }

    public string GetSeatLabel(int seat)
    {
        if (!TryGetPublicItem(seat, out var item))
        {
            return "P" + (seat + 1);
        }
        return "P" + (seat + 1) + " " + GetItemGlyph(item.DefIndex) + GetItemName(item.DefIndex);
    }

    private void SendTradeStateToSeat(int seat)
    {
        if (tradeFailures != null && seat >= 0 && seat < tier && TryGetSeatClient(seat, out var clientId))
        {
            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[1] { clientId }
                }
            };
            TradeStateClientRpc(0, tradeFailures[seat], tradeSuccessUsed[seat], tradeFailures[seat] >= 2, rpcParams);
        }
    }

    private void ResetTradeSegment()
    {
        if (tradeFailures != null)
        {
            ExpireTradeRequests(TradeResultCode.Expired);
            opResults.Clear();
            for (int i = 0; i < tier; i++)
            {
                tradeFailures[i] = 0;
                tradeSuccessUsed[i] = false;
                PublishTradeState(i);
            }
        }
    }

    private void PublishTradeState(int seat)
    {
        for (int i = 0; i < tradePublic.Count; i++)
        {
            if (tradePublic[i].Seat == seat)
            {
                bool isNpc;
                bool isOnline;
                ulong clientId;
                bool flag = authority != null && authority.TryGetSeatInfo(seat, out isNpc, out isOnline, out clientId) && (isNpc || isOnline);
                bool flag2 = tradeFailures[seat] >= 2 || tradeSuccessUsed[seat];
                tradePublic[i] = new TradePublicState
                {
                    Seat = seat,
                    Exhausted = (flag2 ? 1 : 0),
                    Tradable = ((alive[seat] && flag && !flag2) ? 1 : 0)
                };
                break;
            }
        }
        if (TryGetSeatClient(seat, out var clientId2))
        {
            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[1] { clientId2 }
                }
            };
            TradeStateClientRpc(0, tradeFailures[seat], tradeSuccessUsed[seat], tradeFailures[seat] >= 2, rpcParams);
        }
    }

    private void ExpireTradeRequests(TradeResultCode code)
    {
        if (tradeRequests.Count == 0) return;
        List<uint> list = new List<uint>(tradeRequests.Keys);
        foreach (uint item in list)
        {
            if (tradeRequests.TryGetValue(item, out var value))
            {
                FinishRequest(value, code, committed: false);
            }
        }
        pairRequests.Clear();
    }

    private int NextRandom(int maxExclusive)
    {
        if (maxExclusive <= 1)
        {
            return 0;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (DevSeedOverride != 0 || DevShortRounds)
        {
            if (devRandom == null)
            {
                devRandom = new SystemRandomSource((DevSeedOverride != 0) ? DevSeedOverride : Environment.TickCount);
            }
            return devRandom.Next(maxExclusive);
        }
#endif
        return SecureRandom.Next(maxExclusive);
    }

    private BroadcastWorld BuildBroadcastWorld()
    {
        BroadcastWorld broadcastWorld = new BroadcastWorld();
        for (int i = 0; i < tier; i++)
        {
            if (alive[i])
            {
                broadcastWorld.Items.Add(new PublicItemRef
                {
                    Seat = i,
                    InstanceId = itemInstance[i],
                    Name = GetItemName(defIndex[i]),
                    PlayerName = authority != null ? authority.PlayerNameForSeat(i) : null,
                    Category = catalog.Definitions[defIndex[i]].Category,
                    Value = values[i]
                });
            }
        }
        return broadcastWorld;
    }

    private void StartBroadcast()
    {
        if (base.IsServer)
        {
            BroadcastConfigAsset broadcastConfigAsset = broadcastConfig;
            BroadcastWorld world = BuildBroadcastWorld();
            BroadcastRules.MarketRumorPlan plan = default;
            if (machine.Round < machine.TotalRounds)
            {
                PlanMarket(machine.Round);
                float chance = ((Array.IndexOf(Environment.GetCommandLineArgs(), "-worthless-force-market-rumor") >= 0) ? 1f : broadcastConfigAsset.RumorChance);
                BroadcastRules.TryBuildMarketRumor(plannedMarket.Categories, plannedMarket.BasisPoints, false, chance, new BroadcastRandom(NextRandom), out plan);
            }
            var random = new BroadcastRandom(NextRandom);
            BroadcastRules.BroadcastBatch broadcastBatch = BroadcastRules.BuildRedactedBatch(world, new BroadcastBatchOptions
            {
                ItemCount = broadcastConfigAsset.ItemCount, FakeMin = broadcastConfigAsset.FakeMin, FakeMax = broadcastConfigAsset.FakeMax,
                ExactRankPercent = broadcastConfigAsset.ExactRankPercent,
                ZeroLieWeight = broadcastConfigAsset.ZeroLieWeight, OneLieWeight = broadcastConfigAsset.OneLieWeight, TwoLieWeight = broadcastConfigAsset.TwoLieWeight
            }, random, plan);
            var seats = new List<int>();
            for (var seat = 0; seat < tier; seat++) if (alive[seat]) seats.Add(seat);
            broadcastSeatViews = new BroadcastSeatViews(broadcastBatch, seats, new BroadcastRedactionOptions
            {
                NoRedactionPercent = broadcastConfigAsset.NoRedactionPercent,
                DoubleRedactionPercent = broadcastConfigAsset.DoubleRedactionPercent,
                FullRedactionPercent = broadcastConfigAsset.FullRedactionPercent
            }, random);
            this.broadcastBatch.Clear();
            for (int i = 0; i < broadcastBatch.Items.Count; i++)
            {
                this.broadcastBatch.Add(broadcastBatch.Items[i]);
            }
            broadcastFakeCount = broadcastBatch.FakeCount;
            broadcastPublished = 0;
            broadcastRevealed = false;
            broadcastNextAt = ServerNow();
            int num = BroadcastRules.BatchDurationSeconds(this.broadcastBatch.Count);
            machine.SetPhaseEnd(broadcastNextAt + (double)num);
            Debug.Log("[Broadcast] 批次开始 items=" + this.broadcastBatch.Count + " duration=" + num + " round=" + machine.Round);
            PublishNextBroadcast(ServerNow());
        }
    }

    private void TickBroadcast(double now)
    {
        if (!base.IsServer || !initialized) return;
        if (broadcastPublished >= broadcastBatch.Count)
        {
            if (broadcastNextAt > 0.0 && now >= broadcastNextAt)
            {
                machine.SetPhaseEnd(broadcastNextAt);
            }
        }
        else if (!(now < broadcastNextAt))
        {
            PublishNextBroadcast(now);
        }
    }

    private void PublishNextBroadcast(double now)
    {
        if (broadcastPublished >= broadcastBatch.Count) return;
        int order = broadcastPublished;
        broadcastPublished++;
        for (int i = 0; i < tier; i++)
        {
            if (alive[i] && TryGetSeatClient(i, out var clientId))
            {
                ClientRpcParams rpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[1] { clientId }
                    }
                };
                BroadcastItemClientRpc((authority != null) ? authority.GameId : 0u, machine.Round, order, BroadcastTextForSeat(i, order), rpcParams);
            }
        }
        Debug.Log("[Broadcast] 发布 round=" + machine.Round + " order=" + order + " items=" + broadcastBatch.Count + " t=" + ServerNow().ToString("0.0"));
        if (broadcastPublished < broadcastBatch.Count)
        {
            broadcastNextAt = now + (double)broadcastConfig.IntervalSeconds;
            int num = broadcastBatch.Count - broadcastPublished;
            double num2 = broadcastNextAt + (double)((num - 1) * broadcastConfig.IntervalSeconds) + (double)broadcastConfig.LastHoldSeconds;
            if (machine.PhaseEndsAt < num2 - 0.01)
            {
                machine.SetPhaseEnd(num2);
            }
        }
        else
        {
            broadcastNextAt = now + (double)broadcastConfig.LastHoldSeconds;
            machine.SetPhaseEnd(broadcastNextAt);
        }
    }

    private void RevealBroadcastTotal()
    {
        if (!base.IsServer) return;
        int num = 0;
        for (int i = 0; i < broadcastPublished && i < broadcastBatch.Count; i++)
        {
            if (!broadcastBatch[i].Truth)
            {
                num++;
            }
        }
        broadcastFakeCount = num;
        broadcastRevealed = true;
        for (int j = 0; j < tier; j++)
        {
            if (alive[j] && TryGetSeatClient(j, out var clientId))
            {
                ClientRpcParams rpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[1] { clientId }
                    }
                };
                BroadcastTotalClientRpc((authority != null) ? authority.GameId : 0u, machine.Round, broadcastFakeCount, rpcParams);
            }
        }
        Debug.Log("[Broadcast] 揭晓 round=" + machine.Round + " total=" + broadcastFakeCount);
    }

    private void ClearBroadcast()
    {
        broadcastBatch.Clear();
        broadcastSeatViews = null;
        broadcastPublished = 0;
        broadcastRevealed = false;
        broadcastFakeCount = 0;
        broadcastNextAt = 0.0;
        if (!base.IsServer) return;
        for (int i = 0; i < tier; i++)
        {
            if (TryGetSeatClient(i, out var clientId))
            {
                ClientRpcParams rpcParams = new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new ulong[1] { clientId }
                    }
                };
                BroadcastClearClientRpc((authority != null) ? authority.GameId : 0u, (machine != null) ? machine.Round : 0, rpcParams);
            }
        }
    }

    private string BroadcastTextForSeat(int seat, int order)
    {
        return broadcastSeatViews?.GetPublished(seat, order, broadcastPublished) ?? BroadcastRules.FullyRedactedText;
    }

    private void SendBroadcastCatchUp(int seat)
    {
        if (base.IsServer && alive[seat] && TryGetSeatClient(seat, out var clientId) && machine.Phase >= RoundPhase.Broadcast)
        {
            ClientRpcParams rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[1] { clientId }
                }
            };
            for (int i = 0; i < broadcastPublished; i++)
            {
                BroadcastItemClientRpc((authority != null) ? authority.GameId : 0u, machine.Round, i, BroadcastTextForSeat(seat, i), rpcParams);
            }
            if (broadcastRevealed)
            {
                BroadcastTotalClientRpc((authority != null) ? authority.GameId : 0u, machine.Round, broadcastFakeCount, rpcParams);
            }
        }
    }

    [ClientRpc]
    private void BroadcastItemClientRpc(uint gameId, int round, int order, string text, ClientRpcParams rpcParams = default)
    {
        if (authority == null)
        {
            authority = FindObjectOfType<MatchAuthority>();
        }
        if (!(authority == null) && gameId == authority.GameId && !authority.IsLocalSpectator && round >= clientBroadcastRound)
        {
            if (round != clientBroadcastRound)
            {
                clientBroadcast.Clear();
                clientBroadcastTotal = -1;
                clientBroadcastRound = round;
                clientBroadcastGame = gameId;
            }
            if (order == clientBroadcast.Count)
            {
                clientBroadcast.Add(text ?? string.Empty);
                Debug.Log("[Broadcast] 接收 round=" + round + " order=" + order + " hash=" + TextHash(text));
            }
            else if (order <= clientBroadcast.Count)
            {
            }
        }
    }

    [ClientRpc]
    private void BroadcastTotalClientRpc(uint gameId, int round, int total, ClientRpcParams rpcParams = default)
    {
        if (authority == null)
        {
            authority = FindObjectOfType<MatchAuthority>();
        }
        if (!(authority == null) && gameId == authority.GameId && !authority.IsLocalSpectator && round >= clientBroadcastRound)
        {
            if (round != clientBroadcastRound)
            {
                clientBroadcast.Clear();
                clientBroadcastRound = round;
                clientBroadcastGame = gameId;
            }
            clientBroadcastTotal = total;
            Debug.Log("[Broadcast] 客户端揭示 round=" + round + " total=" + total);
        }
    }

    [ClientRpc]
    private void BroadcastClearClientRpc(uint gameId, int round, ClientRpcParams rpcParams = default)
    {
        {
            if (authority == null)
            {
                authority = FindObjectOfType<MatchAuthority>();
            }
            if (!(authority == null) && gameId == authority.GameId)
            {
                clientBroadcast.Clear();
                clientBroadcastTotal = -1;
                clientBroadcastRound = 0;
            }
        }
    }

    public bool TryGetBroadcastLine(int index, out string text)
    {
        text = null;
        if (index < 0 || index >= clientBroadcast.Count)
        {
            return false;
        }
        text = clientBroadcast[index];
        return true;
    }

    private void ClearClientBroadcast()
    {
        clientBroadcast.Clear();
        clientBroadcastTotal = -1;
        clientBroadcastRound = 0;
        clientBroadcastGame = 0u;
    }

    private static string TextHash(string value)
    {
        uint num = 2166136261u;
        if (value != null)
        {
            foreach (char c in value)
            {
                num ^= c;
                num *= 16777619;
            }
        }
        return num.ToString("x8");
    }

    private int CountAlive()
    {
        int num = 0;
        for (int i = 0; i < tier; i++)
        {
            if (alive[i])
            {
                num++;
            }
        }
        return num;
    }

    private void SyncVariables()
    {
        roundNumber.Value = machine.Round;
        phase.Value = (int)machine.Phase;
        phaseDeadline.Value = machine.PhaseEndsAt;
        aliveCount.Value = CountAlive();
    }

    public static long DevForcedValue(int seat)
    {
        if (string.IsNullOrEmpty(DevForcedValues))
        {
            return -1L;
        }
        string[] array = DevForcedValues.Split(',');
        if (seat >= array.Length)
        {
            return -1L;
        }
        long result;
        return long.TryParse(array[seat], out result) ? result : (-1);
    }
    }
    internal static class SecureRandom
    {
        public static int Next(int maxExclusive)
        {
            if (maxExclusive <= 1) return 0;
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            var bytes = new byte[4]; rng.GetBytes(bytes);
            var n = (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
            return (int)(n % (uint)maxExclusive);
        }
    }

    internal sealed class BroadcastRandom : IRandomSource
    {
        private readonly Func<int, int> next;
        public BroadcastRandom(Func<int, int> next) { this.next = next; }
        public int Next(int maxExclusive) { return next(maxExclusive); }
    }

}
