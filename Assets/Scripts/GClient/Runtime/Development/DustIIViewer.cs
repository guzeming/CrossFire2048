using UnityEngine;

namespace OperationBlacktide.Maps
{
    /// <summary>Free camera for inspecting the imported map scene.</summary>
    public sealed class DustIIViewer : MonoBehaviour
    {
        public float moveSpeed = 12f;
        private Vector3 initialPosition;
        private Quaternion initialRotation;

        private void Awake()
        {
            initialPosition = transform.position;
            initialRotation = transform.rotation;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Home))
                transform.SetPositionAndRotation(initialPosition, initialRotation);
            if (!Input.GetMouseButton(1)) return;
            float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement.z++;
            if (Input.GetKey(KeyCode.S)) movement.z--;
            if (Input.GetKey(KeyCode.D)) movement.x++;
            if (Input.GetKey(KeyCode.A)) movement.x--;
            if (Input.GetKey(KeyCode.E)) movement.y++;
            if (Input.GetKey(KeyCode.Q)) movement.y--;
            transform.position += (transform.right * movement.x + Vector3.up * movement.y + transform.forward * movement.z)
                                  * (speed * Time.unscaledDeltaTime);
            var angles = transform.eulerAngles;
            float pitch = angles.x > 180 ? angles.x - 360 : angles.x;
            transform.rotation = Quaternion.Euler(Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * 2, -89, 89),
                                                  angles.y + Input.GetAxis("Mouse X") * 2, 0);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(12, 12, 520, 48), "Dust II | Hold RMB: look + WASD move | Q/E: down/up\nShift: faster | Home: overview");
        }
    }
}
