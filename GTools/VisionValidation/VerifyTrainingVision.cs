using OperationBlacktide.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using OperationBlacktide.Client.Features.Training;

public static class VerifyTrainingVision
{
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        BuildTrainingVision.Build();
        BuildTrainingVision.Build();
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            int count = data.rendererFeatures.FindAll(feature => feature is TrainingVisionRendererFeature).Count;
            if (count != 1) throw new System.Exception("Vision feature must exist exactly once: " + data.name);
        }
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/DustII.unity", true) };
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Vision Verification").AddComponent<VisionVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
