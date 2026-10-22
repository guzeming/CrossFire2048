using OperationBlacktide.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTopDown
{
    public static void Run()
    {
        BuildTrainingWeapons.Build();
        RunWithExistingWeapons();
    }

    // Camera-only changes can exercise the shipped weapon assets without regenerating animations.
    public static void RunWithExistingWeapons()
    {
        BuildTrainingHud.Build();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Top Down Verification").AddComponent<TopDownVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
