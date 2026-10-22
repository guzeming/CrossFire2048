using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using UnityEngine;

namespace OperationBlacktide.Simulation
{
    // Shared collision/movement only. No input polling, camera, UI or server authority here.
    public static class CharacterSimulation
    {
        public const int PawnLayer = 9;
        private static readonly MovementComponent movement = new MovementComponent();
        public static Vector3 Position(BattleState state) => new Vector3(state.X, state.Y, state.Z);
        public static void Capture(CharacterController motor, ref BattleState state)
        { Vector3 p = motor.transform.position; state.X = p.x; state.Y = p.y; state.Z = p.z; }
        public static void Restore(CharacterController motor, BattleState state)
        {
            motor.enabled = false;
            motor.transform.SetPositionAndRotation(Position(state), Quaternion.Euler(0, state.Yaw, 0));
            motor.enabled = true;
        }
        public static void Step(CharacterController motor, ref BattleState state, BattleInput input)
        {
            state.Ack = input.Sequence;
            if (state.Health <= 0) return;
            state.Yaw = input.Yaw;
            motor.transform.rotation = Quaternion.Euler(0, state.Yaw, 0);
            WorldVector motion = movement.Velocity(input.X, input.Z, state.Yaw, input.Has(InputButtons.Sprint),
                input.Has(InputButtons.Jump), motor.isGrounded, ref state.VerticalSpeed, BattleProtocol.Step);
            Vector3 velocity = new Vector3(motion.x, motion.y, motion.z);
            CollisionFlags flags = motor.Move(velocity * BattleProtocol.Step);
            if ((flags & CollisionFlags.Above) != 0 && state.VerticalSpeed > 0) state.VerticalSpeed = 0;
            Capture(motor, ref state);
        }
        public static CharacterController CreatePawn(int id)
        {
            var go = new GameObject("BattlePawn_" + id); go.layer = PawnLayer;
            var motor = go.AddComponent<CharacterController>();
            motor.height = 1.8f; motor.radius = .35f; motor.center = new Vector3(0, .9f, 0);
            motor.stepOffset = .3f; motor.skinWidth = .02f; motor.minMoveDistance = 0;
            return motor;
        }
    }
}
