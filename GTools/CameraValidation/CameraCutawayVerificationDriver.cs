#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class CameraCutawayVerificationDriver : MonoBehaviour
{
    private const int Width = 960, Height = 540;
    private new Camera camera;
    private TrainingCameraController controller;
    private Transform player;
    private TrainingCameraCutaway cutaway;
    private GameObject wall;
    private Material wallMaterial;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.logMessageReceived += OnLog;
    }

    private void OnLog(string text, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.AppendAllText("camera-failure.txt", text + "\n" + trace + "\n");
        EditorApplication.Exit(1);
    }

    private IEnumerator Start()
    {
        yield return null;
        var light = new GameObject("Verification Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2;
        light.transform.rotation = Quaternion.Euler(60, 0, 0);
        camera = new GameObject("Verification Camera").AddComponent<Camera>();
        camera.fieldOfView = 48;
        camera.nearClipPlane = .08f;
        camera.aspect = (float)Width / Height;
        camera.backgroundColor = Color.black;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        controller = camera.gameObject.AddComponent<TrainingCameraController>();
        controller.InputEnabled = false;
        player = Primitive("Player", PrimitiveType.Capsule, new Vector3(0, .9f, 0), new Vector3(.7f, .9f, .7f), Color.blue).transform;
        var playerRoot = new GameObject("Player Root").transform;
        player.SetParent(playerRoot, true);
        player = playerRoot;
        var floor = Primitive("Floor", PrimitiveType.Cube, new Vector3(0, -.1f, 0), new Vector3(30, .2f, 30), Color.green);
        Quaternion rotation = Quaternion.Euler(60, 0, 0);
        Vector3 direction = rotation * Vector3.back;
        wall = Primitive("Occluding Wall", PrimitiveType.Cube, Vector3.up + direction * 6, new Vector3(7, 7, .5f), Color.red);
        wall.transform.rotation = rotation;
        wallMaterial = wall.GetComponent<Renderer>().sharedMaterial;
        var building = new GameObject("Whole Building");
        building.AddComponent<TrainingOcclusionGroup>();
        wall.transform.SetParent(building.transform, true);
        var overlay = Primitive("Visual Only Overlay", PrimitiveType.Cube, Vector3.up + direction * 7, new Vector3(7, 7, .05f), Color.red);
        overlay.transform.rotation = rotation;
        overlay.transform.SetParent(building.transform, true);
        Destroy(overlay.GetComponent<Collider>());
        var neighbor = Primitive("Unrelated Wall", PrimitiveType.Cube, new Vector3(15, 2, 0), new Vector3(3, 4, 1), Color.red);
        neighbor.GetComponent<Renderer>().sharedMaterial = wallMaterial;
        controller.Follow(player);
        cutaway = camera.GetComponent<TrainingCameraCutaway>();
        Check(cutaway != null, "Follow did not initialize fading.");
        yield return null;
        Physics.SyncTransforms();

        foreach (float distance in new[] { 8f, 13f, 20f })
        {
            typeof(TrainingCameraController).GetField("distance", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(controller, distance);
            controller.Snap();
            Check(Mathf.Abs(Vector3.Distance(camera.transform.position, Vector3.up) - distance) < .001f,
                "Building changed the requested camera distance at " + distance);
            Check(Physics.Raycast(camera.transform.position, (Vector3.up - camera.transform.position).normalized,
                out var hit, distance) && hit.collider == wall.GetComponent<Collider>(), "Clipping changed physical wall collision.");
            cutaway.enabled = false;
            yield return null;
            Check(wall.GetComponent<Renderer>().sharedMaterial == wallMaterial && camera.useOcclusionCulling,
                "Disabling cutaway did not restore renderer/camera state.");
            Color32[] blocked = Capture("camera-blocked-" + distance + ".png");
            cutaway.enabled = true;
            yield return null;
            Color32[] clear = Capture("camera-cutaway-" + distance + ".png");
            Check(wallMaterial.GetColor("_BaseColor").a == 1 && neighbor.GetComponent<Renderer>().sharedMaterial == wallMaterial,
                "Fading mutated a shared material or another building.");
            Check(floor.GetComponent<Renderer>().sharedMaterial.GetFloat("_Surface") == 0, "Floor became transparent.");
            int visiblePlayer = clear.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100);
            int hiddenPlayer = blocked.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100);
            Check(visiblePlayer > 100 && hiddenPlayer < 10, "Cutaway failed to reveal the player at " + distance);
            int offset = Mathf.RoundToInt(150 * 13 / distance);
            Color32 panel = clear[(Height / 2) * Width + Width / 2 + offset];
            Color32 opaque = blocked[(Height / 2) * Width + Width / 2 + offset];
            Check(panel.g > opaque.g + 40 && panel.r > 20,
                "Wall must remain translucent far outside the player silhouette, not a circle. " + panel + " vs " + opaque);
            Check(clear.Count(c => c.g > c.r + 25 && c.g > c.b + 25 && c.g > 100) > 100,
                "Cutaway removed the floor.");
            Debug.Log("CAMERA_DISTANCE_PASS " + distance + " visiblePixels=" + visiblePlayer);
        }

        VerifyAimPicking(floor, building);

        // Other cameras must see the uncut building despite shared runtime materials.
        var observer = new GameObject("Other Camera").AddComponent<Camera>();
        observer.CopyFrom(camera);
        observer.transform.SetPositionAndRotation(camera.transform.position, camera.transform.rotation);
        Camera primary = camera;
        camera = observer;
        var otherView = Capture("camera-other-view.png");
        Check(otherView.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100) < 10,
            "Training cutaway leaked into another camera.");
        camera = primary;
        Destroy(observer.gameObject);

        player.position = Vector3.right * 8;
        controller.Snap();
        Physics.SyncTransforms();
        Color32[] afterLeaving = Capture("camera-left-obstruction.png");
        Vector3 oldWallScreen = camera.WorldToScreenPoint(wall.transform.position);
        Color32 restoredWall = afterLeaving[Mathf.RoundToInt(oldWallScreen.y) * Width + Mathf.RoundToInt(oldWallScreen.x)];
        Check(restoredWall.r > restoredWall.g * 2, "Building did not become opaque after leaving its line of sight.");
        player.position = Vector3.zero;
        controller.Snap();
        Physics.SyncTransforms();

        wall.SetActive(false);
        overlay.SetActive(false);
        cutaway.enabled = false;
        Color32[] unobstructed = Capture("camera-unobstructed.png");
        cutaway.enabled = true;
        Color32[] unobstructedCutaway = Capture("camera-unobstructed-cutaway.png");
        Check(unobstructed.Zip(unobstructedCutaway, (a, b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g)
            + Mathf.Abs(a.b - b.b)).Count(d => d > 6) < 100, "Cutaway changed the unobstructed floor or player.");
        wall.SetActive(true);
        overlay.SetActive(true);

        // A camera inside solid geometry must also keep distance and reveal the player.
        wall.transform.position = camera.transform.position;
        wall.transform.localScale = new Vector3(8, 8, 3);
        Physics.SyncTransforms();
        controller.Snap();
        var inside = Capture("camera-inside-building.png");
        Check(inside.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100) > 100,
            "Camera inside building failed to reveal the player.");

        controller.Follow(null);
        Check(wall.GetComponent<Renderer>().sharedMaterial == wallMaterial && camera.useOcclusionCulling,
            "Clearing the follow target did not restore materials.");
        controller.Follow(player);
        controller.enabled = false;
        Check(wall.GetComponent<Renderer>().sharedMaterial == wallMaterial, "Disabled controller left cutaway active.");
        controller.enabled = true;
        Capture("camera-reenabled.png");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        Check(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Building material shader has compilation errors.");

        // Capture the real imported map with an actual foreground obstruction.
        controller.Follow(null);
        Destroy(player.gameObject);
        yield return SceneManager.LoadSceneAsync("Assets/Scenes/DustII.unity");
        camera = Camera.main;
        var viewer = camera.GetComponent<OperationBlacktide.Maps.DustIIViewer>();
        if (viewer != null) viewer.enabled = false;
        controller = camera.GetComponent<TrainingCameraController>();
        if (controller == null) controller = camera.gameObject.AddComponent<TrainingCameraController>();
        controller.InputEnabled = false;
        player = new GameObject("Map Camera Target").transform;
        bool found = false;
        Physics.SyncTransforms();
        for (int x = -25; x <= 25 && !found; x += 2)
        for (int z = -25; z <= 25 && !found; z += 2)
        {
            if (!Physics.Raycast(new Vector3(x, 20, z), Vector3.down, out var ground, 40) || ground.normal.y < .85f) continue;
            Vector3 focus = ground.point + Vector3.up;
            if (!Physics.Raycast(focus, direction, out var obstruction, 13) || obstruction.distance < 2 || obstruction.distance > 11 || obstruction.collider.bounds.size.y < 3 || Mathf.Max(obstruction.collider.bounds.size.x, obstruction.collider.bounds.size.z) < 8) continue;
            string obstructionName = obstruction.collider.name.ToLowerInvariant();
            if (!obstructionName.Contains("plaster") && !obstructionName.Contains("roof") && !obstructionName.Contains("brick")) continue;
            player.position = ground.point + Vector3.up * .05f;
            found = true;
        }
        Check(found, "No map obstruction found for regression capture.");
        var standIn = Primitive("Player Marker", PrimitiveType.Capsule, player.position + Vector3.up * .9f,
            new Vector3(.7f, .9f, .7f), Color.blue);
        standIn.transform.SetParent(player, true);
        controller.Follow(player);
        cutaway = camera.GetComponent<TrainingCameraCutaway>();
        cutaway.enabled = false;
        Color32[] mapBefore = Capture("camera-dustii-before.png");
        cutaway.enabled = true;
        Color32[] mapAfter = Capture("camera-dustii-after.png");
        Check(mapBefore.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100) < 20,
            "Map verification building did not obstruct the player.");
        Check(mapAfter.Count(c => c.b > c.r + 15 && c.b > c.g + 35 && c.b > 100) > 100,
            "Map building remained opaque over the player.");
        Physics.SyncTransforms();
        Ray mapAim = camera.ScreenPointToRay(camera.WorldToScreenPoint(standIn.transform.position));
        Check(Physics.Raycast(mapAim, out var mapPhysical, camera.farClipPlane)
            && mapPhysical.collider != standIn.GetComponent<Collider>(), "Map aim fixture does not intersect the foreground building.");
        Check(cutaway.RaycastAim(mapAim, out var mapPicked, camera.farClipPlane)
            && mapPicked.collider == standIn.GetComponent<Collider>(), "Dust II's translucent building still blocks visible target selection.");
        Check(!ShaderUtil.ShaderHasError(shader), "A map material variant failed to compile.");
        Debug.Log("CAMERA_VERIFY_PASS: whole-building alpha blending at 8/13/20m, separate building pieces, no circle, preserved floor/collision, shared-material isolation, camera inside wall, camera isolation, restoration, Dust II capture at " + player.position);
        EditorApplication.Exit(0);
    }

    private void VerifyAimPicking(GameObject floor, GameObject building)
    {
        foreach (var child in player.GetComponentsInChildren<Transform>()) child.gameObject.layer = 2;
        var character = player.gameObject.AddComponent<TrainingCharacterController>();
        character.enabled = false;
        character.Initialize(camera, player.position, player.rotation);
        player.GetComponent<CharacterController>().enabled = false;
        Vector3 groundPoint = new Vector3(1.5f, 0, 0);
        Vector3 direction = camera.transform.rotation * Vector3.back;
        // More than the initial 32-hit buffer: every translucent shell must be skipped.
        var layers = new GameObject[40];
        for (int i = 0; i < layers.Length; i++)
        {
            var layer = layers[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
            layer.name = "Building Roof Layer " + i;
            layer.transform.SetParent(building.transform, false);
            layer.transform.SetPositionAndRotation(Vector3.up + direction * (3 + i * .15f), camera.transform.rotation);
            layer.transform.localScale = new Vector3(7, 7, .04f);
            layer.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
        }
        var aimTarget = Primitive("Aim Target", PrimitiveType.Cube, new Vector3(1.5f, 1, 0), new Vector3(.4f, .6f, .4f), Color.yellow);
        aimTarget.AddComponent<TrainingTarget>();
        controller.Follow(player);
        Physics.SyncTransforms();
        Ray ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(groundPoint));
        Check(Physics.Raycast(ray, out var physical, camera.farClipPlane) && physical.collider != floor.GetComponent<Collider>(),
            "Aim fixture has no foreground obstruction.");
        // No render yet: the first aim query must already know what this camera will fade.
        character.Aim(camera.WorldToScreenPoint(groundPoint), 0);
        Check(character.TryGetAimHit(out var picked) && picked.collider == floor.GetComponent<Collider>()
            && Vector3.Distance(character.AimPoint, groundPoint) < .01f,
            "First-frame aiming stops on faded buildings instead of the visible ground.");
        Check(Physics.Raycast(ray, out var stillPhysical, camera.farClipPlane) && stillPhysical.collider == physical.collider,
            "Aim filtering changed physical wall collision or global raycasts.");

        character.Aim(camera.WorldToScreenPoint(aimTarget.transform.position), 0);
        Check(character.TryGetAimHit(out picked) && picked.collider == aimTarget.GetComponent<Collider>()
            && picked.collider.GetComponent<TrainingTarget>() != null, "Visible target behind a faded roof cannot be selected.");
        aimTarget.GetComponent<Collider>().isTrigger = true;
        Physics.SyncTransforms();
        Check(character.TryGetAimHit(out picked) && picked.collider == floor.GetComponent<Collider>(), "Aim filtering selects triggers.");
        aimTarget.GetComponent<Collider>().isTrigger = false;

        character.Aim(camera.WorldToScreenPoint(groundPoint), 0);
        floor.SetActive(false);
        Physics.SyncTransforms();
        Check(!character.TryGetAimHit(out picked) && Vector3.Distance(character.AimPoint, ray.GetPoint(camera.farClipPlane)) < .01f,
            "All-filtered ray does not use the empty-space aim fallback.");
        floor.SetActive(true);

        // Nested collision shapes must share their visible parent's cutaway status.
        wall.GetComponent<Collider>().enabled = false;
        var childCollider = new GameObject("Nested Building Collider");
        childCollider.transform.SetParent(wall.transform, false);
        childCollider.AddComponent<BoxCollider>();
        Physics.SyncTransforms();
        Check(character.TryGetAimHit(out picked) && picked.collider == floor.GetComponent<Collider>(), "A child collider still blocks aim through the faded wall.");
        childCollider.SetActive(false); Destroy(childCollider);
        wall.GetComponent<Collider>().enabled = true;

        cutaway.enabled = false;
        Physics.SyncTransforms();
        Check(character.TryGetAimHit(out picked) && picked.collider != floor.GetComponent<Collider>(), "Disabling cutaway still ignores the opaque building.");
        cutaway.enabled = true;
        Check(character.TryGetAimHit(out picked) && picked.collider == floor.GetComponent<Collider>(), "Re-enabling cutaway did not restore aim filtering.");

        player.position = Vector3.right * 8;
        controller.Snap(); Physics.SyncTransforms();
        ray = camera.ScreenPointToRay(camera.WorldToScreenPoint(wall.transform.position));
        character.Aim(camera.WorldToScreenPoint(wall.transform.position), 0);
        Check(Physics.Raycast(ray, out physical, camera.farClipPlane) && character.TryGetAimHit(out picked)
            && picked.collider == physical.collider, "After leaving the obstruction, opaque architecture is still ignored.");
        player.position = Vector3.zero;
        controller.Snap(); Physics.SyncTransforms();
        character.Aim(camera.WorldToScreenPoint(groundPoint), 0);
        Check(character.TryGetAimHit(out picked) && picked.collider == floor.GetComponent<Collider>(), "Camera/player movement uses stale cutaway picking.");
        controller.Follow(null);
        Check(character.TryGetAimHit(out picked) && picked.collider != floor.GetComponent<Collider>(), "Clearing the follow target leaves stale aim exclusions.");
        controller.Follow(player);
        foreach (var layer in layers) { layer.SetActive(false); Destroy(layer); }
        aimTarget.SetActive(false); Destroy(aimTarget);
        controller.Follow(player);
        Physics.SyncTransforms();
        Debug.Log("CAMERA_AIM_PICKING_PASS: first-frame/current-pose picking, 40 faded shells, ground/target selection, child colliders, triggers, empty ray, opaque restoration, physical collision preserved.");
    }

    private static GameObject Primitive(string name, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var result = GameObject.CreatePrimitive(type);
        result.name = name;
        result.transform.position = position;
        result.transform.localScale = scale;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color);
        material.EnableKeyword("_EMISSION");
        result.GetComponent<Renderer>().sharedMaterial = material;
        return result;
    }

    private Color32[] Capture(string path)
    {
        var target = new RenderTexture(Width, Height, 24);
        var previousTarget = camera.targetTexture;
        var previousActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.aspect = (float)Width / Height;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        pixels.Apply();
        File.WriteAllBytes(path, pixels.EncodeToPNG());
        Color32[] result = pixels.GetPixels32();
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        target.Release();
        DestroyImmediate(pixels);
        DestroyImmediate(target);
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("CAMERA_VERIFY_FAILED: " + message);
    }
}
#endif


