using OperationBlacktide.Client.Gameplay.Characters;
using UnityEngine;
using UnityEngine.EventSystems;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    [DefaultExecutionOrder(200)]
    public sealed class LocalWeaponInput : MonoBehaviour
    {
        private CharacterMotor character;
        private WeaponController weapon;
        private void Awake() { character = GetComponent<CharacterMotor>(); weapon = GetComponent<WeaponController>(); }
        private bool Allowed => character != null && character.InputEnabled && character.isActiveAndEnabled && weapon != null &&
            weapon.isActiveAndEnabled && Application.isFocused && Time.timeScale > 0 &&
            (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
        private void Update()
        {
            if (weapon == null || !weapon.isActiveAndEnabled) return;
            if (Allowed && Input.GetKeyDown(KeyCode.R)) weapon.TryReload();
            weapon.ProcessAim(Input.GetMouseButton(1), Allowed);
        }
        private void LateUpdate()
        {
            if (weapon == null || !weapon.isActiveAndEnabled) return;
            weapon.Throwables?.ProcessInput(Input.GetMouseButton(0), Input.GetMouseButtonDown(0), Input.GetMouseButtonDown(1), Allowed, Time.deltaTime);
            if (weapon.IsMelee) weapon.Melee.ProcessInput(Input.GetMouseButton(0), Input.GetMouseButton(1), Allowed);
            else weapon.ProcessTrigger(Input.GetMouseButton(0), Input.GetMouseButtonDown(0), Allowed, Time.timeAsDouble);
        }
    }
}
