using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingThrowableActions
    {
        public const string Folder = "Assets/Art/Throwables/Animations/";
        public const string ModelPath = Folder + "ThrowableActions.fbx";
        [Serializable] private sealed class Manifest { public Clip[] clips; }
        [Serializable] private sealed class Clip { public string name; public float first, last; }

        [MenuItem("OperationBlacktide/Training/Build Throwable Actions")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            AssetDatabase.Refresh();
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) throw new FileNotFoundException("Run export_training_throwable_actions.py first.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "throwable_actions_manifest.json"));
            importer.animationType = ModelImporterAnimationType.Generic; importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true; importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true; importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                loopTime = c.name.StartsWith("idle_", StringComparison.Ordinal), keepOriginalPositionY = true
            }).ToArray();
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            {
                string path = Folder + source.name + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { clip = new AnimationClip { name = source.name }; AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.frameRate = source.frameRate;
                // Absolute upper-body rotations preserve the CS hand/finger poses. Never sample root,
                // pelvis, legs or scale; movement/jump animation and each agent's proportions stay intact.
                int count = 0;
                foreach (var bone in model.GetComponentsInChildren<Transform>())
                {
                    string bonePath = AnimationUtility.CalculateTransformPath(bone, model.transform);
                    if (!bonePath.Split('/').Contains("spine_0")) continue;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        var binding = EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]);
                        // FBX import can omit constant channels. Supply their actual source rest value,
                        // otherwise the lobby held-pose pass leaves those bones different each frame.
                        var curve = AnimationUtility.GetEditorCurve(source, binding) ?? AnimationCurve.Constant(0, source.length, bone.localRotation[axis]);
                        AnimationUtility.SetEditorCurve(clip, binding, curve); count++;
                    }
                }
                if (count < 40) throw new InvalidOperationException("Missing upper-body animation curves: " + source.name);
                clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip);
            }
            var catalog = AssetDatabase.LoadAssetAtPath<TrainingThrowableCatalog>(BuildTrainingThrowables.CatalogPath);
            if (catalog != null) { foreach (var item in catalog.items) Bind(item); EditorUtility.SetDirty(catalog); }
            AssetDatabase.SaveAssets(); Debug.Log("THROWABLE_ACTIONS_BUILD_PASS");
        }

        public static void Bind(TrainingThrowableDefinition data)
        {
            string profile = data.id == "weapon_molotov" ? "molotov" : "grenade";
            data.drawAnimation = Load("draw_" + profile); data.idleAnimation = Load("idle_" + profile);
            data.prepareAnimation = Load("prepare_" + profile); data.throwAnimation = Load("throw_grenade");
            // Authored against the exported world clip: throw_overhand_grenade opens the hand during its forward swing.
            data.releaseNormalizedTime = .25f;
            data.pinSoundNormalizedTime = profile == "molotov" ? .38f : .28f;
        }
        private static AnimationClip Load(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
            if (clip == null) throw new FileNotFoundException("Build Throwable Actions first: " + name);
            return clip;
        }
    }
}
