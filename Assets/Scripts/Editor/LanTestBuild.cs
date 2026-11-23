using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Worthless.Editor
{
    public static class LanTestBuild
    {
        [MenuItem("Worthless/LAN Test/Build")]
        public static void Build()
        {
            BuildPlayer();
        }

        [MenuItem("Worthless/LAN Test/Build and Run")]
        public static void BuildAndRun()
        {
            LaunchPlayer(BuildPlayer());
        }

        [MenuItem("Worthless/LAN Test/Build Release")]
        public static void BuildRelease()
        {
            BuildPlayer(false);
        }

        [MenuItem("Worthless/LAN Test/Build Release and Run")]
        public static void BuildReleaseAndRun()
        {
            LaunchPlayer(BuildPlayer(false));
        }

        private static void LaunchPlayer(string executable)
        {
            var launch = new ProcessStartInfo(executable)
            {
                WorkingDirectory = Path.GetDirectoryName(executable),
                UseShellExecute = false
            };
            using Process player = Process.Start(launch);
            Debug.Log($"LAN test player started (PID {player.Id}): {executable}");
        }

        private static string BuildPlayer(bool development = true)
        {
            string executable = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds/LanTest/Worthless.exe"));
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
            {
                throw new BuildFailedException("Enable at least one scene in Build Settings before building the LAN test player.");
            }

            EnsurePlayerIsClosed(executable);
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows
                ? BuildTarget.StandaloneWindows
                : BuildTarget.StandaloneWindows64;
            var options = development ? BuildOptions.Development : BuildOptions.None;
            BuildReport report = BuildPipeline.BuildPlayer(scenes, executable, target, options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"LAN test build {report.summary.result}: {executable}. See the Unity Console for details.");
            }

            Debug.Log($"LAN {(development ? "development" : "release")} build succeeded: {executable}");
            return executable;
        }

        private static void EnsurePlayerIsClosed(string executable)
        {
            foreach (Process player in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (player)
                {
                    string runningPath;
                    try
                    {
                        runningPath = player.MainModule?.FileName;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                    catch (Win32Exception)
                    {
                        continue;
                    }

                    if (string.Equals(runningPath, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new BuildFailedException($"Close the running LAN test player before rebuilding: {executable}");
                    }
                }
            }
        }
    }
}
