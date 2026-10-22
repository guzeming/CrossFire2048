using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Gameplay.Cameras;
using UnityEditor;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildFirstPerson
    {
        private const string Art = "Assets/Art/FirstPerson/";
        [Serializable] private sealed class Record { public string id; public Clip[] clips; public Surface[] materials; }
        [Serializable] private sealed class Clip { public string name; public float first, last; }
        [Serializable] private sealed class Surface { public string name, color, normal; }

        [MenuItem("OperationBlacktide/Camera/Build First Person Assets")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Art + "Materials"); Directory.CreateDirectory(Art + "Prefabs");
            AssetDatabase.Refresh();
            var weapons = Directory.GetFiles(Art + "Records", "*.json").OrderBy(p => p).Select(p =>
            {
                var record = JsonUtility.FromJson<Record>(File.ReadAllText(p));
                string path = Art + "Models/" + record.id + ".fbx";
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
                importer.importAnimation = true; importer.importCameras = importer.importLights = false;
                importer.animationCompression = ModelImporterAnimationCompression.Off;
                importer.clipAnimations = record.clips.Select(c => new ModelImporterClipAnimation
                {
                    name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                    loopTime = c.name == "idle", keepOriginalPositionY = true,
                    keepOriginalPositionXZ = true, keepOriginalOrientation = true
                }).ToArray();
                foreach (var surface in record.materials)
                {
                    string name = string.Concat(surface.name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_'));
                    string materialPath = Art + "Materials/" + name + ".mat";
                    var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (mat == null)
                    { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, materialPath); }
                    mat.SetTexture("_BaseMap", Texture(surface.color, false));
                    mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", .25f);
                    if (!string.IsNullOrEmpty(surface.normal))
                    { mat.SetTexture("_BumpMap", Texture(surface.normal, true)); mat.EnableKeyword("_NORMALMAP"); }
                    EditorUtility.SetDirty(mat);
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), surface.name), mat);
                }
                importer.SaveAndReimport();
                var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                instance.name = record.id;
                var importedClips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
                importedClips.First(c => c.name == "idle").SampleAnimation(instance, 0);
                AddMuzzle(instance);
                foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.updateWhenOffscreen = true;
                foreach (var node in instance.GetComponentsInChildren<Transform>()) node.gameObject.layer = FirstPersonView.ViewmodelLayer;
                var prefab = PrefabUtility.SaveAsPrefabAsset(instance, Art + "Prefabs/" + record.id + ".prefab");
                UnityEngine.Object.DestroyImmediate(instance);
                var clips = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
                AnimationClip Find(string name) => clips.FirstOrDefault(c => c.name == name);
                return new FirstPersonWeapon { id = record.id, prefab = prefab, idle = Find("idle"), draw = Find("draw"),
                    fire = Find("fire"), reload = Find("reload"), alternate = Find("alternate"), prepare = Find("prepare") };
            }).ToArray();
            const string catalogPath = "Assets/Resources/Gameplay/FirstPerson.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<FirstPersonCatalog>(catalogPath);
            if (catalog == null) { catalog = ScriptableObject.CreateInstance<FirstPersonCatalog>(); AssetDatabase.CreateAsset(catalog, catalogPath); }
            catalog.weapons = weapons; EditorUtility.SetDirty(catalog); AssetDatabase.SaveAssets();
            Debug.Log("FIRST_PERSON_ASSETS_BUILT " + weapons.Length);
        }

        private static Texture2D Texture(string name, bool normal)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("Assets/")) return AssetDatabase.LoadAssetAtPath<Texture2D>(name);
            string path = Art + "Textures/" + name;
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.maxTextureSize = 2048; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void AddMuzzle(GameObject model)
        {
            var points = new System.Collections.Generic.List<Vector3>();
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.name == "Gloves" || skin.name == "Sleeves" || skin.name.Contains("holster")) continue;
                var baked = new Mesh(); skin.BakeMesh(baked, true);
                points.AddRange(baked.vertices.Select(v => model.transform.InverseTransformPoint(skin.transform.TransformPoint(v))));
                UnityEngine.Object.DestroyImmediate(baked);
            }
            if (points.Count == 0) return;
            var nodes = model.GetComponentsInChildren<Transform>();
            bool dual = model.name == "weapon_pist_elite";
            for (int side = 0; side < (dual ? 2 : 1); side++)
            {
                var subset = dual ? points.Where(v => side == 0 ? v.x > 0 : v.x < 0).ToArray() : points.ToArray();
                if (subset.Length == 0) continue;
                float tip = subset.Max(v => v.z);
                var vertices = subset.Where(v => v.z >= tip - .006f).ToArray();
                Vector3 point = vertices.Aggregate(Vector3.zero, (a, b) => a + b) / vertices.Length;
                string bone = dual ? side == 0 ? "Gun_weapon_r" : "Gun_weapon_l" : "Gun_weapon_offset";
                var parent = nodes.FirstOrDefault(t => t.name == bone) ?? nodes.FirstOrDefault(t => t.name == "Gun_weapon") ?? model.transform;
                var marker = new GameObject(side == 0 ? "ViewmodelMuzzle" : "ViewmodelMuzzleLeft").transform;
                marker.SetParent(parent, false); marker.position = model.transform.TransformPoint(point);
            }
        }
    }
}
