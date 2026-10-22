using System;

namespace OperationBlacktide.Shared.Combat
{
    /// <summary>Portable authoring data. Resource identifiers are resolved by an engine adapter.</summary>
    [Serializable]
    public class WeaponInfo
    {
        public string id, modelId;
        public float cycleTime, range, damage, rangeModifier;
        public bool automatic = true, silenced;
        public int pelletCount = 1;
        public float spreadAngle;
        public int magazineSize = 30, reserveAmmo = 90;
        public float reloadDuration = 2.5f;

        public WeaponInfo Copy() => new WeaponInfo {
            id = id, modelId = modelId, cycleTime = cycleTime, range = range, damage = damage, rangeModifier = rangeModifier,
            automatic = automatic, silenced = silenced, pelletCount = pelletCount, spreadAngle = spreadAngle,
            magazineSize = magazineSize, reserveAmmo = reserveAmmo, reloadDuration = reloadDuration
        };

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Weapon id is required.");
            if (!Finite(cycleTime) || cycleTime < .02f || !Finite(reloadDuration) || reloadDuration < .1f ||
                magazineSize < 1 || reserveAmmo < 0 || pelletCount < 1 || !Finite(range) || range < 0 ||
                !Finite(damage) || damage < 0 || !Finite(rangeModifier) || rangeModifier < 0 || rangeModifier > 1 ||
                !Finite(spreadAngle) || spreadAngle < 0 || spreadAngle > 15)
                throw new ArgumentException("Invalid weapon parameters: " + id);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
