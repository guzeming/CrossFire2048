using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Shared.Combat;
using System;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTrainingWeapons
{
    public static void BuildAndRun()
    {
        BuildTrainingWeapons.Build();
        BuildTrainingWeaponActions.Build();
        CheckCadence();
        CheckMuzzles();
        CheckLaserWeapons();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Firing Verification").AddComponent<FiringVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void CheckCadence()
    {
        foreach (float interval in new[] { .1f, .09f })
        foreach (int fps in new[] { 15, 30, 60, 144 })
        {
            var trigger = new FireControl(interval, true);
            Check(trigger.Tick(true, true, true, 0) == 0, "Entry click fired.");
            trigger.Tick(false, false, true, 0);
            int shots = 0;
            for (int frame = 0; frame <= fps; frame++) shots += trigger.Tick(true, frame == 0, true, (double)frame / fps);
            Check(shots == (int)Math.Floor(1d / interval + .00001) + 1, "Frame-dependent cadence at " + fps + ": " + shots);
            trigger.Tick(true, false, false, 2);
            Check(trigger.Tick(true, false, true, 3) == 0, "Blocked click continued firing.");
            trigger.Tick(false, false, true, 3);
            Check(trigger.Tick(true, true, true, 4) == 1, "Stale backlog after resume.");
            Check(trigger.Tick(true, false, true, 20) <= 4, "Unbounded stall burst.");
        }
        var semi = new FireControl(.1f, false);
        semi.Tick(false, false, true, 0);
        Check(semi.Tick(true, true, true, 0) == 1 && semi.Tick(true, false, true, 1) == 0, "Semi-auto repeats while held.");
        Debug.Log("FIRING_CADENCE_PASS");
    }

    private static void CheckMuzzles()
    {
        var loadout = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        var catalog = Resources.Load<WeaponCatalog>("Gameplay/Weapons");
        foreach (var agent in loadout.agents)
        foreach (var definition in catalog.weapons)
        {
            if (definition.IsMelee) continue; // Melee assets have no muzzle.
            var root = new GameObject("Muzzle verification");
            var appearance = root.AddComponent<OperationBlacktide.Client.Gameplay.Avatars.BattleAvatar>();
            appearance.Show(loadout, agent.id, definition.id);
            appearance.Animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Gameplay/Locomotion");
            appearance.Animator.Rebind();
            appearance.Animator.SetBool("Grounded", true);
            appearance.Animator.Update(0);
            // Sidearms use imported combat grips/hold poses; a lobby grip on rifle locomotion is not the runtime pose.
            if (definition.hasCombatGrip)
            {
                appearance.EquippedWeapon.localPosition = definition.combatGripPosition;
                appearance.EquippedWeapon.localRotation = definition.combatGripRotation;
            }
            if (definition.idleAnimation != null) definition.idleAnimation.SampleAnimation(appearance.Animator.gameObject, 0);
            Vector3 muzzle = appearance.EquippedWeapon.TransformPoint(definition.muzzlePosition);
            Check(muzzle.z > .35f && muzzle.z < 1.5f && muzzle.y > .8f && muzzle.y < 1.8f && Mathf.Abs(muzzle.x) < .5f,
                "Bad muzzle position " + agent.id + "/" + definition.id + ": " + muzzle);
            UnityEngine.Object.DestroyImmediate(root);
        }
        Check(catalog.casingMesh.bounds.size.magnitude < .1f && catalog.casingMesh.bounds.size.magnitude > .01f,
            "Casing units are incorrect: " + catalog.casingMesh.bounds.size);
        Debug.Log("FIRING_MUZZLES_PASS: " + loadout.agents.Length + " agents x " + catalog.weapons.Length + " weapons");
    }

    private static void CheckLaserWeapons()
    {
        var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        int guns = 0, snipers = 0;
        foreach (var weapon in catalog.weapons)
        {
            if (weapon.category == EquipmentCategory.Gear) continue;
            bool sniper = weapon.slot == "rifle.scout" || weapon.slot == "rifle.awp" || weapon.slot == "rifle.auto";
            Check(new WeaponDefinition { id = weapon.id }.HasAimLaser,
                "Incorrect laser eligibility: " + weapon.id);
            if (sniper) snipers++; else guns++;
        }
        Check(snipers == 4 && guns > 0, "Sniper / firearm fixtures missing.");
        Debug.Log("AIM_LASER_WEAPONS_PASS: " + guns + " other firearms and " + snipers + " snipers enabled.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
