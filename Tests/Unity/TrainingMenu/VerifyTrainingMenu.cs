using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Gameplay.Combat;
using System;
using System.Linq;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Lobby;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTrainingMenu
{
    public static void Run()
    {
        var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        var three = catalog.weapons.Where(w => WeaponSelection.SlotOf(w) == 4).Take(3).ToArray();
        var inventory = new WeaponSelection(three, null);
        for (int i = 0; i < 7; i++)
        {
            string next = inventory.Next(4);
            Check(next == three[i % 3].id && inventory.Select(next), "Three-grenade wraparound failed.");
        }
        string previous = inventory.CurrentId;
        Check(inventory.Next(1) == null && inventory.Next(0) == null && inventory.Next(5) == null &&
            !inventory.Select("missing") && inventory.CurrentId == previous, "Invalid/empty category changed inventory.");
        var single = new WeaponSelection(three.Take(1), three[0].id);
        Check(single.Next(4) == three[0].id, "Single-item category failed.");
        Debug.Log("TRAINING_MENU_SELECTION_PASS");
        BuildTrainingHud.Build();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Training Menu Verification").AddComponent<TrainingMenuVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
