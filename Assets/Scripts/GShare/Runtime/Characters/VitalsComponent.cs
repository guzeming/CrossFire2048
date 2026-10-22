using System;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.Characters
{
    public sealed class VitalsComponent : GameComponent
    {
        public int MaxHealth { get; }
        public int MaxArmor { get; }
        public int Health { get; private set; }
        public int Armor { get; private set; }
        public bool IsAlive => Health > 0;
        public event Action Changed;
        public event Action Died;

        public VitalsComponent(int maxHealth = 100, int maxArmor = 100)
        { MaxHealth = Math.Max(1, maxHealth); MaxArmor = Math.Max(0, maxArmor); Health = MaxHealth; }
        public void Restore(int armor = 0)
        { Health = MaxHealth; Armor = Math.Max(0, Math.Min(MaxArmor, armor)); Changed?.Invoke(); }
        public void SetArmor(int value) { Armor = Math.Max(0, Math.Min(MaxArmor, value)); Changed?.Invoke(); }
        public int ApplyDamage(float amount, bool bypassArmor = false)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 || !IsAlive) return 0;
            int incoming = (int)Math.Min(int.MaxValue, Math.Round((double)amount, MidpointRounding.ToEven));
            int absorbed = bypassArmor ? 0 : Math.Min(Armor, incoming / 2);
            int before = Health;
            Armor -= absorbed; Health = Math.Max(0, Health - (incoming - absorbed));
            Changed?.Invoke();
            if (!IsAlive) Died?.Invoke();
            return before - Health;
        }
    }
}
