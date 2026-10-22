#if UNITY_EDITOR
using OperationBlacktide.Client.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class VerifyDemolitionClient
{
    public static void Run()
    {
        OperationBlacktide.EditorTools.BuildDemolitionUI.Build();
        EditorSceneManager.OpenScene(GameSceneFlow.LoginScenePath);
        new GameObject("Demolition verification").AddComponent<DemolitionVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
#endif
