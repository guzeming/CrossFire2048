using System;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Local health source for the HUD and future training damage sources.</summary>
    public sealed class TrainingVitals : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 100;
        [SerializeField, Min(0)] private int maxArmor = 100;
        [SerializeField, Min(0)] private int startingArmor;
        public int MaxHealth => maxHealth;
        public int MaxArmor => maxArmor;
        public int Health { get; private set; }
        public int Armor { get; private set; }
        public bool IsAlive => Health > 0;
        public event Action Changed;
        public event Action Died;

        private void Awake() { Restore(); }

        public void Restore()
        {
            Health = Mathf.Max(1, maxHealth);
            Armor = Mathf.Clamp(startingArmor, 0, maxArmor);
            Changed?.Invoke();
        }

        public void SetArmor(int value)
        {
            Armor = Mathf.Clamp(value, 0, maxArmor);
            Changed?.Invoke();
        }

        public void ApplyDamage(int amount, bool bypassArmor = false)
        {
            if (amount <= 0 || !IsAlive) return;
            // Local training rule: armor absorbs half the incoming damage while available.
            int absorbed = bypassArmor ? 0 : Mathf.Min(Armor, amount / 2);
            Armor -= absorbed;
            Health = Mathf.Max(0, Health - (amount - absorbed));
            Changed?.Invoke();
            if (!IsAlive) Died?.Invoke();
        }
    }
}
