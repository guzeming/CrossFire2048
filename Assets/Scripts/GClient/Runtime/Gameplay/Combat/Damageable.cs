using System.Collections.Generic;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>Local damage/visibility contract shared by characters and mode-specific targets.</summary>
    public abstract class Damageable : MonoBehaviour
    {
        internal static readonly HashSet<Damageable> ActiveTargets = new HashSet<Damageable>();
        private Renderer[] surfaces;
        internal Renderer[] Surfaces => surfaces ?? (surfaces = GetComponentsInChildren<Renderer>(true));
        public abstract bool IsAlive { get; }
        public abstract float HealthFraction { get; }
        public virtual bool HasBodyArmor => false;
        public virtual bool HasHeadArmor => false;
        public abstract float ApplyDamage(float amount, bool bypassArmor = false);

        public void RefreshRenderers() { surfaces = GetComponentsInChildren<Renderer>(true); }
        protected virtual void OnEnable() { ActiveTargets.Add(this); }
        protected virtual void OnDisable() { ActiveTargets.Remove(this); }
    }
}
