using System.IO;
using SwarmECS.Runtime;
using UnityEditor;
using UnityEngine;

namespace SwarmECS.Editor
{
    [InitializeOnLoad]
    public static class SwarmCaptureAutomation
    {
        private const string PendingKey = "SwarmECS.Capture.Pending";
        private const string StageKey = "SwarmECS.Capture.Stage";
        private const string StartedKey = "SwarmECS.Capture.Started";

        static SwarmCaptureAutomation()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
        }

        public static void CaptureFromCommandLine()
        {
            SessionState.SetBool(PendingKey, true);
            SessionState.SetInt(StageKey, 0);
            SessionState.SetFloat(StartedKey, 0f);
            SwarmProjectSetup.OpenDemoScene();
        }

        private static void Update()
        {
            if (!SessionState.GetBool(PendingKey, false))
            {
                return;
            }

            int stage = SessionState.GetInt(StageKey, 0);
            if (stage == 0 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetInt(StageKey, 1);
                EditorApplication.EnterPlaymode();
                return;
            }

            if (stage == 1 && EditorApplication.isPlaying)
            {
                SwarmSimulationHost host = Object.FindFirstObjectByType<SwarmSimulationHost>();
                if (host == null || host.SimulationTick < 45)
                {
                    return;
                }

                string path = ScreenshotPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ScreenCapture.CaptureScreenshot(path, 1);
                SessionState.SetFloat(StartedKey, (float)EditorApplication.timeSinceStartup);
                SessionState.SetInt(StageKey, 2);
                Debug.Log($"[SwarmECS] Capturing Game view to {path}");
                return;
            }

            if (stage == 2 && EditorApplication.isPlaying)
            {
                float elapsed = (float)EditorApplication.timeSinceStartup - SessionState.GetFloat(StartedKey, 0f);
                if (elapsed < 2f || !File.Exists(ScreenshotPath()))
                {
                    return;
                }

                SessionState.SetInt(StageKey, 3);
                EditorApplication.ExitPlaymode();
                return;
            }

            if (stage == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseBool(PendingKey);
                SessionState.EraseInt(StageKey);
                SessionState.EraseFloat(StartedKey);
                AssetDatabase.Refresh();
                Debug.Log("[SwarmECS] Screenshot capture completed.");
                EditorApplication.Exit(0);
            }
        }

        private static string ScreenshotPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Docs", "Images", "swarm-sandbox.png");
        }
    }
}
