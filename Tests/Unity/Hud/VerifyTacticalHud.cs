#if UNITY_EDITOR
using OperationBlacktide.EditorTools;
using OperationBlacktide.Client.Features.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTacticalHud
{
    public static void Run()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        BuildCombatRadar.Build();
        EditorSceneManager.OpenScene("Assets/Scenes/BattleArena.unity");
        foreach (var client in Object.FindObjectsOfType<BattleArenaClient>()) client.enabled = false;
        new GameObject("Tactical HUD verification").AddComponent<TacticalHudVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
#endif
