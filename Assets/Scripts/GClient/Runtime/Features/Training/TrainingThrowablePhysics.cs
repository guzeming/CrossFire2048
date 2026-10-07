using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>The same swept-sphere integrator drives both preview and live projectiles.</summary>
    public static class TrainingThrowablePhysics
    {
        public const float Radius = .09f, Gravity = -12f, Step = .02f, MaxRange = 18f;

        public static Vector3 LaunchVelocity(Vector3 origin, Vector3 aim)
        {
            Vector3 flat = Vector3.ProjectOnPlane(aim - origin, Vector3.up);
            flat = Vector3.ClampMagnitude(flat, MaxRange);
            float height = Mathf.Clamp(aim.y - origin.y, -5, 5);
            float time = .65f + flat.magnitude * .04f;
            return flat / time + Vector3.up * (height / time - .5f * Gravity * time);
        }

        public static bool Advance(ref Vector3 position, ref Vector3 velocity, float dt, out RaycastHit contact)
        {
            contact = default;
            // Rest only on a still-present floor. A stationary grenade in mid-air must fall.
            if (velocity.sqrMagnitude < .025f && Physics.SphereCast(position, Radius * .95f, Vector3.down,
                out var floor, .035f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && floor.normal.y > .65f)
            { velocity = Vector3.zero; return false; }
            velocity += Vector3.up * Gravity * dt;
            float remaining = dt;
            bool collided = false;
            for (int i = 0; i < 3 && remaining > .0001f; i++)
            {
                Vector3 motion = velocity * remaining;
                float distance = motion.magnitude;
                if (distance < .00001f) break;
                if (!Physics.SphereCast(position, Radius, motion / distance, out var hit, distance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) { position += motion; break; }
                collided = true; contact = hit;
                position += motion.normalized * Mathf.Max(0, hit.distance - .003f);
                position += hit.normal * .004f;
                remaining *= Mathf.Clamp01(1 - hit.distance / distance);
                Vector3 normalSpeed = hit.normal * Vector3.Dot(velocity, hit.normal);
                velocity = (velocity - normalSpeed) * .72f - normalSpeed * .43f;
                if (hit.normal.y > .65f && velocity.sqrMagnitude < 1) { velocity = Vector3.zero; break; }
            }
            return collided;
        }

        public static bool HasLineOfSight(Vector3 origin, Vector3 destination, Transform target = null)
        {
            Vector3 offset = destination - origin;
            if (offset.sqrMagnitude < .0001f) return true;
            return !Physics.Raycast(origin, offset.normalized, out var hit, offset.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                || (target != null && (hit.transform == target || hit.transform.IsChildOf(target)));
        }
    }
}
