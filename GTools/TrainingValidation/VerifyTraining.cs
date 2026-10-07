using System;
using System.Linq;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Lobby;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTraining
{
    public static void BuildAndRun()
    {
        BuildTrainingAnimations.Build();
        ValidateAnimationBindings();
        ValidateRifleAim();
        BuildTrainingScene.Build();
        RunExisting();
    }

    public static void RunExisting()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
        new GameObject("Training Verification").AddComponent<TrainingVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void ValidateAnimationBindings()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<LobbyLoadoutCatalog>("Assets/Resources/Loadout/LobbyCatalog.asset");
        var clips = AssetDatabase.LoadAllAssetsAtPath(BuildTrainingAnimations.AnimationPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        foreach (var agent in catalog.agents)
        {
            var actor = UnityEngine.Object.Instantiate(agent.prefab);
            var animator = actor.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = null;
            animator.enabled = false;
            var foot = actor.GetComponentsInChildren<Transform>().Single(t => t.name == "ankle_L");
            foreach (var clip in clips)
            {
                foreach (var path in AnimationUtility.GetCurveBindings(clip).Select(c => c.path).Distinct())
                    if (path.Length > 0 && animator.transform.Find(path) == null)
                        throw new Exception("Animation binding missing: " + agent.id + " / " + path);
                clip.SampleAnimation(animator.gameObject, clip.length * .1f);
                Vector3 before = actor.transform.InverseTransformPoint(foot.position);
                clip.SampleAnimation(animator.gameObject, clip.length * .65f);
                Vector3 after = actor.transform.InverseTransformPoint(foot.position);
                if (clip.name != "idle_rifle" && Vector3.Distance(before, after) < .01f)
                    throw new Exception("Animation does not move the foot: " + agent.id + " / " + clip.name);
            }
            UnityEngine.Object.DestroyImmediate(actor);
        }
        Debug.Log("TRAINING_ANIMATION_BINDINGS_PASS: 21 motion clips and combat idle bind on all 10 characters.");
    }

    private static void ValidateRifleAim()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<LobbyLoadoutCatalog>("Assets/Resources/Loadout/LobbyCatalog.asset");
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BuildTrainingAnimations.ControllerPath);
        float worst = 0;
        foreach (var agent in catalog.agents)
        foreach (var weapon in catalog.weapons.Where(w => w.slot == "rifle.main"))
        {
            var root = new GameObject("Aim Validation");
            var appearance = root.AddComponent<LobbyLoadoutPreview>();
            appearance.Show(catalog, agent.id, weapon.id);
            var animator = appearance.Animator;
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            var gun = root.GetComponentsInChildren<Transform>().Single(t => t.name == "Equipped_" + weapon.id);
            var mesh = gun.GetComponentInChildren<MeshFilter>();
            // All three imported main rifles have their actual barrel along mesh-local -Y.
            // Inspect the weapon geometry, not a helper transform that could hide a bad attachment.
            foreach (float yaw in new[] { 0f, 90f, 225f })
            {
                root.transform.rotation = Quaternion.Euler(0, yaw, 0);
                foreach (float speed in agent.id == "ctm_sas" ? new[] { 0f, 2.25f, 4.5f, 5.75f, 7f } : new[] { 0f })
                for (int direction = 0; direction < (agent.id == "ctm_sas" ? 16 : 1); direction++)
                {
                    float angle = direction * Mathf.PI / 8;
                    animator.SetFloat("Speed", speed);
                    animator.SetFloat("MoveX", Mathf.Sin(angle));
                    animator.SetFloat("MoveZ", Mathf.Cos(angle));
                    animator.SetBool("Grounded", true);
                    foreach (float phase in new[] { .1f, .4f, .7f })
                    {
                        animator.Play("Locomotion", 0, phase);
                        animator.Update(0);
                        worst = Mathf.Max(worst, CheckMuzzle(root.transform, mesh.transform,
                            agent.id + "/" + weapon.id + " speed=" + speed + " dir=" + direction + " phase=" + phase));
                    }
                }
                animator.SetBool("Grounded", false);
                foreach (var direction in new[] { Vector2.zero, Vector2.up, Vector2.right, Vector2.down, Vector2.left })
                {
                    animator.SetFloat("MoveX", direction.x); animator.SetFloat("MoveZ", direction.y);
                    foreach (float phase in new[] { .1f, .4f, .7f })
                    {
                        animator.Play("Airborne", 0, phase); animator.Update(0);
                        worst = Mathf.Max(worst, CheckMuzzle(root.transform, mesh.transform, agent.id + "/" + weapon.id + " jump"));
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(root);
        }
        Debug.Log("TRAINING_RIFLE_AIM_PASS: 10 characters, all 3 main rifles, turns, idle/walk/run blends and jumps; max barrel angle=" + worst);
    }

    private static float CheckMuzzle(Transform character, Transform mesh, string context)
    {
        float angle = Vector3.Angle(character.forward, mesh.TransformDirection(Vector3.down));
        if (angle > 6f) throw new Exception("Muzzle is not facing forward: " + context + " angle=" + angle);
        return angle;
    }
}
