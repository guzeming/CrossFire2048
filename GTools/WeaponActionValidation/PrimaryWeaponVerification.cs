#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEngine;
using Object = UnityEngine.Object;

public static class PrimaryWeaponVerification
{
    public static IEnumerator Run(LobbyLoadoutCatalog loadout, TrainingWeaponCatalog catalog)
    {
        var guns = loadout.weapons.Where(w => TrainingWeaponSelection.SlotOf(w) == 1 || TrainingWeaponSelection.SlotOf(w) == 2).ToArray();
        Check(guns.Length == 35 && guns.All(w => catalog.Find(w.id) != null), "Equipment contains unusable guns.");
        var selection = new TrainingWeaponSelection(loadout.weapons, guns[0].id);
        foreach (var item in guns) Check(selection.Select(item.id), "Gun cannot be selected: " + item.id);
        var camera = Camera.main;
        camera.GetComponent<TrainingCameraController>().enabled = false;
        var origin = new Vector3(0, 100, 0);
        camera.transform.position = origin + new Vector3(3, 6, 7); camera.transform.LookAt(origin + Vector3.up);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = origin + Vector3.down * .1f; floor.transform.localScale = new Vector3(20, .2f, 20);
        var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetObject.transform.position = origin + new Vector3(0, 1.3f, 5); targetObject.transform.localScale = new Vector3(3, 2.5f, .4f);
        var target = targetObject.AddComponent<TrainingTarget>();
        var player = Object.Instantiate(Resources.Load<TrainingCharacterController>("Training/TrainingPlayer"));
        player.Initialize(camera, origin, Quaternion.identity); player.enabled = false;
        var visual = new GameObject("Primary Appearance"); visual.transform.SetParent(player.transform, false);
        var appearance = visual.AddComponent<LobbyLoadoutPreview>();
        var motion = player.gameObject.AddComponent<TrainingCharacterAnimator>(); motion.enabled = false;
        var gun = player.gameObject.AddComponent<TrainingWeaponController>();
        var impacts = new List<Vector3>();
        int events = 0;
        gun.TargetHit += (t, damage, position) => impacts.Add(position);
        gun.ShotFired += shot => events++;
        double clock = 2000;
        int checkedGuns = 0;
        foreach (var definition in catalog.weapons.Where(w => !w.IsPistol && !w.IsMelee && w.idleAnimation != null))
        {
            gun.PrepareEquipmentChange();
            appearance.Show(loadout, "ctm_sas", definition.id); motion.Initialize(player, appearance.Animator);
            foreach (var child in player.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 2;
            gun.Initialize(player, appearance.EquippedWeapon, definition.id);
            var animator = appearance.Animator;
            animator.Play("Locomotion", 0, 0); animator.Update(0);
            var presentation = player.GetComponent<TrainingWeaponPresentation>();
            var foot = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "ankle_L");
            Check(gun.CanFire && gun.Ammo.Capacity == definition.magazineSize, "Unusable gun " + definition.id);
            Check(definition.shots.Length > 0 && definition.shots.All(s => s != null), "Missing gun audio " + definition.id);
            Check(definition.fireAnimation.name == "fire_" + definition.id.Substring(definition.id.IndexOf('_', 7) + 1), "Fallback rifle action " + definition.id);
            typeof(TrainingTarget).GetProperty("Health").SetValue(target, 10000f);
            Physics.SyncTransforms(); player.Aim(camera.WorldToScreenPoint(targetObject.transform.position), 1);
            player.GetComponent<TrainingUpperBodyAim>().ApplyAim();
            Capture(player, targetObject, definition.id + "-idle");
            animator.SetBool("Grounded", true); animator.SetFloat("Speed", 4.5f); animator.SetFloat("MoveX", 1);
            animator.Play("Locomotion", 0, .35f); animator.Update(0);
            Vector3 footPosition = foot.position; presentation.UpdatePresentation(0, true);
            Check(Vector3.Distance(foot.position, footPosition) < .001f, "Grip correction moves the feet " + definition.id);
            Check(animator.GetLayerWeight(animator.GetLayerIndex(TrainingWeaponPresentation.HoldLayer)) == 1, "Moving grip missing " + definition.id);
            Capture(player, targetObject, definition.id + "-moving");
            animator.SetFloat("Speed", 0); animator.Play("Locomotion", 0, 0); presentation.UpdatePresentation(0, true);
            int before = gun.ShotsFired, beforeEvents = events, beforeActions = presentation.FireAnimationsPlayed;
            impacts.Clear();
            gun.ProcessTrigger(true, true, true, clock);
            Check(gun.ShotsFired == before, "Held equip input fires " + definition.id);
            gun.ProcessTrigger(false, false, true, clock); gun.ProcessTrigger(true, true, true, clock);
            Check(gun.ShotsFired == before + 1 && gun.Ammo.Magazine == definition.magazineSize - 1, "Wrong shot/ammo count " + definition.id);
            Check(events == beforeEvents + 1 && presentation.FireAnimationsPlayed == beforeActions + 1, "Pellets multiply cartridge events " + definition.id);
            Check(impacts.Count == Mathf.Max(1, definition.pelletCount), "Pellets did not hit the wide target " + definition.id + ": " + impacts.Count);
            if (definition.pelletCount > 1) Check(impacts.Any(p => Vector3.Distance(p, impacts[0]) > .1f), "Shotgun has no spread " + definition.id);
            presentation.UpdatePresentation(.07f, true); presentation.UpdatePresentation(0, true);
            Check(animator.GetLayerWeight(animator.GetLayerIndex(TrainingWeaponPresentation.FireLayer)) == 1, "Firing action missing " + definition.id);
            player.GetComponent<TrainingAimLaser>().UpdateBeam(true);
            Check(player.GetComponentsInChildren<LineRenderer>().Any(l => l.name == "Aim Laser" && l.enabled), "Laser missing " + definition.id);
            gun.ProcessTrigger(true, false, true, clock + definition.cycleTime * 1.1);
            Check(gun.ShotsFired == before + (definition.automatic ? 2 : 1), "Wrong trigger mode " + definition.id);

            int sounds = presentation.ReloadSoundsPlayed;
            Check(gun.TryReload(), "Cannot reload " + definition.id);
            gun.AdvanceReload(definition.reloadDuration * .5f); presentation.UpdatePresentation(0, true);
            Check(presentation.ReloadSoundsPlayed > sounds, "Silent reload " + definition.id);
            Check(animator.GetLayerWeight(animator.GetLayerIndex(TrainingWeaponPresentation.ReloadLayer)) == 1, "Reload action missing " + definition.id);
            Capture(player, targetObject, definition.id + "-reload");
            before = gun.ShotsFired;
            gun.ProcessTrigger(false, false, true, clock + 2); gun.ProcessTrigger(true, true, true, clock + 2);
            Check(gun.ShotsFired == before, "Can fire while reloading " + definition.id);
            player.InputEnabled = false; gun.AdvanceReload(10); presentation.UpdatePresentation(10, true);
            Check(Mathf.Abs(gun.Ammo.ReloadProgress - .5f) < .001f && presentation.AudioPaused, "Menu did not freeze reload " + definition.id);
            player.InputEnabled = true; gun.AdvanceReload(20); presentation.UpdatePresentation(0, true);
            Check(gun.Ammo.Magazine == definition.magazineSize && presentation.ReloadSoundsPlayed - sounds == definition.reloadSounds.Length, "Reload incomplete " + definition.id);

            // Every pellet must respect a physical wall, including faded camera occluders.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = origin + new Vector3(0, 1.3f, 2.5f); wall.transform.localScale = new Vector3(8, 8, .15f);
            Physics.SyncTransforms(); impacts.Clear();
            gun.ProcessTrigger(false, false, true, clock + 4); gun.ProcessTrigger(true, true, true, clock + 4);
            Check(impacts.Count == 0 && gun.LastShot.hit.collider == wall.GetComponent<Collider>(), "Gun shoots through wall " + definition.id);
            wall.SetActive(false); Object.Destroy(wall);
            while (gun.Ammo.TryConsume()) { }
            before = gun.ShotsFired;
            gun.ProcessTrigger(false, false, true, clock + 6); gun.ProcessTrigger(true, true, true, clock + 6);
            Check(gun.ShotsFired == before && gun.TryReload(), "Empty magazine does not block/refill " + definition.id);
            gun.AdvanceReload(20);
            Check(gun.Ammo.Magazine == definition.magazineSize, "Empty reload failed " + definition.id);
            clock += 10; checkedGuns++;
            Debug.Log("PRIMARY_GUN_RUNTIME_PASS " + definition.id);
        }
        Check(checkedGuns == 17, "Not all new primary guns were exercised.");
        Object.Destroy(player.gameObject); Object.Destroy(targetObject); Object.Destroy(floor);
        yield return null;
        Debug.Log("ALL_GUNS_COVERAGE_PASS: 35 selectable guns; 17 new primaries fire, hit, spread, respect walls, animate, reload and pause.");
    }
    private static void Capture(TrainingCharacterController player, GameObject target, string name)
    {
        var visible = target.GetComponent<Renderer>(); visible.enabled = false;
        var visuals = Object.FindObjectsOfType<Transform>().Where(t => t.name == "Training Shot Effects" || t.name == "Aim Laser")
            .Select(t => t.gameObject).Where(g => g.activeSelf).ToArray();
        foreach (var effect in visuals) effect.SetActive(false);
        WeaponActionVerificationDriver.Capture(player, "primary-" + name + ".png");
        foreach (var effect in visuals) effect.SetActive(true);
        visible.enabled = true;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
