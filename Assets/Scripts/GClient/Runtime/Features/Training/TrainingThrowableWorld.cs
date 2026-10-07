using System.Collections.Generic;
using OperationBlacktide.Client.Features.Lobby;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Scene-owned local simulation. Switching equipment never removes live grenades or fields.</summary>
    public sealed class TrainingThrowableWorld : MonoBehaviour
    {
        private sealed class Projectile
        {
            public TrainingThrowableDefinition data;
            public Transform visual;
            public Vector3 position, velocity;
            public float age, lastBounce = -1;
        }
        private sealed class Field
        {
            public TrainingThrowableDefinition data;
            public Vector3 position;
            public float age, nextDamage;
            public TrainingThrowableEffects effect;
            public ParticleSystem flames;
            public float nextFlame;
            public readonly List<Vector3> cells = new List<Vector3>();
        }
        private readonly List<Projectile> projectiles = new List<Projectile>();
        private readonly List<Field> fields = new List<Field>();
        private readonly List<TrainingThrowableEffects> effects = new List<TrainingThrowableEffects>();
        private readonly HashSet<int> damaged = new HashSet<int>();
        private TrainingCharacterController player;
        private TrainingWeaponController weapon;
        private TrainingThrowableCatalog catalog;
        private float flashRemaining, flashDuration, flashPeak;
        private AudioSource ringing;
        public int Detonations { get; private set; }
        public int BounceCount { get; private set; }
        public int ProjectileCount => projectiles.Count;
        public int FieldCount => fields.Count;
        public bool HasCapacity => projectiles.Count + fields.Count < 16;
        public float FlashOpacity => flashDuration > 0 ? flashPeak * Mathf.Clamp01(flashRemaining / (flashDuration * .65f)) : 0;
        public float SmokeOpacity => player == null ? 0 : SmokeAt(player.transform.position + Vector3.up * 1.5f);

        public void Initialize(TrainingCharacterController owner, TrainingThrowableCatalog assets)
        {
            player = owner; catalog = assets; weapon = owner.GetComponent<TrainingWeaponController>();
            ringing = gameObject.AddComponent<AudioSource>(); ringing.playOnAwake = false;
            ringing.clip = assets.ringing; ringing.loop = true; ringing.spatialBlend = 0; ringing.volume = 0;
        }

        private void Update()
        {
            bool active = player != null && player.InputEnabled && Application.isFocused && Time.timeScale > 0;
            foreach (var effect in effects) if (effect != null) effect.SetPaused(!active);
            if (!active) { if (ringing != null) ringing.Pause(); return; }
            if (ringing != null && flashRemaining > 0) ringing.UnPause();
            Simulate(Mathf.Min(Time.deltaTime, .1f));
        }

        public bool Launch(TrainingThrowableDefinition data, Vector3 position, Vector3 velocity)
        {
            if (!HasCapacity || data == null || data.model == null) return false;
            var root = new GameObject("Thrown " + data.id); root.transform.SetParent(transform, false);
            var model = Instantiate(data.model, root.transform, false);
            foreach (var collider in model.GetComponentsInChildren<Collider>()) collider.enabled = false;
            foreach (var child in model.GetComponentsInChildren<Transform>()) child.gameObject.layer = 2;
            var bounds = LobbyLoadoutPreview.StaticMeshBounds(model);
            model.transform.position += root.transform.position - bounds.center;
            root.transform.position = position;
            projectiles.Add(new Projectile { data = data, visual = root.transform, position = position, velocity = velocity });
            var trail = root.AddComponent<TrailRenderer>(); trail.sharedMaterial = catalog.trajectory;
            trail.time = .18f; trail.startWidth = .035f; trail.endWidth = .005f;
            trail.startColor = new Color(1, .76f, .3f, .55f); trail.endColor = new Color(1, .55f, .2f, 0);
            trail.minVertexDistance = .06f;
            if (data.kind == TrainingThrowableKind.Fire)
            {
                var wick = Effect("Burning Fuse", position, data.fuse + .2f);
                wick.transform.SetParent(root.transform, true);
                wick.Particles(catalog.fire, .4f, .4f, Color.white, true, 12);
            }
            return true;
        }

        public void Simulate(float dt)
        {
            if (dt <= 0) return;
            for (int i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i]; bool detonate = false;
                for (float remaining = dt; remaining > .00001f;)
                {
                    float step = Mathf.Min(TrainingThrowablePhysics.Step, remaining); remaining -= step; p.age += step;
                    float speed = p.velocity.magnitude;
                    bool collision = TrainingThrowablePhysics.Advance(ref p.position, ref p.velocity, step, out var hit);
                    if (collision && speed > 1 && p.age - p.lastBounce > .12f)
                    { OneShot(p.data.bounce, p.position, Mathf.Clamp(speed / 12, .12f, .65f)); p.lastBounce = p.age; BounceCount++; }
                    bool fireImpact = p.data.kind == TrainingThrowableKind.Fire && collision && hit.normal.y > .65f;
                    if (fireImpact || p.age >= p.data.fuse) { detonate = true; break; }
                }
                p.visual.position = p.position;
                if (p.velocity.sqrMagnitude > .01f) p.visual.Rotate(new Vector3(270, 160, 95) * dt, Space.World);
                if (detonate || p.position.y < -200)
                {
                    if (detonate) Detonate(p.data, p.position);
                    p.visual.gameObject.SetActive(false); Destroy(p.visual.gameObject); projectiles.RemoveAt(i);
                }
            }
            for (int i = fields.Count - 1; i >= 0; i--)
            {
                var field = fields[i]; field.age += dt;
                if (field.data.kind == TrainingThrowableKind.Fire)
                {
                    bool extinguished = SmokeAt(field.position + Vector3.up) > .2f;
                    if (extinguished) field.age = field.data.duration;
                    if (field.age < field.data.duration && field.age >= field.nextDamage)
                    { DamageFire(field); field.nextDamage = field.age + .25f; }
                    if (field.age < field.data.duration - .5f && field.age >= field.nextFlame)
                    { EmitFlames(field); field.nextFlame = field.age + .23f; }
                }
                if (field.age >= field.data.duration)
                {
                    OneShot(field.data.finish, field.position, .45f);
                    if (field.effect != null) { effects.Remove(field.effect); field.effect.gameObject.SetActive(false); Destroy(field.effect.gameObject); }
                    fields.RemoveAt(i);
                }
            }
            flashRemaining = Mathf.Max(0, flashRemaining - dt);
            if (ringing != null) { ringing.volume = FlashOpacity * .2f; if (flashRemaining <= 0) ringing.Stop(); }
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                var effect = effects[i];
                if (effect == null) { effects.RemoveAt(i); continue; }
                effect.Advance(dt);
                if (effect.Finished) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); effects.RemoveAt(i); }
            }
        }

        public void Detonate(TrainingThrowableDefinition data, Vector3 position)
        {
            Detonations++;
            if (data.detonate.Length > 0) OneShot(data.detonate[Random.Range(0, data.detonate.Length)], position);
            if (data.kind == TrainingThrowableKind.Smoke || data.kind == TrainingThrowableKind.Fire)
            {
                // Fire must find a supporting floor; an airborne timeout does not create a floating damage zone.
                float probe = data.kind == TrainingThrowableKind.Fire ? 1.2f : 4;
                if (Physics.Raycast(position + Vector3.up * .15f, Vector3.down, out var floor, probe,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && floor.normal.y > .65f)
                    position = floor.point + Vector3.up * .04f;
                else if (data.kind == TrainingThrowableKind.Fire) { Burst(position, false); return; }
                CreateField(data, position); return;
            }
            Burst(position, data.kind == TrainingThrowableKind.Flash);
            if (data.kind == TrainingThrowableKind.HighExplosive) DamageExplosion(data, position);
            else ApplyFlash(position, data.radius, data.duration);
        }

        private void Burst(Vector3 position, bool flash)
        {
            var fx = Effect(flash ? "Flash Burst" : "HE Blast", position + Vector3.up * .25f, flash ? .5f : 2);
            fx.Particles(catalog.explosion, flash ? 3.5f : 6f, flash ? .16f : .6f, flash ? Color.white : new Color(1, .48f, .1f), false, 3);
            fx.Glow(flash ? Color.white : new Color(1, .45f, .1f), flash ? 8 : 5, 9);
            if (!flash)
            {
                fx.Particles(catalog.smoke, 3.5f, 1.7f, new Color(.65f, .6f, .53f, .55f), false, 8, .7f);
                var sparks = fx.Particles(catalog.trajectory, .16f, .65f, new Color(1, .6f, .18f), false, 0, .1f, false);
                var main = sparks.main; main.startSpeed = new ParticleSystem.MinMaxCurve(4, 9); main.gravityModifier = .6f;
                sparks.Emit(28);
            }
        }

        private void CreateField(TrainingThrowableDefinition data, Vector3 position)
        {
            if (fields.Count >= 12) return;
            var field = new Field { data = data, position = position };
            field.effect = Effect(data.kind == TrainingThrowableKind.Smoke ? "Smoke Cloud" : "Ground Fire", position, data.duration + .1f);
            if (data.kind == TrainingThrowableKind.Smoke)
            {
                var smoke = field.effect.Particles(catalog.smoke, 4.2f, 4, new Color(.82f, .82f, .8f, .97f), true, 24, 2.4f);
                smoke.transform.localPosition = Vector3.up * 1.3f;
                smoke.Emit(38);
            }
            else
            {
                for (int z = -3; z <= 3; z++) for (int x = -3; x <= 3; x++)
                {
                    Vector3 offset = new Vector3(x, 0, z);
                    if (offset.magnitude > data.radius - .25f) continue;
                    Vector3 candidate = position + offset;
                    if (!TrainingThrowablePhysics.HasLineOfSight(position + Vector3.up * .3f, candidate + Vector3.up * .3f)) continue;
                    if (!Physics.Raycast(candidate + Vector3.up * .7f, Vector3.down, out var floor, 1.3f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || floor.normal.y < .7f) continue;
                    Vector3 point = floor.point + Vector3.up * .08f;
                    if (Mathf.Abs(point.y - position.y) > .5f) continue;
                    field.cells.Add(point);
                }
                // One renderer per fire field, with particles emitted only on the probed floor cells.
                field.flames = field.effect.Particles(catalog.fire, 2.1f, .95f, Color.white, false, 0);
                EmitFlames(field);
                field.effect.Sound(data.loop, .48f, true);
                field.effect.Glow(new Color(1, .42f, .08f), 2.5f, 8);
                Burst(position, false);
            }
            fields.Add(field);
        }

        private static void EmitFlames(Field field)
        {
            if (field.flames == null) return;
            foreach (var cell in field.cells)
                field.flames.Emit(new ParticleSystem.EmitParams { position = cell + Vector3.up * .24f }, 1);
        }

        private void DamageExplosion(TrainingThrowableDefinition data, Vector3 center)
        {
            damaged.Clear();
            foreach (var collider in Physics.OverlapSphere(center, data.radius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<TrainingTarget>();
                var vitals = collider.GetComponentInParent<TrainingVitals>();
                Transform root = target != null ? target.transform : vitals != null ? vitals.transform : null;
                if (root == null || damaged.Contains(root.GetInstanceID())) continue;
                Vector3 point = collider.bounds.center;
                if (!TrainingThrowablePhysics.HasLineOfSight(center, point, root)) continue;
                damaged.Add(root.GetInstanceID());
                float damage = data.damage * Mathf.Pow(Mathf.Clamp01(1 - Vector3.Distance(center, collider.ClosestPoint(center)) / data.radius), 1.5f);
                if (target != null) { float dealt = target.ApplyDamage(damage); if (dealt > 0) weapon?.NotifyThrowableHit(target, dealt, point); }
                else vitals.ApplyDamage(Mathf.RoundToInt(damage));
            }
        }

        private void DamageFire(Field field)
        {
            damaged.Clear();
            foreach (var collider in Physics.OverlapSphere(field.position, field.data.radius + 1, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var target = collider.GetComponentInParent<TrainingTarget>(); var vitals = collider.GetComponentInParent<TrainingVitals>();
                Transform root = target != null ? target.transform : vitals != null ? vitals.transform : null;
                if (root == null || damaged.Contains(root.GetInstanceID())) continue;
                Vector3 feet = root.position;
                foreach (var cell in field.cells)
                {
                    if (Mathf.Abs(feet.y - cell.y) > .9f || Vector3.ProjectOnPlane(feet - cell, Vector3.up).sqrMagnitude > 1.05f) continue;
                    if (!TrainingThrowablePhysics.HasLineOfSight(cell + Vector3.up * .25f, collider.bounds.center, root)) continue;
                    damaged.Add(root.GetInstanceID());
                    float damage = field.data.damage * .25f;
                    if (target != null) { float dealt = target.ApplyDamage(damage); if (dealt > 0) weapon?.NotifyThrowableHit(target, dealt, collider.bounds.center); }
                    else vitals.ApplyDamage(Mathf.RoundToInt(damage), true);
                    break;
                }
            }
        }

        public float FlashExposure(Vector3 position, float radius)
        {
            if (player == null) return 0;
            Vector3 eyes = player.transform.position + Vector3.up * 1.5f, offset = position - eyes;
            float distance = offset.magnitude;
            if (distance >= radius || !TrainingThrowablePhysics.HasLineOfSight(position, eyes, player.transform)) return 0;
            float facing = Vector3.Dot(player.transform.forward, offset.normalized);
            float strength = Mathf.Lerp(.3f, 1, Mathf.InverseLerp(-.3f, .65f, facing));
            return strength * Mathf.Clamp01(1 - distance / radius) * (IsSmokeOccluded(position, eyes) ? .2f : 1);
        }

        public void ApplyFlash(Vector3 position, float radius, float duration)
        {
            float exposure = FlashExposure(position, radius);
            if (exposure <= .01f) return;
            float remaining = duration * exposure;
            if (remaining >= flashRemaining) { flashDuration = remaining; flashRemaining = remaining; flashPeak = Mathf.Clamp01(exposure * 1.6f); }
            if (ringing != null && !ringing.isPlaying) ringing.Play();
        }

        public void ResetSenses() { flashRemaining = flashDuration = 0; if (ringing != null) ringing.Stop(); }

        private static float Density(Field field) => Mathf.Min(Mathf.Clamp01(field.age / .6f), Mathf.Clamp01((field.data.duration - field.age) / 2));

        public float SmokeAt(Vector3 point)
        {
            float result = 0;
            foreach (var field in fields)
            {
                if (field.data.kind != TrainingThrowableKind.Smoke) continue;
                Vector3 relative = point - (field.position + Vector3.up * 1.3f); relative.y *= 1.7f;
                float depth = Mathf.Clamp01((field.data.radius - relative.magnitude) / 1.2f);
                result = Mathf.Max(result, depth * Density(field) * .98f);
            }
            return result;
        }

        public bool IsSmokeOccluded(Vector3 from, Vector3 to)
        {
            foreach (var field in fields)
            {
                if (field.data.kind != TrainingThrowableKind.Smoke || Density(field) < .2f) continue;
                Vector3 center = field.position + Vector3.up * 1.3f;
                Vector3 start = from - center, end = to - center; start.y *= 1.7f; end.y *= 1.7f;
                Vector3 line = end - start;
                float t = line.sqrMagnitude > .0001f ? Mathf.Clamp01(-Vector3.Dot(start, line) / line.sqrMagnitude) : 0;
                if ((start + line * t).magnitude < field.data.radius * .95f) return true;
            }
            return false;
        }

        public float ClipVisibility(Vector3 from, Vector3 to)
        {
            if (!IsSmokeOccluded(from, to)) return 1;
            float low = 0, high = 1;
            for (int i = 0; i < 9; i++) { float mid = (low + high) * .5f; if (IsSmokeOccluded(from, Vector3.Lerp(from, to, mid))) high = mid; else low = mid; }
            return low;
        }

        public void OneShot(AudioClip clip, Vector3 point, float volume = .75f)
        {
            if (clip == null) return;
            Effect("Grenade " + clip.name, point, clip.length + .1f).Sound(clip, volume);
        }
        private TrainingThrowableEffects Effect(string label, Vector3 position, float duration)
        {
            var effect = TrainingThrowableEffects.Create(transform, label, position, duration); effects.Add(effect); return effect;
        }
    }
}
