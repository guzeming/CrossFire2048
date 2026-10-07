#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SidearmVerification
{
    public static IEnumerator Run(TrainingSceneController scene, LobbyLoadoutCatalog loadout, TrainingWeaponCatalog catalog)
    {
        var camera = Camera.main;
        camera.GetComponent<TrainingCameraController>().enabled = false;
        var origin = new Vector3(0, 100, 0);
        camera.transform.position = origin + new Vector3(3, 6, 7);
        camera.transform.LookAt(origin + Vector3.up);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Sidearm Test Floor"; floor.transform.position = origin + Vector3.down * .1f;
        floor.transform.localScale = new Vector3(20, .2f, 20);
        var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetObject.name = "Sidearm Test Target"; targetObject.transform.position = origin + new Vector3(0, 1.3f, 5);
        targetObject.transform.localScale = new Vector3(1, 1.2f, .4f);
        var target = targetObject.AddComponent<TrainingTarget>();
        var player = Object.Instantiate(Resources.Load<TrainingCharacterController>("Training/TrainingPlayer"));
        player.Initialize(camera, origin, Quaternion.identity); player.enabled = false;
        var visual = new GameObject("Sidearm Appearance"); visual.transform.SetParent(player.transform, false);
        var appearance = visual.AddComponent<LobbyLoadoutPreview>();
        var motion = player.gameObject.AddComponent<TrainingCharacterAnimator>(); motion.enabled = false;
        var gun = player.gameObject.AddComponent<TrainingWeaponController>();
        double clock = 1000;

        foreach (var definition in catalog.weapons.Where(w => w.IsPistol || w.IsMelee))
        {
            gun.PrepareEquipmentChange();
            appearance.Show(loadout, definition.id.EndsWith("_t") ? "tm_phoenix" : "ctm_sas", definition.id);
            motion.Initialize(player, appearance.Animator);
            foreach (var child in player.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            gun.Initialize(player, appearance.EquippedWeapon, definition.id);
            var animator = appearance.Animator;
            var presentation = player.GetComponent<TrainingWeaponPresentation>();
            var right = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_R");
            var ankle = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "ankle_L");
            targetObject.transform.position = origin + new Vector3(0, 1.3f, definition.IsMelee ? 1.2f : definition.IsTaser ? 2 : 5);
            ResetTarget(target); Physics.SyncTransforms();
            player.Aim(camera.WorldToScreenPoint(targetObject.transform.position), 1);
            animator.Play("Locomotion", 0, 0); animator.Update(0);
            if (definition.IsDual)
                foreach (var mesh in player.GetComponentsInChildren<MeshFilter>().Where(m => m.name.StartsWith("WeaponMesh_")))
                {
                    Check(Vector3.Angle(mesh.transform.TransformDirection(Vector3.down), player.transform.forward) < 35,
                        "Dual pistol points away from the combat pose: " + mesh.name + " " + mesh.transform.TransformDirection(Vector3.down));
                    Check(Vector3.Dot(mesh.transform.TransformDirection(Vector3.forward), Vector3.up) > 0, "Dual pistol is upside down: " + mesh.name);
                }
            var mappings = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            ((AnimatorOverrideController)animator.runtimeAnimatorController).GetOverrides(mappings);
            Check(mappings.Single(p => p.Key.name == "idle_rifle").Value == definition.idleAnimation, "Wrong held pose " + definition.id);
            Check(mappings.Where(p => p.Key.name.EndsWith("_rifle")).All(p => p.Value != null && !p.Value.name.EndsWith("_rifle")), "Rifle locomotion leaked into " + definition.id);
            Capture(player, targetObject, "sidearm-" + definition.id + "-idle.png");
            animator.SetBool("Grounded", true); animator.SetFloat("Speed", 4.5f);
            animator.SetFloat("MoveX", 1); animator.SetFloat("MoveZ", 0); animator.Play("Locomotion", 0, .35f); animator.Update(0);
            var walkingFoot = ankle.position;
            presentation.UpdatePresentation(0, true);
            Check(Vector3.Distance(walkingFoot, ankle.position) < .001f, "Weapon hold layer changes locomotion feet.");
            if (definition.IsPistol)
                Check(animator.GetLayerWeight(animator.GetLayerIndex(TrainingWeaponPresentation.HoldLayer)) == 1, "Moving pistol grip correction is absent.");
            Capture(player, targetObject, "sidearm-" + definition.id + "-moving.png");
            animator.SetFloat("Speed", 0); animator.Play("Locomotion", 0, 0); presentation.UpdatePresentation(0, true);
            int attacks = presentation.FireAnimationsPlayed;
            int reloadSoundsBefore = presentation.ReloadSoundsPlayed;
            Vector3 handBefore = right.position;
            if (definition.IsMelee)
            {
                Check(!gun.CanFire && gun.CanAttack && gun.Ammo == null && !gun.TryReload(), "Knife uses firearm ammunition.");
                int started = gun.Melee.AttacksStarted;
                gun.Melee.ProcessInput(true, false, true);
                Check(gun.Melee.AttacksStarted == started, "Equip fires a held knife input.");
                gun.Melee.ProcessInput(false, false, true); gun.Melee.ProcessInput(true, false, true);
                Check(presentation.FireAnimationsPlayed == attacks + 1 && target.HealthFraction == 1, "Knife animation / windup missing.");
                presentation.UpdatePresentation(.2f, true); presentation.UpdatePresentation(0, true);
                Check(Vector3.Distance(handBefore, right.position) > .02f, "Knife slash does not move the hand.");
                Capture(player, targetObject, "sidearm-" + definition.id + "-slash.png");
                gun.Melee.Advance(.2f);
                Check(Mathf.Abs(target.HealthFraction - .65f) < .001f, "Knife failed to deal one light strike.");
                gun.Melee.Advance(5); Check(Mathf.Abs(target.HealthFraction - .65f) < .001f, "Knife deals damage every frame.");
                gun.Melee.ProcessInput(false, true, true); gun.Melee.Advance(.31f);
                Check(!target.IsAlive && !gun.IsScoped, "Knife right click did not perform heavy attack.");
                presentation.UpdatePresentation(.3f, true); presentation.UpdatePresentation(0, true);
                Capture(player, targetObject, "sidearm-" + definition.id + "-stab.png");
                gun.Melee.Advance(5); ResetTarget(target);
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position = origin + new Vector3(0, 1.2f, .5f); wall.transform.localScale = new Vector3(2, 2, .1f);
                Physics.SyncTransforms();
                gun.Melee.ProcessInput(true, false, true); gun.Melee.Advance(5);
                Check(target.HealthFraction == 1, "Knife hits through a wall.");
                wall.SetActive(false); Object.Destroy(wall); Physics.SyncTransforms();
                targetObject.transform.position = origin + new Vector3(0, 1.3f, 5); Physics.SyncTransforms();
                player.Aim(camera.WorldToScreenPoint(targetObject.transform.position), 1);
                gun.Melee.ProcessInput(true, false, true); gun.Melee.Advance(5);
                Check(target.HealthFraction == 1, "Knife hits outside melee range.");
                targetObject.transform.position = origin + new Vector3(0, 1.3f, 1.2f); Physics.SyncTransforms();
                player.Aim(camera.WorldToScreenPoint(targetObject.transform.position), 1);
                gun.Melee.ProcessInput(true, false, true); gun.PrepareEquipmentChange(); gun.Melee.Advance(5);
                Check(target.HealthFraction == 1 && !gun.Melee.IsSwinging, "Switching leaves a pending knife strike.");
            }
            else
            {
                Check(gun.CanFire && gun.Ammo != null, "Pistol has no combat profile " + definition.id);
                gun.ProcessTrigger(false, false, true, clock); gun.ProcessTrigger(true, true, true, clock);
                Check(presentation.FireAnimationsPlayed == attacks + 1 && gun.Ammo.Magazine == definition.magazineSize - 1, "Pistol does not fire " + definition.id);
                Check(target.HealthFraction < 1, "Pistol did not damage aimed target " + definition.id);
                presentation.UpdatePresentation(.07f, true); presentation.UpdatePresentation(0, true);
                Check(Vector3.Distance(handBefore, right.position) > .001f, "Pistol recoil has no motion " + definition.id);
                Capture(player, targetObject, "sidearm-" + definition.id + "-fire.png");
                player.GetComponent<TrainingAimLaser>().UpdateBeam(true);
                Check(player.GetComponentsInChildren<LineRenderer>().Any(l => l.name == "Aim Laser" && l.enabled), "Pistol aim laser missing.");
                if (definition.IsDual)
                {
                    Check(!gun.LastShotOffhand, "Dual pistols must start with the right hand.");
                    gun.ProcessTrigger(false, false, true, clock + 1); gun.ProcessTrigger(true, true, true, clock + 1);
                    Check(gun.LastShotOffhand, "Dual pistols do not alternate muzzles.");
                    presentation.UpdatePresentation(.07f, true); presentation.UpdatePresentation(0, true);
                    Check(animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex(TrainingWeaponPresentation.FireLayer)).IsName("AlternateFire"), "Dual left-hand animation missing.");
                    Capture(player, targetObject, "sidearm-elite-left-fire.png");
                }
                Check(gun.TryReload(), "Pistol reload / recharge unavailable.");
                var foot = ankle.position;
                gun.AdvanceReload(definition.reloadDuration * .45f); presentation.UpdatePresentation(0, true);
                Check(Vector3.Distance(foot, ankle.position) < .001f, "Pistol action alters feet.");
                if (!definition.IsTaser) Check(animator.GetLayerWeight(animator.GetLayerIndex(TrainingWeaponPresentation.ReloadLayer)) == 1, "Reload animation has no layer weight.");
                Check(presentation.ReloadSoundsPlayed > reloadSoundsBefore, "Reload sound missing.");
                Capture(player, targetObject, "sidearm-" + definition.id + "-reload.png");
                float progress = gun.Ammo.ReloadProgress;
                player.InputEnabled = false; gun.AdvanceReload(1); presentation.UpdatePresentation(1, true);
                Check(gun.Ammo.ReloadProgress == progress && presentation.AudioPaused, "Menu advances sidearm reload.");
                player.InputEnabled = true; gun.AdvanceReload(20); presentation.UpdatePresentation(0, true);
                Check(gun.Ammo.Magazine == definition.magazineSize && presentation.ReloadSoundsPlayed - reloadSoundsBefore == definition.reloadSounds.Length, "Reload completion / foley mismatch: " + definition.id);
            }
            clock += 10;
            Debug.Log("SIDEARM_RUNTIME_PASS " + definition.id);
        }
        gun.PrepareEquipmentChange();
        appearance.Show(loadout, "ctm_sas", "weapon_rif_m4a4"); motion.Initialize(player, appearance.Animator);
        gun.Initialize(player, appearance.EquippedWeapon, "weapon_rif_m4a4");
        Check(gun.CanFire && !gun.IsMelee && gun.Ammo.Capacity == 30, "Returning from knife did not restore rifle.");
        Object.Destroy(player.gameObject); Object.Destroy(targetObject); Object.Destroy(floor);
        yield return null;
        Debug.Log("SIDEARM_VERIFY_PASS: 11 pistols, both knives, held/moving poses, actual shots, reload, alternating duals, light/heavy damage, range/walls, switch cleanup.");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Capture(TrainingCharacterController player, GameObject target, string path)
    {
        var visible = target.GetComponent<Renderer>(); bool wasVisible = visible.enabled; visible.enabled = false;
        var effects = GameObject.Find("Training Shot Effects"); if (effects != null) effects.SetActive(false);
        WeaponActionVerificationDriver.Capture(player, path);
        if (effects != null) effects.SetActive(true);
        visible.enabled = wasVisible;
    }
    private static void ResetTarget(TrainingTarget target)
    {
        typeof(TrainingTarget).GetProperty("Health").SetValue(target, 100f);
        foreach (var renderer in target.GetComponentsInChildren<Renderer>()) renderer.enabled = true;
        foreach (var collider in target.GetComponentsInChildren<Collider>()) collider.enabled = true;
    }
}
#endif
