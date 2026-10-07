using System;
using System.IO;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingThrowables
    {
        public const string CatalogPath = "Assets/Resources/Training/TrainingThrowables.asset";
        private const string Art = "Assets/Art/Throwables/";

        [MenuItem("OperationBlacktide/Training/Build Throwables")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            AssetDatabase.Refresh();
            BuildTrainingThrowableActions.Build();
            var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
            var catalog = AssetDatabase.LoadAssetAtPath<TrainingThrowableCatalog>(CatalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<TrainingThrowableCatalog>(); AssetDatabase.CreateAsset(catalog, CatalogPath); }
            catalog.fire = Material("Fire", true, 1.2f);
            catalog.explosion = Material("Explosion", true, 2.5f);
            catalog.smoke = Material("Smoke", false, 2.2f);
            catalog.trajectory = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Combat/Tracer.mat");
            catalog.ringing = Clip("flashbang/explosion_ring_loop");
            catalog.items = new[] {
                Item("weapon_hegrenade", TrainingThrowableKind.HighExplosive, 1.7f, 6, 0, 110,
                    "hegrenade/he_bounce-1", null, null, "hegrenade/hegrenade_detonate_02", "hegrenade/hegrenade_detonate_03"),
                Item("weapon_flashbang", TrainingThrowableKind.Flash, 1.6f, 18, 4, 0,
                    "hegrenade/he_bounce-1", null, null, "flashbang/flashbang_explode1", "flashbang/flashbang_explode2"),
                Item("weapon_smokegrenade", TrainingThrowableKind.Smoke, 2.5f, 3.8f, 18, 0,
                    "smokegrenade/grenade_hit1", null, "smokegrenade/smoke_clear", "smokegrenade/smoke_emit"),
                Item("weapon_incendiarygrenade", TrainingThrowableKind.Fire, 3.5f, 3.5f, 7, 32,
                    "incgrenade/inc_grenade_bounce_m", "molotov/fire_loop_1", "molotov/molotov_extinguish", "incgrenade/inc_grenade_detonate_1"),
                Item("weapon_molotov", TrainingThrowableKind.Fire, 3.5f, 3.5f, 7, 32,
                    "molotov/molotov_smash_01", "molotov/fire_loop_1", "molotov/molotov_extinguish", "molotov/molotov_detonate_1")
            };
            foreach (var item in catalog.items) {
                item.model = loadout.Weapon(item.id)?.prefab;
                if (item.model == null) throw new InvalidOperationException("Missing grenade model " + item.id);
                BuildTrainingThrowableActions.Bind(item);
            }
            EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_THROWABLES_BUILD_PASS");
        }

        private static TrainingThrowableDefinition Item(string id, TrainingThrowableKind kind, float fuse, float radius,
            float duration, float damage, string bounce, string loop, string finish, params string[] detonations)
        {
            return new TrainingThrowableDefinition { id = id, kind = kind, fuse = fuse, radius = radius, duration = duration, damage = damage,
                pin = Clip(id == "weapon_molotov" ? "molotov/molotov_throw_fire_01" : "hegrenade/pinpull"), release = Clip(id == "weapon_molotov" ? "molotov/molotov_throw_01" : "hegrenade/grenade_throw"),
                bounce = Clip(bounce), loop = Clip(loop), finish = Clip(finish), detonate = Array.ConvertAll(detonations, Clip) };
        }

        private static AudioClip Clip(string name)
        {
            if (name == null) return null;
            string path = Art + "Audio/" + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new FileNotFoundException(path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis; settings.quality = .8f;
            importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        private static Material Material(string name, bool additive, float intensity)
        {
            string texturePath = Art + "Textures/" + name + ".png";
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null) throw new FileNotFoundException("Run prepare_training_throwables.py first: " + texturePath);
            importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            string path = Art + name + ".mat";
            var shader = Shader.Find("OperationBlacktide/Throwable Particles");
            if (shader == null) throw new InvalidOperationException("Missing throwable particle shader");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_Intensity", intensity); EditorUtility.SetDirty(material);
            return material;
        }
    }
}
