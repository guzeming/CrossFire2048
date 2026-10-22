using OperationBlacktide.Client.Gameplay.Characters;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>Server snapshots/events feed shared combat visuals without local ammo or damage simulation.</summary>
    public sealed class NetworkWeaponPresentation : MonoBehaviour
    {
        WeaponDefinition definition;
        Transform gun;
        CharacterMotor character;
        WeaponPresentation actions;
        WeaponEffects effects;
        UpperBodyAim aim;
        AimLaser laser;
        bool reloading;
        bool objectiveBusy, resumeActions, resumeAim, resumeLaser;
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public bool Automatic => definition != null && definition.automatic;
        public bool IsSniper => definition != null && definition.IsSniper;
        public Vector3 Muzzle => gun.TransformPoint(definition.muzzlePosition);

        public void PrepareChange()
        {
            SetObjectiveBusy(false);
            if (aim != null) { aim.RestorePose(); aim.enabled = false; }
            if (laser != null) { laser.Hide(); laser.enabled = false; }
            if (actions != null) actions.Release();
        }

        public void Bind(CharacterMotor owner, Transform equipped, string weaponId, bool local)
        {
            character = owner; gun = equipped;
            var assets = Resources.Load<WeaponCatalog>("Gameplay/Weapons");
            definition = assets != null ? assets.Find(weaponId) : null;
            if (definition == null || gun == null) { enabled = false; return; }
            if (definition.hasCombatGrip)
            { gun.localPosition = definition.combatGripPosition; gun.localRotation = definition.combatGripRotation; }
            if (effects == null)
            { effects = gameObject.AddComponent<WeaponEffects>(); effects.Initialize(assets, definition, gun); }
            else effects.Rebind(definition, gun);
            // Remote guns are heard from their position, not at full local-player volume.
            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null) { audioSource.spatialBlend = local ? 0 : 1; audioSource.minDistance = 4; audioSource.maxDistance = 80; }
            if (actions == null) actions = gameObject.AddComponent<WeaponPresentation>();
            actions.InitializeNetwork(owner, definition);
            foreach (var audio in actions.GetComponentsInChildren<AudioSource>())
                if (audio.gameObject.name == "Reload Audio") { audio.spatialBlend = local ? 0 : 1; audio.minDistance = 4; audio.maxDistance = 30; }
            if (local)
            {
                if (aim == null) aim = gameObject.AddComponent<UpperBodyAim>();
                aim.enabled = true; aim.InitializeNetwork(owner, gun, () => Muzzle, () => reloading);
                if (laser == null) laser = gameObject.AddComponent<AimLaser>();
                laser.enabled = true; laser.InitializeNetwork(owner, gun, definition, assets.tracerMaterial, () => Muzzle);
            }
            enabled = true; ResetPresentation();
            WeaponDrawAudio.Equip(gameObject, weaponId, local);
        }

        public void PresentReload(int ticks)
        { reloading = ticks > 0; if (actions != null) actions.SetNetworkReload(ticks / 60f); }

        public void ConfirmShot(Vector3 origin, Vector3 end)
        {
            if (objectiveBusy || definition == null || effects == null || gun == null) return;
            LastShotTime = Time.time; actions.ConfirmShot();
            // This ray only recovers the impact surface at the authoritative endpoint for VFX.
            // It never decides damage, hit markers, ammunition, or the shot destination.
            var direction = end - origin;
            RaycastHit impact = default; bool hasImpact = false;
            foreach (var hit in Physics.RaycastAll(origin, direction.normalized, direction.magnitude + .05f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(transform) || (hit.point - end).sqrMagnitude > .04f) continue;
                impact = hit; hasImpact = true; break;
            }
            effects.Play(new Shot(Muzzle, end, impact, hasImpact, 0));
        }

        public void ResetPresentation()
        { LastShotTime = float.NegativeInfinity; reloading = false; if (actions != null) { actions.ResetPresentation(); actions.SetNetworkReload(0); } }

        // Objective actions own the hands and full-body pose until the server cancels/completes them.
        public void SetObjectiveBusy(bool busy)
        {
            if (objectiveBusy == busy) return;
            objectiveBusy = busy;
            if (busy)
            {
                resumeActions = actions != null && actions.enabled;
                resumeAim = aim != null && aim.enabled;
                resumeLaser = laser != null && laser.enabled;
                ResetPresentation();
                if (actions != null) actions.enabled = false;
                if (aim != null) { aim.RestorePose(); aim.enabled = false; }
                if (laser != null) { laser.Hide(); laser.enabled = false; }
            }
            else
            {
                if (actions != null) actions.enabled = resumeActions;
                if (aim != null) aim.enabled = resumeAim;
                if (laser != null) laser.enabled = resumeLaser;
            }
            if (gun != null) gun.gameObject.SetActive(!busy);
        }
    }
}
