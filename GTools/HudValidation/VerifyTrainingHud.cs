using System;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyTrainingHud
{
    public static void BuildAndRun()
    {
        BuildTrainingWeapons.Build();
        BuildTrainingHud.Build();
        CheckAmmo();
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("HUD Verification").AddComponent<HudVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void CheckAmmo()
    {
        var finite = new TrainingAmmoState(20, 7, 2, false);
        Check(!finite.TryReload(), "Full magazine reloaded.");
        for (int i = 0; i < 20; i++) Check(finite.TryConsume(), "Magazine lost a round.");
        Check(!finite.TryConsume() && finite.Magazine == 0, "Empty magazine fired.");
        Check(finite.TryReload() && !finite.TryReload() && !finite.TryConsume(), "Reload allows fire / duplicate reload.");
        finite.Tick(1); Check(Math.Abs(finite.ReloadProgress - .5f) < .001f && finite.Magazine == 0, "Early reload refill.");
        finite.Tick(0); finite.Tick(-10); Check(finite.ReloadProgress == .5f, "Paused reload advanced.");
        finite.Tick(1); Check(finite.Magazine == 7 && finite.Reserve == 0 && !finite.IsReloading, "Partial reserve transfer failed.");
        Check(!finite.TryReload(), "Empty reserve reloaded.");
        finite.Reset(); Check(finite.Magazine == 20 && finite.Reserve == 7, "Reset failed.");
        var infinite = new TrainingAmmoState(30, 90, 2.5f, true);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            for (int i = 0; i < 30; i++) infinite.TryConsume();
            Check(infinite.Magazine == 0 && infinite.TryReload(), "Unlimited reserve never empties magazine.");
            infinite.Tick(20);
            Check(infinite.Magazine == 30 && infinite.Reserve == 90, "Unlimited reserve depleted.");
        }
        Debug.Log("HUD_AMMO_STATE_PASS");
    }

    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
