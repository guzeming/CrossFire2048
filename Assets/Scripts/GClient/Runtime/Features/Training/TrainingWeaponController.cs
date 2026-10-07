using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using OperationBlacktide.Client.Features.Lobby;

namespace OperationBlacktide.Client.Features.Training
{
    public readonly struct TrainingShot
    {
        public readonly Vector3 origin, end;
        public readonly RaycastHit hit;
        public readonly bool hasHit;
        public readonly float damage;
        public TrainingShot(Vector3 origin, Vector3 end, RaycastHit hit, bool hasHit, float damage)
        { this.origin = origin; this.end = end; this.hit = hit; this.hasHit = hasHit; this.damage = damage; }
    }

    /// <summary>Local hitscan: one ShotFired per cartridge, TargetHit for each damaging pellet.</summary>
    // Read the final muzzle after action sampling (180) and upper-body aim (185).
    [DefaultExecutionOrder(200)]
    public sealed class TrainingWeaponController : MonoBehaviour
    {
        private TrainingCharacterController character;
        private Transform weapon;
        private Transform offhandWeapon;
        private int dualShots;
        private TrainingWeaponDefinition definition;
        private TrainingFireControl trigger;
        private TrainingWeaponEffects effects;
        private TrainingWeaponPresentation presentation;
        private TrainingAimLaser aimLaser;
        private TrainingUpperBodyAim upperBodyAim;
        private readonly Dictionary<string, TrainingAmmoState> magazines = new Dictionary<string, TrainingAmmoState>();
        private bool scopeNeedsRelease = true;
        private LobbyWeapon equipped;
        public event Action<TrainingShot> ShotFired;
        public event Action<bool> AttackStarted;
        public event Action WeaponChanged;
        public event Action<TrainingTarget, float, Vector3> TargetHit;
        public bool IsSniper => definition != null && definition.IsSniper;
        public bool IsMelee => definition != null && definition.IsMelee;
        public bool IsTaser => definition != null && definition.IsTaser;
        public bool LastShotOffhand { get; private set; }
        public bool IsScoped { get; private set; }
        public float LastShotTime { get; private set; } = float.NegativeInfinity;
        public TrainingShot LastShot { get; private set; }
        public int ShotsFired { get; private set; }
        public int LastPelletCount { get; private set; }
        public string WeaponId => equipped?.id;
        public Sprite HudIcon => definition != null && definition.hudIcon != null ? definition.hudIcon : equipped?.thumbnail;
        public bool CanFire => definition != null && !definition.IsMelee;
        public TrainingThrowableController Throwables { get; private set; }
        public TrainingMeleeController Melee { get; private set; }
        public bool CanAttack => CanFire || IsMelee || (Throwables != null && Throwables.CanThrow);
        public int Slot => TrainingWeaponSelection.SlotOf(equipped);
        public TrainingAmmoState Ammo { get; private set; }
        public Vector3 MuzzlePosition => weapon.TransformPoint(definition.muzzlePosition);

        public void Initialize(TrainingCharacterController owner, Transform equippedWeapon, string id)
        {
            PrepareEquipmentChange();
            var catalog = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
            definition = catalog != null ? catalog.Find(id) : null;
            equipped = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog")?.Weapon(id);
            character = owner;
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
            if (Throwables == null) Throwables = gameObject.AddComponent<TrainingThrowableController>();
            Throwables.Bind(owner, equippedWeapon, id);
            if (Melee == null) Melee = gameObject.AddComponent<TrainingMeleeController>();
            Melee.Bind(owner, this, definition);
            Ammo = null;
            trigger = null;
            if (equipped == null || equippedWeapon == null)
            {
                Debug.LogError("[Training] Missing firing assets for " + id + ". Run Training/Build Weapon Effects.");
                enabled = false;
                return;
            }
            if (upperBodyAim != null) upperBodyAim.enabled = CanFire;
            if (aimLaser != null) aimLaser.enabled = definition != null;
            if (definition == null)
            {
                // Equipment models without a combat profile still switch normally. Never retain the previous gun's ammo/effects.
                enabled = true;
                WeaponChanged?.Invoke();
                return;
            }
            if (IsMelee)
            {
                if (presentation == null) presentation = gameObject.AddComponent<TrainingWeaponPresentation>();
                presentation.Initialize(owner, this, definition);
                if (aimLaser != null) aimLaser.enabled = false;
                enabled = true; WeaponChanged?.Invoke(); return;
            }
            trigger = new TrainingFireControl(definition.cycleTime, definition.automatic);
            if (!magazines.TryGetValue(id, out var magazine))
                magazines[id] = magazine = new TrainingAmmoState(definition.magazineSize, definition.reserveAmmo, definition.reloadDuration, true);
            Ammo = magazine;
            if (effects == null)
            {
                effects = gameObject.AddComponent<TrainingWeaponEffects>();
                effects.Initialize(catalog, definition, weapon);
            }
            else effects.Rebind(definition, weapon);
            if (presentation == null) presentation = gameObject.AddComponent<TrainingWeaponPresentation>();
            presentation.Initialize(owner, this, definition);
            if (upperBodyAim == null) upperBodyAim = gameObject.AddComponent<TrainingUpperBodyAim>();
            upperBodyAim.Initialize(owner, this, weapon);
            if (aimLaser == null) aimLaser = gameObject.AddComponent<TrainingAimLaser>();
            aimLaser.Initialize(owner, this, weapon, definition, catalog.tracerMaterial);
            enabled = true;
            WeaponChanged?.Invoke();
        }

