using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Source 2 smoke/light shafts need their original shader; opaque URP conversion produces solid sheets.</summary>
public static class DustIIEffectCleanup
{
    public static string[] Apply(GameObject root, BuildDustII.Manifest manifest)
    {
        var materials = new HashSet<string>(manifest.materials
            .Where(m => m.sourceMaterial != null && m.sourceMaterial.StartsWith("materials/effects/smoke/", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.name));
        var removed = new List<string>();
        foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            var slots = renderer.sharedMaterials;
            if (!slots.Any(m => m != null && materials.Contains(m.name))) continue;
            if (slots.Any(m => m == null || !materials.Contains(m.name)) || renderer.transform.childCount != 0)
                throw new InvalidOperationException("Effect shares an object with other map content: " + renderer.name);
            // Keep imported FBX geometry intact. A prefab override removes both rendering and collision,
            // including from camera occlusion/weapon raycasts, without a runtime cleanup component.
            if (!renderer.gameObject.activeSelf) continue;
            renderer.gameObject.SetActive(false);
            removed.Add(renderer.name);
        }
        return removed.ToArray();
    }

    // Run in the isolated map staging project; publish only the verified prefab, never the scene.
    public static void Run()
    {
        try { CleanAndVerify(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    private static void CleanAndVerify()
    {
        ShaderUtil.allowAsyncCompilation = false;
        Directory.CreateDirectory("Validation");
        var manifest = BuildDustII.ReadManifest();
        EditorSceneManager.OpenScene(BuildDustII.ScenePath);
        var beforeRoot = GameObject.Find("DustII");
        var beforeRenderers = beforeRoot.GetComponentsInChildren<MeshRenderer>(true);
        var beforeStates = beforeRenderers.ToDictionary(r => r.name, r => r.gameObject.activeSelf);
        var beforeColliders = beforeRoot.GetComponentsInChildren<Collider>().Select(c => c.name).ToHashSet();
        CaptureViews("before");
        const string prefabPath = BuildDustII.Art + "/Prefabs/DustII.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        string[] removed;
        try
        {
            removed = Apply(root, manifest);
            if (removed.Length == 0) throw new InvalidOperationException("No active unsupported effects found.");
            if (Apply(root, manifest).Length != 0) throw new InvalidOperationException("Cleanup is not idempotent.");
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(BuildDustII.ScenePath);
        var savedRoot = GameObject.Find("DustII");
        var savedRenderers = savedRoot.GetComponentsInChildren<MeshRenderer>(true);
        var removedSet = new HashSet<string>(removed);
        if (savedRenderers.Length != beforeRenderers.Length)
            throw new InvalidOperationException("Source geometry was unexpectedly changed.");
        foreach (var renderer in savedRenderers)
        {
            bool expected = !removedSet.Contains(renderer.name) && beforeStates[renderer.name];
            if (renderer.gameObject.activeSelf != expected)
                throw new InvalidOperationException("Unexpected object visibility: " + renderer.name);
        }
        var activeColliders = savedRoot.GetComponentsInChildren<Collider>().Select(c => c.name).ToHashSet();
        var expectedColliders = new HashSet<string>(beforeColliders.Except(removed));
        if (!activeColliders.SetEquals(expectedColliders))
            throw new InvalidOperationException("Non-effect collision changed or an effect still blocks movement.");
        Physics.SyncTransforms();
        // The screenshot's orange car and both walkways must retain their solid surfaces.
        foreach (var point in new[] { new Vector3(2.34f, 10, 17.04f), new Vector3(-3, 10, 13), new Vector3(5, 10, 13) })
            if (!Physics.Raycast(point, Vector3.down, out var hit, 20) || removedSet.Contains(hit.collider.name))
                throw new InvalidOperationException("Missing car/street collision at " + point);
        CaptureViews("after");
        var report = new Report { passed = true, disabledEffects = removed.Length,
            visibleMeshes = savedRoot.GetComponentsInChildren<MeshRenderer>().Length,
            removedColliders = beforeColliders.Count - activeColliders.Count,
            preservedColliders = activeColliders.Count, objects = removed };
        File.WriteAllText("Validation/effect_cleanup_report.json", JsonUtility.ToJson(report, true));
        Debug.Log("DUSTII_EFFECT_CLEANUP_PASS " + JsonUtility.ToJson(report));
    }

    [Serializable] private class Report
    {
        public bool passed;
        public int disabledEffects, visibleMeshes, removedColliders, preservedColliders;
        public string[] objects;
    }

    private static void CaptureViews(string suffix)
    {
        var camera = Camera.main;
        camera.fieldOfView = 48;
        var rotation = Quaternion.Euler(60, 0, 0);
        var focuses = new[] { new Vector3(-.5f, 1, 13), new Vector3(10, 4.2f, -31), new Vector3(-32, 1, -47) };
        for (int i = 0; i < focuses.Length; i++)
        {
            camera.transform.SetPositionAndRotation(focuses[i] + rotation * Vector3.back * 16, rotation);
            var rt = new RenderTexture(1600, 900, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            try
            {
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                File.WriteAllBytes($"Validation/effects_{i}_{suffix}.png", image.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(image); Object.DestroyImmediate(rt); }
        }
    }
}
