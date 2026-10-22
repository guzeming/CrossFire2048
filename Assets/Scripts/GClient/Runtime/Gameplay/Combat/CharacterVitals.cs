using System;
using OperationBlacktide.Shared.Characters;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Combat
{
    /// <summary>Local character health and armor; death/respawn policy belongs to the game mode.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "OperationBlacktide.Client.Features.Training", "Assembly-CSharp", "TrainingVitals")]
    public sealed class CharacterVitals : Damageable
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0)] private int maxArmor = 100;
        [SerializeField, Min(0)] private int startingArmor;
        [SerializeField] private bool wearsHelmet;
        public int MaxHealth => state?.MaxHealth ?? maxHealth;
        public int MaxArmor => state?.MaxArmor ?? maxArmor;
        private VitalsComponent state;
        public VitalsComponent State => state;
        public int Health => state?.Health ?? 0;
        public int Armor => state?.Armor ?? 0;
        public override bool HasBodyArmor => Armor > 0;
        public override bool HasHeadArmor => wearsHelmet && Armor > 0;
        public override bool IsAlive => Health > 0;
        public override float HealthFraction => (float)Health / Mathf.Max(1, MaxHealth);
        public event Action Changed;
        public event Action Died;

        private void Awake() { Bind(new VitalsComponent(maxHealth, maxArmor)); Restore(); }
        public void Bind(VitalsComponent value)
        {
            if (state != null) { state.Changed -= OnChanged; state.Died -= OnDied; }
            state = value; state.Changed += OnChanged; state.Died += OnDied;
        }
        private void OnChanged() => Changed?.Invoke();
        private void OnDied() => Died?.Invoke();
        private void OnDestroy()
        { if (state != null) { state.Changed -= OnChanged; state.Died -= OnDied; } }

        public void Restore()
        {
            state.Restore(startingArmor);
        }

        public void SetArmor(int value)
        {
            state.SetArmor(value);
        }

        public override float ApplyDamage(float amount, bool bypassArmor = false)
        {
            return state.ApplyDamage(amount, bypassArmor);
        }
    }
}
