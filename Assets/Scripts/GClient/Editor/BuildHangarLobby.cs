using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    /// <summary>Composes the image-based hangar with a live character and a shadow receiving floor.</summary>
    public static class BuildHangarLobby
    {
        private const string Art = "Assets/Art/Lobby/Hangar";
        private const string ScenePath = "Assets/Scenes/LobbyScene.unity";
        [MenuItem("OperationBlacktide/Lobby/Rebuild Hangar Environment")]
        private static void RebuildFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stop Play Mode before rebuilding the hangar environment.");
                return;
            }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) Build();
        }

        // Full rebuild entry. Static props belong to the image; only the character is lit in 3D.
        public static void Build()
        {
            AssetDatabase.Refresh();
            ImportPresentationMaterials();
            ComposeScene();
            AssetDatabase.SaveAssets();
            Debug.Log("HANGAR_BUILD_PASS: baked environment, live character and shadow receiver.");
        }

        // Kept for existing editor callers; rebuilding cannot restore the retired crate instances.
        public static void RebuildPropLayout() => ApplyBakedBackground();

        public static void ApplyBakedBackground()
        {
            AssetDatabase.Refresh();
            ImportPresentationMaterials();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var environment = scene.GetRootGameObjects().First(o => o.name == "LobbyEnvironment").transform;
            var oldForeground = environment.Find("BlenderForeground");
            if (oldForeground != null) Object.DestroyImmediate(oldForeground.gameObject);
            var oldGround = environment.Find("CharacterShadowGround");
            if (oldGround != null) Object.DestroyImmediate(oldGround.gameObject);
            CreateShadowGround(environment);
            var previousLights = environment.Find("HangarLighting");
            if (previousLights != null) Object.DestroyImmediate(previousLights.gameObject);
            CreateLighting(environment);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("BAKED_HANGAR_PASS: no physical crates, columns, cables, lamps or ring; live CT and ground shadow retained.");
        }

        private static void ImportPresentationMaterials()
        {
            Directory.CreateDirectory(Art + "/Materials");
            var background = Texture("HangarBackground_Baked.png", true, false, true);
            var plate = Material("HangarPlate", "OperationBlacktide/Lobby/HangarPlate");
            plate.SetTexture("_BaseMap", background); plate.SetFloat("_Exposure", 1);
            var ground = Material("ProjectedGround", "OperationBlacktide/Lobby/HangarGround");
            ground.SetTexture("_BaseMap", background);
            ground.SetFloat("_Exposure", 1); ground.SetFloat("_ShadowStrength", .83f);
            Material("DustMotes", "OperationBlacktide/Lobby/HangarMotes");
        }

        private static void CreateShadowGround(Transform parent)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "CharacterShadowGround";
            ground.transform.SetParent(parent, false);
            ground.transform.localPosition = new Vector3(0, -.006f, 0);
            ground.transform.localScale = new Vector3(4, 1, 4);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            var renderer = ground.GetComponent<Renderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/ProjectedGround.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static Texture2D Texture(string name, bool srgb, bool normal = false, bool plate = false)
        {
            string path = Art + "/Textures/" + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException("Missing hangar texture: " + path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = srgb;
            importer.maxTextureSize = plate ? 4096 : 1024;
            // The source is 1672x941; rounding to powers of two distorts its perspective.
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = !plate;
            importer.wrapMode = plate ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            importer.anisoLevel = 1;
            importer.textureCompression = plate ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static Material Material(string name, string shader)
        {
            string path = Art + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var selectedShader = Shader.Find(shader);
            if (selectedShader == null) throw new InvalidOperationException("Missing hangar shader: " + shader);
            if (material == null)
            {
                material = new Material(selectedShader);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = selectedShader;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void ComposeScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            string[] replacedRoots = { "LobbyEnvironment", "Main Camera", "KeyLight", "CoolFill", "AmberRim", "LobbyPostProcessing", "LobbyReflection" };
            foreach (var obj in scene.GetRootGameObjects())
                if (replacedRoots.Contains(obj.name)) Object.DestroyImmediate(obj);
            var environment = new GameObject("LobbyEnvironment").transform;
            CreateShadowGround(environment);

            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 1.35f, -4.7f);
            camera.transform.LookAt(new Vector3(0, 1.08f, 0));
            camera.fieldOfView = 32; camera.nearClipPlane = .05f; camera.farClipPlane = 90;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            var data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            var plate = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plate.name = "DistantHangar_ImagePlate";
            plate.transform.SetParent(environment);
            plate.transform.position = camera.transform.position + camera.transform.forward * 40;
            plate.transform.rotation = camera.transform.rotation;
            plate.transform.localScale = new Vector3(90, 60, 1);
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            var plateRenderer = plate.GetComponent<Renderer>();
            plateRenderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/HangarPlate.mat");
            plateRenderer.shadowCastingMode = ShadowCastingMode.Off; plateRenderer.receiveShadows = false;

            CreateLighting(environment);
            var volume = new GameObject("LobbyPostProcessing").AddComponent<Volume>(); volume.isGlobal = true;
            volume.sharedProfile = MakeProfile();
            var probe = new GameObject("LobbyReflection").AddComponent<ReflectionProbe>();
            probe.transform.position = new Vector3(0, 1.2f, 0); probe.size = new Vector3(16, 9, 20);
            probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            probe.resolution = 128; probe.boxProjection = true; probe.intensity = .7f; probe.cullingMask = ~(1 << 5);
            Dust(environment);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        private static void CreateLighting(Transform environment)
        {
            var lighting = new GameObject("HangarLighting").transform; lighting.SetParent(environment);
            // The plate's strongest practicals are amber, above and left. Keep neutral fill subdued.
            var key = Light("KeyLight", lighting, LightType.Directional, new Vector3(-2, 4, -2), new Color(1, .92f, .83f), 1.45f);
            key.transform.rotation = Quaternion.Euler(48, -38, 0);
            key.shadows = LightShadows.Soft; key.shadowBias = .012f; key.shadowNormalBias = .025f;
            Light("NeutralFill", lighting, LightType.Point, new Vector3(1.6f, 2.3f, -2.4f), new Color(.74f, .81f, 1), 1.5f, 7);
            Light("AmberRim", lighting, LightType.Point, new Vector3(-1.7f, 2.2f, 1.4f), new Color(1, .32f, .055f), 8, 5);
            Light("FloorBounce", lighting, LightType.Point, new Vector3(-1.3f, .18f, -.3f), new Color(1, .29f, .045f), .9f, 2.6f);
            RenderSettings.skybox = null; RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.17f, .18f, .20f);
            RenderSettings.ambientEquatorColor = new Color(.085f, .085f, .080f);
            RenderSettings.ambientGroundColor = new Color(.075f, .046f, .025f);
        }

        private static Light Light(string name, Transform parent, LightType type, Vector3 position, Color color, float intensity, float range = 10)
        {
            var light = new GameObject(name).AddComponent<Light>();
            light.transform.SetParent(parent); light.transform.position = position;
            light.type = type; light.color = color; light.intensity = intensity; light.range = range;
            return light;
        }

        private static VolumeProfile MakeProfile()
        {
            string path = Art + "/HangarVolume.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null) { profile = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(profile, path); }
            if (!profile.TryGet<Bloom>(out var bloom)) { bloom = profile.Add<Bloom>(); AssetDatabase.AddObjectToAsset(bloom, profile); }
            bloom.threshold.Override(1.3f); bloom.intensity.Override(.14f); bloom.scatter.Override(.5f);
            if (!profile.TryGet<Vignette>(out var vignette)) { vignette = profile.Add<Vignette>(); AssetDatabase.AddObjectToAsset(vignette, profile); }
            vignette.intensity.Override(.12f); vignette.smoothness.Override(.7f);
            // The matte painting already includes grading and distant haze. Do not blur it a second time.
            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void Dust(Transform parent)
        {
            var particles = new GameObject("AirborneDust").AddComponent<ParticleSystem>();
            particles.transform.SetParent(parent); particles.transform.position = new Vector3(0, 1.4f, 1.2f);
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = true; main.prewarm = true; main.duration = 12; main.startLifetime = 12;
            main.startSpeed = .028f; main.startSize = new ParticleSystem.MinMaxCurve(.008f, .025f);
            main.maxParticles = 90; main.startColor = new Color(1, .58f, .22f, .45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = particles.emission; emission.rateOverTime = 5;
            var shape = particles.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(6, 3.1f, 3);
            var velocity = particles.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = .012f; velocity.y = .045f; velocity.z = -.013f;
            var fade = particles.colorOverLifetime; fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.6f, .15f), new GradientAlphaKey(.5f, .75f), new GradientAlphaKey(0, 1) });
            fade.color = gradient;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(Art + "/Materials/DustMotes.mat");
            particles.Play();
        }
    }
}
