using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Bounded pools for tracers and physical-looking casings, with shared imported materials.</summary>
    public sealed class TrainingWeaponEffects : MonoBehaviour
    {
        private const int PoolSize = 32;
        [Header("Shot visibility")]
        [SerializeField, Min(.01f)] private float tracerWidth = .12f;
        [SerializeField, Min(.1f)] private float tracerLength = 3.5f;
        [SerializeField, Min(1)] private float tracerSpeed = 150f;
        [SerializeField, Min(.01f)] private float tracerFadeTime = .12f;
        [SerializeField, Min(1)] private float tracerBrightness = 3.5f;
        private sealed class ShotVisual
        {
            public LineRenderer tracer;
            public Transform casing;
            public Vector3 velocity, spin;
            public Vector3 origin, direction;
            public float born, distance, tracerUntil, casingUntil;
        }
        private readonly ShotVisual[] pool = new ShotVisual[PoolSize];
        private int next;
        private Transform weapon, root;
        private TrainingWeaponDefinition definition;
        private ParticleSystem flash, impacts;
        private TrainingBulletHoles bulletHoles;
        private AudioSource audioSource;

        public void Rebind(TrainingWeaponDefinition data, Transform gun) { definition = data; weapon = gun; }

        public void Initialize(TrainingWeaponCatalog assets, TrainingWeaponDefinition data, Transform gun)
        {
            definition = data; weapon = gun;
            root = new GameObject("Training Shot Effects").transform;
            // Keep world-space effects out of the moving character hierarchy; destroy them with this owner.
            flash = CreateParticles("Muzzle Flash", root, assets.flashMaterial, .09f, 0, 0, 12);
            impacts = CreateParticles("Bullet Impacts", root, assets.flashMaterial, .28f, 1.8f, .5f, 128);
            bulletHoles = root.gameObject.AddComponent<TrainingBulletHoles>();
            bulletHoles.Initialize(assets.bulletHoleMaterials);
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false; audioSource.spatialBlend = 0; audioSource.volume = .55f;
            for (int i = 0; i < pool.Length; i++)
            {
                var slot = pool[i] = new ShotVisual();
                var tracer = new GameObject("Bullet Tracer").AddComponent<LineRenderer>();
                tracer.transform.SetParent(root, false);
                tracer.sharedMaterial = assets.tracerMaterial;
                tracer.positionCount = 2; tracer.useWorldSpace = true;
                tracer.startWidth = tracerWidth; tracer.endWidth = tracerWidth * .5f;
                var brightness = new MaterialPropertyBlock();
                brightness.SetFloat("_Intensity", tracerBrightness);
                tracer.SetPropertyBlock(brightness);
                tracer.shadowCastingMode = ShadowCastingMode.Off; tracer.receiveShadows = false;
                tracer.enabled = false; slot.tracer = tracer;
                var shell = new GameObject("Ejected Casing");
                shell.transform.SetParent(root, false);
                shell.AddComponent<MeshFilter>().sharedMesh = assets.casingMesh;
                var renderer = shell.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = assets.casingMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                slot.casing = shell.transform;
                shell.SetActive(false);
            }
        }

        private static ParticleSystem CreateParticles(string name, Transform parent, Material material,
            float lifetime, float speed, float gravity, int capacity)
        {
            var ps = new GameObject(name).AddComponent<ParticleSystem>();
            ps.transform.SetParent(parent, false);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = true; main.duration = 1f;
            main.startLifetime = lifetime; main.startSpeed = speed; main.startSize = .12f;
            main.gravityModifier = gravity; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = capacity;
            var emission = ps.emission; emission.enabled = false;
            var shape = ps.shape; shape.enabled = false;
            var color = ps.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
            var brightness = new MaterialPropertyBlock();
            brightness.SetFloat("_Intensity", 2f);
            renderer.SetPropertyBlock(brightness);
            ps.Play();
            return ps;
        }

        public void Play(TrainingShot shot, bool cartridgeEffects = true)
        {
            var slot = pool[next++ % PoolSize];
            float now = Time.time;
            slot.origin = shot.origin;
            slot.direction = (shot.end - shot.origin).normalized;
            slot.distance = Vector3.Distance(shot.origin, shot.end);
            slot.born = now;
            slot.tracer.SetPosition(0, shot.origin);
            slot.tracer.SetPosition(1, shot.origin + slot.direction * Mathf.Min(tracerLength, slot.distance));
            slot.tracer.startColor = slot.tracer.endColor = definition.IsTaser ? new Color(.35f, .75f, 1f) : new Color(1, .9f, .6f, 1f);
            slot.tracer.enabled = true; slot.tracerUntil = now + slot.distance / tracerSpeed + tracerFadeTime;
            if (cartridgeEffects && !definition.IsTaser) flash.Emit(new ParticleSystem.EmitParams {
                position = shot.origin, velocity = Vector3.zero,
                startSize = definition.silenced ? .28f : .55f, rotation = Random.Range(0, 360)
            }, 1);
            if (shot.hasHit)
            {
                if (!definition.IsTaser) bulletHoles.Place(shot.hit);
                for (int i = 0; i < 12; i++)
                    impacts.Emit(new ParticleSystem.EmitParams {
                        position = shot.end + shot.hit.normal * .015f,
                        velocity = (shot.hit.normal + Random.insideUnitSphere * .7f) * Random.Range(.8f, 2.8f),
                        startSize = Random.Range(.075f, .16f), startColor = new Color(1, .8f, .45f)
                    }, 1);
            }
            slot.casing.gameObject.SetActive(cartridgeEffects && !definition.IsTaser);
            slot.casing.SetPositionAndRotation(weapon.TransformPoint(definition.ejectPosition), Random.rotation);
            slot.velocity = transform.right * Random.Range(1.1f, 1.8f) + Vector3.up * Random.Range(1.2f, 2f);
            slot.spin = Random.onUnitSphere * 720;
            slot.casingUntil = now + 1.6f;
            if (cartridgeEffects && definition.shots.Length > 0)
                audioSource.PlayOneShot(definition.shots[Random.Range(0, definition.shots.Length)]);
        }

        private void Update()
        {
            float now = Time.time, delta = Time.deltaTime;
            foreach (var slot in pool)
            {
                if (slot == null) continue;
                if (slot.tracer.enabled)
                {
                    if (now >= slot.tracerUntil) slot.tracer.enabled = false;
                    else
                    {
                        float head = Mathf.Min(slot.distance, (now - slot.born) * tracerSpeed + tracerLength);
                        slot.tracer.SetPosition(0, slot.origin + slot.direction * Mathf.Max(0, head - tracerLength));
                        slot.tracer.SetPosition(1, slot.origin + slot.direction * head);
                        var color = new Color(1, .9f, .6f, Mathf.Clamp01((slot.tracerUntil - now) / tracerFadeTime));
                        slot.tracer.startColor = slot.tracer.endColor = color;
                    }
                }
                if (!slot.casing.gameObject.activeSelf) continue;
                if (now >= slot.casingUntil) { slot.casing.gameObject.SetActive(false); continue; }
                slot.velocity += Physics.gravity * delta;
                Vector3 step = slot.velocity * delta;
                if (Physics.Raycast(slot.casing.position, step.normalized, out var hit, step.magnitude + .008f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    slot.casing.position = hit.point + hit.normal * .008f;
                    slot.velocity = Vector3.Reflect(slot.velocity, hit.normal) * .3f;
                    slot.spin *= .4f;
                }
                else slot.casing.position += step;
                slot.casing.Rotate(slot.spin * delta, Space.World);
            }
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
            if (audioSource != null) Destroy(audioSource);
        }
    }
}
