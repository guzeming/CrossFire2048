using System;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Training;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyThrowables
{
    public static void BuildAndRun()
    {
        BuildTrainingThrowables.Build();
        BuildTrainingThrowables.Build();
        var catalog = Resources.Load<TrainingThrowableCatalog>("Training/TrainingThrowables");
        if (catalog.items.Length != 5 || catalog.fire == null || catalog.smoke == null || catalog.ringing == null)
            throw new Exception("Missing throwable assets");
        foreach (var item in catalog.items)
            if (item.model == null || item.pin == null || item.release == null || item.bounce == null || item.detonate.Length == 0)
                throw new Exception("Missing model/audio: " + item.id);
        ThrowableActionVerification.CheckBindings(catalog);
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Throwable Verification").AddComponent<ThrowableVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
