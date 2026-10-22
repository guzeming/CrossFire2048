using OperationBlacktide.Client.Gameplay.Combat;
using UnityEngine;

namespace OperationBlacktide.Client.Gameplay.Characters
{
    /// <summary>Short downward rays retain mesh triangle indices; water volumes only cover wet feet.</summary>
    internal sealed class FootstepSurfaceProbe
    {
        private readonly Transform owner;
        private readonly CharacterController controller;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[16];

        public FootstepSurfaceProbe(CharacterMotor character)
        {
            owner = character.transform;
            controller = character.GetComponent<CharacterController>();
        }

        public ImpactKind Resolve(out Collider ground)
        {
            ground = null;
            if (controller == null || !controller.enabled) return ImpactKind.Default;
            Bounds bounds = controller.bounds;
            float lift = Mathf.Max(.15f, controller.stepOffset + .05f);
            Vector3 feet = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            bool found = Probe(feet, lift, bounds.min.y, out var support);
            if (!found)
            {
                // The capsule can still stand on an edge when its centre ray is over empty space.
                float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * .7f;
                float nearest = float.PositiveInfinity;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 offset = (i == 0 ? Vector3.right : i == 1 ? Vector3.left : i == 2 ? Vector3.forward : Vector3.back) * radius;
                    if (Probe(feet + offset, lift, bounds.min.y, out var candidate) && candidate.distance < nearest)
                    { support = candidate; nearest = candidate.distance; found = true; }
                }
            }
            if (!found) return ImpactKind.Default;
            ground = support.collider;
            // Ignore triggers for support, then test a point just above the supporting surface for water.
            // A bridge/crate above a water volume therefore retains its own dry material.
            Vector3 waterPoint = new Vector3(feet.x, support.point.y + .04f, feet.z);
            int count = Physics.OverlapSphereNonAlloc(waterPoint, .025f, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var volume = overlaps[i];
                if (!volume.isTrigger || volume.transform.IsChildOf(owner)) continue;
                var surface = volume.GetComponentInParent<ImpactSurface>();
                if (surface != null && surface.Kind == ImpactKind.Water &&
                    (volume.ClosestPoint(waterPoint) - waterPoint).sqrMagnitude < .000001f)
                    return ImpactKind.Water;
            }
            return ImpactSurfaceResolver.Resolve(support);
        }

        private bool Probe(Vector3 feet, float lift, float footHeight, out RaycastHit support)
        {
            support = default;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * lift, Vector3.down, hits,
                lift + .3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider.transform.IsChildOf(owner) || hit.normal.y < .35f ||
                    hit.point.y > footHeight + .1f || hit.distance >= nearest) continue;
                support = hit;
                nearest = hit.distance;
            }
            return support.collider != null;
        }
    }
}
