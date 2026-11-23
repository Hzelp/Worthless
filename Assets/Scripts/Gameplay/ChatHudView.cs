using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Worthless.Lobby;
namespace Worthless
{
    public sealed class ChatHudView : MonoBehaviour
    {
        private static ChatHudView active;
        private MatchRoundDirector director;
        private MatchAuthority authority;
        private HistoryTextScroll messages;
        private Text channel;
        private InputField input;
        private Button sendButton, publicButton, nearbyButton, roomButton, lockButton;
        private GameObject roomBar;
        private Text roomBarText;
        private Button roomBarLockButton, roomBarChatButton;
        private Text roomBarLockLabel;

        public bool RoomBarVisible { get { return roomBar != null && roomBar.activeInHierarchy; } }
        public string RoomBarLabel { get { return roomBarText != null ? roomBarText.text : string.Empty; } }
        public bool RoomLockButtonVisible { get { return roomBarLockButton != null && roomBarLockButton.gameObject.activeInHierarchy; } }
        public bool RoomChatButtonVisible { get { return roomBarChatButton != null && roomBarChatButton.gameObject.activeInHierarchy; } }
        public static bool HasRoomInteraction => active != null && active.CanUseRoomControls;
        private bool CanUseRoomControls => director != null && authority != null && authority.Started &&
            !authority.GameOver && !authority.IsLocalSpectator && authority.IsLocalPlayerSeated &&
            director.IsSeatAlive(authority.LocalSeat) && director.LocalRoomId >= 0;
        private GameObject chatPanel;
        private Text toggleLabel;
        private bool chatOpen;
        private int unread;
        private ChatChannel selectedChannel = ChatChannel.Public;
        private bool subscribed;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool devCheck, nearbyCheck, roomCheck, roomOutside, sentRound1, handledRound2;
        private int nearbyStage, roomStage, outsideStage;
        private float nearbyAt, roomAt, outsideAt;
        private Vector2 nearbyMove, roomMove, outsideMove;
        private bool roomLockSent, roomLockTextSent, outsideRejected, outsideSawLock;
#endif
        private readonly List<string> lines = new List<string>();
        private int lastRoom = -1;
        private int lastSpectatorRoom = -2;
        private int maxLines;
        private uint lastGame;
        private bool lastSpectator;

