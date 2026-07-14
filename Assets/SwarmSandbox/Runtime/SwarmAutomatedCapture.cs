using System;
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
        private bool _captured;

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

            if (!_captured)
            {
                string directory = Path.GetDirectoryName(_capturePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                ScreenCapture.CaptureScreenshot(_capturePath, 1);
                _captureTime = Time.realtimeSinceStartup;
                _captured = true;
                Debug.Log($"[SwarmECS] Player capture requested: {_capturePath}");
                return;
            }

            if (Time.realtimeSinceStartup - _captureTime >= QuitDelaySeconds && File.Exists(_capturePath))
            {
                Debug.Log("[SwarmECS] Player capture completed.");
                Application.Quit(0);
            }
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
