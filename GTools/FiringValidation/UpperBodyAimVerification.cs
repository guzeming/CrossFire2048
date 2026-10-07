#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class UpperBodyAimVerification
{
    public static void Run()
    {
        var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
        var weapons = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
        var camera = new GameObject("Upper Body Test Camera").AddComponent<Camera>();
        camera.enabled = false;
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "Upper Body Aim Target";
        target.transform.localScale = Vector3.one * .08f;
        float worst = 0;
        foreach (var agent in loadout.agents)
        foreach (var definition in weapons.weapons)
        {
            var player = Object.Instantiate(Resources.Load<TrainingCharacterController>("Training/TrainingPlayer"));
            player.Initialize(camera, new Vector3(1000, 100, 0), Quaternion.identity);
            player.enabled = false;
            var visual = new GameObject("Aim Test Appearance");
            visual.transform.SetParent(player.transform, false);
            var appearance = visual.AddComponent<LobbyLoadoutPreview>();
            appearance.Show(loadout, agent.id, definition.id);
            var motion = player.gameObject.AddComponent<TrainingCharacterAnimator>();
            motion.Initialize(player, appearance.Animator);
            motion.enabled = false;
            foreach (var bone in player.GetComponentsInChildren<Transform>()) bone.gameObject.layer = 2;
            var gun = player.gameObject.AddComponent<TrainingWeaponController>();
            gun.Initialize(player, appearance.EquippedWeapon, definition.id);
            var aim = player.GetComponent<TrainingUpperBodyAim>();
            var presentation = player.GetComponent<TrainingWeaponPresentation>();
            var animator = appearance.Animator;
            var barrel = appearance.EquippedWeapon.GetComponentInChildren<MeshFilter>().transform;
            var bones = player.GetComponentsInChildren<Transform>();
            var chest = bones.Single(b => b.name == "spine_3");
            var leftHand = bones.Single(b => b.name == "hand_L");
            var rightHand = bones.Single(b => b.name == "hand_R");
            var foot = bones.Single(b => b.name == "ankle_L");
            var pelvis = bones.Single(b => b.name == "pelvis");
            string context = agent.id + "/" + definition.id;

            for (int pose = 0; pose < 4; pose++)
            foreach (float yaw in new[] { 0f, 135f })
            foreach (Vector3 localTarget in new[] { new Vector3(0, 0, 1), new Vector3(1, 0, 5), new Vector3(-1, 4, 5) })
            {
                aim.RestorePose();
                player.transform.rotation = Quaternion.Euler(0, yaw, 0);
                animator.SetFloat("Speed", pose == 1 ? 4.5f : pose == 2 ? 7 : 0);
                animator.SetFloat("MoveX", pose == 1 ? 1 : 0);
                animator.SetFloat("MoveZ", pose == 1 ? 0 : 1);
                animator.SetBool("Grounded", pose != 3);
                animator.Play(pose == 3 ? "Airborne" : "Locomotion", 0, .4f);
                animator.Update(0);
                SetTarget(player, camera, target, localTarget);
                Vector3 footBefore = foot.position, pelvisBefore = pelvis.position;
                Quaternion pelvisRotation = pelvis.rotation;
                Vector3 leftBefore = chest.InverseTransformPoint(leftHand.position);
                Vector3 rightBefore = chest.InverseTransformPoint(rightHand.position);
                aim.ApplyAim();
                float error = BarrelError(player, gun, barrel);
                worst = Mathf.Max(worst, error);
                Check(error < 1, "Barrel misses aim: " + context + " pose=" + pose + " target=" + localTarget + " error=" + error
                    + " muzzle=" + player.transform.InverseTransformPoint(gun.MuzzlePosition) + " direction=" + player.transform.InverseTransformDirection(barrel.TransformDirection(Vector3.down)));
                Check(Vector3.Distance(foot.position, footBefore) < .0001f && Vector3.Distance(pelvis.position, pelvisBefore) < .0001f
                    && Quaternion.Angle(pelvisRotation, pelvis.rotation) < .1f, "Aim changed lower-body animation: " + context);
                Check(Vector3.Distance(leftBefore, chest.InverseTransformPoint(leftHand.position)) < .001f
                    && Vector3.Distance(rightBefore, chest.InverseTransformPoint(rightHand.position)) < .001f, "Aim broke the two-handed grip: " + context);
                Vector3 stableMuzzle = gun.MuzzlePosition;
                for (int repeat = 0; repeat < 10; repeat++) aim.ApplyAim();
                Check(Vector3.Distance(stableMuzzle, gun.MuzzlePosition) < .001f, "Repeated aim accumulated bone rotations: " + context);
                if (agent.id == "ctm_sas" && definition.silenced && pose == 0 && yaw == 0)
                    Capture(player, camera, "upper-body-" + (localTarget.z == 1 ? "near-ground" : localTarget.y > 0 ? "up" : "down") + ".png");
            }

            // Real firing must use the posed muzzle and the geometry's barrel direction.
            aim.RestorePose();
            player.transform.rotation = Quaternion.identity;
            animator.SetBool("Grounded", true); animator.SetFloat("Speed", 0);
            animator.Play("Locomotion", 0, .2f); animator.Update(0);
            SetTarget(player, camera, target, new Vector3(0, 0, 2));
            gun.ProcessTrigger(false, false, true, 100);
            gun.ProcessTrigger(true, true, true, 100);
            Check(gun.LastShot.hasHit && gun.LastShot.hit.collider == target.GetComponent<Collider>()
                && Vector3.Distance(gun.LastShot.origin, gun.MuzzlePosition) < .001f
                && Vector3.Angle(barrel.TransformDirection(Vector3.down), gun.LastShot.end - gun.LastShot.origin) < 1,
                "Shot did not leave the posed barrel: " + context);
            presentation.UpdatePresentation(.07f, true);
            presentation.UpdatePresentation(0, true);
            aim.ApplyAim();
            Check(BarrelError(player, gun, barrel) < 1, "Recoil sampling overwrote upper-body aim: " + context);
            player.GetComponent<TrainingAimLaser>().UpdateBeam(true);
            var beamTransform = player.transform.Find("Aim Laser");
            var beam = beamTransform != null ? beamTransform.GetComponent<LineRenderer>() : null;
            if (definition.HasAimLaser)
                Check(beam != null && beam.enabled && Vector3.Distance(beam.GetPosition(0), gun.MuzzlePosition) < .001f
                    && Vector3.Angle(barrel.TransformDirection(Vector3.down), beam.GetPosition(1) - beam.GetPosition(0)) < 1,
                    "Laser does not follow the posed barrel: " + context);
            else Check(beam == null || !beam.enabled, "Non-firearm unexpectedly has a laser: " + context);

            Check(gun.TryReload(), "Could not begin reload.");
            gun.AdvanceReload(definition.reloadDuration * .45f);
            presentation.UpdatePresentation(0, true);
            Vector3 reloadHand = chest.InverseTransformPoint(leftHand.position);
            Vector3 reloadGun = chest.InverseTransformPoint(gun.MuzzlePosition);
            aim.ApplyAim();
            Check(Vector3.Distance(reloadHand, chest.InverseTransformPoint(leftHand.position)) < .001f
                && Vector3.Distance(reloadGun, chest.InverseTransformPoint(gun.MuzzlePosition)) < .001f,
                "Aim altered authored reload gestures: " + context);
            // The gun leaves its aiming pose during reload: its laser must follow the animated barrel,
            // not bend back toward the unchanged crosshair. Cover mid/late reload and recovery.
            foreach (float progress in new[] { .45f, .6f, .8f, .95f })
            {
                gun.AdvanceReload(Mathf.Max(0, definition.reloadDuration * progress - gun.Ammo.ReloadElapsed));
                presentation.UpdatePresentation(0, true);
                aim.ApplyAim();
                player.GetComponent<TrainingAimLaser>().UpdateBeam(true);
                if (definition.HasAimLaser)
                {
                    Vector3 line = beam.GetPosition(1) - beam.GetPosition(0);
                    Check(beam.enabled && Vector3.Distance(beam.GetPosition(0), gun.MuzzlePosition) < .001f
                        && Vector3.Angle(barrel.TransformDirection(Vector3.down), line) < .05f
                        && line.magnitude <= 30.001f,
                        "Reload laser left barrel / exceeded 30m: " + context + " progress=" + progress);
                }
                if (agent.id == "ctm_sas" && definition.silenced && progress == .45f)
                    Capture(player, camera, "upper-body-reload-laser.png");
            }
            int shots = gun.ShotsFired;
            gun.ProcessTrigger(true, true, true, 101);
            Check(gun.ShotsFired == shots, "Aimed weapon fired during reload.");
            gun.AdvanceReload(definition.reloadDuration);
            presentation.UpdatePresentation(0, true);
            aim.ApplyAim();
            Check(BarrelError(player, gun, barrel) < 1, "Aim did not recover after reload: " + context);
            aim.enabled = false;
            animator.Update(0);
            Vector3 resetMuzzle = gun.MuzzlePosition;
            aim.enabled = true; aim.ApplyAim(); aim.enabled = false;
            Check(Vector3.Distance(resetMuzzle, gun.MuzzlePosition) < .001f, "Disabling aim left a bone offset: " + context);
            Object.DestroyImmediate(player.gameObject);
        }
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(camera.gameObject);
        Debug.Log("UPPER_BODY_AIM_PASS: " + loadout.agents.Length + " agents x " + weapons.weapons.Length + " weapons, ground/high targets, yaw, idle/strafe/run/jump, grip, recoil/reload, posed shots, laser, no drift; worst barrel error=" + worst);
        Debug.Log("AIM_LASER_RELOAD_PASS: animated barrel alignment at 45/60/80/95 percent reload, bounded to 30m.");
    }

    private static void SetTarget(TrainingCharacterController player, Camera camera, GameObject target, Vector3 localTarget)
    {
        target.transform.position = player.transform.TransformPoint(localTarget);
        camera.transform.position = player.transform.TransformPoint(new Vector3(0, 9, -8));
        camera.transform.LookAt(target.transform);
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(target.transform.position), 0);
        Check(Vector3.Distance(player.AimPoint, target.transform.position) < .2f, "Aim fixture is occluded.");
    }

    private static float BarrelError(TrainingCharacterController player, TrainingWeaponController gun, Transform barrel)
        => Vector3.Angle(barrel.TransformDirection(Vector3.down), player.AimPoint - gun.MuzzlePosition);

    private static void Capture(TrainingCharacterController player, Camera camera, string path)
    {
        // Multiple poses are rendered within one test frame; bake each pose instead of reusing
        // Unity's once-per-frame GPU skinning output from the first capture.
        var skins = player.GetComponentsInChildren<SkinnedMeshRenderer>();
        var snapshots = new GameObject[skins.Length];
        var meshes = new Mesh[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            meshes[i] = new Mesh(); skins[i].BakeMesh(meshes[i], true);
            snapshots[i] = new GameObject("Aim Pose Capture", typeof(MeshFilter), typeof(MeshRenderer));
            snapshots[i].layer = 2;
            snapshots[i].transform.SetParent(skins[i].transform, false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = meshes[i];
            snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
            skins[i].enabled = false;
        }
        var position = camera.transform.position; var rotation = camera.transform.rotation;
        float field = camera.fieldOfView; int mask = camera.cullingMask;
        camera.fieldOfView = 35; camera.cullingMask = 1 << 2;
        camera.transform.position = player.transform.position + new Vector3(3.2f, 2.3f, 2.6f);
        camera.transform.LookAt(player.transform.position + new Vector3(0, .85f, .4f));
        camera.backgroundColor = new Color(.12f, .15f, .19f); camera.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(960, 960, 24);
        camera.targetTexture = target;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var active = RenderTexture.active; RenderTexture.active = target;
        var pixels = new Texture2D(960, 960, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 960, 960), 0, 0); pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        RenderTexture.active = active; camera.targetTexture = null; target.Release();
        Object.DestroyImmediate(target); Object.DestroyImmediate(pixels);
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.fieldOfView = field; camera.cullingMask = mask;
        for (int i = 0; i < skins.Length; i++)
        {
            skins[i].enabled = true;
            Object.DestroyImmediate(snapshots[i]); Object.DestroyImmediate(meshes[i]);
        }
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
#endif
