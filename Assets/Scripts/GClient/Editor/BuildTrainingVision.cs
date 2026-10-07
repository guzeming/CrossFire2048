using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace OperationBlacktide.Client.Editor
{
    public static class BuildTrainingVision
    {
        [MenuItem("OperationBlacktide/Training/Build Vision")]
        public static void Build()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                bool present = false;
                foreach (var feature in data.rendererFeatures) present |= feature is TrainingVisionRendererFeature;
                if (present) continue;
                var sight = ScriptableObject.CreateInstance<TrainingVisionRendererFeature>();
                sight.name = "Training character sight";
                AssetDatabase.AddObjectToAsset(sight, data);
                data.rendererFeatures.Add(sight);
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("TRAINING_VISION_BUILD_PASS");
        }
    }
}
