#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class VisionVerificationDriver : MonoBehaviour
{
    private new Camera camera;
    private TrainingVision vision;
    private Transform player;
    private const int Width = 1280, Height = 800;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }

    private void OnLog(string text, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.AppendAllText("vision-failure.txt", text + "\n" + trace + "\n");
        EditorApplication.Exit(1);
    }

    private IEnumerator Start()
    {
        yield return null;
        var light = new GameObject("Sun").AddComponent<Light>();
        light.type = LightType.Directional; light.intensity = 1.5f;
        light.transform.rotation = Quaternion.Euler(55, -35, 0);
        camera = new GameObject("Sight Camera").AddComponent<Camera>();
        camera.fieldOfView = 52; camera.nearClipPlane = .08f;
        camera.transform.SetPositionAndRotation(new Vector3(13, 24, -15), Quaternion.Euler(55, -35, 0));
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        camera.allowHDR = false; camera.allowMSAA = false;
        player = new GameObject("Observer").transform;
        var body = Primitive("Player", new Vector3(0, .85f, 0), new Vector3(.55f, 1.7f, .55f), Color.cyan);
        body.transform.SetParent(player, true); body.layer = 2;
        Primitive("Floor", new Vector3(0, -.2f, 0), new Vector3(80, .4f, 80), new Color(.55f, .55f, .55f));
        // Ground markings make the visible cone and occluded ground easy to inspect.
        for (int x = -12; x <= 12; x += 2)
        for (int z = -10; z <= 16; z += 2)
            Primitive("Floor Marker", new Vector3(x, .015f, z), new Vector3(.08f, .02f, .08f), Color.white);
        var wall = Primitive("Tall Wall", new Vector3(0, 1.7f, 7), new Vector3(6, 3.4f, .5f), new Color(.72f, .5f, .22f));
        var visible = Target("Visible Target", new Vector3(5, 0, 7));
        var behind = Target("Behind Wall Target", new Vector3(0, 0, 10));
        var rear = Target("Rear Target", new Vector3(0, 0, -6));
        vision = camera.gameObject.AddComponent<TrainingVision>();
        vision.Follow(player);
        Physics.SyncTransforms();
        yield return null;
        Check(vision.CanSeePoint(new Vector3(0, 1.5f, 5)), "Clear front was hidden");
        Check(!vision.CanSeePoint(new Vector3(0, 1.5f, -6)), "Rear was visible");
        Check(!vision.CanSeePoint(new Vector3(10, 1.5f, 0)), "Side was visible");
        Check(vision.CanSeePoint(new Vector3(0, 1.5f, -1)), "Near awareness was hidden");
        Check(!vision.CanSeePoint(new Vector3(20, 1.5f, 40)), "Distant point was visible");
        Check(vision.CanSeeTarget(visible) && !vision.CanSeeTarget(behind) && !vision.CanSeeTarget(rear), "Target LOS mismatch");
        Capture("vision-warmup.png");
        var blockedPixels = Capture("vision-front.png");
        vision.enabled = false;
        var unrestrictedPixels = Capture("vision-disabled.png");
        vision.enabled = true;
        float frontRatio = Brightness(blockedPixels, new Vector3(3, 0, 4)) / Brightness(unrestrictedPixels, new Vector3(3, 0, 4));
        float rearRatio = Brightness(blockedPixels, new Vector3(0, 0, -4)) / Brightness(unrestrictedPixels, new Vector3(0, 0, -4));
        float wallRatio = Brightness(blockedPixels, new Vector3(0, 0, 11)) / Brightness(unrestrictedPixels, new Vector3(0, 0, 11));
        Check(frontRatio > .9f && rearRatio < .8f && wallRatio < .8f,
            $"Depth overlay pixels incorrect: front={frontRatio}, rear={rearRatio}, wall={wallRatio}");
        Debug.Log($"VISION_RENDER_PASS front={frontRatio:F3}, rear={rearRatio:F3}, wall={wallRatio:F3}");

        bool inspected = false;
        Action<ScriptableRenderContext, Camera> inspect = (context, renderingCamera) => {
            if (renderingCamera != camera) return;
            Check(behind.GetComponentInChildren<Renderer>().forceRenderingOff, "Wall target renderer leaked");
            Check(rear.GetComponentInChildren<Renderer>().forceRenderingOff, "Rear target renderer leaked");
            Check(!visible.GetComponentInChildren<Renderer>().forceRenderingOff, "Visible renderer hidden");
            inspected = true;
        };
        RenderPipelineManager.beginCameraRendering += inspect;
        Capture("vision-targets.png");
        RenderPipelineManager.beginCameraRendering -= inspect;
        Check(inspected && !behind.GetComponentInChildren<Renderer>().forceRenderingOff, "Render state not restored");
        Check(behind.GetComponentInChildren<Collider>().enabled, "Vision disabled target collision");
        var fresh = Target("Spawned Behind Wall", new Vector3(.6f, 0, 10));
        bool freshHidden = false;
        Action<ScriptableRenderContext, Camera> inspectSpawn = (context, renderingCamera) => {
            if (renderingCamera == camera) freshHidden = fresh.GetComponentInChildren<Renderer>().forceRenderingOff;
        };
        RenderPipelineManager.beginCameraRendering += inspectSpawn;
        Capture("vision-spawn.png");
        RenderPipelineManager.beginCameraRendering -= inspectSpawn;
        Check(freshHidden, "Newly spawned target leaked on first frame");
        fresh.GetComponentInChildren<Renderer>().forceRenderingOff = true;
        Capture("vision-preserved-state.png");
        Check(fresh.GetComponentInChildren<Renderer>().forceRenderingOff, "Pre-existing forced-off state was lost");
        fresh.gameObject.SetActive(false); Destroy(fresh.gameObject);
        // A second camera is never filtered by this observer.
        var spectator = new GameObject("Spectator").AddComponent<Camera>();
        spectator.CopyFrom(camera); spectator.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        var primary = camera; camera = spectator;
        var spectatorPixels = Capture("vision-spectator.png");
        Check(Brightness(spectatorPixels, new Vector3(0, 0, -4)) / Brightness(unrestrictedPixels, new Vector3(0, 0, -4)) > .95f,
            "Sight shading leaked to spectator");
        camera = primary; Destroy(spectator.gameObject);

        var character = player.gameObject.AddComponent<TrainingCharacterController>();
        character.Initialize(camera, Vector3.zero, Quaternion.identity); character.enabled = false;
        player.gameObject.layer = 2;
        var weapon = player.gameObject.AddComponent<TrainingWeaponController>(); weapon.enabled = false;
        var canvas = new GameObject("Verification HUD", typeof(Canvas)).GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var overlay = new GameObject("Combat", typeof(RectTransform)).AddComponent<TrainingCombatOverlay>();
        overlay.transform.SetParent(canvas.transform, false);
        overlay.Initialize(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        // Aim from overhead can pick a rear target; that must not reveal its HUD data.
        character.Aim(camera.WorldToScreenPoint(rear.transform.position + Vector3.up), 0);
        overlay.Present(character, weapon, 0, true, null);
        Check(!(bool)typeof(TrainingCombatOverlay).GetField("hasTarget", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay),
            "Rear target health/crosshair leaked");
        overlay.ShowHit(rear, 12, rear.transform.position + Vector3.up);
        Check((int)typeof(TrainingCombatOverlay).GetField("next", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay) == 0,
            "Hidden damage number leaked");
        character.Aim(camera.WorldToScreenPoint(visible.transform.position + Vector3.up), 0);
        overlay.Present(character, weapon, 0, true, null);
        Check((bool)typeof(TrainingCombatOverlay).GetField("hasTarget", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay),
            "Visible target HUD missing");
        overlay.ShowHit(visible, 12, visible.transform.position + Vector3.up);
        Check((int)typeof(TrainingCombatOverlay).GetField("next", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(overlay) == 1,
            "Visible damage feedback missing");
        Destroy(canvas.gameObject);
        Debug.Log("VISION_HUD_PASS: target highlight, health and damage numbers respect character sight");

        player.rotation = Quaternion.Euler(0, 180, 0);
        Check(vision.CanSeeTarget(rear) && !vision.CanSeeTarget(visible), "Rotation uses stale heading");
        Capture("vision-turned.png");
        player.rotation = Quaternion.identity;
        wall.transform.localScale = new Vector3(6, .6f, .5f); wall.transform.position = new Vector3(0, .3f, 7);
        Physics.SyncTransforms();
        Check(vision.CanSeeTarget(behind), "Low cover hid exposed head");
        vision.RefreshMask(true); Capture("vision-low-cover.png");
        wall.transform.localScale = new Vector3(6, 3.4f, .5f); wall.transform.position = new Vector3(0, 1.7f, 7);
        Physics.SyncTransforms();
        wall.GetComponent<Collider>().isTrigger = true;
        Check(vision.CanSeeTarget(behind), "Trigger blocked sight");
        wall.GetComponent<Collider>().isTrigger = false;
        // Near awareness must not reveal someone through a wall, even within the near radius.
        wall.transform.position = new Vector3(0, 1.7f, 1);
        Physics.SyncTransforms();
        Check(!vision.CanSeePoint(new Vector3(0, 1.5f, 2)), "Awareness revealed through wall");
        wall.SetActive(false);
        Physics.SyncTransforms();
        Check(vision.CanSeePoint(new Vector3(0, 1.5f, 2)), "Disabled wall still blocks");
        wall.SetActive(true); wall.transform.position = new Vector3(0, 1.7f, 7);
        player.position = new Vector3(10, 0, 2); player.LookAt(new Vector3(0, 0, 10));
        Physics.SyncTransforms();
        Check(vision.CanSeeTarget(behind), "Moving around corner did not reveal target");
        Capture("vision-corner.png");

        player.position = Vector3.zero; player.rotation = Quaternion.Euler(0, 45, 0);
        var follow = camera.gameObject.AddComponent<TrainingCameraController>();
        follow.InputEnabled = false; follow.Follow(player);
        var farPoint = new Vector3(28, 1.5f, 28);
        Check(!vision.CanSeePoint(farPoint), "Unscoped range exceeded");
        follow.UpdateView(Vector2.zero, true, true, 0, 1);
        Check(vision.CanSeePoint(farPoint), "Scope range did not extend");
        follow.UpdateView(Vector2.zero, false, true, 0, 1);
        Check(!vision.CanSeePoint(farPoint), "Unscope did not restore range");
        follow.enabled = false;
        Check(!vision.enabled, "Disabling camera did not disable vision");
        follow.enabled = true;
        Check(vision.enabled, "Enabling camera did not restore vision");
        follow.Follow(null);
        Check(!vision.HasObserver && vision.CanSeePoint(farPoint), "Clearing observer left stale vision");
        Debug.Log("VISION_RULES_PASS: cone, rear awareness, wall, trigger, low cover, corner, scope, lifecycle and target render state");

        yield return SceneManager.LoadSceneAsync("DustII");
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        Check(scene != null && scene.Player != null, "Training scene did not start");
        camera = scene.Player.ViewCamera;
        vision = camera.GetComponent<TrainingVision>();
        Check(vision != null && vision.HasObserver, "Dust II camera has no vision");
        player = scene.Player.transform;
        scene.Player.InputEnabled = false;
        camera.GetComponent<TrainingCameraController>().InputEnabled = false;
        player.rotation = Quaternion.Euler(0, 0, 0);
        vision.RefreshMask(true); Capture("vision-dustii.png");
        player.rotation = Quaternion.Euler(0, 135, 0);
        vision.RefreshMask(true); Capture("vision-dustii-turned.png");
        // Smoke and flash use the existing throwable simulation, so geometry and target/UI rules agree.
        var world = scene.Player.GetComponent<TrainingWeaponController>().Throwables.World;
        var throwableCatalog = Resources.Load<TrainingThrowableCatalog>("Training/TrainingThrowables");
        var sample = vision.EyePosition + player.forward;
        Check(vision.CanSeePoint(sample), "Smoke fixture starts blocked");
        var smoke = Array.Find(throwableCatalog.items, item => item.kind == TrainingThrowableKind.Smoke);
        world.Detonate(smoke, player.position + Vector3.up * .05f);
        for (int i = 0; i < 20; i++) world.Simulate(.05f);
        Check(!vision.CanSeePoint(sample), "Smoke did not hide sight target");
        vision.RefreshMask(true); Capture("vision-dustii-smoke.png");
        for (int i = 0; i < 400; i++) world.Simulate(.05f);
        Check(vision.CanSeePoint(sample), "Smoke expiry left hidden sight");
        world.ApplyFlash(vision.EyePosition + player.forward, 18, 4);
        Check(!vision.CanSeePoint(sample), "Flash leaked target sight");
        for (int i = 0; i < 100; i++) world.Simulate(.05f);
        Check(vision.CanSeePoint(sample), "Flash expiry left hidden sight");
        Debug.Log("VISION_THROWABLE_PASS: smoke/flash and expiry");
        Debug.Log("VISION_VERIFY_PASS");
        EditorApplication.Exit(0);
    }

    private static GameObject Primitive(string name, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name; go.transform.position = position; go.transform.localScale = scale;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * .3f);
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    private static TrainingTarget Target(string name, Vector3 position)
    {
        var root = new GameObject(name); root.transform.position = position;
        var body = Primitive("Body", position + Vector3.up * .85f, new Vector3(.55f, 1.7f, .55f), Color.red);
        body.transform.SetParent(root.transform, true);
        return root.AddComponent<TrainingTarget>();
    }

    private Color32[] Capture(string path)
    {
        var destination = new RenderTexture(Width, Height, 24);
        var previous = RenderTexture.active;
        var previousTarget = camera.targetTexture;
        camera.targetTexture = destination; camera.aspect = (float)Width / Height;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = destination });
        RenderTexture.active = destination;
        var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        var colors = pixels.GetPixels32();
        camera.targetTexture = previousTarget; RenderTexture.active = previous;
        destination.Release(); DestroyImmediate(destination); DestroyImmediate(pixels);
        return colors;
    }

    private float Brightness(Color32[] pixels, Vector3 point)
    {
        var viewport = camera.WorldToViewportPoint(point);
        var screen = new Vector2(viewport.x * Width, viewport.y * Height);
        int x = Mathf.Clamp(Mathf.RoundToInt(screen.x), 2, Width - 3);
        int y = Mathf.Clamp(Mathf.RoundToInt(screen.y), 2, Height - 3);
        float value = 0;
        for (int dx = -2; dx <= 2; dx++) for (int dy = -2; dy <= 2; dy++)
        { var c = pixels[(y + dy) * Width + x + dx]; value += c.r + c.g + c.b; }
        return value / 75;
    }

    private static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception("VISION_VERIFY_FAILED: " + message);
    }
}
#endif
