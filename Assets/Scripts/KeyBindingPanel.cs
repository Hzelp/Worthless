using System;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Worthless
{
    public sealed class KeyBindingPanel : MonoBehaviour
    {
        private bool open, bindRan;
        private GameInputAction capturing=(GameInputAction)(-1);
        private string message="";
        public GameInputAction? CapturingAction => capturing >= 0 ? capturing : (GameInputAction?)null;
        public string Message => message;
        private void Awake(){
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var a=System.Environment.GetCommandLineArgs();
            if(System.Array.IndexOf(a,"-p101bindfile")>=0){KeyBindings.Get(GameInputAction.Nameplates);KeyBindings.Get(GameInputAction.Interact);KeyBindings.Get(GameInputAction.MoveRight);}
#endif
        }
        public static void Ensure(){if(FindObjectOfType<KeyBindingPanel>()!=null)return;var g=new GameObject("KeyBindingPanel");DontDestroyOnLoad(g);g.AddComponent<KeyBindingPanel>();}
        public void OpenPanel(){open=true;message="";KeyBindings.Capturing=true;}
        public void BeginCapture(GameInputAction a){capturing=a;message="";}
        public void AcceptKey(KeyCode k){if(capturing==(GameInputAction)(-1))return;var err=KeyBindings.TryBind(capturing,k);message=err??("已设置 "+KeyBindings.ActionName(capturing)+" = "+k);Debug.Log("[P101Bind] accept "+capturing+" "+k+" result="+message);if(err==null)capturing=(GameInputAction)(-1);}
        public void CancelCapture(){capturing=(GameInputAction)(-1);message="已取消";Debug.Log("[P101Bind] cancel");}
        public void HandleKey(KeyCode k){if(k==KeyCode.Escape)CancelCapture();else if(k!=KeyCode.None)AcceptKey(k);}
        public void ResetDefaults()
        {
            var error = KeyBindings.ResetAll();
            KeyBindings.ResetCache();
            message = error ?? "已恢复默认";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (error == null) Debug.Log("[P101Bind] reset defaults"); else Debug.LogError(error);
#endif
        }
        public void ClosePanel(){open=false;capturing=(GameInputAction)(-1);KeyBindings.Capturing=false;}
        private void Update(){
            var s=SceneManager.GetActiveScene().name;
            if(s!=SceneNames.MainMenu&&s!=SceneNames.LanLobby&&s!=SceneNames.Gameplay){ClosePanel();return;}
            KeyBindings.Capturing=open||capturing!=(GameInputAction)(-1);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if(!bindRan&&System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-p101bindreset")>=0&&s=="LanLobby"&&Time.unscaledTime>3f){
                bindRan=true;OpenPanel();ResetDefaults();Debug.Log("[P101Bind] values after reset n="+KeyBindings.Get(GameInputAction.Nameplates)+" i="+KeyBindings.Get(GameInputAction.Interact)+" r="+KeyBindings.Get(GameInputAction.MoveRight));ClosePanel();
            }
            if(!bindRan&&System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-p101bind")>=0&&s=="LanLobby"&&Time.unscaledTime>3f){
                bindRan=true;OpenPanel();
                BeginCapture(GameInputAction.Nameplates);HandleKey(KeyCode.E); // conflict expected
                HandleKey(KeyCode.Escape);
                BeginCapture(GameInputAction.Nameplates);AcceptKey(KeyCode.F6);
                BeginCapture(GameInputAction.Interact);AcceptKey(KeyCode.F7);
                BeginCapture(GameInputAction.MoveRight);AcceptKey(KeyCode.F8);
                BeginCapture(GameInputAction.Nameplates);HandleKey(KeyCode.F6);
                Debug.Log("[P101Bind] values n="+KeyBindings.Get(GameInputAction.Nameplates)+" i="+KeyBindings.Get(GameInputAction.Interact)+" r="+KeyBindings.Get(GameInputAction.MoveRight));
                ClosePanel();
            }
#endif
        }
        private void OnGUI(){
            var input = Event.current;
            if (!open || !CapturingAction.HasValue || input.type != EventType.KeyDown) return;
            HandleKey(input.keyCode);
            input.Use();
        }
    }
}
