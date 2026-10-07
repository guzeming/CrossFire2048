#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public sealed class ThrowableVerificationDriver : MonoBehaviour
{
    private readonly Vector3 origin = new Vector3(1000, 100, 1000);
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText("throwables-failure.txt", message + "\n" + trace); EditorApplication.Exit(1);
    }
    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        ThrowableActionVerification.Run(scene);
        var player = scene.Player; var gun = player.GetComponent<TrainingWeaponController>();
        var control = gun.Throwables; var world = control.World;
        var assets = Resources.Load<TrainingThrowableCatalog>("Training/TrainingThrowables");
        var he = assets.Find("weapon_hegrenade"); var fire = assets.Find("weapon_molotov");
        var smoke = assets.Find("weapon_smokegrenade"); var flash = assets.Find("weapon_flashbang");
        Check(world != null && !control.CanThrow, "Rifle binding / world creation");
        scene.enabled = false; player.enabled = false; gun.enabled = false; world.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        Vector3 saved = player.transform.position; Quaternion savedRotation = player.transform.rotation;
        player.transform.position = origin + Vector3.back * 8; player.transform.rotation = Quaternion.identity;
        var fixtures = new GameObject("Throwable Fixtures");
        Box(fixtures.transform, "Floor", origin + Vector3.down * .5f, new Vector3(50, 1, 50));
        var wall = Box(fixtures.transform, "Cover", origin + new Vector3(2, 2, 0), new Vector3(.2f, 4, 12));
        var exposed = Target(fixtures.transform, origin + new Vector3(-1.5f, 0, 0));
        var covered = Target(fixtures.transform, origin + new Vector3(3.5f, 0, 0));
        Physics.SyncTransforms();
        world.Detonate(he, origin + Vector3.up);
        Check(exposed.Health > 0 && exposed.Health < 100, "HE damage/falloff or duplicate collider damage");
        Check(covered.Health == 100, "HE penetrated solid cover");
        var victim = new GameObject("Player damage fixture"); victim.transform.SetParent(fixtures.transform);
        victim.transform.position = origin + Vector3.back * 12;
        var victimCollider = victim.AddComponent<CapsuleCollider>(); victimCollider.height = 1.8f; victimCollider.center = Vector3.up * .9f;
        var victimVitals = victim.AddComponent<TrainingVitals>(); victim.layer = 2;
        Physics.SyncTransforms(); world.Detonate(he, victim.transform.position + Vector3.up);
        Check(victimVitals.Health < 100, "HE ignored player on Ignore Raycast layer");
        victim.SetActive(false);
        float nearHealth = exposed.Health;
        var farther = Target(fixtures.transform, origin + new Vector3(-4.5f, 0, 0));
        Physics.SyncTransforms(); world.Detonate(he, origin + Vector3.up);
        Check(100 - farther.Health < 100 - nearHealth, "HE distance falloff");
        exposed.gameObject.SetActive(false); covered.gameObject.SetActive(false); farther.gameObject.SetActive(false);

        Vector3 p = origin + new Vector3(0, 1, 0), v = Vector3.right * 40;
        for (int i = 0; i < 4; i++) TrainingThrowablePhysics.Advance(ref p, ref v, .02f, out _);
        Check(p.x < origin.x + 1.9f && v.x < 0, "Swept projectile tunnelled through wall");
        int detonations = world.Detonations;
        Check(world.Launch(he, origin + new Vector3(-4, 1, 0), new Vector3(2, 3, 0)), "Launch rejected");
        Advance(world, 1.5f); Check(world.Detonations == detonations && world.ProjectileCount == 1, "Fuse detonated early");
        Advance(world, .3f); Check(world.Detonations == detonations + 1 && world.ProjectileCount == 0, "Fuse failed / projectile leaked");
        Check(world.BounceCount > 0, "Bounce callback/audio missing");

        var burning = Target(fixtures.transform, origin + Vector3.left);
        var shielded = Target(fixtures.transform, origin + Vector3.right * 3);
        Physics.SyncTransforms(); world.Detonate(fire, origin + Vector3.up * .15f);
        Advance(world, .55f);
        Check(burning.Health < 100 && burning.Health >= 68, "Fire DPS / duplicate collider damage");
        Check(shielded.Health == 100, "Fire crossed a wall");
        Check(world.FieldCount == 1, "Fire failed to create a field");
        world.Detonate(smoke, origin + Vector3.up * .15f); Advance(world, .7f);
        Check(world.FieldCount == 1, "Smoke did not extinguish fire");
        Check(world.IsSmokeOccluded(origin + new Vector3(-7, 1, 0), origin + new Vector3(7, 1, 0)), "Smoke LOS not blocked");
        Check(!world.IsSmokeOccluded(origin + new Vector3(-7, 8, 0), origin + new Vector3(7, 8, 0)), "Smoke blocks unrelated elevation");
        player.transform.position = origin; Check(world.SmokeOpacity > .9f, "Inside-smoke veil missing");
        player.transform.position = origin + Vector3.back * 8;
        Advance(world, 19); Check(world.FieldCount == 0, "Expired smoke/fire field leaked");
        burning.gameObject.SetActive(false); shielded.gameObject.SetActive(false);

        player.transform.position = origin; Physics.SyncTransforms();
        float front = world.FlashExposure(origin + new Vector3(0, 1.5f, 4), 18);
        float back = world.FlashExposure(origin + new Vector3(0, 1.5f, -4), 18);
        float hidden = world.FlashExposure(origin + new Vector3(4, 1.5f, 0), 18);
        Check(front > back * 2 && hidden == 0, "Flash facing / wall occlusion");
        world.ApplyFlash(origin + new Vector3(0, 1.5f, 4), 18, 4);
        Check(world.FlashOpacity > .9f, "Flash whiteout missing");
        var panel = FindObjectOfType<TrainingPanel>(); panel.SendMessage("LateUpdate");
        var flashImage = panel.transform.Find("Flash Blindness").GetComponent<Image>();
        var smokeImage = panel.transform.Find("Smoke Obscuration").GetComponent<Image>();
        Check(flashImage.color.a > .9f && !flashImage.raycastTarget && !smokeImage.raycastTarget, "HUD flash not visible or veils intercept input");
        Check(world.GetComponentsInChildren<AudioSource>().Any(s => s.isPlaying && s.clip == assets.ringing), "Flash ringing did not play");
        Advance(world, 5); Check(world.FlashOpacity == 0, "Flash did not clear");
        world.Detonate(fire, origin + Vector3.up * 10);
        Check(world.FieldCount == 0, "Fire field created without a supporting floor");

        // Real equipment changes, mouse release gating, cancellation and infinite training replenishment.
        player.transform.position = origin + Vector3.back * 8;
        player.InputEnabled = true;
        for (int i = 0; i < 8 && gun.WeaponId != "weapon_hegrenade"; i++) Check(scene.EquipSlot(4), "Cannot equip slot 4");
        Check(control.CanThrow && gun.Ammo == null && gun.CanAttack, "Throwable not integrated with weapon/HUD");
        control.ProcessInput(false, false, false, true, he.drawAnimation.length + .01f);
        gun.enabled = false; // Manual input only, without frame polling.
        int initialThrows = control.Throws;
        control.ProcessInput(true, true, false, true, .4f); Check(!control.IsPreparing, "Equip while held created a throw");
        control.ProcessInput(false, false, false, true, .01f);
        control.ProcessInput(true, true, false, true, .3f); Check(control.IsPreparing, "Pin pull did not start");
        control.ProcessInput(false, false, true, true, .01f); Check(control.Throws == initialThrows, "Right cancel threw grenade");
        control.ProcessInput(false, false, false, true, .01f);
        control.ProcessInput(true, true, false, true, .3f);
        control.ProcessInput(false, false, false, false, .01f); Check(control.Throws == initialThrows && !control.IsPreparing, "Blocked input released grenade");
        control.ProcessInput(false, false, false, true, .01f);
        control.ProcessInput(true, true, false, true, .3f);
        scene.OpenTrainingMenu();
        control.ProcessInput(false, false, false, true, .01f);
        Check(!player.InputEnabled && !control.IsPreparing && control.Throws == initialThrows, "Training menu released a prepared grenade");
        UIManager.Instance.CloseAll(UILayer.Popup); player.InputEnabled = true;
        control.ProcessInput(false, false, false, true, .01f);
        control.ProcessInput(true, true, false, true, .3f);
        control.ProcessInput(false, false, false, true, he.prepareAnimation.length + he.ReleaseTime);
        Check(control.Throws == initialThrows + 1, "Release did not throw");
        int live = world.ProjectileCount;
        Check(scene.EquipSlot(1), "Return to rifle failed"); Check(world.ProjectileCount == live && gun.Ammo != null, "Switch destroyed projectile or broke ammo");
        world.ResetSenses(); Advance(world, 25);
        Check(world.ProjectileCount == 0 && world.FieldCount == 0, "Lingering live objects");
        Check(world.GetComponentsInChildren<ParticleSystemRenderer>().All(r => r.sharedMaterial != null && r.sharedMaterial.shader.isSupported), "Particle shader unsupported");
        for (int i = 0; i < 16; i++) Check(world.Launch(he, origin + Vector3.up * 10, Vector3.zero), "Capacity too small");
        Check(!world.Launch(he, origin + Vector3.up * 10, Vector3.zero), "Unbounded active projectiles");
        Advance(world, 3);
        Debug.Log("THROWABLE_GAMEPLAY_PASS: HE cover/falloff/dedup, sweep/bounce/fuse, fire floor/cover/DPS, smoke extinguish/LOS/lifetime, flash facing/cover/expiry, input and weapon switching.");

        // Visual verification in the actual Dust II scene using the real asset compositions.
        fixtures.SetActive(false); Object.Destroy(fixtures);
        player.transform.SetPositionAndRotation(saved, savedRotation); Physics.SyncTransforms();
        var camera = player.ViewCamera;
        camera.GetComponent<TrainingCameraController>().enabled = false;
        var cutaway = camera.GetComponent<TrainingCameraCutaway>(); if (cutaway != null) cutaway.enabled = false;
        Vector3 stage = saved + new Vector3(0, .15f, 6);
        camera.transform.position = stage + new Vector3(8, 13, -10); camera.transform.LookAt(stage);
        camera.orthographic = true; camera.orthographicSize = 9;
        world.Detonate(fire, stage);
        world.Simulate(.25f);
        foreach (var ps in world.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(.35f, true, false);
        Capture(camera, "throwables-fire.png");
        Advance(world, 20);
        world.Detonate(smoke, stage);
        foreach (var ps in world.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(2, true, false);
        world.Simulate(.8f);
        Capture(camera, "throwables-smoke.png");
        Advance(world, 20);
        world.Detonate(he, stage);
        foreach (var ps in world.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(.18f, true, false);
        Capture(camera, "throwables-he.png");
        world.ResetSenses();
        yield return null;
        Check(!FindObjectsOfType<TrainingThrowableEffects>().Any(e => e.name == "Ground Fire"), "Expired visual survived frame cleanup");
        Debug.Log("THROWABLES_VERIFY_PASS"); EditorApplication.Exit(0);
    }

    private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent);
        go.transform.position = position; go.transform.localScale = scale; return go;
    }
    private static TrainingTarget Target(Transform parent, Vector3 point)
    {
        var go = new GameObject("Test Target"); go.transform.SetParent(parent); go.transform.position = point;
        Box(go.transform, "Body", point + Vector3.up, new Vector3(.5f, 1.5f, .5f));
        Box(go.transform, "Head", point + Vector3.up * 1.8f, Vector3.one * .3f);
        return go.AddComponent<TrainingTarget>();
    }
    private static void Advance(TrainingThrowableWorld world, float duration)
    { for (int i = 0; i < Mathf.CeilToInt(duration / .02f); i++) world.Simulate(.02f); }
    private static void Capture(Camera camera, string file)
    {
        var rt = new RenderTexture(1600, 1000, 24); rt.Create(); camera.aspect = 1.6f;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
        var previous = RenderTexture.active; RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
        File.WriteAllBytes(file, image.EncodeToPNG()); RenderTexture.active = previous;
        Object.Destroy(image); rt.Release(); Object.Destroy(rt);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
