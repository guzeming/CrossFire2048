using System;
using System.Collections.Generic;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.Combat;

namespace OperationBlacktide.Shared.Characters
{
    public sealed class EquipmentComponent : GameComponent
    {
        private readonly Dictionary<string, Weapon> weapons = new Dictionary<string, Weapon>();
        public Weapon Current { get; private set; }
        public IEnumerable<Weapon> Weapons => weapons.Values;
        public void AddWeapon(Weapon weapon)
        {
            if (weapon == null || weapon.IsDisposed || weapon.Owner != null || weapons.ContainsKey(weapon.InstanceId))
                throw new ArgumentException("A live weapon instance must have exactly one inventory owner.");
            weapons.Add(weapon.InstanceId, weapon);
            weapon.Owner = this;
        }
        public Weapon Find(string instanceId) => weapons.TryGetValue(instanceId, out var weapon) ? weapon : null;
        public Weapon Equip(string instanceId)
        {
            var selected = Find(instanceId) ?? throw new ArgumentException("Weapon is not owned: " + instanceId);
            if (Current != selected) { Current?.Ammo.CancelReload(); Current = selected; }
            return Current;
        }
        public Weapon Remove(string instanceId)
        {
            var weapon = Find(instanceId);
            if (weapon == null) return null;
            weapon.Ammo.CancelReload();
            weapons.Remove(instanceId);
            weapon.Owner = null;
            if (Current == weapon) Current = null;
            return weapon; // Ownership transfers to a pickup or another inventory.
        }
        public void Unequip() { Current?.Ammo.CancelReload(); Current = null; }
        public void Reset() { foreach (var weapon in weapons.Values) weapon.Reset(); }
        public override void Tick(float deltaTime) { Current?.Tick(deltaTime); }
        public override void Dispose()
        {
            foreach (var weapon in weapons.Values) { weapon.Owner = null; weapon.Dispose(); }
            weapons.Clear(); Current = null;
        }
    }
}
