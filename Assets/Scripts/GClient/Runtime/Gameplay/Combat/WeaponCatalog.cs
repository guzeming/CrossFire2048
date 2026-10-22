using System;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingReloadSound")]
    public sealed class ReloadSound
    {
        [Range(0, 1)] public float normalizedTime;
        public AudioClip clip;
        [Range(0, 1)] public float volume = .65f;
    }

    [Serializable]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingWeaponDefinition")]
    public sealed class WeaponDefinition : OperationBlacktide.Shared.Combat.WeaponInfo
    {
        public Sprite hudIcon;
        public AnimationClip fireAnimation, alternateFireAnimation, reloadAnimation, idleAnimation;
        public AnimationClip[] locomotionAnimations = Array.Empty<AnimationClip>();
        public ReloadSound[] reloadSounds = Array.Empty<ReloadSound>();
        public Vector3 muzzlePosition, ejectPosition;
        public Vector3 offhandMuzzlePosition, combatGripPosition, offhandGripPosition;
        public bool hasCombatGrip;
        public Quaternion combatGripRotation = Quaternion.identity, offhandGripRotation = Quaternion.identity;
        public AudioClip[] shots;
        public AudioClip meleeHeavySound;
        public AudioClip[] meleeHitSounds = Array.Empty<AudioClip>();

        // Imported weapon IDs share this prefix for both bolt-action and automatic sniper rifles.
        public bool IsSniper => !string.IsNullOrEmpty(id) && id.StartsWith("weapon_snip_", StringComparison.Ordinal);
        public bool IsPistol => !string.IsNullOrEmpty(id) && id.StartsWith("weapon_pist_", StringComparison.Ordinal);
        public bool IsMelee => !string.IsNullOrEmpty(id) && id.StartsWith("weapon_knife_", StringComparison.Ordinal);
        public bool IsTaser => id == "weapon_pist_taser";
        public bool IsDual => id == "weapon_pist_elite";
        public bool HasAimLaser => !string.IsNullOrEmpty(id) && !IsMelee;
    }

    [CreateAssetMenu(menuName = "OperationBlacktide/Gameplay/Weapons")]
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingWeaponCatalog")]
    public sealed class WeaponCatalog : ScriptableObject
    {
        public WeaponDefinition[] weapons;
        public Material flashMaterial, tracerMaterial, casingMaterial;
        public Material[] bulletHoleMaterials = Array.Empty<Material>();
        public Mesh casingMesh;

        public WeaponDefinition Find(string id)
        {
            var definition = Array.Find(weapons, weapon => weapon.id == id);
            var info = OperationBlacktide.Simulation.GameConfig.FindWeapon(id);
            if (definition != null && info != null)
            {
                definition.cycleTime = info.cycleTime; definition.range = info.range; definition.damage = info.damage;
                definition.rangeModifier = info.rangeModifier; definition.automatic = info.automatic; definition.silenced = info.silenced;
                definition.magazineSize = info.magazineSize; definition.reserveAmmo = info.reserveAmmo; definition.reloadDuration = info.reloadDuration;
                definition.pelletCount = info.pelletCount; definition.spreadAngle = info.spreadAngle; definition.modelId = info.modelId;
            }
            return definition;
        }
    }
}
