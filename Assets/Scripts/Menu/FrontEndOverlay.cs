using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Worthless
{
    public sealed class FrontEndOverlay : MonoBehaviour
    {
        private static FrontEndOverlay current;
        private GameObject modal;
        private RectTransform entries;
        private GameObject tutorial;
        private GameObject settings;
        private Text bindingMessage;
        private Button generalTab;
        private Button tutorialTab;
        private KeyBindingPanel bindings;
        private Text[] bindingLabels;
        private RawImage[] keycaps;
        private Text[] keycapFallbacks;
        private RectTransform controlSettings;
        private GameObject nicknameSettings;
        private InputField nicknameInput;
        private Text nicknameStatus;
        private Texture2D letters;
        private Texture2D symbols;
        private static readonly GameInputAction[] Actions = (GameInputAction[])Enum.GetValues(typeof(GameInputAction));

        public static bool IsOpen => current != null && current.modal != null && current.modal.activeSelf;

        public static void Configure(Texture2D letterAtlas, Texture2D symbolAtlas)
        {
            Install();
            current.letters = letterAtlas;
            current.symbols = symbolAtlas;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (current != null) return;
            var root = new GameObject("Settings overlay");
            DontDestroyOnLoad(root);
            current = root.AddComponent<FrontEndOverlay>();
            current.Build();
        }

        private void OnEnable()
        {
            SceneManager.activeSceneChanged += OnSceneChanged;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
        }

        private void OnSceneChanged(Scene previous, Scene next)
        {
            Close();
            RefreshEntries(next.name);
        }

        private void Build()
        {
            KeyBindingPanel.Ensure();
            bindings = FindObjectOfType<KeyBindingPanel>();
            var layer = new GameObject("Presentation canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            layer.transform.SetParent(transform, false);
            var canvas = layer.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = layer.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            entries = ArtTheme.Rect(layer.transform, "Menu shortcuts", new Vector2(-22, -20), new Vector2(136, 50), Vector2.one);
            entries.pivot = Vector2.one;
            ArtTheme.Button(entries, "设置", Vector2.zero, new Vector2(136, 48), false).onClick.AddListener(Open);

            var veil = ArtTheme.Block(layer.transform, "Modal shield", new Color32(7, 16, 13, 226), Vector2.zero, Vector2.zero, true);
            ArtTheme.Stretch(veil);
            modal = veil.gameObject;
            var card = ArtTheme.Block(veil, "Contents", ArtTheme.Panel, Vector2.zero, new Vector2(1110, 742));
            card.gameObject.AddComponent<CatalogueLeaf>();
            ArtTheme.Block(card, "Brass binding", ArtTheme.Gold, new Vector2(-552, 0), new Vector2(6, 742));
            ArtTheme.Block(card, "Header rule", ArtTheme.Rule, new Vector2(0, 268), new Vector2(1004, 2));
            ArtTheme.Label(card, "Header", "设置", 36, new Vector2(-235, 305), new Vector2(510, 62), ArtTheme.Gold, TextAnchor.MiddleLeft);
            generalTab = ArtTheme.Button(card, "常规", new Vector2(259, 305), new Vector2(136, 44), false);
            tutorialTab = ArtTheme.Button(card, "玩法教程", new Vector2(423, 305), new Vector2(158, 44), false);
            generalTab.onClick.AddListener(() => SelectTab(false));
            tutorialTab.onClick.AddListener(() => SelectTab(true));
            ArtTheme.Button(card, "返回", new Vector2(432, -309), new Vector2(144, 48), false).onClick.AddListener(Close);

            tutorial = ArtTheme.Rect(card, "Tutorial", Vector2.zero, Vector2.zero).gameObject;
            BuildTutorial(tutorial.transform);
            settings = ArtTheme.Rect(card, "Settings", Vector2.zero, Vector2.zero).gameObject;
            BuildSettings(settings.transform);
            modal.SetActive(false);
            RefreshEntries(SceneManager.GetActiveScene().name);
        }

        private void BuildTutorial(Transform parent)
        {
            keycaps = new RawImage[Actions.Length];
            keycapFallbacks = new Text[Actions.Length];
            string[] captions = { "上", "下", "左", "右", "交易", "名牌开关", "奔跑", "房门 / 大屏" };
            for (var i = 0; i < Actions.Length; i++)
            {
                var x = -455 + 130 * i;
                var slot = ArtTheme.Block(parent, "Key " + captions[i], ArtTheme.Surface, new Vector2(x, 218), new Vector2(120, 88));
                var imageRect = ArtTheme.Rect(slot, "From keyboard atlas", new Vector2(0, 14), new Vector2(42, 42));
                keycaps[i] = imageRect.gameObject.AddComponent<RawImage>();
                keycaps[i].raycastTarget = false;
                keycapFallbacks[i] = ArtTheme.Label(slot, "Key fallback", "", 18, new Vector2(0, 14), new Vector2(112, 40), ArtTheme.Gold, TextAnchor.MiddleCenter);
                keycapFallbacks[i].resizeTextForBestFit = true;
                keycapFallbacks[i].resizeTextMinSize = 12;
                keycapFallbacks[i].resizeTextMaxSize = 18;
                ArtTheme.Label(slot, "Action", captions[i], 17, new Vector2(0, -26), new Vector2(112, 27), ArtTheme.Text, TextAnchor.MiddleCenter);
            }
            TutorialCard(parent, -258, 94, "开局与目标",
                "单人或联机可设5～10席，空位补NPC。\n默认3轮，开局大屏显示你的秘密目标。\n高价组从高往低排，低价组从低往高排。\n两组各自抽第一或第二名获胜。");
            TutorialCard(parent, 258, 94, "交换物品",
                "走近黄色描边对象，按交易键。\n双方确认后成交。与NPC交易时选话术。\n每段可成交一次。两次失败后等下一段。\nCtrl投票跳过本段，再按撤票。");
            TutorialCard(parent, -258, -51, "情报与大屏回看",
                "每3秒播一条情报，涂黑因人而异。\n揭晓只公布谎言总数。走近大屏后，\n按房门／大屏键回看本轮情报和全部交易。\n交易记录标明轮次和阶段。");
            TutorialCard(parent, 258, -51, "秘密操作与结算",
                "每轮末在大屏选另一人，给其物品+5或-5。\n确认后锁定，30秒超时随机补齐。\n普通轮先算市场，末轮只结算秘密操作。\n并列加赛一次，仍并列则共同获胜。");
            TutorialCard(parent, -258, -196, "价格与价值日志",
                "价格下方可滚动回看整局价值日志。\n市场结算价记作下一轮的开场价值。\n转手物品保留当时价格，↑／↓表示此后\n公开市场的累计涨跌方向。");
            TutorialCard(parent, 258, -196, "聊天与房门",
                "按Enter打开文字聊天，可切换频道。\n公共给全场，附近给周围，房间给同屋。\n在私人房间按房门／大屏键反锁或解锁。\n反锁后，门的两侧都无法通行。");
            ArtTheme.Label(parent, "Movement and nameplates", "按住奔跑键加速。名牌键切换玩家名字与物品名称。", 19,
                new Vector2(-90, -307), new Vector2(820, 40), ArtTheme.Muted, TextAnchor.MiddleLeft);
        }

        private static void TutorialCard(Transform parent, float x, float y, string heading, string text)
        {
            var box = ArtTheme.Block(parent, heading, ArtTheme.Surface, new Vector2(x, y), new Vector2(494, 132));
            ArtTheme.Label(box, "Title", heading, 23, new Vector2(0, 42), new Vector2(454, 30), ArtTheme.Teal, TextAnchor.MiddleLeft);
            var body = ArtTheme.Label(box, "Body", text, 21, new Vector2(0, -18), new Vector2(454, 86), ArtTheme.Text, TextAnchor.UpperLeft);
            body.lineSpacing = 1.12f;
            body.resizeTextForBestFit = true;
            body.resizeTextMinSize = 16;
            body.resizeTextMaxSize = 21;
        }

        private void BuildSettings(Transform parent)
        {
            controlSettings = ArtTheme.Rect(parent, "Controls", Vector2.zero, Vector2.zero);
            ArtTheme.Label(controlSettings, "Title", "操作", 25, new Vector2(-410, 214), new Vector2(150, 45), ArtTheme.Teal, TextAnchor.MiddleLeft);
            bindingLabels = new Text[Actions.Length];
            for (var i = 0; i < Actions.Length; i++)
            {
                var action = Actions[i];
                var y = 150 - i * 48;
                ArtTheme.Label(controlSettings, "Action", KeyBindings.ActionName(action), 21, new Vector2(-341, y), new Vector2(278, 40), ArtTheme.Text, TextAnchor.MiddleLeft);
                var button = ArtTheme.Button(controlSettings, "改键", new Vector2(-92, y), new Vector2(188, 40), false);
                bindingLabels[i] = button.GetComponentInChildren<Text>();
                button.onClick.AddListener(() =>
                {
                    if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
                    bindings.BeginCapture(action);
                });
            }
            bindingMessage = ArtTheme.Label(controlSettings, "Binding status", "", 18, new Vector2(-273, -244), new Vector2(425, 38), ArtTheme.Gold, TextAnchor.MiddleLeft);
            ArtTheme.Button(controlSettings, "恢复默认键位", new Vector2(-395, -303), new Vector2(185, 48), false).onClick.AddListener(bindings.ResetDefaults);

            BuildNicknameSettings(parent);
        }

        private void BuildNicknameSettings(Transform parent)
        {
            var profile = ArtTheme.Rect(parent, "Nickname settings", Vector2.zero, Vector2.zero);
            nicknameSettings = profile.gameObject;
            ArtTheme.Label(profile, "Nickname title", "玩家昵称", 24, new Vector2(215, 214), new Vector2(430, 45), ArtTheme.Teal, TextAnchor.MiddleLeft);
            var fieldSize = new Vector2(278, 44);
            var field = ArtTheme.Block(profile, "Nickname input", ArtTheme.Ink, new Vector2(139, 150), fieldSize, true);
            nicknameInput = field.gameObject.AddComponent<InputField>();
            nicknameInput.targetGraphic = field.GetComponent<Image>();
            nicknameInput.lineType = InputField.LineType.SingleLine;
            var text = ArtTheme.Label(field, "Text", string.Empty, 20, Vector2.zero, fieldSize, ArtTheme.Text, TextAnchor.MiddleLeft);
            ArtTheme.Stretch(text.rectTransform);
            text.rectTransform.offsetMin = new Vector2(10, 0);
            text.rectTransform.offsetMax = new Vector2(-10, 0);
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.supportRichText = false;
            nicknameInput.textComponent = text;
            ArtTheme.Button(profile, "保存昵称", new Vector2(362, 150), new Vector2(136, 44), true).onClick.AddListener(SaveNickname);
            nicknameStatus = ArtTheme.Label(profile, "Nickname status", "", 18, new Vector2(215, 103), new Vector2(430, 32), ArtTheme.Muted, TextAnchor.MiddleLeft);
            nicknameInput.onValueChanged.AddListener(_ => SetNicknameStatus("单人和联机共用", ArtTheme.Muted));
        }

        private void SaveNickname()
        {
            var name = PlayerNameRules.Normalize(nicknameInput.text);
            if (!PlayerNameStore.Save(name))
            {
                SetNicknameStatus("保存失败，请重试。", ArtTheme.Gold);
                return;
            }
            nicknameInput.SetTextWithoutNotify(name);
            SetNicknameStatus("昵称已保存", ArtTheme.Teal);
        }

        private void SetNicknameStatus(string message, Color color)
        {
            nicknameStatus.font = ArtTheme.FontFor(message);
            nicknameStatus.text = message;
            nicknameStatus.color = color;
        }

        private void RefreshEntries(string scene)
        {
            if (entries == null) return;
            var main = scene == SceneNames.MainMenu;
            var gameplay = scene == SceneNames.Gameplay;
            var show = main || scene == SceneNames.LanLobby || (gameplay && FindObjectOfType<MatchAuthority>() != null);
            entries.gameObject.SetActive(show && !BroadcastHudView.IsSpotlight);
            entries.anchorMin = entries.anchorMax = gameplay ? new Vector2(1f, 0f) : Vector2.one;
            entries.anchoredPosition = gameplay ? new Vector2(-30, 136) : new Vector2(-30, -26);
        }

        private void Open()
        {
            if (BroadcastHudView.IsSpotlight) return;
            var mainMenu = SceneManager.GetActiveScene().name == SceneNames.MainMenu;
            nicknameSettings.SetActive(mainMenu);
            controlSettings.anchoredPosition = mainMenu ? Vector2.zero : new Vector2(242, 0);
            if (mainMenu)
            {
                nicknameInput.SetTextWithoutNotify(PlayerNameRules.Normalize(PlayerNameStore.Load()));
                SetNicknameStatus("单人和联机共用", ArtTheme.Muted);
            }
            modal.SetActive(true);
            SelectTab(false);
        }

        private void SelectTab(bool showTutorial)
        {
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            settings.SetActive(!showTutorial);
            tutorial.SetActive(showTutorial);
            SetTabSelected(generalTab, !showTutorial);
            SetTabSelected(tutorialTab, showTutorial);
            if (showTutorial)
            {
                bindings.ClosePanel();
                RefreshKeycaps();
            }
            else bindings.OpenPanel();
        }

        private static void SetTabSelected(Button button, bool selected)
        {
            button.interactable = !selected;
            var colors = button.colors;
            colors.disabledColor = Color.white;
            button.colors = colors;
            button.targetGraphic.color = selected ? ArtTheme.Teal : ArtTheme.Surface;
            button.GetComponentInChildren<Text>().color = selected ? ArtTheme.Ink : ArtTheme.Text;
        }

        private void Close()
        {
            if (modal == null || !modal.activeSelf) return;
            modal.SetActive(false);
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            bindings.ClosePanel();
            PlayerPrefs.Save();
        }

        private void Update()
        {
            if (entries == null) return;
            if (BroadcastHudView.IsSpotlight && IsOpen) Close();
            RefreshEntries(SceneManager.GetActiveScene().name);
            if (!IsOpen) return;
            if (Input.GetKeyDown(KeyCode.Escape) && !bindings.CapturingAction.HasValue) Close();
            if (!settings.activeSelf) return;
            if (nicknameInput.isFocused && bindings.CapturingAction.HasValue) bindings.CancelCapture();
            for (var i = 0; i < Actions.Length; i++)
            {
                var action = Actions[i];
                bindingLabels[i].text = bindings.CapturingAction == action ? "按下新键…" : KeyBindings.Get(action).ToString();
            }
            bindingMessage.text = bindings.CapturingAction.HasValue && string.IsNullOrEmpty(bindings.Message)
                ? "按 Esc 取消录入" : bindings.Message;
        }

        private void RefreshKeycaps()
        {
            for (var i = 0; i < Actions.Length; i++)
            {
                var key = KeyBindings.Get(Actions[i]);
                var letter = (int)key - (int)KeyCode.A;
                var atlas = letters;
                var row = letter;
                if (key == KeyCode.Tab || key == KeyCode.LeftShift || key == KeyCode.RightShift)
                {
                    atlas = symbols;
                    row = key == KeyCode.Tab ? 2 : 5;
                }
                else if (letter < 0 || letter >= 26) atlas = null;
                var draw = atlas != null && (row + 1) * 16 <= atlas.height;
                keycaps[i].gameObject.SetActive(draw);
                keycapFallbacks[i].gameObject.SetActive(!draw);
                keycapFallbacks[i].text = key == KeyCode.LeftShift ? "左 Shift" : key == KeyCode.RightShift ? "右 Shift" : key.ToString();
                keycapFallbacks[i].font = ArtTheme.FontFor(keycapFallbacks[i].text);
                if (!draw) continue;
                atlas.filterMode = FilterMode.Point;
                keycaps[i].texture = atlas;
                keycaps[i].uvRect = new Rect(0f, 1f - (row + 1) * 16f / atlas.height, 16f / atlas.width, 16f / atlas.height);
            }
        }
    }

    internal static class ArtTheme
    {
        public static readonly Color Ink = new Color32(14, 26, 23, 255);
        public static readonly Color Panel = new Color32(24, 42, 35, 255);
        public static readonly Color Surface = new Color32(39, 60, 49, 255);
        public static readonly Color Teal = new Color32(165, 206, 175, 255);
        public static readonly Color Gold = new Color32(227, 190, 125, 255);
        public static readonly Color Text = new Color32(245, 237, 216, 255);
        public static readonly Color Muted = new Color32(193, 202, 183, 255);
        public static readonly Color Rule = new Color32(104, 110, 83, 255);
        private static Font pixelFont;
        private static Font defaultFont;

        public static Font FontFor(string content)
        {
            if (pixelFont == null) pixelFont = Resources.Load<Font>("Fonts/FusionPixel10");
            if (defaultFont == null) defaultFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (pixelFont == null) return defaultFont;
            foreach (var c in content)
                if (!char.IsWhiteSpace(c) && !pixelFont.HasCharacter(c)) return defaultFont;
            return pixelFont;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size, Vector2? anchor = null)
        {
            var element = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)element.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor ?? new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            return rect;
        }

        public static RectTransform Block(Transform parent, string name, Color color, Vector2 pos, Vector2 size, bool raycast = false, Vector2? anchor = null)
        {
            var rect = Rect(parent, name, pos, size, anchor);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return rect;
        }

        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        public static Text Label(Transform parent, string name, string content, int size, Vector2 pos, Vector2 area, Color color, TextAnchor alignment)
        {
            var rect = Rect(parent, name, pos, area);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = FontFor(content);
            text.text = content;
            text.color = color;
            text.fontSize = size;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string caption, Vector2 pos, Vector2 size, bool primary)
        {
            var rect = Block(parent, caption, primary ? Gold : Surface, pos, size, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = Label(rect, "Label", caption, 22, Vector2.zero, size, primary ? Ink : Text, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return button;
        }
    }
}
