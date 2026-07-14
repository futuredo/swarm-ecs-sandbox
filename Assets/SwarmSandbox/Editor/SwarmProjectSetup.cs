using System.IO;
using SwarmECS.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SwarmECS.Editor
{
    public static class SwarmProjectSetup
    {
        private const string ScenePath = "Assets/Scenes/SwarmSandbox.unity";

        [MenuItem("Swarm ECS/Create or Refresh Demo Scene")]
        public static void CreateOrRefreshProject()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject host = new("Swarm ECS Sandbox");
            host.AddComponent<SwarmSimulationHost>();

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<SwarmCameraController>();
            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 92f, -74f),
                Quaternion.Euler(53f, 0f, 0f));
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.006f, 0.01f, 0.018f, 1f);
            camera.allowHDR = true;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "Swarm ECS Lab";
            PlayerSettings.productName = "Swarm ECS Sandbox";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SwarmECS] Demo scene created: {ScenePath}");
        }

        [MenuItem("Swarm ECS/Open Demo Scene")]
        public static void OpenDemoScene()
        {
            if (!File.Exists(ScenePath))
            {
                CreateOrRefreshProject();
                return;
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        public static void BuildMacPlayerFromCommandLine()
        {
            if (!File.Exists(ScenePath))
            {
                CreateOrRefreshProject();
            }
            else if (!IsSceneEnabledInBuildSettings())
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectRoot, "Builds", "macOS", "SwarmECS.app");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            bool previousHybridClrEnabled = HybridCLR.Editor.Settings.HybridCLRSettings.Instance.enable;
            HybridCLR.Editor.Settings.HybridCLRSettings.Instance.enable = false;
            HybridCLR.Editor.Settings.HybridCLRSettings.Save();
#pragma warning disable CS0618
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
#pragma warning restore CS0618
            try
            {
                BuildPlayerOptions options = new()
                {
                    scenes = new[] { ScenePath },
                    locationPathName = outputPath,
                    target = BuildTarget.StandaloneOSX,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        $"Swarm ECS macOS build failed: {report.summary.result}, {report.summary.totalErrors} errors");
                }

                Debug.Log($"[SwarmECS] macOS player built: {outputPath} ({report.summary.totalSize} bytes)");
            }
            finally
            {
#pragma warning disable CS0618
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previousBackend);
#pragma warning restore CS0618
                HybridCLR.Editor.Settings.HybridCLRSettings.Instance.enable = previousHybridClrEnabled;
                HybridCLR.Editor.Settings.HybridCLRSettings.Save();
            }
        }

        private static bool IsSceneEnabledInBuildSettings()
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && scene.path == ScenePath)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
