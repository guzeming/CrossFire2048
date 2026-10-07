using System;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    [Serializable]
    public sealed class TrainingReloadSound
    {
        [Range(0, 1)] public float normalizedTime;
        public AudioClip clip;
        [Range(0, 1)] public float volume = .65f;
    }

    [Serializable]
    public sealed class TrainingWeaponDefinition
    {
        public string id;
        public float cycleTime, range, damage, rangeModifier;
        public bool automatic = true, silenced;
        [Min(1)] public int pelletCount = 1;
        [Range(0, 15)] public float spreadAngle;
        [Min(1)] public int magazineSize = 30;
        [Min(0)] public int reserveAmmo = 90;
        [Min(.1f)] public float reloadDuration = 2.5f;
        public Sprite hudIcon;
        public AnimationClip fireAnimation, alternateFireAnimation, reloadAnimation, idleAnimation;
        public AnimationClip[] locomotionAnimations = Array.Empty<AnimationClip>();
        public TrainingReloadSound[] reloadSounds = Array.Empty<TrainingReloadSound>();
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

    [CreateAssetMenu(menuName = "OperationBlacktide/Training Weapons")]
    public sealed class TrainingWeaponCatalog : ScriptableObject
    {
        public TrainingWeaponDefinition[] weapons;
        public Material flashMaterial, tracerMaterial, casingMaterial;
        public Material[] bulletHoleMaterials = Array.Empty<Material>();
        public Mesh casingMesh;

        public TrainingWeaponDefinition Find(string id) => Array.Find(weapons, weapon => weapon.id == id);
    }
}
