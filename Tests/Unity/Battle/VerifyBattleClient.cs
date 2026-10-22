#if UNITY_EDITOR
using OperationBlacktide.Client.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyBattleClient
{
    public static void Run()
    {
        EditorSceneManager.OpenScene(GameSceneFlow.BattleScenePath);
        new GameObject("Battle client verification").AddComponent<BattleClientVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
#endif
