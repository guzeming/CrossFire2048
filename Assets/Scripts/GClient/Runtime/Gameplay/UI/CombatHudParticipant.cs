using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.UI
{
    public struct CombatHudParticipant
    {
        public int Id, Team, Health, MaxHealth;
        public string Name, Status;
        public bool Connected, HasSnapshot;
        public Sprite Portrait;
        public Vector3 Position;
        public float Yaw;
        // Rendered actor transform keeps radar and LOS aligned with client interpolation/prediction.
        public Transform WorldTransform;
    }
}
