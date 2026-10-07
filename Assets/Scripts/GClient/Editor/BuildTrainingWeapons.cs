using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingWeapons
    {
        private const string Art = "Assets/Art/Combat/";
        public const string CatalogPath = "Assets/Resources/Training/TrainingWeapons.asset";
        [Serializable] private sealed class ImportedData { public TrainingWeaponDefinition[] weapons; }

        [MenuItem("OperationBlacktide/Training/Build Weapon Effects")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before building weapon effects.");
            AssetDatabase.Refresh();
            var imported = JsonUtility.FromJson<ImportedData>(File.ReadAllText(Art + "weapon_data.json"));
            var loadout = Resources.Load<LobbyLoadoutCatalog>("Loadout/LobbyCatalog");
            var catalog = AssetDatabase.LoadAssetAtPath<TrainingWeaponCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<TrainingWeaponCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            catalog.weapons = imported.weapons;
            var missing = loadout.weapons.Where(w => (TrainingWeaponSelection.SlotOf(w) == 1 || TrainingWeaponSelection.SlotOf(w) == 2) && catalog.Find(w.id) == null).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Guns without combat profiles: " + string.Join(", ", missing.Select(w => w.id)));
            foreach (var weapon in catalog.weapons)
            {
                weapon.hudIcon = AssetDatabase.LoadAssetAtPath<Sprite>(BuildTrainingHud.IconFolder + "/" + weapon.id + ".png") ?? loadout.Weapon(weapon.id).thumbnail;
                BuildTrainingWeaponActions.Bind(weapon);
                if (weapon.IsMelee)
                {
                    weapon.shots = new[] { "knife_slash1", "knife_slash2" }.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "MeleeAudio/" + n + ".wav")).ToArray();
                    weapon.meleeHeavySound = AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "MeleeAudio/knife_stab.wav");
                    weapon.meleeHitSounds = new[] { "knife_hit1", "knife_hit2" }.Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>(Art + "MeleeAudio/" + n + ".wav")).ToArray();
                    continue;
                }
                var model = loadout.Weapon(weapon.id).prefab;
                var mesh = model.GetComponentInChildren<MeshFilter>();
                // Exported rifles use mesh-local -Y for the barrel. Average only vertices at the tip,
                // rather than the whole bounds (which include the magazine and scope).
                var vertices = mesh.sharedMesh.vertices;
                float tip = vertices.Min(v => v.y);
                var tipVertices = vertices.Where(v => v.y < tip + mesh.sharedMesh.bounds.size.y * .012f).ToArray();
                Vector3 point = tipVertices.Aggregate(Vector3.zero, (a, b) => a + b) / tipVertices.Length;
                weapon.muzzlePosition = model.transform.InverseTransformPoint(mesh.transform.TransformPoint(point));
                weapon.ejectPosition = model.transform.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center));
                if (!weapon.IsPistol && weapon.idleAnimation != null)
                {
                    string profile = weapon.id.Substring(weapon.id.IndexOf('_', 7) + 1);
                    var grip = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Training/PrimaryWeaponGrips.fbx")
                        .GetComponentsInChildren<Transform>().Single(t => t.name == "TrainingGrip_" + profile);
                    weapon.hasCombatGrip = true;
                    weapon.combatGripPosition = grip.localPosition;
                    weapon.combatGripRotation = grip.localRotation;
                }
                if (weapon.IsDual)
                {
                    var grips = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Training/EliteTrainingGrips.fbx").GetComponentsInChildren<Transform>();
                    var right = grips.Single(t => t.name == "TrainingGrip_R");
                    var left = grips.Single(t => t.name == "TrainingGrip_L");
                    // The neutral legacy right-hand Elite mesh has the opposite facing.
                    // Correct that half, then keep each authored grip marker at its wrist.
                    var neutralFacing = Quaternion.AngleAxis(180, Vector3.forward);
                    weapon.combatGripRotation = right.localRotation * neutralFacing;
                    // The left-hand skeleton has the opposite roll; keep the slide above the grip.
                    weapon.offhandGripRotation = left.localRotation * Quaternion.AngleAxis(180, Vector3.down);
                    weapon.combatGripPosition = -(weapon.combatGripRotation * Vector3.Scale(model.transform.Find("Grip_R").localPosition, model.transform.localScale));
                    var offhand = loadout.Weapon(weapon.id).offhandPrefab;
                    weapon.offhandGripPosition = -(weapon.offhandGripRotation * Vector3.Scale(offhand.transform.Find("Grip_L").localPosition, offhand.transform.localScale));
                    var leftMesh = offhand.GetComponentInChildren<MeshFilter>();
                    float leftTip = leftMesh.sharedMesh.vertices.Min(v => v.y);
                    var leftVertices = leftMesh.sharedMesh.vertices.Where(v => v.y < leftTip + leftMesh.sharedMesh.bounds.size.y * .012f).ToArray();
                    Vector3 leftPoint = leftVertices.Aggregate(Vector3.zero, (a, b) => a + b) / leftVertices.Length;
                    weapon.offhandMuzzlePosition = offhand.transform.InverseTransformPoint(leftMesh.transform.TransformPoint(leftPoint));
                }
                string folder = weapon.id.Substring(weapon.id.IndexOf('_', 7) + 1);
                if (weapon.id == "weapon_rif_m4a1_silencer") folder = "m4a1s";
                weapon.shots = AssetDatabase.FindAssets("t:AudioClip", new[] { Art + "Audio/" + folder })
                    .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p)
                    .Select(AssetDatabase.LoadAssetAtPath<AudioClip>).ToArray();
                if (weapon.shots.Length == 0) throw new InvalidOperationException("Missing firing audio: " + weapon.id);
                Debug.Log("FIRING_PROFILE " + weapon.id + " cycle=" + weapon.cycleTime + " muzzle=" + weapon.muzzlePosition);
            }
            var shader = Shader.Find("OperationBlacktide/Combat Additive");
            if (shader == null) throw new InvalidOperationException("Missing combat shader.");
            catalog.flashMaterial = Material("MuzzleFlash", shader, Art + "Textures/muzzleflash4.png");
            catalog.tracerMaterial = Material("Tracer", shader, Art + "Textures/bullet_tracer_tintable.png");
            catalog.tracerMaterial.SetFloat("_SwapUV", 1);
            var decalShader = Shader.Find("OperationBlacktide/Bullet Hole");
            if (decalShader == null) throw new InvalidOperationException("Missing bullet hole shader.");
            catalog.bulletHoleMaterials = new Material[2];
            for (int i = 0; i < catalog.bulletHoleMaterials.Length; i++)
            {
                string colorPath = Art + "Textures/bullet_hole_" + (i + 1) + ".png";
                string aoPath = Art + "Textures/bullet_hole_" + (i + 1) + "_ao.png";
                foreach (string texturePath in new[] { colorPath, aoPath })
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
                    if (importer == null) throw new InvalidOperationException("Missing bullet hole texture: " + texturePath);
                    importer.sRGBTexture = texturePath == colorPath;
                    importer.alphaIsTransparency = texturePath == colorPath;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
                var decal = Material("BulletHole" + (i + 1), decalShader, colorPath);
                decal.SetTexture("_OcclusionTex", AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath));
                catalog.bulletHoleMaterials[i] = decal;
            }
            catalog.casingMaterial = Material("Casing", Shader.Find("Universal Render Pipeline/Lit"), null);
            catalog.casingMaterial.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Art + "Textures/casing_color.png"));
            catalog.casingMaterial.SetFloat("_Metallic", .7f);
            catalog.casingMaterial.SetFloat("_Smoothness", .6f);
            var casing = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Models/ak47_casing.fbx");
            var shell = casing.GetComponentInChildren<MeshFilter>();
            var combined = new Mesh { name = "Rifle Casing" };
            combined.CombineMeshes(new[] { new CombineInstance {
                // Include the FBX root's unit-conversion scale: this mesh is rendered without its imported hierarchy.
                mesh = shell.sharedMesh, transform = shell.transform.localToWorldMatrix
            } });
            string meshPath = Art + "Models/RifleCasing.asset";
            var previous = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (previous != null) { EditorUtility.CopySerialized(combined, previous); Object.DestroyImmediate(combined); }
            else { AssetDatabase.CreateAsset(combined, meshPath); previous = combined; }
            catalog.casingMesh = previous;
            EditorUtility.SetDirty(catalog);
            EditorUtility.SetDirty(catalog.tracerMaterial);
            EditorUtility.SetDirty(catalog.casingMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_WEAPONS_BUILD_PASS");
        }

        private static Material Material(string name, Shader shader, string texture)
        {
            string path = Art + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader;
            if (texture != null) material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
