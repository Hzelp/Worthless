using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Worthless.Lobby;

namespace Worthless
{
    [DefaultExecutionOrder(50)]
    public sealed class NameplateController : MonoBehaviour
    {
        private static readonly Color CardColor = new Color32(12, 22, 34, 241);
        private readonly List<Rect> occupied = new List<Rect>(MatchAuthority.SeatCount);
        private readonly Nameplate[] labels = new Nameplate[MatchAuthority.SeatCount];
        private MatchAuthority authority;
        private MatchRoundDirector director;
        private RectTransform canvasRect;
        private RectTransform labelsLayer;
        private bool toggledOn;
        private uint gameId;
        private bool wasShowing;
        private KeyBindingDefaults defaults;

        public static void Boot()
        {
            if (FindObjectOfType<NameplateController>() != null) return;
            new GameObject("Nameplates").AddComponent<NameplateController>();
        }

        private void Awake()
        {
            defaults = KeyBindings.Defaults;
            var root = new GameObject("NameplateCanvas", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6;
            canvasRect = (RectTransform)root.transform;
            labelsLayer = ArtTheme.Rect(canvasRect, "Labels", Vector2.zero, Vector2.zero);
            labelsLayer.gameObject.SetActive(false);
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        }

        private void LateUpdate()
        {
            if (authority == null) authority = FindObjectOfType<MatchAuthority>();
            if (director == null) director = FindObjectOfType<MatchRoundDirector>();
            if (authority != null && authority.GameId != gameId)
            {
                gameId = authority.GameId;
                toggledOn = false;
            }
            var blocked = InputGate.Blocked;
            if (!blocked && authority != null && authority.Started &&
                InputProbe.GetKeyDown(KeyBindings.Get(GameInputAction.Nameplates)))
                toggledOn = !toggledOn;

            var camera = Camera.main;
            var show = !blocked && authority != null && authority.Started &&
                camera != null && canvasRect.rect.width > 0f;
            labelsLayer.gameObject.SetActive(show);
            if (show) PositionLabels(camera);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var namesShowing = show && toggledOn;
            if (namesShowing != wasShowing)
            {
                Debug.Log("[P101Probe] nameplates show=" + namesShowing + " active=" + (show ? occupied.Count : 0)
                    + " offset=" + defaults.NameplateOffset + " fontSize=" + defaults.NameplateFontSize);
                wasShowing = namesShowing;
            }
#endif
        }

        public bool ToggledOn => toggledOn;
        public bool LabelsVisible => toggledOn && labelsLayer != null && labelsLayer.gameObject.activeInHierarchy;

        private void PositionLabels(Camera camera)
        {
            occupied.Clear();
            var localSeat = authority.LocalSeat;
            if (localSeat >= 0 && localSeat < labels.Length) PlaceLabel(localSeat, camera);
            for (var seat = 0; seat < labels.Length; seat++)
            {
                if (seat != localSeat) PlaceLabel(seat, camera);
            }
        }

        private void PlaceLabel(int seat, Camera camera)
        {
            var actor = authority.GetSeatActor(seat);
            var label = labels[seat];
            if (actor == null || !actor.gameObject.activeInHierarchy)
            {
                if (label != null) label.Root.SetActive(false);
                return;
            }

            var viewport = camera.WorldToViewportPoint(actor.transform.position);
            if (viewport.z <= 0 || viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1)
            {
                if (label != null) label.Root.SetActive(false);
                return;
            }

            var foot = camera.WorldToScreenPoint(actor.transform.position + defaults.NameplateOffset);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, foot, null, out var anchor);
            var status = string.Empty;
            if (director != null && RoundStateMachine.IsTradePhase(director.Phase))
            {
                if (director.TryGetTradePublic(seat, out var trade) && trade.Exhausted > 0)
                    status = "交易用尽";
                if (director.IsLocalReturnTradeBlocked(seat))
                    status += (status.Length > 0 ? " · " : string.Empty) + "不可换回";
            }
            if (!toggledOn && status.Length == 0)
            {
                if (label != null) label.Root.SetActive(false);
                return;
            }
            if (label == null) labels[seat] = label = new Nameplate(labelsLayer);
            var itemName = director != null && director.TryGetPublicItem(seat, out var item)
                ? director.GetItemName(item.DefIndex) : string.Empty;
            label.SetText(toggledOn ? authority.PlayerNameForSeat(seat) : string.Empty,
                toggledOn ? itemName : string.Empty, status, seat == authority.LocalSeat, defaults);
            if (TryPlace(anchor, label.Size, out var placed))
            {
                label.Root.SetActive(true);
                label.Place(anchor, placed);
                occupied.Add(placed);
            }
            else label.Root.SetActive(false);
        }

        private bool TryPlace(Vector2 origin, Vector2 size, out Rect result)
        {
            var area = canvasRect.rect;
            var x = Mathf.Clamp(origin.x - size.x * 0.5f, area.xMin + 12, area.xMax - size.x - 12);
            var baseY = Mathf.Clamp(origin.y - size.y - 7f, area.yMin + 12, area.yMax - size.y - 12);
            for (var row = 0; row < occupied.Count * 2 + 1; row++)
            {
                var distance = (row + 1) / 2;
                var direction = row % 2 == 0 ? 1 : -1;
                var y = baseY + direction * distance * (size.y + 5);
                if (y < area.yMin + 12 || y + size.y > area.yMax - 12) continue;
                var candidate = new Rect(x, y, size.x, size.y);
                var padded = new Rect(x - 4, y - 3, size.x + 8, size.y + 6);
                var overlaps = false;
                foreach (var earlier in occupied)
                {
                    if (!earlier.Overlaps(padded)) continue;
                    overlaps = true;
                    break;
                }
                if (overlaps) continue;
                result = candidate;
                return true;
            }
            result = default;
            return false;
        }