        public static ChatHudView Create(Transform parent, MatchRoundDirector d, MatchAuthority a, int maxLines, int fontSize, float height)
        {
            if (FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            var go = new GameObject("ChatHud");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<ChatHudView>();
            view.director = d; view.authority = a; view.maxLines = Mathf.Max(1, maxLines);
            active = view;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            view.devCheck = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-chat-check") >= 0;
            view.nearbyCheck = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-nearby-check") >= 0;
            view.roomCheck = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-room-check") >= 0;
            view.roomOutside = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-room-outside") >= 0;
            view.chatOpen = view.devCheck || view.nearbyCheck || view.roomCheck || view.roomOutside;
#endif
            view.Build(fontSize, height);
            view.TryBind();
            return view;
        }

        private void Build(int fontSize, float height)
        {
            var canvasGo = new GameObject("ChatCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            var toggle = ArtTheme.Button(canvasGo.transform, "聊天", new Vector2(88, 40), new Vector2(136, 40), false);
            var toggleRect = (RectTransform)toggle.transform;
            toggleRect.anchorMin = toggleRect.anchorMax = Vector2.zero;
            toggleRect.anchoredPosition = new Vector2(88, 40);
            toggleLabel = toggle.GetComponentInChildren<Text>();
            toggle.onClick.AddListener(() => SetChatOpen(!chatOpen));

            height = Mathf.Max(height, 210);
            var panel = ArtTheme.Block(canvasGo.transform, "Panel", ArtTheme.Panel, new Vector2(20, 68), new Vector2(416, height), true, Vector2.zero);
            panel.pivot = Vector2.zero;
            chatPanel = panel.gameObject;
            var upper = height * .5f;
            var lower = -upper;
            ArtTheme.Block(panel, "Channel rule", ArtTheme.Teal, new Vector2(0, upper - 1), new Vector2(416, 2));
            channel = ArtTheme.Label(panel, "Channel", "发送：公共", 17, new Vector2(-135, upper - 24), new Vector2(118, 32), ArtTheme.Teal, TextAnchor.MiddleLeft);
            publicButton = ChannelButton(panel, "公共", new Vector2(-46, upper - 24), ChatChannel.Public);
            nearbyButton = ChannelButton(panel, "附近", new Vector2(19, upper - 24), ChatChannel.Nearby);
            roomButton = ChannelButton(panel, "房间", new Vector2(84, upper - 24), ChatChannel.Room);
            lockButton = ArtTheme.Button(panel, "反锁", new Vector2(163, upper - 24), new Vector2(62, 30), false);
            lockButton.GetComponentInChildren<Text>().fontSize = 16;
            lockButton.onClick.AddListener(() => { if (CanUseRoomControls) director.ToggleRoomLock(); });

            ArtTheme.Block(panel, "Header rule", ArtTheme.Surface, new Vector2(0, upper - 46), new Vector2(388, 1));
            messages = HistoryTextScroll.Create(panel, "Messages", Vector2.zero, new Vector2(388, height - 104), fontSize);
            messages.Text.color = ArtTheme.Text;
            messages.Text.alignment = TextAnchor.LowerLeft;

            var field = ArtTheme.Block(panel, "Input", ArtTheme.Ink, new Vector2(-41, lower + 26), new Vector2(306, 34), true);
            var txt = ArtTheme.Label(field, "Text", "", fontSize, Vector2.zero, new Vector2(286, 30), ArtTheme.Text, TextAnchor.MiddleLeft);
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            txt.supportRichText = false;
            input = field.gameObject.AddComponent<InputField>();
            input.textComponent = txt;
            input.targetGraphic = field.GetComponent<Image>();
            input.placeholder = ArtTheme.Label(field, "Placeholder", "输入消息…", fontSize, Vector2.zero, new Vector2(286, 30), ArtTheme.Muted, TextAnchor.MiddleLeft);
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = MatchRoundDirector.ChatMaxLength;
            input.onEndEdit.AddListener(_ => { if (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter)) Send(); });
            sendButton = ArtTheme.Button(panel, "发送", new Vector2(157, lower + 26), new Vector2(74, 34), true);
            sendButton.GetComponentInChildren<Text>().fontSize = 18;
            sendButton.onClick.AddListener(Send);
            BuildRoomBar(canvasGo.transform);
            SetChatOpen(chatOpen);
        }

        private void BuildRoomBar(Transform canvas)
        {
            roomBar = new GameObject("RoomBar", typeof(RectTransform), typeof(Image));
            roomBar.transform.SetParent(canvas, false);
            var rt = roomBar.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-20f, 154f); rt.sizeDelta = new Vector2(410f, 44f);
            roomBar.GetComponent<Image>().color = ArtTheme.Panel;
            roomBarText = ArtTheme.Label(roomBar.transform, "State", "房间 1 · 未反锁", 20,
                new Vector2(12f, 11f), new Vector2(160f, 22f), ArtTheme.Text, TextAnchor.MiddleLeft);
            var stateRect = roomBarText.rectTransform;
            stateRect.anchorMin = stateRect.anchorMax = Vector2.zero; stateRect.pivot = Vector2.zero;
            stateRect.anchoredPosition = new Vector2(12f, 11f);
            roomBarLockButton = ArtTheme.Button(roomBar.transform, "反锁", new Vector2(174f, 7f), new Vector2(96f, 30f), false);
            var lockRect = (RectTransform)roomBarLockButton.transform;
            lockRect.anchorMin = lockRect.anchorMax = Vector2.zero; lockRect.pivot = Vector2.zero;
            lockRect.anchoredPosition = new Vector2(174f, 7f);
            roomBarLockLabel = roomBarLockButton.GetComponentInChildren<Text>();
            if (roomBarLockLabel != null) { roomBarLockLabel.font = ArtTheme.FontFor("反锁"); roomBarLockLabel.fontSize = 20; }
            roomBarLockButton.onClick.AddListener(() =>
            {
                if (!CanUseRoomControls) return;
                director.ToggleRoomLock();
            });
            roomBarChatButton = ArtTheme.Button(roomBar.transform, "房间聊天", new Vector2(280f, 7f), new Vector2(118f, 30f), false);
            var chatRect = (RectTransform)roomBarChatButton.transform;
            chatRect.anchorMin = chatRect.anchorMax = Vector2.zero; chatRect.pivot = Vector2.zero;
            chatRect.anchoredPosition = new Vector2(280f, 7f);
            var chatLabel = roomBarChatButton.GetComponentInChildren<Text>();
            if (chatLabel != null) { chatLabel.font = ArtTheme.FontFor("房间聊天"); chatLabel.fontSize = 20; }
            roomBarChatButton.onClick.AddListener(() =>
            {
                if (!CanUseRoomControls) return;
                selectedChannel = ChatChannel.Room;
                SetChatOpen(true);
                if (input != null) input.ActivateInputField();
            });
            roomBar.SetActive(false);
        }

