using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyLobbyLoadout
{
    public static void BuildAndRun()
    {
        OperationBlacktide.Client.Editor.BuildLobbyLoadout.Build();
        Run();
    }
    public static void Run()
    {
        OperationBlacktide.Client.Editor.BuildLobbyLoadout.UpdateWeaponCategories();
        EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
        new GameObject("Loadout Verification").AddComponent<LoadoutVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
