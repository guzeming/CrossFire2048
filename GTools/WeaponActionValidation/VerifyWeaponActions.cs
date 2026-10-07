using System;
using System.Linq;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyWeaponActions
{
    // Recheck new primary weapons without reimporting assets already built by BuildAndRun.
    public static void RunPrimaries()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Primary Verification").AddComponent<WeaponActionVerificationDriver>().primaryOnly = true;
        EditorApplication.EnterPlaymode();
    }

    public static void BuildAndRun()
    {
        BuildTrainingWeaponActions.Build();
        BuildTrainingWeaponActions.Build(); // Rebuilding an existing controller must be safe.
        BuildTrainingWeapons.Build();
        CheckBindings();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Weapon Action Verification").AddComponent<WeaponActionVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void CheckBindings()
    {
        var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
        var weapons = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
        foreach (var agent in loadout.agents)
        {
            var actor = UnityEngine.Object.Instantiate(agent.prefab);
            var animator = actor.GetComponentInChildren<Animator>(); animator.enabled = false; animator.runtimeAnimatorController = null;
            var hand = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_L");
            foreach (var weapon in weapons.weapons)
            foreach (var clip in new[] { weapon.fireAnimation, weapon.alternateFireAnimation, weapon.reloadAnimation, weapon.idleAnimation }
                .Concat(weapon.locomotionAnimations).Where(c => c != null))
            {
                Check(clip.length > 0, "Zero-length action " + weapon.id);
                foreach (string path in AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct())
                    Check(path.Length == 0 || animator.transform.Find(path) != null, "Unbound action " + agent.id + "/" + path);
                clip.SampleAnimation(animator.gameObject,0);
                Vector3 start = hand.position;
                float motion = 0;
                for (int i = 1; i <= 10; i++)
                { clip.SampleAnimation(animator.gameObject,clip.length*i/10); motion = Mathf.Max(motion,Vector3.Distance(start,hand.position)); }
                if (!clip.name.StartsWith("idle_")) Check(motion > .0001f, "Action has no hand motion " + agent.id + "/" + clip.name + "=" + motion);
            }
            UnityEngine.Object.DestroyImmediate(actor);
        }
        foreach (var weapon in weapons.weapons)
        {
            Check(weapon.fireAnimation != null, "Missing attack animation " + weapon.id);
            if (weapon.IsPistol || weapon.IsMelee) Check(weapon.idleAnimation != null && weapon.locomotionAnimations.Length == 22, "Missing held locomotion " + weapon.id);
            if (!weapon.IsMelee && !weapon.IsTaser) Check(weapon.reloadAnimation != null, "Missing reload " + weapon.id);
            float previous = -1;
            foreach (var cue in weapon.reloadSounds)
            {
                Check(cue.clip != null && cue.clip.length > .01f && cue.normalizedTime > previous && cue.normalizedTime < 1,"Invalid reload cue " + weapon.id);
                previous = cue.normalizedTime;
            }
        }
        Debug.Log("WEAPON_ACTION_BINDINGS_PASS: rifle/pistol/knife actions and directional locomotion bind on every agent.");
    }
    private static void Check(bool value,string message) { if (!value) throw new Exception(message); }
}
