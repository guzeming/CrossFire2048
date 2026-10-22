using System;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.Characters
{
    /// <summary>Movement rules in meters/seconds. Collision integration belongs to the host engine.</summary>
    public sealed class MovementComponent : GameComponent
    {
        public float WalkSpeed = 4.5f, SprintSpeed = 7, Gravity = -24, JumpHeight = 1;
        public WorldVector Velocity(float x, float z, float yaw, bool sprint, bool jump, bool grounded, ref float vertical, float dt, float speedScale = 1)
        {
            float length = (float)Math.Sqrt(x * x + z * z);
            if (length > 1) { x /= length; z /= length; }
            var horizontal = WorldVector.Rotate(yaw, new WorldVector(x, 0, z)) * ((sprint ? SprintSpeed : WalkSpeed) * speedScale);
            if (grounded && vertical < 0) vertical = -2;
            if (grounded && jump) vertical = (float)Math.Sqrt(JumpHeight * -2 * Gravity);
            vertical = Math.Max(-40, vertical + Gravity * dt);
            horizontal.y = vertical; return horizontal;
        }
    }
}
