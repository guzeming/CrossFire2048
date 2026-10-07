#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class TopDownVerificationDriver : MonoBehaviour
{
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText("top-down-failure.txt", message + "\n" + trace); EditorApplication.Exit(1);
    }
    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var player = scene.Player;
        var gun = player.GetComponent<TrainingWeaponController>();
        var camera = player.ViewCamera;
        var follow = camera.GetComponent<TrainingCameraController>();
        player.enabled = false; scene.enabled = false; follow.enabled = false;
        player.InputEnabled = true;
        Check(FindObjectsOfType<TrainingTarget>().Length > 0, "No accessible practice targets spawned.");
        Vector3 saved = player.transform.position;
        Quaternion initialFacing = player.transform.rotation;
        float yawOffset = Mathf.DeltaAngle(initialFacing.eulerAngles.y, camera.transform.eulerAngles.y);
        Vector2 cursor = new Vector2(.7f, .4f);
        for (int i = 0; i < 300; i++) follow.UpdateView(cursor, false, true, 0, 1f / 60);
        Vector3 settled = follow.Focus;
        Check(Vector3.Distance(settled, saved + Vector3.up) > .8f && Vector3.Distance(settled, saved + Vector3.up) <= 2.41f,
            "Ordinary look-ahead is absent or unbounded.");
        for (int i = 0; i < 600; i++) follow.UpdateView(cursor, false, true, 0, 1f / 60);
        Check(Vector3.Distance(settled, follow.Focus) < .001f, "Stationary mouse causes camera drift.");
        player.transform.rotation = Quaternion.Euler(0, 130, 0);
        for (int i = 0; i < 120; i++) follow.UpdateView(cursor, false, true, 0, 1f / 60);
        Check(Mathf.Abs(Mathf.DeltaAngle(130 + yawOffset, camera.transform.eulerAngles.y)) < .01f,
            "Camera does not follow the character heading.");
        player.transform.rotation = initialFacing;
        follow.Snap();
        for (int i = 0; i < 60; i++) follow.UpdateView(cursor, true, true, 0, 1f / 30);
        Vector3 at30 = follow.Focus;
        follow.Snap();
        for (int i = 0; i < 288; i++) follow.UpdateView(cursor, true, true, 0, 1f / 144);
        Check(Vector3.Distance(at30, follow.Focus) < .005f, "Scope follow changes with frame rate.");
        Check(follow.ScopeBlend > .99f && Vector3.Distance(follow.Focus, saved + Vector3.up) > 6 && camera.fieldOfView < 36,
            "Scope does not pan farther / magnify.");
        follow.UpdateView(cursor, true, false, 20, .016f);
        Check(follow.ScopeBlend == 0 && Mathf.Abs(camera.fieldOfView - 48) < .01f, "Blocked view retains scope zoom.");
        for (int i = 0; i < 300; i++) follow.UpdateView(Vector2.zero, false, true, 0, 1f / 60);
        Check(Vector3.Distance(follow.Focus, saved + Vector3.up) < .001f, "View does not return to player.");
        Debug.Log("TOP_DOWN_CAMERA_PASS: bounded look-ahead, no drift, character yaw follow, 30/144 fps, scoped pan/zoom, return.");
        VerifyOrbit(player, follow);
        VerifyFacingControls(player, follow);

        gun.ProcessAim(false, true); gun.ProcessAim(true, true);
        Check(!gun.IsScoped, "Rifle opens sniper scope.");
        var rifleAmmo = gun.Ammo; rifleAmmo.TryConsume(); int rounds = rifleAmmo.Magazine;
        Check(EquipPrimary(scene, "weapon_snip_awp") && gun.IsSniper, "Sniper slot unavailable.");
        Check(gun.HudIcon != null, "Sniper HUD icon missing.");
        gun.ProcessAim(true, true); Check(!gun.IsScoped, "Held RMB survives a switch.");
        gun.ProcessAim(false, true); gun.ProcessAim(true, true); Check(gun.IsScoped, "RMB does not open scope.");
        gun.ProcessAim(true, false); Check(!gun.IsScoped, "Blocked input retains scope.");
        gun.ProcessAim(true, true); Check(!gun.IsScoped, "Scope reopens without release after blocking.");
        gun.ProcessAim(false, true); gun.ProcessAim(true, true);
        gun.Ammo.TryConsume(); Check(gun.TryReload() && !gun.IsScoped, "Reload did not close scope.");
        gun.ProcessAim(false, true); gun.ProcessAim(true, true); Check(!gun.IsScoped, "Reload allows scope.");
        Check(EquipPrimary(scene, "weapon_rif_m4a1_silencer") && ReferenceEquals(gun.Ammo, rifleAmmo) && gun.Ammo.Magazine == rounds,
            "Switching refills or replaces the saved magazine.");
        for (int i = 0; i < 8; i++) { EquipPrimary(scene, "weapon_snip_awp"); EquipPrimary(scene, "weapon_rif_m4a1_silencer"); }
        Check(player.GetComponents<TrainingWeaponEffects>().Length == 1 && player.GetComponents<TrainingWeaponPresentation>().Length == 1
            && player.GetComponents<TrainingUpperBodyAim>().Length == 1 && player.GetComponents<TrainingAimLaser>().Length == 1,
            "Switching duplicates components.");
        yield return null;
        Check(FindObjectsOfType<LineRenderer>().Count(l => l.name == "Bullet Tracer") == 32, "Switching leaks tracer pools.");
        Check(player.GetComponentsInChildren<AudioSource>().Count(a => a.name == "Reload Audio") == 1, "Switching leaks reload audio.");
        Check(player.GetComponentsInChildren<LineRenderer>().Count(l => l.name == "Aim Laser") == 1, "Switching leaks aim beams.");
        Debug.Log("TOP_DOWN_EQUIPMENT_PASS: four sniper profiles, scope gates, saved ammo, bounded effects.");

        // An elevated fixture isolates ballistic tests from map geometry.
        player.GetComponent<CharacterController>().enabled = false;
        player.transform.position = saved + Vector3.up * 100; player.transform.rotation = Quaternion.identity;
        player.GetComponent<CharacterController>().enabled = true;
        player.Move(Vector2.right, false, false, .1f);
        float walking = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up).magnitude;
        EquipPrimary(scene, "weapon_snip_awp"); gun.ProcessAim(false, true); gun.ProcessAim(true, true);
        player.Move(Vector2.right, true, false, .1f);
        float aiming = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up).magnitude;
        Check(Mathf.Abs(walking - 4.5f) < .02f && Mathf.Abs(aiming - walking * .45f) < .02f,
            "Scoped movement does not slow down or allows sprinting.");
        EquipPrimary(scene, "weapon_rif_m4a1_silencer");
        player.GetComponent<CharacterController>().enabled = false;
        player.transform.position = saved + Vector3.up * 100;
        player.GetComponent<TrainingUpperBodyAim>().enabled = false;
        follow.Snap();
        var fixture = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fixture.name = "Ballistic Target Fixture";
        fixture.transform.position = player.transform.position + new Vector3(0, 1.3f, 6);
        fixture.transform.localScale = new Vector3(1.2f, 2.4f, .4f);
        var target = fixture.AddComponent<TrainingTarget>();
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(fixture.transform.position), 1);
        int confirmed = 0;
        gun.TargetHit += (victim, damage, position) => { confirmed++; Check(damage > 0, "Nonpositive damage feedback."); };
        gun.ProcessTrigger(false, false, true, 100);
        gun.ProcessTrigger(true, true, true, 100);
        Check(target.Health > 0 && target.Health < 100 && confirmed == 1, "Rifle target damage or feedback failed.");
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cover.transform.position = Vector3.Lerp(gun.MuzzlePosition, gun.LastShot.end, .5f);
        cover.transform.localScale = new Vector3(2, 2, .1f);
        Physics.SyncTransforms();
        float before = target.Health;
        gun.ProcessTrigger(true, false, true, 101);
        Check(target.Health == before && confirmed == 1, "Cover awards false damage / hit marker.");
        cover.SetActive(false);
        EquipPrimary(scene, "weapon_snip_awp");
        player.Aim(camera.WorldToScreenPoint(fixture.transform.position), 1);
        gun.ProcessAim(false, true); gun.ProcessAim(true, true);
        gun.ProcessTrigger(false, false, true, 103); gun.ProcessTrigger(true, true, true, 103);
        Check(!target.IsAlive && confirmed == 2, "Scoped sniper shot failed.");
        int fired = gun.ShotsFired;
        gun.ProcessTrigger(true, false, true, 106);
        Check(gun.ShotsFired == fired, "Bolt-action rifle repeats while held.");
        gun.GetComponent<TrainingAimLaser>().UpdateBeam(true);
        Check(player.GetComponentsInChildren<LineRenderer>().Any(l => l.name == "Aim Laser" && l.enabled), "Scoped sniper has no persistent laser.");
        Object.Destroy(fixture); Object.Destroy(cover);
        player.transform.position = saved; player.transform.rotation = Quaternion.identity;
        player.GetComponent<TrainingUpperBodyAim>().enabled = true;
        gun.ResetEquipment(); EquipPrimary(scene, "weapon_rif_m4a1_silencer"); follow.Snap();
        camera.GetComponent<TrainingCameraCutaway>().enabled = true;
        yield return null;
        var panel = FindObjectOfType<TrainingPanel>(); panel.enabled = false;
        Capture(panel, player, gun, follow, "top-down-rifle.png", 1600, 900, false);
        EquipPrimary(scene, "weapon_snip_awp");
        Capture(panel, player, gun, follow, "top-down-sniper.png", 1600, 900, true);
        Capture(panel, player, gun, follow, "top-down-sniper-16x10.png", 1280, 800, true);
        EquipPrimary(scene, "weapon_rif_m4a1_silencer");
        SetOrbitAngles(follow, 50, 90);
        Capture(panel, player, gun, follow, "top-down-orbit-90.png", 1600, 900, false);
        SetOrbitAngles(follow, 70, 225);
        Capture(panel, player, gun, follow, "top-down-orbit-225.png", 1600, 900, false);
        Debug.Log("TOP_DOWN_BALLISTICS_PASS: true muzzle damage, cover rejection, bolt-action cadence, scoped sniper laser.");
        Debug.Log("TOP_DOWN_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private static void VerifyOrbit(TrainingCharacterController player, TrainingCameraController follow)
    {
        var camera = player.ViewCamera;
        Quaternion initialCamera = camera.transform.rotation;
        Vector3 position = player.transform.position;
        Quaternion facing = player.transform.rotation;
        float originalDistance = Vector3.Distance(camera.transform.position, follow.Focus);
        player.Aim(camera.WorldToScreenPoint(position + Vector3.forward * 4), 1);
        follow.Snap();
        Quaternion original = camera.transform.rotation;
        Vector3 aim = player.AimPoint;
        follow.ProcessOrbit(true, false, new Vector2(30, 30), true);
        Check(!follow.IsOrbiting, "Orbit starts without a middle press.");
        follow.ProcessOrbit(true, true, new Vector2(100, 100), true);
        follow.UpdateView(Vector2.one, false, true, 0, .016f);
        Check(follow.IsOrbiting && Quaternion.Angle(original, camera.transform.rotation) < .01f,
            "Middle press consumes mouse motion from before the drag.");
        follow.ProcessOrbit(true, false, new Vector2(30, 10f / 3), true);
        for (int i = 0; i < 120; i++) follow.UpdateView(Vector2.one, true, true, 0, 1f / 60);
        Quaternion rotated = camera.transform.rotation;
        Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, original.eulerAngles.y + 90)) < .01f
            && Mathf.Abs(camera.transform.eulerAngles.x - 50) < .01f, "Drag does not orbit horizontally / vertically.");
        Check(Vector3.Distance(follow.Focus, position + Vector3.up) < .001f, "Drag also moves aim look-ahead.");
        Check(Mathf.Abs(Vector3.Distance(camera.transform.position, follow.Focus) - originalDistance) < .001f,
            "Orbit changes camera distance.");
        Check(follow.ScopeBlend > .99f && camera.fieldOfView < 36, "Orbit breaks scope magnification.");
        player.Aim(new Vector2(10, 10), 1);
        Check(Vector3.Distance(player.AimPoint, aim) < .001f, "Orbit drag steers the world aim point.");
        Check(Vector2.Distance(player.AimScreenPosition, camera.WorldToScreenPoint(aim)) < .01f,
            "Orbit crosshair no longer projects the held aim point.");
        follow.ProcessOrbit(false, false, new Vector2(100, 100), true);
        for (int i = 0; i < 120; i++) follow.UpdateView(new Vector2(.6f, .2f), false, true, 0, 1f / 60);
        Check(!follow.IsOrbiting && Quaternion.Angle(rotated, camera.transform.rotation) < .01f,
            "Releasing middle mouse resets or keeps changing the view angle.");
        Check(Vector3.Distance(follow.Focus, position + Vector3.up) > 1,
            "Releasing orbit does not restore aim look-ahead.");
        player.Aim(new Vector2(camera.pixelWidth * .5f, camera.pixelHeight * .5f), 0);
        Check(Vector3.Distance(player.AimPoint, aim) > .1f, "Aim remains frozen after orbit release.");

        follow.ProcessOrbit(true, true, Vector2.zero, true);
        follow.ProcessOrbit(true, false, new Vector2(480, 1000), true);
        follow.UpdateView(Vector2.zero, false, true, 0, .016f);
        Check(Mathf.Abs(camera.transform.eulerAngles.x - 45) < .01f
            && Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, rotated.eulerAngles.y)) < .01f,
            "Orbit does not wrap full turns or clamp the lower pitch limit.");
        follow.ProcessOrbit(true, false, new Vector2(0, -1000), true);
        follow.UpdateView(Vector2.zero, false, true, 0, .016f);
        Check(Mathf.Abs(camera.transform.eulerAngles.x - 70) < .01f, "Upper pitch limit failed.");
        Quaternion limited = camera.transform.rotation;
        follow.ProcessOrbit(true, false, Vector2.one * 100, false);
        follow.ProcessOrbit(true, false, Vector2.one * 100, true);
        follow.UpdateView(Vector2.zero, false, true, 0, .016f);
        Check(!follow.IsOrbiting && Quaternion.Angle(limited, camera.transform.rotation) < .01f,
            "Blocked drag changes the view or resumes without a fresh press.");
        follow.InputEnabled = false;
        follow.ProcessOrbit(true, true, Vector2.one, true);
        Check(!follow.IsOrbiting, "Disabled camera accepts a drag.");
        follow.InputEnabled = true;
        follow.ProcessOrbit(true, true, Vector2.zero, true);
        follow.SendMessage("OnApplicationFocus", false);
        follow.ProcessOrbit(true, false, Vector2.one, true);
        Check(!follow.IsOrbiting, "Focus loss leaves an active drag.");

        // Same total mouse displacement split over 30 and 144 frames must end at the same pose.
        SetOrbitAngles(follow, original.eulerAngles.x, original.eulerAngles.y);
        follow.ProcessOrbit(true, true, Vector2.zero, true);
        for (int i = 0; i < 30; i++)
        {
            follow.ProcessOrbit(true, false, new Vector2(20, 3) / 30, true);
            follow.UpdateView(Vector2.zero, false, true, 0, 1f / 30);
        }
        Quaternion at30 = camera.transform.rotation;
        SetOrbitAngles(follow, original.eulerAngles.x, original.eulerAngles.y);
        follow.ProcessOrbit(true, true, Vector2.zero, true);
        for (int i = 0; i < 144; i++)
        {
            follow.ProcessOrbit(true, false, new Vector2(20, 3) / 144, true);
            follow.UpdateView(Vector2.zero, false, true, 0, 1f / 144);
        }
        Check(Quaternion.Angle(at30, camera.transform.rotation) < .05f, "Orbit depends on frame rate.");

        // Move above map geometry so collisions cannot mask the character-relative direction.
        var motor = player.GetComponent<CharacterController>();
        motor.enabled = false; player.transform.position = position + Vector3.up * 100; motor.enabled = true;
        player.Move(Vector2.up, false, false, .1f);
        Vector3 travel = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up).normalized;
        Vector3 characterForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        Check(Vector3.Dot(travel, characterForward) > .999f, "W does not follow character facing after a camera orbit.");
        motor.enabled = false; player.transform.SetPositionAndRotation(position, facing); motor.enabled = true;
        SetOrbitAngles(follow, initialCamera.eulerAngles.x, initialCamera.eulerAngles.y);
        follow.Snap();
        Debug.Log("TOP_DOWN_ORBIT_PASS: horizontal/vertical drag, retained offset, 45-70 pitch, full turns, fixed distance/aim, scope, release, input/focus gates, 30/144 fps, character-relative movement.");
    }

    private static void VerifyFacingControls(TrainingCharacterController player, TrainingCameraController follow)
    {
        var camera = player.ViewCamera;
        var motor = player.GetComponent<CharacterController>();
        Vector3 originalPosition = player.transform.position;
        Quaternion originalFacing = player.transform.rotation, originalCamera = camera.transform.rotation;
        Vector3 elevated = originalPosition + Vector3.up * 100;
        motor.enabled = false; player.transform.position = elevated; motor.enabled = true;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Facing Control Floor";
        floor.transform.position = elevated + Vector3.down * .5f;
        floor.transform.localScale = new Vector3(100, 1, 100);
        Physics.SyncTransforms();

        player.transform.rotation = Quaternion.Euler(0, 125, 0);
        SetOrbitAngles(follow, 60, 215); // Deliberately put camera and body at different headings.
        foreach (Vector2 input in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right, Vector2.one })
        {
            motor.enabled = false; player.transform.position = elevated; motor.enabled = true;
            player.Move(input, false, false, .1f);
            Vector3 velocity = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
            Vector3 expected = (player.transform.forward * input.y + player.transform.right * input.x).normalized;
            Check(Vector3.Dot(velocity.normalized, expected) > .999f && Mathf.Abs(velocity.magnitude - 4.5f) < .02f,
                "WASD does not use character axes or diagonal movement is faster: " + input);
            Check(Mathf.Abs(Mathf.DeltaAngle(player.transform.eulerAngles.y, 125)) < .01f,
                "Backward/strafe movement turns the character.");
        }
        motor.enabled = false; player.transform.position = elevated; motor.enabled = true;
        player.transform.rotation = Quaternion.Euler(0, 170, 0);
        for (int i = 0; i < 120; i++) follow.UpdateView(Vector2.zero, false, true, 0, 1f / 60);
        Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 260)) < .01f
            && Mathf.Abs(camera.transform.eulerAngles.x - 60) < .01f,
            "Following a turn loses the manual orbit offset or pitch.");

        player.transform.rotation = Quaternion.Euler(0, 350, 0);
        SetOrbitAngles(follow, 60, 350);
        player.transform.rotation = Quaternion.Euler(0, 10, 0);
        follow.UpdateView(Vector2.zero, false, true, 0, 1f / 60);
        float firstTurn = Mathf.DeltaAngle(350, camera.transform.eulerAngles.y);
        Check(firstTurn > 0 && firstTurn < 20, "Camera takes the long way around the 360-degree seam.");
        for (int i = 0; i < 120; i++) follow.UpdateView(Vector2.zero, false, true, 0, 1f / 60);
        Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, 10)) < .01f, "Camera does not settle on facing.");

        // Use actual mouse-aim, turn, camera-follow and look-ahead paths together. A stationary
        // physical mouse must not be reinterpreted in each newly rotated camera coordinate frame.
        player.Aim(camera.WorldToScreenPoint(elevated + new Vector3(5, 0, 4)), 1);
        Quaternion aimedFacing = player.transform.rotation;
        Vector3 heldAim = player.AimPoint;
        Vector3 settledFocus = Vector3.zero;
        for (int i = 0; i < 360; i++)
        {
            player.UpdateAimInput(Vector2.zero, true, 1f / 60);
            UpdateFollowingAim(player, follow);
            if (i == 240) settledFocus = follow.Focus;
        }
        Check(Quaternion.Angle(aimedFacing, player.transform.rotation) < .01f,
            "Camera-follow feeds back into aim and spins the character with an idle mouse.");
        Check(Vector3.Distance(heldAim, player.AimPoint) < .01f && Vector3.Distance(settledFocus, follow.Focus) < .002f,
            "Stationary aim or camera look-ahead drifts after a heading change.");
        Check(Mathf.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y, player.transform.eulerAngles.y)) < .01f,
            "Camera heading does not follow the mouse-aimed character.");

        Vector3 beforeWalking = player.transform.position;
        for (int i = 0; i < 240; i++)
        {
            player.UpdateAimInput(Vector2.zero, true, 1f / 60);
            player.Move(Vector2.up, false, false, 1f / 60);
            UpdateFollowingAim(player, follow);
        }
        Vector3 travel = Vector3.ProjectOnPlane(player.transform.position - beforeWalking, Vector3.up);
        Check(travel.magnitude > 17 && Vector3.Dot(travel.normalized, aimedFacing * Vector3.forward) > .999f
            && Quaternion.Angle(aimedFacing, player.transform.rotation) < .01f,
            "Holding W walks past a fixed aim point and turns the character around.");
        player.UpdateAimInput(new Vector2(-2, 0), true, 1);
        Check(Quaternion.Angle(aimedFacing, player.transform.rotation) > .1f, "Mouse motion no longer steers aiming.");
        Quaternion beforeBlocked = player.transform.rotation;
        player.UpdateAimInput(new Vector2(30, 0), false, 1);
        player.UpdateAimInput(new Vector2(30, 0), true, 1);
        Check(Quaternion.Angle(beforeBlocked, player.transform.rotation) < .01f,
            "Blocked input or the first frame after resume warps the aim.");

        Object.DestroyImmediate(floor);
        motor.enabled = false; player.transform.SetPositionAndRotation(originalPosition, originalFacing); motor.enabled = true;
        SetOrbitAngles(follow, originalCamera.eulerAngles.x, originalCamera.eulerAngles.y);
        player.Aim(camera.WorldToScreenPoint(originalPosition + originalFacing * Vector3.forward * 6), 0);
        follow.Snap();
        Debug.Log("TOP_DOWN_FACING_PASS: four local movement axes, normalized diagonal, strafing/backpedal, heading follow, orbit offset, shortest yaw wrap, stationary mouse/no spin, stable look-ahead, sustained W, mouse aim, menu resume.");
    }

    private static void UpdateFollowingAim(TrainingCharacterController player, TrainingCameraController follow)
    {
        Vector3 cursor = player.ViewCamera.ScreenToViewportPoint(player.AimScreenPosition);
        follow.UpdateView(new Vector2(cursor.x * 2 - 1, cursor.y * 2 - 1), false, true, 0, 1f / 60);
    }

    private static void SetOrbitAngles(TrainingCameraController follow, float pitch, float yaw)
    {
        follow.Snap();
        follow.ProcessOrbit(false, false, Vector2.zero, true);
        follow.ProcessOrbit(true, true, Vector2.zero, true);
        Vector3 angles = follow.transform.eulerAngles;
        follow.ProcessOrbit(true, false, new Vector2(Mathf.DeltaAngle(angles.y, yaw), angles.x - pitch) / 3, true);
        follow.UpdateView(Vector2.zero, false, true, 0, .1f);
        follow.ProcessOrbit(false, false, Vector2.zero, true);
    }

    private static void Capture(TrainingPanel panel, TrainingCharacterController player, TrainingWeaponController gun,
        TrainingCameraController follow, string path, int width, int height, bool scoped)
    {
        var camera = player.ViewCamera;
        var canvas = UIRoot.Instance.GetComponentInChildren<Canvas>();
        int previousCanvasLayer = canvas.gameObject.layer; canvas.gameObject.layer = 5;
        var scaler = canvas.GetComponent<CanvasScaler>(); bool previousScaler = scaler.enabled; scaler.enabled = false;
        float previousScale = canvas.scaleFactor; canvas.scaleFactor = Mathf.Sqrt((float)width / 1920 * height / 1080);
        var sceneTarget = new RenderTexture(width, height, 24); var output = new RenderTexture(width, height, 24);
        int previousMask = camera.cullingMask;
        camera.targetTexture = sceneTarget; camera.aspect = (float)width / height;
        follow.Snap();
        Vector2 cursor = new Vector2(.25f, .3f);
        for (int i = 0; i < 300; i++) follow.UpdateView(cursor, scoped, true, 0, 1f / 60);
        player.Aim(new Vector2(width * .625f, height * .65f), 1);
        // Capture the settled, character-facing view, including the reprojected reticle.
        for (int i = 0; i < 180; i++)
        {
            player.UpdateAimInput(Vector2.zero, true, 1f / 60);
            Vector3 projected = camera.ScreenToViewportPoint(player.AimScreenPosition);
            follow.UpdateView(new Vector2(projected.x * 2 - 1, projected.y * 2 - 1), scoped, true, 0, 1f / 60);
        }
        player.GetComponent<TrainingWeaponPresentation>().UpdatePresentation(0, true);
        player.GetComponent<TrainingUpperBodyAim>().ApplyAim();
        player.GetComponent<TrainingAimLaser>().UpdateBeam(true);
        // Bake animated skins for deterministic manual batch-mode rendering.
        var skins = Object.FindObjectsOfType<SkinnedMeshRenderer>().Where(s => s.enabled).ToArray();
        var snapshots = new GameObject[skins.Length]; var baked = new Mesh[skins.Length];
        for (int i = 0; i < skins.Length; i++)
        {
            baked[i] = new Mesh(); skins[i].BakeMesh(baked[i], true);
            snapshots[i] = new GameObject("Capture Skin", typeof(MeshFilter), typeof(MeshRenderer));
            snapshots[i].transform.SetParent(skins[i].transform, false);
            snapshots[i].GetComponent<MeshFilter>().sharedMesh = baked[i];
            snapshots[i].GetComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials; skins[i].enabled = false;
        }
        canvas.enabled = false;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = sceneTarget });
        var backdrop = new GameObject("Capture Background", typeof(RectTransform), typeof(RawImage)); backdrop.layer = 5;
        backdrop.transform.SetParent(canvas.transform, false); backdrop.transform.SetAsFirstSibling();
        var rect = (RectTransform)backdrop.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        backdrop.GetComponent<RawImage>().texture = sceneTarget; backdrop.GetComponent<RawImage>().raycastTarget = false;
        canvas.enabled = true; canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = .2f;
        camera.cullingMask = 1 << 5; camera.targetTexture = output;
        Canvas.ForceUpdateCanvases(); panel.SendMessage("RefreshSafeArea"); Canvas.ForceUpdateCanvases();
        var overlay = panel.GetComponentInChildren<TrainingCombatOverlay>();
        overlay.Present(player, gun, follow.ScopeBlend, true, camera); Canvas.ForceUpdateCanvases();
        Check(overlay.canvasRenderer != null && overlay.canvasRenderer.materialCount > 0, "Combat overlay has no renderer/material.");
        Check(!overlay.raycastTarget && overlay.GetComponentsInChildren<Graphic>().All(g => !g.raycastTarget), "Combat HUD blocks input.");
        foreach (var label in panel.GetComponentsInChildren<Text>())
            Check(label.preferredHeight <= label.rectTransform.rect.height + 1, "HUD text clipped: " + label.name);
        if (scoped) Check(overlay.ScopeAmount > .99f && overlay.ScopeCenter.magnitude > 30, "Scope is not centred on off-centre cursor.");
        var eventData = new PointerEventData(EventSystem.current) { position = new Vector2(width * .625f, height * .65f) };
        var hits = new System.Collections.Generic.List<RaycastResult>(); canvas.GetComponent<GraphicRaycaster>().Raycast(eventData, hits);
        Check(hits.Count == 0, "Scope intercepts shooting pointer.");
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = output });
        var active = RenderTexture.active; RenderTexture.active = output;
        var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG());
        Check(pixels.GetPixels32().Distinct().Take(64).Count() == 64, "Blank combat capture: " + path);
        RenderTexture.active = active;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; canvas.scaleFactor = previousScale; scaler.enabled = previousScaler;
        camera.targetTexture = null; camera.cullingMask = previousMask; camera.ResetAspect();
        canvas.gameObject.layer = previousCanvasLayer;
        Object.DestroyImmediate(backdrop); Object.DestroyImmediate(pixels);
        sceneTarget.Release(); output.Release(); Object.DestroyImmediate(sceneTarget); Object.DestroyImmediate(output);
        for (int i = 0; i < skins.Length; i++) { skins[i].enabled = true; Object.DestroyImmediate(snapshots[i]); Object.DestroyImmediate(baked[i]); }
        Debug.Log("TOP_DOWN_CAPTURE_PASS " + path);
    }
    private static bool EquipPrimary(TrainingSceneController scene, string id)
    {
        for (int i = 0; i <= scene.Weapons.Options(1).Count; i++)
        {
            if (scene.Player.GetComponent<TrainingWeaponController>().WeaponId == id) return true;
            if (!scene.EquipSlot(1)) return false;
        }
        return false;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
