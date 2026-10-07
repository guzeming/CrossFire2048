#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class BulletEffectsVerification
{
    public static void Run()
    {
        var catalog = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
        Check(catalog.bulletHoleMaterials.Length == 2, "Missing bullet hole variants.");
        var previousPools = Object.FindObjectsOfType<TrainingBulletHoles>();
        var fixture = new GameObject("Bullet Effects Verification");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Bullet Hole Test Wall";
        Vector3 origin = new Vector3(1000, 30, 1000);
        fixture.transform.position = origin;
        wall.transform.position = origin + Vector3.forward * 6;
        wall.transform.localScale = new Vector3(15, 15, .2f);
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        material.SetColor("_BaseColor", new Color(.6f, .54f, .45f));
        wall.GetComponent<Renderer>().sharedMaterial = material;
        var effects = fixture.AddComponent<TrainingWeaponEffects>();
        var definition = new TrainingWeaponDefinition { shots = Array.Empty<AudioClip>(), silenced = false };
        effects.Initialize(catalog, definition, fixture.transform);
        var holes = Object.FindObjectsOfType<TrainingBulletHoles>().Single(pool => !previousPools.Contains(pool));
        var renderers = holes.GetComponentsInChildren<MeshRenderer>().Where(r => r.name == "Bullet Hole").ToArray();
        var camera = Camera.main;
        var savedPosition = camera.transform.position; var savedRotation = camera.transform.rotation;
        float savedFov = camera.fieldOfView;
        try
        {
            Physics.SyncTransforms();
            Check(Physics.Raycast(origin, Vector3.forward, out var hit, 10), "Bullet hole fixture ray missed.");
            effects.Play(new TrainingShot(origin, hit.point, hit, true, 36));
            var mark = renderers.Single(r => r.enabled);
            Check(Vector3.Distance(mark.transform.position, hit.point) < .004f && Vector3.Dot(mark.transform.forward, hit.normal) > .999f,
                "Bullet hole not aligned to actual hit.");
            effects.Play(new TrainingShot(origin, hit.point, hit, true, 36));
            Check(renderers.Count(r => r.enabled) == 1, "Repeated same-point hits stack coplanar marks.");
            effects.Play(new TrainingShot(origin, origin + Vector3.left * 20, default, false, 0));
            Check(renderers.Count(r => r.enabled) == 1, "A missed shot made a bullet hole.");
            Vector3 localPoint = wall.transform.InverseTransformPoint(hit.point);
            wall.transform.position += Vector3.right;
            wall.transform.rotation = Quaternion.Euler(0, 25, 0);
            Physics.SyncTransforms();
            holes.UpdateMarks(Time.time);
            Check(Vector3.Distance(mark.transform.position, wall.transform.TransformPoint(localPoint)) < .004f
                && Vector3.Dot(mark.transform.forward, -wall.transform.forward) > .999f, "Mark did not follow moving surface.");
            holes.UpdateMarks(Time.time + 43);
            var properties = new MaterialPropertyBlock(); mark.GetPropertyBlock(properties);
            Check(properties.GetFloat("_Opacity") > 0 && properties.GetFloat("_Opacity") < 1, "Bullet hole does not fade.");
            holes.UpdateMarks(Time.time + 46);
            Check(renderers.All(r => !r.enabled), "Expired marks are still enabled.");
            wall.transform.SetPositionAndRotation(origin + Vector3.forward * 6, Quaternion.identity);
            Physics.SyncTransforms();
            for (int y = 0; y < 10; y++)
            for (int x = 0; x < 14; x++)
            {
                Vector3 rayStart = origin + new Vector3((x - 6.5f) * .7f, (y - 4.5f) * .7f, 0);
                Check(Physics.Raycast(rayStart, Vector3.forward, out hit, 10), "Pool fixture ray missed.");
                holes.Place(hit);
            }
            Check(renderers.Length == TrainingBulletHoles.Capacity && renderers.Count(r => r.enabled) == TrainingBulletHoles.Capacity,
                "Bullet hole pool is unbounded or failed to reuse slots.");
            wall.SetActive(false); holes.UpdateMarks(Time.time);
            Check(renderers.All(r => !r.enabled), "Marks float after disabling their surface.");
            wall.SetActive(true); Physics.SyncTransforms();
            Check(Physics.Raycast(origin + Vector3.right * 7.499f, Vector3.forward, out hit, 10), "Edge fixture ray missed.");
            holes.Place(hit);
            Check(renderers.All(r => !r.enabled), "Bullet hole floats past a wall edge.");
            // Render several separated real hits, including the active tracer/flash, at a useful inspection distance.
            foreach (var offset in new[] { new Vector3(-.6f,.35f,0), new Vector3(.55f,.3f,0), new Vector3(.1f,-.35f,0) })
            {
                Vector3 direction = (wall.transform.position + offset - origin).normalized;
                Check(Physics.Raycast(origin, direction, out hit, 10), "Visual fixture ray missed.");
                effects.Play(new TrainingShot(origin, hit.point, hit, true, 36));
            }
            foreach (var ps in holes.GetComponentsInChildren<ParticleSystem>()) ps.Simulate(.005f, false, false);
            camera.transform.position = origin + new Vector3(2.8f, 1.8f, -2);
            camera.transform.LookAt(wall.transform.position - Vector3.forward * 2.5f);
            camera.fieldOfView = 42;
            Capture(camera, "combat-effects-closeup.png");
            camera.transform.position = origin + new Vector3(0, 0, 3.6f);
            camera.transform.LookAt(wall.transform.position);
            Capture(camera, "bullet-holes-closeup.png");
            foreach (var shader in catalog.bulletHoleMaterials.Select(m => m.shader).Append(catalog.tracerMaterial.shader))
                Check(!ShaderUtil.ShaderHasError(shader), "Combat shader failed compilation: " + shader.name);
            Debug.Log("BULLET_EFFECTS_VERIFY_PASS: impact alignment, misses, repeat hits, moving surfaces, fade, expiry, pool bound and edges.");
        }
        finally
        {
            camera.transform.SetPositionAndRotation(savedPosition, savedRotation); camera.fieldOfView = savedFov;
            Object.Destroy(fixture); Object.Destroy(wall); Object.Destroy(material);
        }
    }

    private static void Capture(Camera camera, string path)
    {
        var target = new RenderTexture(1280, 720, 24);
        var previous = camera.targetTexture; var active = RenderTexture.active;
        camera.targetTexture = target;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture.active = target;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = previous; RenderTexture.active = active;
        Object.Destroy(image); target.Release(); Object.Destroy(target);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