        private sealed class Nameplate
        {
            public readonly GameObject Root;
            private readonly RectTransform card;
            private readonly RectTransform guide;
            private readonly RectTransform marker;
            private readonly Image edge;
            private readonly Text playerText;
            private readonly Text itemText;
            private readonly Text statusText;
            private string player;
            private string item;
            private string status;
            private bool local;
            public Vector2 Size { get; private set; }

            public Nameplate(RectTransform parent)
            {
                var root = ArtTheme.Rect(parent, "Nameplate", Vector2.zero, Vector2.zero);
                Root = root.gameObject;
                guide = ArtTheme.Block(root, "Leader", new Color32(154, 206, 196, 156), Vector2.zero, Vector2.zero);
                marker = ArtTheme.Block(root, "Foot", ArtTheme.Teal, Vector2.zero, new Vector2(5f, 5f));
                marker.localRotation = Quaternion.Euler(0, 0, 45f);
                card = ArtTheme.Block(root, "Card", CardColor, Vector2.zero, Vector2.zero);
                var rule = ArtTheme.Block(card, "Accent", ArtTheme.Teal, Vector2.zero, Vector2.zero);
                rule.anchorMin = new Vector2(0, 0);
                rule.anchorMax = new Vector2(0, 1);
                rule.offsetMin = Vector2.zero;
                rule.offsetMax = new Vector2(3f, 0);
                edge = rule.GetComponent<Image>();
                playerText = ArtTheme.Label(card, "Player", string.Empty, 19, Vector2.zero, Vector2.zero, ArtTheme.Text, TextAnchor.MiddleLeft);
                itemText = ArtTheme.Label(card, "Item", string.Empty, 16, Vector2.zero, Vector2.zero, ArtTheme.Gold, TextAnchor.MiddleLeft);
                statusText = ArtTheme.Label(card, "TradeStatus", string.Empty, 16, Vector2.zero, Vector2.zero, ArtTheme.Gold, TextAnchor.MiddleLeft);
                playerText.supportRichText = itemText.supportRichText = statusText.supportRichText = false;
                playerText.verticalOverflow = itemText.verticalOverflow = statusText.verticalOverflow = VerticalWrapMode.Truncate;
                playerText.resizeTextForBestFit = itemText.resizeTextForBestFit = statusText.resizeTextForBestFit = true;
            }

            public void SetText(string playerName, string itemName, string tradeStatus, bool own, KeyBindingDefaults defaults)
            {
                if (player == playerName && item == itemName && status == tradeStatus && local == own) return;
                player = playerName;
                item = itemName;
                status = tradeStatus;
                local = own;
                var scale = Mathf.Clamp(defaults.NameplateCharacterSize / 0.16f, 0.55f, 1.6f);
                var playerSize = Mathf.Clamp(Mathf.RoundToInt(defaults.NameplateFontSize * 0.46f * scale), 13, 32);
                var itemSize = Mathf.Max(12, playerSize - 2);
                SetLabel(playerText, playerName, playerSize);
                SetLabel(itemText, itemName, itemSize);
                var statusSize = Mathf.Clamp(Mathf.RoundToInt(defaults.NameplateStatusFontSize * scale), 12, 26);
                SetLabel(statusText, tradeStatus, statusSize);
                playerText.color = own ? ArtTheme.Teal : ArtTheme.Text;
                itemText.color = defaults.NameplateColor;
                statusText.color = defaults.NameplateStatusColor;
                edge.color = own ? ArtTheme.Teal : new Color32(82, 112, 128, 255);
                var width = Mathf.Clamp(Mathf.Max(playerText.preferredWidth, itemText.preferredWidth, statusText.preferredWidth) + 25, 88, 245);
                var height = 10f;
                if (playerName.Length > 0) height += playerSize + 4;
                if (itemName.Length > 0) height += itemSize + 4;
                if (tradeStatus.Length > 0) height += statusSize + 4;
                Size = new Vector2(width, height);
                card.sizeDelta = Size;
                var y = height * .5f - 5;
                PlaceRow(playerText, playerName.Length > 0, playerSize, width, ref y);
                PlaceRow(itemText, itemName.Length > 0, itemSize, width, ref y);
                PlaceRow(statusText, tradeStatus.Length > 0, statusSize, width, ref y);
            }

            private static void PlaceRow(Text text, bool visible, int size, float width, ref float y)
            {
                text.gameObject.SetActive(visible);
                if (!visible) return;
                var height = size + 4;
                text.rectTransform.sizeDelta = new Vector2(width - 22, height);
                text.rectTransform.anchoredPosition = new Vector2(3, y - height * .5f);
                y -= height;
            }

            private static void SetLabel(Text label, string content, int size)
            {
                label.text = content ?? string.Empty;
                label.font = ArtTheme.FontFor(label.text);
                label.fontSize = size;
                label.resizeTextMinSize = Mathf.Max(10, size - 7);
                label.resizeTextMaxSize = size;
            }

            public void Place(Vector2 origin, Rect position)
            {
                card.anchoredPosition = position.center;
                var top = origin.y >= position.center.y ? position.yMax : position.yMin;
                var endpoint = new Vector2(Mathf.Clamp(origin.x, position.xMin + 6, position.xMax - 6), top);
                var direction = endpoint - origin;
                marker.anchoredPosition = origin;
                guide.anchoredPosition = (origin + endpoint) * 0.5f;
                guide.sizeDelta = new Vector2(direction.magnitude, 1.5f);
                guide.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
            }
        }
    }
}
