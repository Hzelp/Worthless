using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Worthless
{
    public sealed class TransitionOverlay : MonoBehaviour
    {
        private const float IrisDurationScale = 1.3f;

        private static TransitionOverlay instance;
        private CanvasGroup group;
        private AuctionIris iris;
        private MenuShutters shutters;
        private float startedAt;
        private Transition active;
        private Transition pending;
        private bool covered;

        private sealed class Transition
        {
            public string Key;
            public string Scene;
            public IEnumerator Work;
            public bool IsPage;
            public bool UseIris;
        }

        public static bool IsPlaying => instance != null && instance.active != null;
        public static bool IsScreenCovered => instance != null && instance.covered;
        public static bool Covers(string key) => IsScreenCovered && instance.active?.Key == key;

        public static void LoadScene(string scene, IEnumerator beforeLoad = null)
        {
            // Gameplay is loaded additively. Capture the source before network teardown unloads it.
            bool crossesMatch = scene == SceneNames.Gameplay || SceneManager.GetSceneByName(SceneNames.Gameplay).isLoaded;
            GetOrCreate().Request(new Transition { Key = scene, Scene = scene, Work = beforeLoad, UseIris = crossesMatch });
        }

        public static void Cover(string key, IEnumerator work)
        {
            GetOrCreate().Request(new Transition { Key = key, Work = work, UseIris = key == SceneNames.Gameplay });
        }

        public static void SwitchPage(Action apply)
        {
            GetOrCreate().Request(new Transition { Key = "LobbyPage", Work = ApplyPage(apply), IsPage = true });
        }

        private static IEnumerator ApplyPage(Action apply)
        {
            apply();
            yield return null;
        }

        private void Request(Transition request)
        {
            if (active == null) StartCoroutine(Play(request));
            else if (active.Key != request.Key || (request.Scene == null && !request.IsPage)) pending = request;
        }

        private static TransitionOverlay GetOrCreate()
        {
            if (instance != null) return instance;
            var root = new GameObject("TransitionOverlay", typeof(RectTransform));
            DontDestroyOnLoad(root);
            instance = root.AddComponent<TransitionOverlay>();
            instance.Build();
            return instance;
        }

        private void Build()
        {
            var frame = (RectTransform)transform;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;

            var shield = ArtTheme.Block(frame, "Input shield", Color.clear, Vector2.zero, Vector2.zero, true);
            ArtTheme.Stretch(shield);
            var blades = ArtTheme.Rect(frame, "Engraved iris", Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(blades);
            iris = blades.gameObject.AddComponent<AuctionIris>();
            iris.raycastTarget = false;
            var curtains = ArtTheme.Rect(frame, "Menu shutters", Vector2.zero, Vector2.zero);
            ArtTheme.Stretch(curtains);
            shutters = curtains.gameObject.AddComponent<MenuShutters>();
        }

        private void LateUpdate()
        {
            // A loaded scene or an activated page can register a Canvas after the
            // coroutine's last step. Refresh before this frame is rendered too.
            if (active == null) return;
            RefreshCanvasOrder();
            if (covered) Draw(1f, 1f);
        }

        private IEnumerator Play(Transition request)
        {
            active = request;
            iris.gameObject.SetActive(request.UseIris);
            shutters.gameObject.SetActive(!request.UseIris);
            RefreshCanvasOrder();
            startedAt = Time.unscaledTime;
            Draw(0f, 0f);
            group.alpha = 1f;
            group.blocksRaycasts = true;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

            try
            {
                LogTransition("cover", request.Key);
                yield return Animate(0f, 1f, request.IsPage ? .22f : request.UseIris ? .48f * IrisDurationScale : .55f);
                covered = true;
                yield return new WaitForSecondsRealtime(request.IsPage ? .045f : request.UseIris ? .11f * IrisDurationScale : .12f);
                if (request.Work != null) yield return request.Work;
                if (!string.IsNullOrEmpty(request.Scene))
                {
                    LogTransition("load", request.Scene);
                    var operation = SceneManager.LoadSceneAsync(request.Scene, LoadSceneMode.Single);
                    if (operation != null) yield return operation;
                }
                yield return null;
                RefreshCanvasOrder();
                covered = false;
                LogTransition("reveal", request.Key);
                yield return Animate(1f, 0f, request.IsPage ? .28f : request.UseIris ? .58f * IrisDurationScale : .75f);
                LogTransition("done", request.Key);
            }
            finally
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                covered = false;
                active = null;
                var next = pending;
                pending = null;
                if (next != null && (next.Scene == null || next.Scene != SceneManager.GetActiveScene().name))
                    StartCoroutine(Play(next));
            }
        }

        private void RefreshCanvasOrder()
        {
            // Runtime canvases created by the incoming scene can leave Unity's native
            // overlay order stale. A changed sorting value rebuilds that order.
            var canvas = GetComponent<Canvas>();
            canvas.sortingOrder = 31999;
            canvas.sortingOrder = 32000;
            Canvas.ForceUpdateCanvases();
        }

        private IEnumerator Animate(float start, float finish, float duration)
        {
            for (var elapsed = 0f; elapsed < duration; elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.05f))
            {
                var time = elapsed / duration;
                Draw(Mathf.SmoothStep(start, finish, time), time);
                yield return null;
            }
            Draw(finish, 1f);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private static void LogTransition(string stage, string scene) =>
            Debug.Log("[SceneTransition] " + stage + " -> " + scene + " t=" + Time.unscaledTime.ToString("F2"));

        private void Draw(float closed, float progress)
        {
            if (active.UseIris) iris.Pose(closed, 32f + (Time.unscaledTime - startedAt) * (54f / IrisDurationScale));
            else shutters.Pose(closed, progress);
        }
    }
}
