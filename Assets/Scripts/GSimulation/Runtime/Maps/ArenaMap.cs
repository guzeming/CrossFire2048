using System;
using OperationBlacktide.Shared.Battle;
using UnityEngine;

namespace OperationBlacktide.Simulation
{
    // Both scenes are generated from the same Dust II prefab and validated spawn points.
    public sealed class ArenaMap : MonoBehaviour
    {
        [SerializeField] private Vector3[] spawnPoints = Array.Empty<Vector3>();
        [SerializeField] private float killHeight = -20;
        public float KillHeight => killHeight;
        public int SpawnCount => spawnPoints.Length;
        public Vector3 Spawn(int slot) => spawnPoints[slot % spawnPoints.Length];

        public void Configure(Vector3[] points, float bottom)
        {
            spawnPoints = (Vector3[])points.Clone();
            killHeight = bottom;
        }

        public static ArenaMap Initialize()
        {
            var map = FindObjectOfType<ArenaMap>();
            if (map == null || map.SpawnCount < BattleProtocol.MaxPlayers)
                throw new InvalidOperationException("Dust II arena is missing. Regenerate both battle scenes.");
            Physics.IgnoreLayerCollision(CharacterSimulation.PawnLayer, CharacterSimulation.PawnLayer, true);
            Physics.SyncTransforms();
            return map;
        }
    }
}
