using System;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.Combat
{
    /// <summary>Independent trigger/cadence state; blocked clicks must be released before firing again.</summary>
    public sealed class FireControl : GameComponent
    {
        private readonly double interval;
        private readonly bool automatic;
        private double nextShot;
        private bool wasHeld, requireRelease = true;

        public FireControl(float cycleTime, bool fullAuto)
        {
            interval = Math.Max(.02, cycleTime);
            automatic = fullAuto;
        }

        public int Tick(bool held, bool pressed, bool allowed, double now)
        {
            if (!held) { wasHeld = false; requireRelease = false; return 0; }
            if (!allowed) { wasHeld = false; requireRelease = true; return 0; }
            if (requireRelease || (!automatic && !pressed)) return 0;
            if (!wasHeld) nextShot = Math.Max(nextShot, now);
            wasHeld = true;
            int count = 0;
            // Preserve cadence at low frame rates, without replaying a long stall as a huge burst.
            while (now + .000001 >= nextShot && count < 4)
            {
                count++;
                nextShot += interval;
                if (!automatic) break;
            }
            if (count == 4 && nextShot <= now) nextShot = now + interval;
            return count;
        }

        public bool TryFireSingle(double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now + .000001 < nextShot) return false;
            nextShot = now + interval;
            return true;
        }

        public void Reset() { nextShot = 0; wasHeld = false; requireRelease = true; }
    }
}
