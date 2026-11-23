using UnityEngine;
using Worthless.Lobby;

namespace Worthless
{
    public sealed class SeatInputSource : MonoBehaviour
    {
        private MatchAuthority authority;
        private float lastSendTime = -1f;
        private Vector2 lastSent = new Vector2(-1f, -1f);
        private bool lastRun;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private Vector2 devDirection = Vector2.zero;
        public void SetDevDirection(Vector2 direction) { devDirection = direction; }
#endif

        public void Configure(MatchAuthority matchAuthority) { authority = matchAuthority; }

        private void Update()
        {
            if (authority == null || !authority.IsLocalPlayerSeated || !authority.IsStartedForLocal) return;
            if (authority.IsLocalSpectator || authority.GameOver) return;
            var now = Time.unscaledTime;
            var focused = InputProbe.Focused;
            var typing = InputGate.ModalOpen || InputGate.IsTyping();
            var direction = Vector2.zero;
            var devOverride = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            devOverride = devDirection != Vector2.zero;
            if (!typing) direction = devOverride ? devDirection : (focused ? ReadDirection() : Vector2.zero);
#else
            direction = focused && !typing ? ReadDirection() : Vector2.zero;
#endif
            var run = direction != Vector2.zero && InputProbe.GetKey(KeyBindings.Get(GameInputAction.Run));
            if (run != lastRun) { lastRun = run; lastSent = new Vector2(-1f, -1f); }
            if (!InputSendPolicy.ShouldSend(now, lastSendTime, direction.x, direction.y, lastSent.x, lastSent.y, focused, typing, devOverride)) return;
            lastSendTime = now; lastSent = direction;
            authority.SendLocalMoveInput(direction, run);
        }

        private static Vector2 ReadDirection()
        {
            var x = 0f; var y = 0f;
            if (InputProbe.GetKey(KeyBindings.Get(GameInputAction.MoveLeft)) || InputProbe.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (InputProbe.GetKey(KeyBindings.Get(GameInputAction.MoveRight)) || InputProbe.GetKey(KeyCode.RightArrow)) x += 1f;
            if (InputProbe.GetKey(KeyBindings.Get(GameInputAction.MoveDown)) || InputProbe.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (InputProbe.GetKey(KeyBindings.Get(GameInputAction.MoveUp)) || InputProbe.GetKey(KeyCode.UpArrow)) y += 1f;
            return new Vector2(x, y);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus || authority == null) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (devDirection != Vector2.zero) return;
#endif
            lastSent = Vector2.zero; lastSendTime = Time.unscaledTime;
            authority.SendLocalMoveInput(Vector2.zero);
        }
    }
}
