#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class ThrowableActionVerification
{
    public static void CheckBindings(TrainingThrowableCatalog assets)
    {
        var clips = assets.items.SelectMany(d => new[] { d.drawAnimation, d.idleAnimation, d.prepareAnimation, d.throwAnimation }).Distinct().ToArray();
        Check(clips.Length == 7 && clips.All(c => c != null), "Seven CS action clips must be bound");
        foreach (var agent in Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog").agents)
        {
            var actor = Object.Instantiate(agent.prefab);
            var animator = actor.GetComponentInChildren<Animator>(); animator.enabled = false;
            var hand = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_R");
            foreach (var clip in clips)
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Check(bindings.All(b => b.path.Split('/').Contains("spine_0") && b.propertyName.StartsWith("m_LocalRotation.")), "Clip overwrites lower body or root: " + clip.name);
                Check(bindings.All(b => animator.transform.Find(b.path) != null), "Unbound action: " + agent.id + "/" + clip.name);
                clip.SampleAnimation(animator.gameObject, 0); Vector3 start = hand.position; float motion = 0;
                for (int i = 1; i <= 12; i++)
                { clip.SampleAnimation(animator.gameObject, clip.length * i / 12); motion = Mathf.Max(motion, Vector3.Distance(start, hand.position)); }
                if (!clip.name.StartsWith("idle_")) Check(motion > .04f, "Static action: " + agent.id + "/" + clip.name + " motion=" + motion);
            }
            Object.DestroyImmediate(actor);
        }
        Debug.Log("THROWABLE_ANIMATION_BINDINGS_PASS: seven upper-body clips bind on all ten agents.");
    }

    public static void Run(TrainingSceneController scene)
    {
        var player = scene.Player; var gun = player.GetComponent<TrainingWeaponController>(); var control = gun.Throwables;
        var world = control.World; var assets = Resources.Load<TrainingThrowableCatalog>("Training/TrainingThrowables");
        Vector3 savedPosition = player.transform.position; Quaternion savedRotation = player.transform.rotation;
        var motor = player.GetComponent<CharacterController>(); bool motorEnabled = motor.enabled; motor.enabled = false;
        bool simulate = world.enabled; world.enabled = false;
        player.transform.SetPositionAndRotation(new Vector3(1000, 150, 1000), Quaternion.identity);
        Physics.SyncTransforms();
        foreach (var data in assets.items)
        {
            Equip(scene, data.id);
            control.ProcessInput(false, false, false, true, data.drawAnimation.length + .01f);
            Check(control.Phase == TrainingThrowPhase.Ready, "Draw did not finish: " + data.id);
            var animator = player.GetComponent<TrainingCharacterAnimator>().Animator;
            if (data.id == "weapon_hegrenade")
            {
                var poses = player.GetComponent<TrainingThrowablePresentation>();
                for (int sample = 1; sample <= 7; sample++)
                {
                    poses.Present(data.throwAnimation, data.throwAnimation.length * sample * .05f, .1f);
                    Capture(player, "throwable-swing-" + sample + ".png");
                }
                poses.Present(data.idleAnimation, 0, .1f);
            }
            var hand = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_R");
            var lower = animator.GetComponentsInChildren<Transform>().Where(t => !t.GetComponentsInParent<Transform>().Any(p => p.name == "spine_0")).ToArray();
            var rotations = lower.Select(t => t.localRotation).ToArray(); var positions = lower.Select(t => t.localPosition).ToArray();
            Vector3 idleHand = hand.position;
            int throws = control.Throws;
            control.ProcessInput(true, true, false, true, data.prepareAnimation.length * .45f);
            Check(control.Phase == TrainingThrowPhase.Preparing && Vector3.Distance(hand.position, idleHand) > .025f, "Prepare pose did not move: " + data.id);
            if (data.id == "weapon_hegrenade" || data.id == "weapon_molotov") Capture(player, "throwable-" + data.id + "-prepare.png");
            control.ProcessInput(true, false, false, true, data.prepareAnimation.length);
            Check(control.Phase == TrainingThrowPhase.Holding && control.Throws == throws, "Held grenade threw itself");
            Vector3 held = hand.position;
            for (int i = 0; i < 30; i++)
            {
                // Component.SendMessage would also invoke every other LateUpdate on this GameObject,
                // including real mouse polling. Invoke only the held-pose pass being verified.
                typeof(TrainingCharacterAnimator).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(player.GetComponent<TrainingCharacterAnimator>(), null);
                control.ProcessInput(true, false, false, true, 1f / 30);
            }
            Check(control.Phase == TrainingThrowPhase.Holding && Vector3.Distance(held, hand.position) < .002f,
                "Holding pose drifted: " + data.id + " phase=" + control.Phase + " distance=" + Vector3.Distance(held, hand.position));
            control.ProcessInput(false, false, false, true, 0);
            Check(control.IsThrowing && control.Throws == throws, "Mouse release must begin throw animation before spawning");
            control.ProcessInput(false, false, false, true, data.ReleaseTime - .025f);
            Check(control.Throws == throws, "Projectile left before release marker");
            if (data.id == "weapon_hegrenade") Capture(player, "throwable-he-before-release.png");
            control.ProcessInput(false, false, false, true, .035f);
            Check(control.Throws == throws + 1, "Release marker failed");
            Check(Vector3.Distance(control.LastReleasePosition, control.LastReleaseHandPosition) < .015f, "Projectile did not originate at animated hand");
            for (int i = 0; i < lower.Length; i++)
                Check(Quaternion.Angle(rotations[i], lower[i].localRotation) < .01f && Vector3.Distance(positions[i], lower[i].localPosition) < .0001f,
                    "Action altered locomotion/root: " + lower[i].name);
            if (data.id == "weapon_hegrenade") Capture(player, "throwable-he-release.png");
            control.ProcessInput(false, false, false, true, .15f);
            Check(control.Throws == throws + 1, "Release fired twice");
            control.ProcessInput(false, false, false, true, data.throwAnimation.length + data.drawAnimation.length);
            Check(control.Phase == TrainingThrowPhase.Ready, "Follow-through/refill did not finish");
            // A quick click queues the release, but cannot skip the pin / ignition animation.
            control.ProcessInput(true, true, false, true, .01f);
            control.ProcessInput(false, false, false, true, .01f);
            Check(control.IsPreparing && control.Throws == throws + 1, "Quick click bypassed preparation");
            control.ProcessInput(false, false, false, true, data.prepareAnimation.length + data.throwAnimation.length + data.drawAnimation.length);
            Check(control.Throws == throws + 2 && control.Phase == TrainingThrowPhase.Ready, "Long frame lost or duplicated a release");
            control.ProcessInput(false, false, false, true, .01f);
            control.ProcessInput(true, true, false, true, data.prepareAnimation.length);
            control.ProcessInput(false, false, true, true, .01f);
            Check(control.Phase == TrainingThrowPhase.Ready && control.Throws == throws + 2, "Cancel did not clear held pose");
            for (int i = 0; i < 1300; i++) world.Simulate(.02f);
        }
        // An interrupted swing must not produce a late grenade after switching to a rifle.
        Equip(scene, "weapon_hegrenade");
        control.ProcessInput(false, false, false, true, 2);
        control.ProcessInput(true, true, false, true, 2);
        control.ProcessInput(false, false, false, true, .1f);
        int finalThrows = control.Throws;
        Check(scene.EquipSlot(1), "Switch back to rifle failed");
        control.ProcessInput(false, false, false, true, 3);
        Check(!control.CanThrow && control.Throws == finalThrows && player.GetComponent<TrainingThrowablePresentation>().CurrentClip == null,
            "Switch retained throwable animation/release");
        player.transform.SetPositionAndRotation(savedPosition, savedRotation); motor.enabled = motorEnabled; world.enabled = simulate;
        Debug.Log("THROWABLE_ANIMATION_RUNTIME_PASS: five models, preparation/hold, marker timing, hand origin, lower-body preservation, quick click, long frames, cancellation and switching.");
    }

    private static void Equip(TrainingSceneController scene, string id)
    {
        var gun = scene.Player.GetComponent<TrainingWeaponController>();
        for (int i = 0; i <= scene.Weapons.Options(4).Count && gun.WeaponId != id; i++) Check(scene.EquipSlot(4), "Cannot equip " + id);
        Check(gun.WeaponId == id, "Missing equipment " + id);
    }

    private static void Capture(TrainingCharacterController player, string path)
    {
        var camera = player.ViewCamera; Vector3 oldPosition = camera.transform.position; Quaternion oldRotation = camera.transform.rotation;
        bool orthographic = camera.orthographic; float size = camera.orthographicSize, aspect = camera.aspect;
        var clear = camera.clearFlags; Color background = camera.backgroundColor;
        camera.transform.position = player.transform.position + new Vector3(3, 2.3f, 4);
        camera.transform.LookAt(player.transform.position + Vector3.up * .95f);
        camera.orthographic = true; camera.orthographicSize = 1.35f; camera.aspect = 1.4f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .10f, .12f);
        var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>();
        var snapshots = new GameObject[skins.Length]; var meshes = new Mesh[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            meshes[i] = new Mesh(); skins[i].BakeMesh(meshes[i], true);
            snapshots[i] = new GameObject("Action Capture", typeof(MeshFilter), typeof(MeshRenderer));
            snapshots[i].transform.SetParent(skins[i].transform, false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = meshes[i];
            snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials; skins[i].enabled = false;
        }
        var rt = new RenderTexture(1400, 1000, 24); rt.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var active = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(1400, 1000, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 1400, 1000), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG()); RenderTexture.active = active; Object.DestroyImmediate(image); rt.Release(); Object.DestroyImmediate(rt);
        for (int i = 0; i < skins.Length; i++) { skins[i].enabled = true; Object.DestroyImmediate(snapshots[i]); Object.DestroyImmediate(meshes[i]); }
        camera.transform.SetPositionAndRotation(oldPosition, oldRotation); camera.orthographic = orthographic;
        camera.orthographicSize = size; camera.aspect = aspect; camera.clearFlags = clear; camera.backgroundColor = background;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif
