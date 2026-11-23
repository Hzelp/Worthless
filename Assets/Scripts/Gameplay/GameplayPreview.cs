using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Worthless
{
    /// <summary>Gameplay 被单独打开时：显示预览提示与返回入口，不伪造对局。</summary>
    public sealed class GameplayPreview : MonoBehaviour
    {
        private void Start()
        {
            if (FindObjectOfType<Unity.Netcode.NetworkManager>() != null) return;
            var binder = FindObjectOfType<GameplayBinder>();
            if (binder != null) binder.HideAllSeats();
            var cam = Camera.main;
            if (cam != null)
            {
                cam.orthographic = true;
                cam.orthographicSize = 18f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                var follow = cam.GetComponent<CameraFollow2D>();
                if (follow != null) follow.enabled = false;
            }
            BuildPanel();
        }

        private void BuildPanel()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindObjectOfType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }

            var canvasObject = new GameObject("GameplayPreviewCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasObject.transform, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.07f, 0.45f);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = new Vector2(0.5f, 0.5f);
            labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.sizeDelta = new Vector2(900f, 160f);
            labelRect.anchoredPosition = new Vector2(0f, 120f);
            var text = label.GetComponent<Text>();
            text.font = font;
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = "Gameplay 预览（无网络会话）\n正常运行：MainMenu → 局域网联机 → 房主开赛。";

            var buttonObject = new GameObject("BackToMenuButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.SetParent(rect, false);
            buttonRect.anchorMin = new Vector2(0.5f, 0.5f);
            buttonRect.anchorMax = new Vector2(0.5f, 0.5f);
            buttonRect.sizeDelta = new Vector2(280f, 56f);
            buttonRect.anchoredPosition = new Vector2(0f, 20f);
            buttonObject.GetComponent<Image>().color = new Color(0.18f, 0.28f, 0.42f, 1f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            var buttonLabel = new GameObject("Label", typeof(RectTransform), typeof(Text));
            var buttonLabelRect = buttonLabel.GetComponent<RectTransform>();
            buttonLabelRect.SetParent(buttonRect, false);
            buttonLabelRect.anchorMin = Vector2.zero;
            buttonLabelRect.anchorMax = Vector2.one;
            buttonLabelOffset(buttonLabelRect);
            var buttonText = buttonLabel.GetComponent<Text>();
            buttonText.font = font;
            buttonText.fontSize = 22;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = Color.white;
            buttonText.text = "返回主菜单";
            button.onClick.AddListener(ReturnToMenu);
        }

        private static void buttonLabelOffset(RectTransform rect)
        {
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void ReturnToMenu()
        {
            TransitionOverlay.LoadScene(SceneNames.MainMenu);
        }
    }
}
