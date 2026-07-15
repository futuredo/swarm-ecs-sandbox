using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SwarmECS.Runtime
{
    /// <summary>
    /// Player-only automated capture hook. It is dormant unless -swarmCapturePath is supplied.
    /// </summary>
    public sealed class SwarmAutomatedCapture : MonoBehaviour
    {
        private const int ConfigureTick = 45;
        private const int DefaultCaptureTick = 60;
        private const int ObstacleInteractionCaptureTick = 240;
        private const float QuitDelaySeconds = 2f;

        private SwarmSimulationHost _host;
        private SwarmLabView _requestedView;
        private SwarmUiLanguage _requestedLanguage;
        private string _capturePath;
        private float _captureTime;
        private bool _viewConfigured;
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
            capture._requestedView = ReadLabViewArgument();
            capture._requestedLanguage = ReadLanguageArgument();
        }

        private void Update()
        {
            _host ??= FindFirstObjectByType<SwarmSimulationHost>();
            if (_host == null)
            {
                return;
            }

            if (!_viewConfigured && _host.SimulationTick >= ConfigureTick)
            {
                ConfigureRequestedView();
            }

            if (!_viewConfigured || _host.SimulationTick < CaptureTickForView(_requestedView))
            {
                return;
            }

            if (!_captureRequested)
            {
                _captureRequested = true;
                StartCoroutine(CaptureAtEndOfFrame());
                return;
            }

            if (_captureCompleted && File.Exists(_capturePath) &&
                Time.realtimeSinceStartup - _captureTime >= QuitDelaySeconds)
            {
                Debug.Log("[SwarmECS] Player capture completed.");
                Application.Quit(0);
            }
        }

        private void ConfigureRequestedView()
        {
            SwarmDebugHud hud = FindFirstObjectByType<SwarmDebugHud>();
            if (hud == null)
            {
                return;
            }

            hud.SetLanguage(_requestedLanguage, false);
            hud.SetActiveView(_requestedView);
            switch (_requestedView)
            {
                case SwarmLabView.Navigation:
                    _host.QueueBlockedNavigationProbe();
                    break;
                case SwarmLabView.Rollback:
                    _host.InjectLateCorrection();
                    break;
            }

            _viewConfigured = true;
            Debug.Log($"[SwarmECS] Capture view configured: {_requestedView}, language: {_requestedLanguage}");
        }

        private IEnumerator CaptureAtEndOfFrame()
        {
            // Metal can defer the final IMGUI/GL submission beyond the frame in
            // which the view is switched. Give the selected lab several complete
            // presentation frames, then use Unity's screenshot path so the GPU
            // back buffer is synchronized before encoding.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            string directory = Path.GetDirectoryName(_capturePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(_capturePath))
            {
                File.Delete(_capturePath);
            }

            ScreenCapture.CaptureScreenshot(_capturePath, 1);

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

        private static SwarmLabView ReadLabViewArgument()
        {
            string value = ReadArgument("-swarmCaptureView");
            return Enum.TryParse(value, true, out SwarmLabView view) &&
                (uint)view <= (uint)SwarmLabView.Network
                ? view
                : SwarmLabView.Overview;
        }

        private static SwarmUiLanguage ReadLanguageArgument()
        {
            string value = ReadArgument("-swarmCaptureLanguage");
            return Enum.TryParse(value, true, out SwarmUiLanguage language) &&
                (language == SwarmUiLanguage.English || language == SwarmUiLanguage.SimplifiedChinese)
                ? language
                : SwarmUiLanguage.English;
        }

        private static int CaptureTickForView(SwarmLabView view)
        {
            return view == SwarmLabView.Avoidance || view == SwarmLabView.Collision
                ? ObstacleInteractionCaptureTick
                : DefaultCaptureTick;
        }
    }
}
