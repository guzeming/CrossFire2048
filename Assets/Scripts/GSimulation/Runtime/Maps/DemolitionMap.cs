using System;
using System.Linq;
using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Modes.Demolition;
using UnityEngine;

namespace OperationBlacktide.Simulation
{
    [Serializable] public sealed class DemolitionRoute
    {
        public OpeningLane lane;
        public string name;
        public int site;
        public Vector3[] waypoints;
        public Vector3 lookAt;
    }
    [Serializable] public sealed class DemolitionHold
    {
        public int site;
        public Vector3 position, lookAt;
    }
    public sealed class DemolitionMap : MonoBehaviour, IDemolitionMap
    {
        public Vector3[] AttackSpawns = Array.Empty<Vector3>(), DefendSpawns = Array.Empty<Vector3>(), Sites = Array.Empty<Vector3>();
        public float[] AttackYaw = Array.Empty<float>(), DefendYaw = Array.Empty<float>();
        public DemolitionRoute[] OpeningRoutes = Array.Empty<DemolitionRoute>();
        public DemolitionHold[] SiteHolds = Array.Empty<DemolitionHold>();
        public DemolitionRoute Route(OpeningLane lane) => OpeningRoutes.First(r => r.lane == lane);
        public DemolitionHold Hold(int site, int slot) => SiteHolds.Where(h => h.site == site).ElementAt(slot % 5);
        public const float SiteRadius = 3.5f;
        public static Vector3 Vector(DemolitionPoint p) => new Vector3(p.X, p.Y, p.Z);
        public static DemolitionPoint Point(Vector3 p) => new DemolitionPoint(p.x, p.y, p.z);
        public bool InSite(DemolitionPoint position, out int site)
        {
            for (int i = 0; i < Sites.Length; i++)
            {
                Vector3 d = Vector(position) - Sites[i];
                if (Mathf.Abs(d.y) < 1.5f && d.x*d.x + d.z*d.z <= SiteRadius*SiteRadius) { site = i; return true; }
            }
            site = -1; return false;
        }
        public bool CanReach(DemolitionPoint from, DemolitionPoint target)
        {
            Vector3 start = Vector(from) + Vector3.up * .5f, end = Vector(target) + Vector3.up * .5f;
            return !Physics.Linecast(start, end, Physics.DefaultRaycastLayers & ~(1 << CharacterSimulation.PawnLayer), QueryTriggerInteraction.Ignore);
        }
        public void Validate()
        {
            if (AttackSpawns.Length < 5 || DefendSpawns.Length < 5 || Sites.Length != 2 ||
                AttackYaw.Length != AttackSpawns.Length || DefendYaw.Length != DefendSpawns.Length ||
                OpeningRoutes.Length != Enum.GetValues(typeof(OpeningLane)).Length || OpeningRoutes.Select(r => r.lane).Distinct().Count() != OpeningRoutes.Length ||
                OpeningRoutes.Any(r => r.waypoints == null || r.waypoints.Length == 0) ||
                SiteHolds.Count(h => h.site == 0) != 5 || SiteHolds.Count(h => h.site == 1) != 5)
                throw new InvalidOperationException("Rebuild battle scenes to configure demolition spawn pools and tactical routes.");
        }
    }
}
