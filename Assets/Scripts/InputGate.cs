using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace Worthless
{
    public static class InputGate
    {
        public static bool IsTyping()
        {
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            var go = es.currentSelectedGameObject;
            return go.GetComponent<InputField>() != null || go.GetComponent("TMP_InputField") != null;
        }
        public static bool ModalOpen => KeyBindings.Capturing || FrontEndOverlay.IsOpen ||
            BroadcastHudView.BlocksMovement || TradeHudView.IsOpen || TransitionOverlay.IsPlaying;
        public static bool Blocked => ModalOpen || !InputProbe.Focused || IsTyping();
    }
}
