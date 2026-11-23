using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>摆放主菜单并保留场景中联机和退出按钮的回调。</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button lanButton;
        [SerializeField] private Button quitButton;
        private Button soloButton;
        [SerializeField] private Texture2D letterKeycaps;
        [SerializeField] private Texture2D symbolKeycaps;

        public Button LanButton { get { return lanButton; } }
        public Button SoloButton { get { return soloButton; } }
        public Button QuitButton { get { return quitButton; } }

        public void Configure(Button lan, Button quit)
        {
            lanButton = lan;
            quitButton = quit;
        }

        private void Awake()
        {
            Time.timeScale = 1f;
            Debug.Log("[Menu] MainMenu 就绪");
            if (lanButton != null) lanButton.onClick.AddListener(RequestLanLobby);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            FrontEndOverlay.Configure(letterKeycaps, symbolKeycaps);
            StyleMenu();
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-worthless-auto-solo") >= 0) Invoke(nameof(RequestSolo), 0.6f);
        }

        private void StyleMenu()
        {
            if (lanButton == null || quitButton == null) return;
            var panel = (RectTransform)lanButton.transform.parent;
            var canvas = panel.GetComponentInParent<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var background = ArtTheme.Block(canvas.transform, "Background", ArtTheme.Ink, Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(background);
            background.SetAsFirstSibling();
            var frame = ArtTheme.Rect(canvas.transform, "Engraved frame", Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(frame);
            frame.SetSiblingIndex(background.GetSiblingIndex() + 1);
            frame.gameObject.AddComponent<MenuEngraving>().raycastTarget = false;
            panel.sizeDelta = new Vector2(1440, 730);
            panel.GetComponent<Image>().enabled = false;

            var copy = ArtTheme.Rect(panel, "Catalogue title", new Vector2(-376, 30), new Vector2(628, 650));
            var actions = ArtTheme.Rect(panel, "Game modes", new Vector2(-418, -170), new Vector2(544, 226));
            var balance = ArtTheme.Rect(panel, "Auction balance", new Vector2(356, -10), new Vector2(646, 692));
            balance.gameObject.AddComponent<MenuEngraving>().Configure(MenuEngraving.Pattern.Balance);
            var instrument = ArtTheme.Rect(balance, "Instrument", Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(instrument);
            instrument.gameObject.AddComponent<AuctionBalance>().raycastTarget = false;

            var ornament = ArtTheme.Rect(copy, "Title engraving", new Vector2(-24, 108), new Vector2(580, 226));
            ornament.gameObject.AddComponent<MenuEngraving>().Configure(MenuEngraving.Pattern.Title);
            var title = (RectTransform)panel.Find("Title");
            title.SetParent(copy, false);
            title.anchoredPosition = new Vector2(-24, 104);
            title.sizeDelta = new Vector2(600, 140.625f);

            lanButton.transform.SetParent(actions, false);
            StyleButton(lanButton, "局域网联机", 75, true);
            var solo = ArtTheme.Block(actions, "SoloButton", ArtTheme.Surface, Vector2.zero, Vector2.zero, true);
            soloButton = solo.gameObject.AddComponent<Button>();
            soloButton.targetGraphic = solo.GetComponent<Image>();
            ArtTheme.Label(solo, "Label", "单人游戏", 26, Vector2.zero, Vector2.zero, ArtTheme.Text, TextAnchor.MiddleLeft);
            StyleButton(soloButton, "单人游戏", 0, false);
            soloButton.onClick.AddListener(RequestSolo);
            quitButton.transform.SetParent(actions, false);
            StyleButton(quitButton, "退出游戏", -75, false);
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null) events.firstSelectedGameObject = lanButton.gameObject;
            StartCoroutine(Reveal(balance, 0f, 22f));
            StartCoroutine(Reveal(copy, 0.06f, 15f));
            StartCoroutine(Reveal((RectTransform)lanButton.transform, .13f, 12f));
            StartCoroutine(Reveal((RectTransform)soloButton.transform, .19f, 12f));
            StartCoroutine(Reveal((RectTransform)quitButton.transform, .25f, 12f));
        }

        private void Update()
        {
            var events = UnityEngine.EventSystems.EventSystem.current;
            if (events != null && events.currentSelectedGameObject == null && !FrontEndOverlay.IsOpen && !TransitionOverlay.IsPlaying &&
                (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)))
                events.SetSelectedGameObject(lanButton.gameObject);
        }

        private static System.Collections.IEnumerator Reveal(RectTransform rect, float delay, float distance)
        {
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            var end = rect.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < delay + 0.52f)
            {
                var t = Mathf.Clamp01((elapsed - delay) / 0.52f);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                group.alpha = eased;
                rect.anchoredPosition = end + Vector2.down * (distance * (1f - eased));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
            group.alpha = 1f;
            rect.anchoredPosition = end;
        }

        private static void StyleButton(Button button, string caption, float y, bool primary)
        {
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(0, y);
            rect.sizeDelta = new Vector2(544, 62);
            var image = button.GetComponent<Image>();
            image.sprite = null;
            image.color = primary ? ArtTheme.Gold : ArtTheme.Surface;
            var ink = primary ? ArtTheme.Ink : ArtTheme.Text;
            var label = button.GetComponentInChildren<Text>();
            ArtTheme.Stretch(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(24, 0);
            label.alignment = TextAnchor.MiddleLeft;
            label.font = ArtTheme.FontFor(caption);
            label.fontSize = 26;
            label.color = ink;
            label.text = caption;
            label.raycastTarget = false;
            var marker = ArtTheme.Rect(rect, "Focus arrow", new Vector2(230, 0), new Vector2(28, 24));
            ArtTheme.Block(marker, "Shaft", ink, new Vector2(-2, 0), new Vector2(19, 2));
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var tip = ArtTheme.Block(marker, "Tip", ink, new Vector2(5, sign * 3), new Vector2(10, 2));
                tip.localRotation = Quaternion.Euler(0, 0, -sign * 42f);
            }
            button.gameObject.AddComponent<MenuFocusMotion>().Configure(marker);
        }

        /// <summary>真实按钮回调：进入联机房间。</summary>
        public void RequestLanLobby()
        {
            SoloLaunch.Pending = false;
            TransitionOverlay.LoadScene(SceneNames.LanLobby);
        }

        public void RequestSolo()
        {
            SoloLaunch.Pending = true;
            TransitionOverlay.LoadScene(SceneNames.LanLobby);
        }

        public void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