        private void SetChatOpen(bool open)
        {
            chatOpen = open;
            if (open) unread = 0;
            else
            {
                if (input != null && input.isFocused) input.DeactivateInputField();
                var events = EventSystem.current;
                var selected = events != null ? events.currentSelectedGameObject : null;
                if (selected != null && selected.transform.IsChildOf(chatPanel.transform)) events.SetSelectedGameObject(null);
            }
            chatPanel.SetActive(open);
            if (open) RefreshMessages(true);
            UpdateToggle();
        }

        private void UpdateToggle()
        {
            var label = chatOpen ? "收起聊天" : unread > 0 ? "聊天  " + unread : "聊天";
            if (toggleLabel.text == label) return;
            toggleLabel.color = unread > 0 ? ArtTheme.Gold : ArtTheme.Text;
            toggleLabel.text = label;
            toggleLabel.font = ArtTheme.FontFor(label);
        }

        private Button ChannelButton(Transform parent, string label, Vector2 position, ChatChannel target)
        {
            var button = ArtTheme.Button(parent, label, position, new Vector2(60, 30), false);
            button.GetComponentInChildren<Text>().fontSize = 16;
            button.onClick.AddListener(() =>
            {
                if (authority == null || !authority.Started || authority.LocalSeat < 0 || authority.IsLocalSpectator) return;
                selectedChannel = target;
            });
            return button;
        }

        private static void HighlightChannel(Button button, bool selected)
        {
            var color = selected ? ArtTheme.Teal : ArtTheme.Surface;
            if (button.image.color == color) return;
            button.image.color = color;
            button.GetComponentInChildren<Text>(true).color = selected ? ArtTheme.Ink : ArtTheme.Text;
        }

        private void RefreshMessages(bool latest = false)
        {
            if (!chatOpen) return;
            var follow = latest || messages.Scroll.content.rect.height <= messages.Scroll.viewport.rect.height + 1f
                || messages.Scroll.verticalNormalizedPosition < .03f;
            messages.SetContent(string.Join("\n", lines.ToArray()));
            if (follow) messages.Scroll.verticalNormalizedPosition = 0;
        }

