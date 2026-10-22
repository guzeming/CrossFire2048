using System;
using System.Linq;
using OperationBlacktide.Simulation;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;

namespace OperationBlacktide.EditorTools
{
    public static class BuildDemolitionMap
    {
        [Serializable] sealed class Spawn { public Vector3 position; public float yaw; }
        [Serializable] sealed class Layout
        {
            public Spawn[] attackSpawns, defendSpawns;
            public DemolitionRoute[] routes;
            public DemolitionHold[] holds;
        }
        public static Vector3[] FindSites(GameObject map)
        {
            var marks = map.GetComponentsInChildren<Renderer>().Where(r => r.sharedMaterials.Any(m => m != null && m.name.Contains("bombsite_x_spray")))
                .Select(r => r.bounds.center).OrderByDescending(p => p.z).ToArray();
            if (marks.Length != 2) throw new InvalidOperationException("Expected both imported Dust II bombsite floor markings; found " + marks.Length);
            return marks;
        }
        public static DemolitionMap Configure(GameObject map, Vector3[] marks)
        {
            var rules = map.AddComponent<DemolitionMap>();
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Maps/DustII/DemolitionLayout.json");
            if (source == null) throw new InvalidOperationException("Missing Dust II tactical layout.");
            var layout = JsonUtility.FromJson<Layout>(source.text);
            rules.AttackSpawns = layout.attackSpawns.Select(s => SpawnFloor(s.position)).ToArray();
            rules.DefendSpawns = layout.defendSpawns.Select(s => SpawnFloor(s.position)).ToArray();
            rules.AttackYaw = layout.attackSpawns.Select(s => s.yaw).ToArray();
            rules.DefendYaw = layout.defendSpawns.Select(s => s.yaw).ToArray();
            Vector3 connectedOrigin = rules.AttackSpawns[0];
            rules.Sites = marks.Select(p => WalkableConnected(p, 8, connectedOrigin)).ToArray();
            rules.OpeningRoutes = layout.routes;
            foreach (var route in rules.OpeningRoutes)
            {
                route.waypoints = route.waypoints.Select(p => WalkableConnected(p, 2, connectedOrigin)).ToArray();
                if ((int)route.lane <= 3) route.waypoints = route.waypoints.Concat(new[] { rules.Sites[route.site] }).ToArray();
            }
            rules.SiteHolds = layout.holds;
            foreach (var hold in rules.SiteHolds) hold.position = WalkableConnected(hold.position, 2, connectedOrigin);
            var path = new NavMeshPath();
            foreach (var p in rules.AttackSpawns.Concat(rules.DefendSpawns).Concat(rules.Sites).Concat(rules.OpeningRoutes.SelectMany(r => r.waypoints)))
                if (!NavMesh.CalculatePath(rules.AttackSpawns[0], p, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("Demolition location has no complete navigation path: " + p);
            rules.Validate(); Debug.Log("DEMOLITION_MAP_PASS sites=" + string.Join(" / ", rules.Sites.Select(p => p.ToString("F2"))));
            return rules;
        }
        static Vector3 SpawnFloor(Vector3 source)
        {
            if (!Physics.Raycast(source + Vector3.up, Vector3.down, out var ground, 3,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .85f ||
                !NavMesh.SamplePosition(ground.point, out var nav, .75f, NavMesh.AllAreas))
                throw new InvalidOperationException("Original Dust II spawn has no walkable floor: " + source);
            var p = nav.position + Vector3.up * .08f;
            if (Physics.CheckCapsule(p + Vector3.up * .35f, p + Vector3.up * 1.45f, .35f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Original Dust II spawn is obstructed: " + source);
            return p;
        }
        static Vector3 WalkableConnected(Vector3 near, float radius, Vector3 origin)
        {
            var triangles = NavMesh.CalculateTriangulation(); var candidates = new System.Collections.Generic.List<Vector3>();
            if (NavMesh.SamplePosition(near, out var nearest, radius, NavMesh.AllAreas)) candidates.Add(nearest.position);
            for (int i = 0; i < triangles.indices.Length; i += 3)
            {
                var p = (triangles.vertices[triangles.indices[i]] + triangles.vertices[triangles.indices[i+1]] + triangles.vertices[triangles.indices[i+2]]) / 3;
                if ((p-near).sqrMagnitude <= radius*radius) candidates.Add(p);
            }
            var path = new NavMeshPath();
            foreach (var candidate in candidates.OrderBy(p => (p-near).sqrMagnitude))
                if (NavMesh.CalculatePath(origin, candidate, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete)
                    return candidate + Vector3.up * .08f;
            throw new InvalidOperationException("No reachable navigation near demolition location " + near);
        }
    }
}
