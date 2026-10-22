using System;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.Combat
{
    /// <summary>One physical weapon; switching a slot never recreates its ammunition.</summary>
    public sealed class Weapon : ComponentHost
    {
        public string InstanceId { get; }
        public WeaponInfo Info { get; }
        public AmmoState Ammo { get; }
        public FireControl Fire { get; }
        public Characters.EquipmentComponent Owner { get; internal set; }

        public Weapon(string instanceId, WeaponInfo info, bool infiniteReserve = false, params GameComponent[] extensions)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) throw new ArgumentException(nameof(instanceId));
            if (info == null) throw new ArgumentNullException(nameof(info));
            info.Validate();
            InstanceId = instanceId; Info = info.Copy();
            Ammo = Add(new AmmoState(info.magazineSize, info.reserveAmmo, info.reloadDuration, infiniteReserve));
            Fire = Add(new FireControl(info.cycleTime, info.automatic));
            if (extensions != null) foreach (var extension in extensions) Add(extension);
            Start();
        }

        public int ProcessTrigger(bool held, bool pressed, bool allowed, double now)
        {
            int requested = Fire.Tick(held, pressed, allowed && !Ammo.IsReloading && Ammo.Magazine > 0, now);
            int fired = 0;
            while (fired < requested && Ammo.TryConsume()) fired++;
            return fired;
        }

        // Authoritative fixed-tick commands never replay missed trigger frames as a burst.
        public bool TryFire(double now) => !Ammo.IsReloading && Ammo.Magazine > 0 && Fire.TryFireSingle(now) && Ammo.TryConsume();
        public void Reset() { Ammo.Reset(); Fire.Reset(); }
    }
}
