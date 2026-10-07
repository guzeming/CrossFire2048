using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingWeaponActions
    {
        public const string AnimationPath = "Assets/Art/Training/RifleWeaponActions.fbx";
        public const string SidearmPath = "Assets/Art/Training/SidearmWeaponActions.fbx";
        public const string PrimaryPath = "Assets/Art/Training/PrimaryWeaponActions.fbx";
        private const string MaskPath = "Assets/Resources/Training/WeaponUpperBody.mask";
        private const string AudioPath = "Assets/Art/Combat/ReloadAudio/";
        [Serializable] private sealed class Manifest { public Clip[] clips; }
        [Serializable] private sealed class Clip { public string name; public float first, last; public bool loop, additive; }
        [Serializable] private sealed class PrimaryManifest { public PrimaryBinding[] weapons; }
        [Serializable] private sealed class PrimaryBinding { public string id, profile; public string[] sounds; public float[] times; }

        [MenuItem("OperationBlacktide/Training/Build Weapon Actions")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode before building weapon actions.");
            var importer = (ModelImporter)AssetImporter.GetAtPath(AnimationPath);
            if (importer == null) throw new InvalidOperationException("Export RifleWeaponActions.fbx first.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Art/Training/weapon_actions_manifest.json"));
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true; importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                loopTime = false, keepOriginalPositionY = true,
                hasAdditiveReferencePose = true, additiveReferencePoseFrame = c.first
            }).ToArray();
            importer.SaveAndReimport();
            ImportSidearms();
            ImportPrimaries();
            var catalog = Resources.Load<TrainingWeaponCatalog>("Training/TrainingWeapons");
            foreach (var weapon in catalog.weapons) Bind(weapon);
            EditorUtility.SetDirty(catalog);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(BuildTrainingAnimations.ControllerPath);
            ConfigureController(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("WEAPON_ACTIONS_BUILD_PASS");
        }

        // Weapon-effects rebuilds also keep the action and foley references.
        public static void Bind(TrainingWeaponDefinition weapon)
        {
            if (weapon.IsPistol || weapon.IsMelee) { BindSidearm(weapon); return; }
            var primaries = JsonUtility.FromJson<PrimaryManifest>(File.ReadAllText("Assets/Art/Combat/primary_sources.json"));
            var primary = Array.Find(primaries.weapons, p => p.id == weapon.id);
            if (primary != null)
            {
                var actions = AssetDatabase.LoadAllAssetsAtPath(PrimaryPath).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
                weapon.idleAnimation = actions["idle_" + primary.profile];
                weapon.fireAnimation = actions["fire_" + primary.profile];
                weapon.reloadAnimation = actions["reload_" + primary.profile];
                weapon.reloadSounds = primary.sounds.Select((name, i) => Cue(primary.times[i], name, .65f)).ToArray();
                if (weapon.reloadSounds.Any(c => c.clip == null)) throw new InvalidOperationException("Missing primary foley: " + weapon.id);
                return;
            }
            string profile = weapon.id == "weapon_rif_ak47" ? "ak" : weapon.silenced ? "m4a1s" : "m4a4";
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>().ToArray();
            weapon.fireAnimation = clips.FirstOrDefault(c => c.name == "fire_" + profile);
            weapon.reloadAnimation = clips.FirstOrDefault(c => c.name == "reload_" + profile);
            // World clips contain no exported sound events. These normalized cues are authored against their motion.
            weapon.reloadSounds = profile == "ak" ? new[]
            {
                Cue(.12f,"ak47_clipout_01",.65f), Cue(.55f,"ak47_addammo_02",.65f), Cue(.78f,"ak47_boltpull",.7f)
            } : new[]
            {
                Cue(.12f,"m4a1_clipout",.65f), Cue(.53f,"m4a1_clipin",.65f),
                Cue(.76f,weapon.silenced ? "m4a1_silencer_boltback" : "m4a1_boltback",.6f),
                Cue(.87f,weapon.silenced ? "m4a1_silencer_boltforward" : "m4a1_boltforward",.6f)
            };
            if (weapon.IsSniper)
            {
                string id = weapon.id.Substring("weapon_snip_".Length);
                string action = id == "g3sg1" ? "slide" : "bolt";
                weapon.reloadSounds = new[] { Cue(.12f, id + "_clipout", .65f), Cue(.53f, id + "_clipin", .65f),
                    Cue(.76f, id + "_" + action + "back", .65f), Cue(.87f, id + "_" + action + "forward", .65f) };
            }
            if (weapon.reloadSounds.Any(c => c.clip == null)) throw new InvalidOperationException("Missing reload audio for " + weapon.id);
        }

        private static void ImportPrimaries()
        {
            var importer = AssetImporter.GetAtPath(PrimaryPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Export PrimaryWeaponActions.fbx first.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Art/Training/primary_actions_manifest.json"));
            float reference = manifest.clips.Single(c => c.name == "idle_primary_reference").first;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true; importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last, loopTime = c.loop,
                keepOriginalPositionY = true, hasAdditiveReferencePose = true,
                additiveReferencePoseFrame = c.name.StartsWith("idle_") ? reference : c.first
            }).ToArray();
            importer.SaveAndReimport();
        }

        private static void ImportSidearms()
        {
            var importer = AssetImporter.GetAtPath(SidearmPath) as ModelImporter;
            if (importer == null) throw new InvalidOperationException("Export SidearmWeaponActions.fbx first.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Art/Training/sidearm_actions_manifest.json"));
            float pistolReference = manifest.clips.Single(c => c.name == "idle_pistol").first;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true; importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last, loopTime = c.loop,
                keepOriginalPositionY = true, hasAdditiveReferencePose = c.additive || c.name.StartsWith("idle_"),
                additiveReferencePoseFrame = c.name.StartsWith("idle_") && c.name != "idle_knife" ? pistolReference : c.first
            }).ToArray();
            importer.SaveAndReimport();
        }

        private static void BindSidearm(TrainingWeaponDefinition weapon)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(SidearmPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            string family = weapon.IsMelee ? "knife" : "pistol";
            weapon.locomotionAnimations = clips.Values.Where(c => c.name.EndsWith("_" + family) &&
                (c.name.StartsWith("walk_") || c.name.StartsWith("run_") || c.name.StartsWith("jump_") || c.name == "idle_" + family)).ToArray();
            if (weapon.locomotionAnimations.Length != 22) throw new InvalidOperationException("Missing locomotion: " + weapon.id);
            string profile = weapon.IsMelee ? "knife" : weapon.id.Substring("weapon_pist_".Length);
            if (profile == "glock18") profile = "glock";
            if (profile == "hkp2000") profile = "hkp";
            if (profile == "usp_silencer") profile = "usp";
            weapon.idleAnimation = clips["idle_" + profile];
            weapon.fireAnimation = clips["fire_" + profile];
            weapon.alternateFireAnimation = weapon.IsMelee ? clips["stab_knife"] : weapon.IsDual ? clips["fire_elite_left"] : null;
            weapon.reloadAnimation = weapon.IsMelee ? null : weapon.IsTaser ? null : clips["reload_" + profile];
            string[] sounds;
            switch (profile)
            {
                case "knife": sounds = Array.Empty<string>(); break;
                case "cz75a": sounds = new[] { "cz75_clipin_01", "cz75_addammo_01" }; break;
                case "deagle": sounds = new[] { "de_clipout", "de_clipin", "de_slideforward" }; break;
                case "elite": sounds = new[] { "elite_clipout", "elite_leftclipin", "elite_rightclipin", "elite_sliderelease" }; break;
                case "taser": sounds = new[] { "taser_charging", "taser_charge_ready" }; break;
                default:
                    string soundProfile = profile == "hkp" ? "hkp2000" : profile;
                    sounds = new[] { soundProfile + "_clipout", soundProfile + "_clipin", soundProfile +
                        (profile == "tec9" ? "_boltrelease" : profile == "revolver" ? "_siderelease" : "_sliderelease") };
                    break;
            }
            float[] timings = sounds.Length == 4 ? new[] { .12f, .42f, .62f, .87f } :
                sounds.Length == 2 ? new[] { .12f, .87f } : new[] { .12f, .53f, .87f };
            weapon.reloadSounds = sounds.Select((name, i) => Cue(timings[i], name, .65f)).ToArray();
            if (weapon.reloadSounds.Any(c => c.clip == null)) throw new InvalidOperationException("Missing sidearm foley: " + weapon.id);
        }

        private static TrainingReloadSound Cue(float time, string name, float volume) => new TrainingReloadSound
        { normalizedTime = time, clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioPath + name + ".wav"), volume = volume };

        public static void ConfigureController(AnimatorController controller)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>().ToArray();
            var fire = clips.FirstOrDefault(c => c.name == "fire_m4a4");
            var reload = clips.FirstOrDefault(c => c.name == "reload_m4a4");
            if (fire == null || reload == null) return; // Locomotion can be built before action import on a fresh project.
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
            if (mask == null) { mask = new AvatarMask { name = "Weapon Upper Body" }; AssetDatabase.CreateAsset(mask,MaskPath); }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(AnimationPath);
            mask.transformCount = 0; mask.AddTransformPath(model.transform,true);
            for (int i = 0; i < mask.transformCount; i++)
            {
                string path = mask.GetTransformPath(i);
                // Add only upper-body deltas; retain all pelvis/leg counter-rotation from locomotion.
                mask.SetTransformActive(i,path.Split('/').Any(p => p.StartsWith("spine",StringComparison.Ordinal)));
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i,false);
            EditorUtility.SetDirty(mask);
            RemoveLayer(controller, TrainingWeaponPresentation.HoldLayer);
            RemoveLayer(controller, TrainingWeaponPresentation.FireLayer);
            RemoveLayer(controller, TrainingWeaponPresentation.ReloadLayer);
            var sidearmClips = AssetDatabase.LoadAllAssetsAtPath(SidearmPath).OfType<AnimationClip>().ToArray();
            var hold = sidearmClips.FirstOrDefault(c => c.name == "idle_glock");
            if (hold != null) AddLayer(controller, TrainingWeaponPresentation.HoldLayer, "Hold", hold, mask);
            AddLayer(controller, TrainingWeaponPresentation.FireLayer, "Fire", fire, mask);
            var alternate = sidearmClips.FirstOrDefault(c => c.name == "fire_elite_left");
            if (alternate != null)
            {
                var machine = controller.layers[controller.layers.Length - 1].stateMachine;
                var state = machine.AddState("AlternateFire"); state.motion = alternate; state.speed = 0; state.writeDefaultValues = false;
            }
            AddLayer(controller, TrainingWeaponPresentation.ReloadLayer, "Reload", reload, mask);
            EditorUtility.SetDirty(controller);
        }

        private static void RemoveLayer(AnimatorController controller, string name)
        {
            int index = Array.FindIndex(controller.layers,l => l.name == name);
            if (index < 0) return;
            // RemoveLayer owns the state-machine subassets and destroys them itself.
            controller.RemoveLayer(index);
        }

        private static void AddLayer(AnimatorController controller, string name, string stateName, AnimationClip clip, AvatarMask mask)
        {
            controller.AddLayer(name);
            var layers = controller.layers; var layer = layers[layers.Length-1];
            layer.avatarMask = mask; layer.blendingMode = AnimatorLayerBlendingMode.Additive; layer.defaultWeight = 0;
            var state = layer.stateMachine.AddState(stateName); state.motion = clip; state.speed = 0; state.writeDefaultValues = false;
            layer.stateMachine.defaultState = state; controller.layers = layers;
        }
    }
}
