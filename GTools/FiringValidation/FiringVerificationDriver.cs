#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public sealed class FiringVerificationDriver : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        { File.WriteAllText("firing-failure.txt", message + "\n" + trace); EditorApplication.Exit(1); }
    }
    private IEnumerator Start()
    {
        yield return null; yield return null;
        UpperBodyAimVerification.Run();
        yield return null; // Release the fixture's world-space effects before checking the live player's pool.
        var scene = FindObjectOfType<TrainingSceneController>();
        var player = scene.Player;
        var gun = player.GetComponent<TrainingWeaponController>();
        Check(gun != null && gun.enabled, "Weapon controller was not initialized.");
        // Freeze movement and place the character in open space to test precise physics independently of map cover.
        player.enabled = false;
        // Bone/animation integration is covered above; keep these obstruction fixtures in a fixed pose.
        var upperBodyAim = player.GetComponent<TrainingUpperBodyAim>();
        upperBodyAim.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        Vector3 saved = player.transform.position;
        player.transform.position += Vector3.up * 100;
        player.transform.rotation = Quaternion.identity;
        var camera = Camera.main;
        var cameraController = camera.GetComponent<TrainingCameraController>();
        cameraController.enabled = false;
        PositionAimCamera(camera, player);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Firing Test Wall";
        wall.transform.position = gun.MuzzlePosition + Vector3.forward * 3;
        wall.transform.localScale = new Vector3(2, 2, .2f);
        var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.transform.position = gun.MuzzlePosition + Vector3.forward;
        trigger.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(wall.transform.position), 0);
        gun.ProcessTrigger(false, false, true, 100);
        gun.ProcessTrigger(true, true, true, 100);
        Check(gun.ShotsFired == 1 && gun.LastShot.hasHit && gun.LastShot.hit.collider == wall.GetComponent<Collider>(), "Shot failed nearest-solid / trigger-ignore test.");
        Check(gun.LastShot.damage > 0 && gun.LastShot.damage <= 38, "Invalid range-adjusted damage.");
        gun.ProcessTrigger(true, false, true, 100.05);
        Check(gun.ShotsFired == 1, "Cooldown ignored.");
        gun.ProcessTrigger(true, false, true, 100.1);
        Check(gun.ShotsFired == 2, "Held trigger did not fire.");
        Check(FindObjectsOfType<LineRenderer>().Count(line => line.name == "Bullet Tracer") == 32, "Tracer pool not bounded.");
        var laser = player.GetComponent<TrainingAimLaser>();
        var beam = player.transform.Find("Aim Laser").GetComponent<LineRenderer>();
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        Check(player.GetComponent<AudioSource>().isPlaying, "Shot audio did not play.");
        bool particles = false;
        foreach (var ps in FindObjectsOfType<ParticleSystem>()) particles |= ps.particleCount > 0;
        Check(particles, "No muzzle/impact particles emitted.");

        Vector3 muzzle = gun.MuzzlePosition;
        Vector3 breech = player.transform.position + Vector3.up * (muzzle.y - player.transform.position.y);
        wall.transform.position = Vector3.Lerp(breech, muzzle, .6f);
        wall.transform.localScale = new Vector3(2, 2, .04f);
        Physics.SyncTransforms();
        gun.ProcessTrigger(true, false, true, 100.2);
        Check(gun.LastShot.hit.collider == wall.GetComponent<Collider>() && Vector3.Distance(gun.LastShot.origin, breech) < .01f,
            "Barrel fired through nearby cover.");
        laser.UpdateBeam(true);
        Check(!beam.enabled, "Laser illuminated through cover behind the muzzle.");
        wall.SetActive(false); trigger.SetActive(false);
        camera.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1, 1));
        player.Aim(camera.pixelRect.center, 0);
        gun.ProcessTrigger(true, false, true, 100.3);
        Check(!gun.LastShot.hasHit && Vector3.Distance(gun.LastShot.origin, gun.LastShot.end) > 200, "Miss range is wrong.");
        Check(gun.LastShot.end.y > gun.LastShot.origin.y, "Empty-space aim lost its vertical direction.");
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        int shots = gun.ShotsFired;
        scene.RequestReturnToLobby();
        gun.ProcessTrigger(true, false, true, 101);
        Check(gun.ShotsFired == shots, "Popup allows firing.");
        laser.UpdateBeam(true);
        Check(!beam.enabled, "Popup leaves the laser visible.");
        UIRoot.Instance.HandleBackInput();
        Check(!scene.IsInputBlocked, "Popup did not close.");
        // Advance the scene's input gate without letting real (released) batch-mode mouse input
        // interrupt this simulated continuous hold between frames.
        player.InputEnabled = !scene.IsInputBlocked;
        gun.ProcessTrigger(true, false, true, 102);
        Check(gun.ShotsFired == shots, "Closing popup reused held click.");
        gun.ProcessTrigger(false, false, true, 102);
        gun.ProcessTrigger(true, true, true, 103);
        Check(gun.ShotsFired == shots + 1, "Firing did not resume after release.");
        Debug.Log("FIRING_PHYSICS_INPUT_PASS");
        laser.UpdateBeam(true);
        Check(beam.enabled, "Laser did not return after closing the popup.");
        gun.enabled = false;
        Check(!beam.enabled, "Disabling the weapon leaves the laser visible.");
        gun.enabled = true;
        laser.UpdateBeam(true);
        Check(beam.enabled, "Re-enabled weapon has no laser.");
        laser.UpdateBeam(false);
        Check(!beam.enabled, "Inactive view leaves the laser visible.");
        CheckAimGeometry(player, gun, camera);
        CheckLaserGeometry(player, gun, camera);
        BulletEffectsVerification.Run();
        Destroy(wall); Destroy(trigger);
        player.transform.position = saved;
        cameraController.enabled = true;
        cameraController.Snap();
        player.Aim(camera.WorldToScreenPoint(saved + Vector3.forward * 5), 1);
        upperBodyAim.enabled = true;
        yield return new WaitForSeconds(.2f);
        player.Aim(camera.WorldToScreenPoint(saved + Vector3.forward * 5), 0);
        gun.ProcessTrigger(false, false, true, 130);
        gun.ProcessTrigger(true, true, true, 130);
        // Update the particle render data before the manual render performed in this same frame.
        foreach (var ps in FindObjectsOfType<ParticleSystem>()) ps.Simulate(.005f, false, false);
        laser.UpdateBeam(true);
        Capture("training-firing.png");
        yield return new WaitForSeconds(1.8f);
        foreach (var line in FindObjectsOfType<LineRenderer>().Where(line => line.name == "Bullet Tracer")) Check(!line.enabled, "Tracer did not expire.");
        laser.UpdateBeam(true);
        Check(beam.enabled, "Persistent laser expired with the bullet tracers.");
        Check(!ShaderUtil.ShaderHasError(beam.sharedMaterial.shader), "Laser shader has compilation errors.");
        Capture("training-aim-laser.png");
        SceneManager.LoadScene("Assets/Scenes/SampleScene.unity");
        yield return null; yield return null;
        Check(GameObject.Find("Training Shot Effects") == null, "Effects survived scene exit.");
        Check(FindObjectOfType<TrainingAimLaser>() == null && GameObject.Find("Aim Laser") == null, "Laser survived scene exit.");
        Debug.Log("AIM_LASER_LIFECYCLE_PASS");
        Debug.Log("FIRING_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private static void PositionAimCamera(Camera camera, TrainingCharacterController player)
    {
        camera.transform.position = player.transform.position + new Vector3(0, 8, -6);
        camera.transform.LookAt(player.transform.position + Vector3.forward * 6);
    }

    private static void CheckAimGeometry(TrainingCharacterController player, TrainingWeaponController gun, Camera camera)
    {
        PositionAimCamera(camera, player);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Aim Test Ground";
        floor.transform.position = player.transform.position + Vector3.down * .1f;
        floor.transform.localScale = new Vector3(40, .2f, 40);
        Vector3 target = player.transform.position + new Vector3(4, 0, 7);

        // Neither a trigger nor the local player's Ignore Raycast layer may replace the surface under the cursor.
        var trigger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        trigger.transform.position = Vector3.Lerp(camera.transform.position, target, .35f);
        trigger.GetComponent<Collider>().isTrigger = true;
        var equipment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        equipment.transform.SetParent(player.transform, true);
        equipment.layer = player.gameObject.layer;
        equipment.transform.position = Vector3.Lerp(camera.transform.position, target, .55f);
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(target), 0);
        Check(Vector3.Distance(player.AimPoint, target) < .01f, "Crosshair did not resolve the actual ground surface.");
        var laser = player.GetComponent<TrainingAimLaser>();
        var beam = player.transform.Find("Aim Laser").GetComponent<LineRenderer>();
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        gun.ProcessTrigger(false, false, true, 110);
        gun.ProcessTrigger(true, true, true, 110);
        Check(gun.LastShot.hasHit && gun.LastShot.hit.collider == floor.GetComponent<Collider>()
            && Vector3.Distance(gun.LastShot.origin, gun.MuzzlePosition) < .001f
            && Vector3.Distance(gun.LastShot.end, target) < .02f
            && gun.LastShot.end.y < gun.LastShot.origin.y, "Shot did not travel from the muzzle down to the crosshair.");
        trigger.SetActive(false); equipment.SetActive(false);

        // Low cover intersects only the muzzle path; the camera still sees the ground target behind it.
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cover.name = "Aim Test Near Cover";
        cover.transform.position = Vector3.Lerp(gun.MuzzlePosition, target, .45f);
        cover.transform.localScale = Vector3.one * .5f;
        var fartherCover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fartherCover.transform.position = Vector3.Lerp(gun.MuzzlePosition, target, .7f);
        fartherCover.transform.localScale = Vector3.one * .5f;
        Physics.SyncTransforms();
        Check(Vector3.Distance(player.AimPoint, target) < .01f, "Cover fixture hides the camera target.");
        gun.ProcessTrigger(true, false, true, 110.1);
        Check(gun.LastShot.hasHit && gun.LastShot.hit.collider == cover.GetComponent<Collider>()
            && Vector3.Distance(gun.LastShot.origin, gun.MuzzlePosition) < .001f
            && Vector3.Distance(gun.LastShot.end, target) > 1, "Shot bypassed the nearest cover on the muzzle path.");
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        cover.SetActive(false); fartherCover.SetActive(false);

        // A high target must produce an upward shot even while the body is facing elsewhere.
        var highTarget = GameObject.CreatePrimitive(PrimitiveType.Cube);
        highTarget.transform.position = gun.MuzzlePosition + new Vector3(-3, 3, 8);
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(highTarget.transform.position), 0);
        Vector3 highAim = player.AimPoint;
        gun.ProcessTrigger(true, false, true, 110.2);
        Check(gun.LastShot.hasHit && gun.LastShot.hit.collider == highTarget.GetComponent<Collider>()
            && Vector3.Distance(gun.LastShot.end, highAim) < .02f
            && gun.LastShot.end.y > gun.LastShot.origin.y, "Shot did not converge on an elevated crosshair target.");
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        highTarget.SetActive(false);

        // A camera move/zoom after Update must be reflected when LateUpdate fires using the same screen coordinate.
        Vector2 screenTarget = camera.WorldToScreenPoint(target);
        player.Aim(screenTarget, 0);
        camera.transform.position += Vector3.right * 2;
        gun.ProcessTrigger(true, false, true, 110.3);
        Check(gun.LastShot.hasHit && Vector3.Distance(gun.LastShot.end, target + Vector3.right * 2) < .02f,
            "Shot used a stale aim point after the camera moved.");
        laser.UpdateBeam(true);
        CheckLaserPose(player, gun, beam);
        player.GetComponent<TrainingWeaponPresentation>().UpdatePresentation(.06f, true);
        player.GetComponent<TrainingWeaponPresentation>().UpdatePresentation(0, true);
        laser.UpdateBeam(true);
        Check(Vector3.Distance(beam.GetPosition(0), gun.MuzzlePosition) < .001f, "Laser detached from animated muzzle.");

        // Turning uses the same world target but must keep the character upright.
        player.Aim(camera.WorldToScreenPoint(target), 1);
        Vector3 facing = Vector3.ProjectOnPlane(target - player.transform.position, Vector3.up).normalized;
        Check(Vector3.Dot(player.transform.forward, facing) > .999f && Vector3.Dot(player.transform.up, Vector3.up) > .999f,
            "Body turn did not follow the surface target or tilted the character.");
        floor.SetActive(false);

        // Camera picking may see farther than the gun can shoot; never use that camera hit as the bullet hit.
        var definition = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons").Find(gun.WeaponId);
        highTarget.SetActive(true);
        highTarget.transform.position = gun.MuzzlePosition + new Vector3(0, 1, 1).normalized * (definition.range + 20);
        camera.transform.LookAt(highTarget.transform);
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(highTarget.transform.position), 0);
        Check(Vector3.Distance(player.AimPoint, highTarget.transform.position) < 1, "Out-of-range fixture was not picked by the camera.");
        gun.ProcessTrigger(true, false, true, 110.4);
        Check(!gun.LastShot.hasHit && Mathf.Abs(Vector3.Distance(gun.LastShot.origin, gun.LastShot.end) - definition.range) < .02f,
            "Shot reached a camera target beyond the weapon range.");
        highTarget.SetActive(false);
        Destroy(floor); Destroy(trigger); Destroy(equipment); Destroy(cover); Destroy(fartherCover); Destroy(highTarget);
        Debug.Log("FIRING_AIM_GEOMETRY_PASS: ground, elevated target, nearest muzzle obstruction, ignored triggers/player, camera movement, upright turn, weapon range.");
    }

    private static void CheckLaserPose(TrainingCharacterController player, TrainingWeaponController gun, LineRenderer beam)
    {
        var equipped = player.GetComponentInChildren<OperationBlacktide.Client.Features.Lobby.LobbyLoadoutPreview>().EquippedWeapon;
        Vector3 direction = equipped.GetComponentInChildren<MeshFilter>().transform.TransformDirection(Vector3.down);
        Vector3 line = beam.GetPosition(1) - beam.GetPosition(0);
        Check(beam.enabled && Vector3.Distance(beam.GetPosition(0), gun.MuzzlePosition) < .001f
            && Vector3.Angle(direction, line) < .05f && line.magnitude <= 30.001f,
            "Laser detached from the actual barrel or exceeded 30 metres.");
    }

    private static void CheckLaserGeometry(TrainingCharacterController player, TrainingWeaponController gun, Camera camera)
    {
        var equipped = player.GetComponentInChildren<OperationBlacktide.Client.Features.Lobby.LobbyLoadoutPreview>().EquippedWeapon;
        var barrel = equipped.GetComponentInChildren<MeshFilter>().transform;
        Quaternion rotation = equipped.localRotation;
        var laser = player.GetComponent<TrainingAimLaser>();
        var beam = player.transform.Find("Aim Laser").GetComponent<LineRenderer>();
        // Freeze a horizontal barrel in empty space, deliberately different from the mouse aim.
        equipped.rotation = Quaternion.FromToRotation(barrel.TransformDirection(Vector3.down), Vector3.right) * equipped.rotation;
        laser.UpdateBeam(true);
        Vector3 origin = gun.MuzzlePosition;
        Check(beam.enabled && Vector3.Distance(beam.GetPosition(1), origin + Vector3.right * 30) < .001f,
            "Unobstructed laser is not exactly 30 metres along the barrel.");
        Vector3 endpoint = beam.GetPosition(1);
        camera.transform.position += Vector3.forward * 4;
        player.Aim(camera.pixelRect.center, 0);
        laser.UpdateBeam(true);
        Check(Vector3.Distance(endpoint, beam.GetPosition(1)) < .001f, "Moving the camera/cursor steers a frozen barrel's laser.");
        var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blocker.transform.position = origin + Vector3.right * 8;
        blocker.transform.localScale = Vector3.one;
        var ignored = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ignored.transform.position = origin + Vector3.right * 4;
        ignored.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms(); laser.UpdateBeam(true);
        Check(Vector3.Distance(beam.GetPosition(1), origin + Vector3.right * 7.5f) < .001f,
            "Laser does not stop at the first solid surface / ignores triggers incorrectly.");
        ignored.GetComponent<Collider>().isTrigger = false; ignored.layer = 2;
        blocker.transform.position = origin + Vector3.right * 40;
        Physics.SyncTransforms(); laser.UpdateBeam(true);
        Check(Vector3.Distance(beam.GetPosition(1), origin + Vector3.right * 30) < .001f,
            "Distant geometry or Ignore Raycast equipment changes the 30m length.");
        blocker.SetActive(false); ignored.SetActive(false);
        // Sweep upward with the weapon, as in reload, without moving the cursor.
        Vector3 raised = new Vector3(0, 1, 1).normalized;
        equipped.rotation = Quaternion.FromToRotation(barrel.TransformDirection(Vector3.down), raised) * equipped.rotation;
        laser.UpdateBeam(true);
        Check(Vector3.Distance(beam.GetPosition(1), gun.MuzzlePosition + raised * 30) < .001f,
            "Rotated barrel leaves the laser pointing at the cursor.");
        equipped.localRotation = rotation;
        Destroy(blocker); Destroy(ignored);
        Debug.Log("AIM_LASER_GEOMETRY_PASS: fixed 30m, frozen cursor independence, animated direction, nearest solid, ignored triggers/player.");
    }

    private static void Capture(string path)
    {
        var camera = Camera.main;
        var target = new RenderTexture(1280, 720, 24);
        var previous = camera.targetTexture;
        var active = RenderTexture.active;
        camera.targetTexture = target;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = previous; RenderTexture.active = active;
        Destroy(image); target.Release(); Destroy(target);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
