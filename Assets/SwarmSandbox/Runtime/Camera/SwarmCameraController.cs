using UnityEngine;

namespace SwarmECS.Runtime
{

public sealed class SwarmCameraController : MonoBehaviour
{
    [SerializeField] private float panSpeed = 42f;
    [SerializeField] private float zoomSpeed = 90f;
    [SerializeField] private float minHeight = 18f;
    [SerializeField] private float maxHeight = 145f;

    private void Update()
    {
        float delta = Time.unscaledDeltaTime;
        Vector3 position = transform.position;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float heightScale = Mathf.Clamp(position.y / 70f, 0.4f, 2.2f);
        position += (right * horizontal + forward * vertical) * (panSpeed * heightScale * delta);

        float scroll = Input.mouseScrollDelta.y;
        position += transform.forward * (scroll * zoomSpeed * heightScale * delta);
        position.y = Mathf.Clamp(position.y, minHeight, maxHeight);
        position.x = Mathf.Clamp(position.x, -95f, 95f);
        position.z = Mathf.Clamp(position.z, -125f, 95f);
        transform.position = position;
    }
}
}
