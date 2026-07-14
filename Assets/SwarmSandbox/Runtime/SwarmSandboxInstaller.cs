using UnityEngine;

namespace SwarmECS.Runtime
{
    public static class SwarmSandboxInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (Object.FindFirstObjectByType<SwarmSimulationHost>() == null)
            {
                GameObject host = new("Swarm ECS Sandbox");
                host.AddComponent<SwarmSimulationHost>();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<SwarmCameraController>();
            }
            else if (camera.GetComponent<SwarmCameraController>() == null)
            {
                camera.gameObject.AddComponent<SwarmCameraController>();
            }

            camera.transform.SetPositionAndRotation(
                new Vector3(0f, 92f, -74f),
                Quaternion.Euler(53f, 0f, 0f));
            camera.fieldOfView = 58f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.006f, 0.01f, 0.018f, 1f);
            camera.allowHDR = true;
        }
    }
}
