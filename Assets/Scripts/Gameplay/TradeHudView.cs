using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Worthless
{
    public sealed class TradeHudView : MonoBehaviour
    {
        private const float InteractionRange = 1.85f;
        private static TradeHudView active;
        private MatchRoundDirector director;
        private MatchAuthority authority;
        private InteractionOutline outline;
        private int interactionSeat = -1;
        private RectTransform sheet;
        private CanvasGroup sheetFade;
        private Text title;
        private RectTransform exchange;
        private Text ownItem;
        private Text otherItem;
        private Text description;
        private RectTransform receipt;
        private RectTransform receiptRule;
        private Text hintKey;
        private Button startButton;
        private Button cancelButton;
        private Button nextRequest;
        private Text hint;
        private Button requestsButton;
        private Text feedback;
        private readonly Button[] approaches = new Button[3];
        private int target = -1;
        private int requestIndex;
        private uint displayedRequest;
        private bool displayedIncoming;
        private bool choosingApproach;
        private bool readingRequest;
        private string lastFeedback = string.Empty;
        private float feedbackUntil;

        public static bool HasInteractTarget => active != null && active.FindNearbyTarget() >= 0;
        public static bool IsOpen => active != null && active.Open;
        public static int InteractionTargetSeat => active != null ? active.interactionSeat : -1;
        public bool OutlineVisible => outline != null && outline.Visible;
        private bool Open => target >= 0 || readingRequest;
        private bool TradeAvailable => director != null && authority != null && !authority.IsLocalSpectator &&
            director.IsTradeOpen && !director.LocalTradeLocked && !director.LocalTradeExhausted;

        public static TradeHudView Create(Transform parent)
        {
            var root = new GameObject("TradeHud");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<TradeHudView>();
            view.Build();
            return view;
        }

        private void OnEnable() { active = this; }
        private void OnDisable()
        {
            if (active == this) active = null;
            interactionSeat = -1;
            if (outline != null) outline.SetTarget(null);
        }

        private void Build()
        {
            outline = gameObject.AddComponent<InteractionOutline>();
            if (FindObjectOfType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            var root = new GameObject("TradeCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 12;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600f, 900f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var hintBar = ArtTheme.Block(root.transform, "Nearby trade", ArtTheme.Panel, new Vector2(0, 44), new Vector2(360, 44), anchor: new Vector2(0.5f, 0));
            var keycap = ArtTheme.Block(hintBar, "Interact key", ArtTheme.Gold, new Vector2(-141, 0), new Vector2(60, 28));
            hintKey = Label(keycap, "Key", Vector2.zero, new Vector2(56, 26), 17, ArtTheme.Ink);
            hint = Label(hintBar, "Interaction", new Vector2(35, 0), new Vector2(274, 36), 19, ArtTheme.Text);
            receipt = ArtTheme.Block(root.transform, "Trade receipt", ArtTheme.Panel, new Vector2(0, 102), new Vector2(468, 44), anchor: new Vector2(.5f, 0));
            ArtTheme.Block(receipt, "Receipt edge", ArtTheme.Gold, new Vector2(-232, 0), new Vector2(4, 44));
            receiptRule = ArtTheme.Block(receipt, "Receipt underline", ArtTheme.Gold, new Vector2(4, 2), new Vector2(456, 2), anchor: Vector2.zero);
            receiptRule.pivot = Vector2.zero;
            feedback = Label(receipt, "Feedback", Vector2.zero, new Vector2(440, 39), 18, ArtTheme.Gold);
            receipt.gameObject.SetActive(false);
            requestsButton = ArtTheme.Button(root.transform, "交易请求", new Vector2(-114, -160), new Vector2(190, 41), true);
            ((RectTransform)requestsButton.transform).anchorMin = ((RectTransform)requestsButton.transform).anchorMax = Vector2.one;
            ((RectTransform)requestsButton.transform).anchoredPosition = new Vector2(-114, -160);
            requestsButton.onClick.AddListener(() =>
            {
                Close();
                readingRequest = true;
                ShowRequest();
            });

            sheet = ArtTheme.Block(root.transform, "Trade dialog", ArtTheme.Panel, new Vector2(0, 20), new Vector2(680, 188), true, new Vector2(0.5f, 0));
            sheet.pivot = new Vector2(0.5f, 0);
            sheetFade = sheet.gameObject.AddComponent<CanvasGroup>();
            sheetFade.alpha = 0;
            ArtTheme.Block(sheet, "Binding", ArtTheme.Gold, new Vector2(0, 92), new Vector2(680, 4));
            title = Label(sheet, "Title", new Vector2(-65, 66), new Vector2(498, 34), 23, ArtTheme.Text);
            title.alignment = TextAnchor.MiddleLeft;
            exchange = ArtTheme.Rect(sheet, "Exchange items", new Vector2(0, 8), new Vector2(632, 64));
            ownItem = ItemColumn(exchange, "你的物品", -171);
            otherItem = ItemColumn(exchange, "对方物品", 171);
            ArtTheme.Label(exchange, "Swap", "↔", 25, Vector2.zero, new Vector2(46, 52), ArtTheme.Gold, TextAnchor.MiddleCenter);
            description = Label(sheet, "Approach hint", new Vector2(0, 10), new Vector2(630, 42), 18, ArtTheme.Muted);
            startButton = ArtTheme.Button(sheet, "发起交易", new Vector2(-87, -59), new Vector2(156, 40), true);
            startButton.onClick.AddListener(Confirm);
            cancelButton = ArtTheme.Button(sheet, "取消", new Vector2(87, -59), new Vector2(156, 40), false);
            cancelButton.onClick.AddListener(Cancel);
            var next = ArtTheme.Button(sheet, "下一条", new Vector2(259, 66), new Vector2(116, 29), false);
            next.onClick.AddListener(() => { requestIndex++; ShowRequest(); });
            next.gameObject.SetActive(false);
            nextRequest = next;

            for (var i = 0; i < approaches.Length; i++)
            {
                var index = i;
                var button = ArtTheme.Button(sheet, "话术", new Vector2((i - 1) * 204, -59), new Vector2(192, 38), i == 1);
                button.onClick.AddListener(() => SendOffer(index));
                button.gameObject.SetActive(false);
                approaches[i] = button;
            }
        }

        private static Text ItemColumn(Transform parent, string heading, float x)
        {
            var column = ArtTheme.Block(parent, heading, ArtTheme.Surface, new Vector2(x, 0), new Vector2(288, 64));
            ArtTheme.Label(column, "Owner", heading, 14, new Vector2(0, 15), new Vector2(260, 22), ArtTheme.Teal, TextAnchor.MiddleLeft);
            var item = Label(column, "Item", new Vector2(0, -11), new Vector2(260, 29), 21, ArtTheme.Text);
            item.alignment = TextAnchor.MiddleLeft;
            return item;
        }

        private static Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, Color color)
        {
            var label = ArtTheme.Label(parent, name, string.Empty, fontSize, position, size, color, TextAnchor.MiddleCenter);
            label.supportRichText = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 14;
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        private static void SetText(Text text, string value)
        {
            if (text.text == value) return;
            text.font = ArtTheme.FontFor(value);
            text.text = value;
        }

        private static void SetCaption(Button button, string value) => SetText(button.GetComponentInChildren<Text>(true), value);

        private void Update()
        {
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (!TradeAvailable) Close();
            var nearby = FindNearbyTarget();
            if (Open && InputProbe.GetKeyDown(KeyCode.Escape)) Close();
            else if (!Open && nearby >= 0 && !InputGate.Blocked && InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.Interact)))
                OpenOffer(nearby);
            if (readingRequest) ShowRequest();

            var showHint = nearby >= 0 && !Open && !InputGate.Blocked;
            interactionSeat = showHint ? nearby : -1;
            outline.SetTarget(showHint ? authority.GetSeatActor(nearby) : null);
            hint.transform.parent.gameObject.SetActive(showHint);
            if (showHint)
            {
                SetText(hintKey, KeyBindings.Get(GameInputAction.Interact).ToString());
                SetText(hint, "与 " + director.GetSeatLabel(nearby) + " 交易");
            }
            requestsButton.gameObject.SetActive(TradeAvailable && !Open && director.TradeRequestCount > 0);
            if (requestsButton.gameObject.activeSelf)
                SetCaption(requestsButton, "交易请求  " + director.TradeRequestCount);

            var previous = sheetFade.alpha;
            var amount = Mathf.MoveTowards(previous, Open ? 1 : 0, Time.unscaledDeltaTime * 5.5f);
            sheetFade.alpha = amount;
            sheetFade.blocksRaycasts = Open;
            sheetFade.interactable = Open;
            if (previous != amount)
            {
                float tucked = (1f - amount) * (1f - amount);
                sheet.anchoredPosition = new Vector2(0, 20f - 24f * tucked);
                // The two offer slips open out from the exchange arrow.
                ((RectTransform)ownItem.transform.parent).anchoredPosition = new Vector2(-171f + 34f * tucked, -4f * tucked);
                ((RectTransform)otherItem.transform.parent).anchoredPosition = new Vector2(171f - 34f * tucked, -4f * tucked);
                exchange.localRotation = Quaternion.Euler(0f, 0f, -2f * tucked);
            }
            if (director == null) return;
            var message = director.LocalTradeMessage;
            if (message != lastFeedback)
            {
                lastFeedback = message;
                feedbackUntil = Time.unscaledTime + 3.2f;
            }
            var showReceipt = !Open && !string.IsNullOrEmpty(lastFeedback) && Time.unscaledTime < feedbackUntil;
            receipt.gameObject.SetActive(showReceipt);
            if (showReceipt)
            {
                float elapsed = Time.unscaledTime - (feedbackUntil - 3.2f);
                float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .24f));
                receipt.anchoredPosition = new Vector2(0, Mathf.Lerp(87, 102, reveal));
                receipt.localRotation = Quaternion.Euler(0, 0, -4f * (1f - reveal));
                float ink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.1f, .44f, elapsed));
                receiptRule.localScale = new Vector3(ink, 1f, 1f);
                SetText(feedback, lastFeedback);
            }
        }

        private int FindNearbyTarget()
        {
            if (!TradeAvailable || !authority.TryGetLocalPlayerPosition(out var localPosition)) return -1;
            var closest = InteractionRange * InteractionRange;
            var closestSeat = -1;
            for (var seat = 0; seat < MatchAuthority.SeatCount; seat++)
            {
                if (seat == authority.LocalSeat || !director.IsSeatAlive(seat) ||
                    !director.TryGetTradePublic(seat, out var status) || status.Tradable == 0 || status.Exhausted > 0 ||
                    director.IsLocalReturnTradeBlocked(seat)) continue;
                var actor = authority.GetSeatActor(seat);
                if (actor == null || !actor.gameObject.activeInHierarchy || !authority.GetSeatSnapshot(seat).IsOnline) continue;
                var position = (Vector2)actor.transform.position;
                var distance = (localPosition - position).sqrMagnitude;
                if (distance > closest) continue;
                closest = distance;
                closestSeat = seat;
            }
            return closestSeat;
        }

        private void OpenOffer(int seat)
        {
            if (!director.TryGetPublicItem(authority.LocalSeat, out var own) ||
                !director.TryGetPublicItem(seat, out var other)) return;
            target = seat;
            choosingApproach = false;
            readingRequest = false;
            SetText(title, "与 " + director.GetSeatLabel(seat) + " 交易");
            SetText(ownItem, director.GetItemName(own.DefIndex));
            SetText(otherItem, director.GetItemName(other.DefIndex));
            SetCaption(startButton, "发起交易");
            SetCaption(cancelButton, "取消");
            SetOfferButtons(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[TradeUI] 近身打开目标席位 " + (seat + 1));
#endif
        }

        private void ShowRequest()
        {
            var count = director.TradeRequestCount;
            if (count == 0)
            {
                Close();
                return;
            }
            requestIndex %= count;
            if (!director.TryGetTradeRequest(requestIndex, out displayedRequest, out var from, out var to, out var fromDef, out var toDef, out displayedIncoming)) return;
            var incoming = displayedIncoming;
            SetText(title, (incoming ? "收到交易 · " : "请求已发 · ") + director.GetSeatLabel(incoming ? from : to));
            SetText(ownItem, director.GetItemName(incoming ? toDef : fromDef));
            SetText(otherItem, director.GetItemName(incoming ? fromDef : toDef));
            SetCaption(startButton, incoming ? "确认交换" : "撤回请求");
            SetCaption(cancelButton, incoming ? "拒绝" : "关闭");
            SetOfferButtons(false);
            nextRequest.gameObject.SetActive(count > 1);
        }

        private void Confirm()
        {
            if (readingRequest)
            {
                if (displayedIncoming) director.ConfirmTrade(displayedRequest);
                else director.WithdrawTrade(displayedRequest);
                Close();
                return;
            }
            if (!TradeAvailable || target < 0 || !director.TryGetPublicItem(target, out var other)) { Close(); return; }
            if (other.IsNpc == 0) { SendOffer(1); return; }

            choosingApproach = true;
            SetText(title, "向 " + director.GetSeatLabel(target) + " 选择话术");
            SetText(description, "按 Esc 可取消");
            for (var i = 0; i < approaches.Length; i++) SetCaption(approaches[i], director.NpcApproachLabel(i));
            SetOfferButtons(true);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[TradeUI] NPC 话术对话框已显示");
#endif
        }

        private void Cancel()
        {
            if (readingRequest && displayedIncoming) director.RejectTrade(displayedRequest);
            Close();
        }

        private void SendOffer(int approach)
        {
            if (TradeAvailable && target >= 0 && (choosingApproach || director.TryGetPublicItem(target, out var item) && item.IsNpc == 0))
                director.SendTradeRequest(target, approach);
            Close();
        }

        private void SetOfferButtons(bool showApproaches)
        {
            exchange.gameObject.SetActive(!showApproaches);
            description.gameObject.SetActive(showApproaches);
            startButton.gameObject.SetActive(!showApproaches);
            cancelButton.gameObject.SetActive(!showApproaches);
            nextRequest.gameObject.SetActive(false);
            foreach (var button in approaches) button.gameObject.SetActive(showApproaches);
        }

        private void Close()
        {
            target = -1;
            readingRequest = false;
            choosingApproach = false;
            requestIndex = 0;
            displayedRequest = 0;
            displayedIncoming = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DevSelectAndSend(int targetSeat) => DevSelectAndSend(targetSeat, 1);
        public void DevSelectAndSend(int targetSeat, int approach)
        {
            var round = director != null ? director : FindObjectOfType<MatchRoundDirector>();
            if (round != null) round.SendTradeRequest(targetSeat, Mathf.Clamp(approach, 0, 2));
        }
        public bool DevConfirmFirst() => DevAnswerFirst(true);
        public bool DevRejectFirst() => DevAnswerFirst(false);
        private bool DevAnswerFirst(bool confirm)
        {
            var round = director != null ? director : FindObjectOfType<MatchRoundDirector>();
            if (round == null) return false;
            for (var i = 0; i < round.TradeRequestCount; i++)
            {
                if (!round.TryGetTradeRequest(i, out var id, out _, out _, out _, out _, out var incoming) || !incoming) continue;
                if (confirm) round.ConfirmTrade(id);
                else round.RejectTrade(id);
                return true;
            }
            return false;
        }
#endif
    }
}
