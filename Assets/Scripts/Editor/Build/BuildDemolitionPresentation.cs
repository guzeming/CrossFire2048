using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Demolition;
using UnityEditor;
using UnityEngine;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildDemolitionPresentation
    {
        public const string Folder = "Assets/Art/Demolition/Animations/";
        public const string AssetPath = "Assets/Resources/Gameplay/Demolition/Presentation.asset";
        [Serializable] sealed class Manifest { public Clip[] clips; }
        [Serializable] sealed class Clip { public string name; public float first, last; }

        [MenuItem("OperationBlacktide/Demolition/Build C4 Presentation")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.Refresh();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Folder + "demolition_actions_manifest.json"));
            string modelPath = Folder + "DemolitionActions.fbx";
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                loopTime = c.name.EndsWith("loop"), keepOriginalPositionY = true
            }).ToArray();
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            foreach (var source in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")))
            {
                string path = Folder + source.name + ".anim";
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null) { clip = new AnimationClip { name = source.name }; AssetDatabase.CreateAsset(clip, path); }
                clip.ClearCurves(); clip.frameRate = source.frameRate;
                // Full-body rotations, plus the pelvis height needed to crouch. Keep each agent's
                // bone lengths/scale and the network root intact; clips never move the pawn.
                foreach (var bone in model.GetComponentsInChildren<Transform>())
                {
                    string bonePath = AnimationUtility.CalculateTransformPath(bone, model.transform);
                    if (!bonePath.Contains("/pelvis")) continue;
                    for (int axis = 0; axis < 4; axis++)
                    {
                        var binding = EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "m_LocalRotation." + "xyzw"[axis]);
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding) ??
                            AnimationCurve.Constant(0, source.length, bone.localRotation[axis]));
                    }
                    if (bone.name == "pelvis")
                    {
                        var binding = EditorCurveBinding.FloatCurve(bonePath, typeof(Transform), "m_LocalPosition.y");
                        AnimationUtility.SetEditorCurve(clip, binding, AnimationUtility.GetEditorCurve(source, binding) ??
                            AnimationCurve.Constant(0, source.length, bone.localPosition.y));
                    }
                }
                if (AnimationUtility.GetCurveBindings(clip).Length < 100) throw new InvalidOperationException("Missing C4 rig curves: " + source.name);
                clip.EnsureQuaternionContinuity(); EditorUtility.SetDirty(clip);
            }
            var data = AssetDatabase.LoadAssetAtPath<DemolitionPresentationAssets>(AssetPath);
            if (data == null) { data = ScriptableObject.CreateInstance<DemolitionPresentationAssets>(); AssetDatabase.CreateAsset(data, AssetPath); }
            data.bombPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Loadout/Prefabs/weapon_c4.prefab");
            data.groundEuler = new Vector3(-90, 0, 0);
            var carrier = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Loadout/Prefabs/tm_phoenix.prefab"));
            data.worldScale = carrier.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_R").lossyScale;
            UnityEngine.Object.DestroyImmediate(carrier);
            data.plant = LoadClip("planting"); data.defuseEnter = LoadClip("defuse_crouch_enter"); data.defuseLoop = LoadClip("defuse_crouch_loop");
            data.draw = Sound("c4_draw"); data.initiate = Sound("c4_initiate"); data.click = Sound("c4_click");
            data.planted = Sound("c4_plant"); data.defuseStart = Sound("c4_disarmstart"); data.defused = Sound("c4_disarmfinish");
            data.beep = Sound("c4_beep2"); data.urgentBeep = Sound("c4_beep3_10sec"); data.explode = Sound("c4_explode1");
            EditorUtility.SetDirty(data); AssetDatabase.SaveAssets();
            Debug.Log("DEMOLITION_PRESENTATION_BUILD_PASS");
        }
        static AnimationClip LoadClip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");
        static AudioClip Sound(string name)
        {
            string path = "Assets/Art/Demolition/Audio/" + name + ".wav";
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) throw new FileNotFoundException(path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.preloadAudioData = true;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = settings; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
