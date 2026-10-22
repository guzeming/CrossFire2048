using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Gameplay.Characters
{
    /// <summary>Keyboard/mouse adapter. A motor can instead be driven by network or AI commands.</summary>
    [DefaultExecutionOrder(60)]
    public sealed class LocalPlayerInput : MonoBehaviour
    {
        private CharacterMotor motor;
        private void Awake() { motor = GetComponent<CharacterMotor>(); }
        private void Update()
        {
            if (motor == null || !motor.isActiveAndEnabled || motor.ViewCamera == null) return;
            bool active = motor.InputEnabled && Application.isFocused && Time.timeScale > 0;
            motor.UpdateAimInput(new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")),
                active && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()), Time.deltaTime);
            var axis = active ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")) : Vector2.zero;
            motor.Move(axis, active && Input.GetKey(KeyCode.LeftShift), active && Input.GetKeyDown(KeyCode.Space), Time.deltaTime);
        }
    }
}