        private void OnChat(int seat, ChatChannel channel, int roomId, string text, double serverTime)
        {
            RefreshChatContext();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devCheck) Debug.Log("[ChatCheck] recv game=" + lastGame + " channel=" + channel + " seat=" + seat + " text=" + text);
#endif
            if (channel == ChatChannel.Room && authority != null && authority.IsLocalSpectator && roomId != authority.SpectatorTargetRoom) return;
            var tag = channel == ChatChannel.Nearby ? "[附近] " : channel == ChatChannel.Spectator ? "[观战] " : channel == ChatChannel.Room ? (authority != null && authority.IsLocalSpectator ? "[观战·房间] " : "[房间] ") : "[公共] ";
            var sender = authority != null ? authority.PlayerNameForSeat(seat) : ("P" + (seat + 1));
            lines.Add(tag + sender + " · P" + (seat + 1) + ": " + text);
            while (lines.Count > maxLines) lines.RemoveAt(0);
            RefreshMessages();
            if (!chatOpen) { unread = Mathf.Min(unread + 1, maxLines); UpdateToggle(); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devCheck) Debug.Log("[ChatCheck] queue lines=" + lines.Count + " max=" + maxLines);
#endif
        }

        private void Send()
        {
            if (input == null || director == null || authority == null) return;
            if (!authority.Started || !authority.IsLocalPlayerSeated) return;
            RefreshChatContext();
            SyncRoomContext();
            var body = input.text.Trim(); if (body.Length == 0) return;
            var channel = authority.IsLocalSpectator ? ChatChannel.Spectator : selectedChannel;
            if (channel == ChatChannel.Room)
            {
                var roomId = director.LocalRoomId;
                if (roomId < 0) { input.text = string.Empty; selectedChannel = ChatChannel.Public; return; }
                director.SubmitChat(body, channel, roomId);
            }
            else director.SubmitChat(body, channel);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devCheck) Debug.Log("[ChatCheck] send channel=" + channel + " text=" + body);
#endif
            input.text = string.Empty; input.ActivateInputField();
        }

        private void TryBind()
        {
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (director != null && !subscribed) { director.ChatReceived += OnChat; subscribed = true; }
        }

        private void OnDestroy()
        {
            if (active == this) active = null;
            if (director != null) director.ChatReceived -= OnChat;
        }

        private void RefreshChatContext()
        {
            if (authority == null) return;
            if (authority.GameId == lastGame && authority.IsLocalSpectator == lastSpectator) return;
            lastGame = authority.GameId; lastSpectator = authority.IsLocalSpectator;
            lines.Clear(); RefreshMessages(true); input.text = string.Empty;
            unread = 0; UpdateToggle();
            selectedChannel = ChatChannel.Public;
            channel.text = lastSpectator ? "发送：观战" : "发送：公共";
            if (publicButton != null) publicButton.gameObject.SetActive(!lastSpectator);
            if (nearbyButton != null) nearbyButton.gameObject.SetActive(!lastSpectator);
        }

        private int SyncRoomContext()
        {
            if (director == null) return -1;
            var room = director.LocalRoomId;
            if (room != lastRoom)
            {
                lastRoom = room;
                lines.RemoveAll(line => line.StartsWith("[房间]"));
                RefreshMessages();
                if (selectedChannel == ChatChannel.Room) { input.text = string.Empty; if (room < 0) selectedChannel = ChatChannel.Public; }
            }
            return room;
        }

        private void SendProbe(string text)
        {
            input.text = text;
            sendButton.onClick.Invoke();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void NearbyProbe()
        {
            var now = Time.unscaledTime;
            if (nearbyMove != Vector2.zero) authority.SendLocalMoveInput(nearbyMove);
            if (nearbyStage == 0)
            {
                nearbyStage = 1; nearbyAt = now;
                selectedChannel = ChatChannel.Nearby; SendProbe("near-initial");
                nearbyMove = Vector2.right;
            }
            else if (nearbyStage == 1 && now >= nearbyAt + 3f)
            {
                nearbyStage = 2; nearbyAt = now;
                nearbyMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                selectedChannel = ChatChannel.Nearby; SendProbe("far");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.Log("[ChatNearby] probe stage=far");
#endif
            }
            else if (nearbyStage == 2 && now >= nearbyAt + 6f)
            {
                nearbyStage = 3; nearbyAt = now;
                nearbyMove = Vector2.left;
            }
            else if (nearbyStage == 3 && now >= nearbyAt + 3f)
            {
                nearbyStage = 4; nearbyAt = now;
                nearbyMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                selectedChannel = ChatChannel.Nearby; SendProbe("near-return");
            }
            else if (nearbyStage == 4 && now >= nearbyAt + 2f)
            {
                nearbyStage = 5;
                selectedChannel = ChatChannel.Public; SendProbe("public-after");
            }
        }
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void RoomProbe()
        {
            var now = Time.unscaledTime;
            if (!authority.TryGetLocalPlayerPosition(out var pos)) return;
            if (roomMove != Vector2.zero) authority.SendLocalMoveInput(roomMove);
            if (roomStage == 0) { roomStage = 1; roomMove = Vector2.left; }
            else if (roomStage == 1 && pos.x <= -13.6f) { roomStage = 2; roomMove = Vector2.up; }
            else if (roomStage == 2 && pos.y >= 8.8f)
            {
                roomStage = 3; roomAt = now; roomMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
            }
            else if (roomStage == 3 && now >= roomAt + 1f)
            {
                roomStage = 4;
                selectedChannel = ChatChannel.Room;
                if (roomButton != null && roomButton.gameObject.activeSelf) roomButton.onClick.Invoke();
                SendProbe("room-in-" + (authority.LocalSeat + 1));
                Debug.Log("[RoomProbe] seat=" + (authority.LocalSeat + 1) + " room=" + director.LocalRoomId + " pos=" + pos);
            }
            else if (roomStage == 4 && authority.LocalSeat == 0 && !roomLockSent && director.RoomOfSeat(1) == 0 && director.LocalRoomId == 0)
            {
                roomLockSent = true; roomStage = 5; roomAt = now;
                if (lockButton != null && lockButton.gameObject.activeSelf) lockButton.onClick.Invoke();
                Debug.Log("[RoomProbe] host locked room=" + director.LocalRoomId + " c1Room=" + director.RoomOfSeat(1) + " pos=" + pos);
            }
            else if (roomStage == 5 && authority.LocalSeat == 0 && !roomLockTextSent && director.RoomOfSeat(1) < 0)
            {
                roomLockTextSent = true;
                selectedChannel = ChatChannel.Room;
                if (roomButton != null && roomButton.gameObject.activeSelf) roomButton.onClick.Invoke();
                SendProbe("host-after-exit-1");
                Debug.Log("[RoomProbe] host room text after c1 exit pos=" + pos + " lock=" + director.IsRoomLocked(0) + " c1Room=" + director.RoomOfSeat(1));
            }
            else if (roomStage == 5 && authority.LocalSeat == 0 && now >= roomAt + 22f)
            {
                roomStage = 6;
                if (lockButton != null && lockButton.gameObject.activeSelf) lockButton.onClick.Invoke();
                Debug.Log("[RoomProbe] host unlocked room=" + director.LocalRoomId + " pos=" + pos);
            }
            else if (roomStage == 4 && authority.LocalSeat == 1 && director.IsRoomLocked(0) && !roomLockSent)
            {
                roomLockSent = true; roomStage = 5; roomAt = now;
            }
            else if (roomStage == 5 && authority.LocalSeat == 1 && now >= roomAt + 6f)
            {
                roomStage = 6; roomMove = Vector2.down;
                Debug.Log("[RoomProbe] inside player exiting while locked pos=" + pos);
            }
            else if (roomStage == 6 && authority.LocalSeat == 1 && pos.y <= 6.2f)
            {
                roomStage = 7; roomAt = now; roomMove = Vector2.left;
                Debug.Log("[RoomProbe] inside player outside locked pos=" + pos + " room=" + director.LocalRoomId);
            }
            else if (roomStage == 7 && authority.LocalSeat == 1 && now >= roomAt + 2f)
            {
                roomStage = 8; roomMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                Debug.Log("[RoomProbe] c1 aside outside pos=" + pos);
            }
        }

        private void RoomOutsideProbe()
        {
            var now = Time.unscaledTime;
            if (!authority.TryGetLocalPlayerPosition(out var pos)) return;
            if (outsideMove != Vector2.zero) authority.SendLocalMoveInput(outsideMove);
            if (outsideStage == 0) { outsideStage = 1; outsideMove = Vector2.left; }
            else if (outsideStage == 1 && pos.x <= -13.6f)
            {
                outsideStage = 2; outsideMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                Debug.Log("[RoomProbe] outside aligned before lock pos=" + pos + " room=" + director.LocalRoomId);
            }
            else if (outsideStage == 2)
            {
                outsideMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                if (director.IsRoomLocked(0) && !outsideSawLock)
                {
                    outsideSawLock = true; outsideStage = 3; outsideAt = now; outsideMove = Vector2.up;
                    Debug.Log("[RoomProbe] observed lock, start pushing pos=" + pos);
                }
                else if (!director.IsRoomLocked(0) && pos.y >= 6.2f) outsideStage = 4;
            }
            else if (outsideStage == 3)
            {
                outsideMove = Vector2.up;
                if (!outsideRejected && outsideSawLock && now >= outsideAt + 2f)
                {
                    outsideRejected = true;
                    director.SubmitChat("outside-room-never-deliver-" + (authority.LocalSeat + 1), ChatChannel.Room, 0);
                    Debug.Log("[RoomProbe] outside stale Room while locked pos=" + pos + " localRoom=" + director.LocalRoomId);
                }
                if (now >= outsideAt + 3f)
                {
                    outsideStage = 4; outsideMove = (Vector2.down + Vector2.right).normalized;
                    Debug.Log("[RoomProbe] retreat after push pos=" + pos);
                }
            }
            else if (outsideStage == 4)
            {
                if (pos.y <= 5.2f && pos.x >= -13.2f)
                {
                    outsideStage = 5; outsideMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                    Debug.Log("[RoomProbe] outsider aside pos=" + pos + " lock=" + director.IsRoomLocked(0));
                }
            }
            else if (outsideStage == 5)
            {
                if (!director.IsRoomLocked(0)) { outsideStage = 6; outsideMove = Vector2.up; Debug.Log("[RoomProbe] unlock observed after push pos=" + pos); }
            }
            else if (outsideStage == 6)
            {
                outsideMove = Vector2.up;
                if (director.LocalRoomId >= 0)
                {
                    outsideStage = 7; outsideMove = Vector2.zero; authority.SendLocalMoveInput(Vector2.zero);
                    selectedChannel = ChatChannel.Room;
                    if (roomButton != null && roomButton.gameObject.activeSelf) roomButton.onClick.Invoke();
                    SendProbe("room-after-unlock");
                    Debug.Log("[RoomProbe] outsider entered after unlock pos=" + pos + " room=" + director.LocalRoomId);
                }
            }
        }
#endif

        private void Update()
        {
            if (director == null || authority == null) { TryBind(); if (director == null || authority == null) return; }
            RefreshChatContext();
            if (CanUseRoomControls && !InputGate.Blocked &&
                InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.ContextInteract)))
                director.ToggleRoomLock();
            if (chatOpen && !FrontEndOverlay.IsOpen && InputProbe.GetKeyDown(KeyCode.Escape)) SetChatOpen(false);
            if (!chatOpen && !InputGate.Blocked && InputProbe.GetKeyDown(KeyCode.Return))
            {
                SetChatOpen(true);
                input.ActivateInputField();
            }
            var channelReady = authority.Started && authority.LocalSeat >= 0 && !authority.IsLocalSpectator;
            var spectatorRoom = authority.IsLocalSpectator ? authority.SpectatorTargetRoom : -1;
            if (spectatorRoom != lastSpectatorRoom)
            {
                lastSpectatorRoom = spectatorRoom;
                lines.RemoveAll(line => line.StartsWith("[观战·房间]"));
                RefreshMessages();
            }
            var room = SyncRoomContext();
            var roomReady = CanUseRoomControls;
            if (roomBar != null) roomBar.SetActive(roomReady);
            if (roomReady && roomBarText != null)
            {
                var locked = director.IsRoomLocked(room);
                roomBarText.color = locked ? ArtTheme.Gold : ArtTheme.Teal;
                roomBarText.text = "房间 " + (room + 1) + " · " + (locked ? "已反锁" : "未反锁");
                if (roomBarLockLabel != null)
                    roomBarLockLabel.text = KeyBindings.Get(GameInputAction.ContextInteract) + (locked ? " 解锁" : " 反锁");
            }
            if (publicButton != null) publicButton.gameObject.SetActive(channelReady);
            if (nearbyButton != null) nearbyButton.gameObject.SetActive(channelReady);
            if (roomButton != null) roomButton.gameObject.SetActive(roomReady);
            if (lockButton != null)
            {
                lockButton.gameObject.SetActive(roomReady);
                if (roomReady)
                {
                    var lockLabel = lockButton.GetComponentInChildren<Text>();
                    if (lockLabel != null) lockLabel.text = director.IsRoomLocked(room) ? "解锁" : "反锁";
                }
            }
            if (selectedChannel == ChatChannel.Room && !roomReady) selectedChannel = ChatChannel.Public;
            if (!authority.IsLocalSpectator)
            {
                channel.text = "发送：" + (selectedChannel == ChatChannel.Nearby ? "附近" : selectedChannel == ChatChannel.Room ? "房间" : "公共");
                HighlightChannel(publicButton, selectedChannel == ChatChannel.Public);
                HighlightChannel(nearbyButton, selectedChannel == ChatChannel.Nearby);
                HighlightChannel(roomButton, selectedChannel == ChatChannel.Room);
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (roomCheck && authority.Started && authority.IsLocalPlayerSeated && !authority.IsLocalSpectator) RoomProbe();
            if (roomOutside && authority.Started && authority.LocalSeat >= 0 && !authority.IsLocalSpectator) RoomOutsideProbe();
            if (nearbyCheck && authority.Started && authority.IsLocalPlayerSeated && !authority.IsLocalSpectator) NearbyProbe();
            if (devCheck && authority.Started && authority.IsLocalPlayerSeated)
            {
                var seat = authority.LocalSeat + 1;
                if (!sentRound1 && director.Round == 1 && director.Phase == RoundPhase.FirstTrade)
                {
                    sentRound1 = true;
                    SendProbe("pub-" + seat);
                }
                if (!handledRound2 && director.Round >= 2 && director.Phase == RoundPhase.FirstTrade)
                {
                    handledRound2 = true;
                    if (authority.IsLocalSpectator)
                    {
                        director.SubmitChat("stale-near-" + seat, ChatChannel.Nearby);
                        director.SubmitChat("stale-room-" + seat, ChatChannel.Room, 0);
                        Debug.Log("[ChatCheck] stale-near+room submitted seat=" + seat
                            + " publicBtn=" + publicButton.gameObject.activeSelf
                            + " nearbyBtn=" + nearbyButton.gameObject.activeSelf
                            + " roomBtn=" + (roomButton != null && roomButton.gameObject.activeSelf));
                        SendProbe("spec-after-" + seat);
                    }
                    else
                    {
                        nearbyButton.onClick.Invoke();
                        Debug.Log("[ChatCheck] real nearby button selected");
                        SendProbe("near-after-" + seat);
                    }
                }
            }
#endif
        }
    }
}
