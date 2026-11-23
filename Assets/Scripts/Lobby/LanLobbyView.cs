using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    /// <summary>房主配置总人数与回合数，客机查看同步设置。</summary>
    public sealed class LanLobbyView : MonoBehaviour
    {
        public event Action HostRequested;
        public event Action JoinRequested;
        public event Action ExitRequested;
        public event Action RefreshRequested;
        public event Action<int> TargetSeatsRequested;
        public event Action StartMatchRequested;
        public event Action MainMenuRequested;
        public event Action ReconnectRequested;
        public event Action<string> CreateRoomRequested;
        public event Action<string> SearchRoomsRequested;
        public event Action<string, ushort> JoinRoomRequested;
        public event Action<int, int, int> TradeTimesRequested;
        public event Action<int> TotalRoundsRequested;
        public event Action<string> PlayerNameEdited;
        public Button MainMenuButton { get { return mainMenuButton; } }
        public Button ReconnectButton { get { return reconnectButton; } }
        public Button StartButton { get { return startButton; } }
        public Button HostButton { get { return hostButton; } }
        public Button JoinButton { get { return joinButton; } }
        public Button ExitButton { get { return exitButton; } }

        private Text statusText;
        private Text detailText;
        private Text lobbyInfoText;
        private Text noticeText;
        private InputField addressInput;
        private InputField portInput;
        private Button hostButton;
        private Button joinButton;
        private Button exitButton;
        private readonly Button[] tierButtons = new Button[LobbySeatingRules.Tiers.Length];
        private InputField roundsInput;
        private Button roundsApplyButton;
        private int displayedRounds;
        private bool displayedRoundsEditable;
        private int roomRealCount;
        private Button startButton;
        private Button mainMenuButton;
        private Button reconnectButton;
        private Button searchButton;
        private Button roomLeaveButton;
        private InputField roomNameInput;
        private InputField playerNameInput;
        private InputField roomPlayerNameInput;
        private string savedPlayerName;
        private InputField searchInput;
        private InputField firstTradeInput;
        private InputField secondTradeInput;
        private InputField thirdTradeInput;
        private Button tradeApplyButton;
        private Text roomNameText;
        private Text roomInfoText;
        private Text roomHostText;
        private GameObject panel;
        private GameObject entryPage;
        private GameObject roomPage;
        private readonly Image[] seatMarks = new Image[LobbySeatingRules.MaxRealPlayers];
        private readonly Text[] seatNumbers = new Text[LobbySeatingRules.MaxRealPlayers];
        private int shownSeats = -1;
        private int shownPlayers = -1;
        private readonly Text[] tierLabels = new Text[LobbySeatingRules.Tiers.Length];
        private Text seatSummary;
        private Text roomResultsEmpty;
        private Text roomHint;
        private bool solo;
        private bool requestedEntry;
        private bool requestedRoom;
        private bool pageSwitchQueued;
        public bool IsPageTransitioning => pageSwitchQueued || TransitionOverlay.IsPlaying;
        private RectTransform resultContent;
        private readonly List<Button> resultButtons = new List<Button>();

        public string TargetAddress { get { return addressInput != null ? addressInput.text.Trim() : string.Empty; } }
        public string TargetPort { get { return portInput != null ? portInput.text.Trim() : string.Empty; } }
        public string RoomName { get { return roomNameInput != null ? roomNameInput.text : string.Empty; } }
        public string PlayerName => roomPlayerNameInput != null && roomPlayerNameInput.gameObject.activeInHierarchy
            ? roomPlayerNameInput.text : playerNameInput?.text ?? string.Empty;

        public void SetPlayerName(string value)
        {
            savedPlayerName = value ?? string.Empty;
            playerNameInput?.SetTextWithoutNotify(savedPlayerName);
            roomPlayerNameInput?.SetTextWithoutNotify(savedPlayerName);
        }

        private void BindPlayerNameInput(InputField input)
        {
            input.onEndEdit.AddListener(value =>
            {
                if (input.wasCanceled) SetPlayerName(savedPlayerName);
                else PlayerNameEdited?.Invoke(value);
            });
        }
        public string SearchFilter { get { return searchInput != null ? searchInput.text : string.Empty; } }

        public static LanLobbyView Create(Transform parent)
        {
            var host = new GameObject("LanLobbyUI");
            host.transform.SetParent(parent, false);
            var view = host.AddComponent<LanLobbyView>();
            view.Build();
            return view;
        }

        public void SetReconnectVisible(bool visible)
        {
            if (reconnectButton != null) { reconnectButton.gameObject.SetActive(visible); reconnectButton.interactable = visible; }
        }

        public void SetLobbyPanelVisible(bool visible)
        {
            if (panel != null) panel.SetActive(visible);
        }

        public void SetEndpointFields(string address, string port)
        {
            if (addressInput != null) addressInput.text = address;
            if (portInput != null) portInput.text = port;
        }

        public void SetButtonsEnabled(bool canHost, bool canJoin, bool canExit)
        {
            if (hostButton != null) hostButton.interactable = canHost;
            if (joinButton != null) joinButton.interactable = canJoin;
            if (exitButton != null) exitButton.interactable = canExit;
        }

        public void SetStatus(string label, string message, Color color)
        {
            if (statusText == null) return;
            statusText.text = string.IsNullOrEmpty(message) ? label : label + " — " + message;
            statusText.color = color;
        }

        public void SetDetails(string text)
        {
            if (detailText != null) detailText.text = text;
        }

        public void SetLobbyInfo(string text)
        {
            if (lobbyInfoText != null) lobbyInfoText.text = text;
        }

        public void SetMatchControls(bool isHost, int targetSeats, bool canStart, string notice)
        {
            for (var i = 0; i < tierButtons.Length; i++)
            {
                if (tierButtons[i] == null) continue;
                var tier = LobbySeatingRules.Tiers[i];
                tierButtons[i].interactable = isHost && tier >= roomRealCount;
                var image = tierButtons[i].targetGraphic as Image;
                if (image != null)
                {
                    image.color = tier == targetSeats ? ArtTheme.Gold : ArtTheme.Surface;
                    tierLabels[i].color = tier == targetSeats ? ArtTheme.Ink : ArtTheme.Text;
                }
            }

            if (startButton != null) startButton.interactable = isHost && canStart;
            if (noticeText != null) noticeText.text = notice ?? string.Empty;
        }

        private static Font ResolveFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null) font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }

        private static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static RectTransform CreateRect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void AddBackground(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
        }

        private static Text CreateText(Transform parent, string name, string content, Font font, int fontSize, TextAnchor alignment, Vector2 position, Vector2 size, Color color)
        {
            var rect = CreateRect(parent, name, position, size);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = string.IsNullOrEmpty(content) ? font : ArtTheme.FontFor(content);
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.text = content;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 position, Vector2 size, Color background)
        {
            var rect = CreateRect(parent, name, position, size);
            AddBackground(rect, background);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            var text = CreateText(rect, "Label", label, font, 22, TextAnchor.MiddleCenter, Vector2.zero, size, background == ArtTheme.Gold ? ArtTheme.Ink : ArtTheme.Text);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            return button;
        }

        private static InputField CreateInput(Transform parent, string name, string placeholder, Font font, Vector2 position, Vector2 size)
        {
            var rect = CreateRect(parent, name, position, size);
            AddBackground(rect, ArtTheme.Ink);
            var field = rect.gameObject.AddComponent<InputField>();

            var text = CreateText(rect, "Text", string.Empty, font, 20, TextAnchor.MiddleLeft, new Vector2(10f, 0f), size, ArtTheme.Text);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(10f, 0f);
            textRect.offsetMax = new Vector2(-10f, 0f);
            text.supportRichText = false;

            var hint = CreateText(rect, "Placeholder", placeholder, font, 19, TextAnchor.MiddleLeft, new Vector2(10f, 0f), size, ArtTheme.Muted);
            var hintRect = hint.GetComponent<RectTransform>();
            hintRect.anchorMin = new Vector2(0f, 0f);
            hintRect.anchorMax = new Vector2(1f, 1f);
            hintRect.offsetMin = new Vector2(10f, 0f);
            hintRect.offsetMax = new Vector2(-10f, 0f);
            CreateRule(rect, "Field rule", 0, size.y - 2, size.x, 2, Color.Lerp(ArtTheme.Panel, ArtTheme.Gold, 0.43f));
            field.customCaretColor = true;
            field.caretColor = ArtTheme.Gold;
            field.selectionColor = new Color(0.65f, 0.50f, 0.20f, 0.48f);
            field.textComponent = text;
            field.placeholder = hint;
            field.targetGraphic = rect.GetComponent<Image>();
            field.contentType = InputField.ContentType.Standard;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        private static Image CreateRule(Transform parent, string name, float x, float y, float width, float height, Color tint)
        {
            var rect = CreateRect(parent, name, new Vector2(x, -y), new Vector2(width, height));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;
            return image;
        }

        private static Text Caption(Transform parent, string name, string content, Font font, int fontSize,
            float x, float y, float width, float height, Color tint)
        {
            return CreateText(parent, name, content, font, fontSize, TextAnchor.MiddleLeft,
                new Vector2(x, -y), new Vector2(width, height), tint);
        }

        private static void Fit(Text text, int minimum)
        {
            text.resizeTextMaxSize = text.fontSize;
            text.resizeTextMinSize = minimum;
            text.resizeTextForBestFit = true;
        }

        private static RectTransform CataloguePanel(Transform parent, string name)
        {
            var card = ArtTheme.Block(parent, name, ArtTheme.Panel, Vector2.zero, new Vector2(1232, 736));
            var brassLine = Color.Lerp(ArtTheme.Surface, ArtTheme.Gold, 0.45f);
            CreateRule(card, "Printed cover", 0, 0, 402, 736, Color.Lerp(ArtTheme.Panel, ArtTheme.Surface, 0.58f));
            CreateRule(card, "Spine", 402, 0, 2, 736, brassLine);
            CreateRule(card, "Cover head", 0, 0, 402, 4, ArtTheme.Gold);
            CreateRule(card, "Sheet head", 404, 0, 828, 2, brassLine);
            CreateRule(card, "Footer rule", 32, 641, 1168, 1, brassLine);
            var engraving = CreateRect(card, "Auction gavel engraving", new Vector2(34, -19), new Vector2(334, 112));
            engraving.gameObject.AddComponent<LobbyGavelGraphic>().raycastTarget = false;
            return card;
        }

        private void Build()
        {
            KeyBindingPanel.Ensure();
            EnsureEventSystem();
            var font = ResolveFont();

            var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var backdrop = ArtTheme.Block(canvasObject.transform, "Lobby background", ArtTheme.Ink, Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(backdrop);
            panel = backdrop.gameObject;
            var card = CataloguePanel(backdrop, "Panel");
            entryPage = card.gameObject;

            Caption(card, "Title", "局域网大厅", font, 46, 36, 137, 334, 61, ArtTheme.Text);
            statusText = Caption(card, "Status", "未连接", font, 19, 38, 206, 330, 68, ArtTheme.Teal);
            Fit(statusText, 15);
            detailText = Caption(card, "Details", string.Empty, font, 18, 38, 205, 328, 72, ArtTheme.Muted);
            detailText.gameObject.SetActive(false);
            Caption(card, "PlayerNameLabel", "你的昵称", font, 20, 38, 287, 326, 27, ArtTheme.Text);
            playerNameInput = CreateInput(card, "PlayerNameInput", "留空使用席号", font, new Vector2(38, -320), new Vector2(326, 45));
            BindPlayerNameInput(playerNameInput);
            Caption(card, "RoomNameLabel", "房间名称（可选）", font, 20, 38, 384, 326, 27, ArtTheme.Text);
            roomNameInput = CreateInput(card, "RoomNameInput", "输入房间名称", font, new Vector2(38, -417), new Vector2(326, 45));
            hostButton = CreateButton(card, "HostButton", "创建并进入", font, new Vector2(38, -484), new Vector2(326, 54), ArtTheme.Gold);
            lobbyInfoText = Caption(card, "LobbyInfo", "等待真人加入", font, 18, 38, 550, 326, 77, ArtTheme.Muted);
            Fit(lobbyInfoText, 15);

            Caption(card, "SearchLabel", "发现房间", font, 35, 447, 34, 410, 53, ArtTheme.Text);
            searchInput = CreateInput(card, "SearchInput", "按房间名称筛选", font, new Vector2(448, -106), new Vector2(596, 48));
            searchButton = CreateButton(card, "SearchButton", "搜索", font, new Vector2(1056, -106), new Vector2(138, 48), ArtTheme.Surface);
            BuildResults(card, font);
            CreateRule(card, "Connection rule", 448, 406, 746, 1, ArtTheme.Surface);
            Caption(card, "JoinLabel", "通过地址加入", font, 28, 448, 421, 740, 44, ArtTheme.Text);
            Caption(card, "Address label", "房主 IPv4 地址", font, 18, 448, 474, 300, 25, ArtTheme.Muted);
            Caption(card, "Port label", "端口", font, 18, 844, 474, 128, 25, ArtTheme.Muted);
            addressInput = CreateInput(card, "AddressInput", "192.168.1.10", font, new Vector2(448, -505), new Vector2(382, 49));
            portInput = CreateInput(card, "PortInput", "7777", font, new Vector2(844, -505), new Vector2(132, 49));
            joinButton = CreateButton(card, "JoinButton", "加入房间", font, new Vector2(990, -505), new Vector2(204, 49), ArtTheme.Surface);
            noticeText = Caption(card, "Notice", string.Empty, font, 19, 448, 565, 746, 64, ArtTheme.Gold);
            Fit(noticeText, 16);

            mainMenuButton = CreateButton(card, "MainMenuButton", "返回主菜单", font, new Vector2(38, -663), new Vector2(226, 48), ArtTheme.Surface);
            var refreshButton = CreateButton(card, "RefreshButton", "刷新本机地址", font, new Vector2(448, -663), new Vector2(191, 48), ArtTheme.Surface);
            exitButton = CreateButton(card, "ExitButton", "断开连接", font, new Vector2(654, -663), new Vector2(149, 48), ArtTheme.Surface);
            reconnectButton = CreateButton(card, "ReconnectButton", "重连原席位", font, new Vector2(971, -663), new Vector2(223, 48), ArtTheme.Gold);
            hostButton.onClick.AddListener(OnHostClicked);
            joinButton.onClick.AddListener(OnJoinClicked);
            refreshButton.onClick.AddListener(OnRefreshClicked);
            exitButton.onClick.AddListener(OnExitClicked);
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
            reconnectButton.onClick.AddListener(OnReconnectClicked);
            reconnectButton.gameObject.SetActive(false);
            searchButton.onClick.AddListener(OnSearchClicked);
            BuildRoomPage(backdrop, font);
        }

        private void BuildResults(Transform parent, Font font)
        {
            var viewport = CreateRect(parent, "ResultsViewport", new Vector2(448, -170), new Vector2(746, 218));
            AddBackground(viewport, ArtTheme.Ink);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            roomResultsEmpty = CreateText(viewport, "No rooms", "尚未发现房间", font, 21, TextAnchor.MiddleCenter,
                Vector2.zero, viewport.sizeDelta, ArtTheme.Muted);
            var content = CreateRect(viewport, "Content", Vector2.zero, Vector2.zero);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1);
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 4;
            layout.padding = new RectOffset(6, 6, 6, 6);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            resultContent = content;
        }

        private void BuildRoomPage(Transform parent, Font font)
        {
            var card = CataloguePanel(parent, "RoomPage");
            roomPage = card.gameObject;
            roomNameText = Caption(card, "RoomName", "房间", font, 42, 35, 135, 333, 89, ArtTheme.Text);
            Fit(roomNameText, 22);
            roomHostText = Caption(card, "RoomHost", "房主地址", font, 20, 38, 228, 327, 54, ArtTheme.Teal);
            for (var i = 0; i < seatMarks.Length; i++)
            {
                int x = 39 + (i % 5) * 67;
                int y = 298 + (i / 5) * 56;
                seatMarks[i] = CreateRule(card, "Bid paddle " + (i + 1), x, y, 53, 36, ArtTheme.Ink);
                CreateRule(card, "Paddle handle " + (i + 1), x + 23, y + 36, 7, 9, ArtTheme.Muted);
                seatNumbers[i] = CreateText(seatMarks[i].transform, "Seat", (i + 1).ToString("00"), font, 20,
                    TextAnchor.MiddleCenter, Vector2.zero, new Vector2(53, 36), ArtTheme.Muted);
            }
            seatSummary = Caption(card, "Seat summary", "", font, 18, 39, 408, 326, 34, ArtTheme.Text);
            var roster = CreateRect(card, "Roster viewport", new Vector2(38, -457), new Vector2(326, 171));
            AddBackground(roster, Color.clear);
            roster.gameObject.AddComponent<RectMask2D>();
            var scroll = roster.gameObject.AddComponent<ScrollRect>();
            roomInfoText = CreateText(roster, "RoomInfo", "等待连接…", font, 20, TextAnchor.UpperLeft,
                Vector2.zero, new Vector2(326, 0), ArtTheme.Muted);
            roomInfoText.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = roster;
            scroll.content = roomInfoText.rectTransform;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            Caption(card, "Setup heading", "对局设置", font, 35, 447, 34, 450, 53, ArtTheme.Text);
            Caption(card, "RoomTierLabel", "总席位", font, 21, 448, 112, 260, 32, ArtTheme.Text);
            for (var i = 0; i < tierButtons.Length; i++)
            {
                var tier = LobbySeatingRules.Tiers[i];
                var button = CreateButton(card, "Tier" + tier, tier + " 席", font,
                    new Vector2(448 + i * 126, -154), new Vector2(116, 49), ArtTheme.Surface);
                tierButtons[i] = button;
                tierLabels[i] = button.GetComponentInChildren<Text>(true);
                button.onClick.AddListener(() => OnTierClicked(tier));
            }
            Caption(card, "Seat rule", "空席由 NPC 补齐", font, 18, 449, 213, 745, 30, ArtTheme.Muted);
            CreateRule(card, "Rules divider", 448, 260, 746, 1, ArtTheme.Surface);
            Caption(card, "RoomPlayerNameLabel", "你的昵称", font, 20, 448, 277, 350, 30, ArtTheme.Text);
            roomPlayerNameInput = CreateInput(card, "RoomPlayerNameInput", "输入昵称", font, new Vector2(448, -317), new Vector2(374, 48));
            BindPlayerNameInput(roomPlayerNameInput);
            Caption(card, "RoundsLabel", "总轮数（1–10）", font, 20, 854, 277, 340, 30, ArtTheme.Text);
            roundsInput = CreateInput(card, "TotalRounds", "3", font, new Vector2(854, -317), new Vector2(165, 48));
            roundsInput.contentType = InputField.ContentType.IntegerNumber;
            roundsApplyButton = CreateButton(card, "RoundsApply", "应用轮数", font, new Vector2(1032, -317), new Vector2(162, 48), ArtTheme.Surface);
            roundsApplyButton.onClick.AddListener(() =>
            {
                if (!int.TryParse(roundsInput.text, out var rounds)) return;
                displayedRounds = 0;
                TotalRoundsRequested?.Invoke(rounds);
            });
            CreateRule(card, "Time divider", 448, 400, 746, 1, ArtTheme.Surface);
            Caption(card, "TradeLabel", "各段交易时长（秒）", font, 23, 448, 414, 746, 40, ArtTheme.Text);
            Caption(card, "Phase one", "第一段", font, 18, 448, 463, 169, 25, ArtTheme.Muted);
            Caption(card, "Phase two", "第二段", font, 18, 643, 463, 169, 25, ArtTheme.Muted);
            Caption(card, "Phase three", "第三段", font, 18, 838, 463, 169, 25, ArtTheme.Muted);
            firstTradeInput = CreateInput(card, "FirstTrade", "120", font, new Vector2(448, -494), new Vector2(181, 48));
            secondTradeInput = CreateInput(card, "SecondTrade", "120", font, new Vector2(643, -494), new Vector2(181, 48));
            thirdTradeInput = CreateInput(card, "ThirdTrade", "60", font, new Vector2(838, -494), new Vector2(181, 48));
            tradeApplyButton = CreateButton(card, "TradeApply", "应用秒数", font, new Vector2(1032, -494), new Vector2(162, 48), ArtTheme.Surface);
            tradeApplyButton.onClick.AddListener(OnTradeApply);
            roomHint = Caption(card, "RoomHint", "房主可设置席位、轮数和时长，客机等待开局。", font, 19, 449, 562, 745, 60, ArtTheme.Muted);
            roomLeaveButton = CreateButton(card, "RoomLeaveButton", "离开房间", font, new Vector2(38, -663), new Vector2(226, 48), ArtTheme.Surface);
            roomLeaveButton.onClick.AddListener(OnExitClicked);
            startButton = CreateButton(card, "StartButton", "开始对局", font, new Vector2(890, -656), new Vector2(304, 60), ArtTheme.Gold);
            startButton.onClick.AddListener(OnStartClicked);
            roomPage.SetActive(false);
        }

        public void SetPages(bool entryVisible, bool roomVisible)
        {
            requestedEntry = entryVisible;
            requestedRoom = roomVisible;
            var wasEntry = entryPage != null && entryPage.activeSelf;
            var wasRoom = roomPage != null && roomPage.activeSelf;
            if ((!entryVisible && !roomVisible) || !panel.activeInHierarchy)
            {
                ApplyPages();
                return;
            }
            if (wasEntry == entryVisible && wasRoom == roomVisible) return;
            if (TransitionOverlay.IsScreenCovered || (!wasEntry && !wasRoom)) ApplyPages();
            else if (!pageSwitchQueued)
            {
                pageSwitchQueued = true;
                TransitionOverlay.SwitchPage(ApplyPages);
            }
        }

        private void ApplyPages()
        {
            if (this == null) return;
            pageSwitchQueued = false;
            if (entryPage != null) entryPage.SetActive(requestedEntry);
            if (roomPage != null) roomPage.SetActive(requestedRoom);
        }

        public void SetSoloMode()
        {
            solo = true;
            roomHint.text = "设置总人数、轮数和交易时长后，点击开始游戏。";
            startButton.GetComponentInChildren<Text>(true).text = "开始游戏";
            roomLeaveButton.GetComponentInChildren<Text>(true).text = "返回主菜单";
        }

        public void SetRoom(string name, bool isHost, string address, string status, int tier, int real, bool canStart)
        {
            if (roomNameText != null) roomNameText.text = solo ? "单人准备" : (string.IsNullOrEmpty(name) ? "房间" : name);
            roomRealCount = real;
            if (roomHostText != null) roomHostText.text = solo ? "本地对局" : "房主地址：" + address;
            if (roomInfoText != null) roomInfoText.text = solo ? status : (isHost ? "你是房主" : "你是客机") + "\n" + status;
            if (shownSeats != tier || shownPlayers != real)
            {
                shownSeats = tier;
                shownPlayers = real;
                seatSummary.text = "真人 " + real + "   ·   NPC " + Mathf.Max(0, tier - real) + "   ·   共 " + tier + " 席";
                for (var i = 0; i < seatMarks.Length; i++)
                {
                    var tint = i < real ? ArtTheme.Gold : i < tier ? ArtTheme.Teal : ArtTheme.Ink;
                    var mark = seatMarks[i];
                    if (mark.color != tint && mark.isActiveAndEnabled)
                    {
                        mark.canvasRenderer.SetAlpha(0.5f);
                        mark.CrossFadeAlpha(1f, 0.22f, true);
                    }
                    mark.color = tint;
                    seatNumbers[i].color = i < tier ? ArtTheme.Ink : ArtTheme.Muted;
                }
            }
            SetMatchControls(isHost, tier, canStart, status);
        }

        public void SetTotalRounds(int rounds, bool editable)
        {
            if (rounds == displayedRounds && editable == displayedRoundsEditable) return;
            displayedRounds = rounds;
            displayedRoundsEditable = editable;
            SetTradeInput(roundsInput, rounds, editable);
            roundsApplyButton.interactable = editable;
        }

        public void SetDiscoveryResults(IList<LanRoomInfo> rooms, string filter)
        {
            if (resultContent == null) return;
            for (var i = 0; i < resultButtons.Count; i++) if (resultButtons[i] != null) Destroy(resultButtons[i].gameObject);
            resultButtons.Clear();
            roomResultsEmpty.gameObject.SetActive(true);
            roomResultsEmpty.text = string.IsNullOrEmpty(filter) ? "尚未发现房间" : "没有符合筛选条件的房间";
            if (rooms == null) return;
            var shown = 0;
            for (var i = 0; i < rooms.Count && shown < 12; i++)
            {
                var room = rooms[i];
                if (!string.IsNullOrEmpty(filter) && (room.n == null || room.n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                if (room.s != 0) continue;
                var font = ResolveFont();
                var button = CreateButton(resultContent, "RoomResult" + shown, room.n ?? "房间", font, Vector2.zero, new Vector2(734, 48), ArtTheme.Surface);
                var label = button.GetComponentInChildren<Text>(true);
                label.alignment = TextAnchor.MiddleLeft;
                label.rectTransform.offsetMin = new Vector2(16, 0);
                label.rectTransform.offsetMax = new Vector2(-292, 0);
                Fit(label, 16);
                Caption(button.transform, "Capacity", room.r + "/" + room.t + " 席", font, 20, 456, 0, 105, 48, ArtTheme.Gold);
                Caption(button.transform, "Address", room.Address, font, 18, 563, 0, 165, 48, ArtTheme.Muted);
                var element = button.gameObject.AddComponent<LayoutElement>();
                element.preferredHeight = 48;
                var info = room;
                button.onClick.AddListener(delegate { if (JoinRoomRequested != null) JoinRoomRequested(info.Address, info.p); });
                resultButtons.Add(button); shown++;
            }
            roomResultsEmpty.gameObject.SetActive(shown == 0);
        }

        public bool ClickFirstResult()
        {
            if (resultButtons.Count == 0) return false;
            var button = resultButtons[0];
            if (button == null || !button.gameObject.activeInHierarchy || !button.interactable) return false;
            button.onClick.Invoke(); return true;
        }

        private void OnTierClicked(int tier)
        {
            if (TargetSeatsRequested != null) TargetSeatsRequested(tier);
        }

        private void OnStartClicked()
        {
            if (StartMatchRequested != null) StartMatchRequested();
        }

        private void OnHostClicked()
        {
            if (CreateRoomRequested != null) CreateRoomRequested(RoomName);
            else if (HostRequested != null) HostRequested();
        }

        private (int First, int Second, int Third, bool Host)? shownTradeTimes;

        public void SetTradeTimes(int first, int second, int third, bool host)
        {
            var times = (First: first, Second: second, Third: third, Host: host);
            if (shownTradeTimes.HasValue && shownTradeTimes.Value == times) return;
            shownTradeTimes = times;
            SetTradeInput(firstTradeInput, first, host);
            SetTradeInput(secondTradeInput, second, host);
            SetTradeInput(thirdTradeInput, third, host);
            if (tradeApplyButton != null) tradeApplyButton.interactable = host;
        }

        private static void SetTradeInput(InputField field, int seconds, bool host)
        {
            if (field == null) return;
            field.SetTextWithoutNotify(seconds.ToString());
            field.interactable = host;
        }

        private void OnTradeApply()
        {
            int a, b, c;
            if (firstTradeInput == null || secondTradeInput == null || thirdTradeInput == null) return;
            if (!int.TryParse(firstTradeInput.text, out a) || !int.TryParse(secondTradeInput.text, out b) || !int.TryParse(thirdTradeInput.text, out c)) return;
            shownTradeTimes = null;
            if (TradeTimesRequested != null) TradeTimesRequested(a, b, c);
        }

        private void OnSearchClicked()
        {
            if (SearchRoomsRequested != null) SearchRoomsRequested(SearchFilter);
        }

        private void OnJoinClicked()
        {
            if (JoinRequested != null) JoinRequested();
        }

        private void OnRefreshClicked()
        {
            if (RefreshRequested != null) RefreshRequested();
        }

        private void OnExitClicked()
        {
            if (ExitRequested != null) ExitRequested();
        }

        private void OnMainMenuClicked()
        {
            if (MainMenuRequested != null) MainMenuRequested();
        }

        private void OnReconnectClicked()
        {
            if (ReconnectRequested != null) ReconnectRequested();
        }
    }
}
