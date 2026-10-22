#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.Cameras;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEditor;
using UnityEngine;

public sealed class ImpactAudioVerificationDriver : MonoBehaviour
{
    private void Awake() { Application.logMessageReceived += OnLog; }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText("impact-audio-failure.txt", message + "\n" + trace);
        EditorApplication.Exit(1);
    }

    private IEnumerator Start()
    {
        yield return null; yield return null;
        var scene = FindObjectOfType<TrainingSceneController>();
        var player = scene.Player;
        var gun = player.GetComponent<WeaponController>();
        var audio = FindObjectOfType<ImpactAudio>();
        Check(audio != null && gun != null, "Scene did not initialize impact audio");
        var targets = FindObjectsOfType<TrainingTarget>();
        Check(targets.Length > 0 && targets.All(t => t.GetComponentsInChildren<HitRegion>().Any(r => r.Part == HitBodyPart.Head)),
            "Real training targets have no head region");
        Check(targets.All(t => t.GetComponentsInChildren<HitRegion>().Any(r => r.Part == HitBodyPart.Body)), "No body region");
        scene.enabled = false; player.enabled = false;
        player.GetComponent<CharacterController>().enabled = false;
        player.transform.position = new Vector3(0, 100, 0);
        player.transform.rotation = Quaternion.identity;
        player.InputEnabled = true;
        var camera = player.ViewCamera;
        camera.GetComponent<TopDownCameraController>().enabled = false;
        camera.transform.position = player.transform.position + new Vector3(0, 8, -6);
        camera.transform.LookAt(player.transform.position + Vector3.forward * 6);

        // Exercise the real firing/damage/effects path, including a lethal head hit.
        var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetObject.name = "Audio damage fixture";
        targetObject.transform.position = gun.MuzzlePosition + Vector3.forward * 4;
        targetObject.transform.localScale = new Vector3(2, 2, .2f);
        var region = targetObject.AddComponent<HitRegion>();
        var target = targetObject.AddComponent<TrainingTarget>();
        Physics.SyncTransforms();
        player.Aim(camera.WorldToScreenPoint(targetObject.transform.position), 0);
        Fire(gun, 100);
        Check(gun.LastShot.hit.collider == targetObject.GetComponent<Collider>() && audio.LastKind == ImpactKind.Body,
            "Body shot did not reach the body bank");
        Check(target.Health < 100 && audio.LastClip.name.StartsWith("mud_impact"), "Body damage/audio missing");
        region.Part = HitBodyPart.Head;
        typeof(TrainingTarget).GetProperty("Health").SetValue(target, 1f);
        Fire(gun, 101);
        Check(!target.IsAlive && !targetObject.GetComponent<Collider>().enabled && audio.LastKind == ImpactKind.Head,
            "Lethal head hit lost the region when target was disabled");
        Check(audio.LastClip.name.StartsWith("headshot_noarmor"), "Wrong head clip");
        Check(FindObjectsOfType<BulletHoles>().SelectMany(p => p.GetComponentsInChildren<MeshRenderer>())
            .Where(r => r.name == "Bullet Hole").All(r => !r.enabled), "Character received concrete bullet decal");
        targetObject.SetActive(false);

        // Armor is captured before damage depletes it; helmets and bare heads remain distinct.
        var armorObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        armorObject.transform.position = new Vector3(10, 100, 0);
        var armorRegion = armorObject.AddComponent<HitRegion>();
        var vitals = armorObject.AddComponent<CharacterVitals>();
        vitals.SetArmor(1);
        var armored = Cast(armorObject);
        Check(armored.impactKind == ImpactKind.Armor, "Armor was ignored");
        vitals.ApplyDamage(10);
        audio.BeginShot(); Check(audio.Play(armored) && audio.LastKind == ImpactKind.Armor, "Final armor hit lost its sound");
        Check(Cast(armorObject).impactKind == ImpactKind.Body, "Depleted armor still sounds armored");
        vitals.SetArmor(10); armorRegion.Part = HitBodyPart.Head;
        Check(Cast(armorObject).impactKind == ImpactKind.Head, "Body armor falsely supplied a helmet");
        typeof(CharacterVitals).GetField("wearsHelmet", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(vitals, true);
        var helmet = Cast(armorObject);
        audio.BeginShot(); audio.Play(helmet);
        Check(helmet.impactKind == ImpactKind.Helmet && audio.LastClip.name == "headshot_armor_e1", "Wrong helmet sound");
        armorRegion.Part = HitBodyPart.Limb;
        Check(Cast(armorObject).impactKind == ImpactKind.Body, "Limb inherited vest armor sound");
        armorObject.SetActive(false);

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(20, 100, 0);
        var surface = wall.AddComponent<ImpactSurface>();
        foreach (ImpactKind kind in Enum.GetValues(typeof(ImpactKind)))
        {
            surface.Kind = kind;
            var shot = Cast(wall);
            AudioClip previous = null;
            for (int i = 0; i < 8; i++)
            {
                audio.BeginShot(); Check(audio.Play(shot), "Surface sound failed " + kind);
                Check(audio.LastKind == kind, "Wrong material bank " + kind);
                if (kind != ImpactKind.Helmet) Check(audio.LastClip != previous, "Consecutive duplicate " + kind);
                previous = audio.LastClip;
            }
        }
        surface.Kind = ImpactKind.Metal;
        var metal = Cast(wall);
        audio.BeginShot(); int count = audio.SoundsPlayed;
        for (int i = 0; i < 16; i++) audio.Play(metal);
        Check(audio.SoundsPlayed == count + 1, "Pellet impacts piled up on one surface");
        Check(!audio.Play(new Shot(Vector3.zero, Vector3.one, default, false, 0)), "Miss played impact audio");
        var voices = audio.GetComponentsInChildren<AudioSource>();
        Check(voices.Length == ImpactAudio.VoiceCapacity && voices.Any(v => v.isPlaying && v.spatialBlend == 1),
            "Environment voices are missing, unbounded or nonspatial");
        gun.ResetEquipment();
        Check(voices.All(v => !v.isPlaying), "Respawn/reset left stale voices");
        audio.BeginShot(); audio.Play(metal);
        gun.enabled = false;
        Check(voices.All(v => !v.isPlaying), "Disabling gun left impact voices");
        gun.enabled = true;
        CheckSubmeshes();
        Debug.Log("IMPACT_ROUTING_PASS: live gun/body/lethal head, armor depletion, helmet, limb, 21 banks, no repeats, pellets, misses, submeshes.");

        audio.BeginShot(); audio.Play(metal);
        player.InputEnabled = false;
        yield return null;
        Check(voices.All(v => !v.isPlaying), "Blocked input left impact audio playing");
        player.InputEnabled = true;
        audio.BeginShot(); audio.Play(metal);
        Time.timeScale = 0;
        yield return null;
        Check(voices.All(v => !v.isPlaying), "Pause left impact audio playing");
        Time.timeScale = 1;
        Destroy(player.gameObject);
        yield return null; yield return null;
        Check(audio == null && voices.All(v => v == null), "World sound pool survived its owner");
        Debug.Log("IMPACT_AUDIO_VERIFY_PASS: assets, physical hit routing, actual audio voices, menu/pause/reset/disable/owner cleanup.");
        EditorApplication.Exit(0);
    }

    private static void CheckSubmeshes()
    {
        var fixture = new GameObject("Two material mesh");
        fixture.transform.position = new Vector3(30, 100, 0);
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-2,-1,0), new Vector3(-2,1,0), new Vector3(0,-1,0), new Vector3(0,1,0),
            new Vector3(0,-1,0), new Vector3(0,1,0), new Vector3(2,-1,0), new Vector3(2,1,0) };
        mesh.subMeshCount = 2;
        mesh.SetTriangles(new[] { 0,1,2,2,1,3 }, 0);
        mesh.SetTriangles(new[] { 4,5,6,6,5,7 }, 1);
        fixture.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = fixture.AddComponent<MeshRenderer>();
        var wood = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "D2_0168_dust_door_46x106_01" };
        var metal = new Material(wood) { name = "D2_0047_hr_dust_metal_trim_01 (Occluded Building)" };
        renderer.sharedMaterials = new[] { wood, metal };
        fixture.AddComponent<MeshCollider>().sharedMesh = mesh;
        Physics.SyncTransforms();
        foreach (int x in new[] { -1, 1 })
        {
            Vector3 origin = fixture.transform.position + new Vector3(x, 0, -2);
            Check(Physics.Raycast(origin, Vector3.forward, out var hit, 3), "Submesh fixture missed");
            Check(ImpactSurfaceResolver.Resolve(hit) == (x < 0 ? ImpactKind.WoodPanel : ImpactKind.Metal),
                "Collider used first material for every triangle");
        }
        Destroy(fixture); Destroy(mesh); Destroy(wood); Destroy(metal);
    }

    private static Shot Cast(GameObject fixture)
    {
        Physics.SyncTransforms();
        Vector3 origin = fixture.transform.position - Vector3.forward * 2;
        Check(fixture.GetComponent<Collider>().Raycast(new Ray(origin, Vector3.forward), out var hit, 3), "Fixture cast missed");
        return new Shot(origin, hit.point, hit, true, 10);
    }
    private static void Fire(WeaponController gun, double time)
    {
        int before = gun.ShotsFired;
        gun.ProcessTrigger(false, false, true, time); gun.ProcessTrigger(true, true, true, time);
        Check(gun.ShotsFired == before + 1, "Live gun failed to fire");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
