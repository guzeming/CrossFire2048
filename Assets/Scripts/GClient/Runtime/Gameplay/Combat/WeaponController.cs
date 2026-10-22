using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Shared.Combat;
using System;
using OperationBlacktide.Shared.Characters;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingShot")]
    public readonly struct Shot
    {
        public readonly Vector3 origin, end;
        public readonly RaycastHit hit;
        public readonly bool hasHit;
        public readonly float damage;
        public readonly ImpactKind impactKind;
        public Shot(Vector3 origin, Vector3 end, RaycastHit hit, bool hasHit, float damage)
        {
            this.origin = origin; this.end = end; this.hit = hit; this.hasHit = hasHit; this.damage = damage;
            // Capture armor and hit region before ApplyDamage can kill or disable the target.
            impactKind = hasHit ? ImpactSurfaceResolver.Resolve(hit) : ImpactKind.Default;
        }
    }

    /// <summary>Local hitscan: one ShotFired per cartridge, TargetHit for each damaging pellet.</summary>
    // Read the final muzzle after action sampling (180) and upper-body aim (185).
    [DefaultExecutionOrder(200)]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingWeaponController")]
    public sealed class WeaponController : MonoBehaviour
    {
        private CharacterMotor character;
        private Transform weapon;
        private Transform offhandWeapon;
        private int dualShots;
        private WeaponDefinition definition;
        private Weapon runtimeWeapon;
        private EquipmentComponent equipment = new EquipmentComponent();
        private bool ownsEquipment = true;
        public Weapon RuntimeWeapon => runtimeWeapon;
        public bool ExternalTick { get; set; }
        public void BindEquipment(EquipmentComponent value)
        { if (ownsEquipment) equipment.Dispose(); equipment = value; ownsEquipment = false; ExternalTick = true; }
        private WeaponEffects effects;
        private WeaponPresentation presentation;
        private AimLaser aimLaser;
        private UpperBodyAim upperBodyAim;
        private bool scopeNeedsRelease = true;
        private WeaponModelDefinition equipped;
        public event Action<Shot> ShotFired;
        public event Action<bool> AttackStarted;
        public event Action WeaponChanged;
        public event Action<Damageable, float, Vector3> TargetHit;
        public bool IsSniper => definition != null && definition.IsSniper;
        public bool IsMelee => definition != null && definition.IsMelee;
        public bool IsTaser => definition != null && definition.IsTaser;
        public bool LastShotOffhand { get; private set; }
        public bool IsScoped { get; private set; }
        public bool IsAiming { get; private set; }
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public Shot LastShot { get; private set; }
        public int ShotsFired { get; private set; }
        public int LastPelletCount { get; private set; }
        public string WeaponId => equipped?.id;
        public Sprite HudIcon => definition != null && definition.hudIcon != null ? definition.hudIcon : equipped?.thumbnail;
        public bool CanFire => definition != null && !definition.IsMelee;
        // Set by the mode before initialization; ordinary combat has finite supplies.
        public bool InfiniteAmmoReserve { get; set; }
        public bool InfiniteThrowables { get; set; }
        public bool AutoRechargeTaser { get; set; }
        public ThrowableController Throwables { get; private set; }
        public MeleeController Melee { get; private set; }
        public bool CanAttack => CanFire || IsMelee || (Throwables != null && Throwables.CanThrow);
        public int Slot => WeaponSelection.SlotOf(equipped);
        public AmmoState Ammo { get; private set; }
        public Vector3 MuzzlePosition => weapon.TransformPoint(definition.muzzlePosition);

        public void Initialize(CharacterMotor owner, Transform equippedWeapon, string id)
        {
            PrepareEquipmentChange();
            var catalog = Resources.Load<WeaponCatalog>("Gameplay/Weapons");
            definition = catalog != null ? catalog.Find(id) : null;
            equipped = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog")?.Weapon(id);
            character = owner;
            if (owner.ViewCamera != null && GetComponent<LocalWeaponInput>() == null) gameObject.AddComponent<LocalWeaponInput>();
            weapon = equippedWeapon;
            offhandWeapon = null;
            dualShots = 0;
            if (definition != null && definition.hasCombatGrip && weapon != null)
            {
                weapon.localPosition = definition.combatGripPosition;
                weapon.localRotation = definition.combatGripRotation;
            }
            if (definition != null && definition.IsDual)
            {
                foreach (var child in owner.GetComponentsInChildren<Transform>())
                    if (child.name == "Equipped_Offhand_" + id) { offhandWeapon = child; break; }
                // The lobby has relaxed, downward dual-pistol attachments. Combat uses the
                // source world-animation grips, without changing the shared preview prefabs.
                weapon.localPosition = definition.combatGripPosition; weapon.localRotation = definition.combatGripRotation;
                if (offhandWeapon != null)
                { offhandWeapon.localPosition = definition.offhandGripPosition; offhandWeapon.localRotation = definition.offhandGripRotation; }
            }
            if (Throwables == null) Throwables = gameObject.AddComponent<ThrowableController>();
            Throwables.InfiniteSupply = InfiniteThrowables;
            Throwables.Bind(owner, equippedWeapon, id);
            if (Melee == null) Melee = gameObject.AddComponent<MeleeController>();
            Melee.Bind(owner, this, definition);
            Ammo = null;
            runtimeWeapon = null;
            if (equipped == null || equippedWeapon == null)
            {
                Debug.LogError("[Gameplay] Missing firing assets for " + id + ". Run Training/Build Weapon Effects.");
                enabled = false;
                return;
            }
            if (upperBodyAim != null) upperBodyAim.enabled = CanFire;
            if (aimLaser != null) aimLaser.enabled = definition != null;
            if (definition == null)
            {
                // Equipment models without a combat profile still switch normally. Never retain the previous gun's ammo/effects.
                enabled = true;
                NotifyWeaponChanged();
                return;
            }
            if (IsMelee)
            {
                if (presentation == null) presentation = gameObject.AddComponent<WeaponPresentation>();
                presentation.Initialize(owner, this, definition);
                if (aimLaser != null) aimLaser.enabled = false;
                enabled = true; NotifyWeaponChanged(); return;
            }
            if (equipment.Find(id) == null) equipment.AddWeapon(new Weapon(id, definition, InfiniteAmmoReserve));
            runtimeWeapon = equipment.Equip(id);
            runtimeWeapon.Fire.Reset();
            Ammo = runtimeWeapon.Ammo;
            if (effects == null)
            {
                effects = gameObject.AddComponent<WeaponEffects>();
                effects.Initialize(catalog, definition, weapon);
            }
            else effects.Rebind(definition, weapon);
            if (presentation == null) presentation = gameObject.AddComponent<WeaponPresentation>();
            presentation.Initialize(owner, this, definition);
            if (upperBodyAim == null) upperBodyAim = gameObject.AddComponent<UpperBodyAim>();
            upperBodyAim.Initialize(owner, this, weapon);
            if (aimLaser == null) aimLaser = gameObject.AddComponent<AimLaser>();
            aimLaser.Initialize(owner, this, weapon, definition, catalog.tracerMaterial);
            enabled = true;
            NotifyWeaponChanged();
        }

        private void NotifyWeaponChanged()
        {
            WeaponDrawAudio.Equip(gameObject, WeaponId, character.ViewCamera != null);
            WeaponChanged?.Invoke();
        }

        public void PrepareEquipmentChange()
        {
            Melee?.Cancel();
            Throwables?.ReleaseEquipment();
            CancelAim();
            Ammo?.CancelReload();
            equipment.Unequip();
            upperBodyAim?.RestorePose();
            presentation?.Release();
            aimLaser?.Hide();
        }

        public void ResetEquipment()
        {
            GetComponent<WeaponDrawAudio>()?.Stop();
            effects?.StopImpactAudio();
            Melee?.Cancel();
            presentation?.ResetPresentation();
            Throwables?.ResetEquipment();
            CancelAim();
            equipment.Reset();
        }

        public void CancelAim() { IsAiming = IsScoped = false; scopeNeedsRelease = true; }

        public void ProcessAim(bool held, bool allowed)
        {
            allowed &= character != null && character.InputEnabled && isActiveAndEnabled && Ammo != null && !Ammo.IsReloading;
            if (!allowed) { CancelAim(); return; }
            if (!held) scopeNeedsRelease = false;
            IsAiming = held && !scopeNeedsRelease;
            IsScoped = IsAiming && IsSniper;
        }

        private void Update()
        {
            bool active = character != null && character.InputEnabled && Application.isFocused && Time.timeScale > 0;
            if (!ExternalTick) AdvanceReload(active ? Time.deltaTime : 0);
            else if (AutoRechargeTaser && IsTaser && Ammo != null && Ammo.Magazine == 0 && !Ammo.IsReloading) TryReload();
            Melee?.Advance(active ? Time.deltaTime : 0);
        }

        public bool TryReload()
        {
            if (character == null || !character.InputEnabled || !isActiveAndEnabled || Ammo == null) return false;
            bool started = Ammo.TryReload();
            if (started) CancelAim();
            return started;
        }

        public void AdvanceReload(float deltaTime)
        {
            if (character != null && character.InputEnabled && isActiveAndEnabled) Ammo?.Tick(deltaTime);
            if (AutoRechargeTaser && IsTaser && Ammo != null && Ammo.Magazine == 0 && !Ammo.IsReloading) TryReload();
        }

        public void ProcessTrigger(bool held, bool pressed, bool allowed, double now)
        {
            if (runtimeWeapon == null) return;
            int count = runtimeWeapon.ProcessTrigger(held, pressed, allowed && character.InputEnabled && isActiveAndEnabled, now);
            for (int i = 0; i < count; i++) FireBullet();
        }

        private void FireBullet()
        {
            // Also prepare the pose for deterministic/external ProcessTrigger calls between frames.
            upperBodyAim?.ApplyAim();
            LastShotOffhand = definition.IsDual && offhandWeapon != null && dualShots++ % 2 == 1;
            Transform firingWeapon = LastShotOffhand ? offhandWeapon : weapon;
            Vector3 muzzle = firingWeapon.TransformPoint(LastShotOffhand ? definition.offhandMuzzlePosition : definition.muzzlePosition);
            // The crosshair selects a world point; the muzzle determines the actual path to it.
            Vector3 offset = character.AimPoint - muzzle;
            Vector3 direction = offset.sqrMagnitude > .000001f ? offset.normalized : character.transform.forward;
            // Trace from inside the player's capsule to the barrel first, so a barrel poking through a wall cannot shoot through it.
            Vector3 breech = character.transform.position + Vector3.up * (muzzle.y - character.transform.position.y);
            Vector3 barrel = muzzle - breech;
            bool barrelBlocked = Physics.Raycast(breech, barrel.normalized, out RaycastHit barrelHit, barrel.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Vector3 origin = barrelBlocked ? breech : muzzle;
            LastPelletCount = Mathf.Max(1, definition.pelletCount);
            var spreadRotation = Quaternion.LookRotation(direction);
            float phase = LastPelletCount > 1 ? UnityEngine.Random.Range(0, Mathf.PI * 2) : 0;
            effects.Rebind(definition, firingWeapon);
            for (int i = 0; i < LastPelletCount; i++)
            {
                // A central pellet keeps the crosshair meaningful; the rest fill a rotated disk.
                float radius = Mathf.Tan(definition.spreadAngle * Mathf.Deg2Rad) * Mathf.Sqrt((float)i / LastPelletCount);
                float angle = phase + i * 2.39996323f;
                Vector3 pelletDirection = i == 0 ? direction : spreadRotation * new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 1).normalized;
                RaycastHit hit = barrelHit;
                bool hitSomething = barrelBlocked || Physics.Raycast(muzzle, pelletDirection, out hit, definition.range,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Vector3 end = hitSomething ? hit.point : muzzle + pelletDirection * definition.range;
                float damage = definition.damage * Mathf.Pow(definition.rangeModifier, Vector3.Distance(origin, end) / 12.7f);
                var shot = new Shot(origin, end, hit, hitSomething, damage);
                if (i == 0) LastShot = shot;
                var target = hitSomething ? hit.collider.GetComponentInParent<Damageable>() : null;
                if (target != null && target.IsAlive)
                {
                    float dealt = target.ApplyDamage(damage);
                    TargetHit?.Invoke(target, dealt, end);
                }
                // Only the first pellet produces the muzzle flash, casing and firing sound.
                effects.Play(shot, i == 0);
            }
            ShotsFired++;
            LastShotTime = Time.time;
            NotifyAttack(LastShotOffhand);
            ShotFired?.Invoke(LastShot);
        }

        private void OnDisable()
        {
            GetComponent<WeaponDrawAudio>()?.Stop();
            effects?.StopImpactAudio();
            Melee?.Cancel();
            Throwables?.Cancel();
            CancelAim();
            runtimeWeapon?.Fire.Tick(true, false, false, Time.timeAsDouble);
            presentation?.ResetPresentation();
            upperBodyAim?.RestorePose();
            aimLaser?.Hide();
        }
        private void OnDestroy() { if (ownsEquipment) equipment.Dispose(); }
        public void NotifyThrowableHit(Damageable target, float damage, Vector3 position) => TargetHit?.Invoke(target, damage, position);
        public void NotifyMeleeHit(Damageable target, float damage, Vector3 position) => TargetHit?.Invoke(target, damage, position);
        public void NotifyAttack(bool alternate) { LastShotTime = Time.time; AttackStarted?.Invoke(alternate); }
    }
}
