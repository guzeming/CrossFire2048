using System;
using System.Collections.Generic;
using System.IO;
using OperationBlacktide.Simulation;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace OperationBlacktide.EditorTools
{
    public static class BuildBattleNavigation
    {
        public const string AssetPath = "Assets/Art/Maps/DustII/Navigation/BattleNavMesh.asset";

        public static void Bake(GameObject map, Vector3[] spawns)
        {
            // Bake in the editor: the shipped map meshes are deliberately not CPU-readable.
            var root = new GameObject("Battle Navigation");
            var surface = root.AddComponent<NavMeshSurface>();
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.layerMask = ~(1 << CharacterSimulation.PawnLayer);
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(map.transform, surface.layerMask, surface.useGeometry, 0,
                new List<NavMeshBuildMarkup>(), sources);
            if (sources.Count == 0) throw new InvalidOperationException("No map geometry for bot navigation.");
            var bounds = new Bounds(map.transform.position, Vector3.zero);
            foreach (var collider in map.GetComponentsInChildren<Collider>()) bounds.Encapsulate(collider.bounds);
            bounds.Expand(2);
            var settings = surface.GetBuildSettings();
            // Match the authoritative character motor, especially its step height.
            settings.agentRadius = .4f; settings.agentHeight = 1.8f;
            settings.agentClimb = .3f; settings.agentSlope = 45;
            settings.overrideVoxelSize = true; settings.voxelSize = .1f;
            var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null) throw new InvalidOperationException("Could not bake bot navigation.");
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.Refresh();
            var existing = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if (existing == null) { data.name = "BattleNavMesh"; AssetDatabase.CreateAsset(data, AssetPath); }
            else { EditorUtility.CopySerialized(data, existing); UnityEngine.Object.DestroyImmediate(data); data = existing; EditorUtility.SetDirty(data); }
            surface.navMeshData = data; surface.AddData();
            var path = new NavMeshPath();
            foreach (var spawn in spawns)
                if (!NavMesh.SamplePosition(spawn, out var hit, 1, NavMesh.AllAreas) ||
                    !NavMesh.CalculatePath(spawns[0], hit.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Bot spawn is not connected to navigation: " + spawn);
            AssetDatabase.SaveAssets();
            BuildCombatRadar.Build();
            Debug.Log("BATTLE_NAVIGATION_PASS sources=" + sources.Count + " spawns=" + spawns.Length);
        }
    }
}
