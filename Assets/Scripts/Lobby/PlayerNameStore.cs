using System;
using System.IO;
using UnityEngine;
namespace Worthless
{
    public static class PlayerNameStore
    {
        public static string Load()
        {
            var path = Path();
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) return File.ReadAllText(path).Trim(); }
            catch { }
            try { return PlayerPrefs.GetString("player.name", string.Empty); } catch { return string.Empty; }
        }

        public static bool Save(string value)
        {
            var path = Path();
            try
            {
                if (!string.IsNullOrEmpty(path))
                {
                    var fullPath = System.IO.Path.GetFullPath(path);
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath));
                    File.WriteAllText(fullPath, value ?? string.Empty);
                }
                else
                {
                    PlayerPrefs.SetString("player.name", value ?? string.Empty);
                    PlayerPrefs.Save();
                }
                return true;
            }
            catch { return false; }
        }

        private static string Path()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-worthless-name-file");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : string.Empty;
        }
    }
}
