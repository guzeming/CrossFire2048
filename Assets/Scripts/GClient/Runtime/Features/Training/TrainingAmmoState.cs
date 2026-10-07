using System;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Magazine and reload state. Training refills magazines from an unlimited reserve.</summary>
    public sealed class TrainingAmmoState
    {
        private readonly float reloadDuration;
        private readonly int initialReserve;
        private float reloadElapsed;
        public int Capacity { get; }
        public int Magazine { get; private set; }
        public int Reserve { get; private set; }
        public bool InfiniteReserve { get; }
        public bool IsReloading { get; private set; }
        public float ReloadElapsed => reloadElapsed;
        public float ReloadDuration => reloadDuration;
        public float ReloadProgress => IsReloading ? reloadElapsed / reloadDuration : 0;
        public bool IsLow => Magazine <= Math.Max(1, Capacity / 5);
        public event Action Changed;

        public TrainingAmmoState(int capacity, int reserve, float duration, bool infiniteReserve)
        {
            Capacity = Math.Max(1, capacity);
            initialReserve = Math.Max(0, reserve);
            reloadDuration = Math.Max(.1f, duration);
            InfiniteReserve = infiniteReserve;
            Reset();
        }

        public bool TryConsume()
        {
            if (IsReloading || Magazine == 0) return false;
            Magazine--;
            Changed?.Invoke();
            return true;
        }

        public bool TryReload()
        {
            if (IsReloading || Magazine == Capacity || (!InfiniteReserve && Reserve == 0)) return false;
            IsReloading = true;
            reloadElapsed = 0;
            Changed?.Invoke();
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!IsReloading || deltaTime <= 0) return;
            reloadElapsed = Math.Min(reloadDuration, reloadElapsed + deltaTime);
            if (reloadElapsed < reloadDuration) return;
            int rounds = InfiniteReserve ? Capacity - Magazine : Math.Min(Capacity - Magazine, Reserve);
            Magazine += rounds;
            if (!InfiniteReserve) Reserve -= rounds;
            IsReloading = false;
            Changed?.Invoke();
        }

        public void Reset()
        {
            Magazine = Capacity;
            Reserve = initialReserve;
            IsReloading = false;
            reloadElapsed = 0;
            Changed?.Invoke();
        }

        public void CancelReload()
        {
            if (!IsReloading) return;
            IsReloading = false;
            reloadElapsed = 0;
            Changed?.Invoke();
        }
    }
}
