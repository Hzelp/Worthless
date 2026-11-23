using UnityEngine;
using UnityEngine.SceneManagement;
namespace Worthless
{
    public static class InputProbe
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static bool booted, enabled;
        private static KeyCode probeKey = KeyCode.Tab;
        private static float probeDelay = 8f, probeDuration = 1f;
        private static int tickFrame = -1;
        private static float gameplayAt = -1f, holdUntil = -1f;
        private static bool syntheticHeld, syntheticDown;
        private static void Boot(){ if(booted)return; booted=true; var a=System.Environment.GetCommandLineArgs(); enabled=System.Array.IndexOf(a,"-p101probe")>=0; var i=System.Array.IndexOf(a,"-p101probekey"); if(i>=0&&i+1<a.Length){KeyCode k;if(System.Enum.TryParse(a[i+1],out k))probeKey=k;} var di=System.Array.IndexOf(a,"-p101probeDelay"); if(di>=0&&di+1<a.Length)float.TryParse(a[di+1],out probeDelay); var du=System.Array.IndexOf(a,"-p101probeDuration"); if(du>=0&&du+1<a.Length)float.TryParse(a[du+1],out probeDuration); Debug.Log("[P101Probe] engine enabled="+enabled+" key="+probeKey+" delay="+probeDelay+" duration="+probeDuration); }
        private static void Tick()
        {
            if(!booted)Boot();
            if(tickFrame==Time.frameCount)return;
            tickFrame=Time.frameCount;
            var held=false;
            if(enabled && SceneManager.GetSceneByName("Gameplay").isLoaded)
            {
                if(gameplayAt<0f){gameplayAt=Time.unscaledTime; Debug.Log("[P101Probe] gameplay start t="+Time.unscaledTime.ToString("0.00"));}
                if(holdUntil<0f && Time.unscaledTime>=gameplayAt+probeDelay){holdUntil=Time.unscaledTime+probeDuration; Debug.Log("[P101Probe] Tab DOWN t="+Time.unscaledTime.ToString("0.00"));}
                held=holdUntil>0f && Time.unscaledTime<=holdUntil;
                if(!held && holdUntil>0f && syntheticHeld)Debug.Log("[P101Probe] Tab UP t="+Time.unscaledTime.ToString("0.00"));
            }
            syntheticDown=held && !syntheticHeld;
            syntheticHeld=held;
        }
        public static bool Enabled { get { Boot(); return enabled; } }
        public static bool Focused { get { Boot(); return enabled ? true : Application.isFocused; } }
        private static bool IsProbeKey(KeyCode key){ return key==probeKey; }
        public static bool GetKey(KeyCode key){ if(IsProbeKey(key)){Tick(); return enabled ? syntheticHeld : Input.GetKey(key);} return Input.GetKey(key); }
        public static bool GetKeyDown(KeyCode key){ if(IsProbeKey(key)){Tick(); return enabled ? syntheticDown : Input.GetKeyDown(key);} return Input.GetKeyDown(key); }
#else
        public static bool Enabled{get{return false;}}
        public static bool Focused{get{return Application.isFocused;}}
        public static bool GetKey(KeyCode k){return Input.GetKey(k);}
        public static bool GetKeyDown(KeyCode k){return Input.GetKeyDown(k);}
#endif
    }
}