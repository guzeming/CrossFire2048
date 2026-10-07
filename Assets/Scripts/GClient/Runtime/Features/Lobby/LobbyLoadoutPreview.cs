using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OperationBlacktide.Client.Features.Lobby
{
    public sealed class LobbyLoadoutPreview : MonoBehaviour
    {
        [SerializeField] private GameObject originalModel;
        private LobbyLoadoutCatalog catalog;
        private GameObject actor, weapon, offhandWeapon, displayObject;
        private Animator animator;
        private AnimatorOverrideController animationOverride;
        private string agentId, weaponId;
        public string AgentId => agentId;
        public string WeaponId => weaponId;
        public Animator Animator => animator;
        public Transform EquippedWeapon => weapon != null ? weapon.transform : null;

        private void Awake()
        {
            if (originalModel == null && transform.childCount > 0) originalModel = transform.GetChild(0).gameObject;
        }

        public void Show(LobbyLoadoutCatalog assets, string agent, string item)
        {
            catalog = assets;
            var definition = catalog.Agent(agent);
            var equipment = catalog.Weapon(item);
            if (definition == null || definition.prefab == null || equipment == null || equipment.prefab == null) return;
            if (originalModel != null) originalModel.SetActive(false);
            if (agentId != agent || actor == null)
            {
                ClearWeapon();
                Release(actor);
                actor = Instantiate(definition.prefab, transform, false);
                actor.name = "SelectedAgent_" + agent;
                animator = actor.GetComponentInChildren<Animator>();
                agentId = agent;
                weaponId = null;
            }
            actor.SetActive(equipment.held);
            if (weaponId == item && (weapon != null || displayObject != null)) return;
            ClearWeapon();
            weaponId = item;
            if (!equipment.held)
            {
                displayObject = Instantiate(equipment.prefab, transform, false);
                // Armor and utility equipment use a centered standalone display.
                var bounds = StaticMeshBounds(displayObject);
                float scale = 1.1f / Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                displayObject.transform.localScale *= scale;
                bounds = StaticMeshBounds(displayObject);
                displayObject.transform.position += transform.TransformPoint(new Vector3(0, 1.2f, 0)) - bounds.center;
                return;
            }
            var hand = actor.GetComponentsInChildren<Transform>().First(t => t.name == "hand_R");
            weapon = Instantiate(equipment.prefab, hand, false);
            weapon.name = "Equipped_" + item;
            if (equipment.offhandPrefab != null)
            {
                var leftHand = actor.GetComponentsInChildren<Transform>().First(t => t.name == "hand_L");
                offhandWeapon = Instantiate(equipment.offhandPrefab, leftHand, false);
                offhandWeapon.name = "Equipped_Offhand_" + item;
            }
            var pose = catalog.Pose(equipment.profile);
            var basis = catalog.Pose("m4");
            animationOverride = new AnimatorOverrideController(catalog.controller);
            animationOverride.ApplyOverrides(new List<KeyValuePair<AnimationClip, AnimationClip>>
            {
                new KeyValuePair<AnimationClip, AnimationClip>(basis.idle, pose.idle),
                new KeyValuePair<AnimationClip, AnimationClip>(basis.inspect, pose.inspect)
            });
            animator.runtimeAnimatorController = animationOverride;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
            animator.Rebind();
            animator.Play("Idle", 0, 0);
            animator.Update(0);
        }

        public void Inspect()
        {
            if (actor != null && actor.activeSelf && animator != null) animator.CrossFadeInFixedTime("Inspect", .25f, 0, 0);
        }

        // Mesh-local bounds are valid immediately after instantiation, even before Unity updates renderer bounds.
        public static Bounds StaticMeshBounds(GameObject root)
        {
            var result = new Bounds(); bool first = true;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var bounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = filter.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1)));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; }
                    else result.Encapsulate(point);
                }
            }
            return result;
        }

        private void ClearWeapon()
        {
            Release(weapon); Release(offhandWeapon); Release(displayObject);
            weapon = offhandWeapon = displayObject = null;
            if (animator != null) animator.runtimeAnimatorController = null;
            Release(animationOverride); animationOverride = null;
        }

        private static void Release(Object value)
        {
            if (value == null) return;
            if (value is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy() { Release(animationOverride); }
    }
}
