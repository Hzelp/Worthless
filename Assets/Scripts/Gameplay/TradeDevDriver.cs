#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    public sealed class TradeDevDriver : MonoBehaviour
    {
        private int targetSeat = -1;
        private int targetSeat2 = -1;
        private int[] segmentTargets;
        private int approach = 1;
        private string respond = "";
        private bool repeatPerSegment;
        private bool staleAttempt;
        private float delay = 1.5f;
        private float sendDelay = 1.5f;
        private float tradeOpenElapsed;
        private float respondDelay = 1.2f;
        private float staleAfter = -1f;
        private float elapsed;
        private readonly List<int> sentSegments = new List<int>();
        private readonly HashSet<uint> handledRequests = new HashSet<uint>();
        private double respondAt;
        private int responseCount;
        private uint lastRequestId;
        private uint firstStaleId;
        private bool exhaustedRetried;
        private int retryPhase = -1;
        private bool staleDone;

        private void Awake()
        {
            var args = System.Environment.GetCommandLineArgs();
            targetSeat = IntArg(args, "-worthless-trade-send", -1);
            targetSeat2 = IntArg(args, "-worthless-trade-send2", -1);
            var segments = StringArg(args, "-worthless-trade-segments").Split(',');
            if (segments.Length == 3 && int.TryParse(segments[0], out var first) &&
                int.TryParse(segments[1], out var second) && int.TryParse(segments[2], out var third))
                segmentTargets = new[] { first, second, third };
            approach = IntArg(args, "-worthless-trade-approach", 1);
            respond = StringArg(args, "-worthless-trade-respond");
            repeatPerSegment = segmentTargets != null || System.Array.IndexOf(args, "-worthless-trade-repeat") >= 0;
            staleAttempt = System.Array.IndexOf(args, "-worthless-trade-stale") >= 0;
            respondDelay = FloatArg(args, "-worthless-trade-respond-delay", 1.2f);
            staleAfter = FloatArg(args, "-worthless-trade-stale-after", -1f);
            sendDelay = FloatArg(args, "-worthless-trade-send-delay", 1.5f);
            Debug.Log("[Trade] Dev driver ready target=" + targetSeat + " respond=" + respond);
        }

        private static int IntArg(string[] args, string key, int fallback)
        {
            var i = System.Array.IndexOf(args, key);
            if (i < 0 || i + 1 >= args.Length) return fallback;
            int v;
            return int.TryParse(args[i + 1], out v) ? v : fallback;
        }

        private static float FloatArg(string[] args, string key, float fallback)
        {
            var i = System.Array.IndexOf(args, key);
            if (i < 0 || i + 1 >= args.Length) return fallback;
            float v;
            return float.TryParse(args[i + 1], out v) ? v : fallback;
        }

        private static string StringArg(string[] args, string key)
        {
            var i = System.Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
        }

        private void Update()
        {
            elapsed += Time.unscaledDeltaTime;
            var view = FindObjectOfType<TradeHudView>();
            var director = FindObjectOfType<MatchRoundDirector>();
            var authority = FindObjectOfType<MatchAuthority>();
            if (view == null || director == null || authority == null) return;
            if (!director.IsTradeOpen)
            {
                tradeOpenElapsed = 0f;
                exhaustedRetried = false;
                return;
            }
            tradeOpenElapsed += Time.unscaledDeltaTime;

            var segmentKey = director.Round * 100 + (int)director.Phase;
            if (retryPhase != segmentKey) { retryPhase = segmentKey; exhaustedRetried = false; }

            var localSeat = authority.LocalSeat;
            var phase = director.Phase;
            var selectedTarget = segmentTargets == null ? targetSeat :
                segmentTargets[phase == RoundPhase.FirstTrade ? 0 : phase == RoundPhase.SecondTrade ? 1 : 2];
            var ownReady = localSeat >= 0 && director.TryGetPublicItem(localSeat, out var own);
            var targetReady = selectedTarget >= 0 && director.TryGetPublicItem(selectedTarget, out var targetItem);

            if (tradeOpenElapsed >= sendDelay && ownReady && targetReady)
            {
                if (!repeatPerSegment && sentSegments.Count == 0)
                {
                    sentSegments.Add(segmentKey);
                    Debug.Log("[Trade] Dev 发送目标=" + (selectedTarget + 1));
                    view.DevSelectAndSend(selectedTarget, approach);
                    if (targetSeat2 >= 0) view.DevSelectAndSend(targetSeat2, approach);
                }
                else if (repeatPerSegment && !sentSegments.Contains(segmentKey))
                {
                    sentSegments.Add(segmentKey);
                    Debug.Log("[Trade] Dev 本段发送目标=" + (selectedTarget + 1) + " round=" + director.Round + " phase=" + phase);
                    view.DevSelectAndSend(selectedTarget, approach);
                    if (targetSeat2 >= 0) view.DevSelectAndSend(targetSeat2, approach);
                }
            }

            if (director.LocalTradeExhausted && !exhaustedRetried && ownReady)
            {
                exhaustedRetried = true;
                Debug.Log("[Trade] Dev 本段耗尽后再试");
                view.DevSelectAndSend(selectedTarget, approach);
            }

            if (!string.IsNullOrEmpty(respond))
            {
                var pending = false;
                for (var i = 0; i < director.TradeRequestCount; i++)
                {
                    director.TryGetTradeRequest(i, out var id, out _, out _, out _, out _, out var incoming);
                    if (!incoming || handledRequests.Contains(id)) continue;
                    lastRequestId = id;
                    if (firstStaleId == 0) firstStaleId = id;
                    pending = true;
                    if (respondAt <= 0) respondAt = elapsed + respondDelay;
                    break;
                }
                if (pending && respondAt > 0 && elapsed >= respondAt)
                {
                    respondAt = 0;
                    for (var i = 0; i < director.TradeRequestCount; i++)
                    {
                        director.TryGetTradeRequest(i, out var id, out _, out _, out _, out _, out var incoming);
                        if (!incoming || !handledRequests.Add(id)) continue;
                        var accept = respond == "accept" || (respond == "reject2accept" && responseCount > 0);
                        responseCount++;
                        Debug.Log("[Trade] Dev " + (accept ? "确认" : "拒绝") + "请求 " + id);
                        if (accept) view.DevConfirmFirst(); else view.DevRejectFirst();
                    }
                }
            }

            if (staleAttempt && !staleDone && staleAfter >= 0 && elapsed >= staleAfter && firstStaleId != 0)
            {
                staleDone = true;
                Debug.Log("[Trade] Dev 旧确认尝试 request=" + firstStaleId);
                director.ConfirmTrade(firstStaleId);
            }
        }
    }
}
#endif
