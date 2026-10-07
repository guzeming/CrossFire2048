using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingAnimations
    {
        public const string AnimationPath = "Assets/Art/Training/RifleLocomotion.fbx";
        public const string ControllerPath = "Assets/Resources/Training/TrainingLocomotion.controller";
        [Serializable] private class Manifest { public Clip[] clips; }
        [Serializable] private class Clip { public string name; public float first, last; public bool loop; }

        [MenuItem("OperationBlacktide/Training/Build Character Animations")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding character animations.");
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText("Assets/Art/Training/locomotion_manifest.json"));
            var importer = (ModelImporter)AssetImporter.GetAtPath(AnimationPath);
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
            importer.importAnimation = true;
            importer.importCameras = importer.importLights = false;
            importer.preserveHierarchy = true;
            importer.optimizeGameObjects = false;
            importer.animationRotationError = importer.animationPositionError = .05f;
            importer.clipAnimations = manifest.clips.Select(c => new ModelImporterClipAnimation
            {
                name = c.name, takeName = "Scene", firstFrame = c.first, lastFrame = c.last,
                loopTime = c.loop, keepOriginalPositionY = true
            }).ToArray();
            importer.SaveAndReimport();
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimationPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            if (clips.Count != 22) throw new InvalidOperationException("Expected 21 locomotion clips and a combat aim pose.");

            var idle = clips["idle_rifle"];
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.layers = Array.Empty<AnimatorControllerLayer>();
            controller.parameters = Array.Empty<AnimatorControllerParameter>();
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath))
                if (asset != controller) Object.DestroyImmediate(asset, true);
            controller.AddLayer("Base Layer");
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter("MoveZ", AnimatorControllerParameterType.Float);
            controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;
            var walk = Tree(controller, "Eight Direction Walk", BlendTreeType.SimpleDirectional2D);
            var run = Tree(controller, "Eight Direction Run", BlendTreeType.SimpleDirectional2D);
            string[] directions = { "n", "ne", "e", "se", "s", "sw", "w", "nw" };
            for (int i = 0; i < directions.Length; i++)
            {
                float angle = i * Mathf.PI / 4;
                var direction = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle));
                walk.AddChild(clips["walk_" + directions[i] + "_rifle"], direction);
                run.AddChild(clips["run_" + directions[i] + "_rifle"], direction);
            }
            var movement = Tree(controller, "Idle Walk Run", BlendTreeType.Simple1D);
            movement.blendParameter = "Speed";
            movement.useAutomaticThresholds = false;
            movement.AddChild(idle, 0);
            movement.AddChild(walk, 4.5f);
            movement.AddChild(run, 7f);
            var locomotion = machine.AddState("Locomotion");
            locomotion.motion = movement;
            machine.defaultState = locomotion;

            var jump = Tree(controller, "Directional Jump", BlendTreeType.SimpleDirectional2D);
            jump.AddChild(clips["jump_stand_rifle"], Vector2.zero);
            jump.AddChild(clips["jump_n_rifle"], Vector2.up);
            jump.AddChild(clips["jump_e_rifle"], Vector2.right);
            jump.AddChild(clips["jump_s_rifle"], Vector2.down);
            jump.AddChild(clips["jump_w_rifle"], Vector2.left);
            var airborne = machine.AddState("Airborne");
            airborne.motion = jump;
            Transition(locomotion, airborne, AnimatorConditionMode.IfNot);
            Transition(airborne, locomotion, AnimatorConditionMode.If);

            // The rifle clips already counter-rotate the torso against the moving pelvis.
            // Preserve that whole-body motion so strafing and jumping also keep the muzzle forward.
            BuildTrainingWeaponActions.ConfigureController(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_ANIMATIONS_BUILD_PASS: 8 walk, 8 run, 5 jump clips and combat rifle idle, with full-body aim compensation.");
        }

        private static BlendTree Tree(AnimatorController controller, string name, BlendTreeType type)
        {
            var tree = new BlendTree
            {
                name = name, blendType = type, blendParameter = "MoveX", blendParameterY = "MoveZ",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(tree, controller);
            return tree;
        }

        private static void Transition(AnimatorState from, AnimatorState to, AnimatorConditionMode condition)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = .12f;
            transition.AddCondition(condition, 0, "Grounded");
        }
    }
}
