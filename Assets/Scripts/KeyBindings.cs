using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
namespace Worthless
{
    public enum GameInputAction { MoveUp=0, MoveDown=1, MoveLeft=2, MoveRight=3, Interact=4, Nameplates=5, Run=6, ContextInteract=7 }

    [Serializable] public sealed class KeyBindingEntry { public int action; public int key; }
    [Serializable] public sealed class KeyBindingStore { public List<KeyBindingEntry> entries = new List<KeyBindingEntry>(); }

    public static class KeyBindings
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool MoveFileExW(string existing, string replacement, uint flags);
#endif
        private static Dictionary<GameInputAction, KeyCode> overrides = new Dictionary<GameInputAction, KeyCode>();
        private static bool loaded;
        private static string storePath;
        private static KeyBindingDefaults d;
        public static bool Capturing { get; set; }
        public static KeyBindingDefaults Defaults { get { if(d==null)d=Resources.Load<KeyBindingDefaults>("KeyBindingDefaultsRuntime"); if(d==null)d=ScriptableObject.CreateInstance<KeyBindingDefaults>(); return d; } }

        public static string StorePath { get { EnsureLoaded(); return storePath; } }
        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-p101bindfile");
            storePath = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Application.persistentDataPath, "keybindings.json");
#else
            storePath = Path.Combine(Application.persistentDataPath, "keybindings.json");
#endif
            Load();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[P101Bind] store=" + storePath + " nameplates=" + Get(GameInputAction.Nameplates) + " interact=" + Get(GameInputAction.Interact) + " right=" + Get(GameInputAction.MoveRight));
#endif
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(storePath)) return;
                var data = JsonUtility.FromJson<KeyBindingStore>(File.ReadAllText(storePath));
                if (data == null || data.entries == null) return;
                foreach (var e in data.entries) overrides[(GameInputAction)e.action] = (KeyCode)e.key;
                MigrateContextBinding();
            }
            catch (Exception ex) { Debug.LogWarning("[P101Bind] load failed: " + ex.Message); }
        }

        private static void MigrateContextBinding()
        {
            if (overrides.ContainsKey(GameInputAction.ContextInteract)) return;
            var used = new HashSet<KeyCode>();
            foreach (GameInputAction action in Enum.GetValues(typeof(GameInputAction)))
            {
                if (action != GameInputAction.ContextInteract) used.Add(Get(action));
            }
            if (!used.Contains(DefaultFor(GameInputAction.ContextInteract))) return;

            // Older saves could assign F to movement or nameplates. Keep those
            // bindings and reserve an unused key for the newly added action.
            var alternatives = new[] { KeyCode.G, KeyCode.R, KeyCode.T, KeyCode.F6,
                KeyCode.F7, KeyCode.F8, KeyCode.F9, KeyCode.F10 };
            foreach (var key in alternatives)
            {
                if (used.Contains(key)) continue;
                overrides[GameInputAction.ContextInteract] = key;
                string error;
                Save(out error);
                Debug.Log("[KeyBindings] New room/review action assigned " + key + " to preserve saved bindings.");
                return;
            }
        }

        private static bool Save(out string error)
        {
            error = null;
            string temp = null;
            try
            {
                var dir = Path.GetDirectoryName(storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var data = new KeyBindingStore();
                foreach (var pair in overrides) data.entries.Add(new KeyBindingEntry { action = (int)pair.Key, key = (int)pair.Value });
                temp = storePath + ".tmp." + System.Diagnostics.Process.GetCurrentProcess().Id + "." + DateTime.UtcNow.Ticks;
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(storePath))
                {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                    if (!MoveFileExW(temp, storePath, 1u | 8u)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
#else
                    File.Replace(temp, storePath, null);
#endif
                }
                else File.Move(temp, storePath);
                return true;
            }
            catch (Exception ex)
            {
                try { if (!string.IsNullOrEmpty(temp) && File.Exists(temp)) File.Delete(temp); } catch { }
                error = "保存失败：" + ex.Message;
                Debug.LogError("[P101Bind] " + error);
                return false;
            }
        }

        private static bool TryDelete(out string error)
        {
            error = null;
            try { if (File.Exists(storePath)) File.Delete(storePath); return true; }
            catch (Exception ex) { error = "恢复默认失败：" + ex.Message; Debug.LogWarning("[P101Bind] " + error); return false; }
        }

        public static string ActionName(GameInputAction action)
        {
            switch (action)
            {
                case GameInputAction.MoveUp: return "向上移动";
                case GameInputAction.MoveDown: return "向下移动";
                case GameInputAction.MoveLeft: return "向左移动";
                case GameInputAction.MoveRight: return "向右移动";
                case GameInputAction.Interact: return "交易交互";
                case GameInputAction.Nameplates: return "名牌显示开关";
                case GameInputAction.Run: return "奔跑（按住）";
                case GameInputAction.ContextInteract: return "反锁 / 大屏回看";
                default: return action.ToString();
            }
        }

        public static KeyCode DefaultFor(GameInputAction action)
        {
            var defaults = Defaults;
            switch (action)
            {
                case GameInputAction.MoveUp: return defaults.MoveUp;
                case GameInputAction.MoveDown: return defaults.MoveDown;
                case GameInputAction.MoveLeft: return defaults.MoveLeft;
                case GameInputAction.MoveRight: return defaults.MoveRight;
                case GameInputAction.Interact: return defaults.Interact;
                case GameInputAction.Nameplates: return defaults.Nameplates;
                case GameInputAction.Run: return defaults.Run;
                case GameInputAction.ContextInteract: return defaults.ContextInteract;
                default: return KeyCode.None;
            }
        }
        public static KeyCode Get(GameInputAction a){EnsureLoaded();KeyCode k;return overrides.TryGetValue(a,out k)?k:DefaultFor(a);}

        private static GameInputAction? ArrowCompat(KeyCode k){switch(k){case KeyCode.UpArrow:return GameInputAction.MoveUp;case KeyCode.DownArrow:return GameInputAction.MoveDown;case KeyCode.LeftArrow:return GameInputAction.MoveLeft;case KeyCode.RightArrow:return GameInputAction.MoveRight;}return null;}
        public static string TryBind(GameInputAction a, KeyCode k)
        {
            EnsureLoaded();
            if (k == KeyCode.None || k == KeyCode.Escape) return "无效按键";
            var arrow = ArrowCompat(k);
            if (arrow.HasValue && arrow.Value != a) return "已被「" + ActionName(arrow.Value) + "」占用";
            foreach (GameInputAction o in Enum.GetValues(typeof(GameInputAction))) if (o != a && Get(o) == k) return "已被「" + ActionName(o) + "」占用";
            var old = overrides.ContainsKey(a) ? overrides[a] : (KeyCode?)null;
            overrides[a] = k;
            string error;
            if (Save(out error)) return null;
            if (old.HasValue) overrides[a] = old.Value; else overrides.Remove(a);
            return error;
        }

        public static string ResetAll()
        {
            EnsureLoaded();
            var backup = new Dictionary<GameInputAction, KeyCode>(overrides);
            overrides.Clear();
            string error;
            if (TryDelete(out error)) { Debug.Log("[P101Bind] reset defaults"); return null; }
            overrides = backup;
            return error;
        }
        public static void ResetCache(){d=null;}
    }
}
