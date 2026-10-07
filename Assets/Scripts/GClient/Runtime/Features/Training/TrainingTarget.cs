using System.Collections.Generic;
using OperationBlacktide.Client.Features.Lobby;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Training
{
    /// <summary>Reusable target for bullets, blasts and fire. Defeated targets reset after three seconds.</summary>
    public sealed class TrainingTarget : MonoBehaviour
    {
        internal static readonly HashSet<TrainingTarget> ActiveTargets = new HashSet<TrainingTarget>();
        internal Renderer[] Surfaces => surfaces;
        [SerializeField] private float maxHealth = 100;
        private Renderer[] surfaces;
        private Collider[] colliders;
        private MaterialPropertyBlock tint;
        private readonly List<SurfaceColor> surfaceColors = new List<SurfaceColor>();
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private float lastHit = float.NegativeInfinity, restoreAt;
        public LobbyTeam Team { get; private set; }
        public float Health { get; private set; }
        public float HealthFraction => Health / maxHealth;
        public bool IsAlive => Health > 0;

        private struct SurfaceColor
        {
            public Renderer Renderer;
            public int MaterialIndex;
            public Color Color;
        }

        private void Awake()
        {
            tint = new MaterialPropertyBlock();
            Health = maxHealth;
            surfaces = GetComponentsInChildren<Renderer>();
            colliders = GetComponentsInChildren<Collider>();
            // Keep every skin/equipment material's original color when the hit flash fades.
            foreach (var surface in surfaces)
            {
                var materials = surface.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    if (materials[i] != null && materials[i].HasProperty(BaseColor))
                        surfaceColors.Add(new SurfaceColor { Renderer = surface, MaterialIndex = i,
                            Color = materials[i].GetColor(BaseColor) });
            }
        }

        private void OnEnable() { ActiveTargets.Add(this); }
        private void OnDisable() { ActiveTargets.Remove(this); }

        public float ApplyDamage(float amount)
        {
            if (!IsAlive || amount <= 0) return 0;
            float damage = Mathf.Min(Health, amount);
            Health -= damage; lastHit = Time.time;
            if (!IsAlive)
            {
                restoreAt = Time.time + 3;
                foreach (var surface in surfaces) surface.enabled = false;
                foreach (var collider in colliders) collider.enabled = false;
            }
            return damage;
        }

        private void Update()
        {
            if (!IsAlive && Time.time >= restoreAt)
            {
                Health = maxHealth;
                foreach (var surface in surfaces) surface.enabled = true;
                foreach (var collider in colliders) collider.enabled = true;
            }
            float flash = Mathf.Clamp01(1 - (Time.time - lastHit) / .12f);
            foreach (var surface in surfaceColors)
            {
                surface.Renderer.GetPropertyBlock(tint, surface.MaterialIndex);
                var highlight = new Color(1, 1, 1, surface.Color.a);
                tint.SetColor(BaseColor, Color.Lerp(surface.Color, highlight, flash));
                surface.Renderer.SetPropertyBlock(tint, surface.MaterialIndex);
            }
        }

        public static void CreateRange(Vector3 spawn, LobbyLoadoutCatalog catalog, LobbyTeam playerTeam)
        {
            LobbyTeam targetTeam = playerTeam == LobbyTeam.T ? LobbyTeam.CT : LobbyTeam.T;
            string agentId = targetTeam == LobbyTeam.CT ? "ctm_sas" : "tm_phoenix";
            string weaponId = targetTeam == LobbyTeam.CT ? "weapon_rif_m4a1_silencer" : "weapon_rif_ak47";
            var agent = catalog != null ? catalog.Agent(agentId) : null;
            var weapon = catalog != null ? catalog.Weapon(weaponId) : null;
            var idle = Resources.Load<RuntimeAnimatorController>("Training/TrainingLocomotion");
            if (agent == null || agent.prefab == null || agent.team != targetTeam ||
                weapon == null || weapon.prefab == null || idle == null)
            {
                Debug.LogError("[Training] Missing opposing-team target character, weapon or animation assets.");
                return;
            }
            // Use actual floor/clearance probes so targets cannot appear inside a map wall or above a roof.
            for (int i = 0; i < 3; i++)
            {
                Vector3 candidate = spawn + new Vector3((i - 1) * 2.5f, 2, 6 + i * 2);
                if (!Physics.Raycast(candidate, Vector3.down, out var floor, 4, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore) || floor.normal.y < .8f) continue;
                Vector3 basePoint = floor.point + Vector3.up * .04f;
                if (Physics.CheckCapsule(basePoint + Vector3.up * .4f, basePoint + Vector3.up * 1.6f,
                    .35f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var root = new GameObject("Practice Target " + (i + 1) + " (" + targetTeam + ")");
                root.transform.position = basePoint;
                root.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(spawn - basePoint, Vector3.up));
                var visual = new GameObject("Character Visual");
                visual.transform.SetParent(root.transform, false);
                var appearance = visual.AddComponent<LobbyLoadoutPreview>();
                appearance.Show(catalog, agentId, weaponId);
                var animator = appearance.Animator;
                animator.runtimeAnimatorController = idle;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.applyRootMotion = false;
                animator.Rebind();
                animator.SetBool("Grounded", true);
                animator.SetFloat("Speed", 0);
                animator.SetFloat("MoveZ", 1);
                animator.Update(0);

                // Invisible hit volumes retain bullet, melee and throwable damage handling.
                var body = new GameObject("Torso");
                body.transform.SetParent(root.transform, false);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.center = Vector3.up * .77f;
                capsule.height = 1.45f; capsule.radius = .27f;
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                var sphere = head.AddComponent<SphereCollider>();
                sphere.center = Vector3.up * 1.6f; sphere.radius = .16f;
                // Add last: Awake must discover both the character renderers and hit volumes.
                root.AddComponent<TrainingTarget>().Team = targetTeam;
            }
        }
    }
}