        public void PrepareEquipmentChange()
        {
            Melee?.Cancel();
            Throwables?.ReleaseEquipment();
            CancelAim();
            Ammo?.CancelReload();
            upperBodyAim?.RestorePose();
            presentation?.Release();
            aimLaser?.Hide();
        }

        public void ResetEquipment()
        {
            Melee?.Cancel();
            presentation?.ResetPresentation();
            Throwables?.ResetEquipment();
            CancelAim();
            foreach (var ammo in magazines.Values) ammo.Reset();
            trigger?.Tick(true, false, false, Time.timeAsDouble);
        }

        public void CancelAim() { IsScoped = false; scopeNeedsRelease = true; }

        public void ProcessAim(bool held, bool allowed)
        {
            allowed &= character != null && character.InputEnabled && isActiveAndEnabled && Ammo != null && !Ammo.IsReloading;
            if (!allowed) { CancelAim(); return; }
            if (!held) scopeNeedsRelease = false;
            IsScoped = held && !scopeNeedsRelease && IsSniper;
        }

        private void Update()
        {
            bool active = character != null && character.InputEnabled && Application.isFocused && Time.timeScale > 0;
            AdvanceReload(active ? Time.deltaTime : 0);
            Melee?.Advance(active ? Time.deltaTime : 0);
            bool allowed = active && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            if (allowed && Input.GetKeyDown(KeyCode.R)) TryReload();
            ProcessAim(Input.GetMouseButton(1), allowed);
        }

        private void LateUpdate()
        {
            bool allowed = character != null && character.InputEnabled && Application.isFocused && Time.timeScale > 0
                && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            if (IsMelee) Melee.ProcessInput(Input.GetMouseButton(0), Input.GetMouseButton(1), allowed);
            else ProcessTrigger(Input.GetMouseButton(0), Input.GetMouseButtonDown(0), allowed, Time.timeAsDouble);
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
            if (IsTaser && Ammo != null && Ammo.Magazine == 0 && !Ammo.IsReloading) TryReload();
        }

        public void ProcessTrigger(bool held, bool pressed, bool allowed, double now)
        {
            if (trigger == null || Ammo == null) return;
            int count = trigger.Tick(held, pressed, allowed && character.InputEnabled && isActiveAndEnabled
                && !Ammo.IsReloading && Ammo.Magazine > 0, now);
            for (int i = 0; i < count && Ammo.TryConsume(); i++) FireBullet();
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
                var shot = new TrainingShot(origin, end, hit, hitSomething, damage);
                if (i == 0) LastShot = shot;
                var target = hitSomething ? hit.collider.GetComponentInParent<TrainingTarget>() : null;
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
            Melee?.Cancel();
            Throwables?.Cancel();
            CancelAim();
            trigger?.Tick(true, false, false, Time.timeAsDouble);
            presentation?.ResetPresentation();
            upperBodyAim?.RestorePose();
            aimLaser?.Hide();
        }
        public void NotifyThrowableHit(TrainingTarget target, float damage, Vector3 position) => TargetHit?.Invoke(target, damage, position);
        public void NotifyMeleeHit(TrainingTarget target, float damage, Vector3 position) => TargetHit?.Invoke(target, damage, position);
        public void NotifyAttack(bool alternate) { LastShotTime = Time.time; AttackStarted?.Invoke(alternate); }
    }
}
