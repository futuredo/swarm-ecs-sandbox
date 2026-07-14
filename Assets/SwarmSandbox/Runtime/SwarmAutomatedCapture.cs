using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SwarmECS.Runtime
{
    /// <summary>
    /// Player-only portfolio capture hook. It is dormant unless -swarmCapturePath is supplied.
    /// </summary>
    public sealed class SwarmAutomatedCapture : MonoBehaviour
    {
        private const int CaptureTick = 45;
        private const float QuitDelaySeconds = 2f;

        private SwarmSimulationHost _host;
        private string _capturePath;
        private float _captureTime;
        private bool _captureRequested;
        private bool _captureCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallWhenRequested()
        {
            string path = ReadArgument("-swarmCapturePath");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            GameObject gameObject = new("Swarm Automated Capture");
            DontDestroyOnLoad(gameObject);
            SwarmAutomatedCapture capture = gameObject.AddComponent<SwarmAutomatedCapture>();
            capture._capturePath = Path.GetFullPath(path);
        }

        private void Update()
        {
            _host ??= FindFirstObjectByType<SwarmSimulationHost>();
            if (_host == null || _host.SimulationTick < CaptureTick)
            {
                return;
            }

            if (!_captureRequested)
            {
                _captureRequested = true;
                StartCoroutine(CaptureAtEndOfFrame());
                return;
            }

            if (_captureCompleted && Time.realtimeSinceStartup - _captureTime >= QuitDelaySeconds)
            {
                Debug.Log("[SwarmECS] Player capture completed.");
                Application.Quit(0);
            }
        }

        private IEnumerator CaptureAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();

            string directory = Path.GetDirectoryName(_capturePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Texture2D screenshot = new(Screen.width, Screen.height, TextureFormat.RGB24, false)
            {
                name = "Swarm Automated Capture",
            };
            screenshot.ReadPixels(new Rect(0f, 0f, Screen.width, Screen.height), 0, 0, false);
            screenshot.Apply(false, false);
            File.WriteAllBytes(_capturePath, screenshot.EncodeToPNG());
            Destroy(screenshot);

            _captureTime = Time.realtimeSinceStartup;
            _captureCompleted = true;
            Debug.Log($"[SwarmECS] Player capture written: {_capturePath}");
        }

        private static string ReadArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], name, StringComparison.Ordinal))
                {
                    return arguments[i + 1];
                }
            }

            return null;
        }
    }
}
