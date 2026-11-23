using UnityEngine;
namespace Worthless
{
    [CreateAssetMenu(menuName="Worthless/Key Binding Defaults")]
    public sealed class KeyBindingDefaults : ScriptableObject
    {
        public KeyCode MoveUp=KeyCode.W, MoveDown=KeyCode.S, MoveLeft=KeyCode.A, MoveRight=KeyCode.D, Interact=KeyCode.E, Nameplates=KeyCode.Tab, Run=KeyCode.LeftShift;
        public KeyCode ContextInteract = KeyCode.F;
        public float NameplateFontSize=40f, NameplateCharacterSize=0.16f;
        public Vector3 NameplateOffset=new Vector3(0f,-0.75f,0f);
        public int NameplateStatusFontSize=16;
        public Color NameplateStatusColor=new Color(1f,0.72f,0.38f,1f);
        public Color NameplateColor=new Color(1f,0.92f,0.65f,1f);
    }
}
