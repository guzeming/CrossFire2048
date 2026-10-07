using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Features.Lobby
{
    /// <summary>场景中的角色展示控制，拖动中央区域旋转模型，避开 UI 点击。</summary>
    public sealed class LobbyCharacterPreview : MonoBehaviour
    {
        [SerializeField] private Transform character;
        [SerializeField] private float degreesPerScreenWidth = 240f;
        private bool dragging;
        private float lastPointerX;

        private void Update()
        {
            if (character == null) return;
            if (Input.GetMouseButtonDown(0))
            {
                Vector3 pointer = Input.mousePosition;
                dragging = pointer.x > Screen.width * 0.28f && pointer.x < Screen.width * 0.7f &&
                    pointer.y > Screen.height * 0.15f && pointer.y < Screen.height * 0.85f &&
                    (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
                lastPointerX = pointer.x;
            }
            if (!Input.GetMouseButton(0)) dragging = false;
            if (!dragging) return;

            float pointerX = Input.mousePosition.x;
            character.Rotate(Vector3.up, -(pointerX - lastPointerX) / Screen.width * degreesPerScreenWidth, Space.World);
            lastPointerX = pointerX;
        }

        private void OnDisable() => dragging = false;
    }
}
