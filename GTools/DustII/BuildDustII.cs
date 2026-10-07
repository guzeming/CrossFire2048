using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OperationBlacktide.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

public static class BuildDustII
{
    public const string Art = "Assets/Art/Maps/DustII";
    public const string ScenePath = "Assets/Scenes/DustII.unity";
    [Serializable] public class Manifest
    {
        public int meshCount, triangleCount, hiddenToolMeshes;
        public Chunk[] chunks;
        public MaterialInfo[] materials;
        public TextureInfo[] textures;
        public string[] nonCollidableMeshes;
        public BoundsInfo boundsBlender;
    }
    [Serializable] public class BoundsInfo { public float[] min, max; }
    [Serializable] public class Chunk { public string file; public int meshes, triangles; }
    [Serializable] public class TextureInfo { public string name, role; public int width, height; }
    [Serializable] public class MaterialInfo
    {
        public string name, color, normal, metalSmoothness, emission, alphaMode, sourceMaterial;
        public float[] factor;
        public float metallic, roughness, normalScale, cutoff;
        public bool doubleSided;
    }
    public static Manifest ReadManifest() => JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "/import_manifest.json"));

    public static void Run()
    {
        try { Build(); EditorApplication.Exit(0); }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    // Execute in the isolated staging project, then copy the verified assets with their .meta files.
    public static void Build()
    {
        var manifest = ReadManifest();
        var materials = new Dictionary<string, Material>();
        foreach (var info in manifest.materials)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/" + info.name + ".mat");
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, Art + "/Materials/" + info.name + ".mat");
            }
            material.SetColor("_BaseColor", new Color(info.factor[0], info.factor[1], info.factor[2], info.factor[3]));
            material.SetTexture("_BaseMap", Texture(info.color));
            material.SetFloat("_Metallic", info.metallic);
            material.SetFloat("_Smoothness", 1f - info.roughness);
            if (!string.IsNullOrEmpty(info.normal))
            {
                material.SetTexture("_BumpMap", Texture(info.normal));
                material.SetFloat("_BumpScale", info.normalScale);
                material.EnableKeyword("_NORMALMAP");
            }
            if (!string.IsNullOrEmpty(info.metalSmoothness))
            {
                var packed = Texture(info.metalSmoothness);
                material.SetTexture("_MetallicGlossMap", packed);
                material.SetFloat("_Smoothness", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.SetTexture("_OcclusionMap", packed);
                material.SetFloat("_OcclusionStrength", 1);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            if (info.alphaMode == "MASK")
            {
                material.SetFloat("_AlphaClip", 1); material.SetFloat("_Cutoff", info.cutoff);
                material.EnableKeyword("_ALPHATEST_ON"); material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else if (info.alphaMode == "BLEND")
            {
                material.SetFloat("_Surface", 1); material.SetFloat("_ZWrite", 0);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                material.SetShaderPassEnabled("ShadowCaster", false);
            }
            if (info.doubleSided) material.SetFloat("_Cull", (float)CullMode.Off);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            materials.Add(info.name, material);
        }
        AssetDatabase.SaveAssets();
        foreach (var chunk in manifest.chunks)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(Art + "/Models/" + chunk.file);
            // FBX material names have unique stable source indices, even when VMAT display names repeat.
            foreach (var pair in materials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            importer.SaveAndReimport();
        }
        Debug.Log("DUSTII_MATERIALS_READY");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("DustII");
        foreach (var chunk in manifest.chunks)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/Models/" + chunk.file);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.transform.SetParent(root.transform, false);
        }
        var renderers = root.GetComponentsInChildren<MeshRenderer>();
        var filters = root.GetComponentsInChildren<MeshFilter>();
        long triangles = filters.Sum(f => (long)Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (long)f.sharedMesh.GetIndexCount(s)) / 3);
        if (renderers.Length != manifest.meshCount || triangles != manifest.triangleCount)
            throw new InvalidOperationException($"Geometry count mismatch: meshes={renderers.Length}, triangles={triangles}");
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        Vector3 expectedSize = new Vector3(manifest.boundsBlender.max[0] - manifest.boundsBlender.min[0],
            manifest.boundsBlender.max[2] - manifest.boundsBlender.min[2], manifest.boundsBlender.max[1] - manifest.boundsBlender.min[1]);
        if (Vector3.Distance(expectedSize, bounds.size) > .05f)
            throw new InvalidOperationException($"Map scale mismatch: expected {expectedSize}, got {bounds.size}");
        var lookup = manifest.materials.ToDictionary(m => m.name);
        var nonCollidable = new HashSet<string>(manifest.nonCollidableMeshes ?? Array.Empty<string>());
        var hiddenEffects = DustIIEffectCleanup.Apply(root, manifest);
        int colliderCount = 0;
        foreach (var renderer in renderers)
        {
            if (!renderer.gameObject.activeSelf) continue;
            if (renderer.sharedMaterials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit"))
                throw new InvalidOperationException("Missing URP material on " + renderer.name);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            // Opaque geometry supplies basic collision. Alpha overlays and foliage are visual only.
            bool solid = renderer.sharedMaterials.Any(m => lookup.TryGetValue(m.name, out var info) &&
                info.alphaMode == "OPAQUE" && !info.sourceMaterial.Contains("foliage") && !info.sourceMaterial.Contains("overlay") && !info.sourceMaterial.Contains("decal"));
            // Two source fragments have only a zero-area triangle: keep their render data,
            // but there is no surface from which PhysX can construct a collision shape.
            if (solid && !nonCollidable.Contains(renderer.name))
            {
                var collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                collider.convex = false;
                colliderCount++;
            }
        }
        Physics.SyncTransforms();
        int rayHits = 0;
        for (int x = 1; x < 16; x++) for (int z = 1; z < 16; z++)
        {
            var origin = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x/16f), bounds.max.y + 10, Mathf.Lerp(bounds.min.z, bounds.max.z, z/16f));
            if (Physics.Raycast(origin, Vector3.down, out _, bounds.size.y + 20)) rayHits++;
        }
        if (rayHits < 20) throw new InvalidOperationException("Map collision check failed: " + rayHits);
        PrefabUtility.SaveAsPrefabAssetAndConnect(root, Art + "/Prefabs/DustII.prefab", InteractionMode.AutomatedAction);
        Debug.Log("DUSTII_PREFAB_READY");

        var light = new GameObject("Daylight").AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, .94f, .83f); light.intensity = 1.3f;
        light.transform.rotation = Quaternion.Euler(50, -35, 0);
        light.shadows = LightShadows.Soft;
        RenderSettings.sun = light;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.47f, .55f, .65f);
        RenderSettings.ambientEquatorColor = new Color(.32f, .33f, .34f);
        RenderSettings.ambientGroundColor = new Color(.20f, .18f, .15f);
        RenderSettings.fog = false;
        var camera = new GameObject("Map Overview Camera").AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = bounds.center + new Vector3(-.6f, 1.7f, .85f).normalized * 255;
        camera.transform.LookAt(bounds.center);
        camera.fieldOfView = 58; camera.nearClipPlane = .1f; camera.farClipPlane = 600;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.45f, .61f, .73f);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        camera.gameObject.AddComponent<AudioListener>();
        camera.gameObject.AddComponent<DustIIViewer>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        int missingTextures = 0;
        foreach (var info in manifest.textures)
        {
            var tex = Texture(info.name);
            if (tex == null || tex.width != info.width || tex.height != info.height) missingTextures++;
        }
        if (missingTextures != 0) throw new InvalidOperationException("Missing or downscaled textures: " + missingTextures);
        var report = new Verification { passed = true, meshes = renderers.Length, triangles = triangles,
            materials = materials.Count, textures = manifest.textures.Length, colliders = colliderCount,
            hiddenEffectMeshes = hiddenEffects.Length, visibleMeshes = renderers.Length - hiddenEffects.Length,
            collisionRayHits = rayHits, boundsSize = bounds.size, boundsCenter = bounds.center,
            missingOrDownscaledTextures = missingTextures };
        Directory.CreateDirectory("Validation");
        File.WriteAllText("Validation/import_report.json", JsonUtility.ToJson(report, true));
        EditorSceneManager.OpenScene(ScenePath);
        var savedRoot = GameObject.Find("DustII");
        if (savedRoot == null || savedRoot.GetComponentsInChildren<MeshRenderer>(true).Length != manifest.meshCount ||
            savedRoot.GetComponentsInChildren<MeshRenderer>().Length != manifest.meshCount - hiddenEffects.Length)
            throw new InvalidOperationException("Saved scene/prefab did not reload correctly.");
        camera = Camera.main;
        if (camera == null || camera.GetComponent<DustIIViewer>() == null)
            throw new InvalidOperationException("Saved scene camera is missing.");
        Capture(camera, "Validation/DustII_Unity_Overview.png");
        Debug.Log("DUSTII_IMPORT_PASS " + JsonUtility.ToJson(report));
    }
    [Serializable] private class Verification
    {
        public bool passed;
        public int meshes, materials, textures, colliders, collisionRayHits, missingOrDownscaledTextures;
        public int hiddenEffectMeshes, visibleMeshes;
        public long triangles;
        public Vector3 boundsSize, boundsCenter;
    }
    private static Texture2D Texture(string name) => string.IsNullOrEmpty(name) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "/Textures/" + name);
    private static void Capture(Camera camera, string path)
    {
        var rt = new RenderTexture(1600, 1000, 24);
        camera.targetTexture = rt;
        camera.Render();
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous;
        Object.DestroyImmediate(image); Object.DestroyImmediate(rt);
    }
}

public sealed class DustIIImportSettings : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(BuildDustII.Art + "/Models/", StringComparison.Ordinal)) return;
        var importer = (ModelImporter)assetImporter;
        importer.globalScale = 1; importer.useFileScale = true;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.weldVertices = false;
        importer.optimizeMeshPolygons = false;
        importer.optimizeMeshVertices = false;
        importer.importNormals = ModelImporterNormals.Import;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.importAnimation = false; importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false; importer.importLights = false;
        importer.generateSecondaryUV = false; importer.addCollider = false;
        importer.isReadable = false; importer.preserveHierarchy = true;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
    }
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(BuildDustII.Art + "/Textures/", StringComparison.Ordinal)) return;
        var info = BuildDustII.ReadManifest().textures.First(t => t.name == Path.GetFileName(assetPath));
        var importer = (TextureImporter)assetImporter;
        importer.textureType = info.role == "normal" ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = info.role == "color";
        importer.maxTextureSize = 16384; importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.crunchedCompression = false; importer.compressionQuality = 100;
        importer.mipmapEnabled = true; importer.streamingMipmaps = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 8;
        importer.isReadable = false;
    }
}
