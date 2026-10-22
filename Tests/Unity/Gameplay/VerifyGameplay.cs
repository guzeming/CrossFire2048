using System;
using System.IO;
using OperationBlacktide.Client.Features.Training;
using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using OperationBlacktide.Client.Gameplay.UI;
using OperationBlacktide.Client.Gameplay.Vision;
using OperationBlacktide.Shared.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class VerifyGameplay
{
    public static void TopDown()
    {
        CheckStructure();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Gameplay Verification").AddComponent<GameplayVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    public static void Throwables()
    {
        CheckStructure();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Throwable Verification").AddComponent<ThrowableVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    public static void Vision()
    {
        CheckStructure();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/DustII.unity", true) };
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Vision Verification").AddComponent<VisionVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void CheckStructure()
    {
        // Migration metadata names the old namespace, but executable dependencies must not.
        foreach (string path in Directory.GetFiles("Assets/Scripts/GClient/Runtime/Gameplay", "*.cs", SearchOption.AllDirectories))
            foreach (string line in File.ReadAllLines(path))
                if (!line.Contains("APIUpdating.MovedFrom"))
                    Check(!line.Contains("OperationBlacktide.Client.Features.") && !line.Contains("TrainingTarget") && !line.Contains("TrainingSceneController"),
                        "Gameplay depends on a feature module: " + path);
        foreach (string path in Directory.GetFiles("Assets/Scripts/GShare/Runtime/Combat", "*.cs"))
            Check(!File.ReadAllText(path).Contains("UnityEngine"), "Shared simulation depends on Unity: " + path);

        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Training/TrainingPlayer.prefab");
        Check(player != null && player.GetComponent<CharacterMotor>() != null, "Player script GUID did not survive migration.");
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/UI/Panels/TrainingPanel.prefab");
        var panel = hud != null ? hud.GetComponent<TrainingPanel>() : null;
        Check(panel is CombatHudPanel, "Training HUD does not use the common HUD.");
        var serialized = new SerializedObject(panel);
        foreach (string field in new[] { "returnButton", "safeArea", "healthText", "armorText", "magazineText", "reloadFill", "crosshair" })
            Check(serialized.FindProperty(field)?.objectReferenceValue != null, "Inherited HUD binding was lost: " + field);
        var weapons = Resources.Load<WeaponCatalog>("Gameplay/Weapons");
        Check(weapons != null && weapons.weapons.Length >= 35 && weapons.Find("weapon_rif_m4a1_silencer").fireAnimation != null,
            "Weapon catalog serialization was lost.");
        Check(Resources.Load<ThrowableCatalog>("Gameplay/Throwables")?.items.Length == 5,
            "Throwable catalog serialization was lost.");
        Check(Resources.Load<Shader>("Gameplay/CharacterVision") != null, "Vision shader resource is missing.");
        foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData", new[] { "Assets/Settings" }))
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
            Check(data.rendererFeatures.FindAll(feature => feature is CharacterVisionRendererFeature).Count == 1,
                "Vision renderer binding was lost: " + data.name);
        }
        foreach (string path in new[] { "Assets/Scenes/SampleScene.unity", "Assets/Scenes/LobbyScene.unity", "Assets/Scenes/DustII.unity" })
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects())
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                        "Missing scene script: " + path + "/" + child.name);
        }
        var finite = new AmmoState(3, 2, 1, false);
        for (int i = 0; i < 3; i++) Check(finite.TryConsume(), "Finite magazine rejected a shot.");
        Check(!finite.TryConsume() && finite.TryReload(), "Empty magazine gate failed.");
        finite.Tick(1);
        Check(finite.Magazine == 2 && finite.Reserve == 0, "Finite reload ignored reserve.");
        var trigger = new FireControl(.1f, true);
        trigger.Tick(false, false, true, 0);
        Check(trigger.Tick(true, true, true, 0) == 1 && trigger.Tick(true, false, true, .01) == 0,
            "Shared cadence changed.");
        Debug.Log("GAMEPLAY_STRUCTURE_PASS: mode boundaries, original prefabs, catalogs, scenes, renderer features, shared simulation.");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
