#if UNITY_EDITOR
using OperationBlacktide.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEditor.SceneManagement;
using OperationBlacktide.Client.App;

public static class VerifyBattleBots
{
    public static void Run()
    {
        BuildBattleArena.PrepareArenaOnly();
        EditorSceneManager.OpenScene(BuildBattleArena.ServerScene);
        new GameObject("Battle bot verification").AddComponent<BattleBotVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
    public static void RunClient()
    {
        EditorSceneManager.OpenScene(GameSceneFlow.BattleScenePath);
        new GameObject("Bot client verification").AddComponent<BotClientVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
#endif
