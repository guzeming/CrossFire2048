using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public static class VerifyArchitecture
{
    private static int checks;
    public static void Run()
    {
        try
        {
            checks = 0;
            Debug.Log("LAUNCHER_PROGRESS 50 检查程序集和装备资源");
            Check(typeof(EquipmentCatalog).Assembly.GetName().Name == "Blacktide.Client", "Client assembly");
            var references = typeof(EquipmentCatalog).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Check(!references.Contains("Blacktide.BattleServer"), "Client must not reference server");
            var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
            Check(catalog != null && catalog.agents.Length == 10 && catalog.weapons.Length == 47 && catalog.poses.Length == 26, "Migrated equipment data");
            Check(catalog.controller != null && catalog.agents.All(a => a.prefab != null), "Equipment asset bindings");
            Check(Resources.Load<WeaponCatalog>("Gameplay/Weapons") != null, "Weapons resource");
            Check(Resources.Load<ThrowableCatalog>("Gameplay/Throwables") != null, "Throwables resource");
            Check(Resources.Load<RuntimeAnimatorController>("Gameplay/Locomotion") != null, "Locomotion resource");
            Check(Resources.Load<Shader>("Gameplay/CharacterVision") != null, "Vision resource");
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources" });
            var scenes = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
            bool buildPlayers = Environment.GetCommandLineArgs().Contains("--verify-player-builds");
            int done = 0, total = prefabs.Length + scenes.Length;
            void Report(string path) => Debug.Log("LAUNCHER_PROGRESS " +
                (55 + (buildPlayers ? 20 : 40) * done / Math.Max(1, total)) +
                " 已检查 " + done + "/" + total + " 项：" + path);
            Report("程序集和装备资源检查完成");
            foreach (string id in prefabs)
            {
                string path = AssetDatabase.GUIDToAssetPath(id);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Check(prefab != null, path);
                VerifyObject(prefab, path);
                done++; Report(path);
            }
            foreach (string id in scenes)
            {
                string path = AssetDatabase.GUIDToAssetPath(id);
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects()) VerifyObject(root, path);
                done++; Report(path);
            }
            if (buildPlayers) VerifyPlayerBuilds();
            Debug.Log("ARCHITECTURE_VERIFY_PASS checks=" + checks);
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }

    private static void VerifyPlayerBuilds()
    {
        var args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "--output-root") + 1];
        BuildBattleArena.BuildPreparedScenes(new[] { BuildBattleArena.ServerScene }, "BattleServer/Blacktide.BattleServer.exe", false, false);
        string server = Path.Combine(output, "BattleServer/Blacktide.BattleServer_Data/Managed");
        Check(File.Exists(Path.Combine(server, "Blacktide.BattleServer.dll")), "Server implementation included");
        Check(!File.Exists(Path.Combine(server, "Blacktide.Client.dll")), "Client implementation excluded from server");
        Debug.Log("LAUNCHER_PROGRESS 85 服务端程序集验证完成，正在构建客户端");
        BuildBattleArena.BuildPreparedScenes(EditorBuildSettings.scenes.Where(s => s.enabled && s.path != BuildBattleArena.ServerScene).Select(s => s.path).ToArray(), "Client/Blacktide.exe", false, false);
        string client = Path.Combine(output, "Client/Blacktide_Data/Managed");
        Check(File.Exists(Path.Combine(client, "Blacktide.Client.dll")), "Client implementation included");
        Check(!File.Exists(Path.Combine(client, "Blacktide.BattleServer.dll")), "Server implementation excluded from client");
        Debug.Log("LAUNCHER_PROGRESS 95 客户端程序集验证完成");
        Debug.Log("PLAYER_ASSEMBLY_BOUNDARIES_PASS");
    }

    private static void VerifyObject(GameObject root, string path)
    {
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0, path + ": missing script on " + transform.name);
        foreach (var button in root.GetComponentsInChildren<Button>(true))
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
                Check(button.onClick.GetPersistentTarget(i) != null, path + ": broken event on " + button.name);
    }
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException(message); }
}
