using System;
using OperationBlacktide.Shared.Battle;

namespace OperationBlacktide.Shared.Characters
{
    /// <summary>World-space meters; adapters convert engine axes/units at their boundary.</summary>
    public struct WorldVector
    {
        public float x, y, z;
        public WorldVector(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public WorldVector normalized => magnitude > .000001f ? this * (1 / magnitude) : default;
        public static WorldVector up => new WorldVector(0, 1, 0);
        public static WorldVector forward => new WorldVector(0, 0, 1);
        public static WorldVector operator +(WorldVector a, WorldVector b) => new WorldVector(a.x + b.x, a.y + b.y, a.z + b.z);
        public static WorldVector operator -(WorldVector a, WorldVector b) => new WorldVector(a.x - b.x, a.y - b.y, a.z - b.z);
        public static WorldVector operator *(WorldVector a, float b) => new WorldVector(a.x * b, a.y * b, a.z * b);
        public static float Dot(WorldVector a, WorldVector b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static WorldVector ProjectOnPlane(WorldVector v, WorldVector normal) => v - normal * Dot(v, normal);
        public static WorldVector Position(BattleState state) => new WorldVector(state.X, state.Y, state.Z);
        public static WorldVector Rotate(float yaw, WorldVector v)
        {
            float angle = yaw * GameMath.Deg2Rad, sin = GameMath.Sin(angle), cos = GameMath.Cos(angle);
            return new WorldVector(cos * v.x + sin * v.z, v.y, -sin * v.x + cos * v.z);
        }
    }
    public static class GameMath
    {
        public const float Deg2Rad = (float)Math.PI / 180, Rad2Deg = 180 / (float)Math.PI;
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Abs(float x) => Math.Abs(x);
        public static float DeltaAngle(float a, float b)
        { float delta = (b - a) % 360; return delta > 180 ? delta - 360 : delta < -180 ? delta + 360 : delta; }
        public static float MoveTowardsAngle(float a, float b, float step)
        { float delta = DeltaAngle(a, b); return a + Math.Max(-step, Math.Min(step, delta)); }
    }
}
