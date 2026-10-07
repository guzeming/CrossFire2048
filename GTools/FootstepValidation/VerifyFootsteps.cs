using System;
using OperationBlacktide.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyFootsteps
{
    public static void Run()
    {
        // Generate the current HUD in the isolated copy so its serialized references match the UI scripts.
        BuildTrainingHud.Build();
        foreach (string folder in new[] { "Footsteps", "Landings" })
        {
            var clips = Resources.LoadAll<AudioClip>("Training/Audio/" + folder);
            if (clips.Length < 4) throw new Exception("Missing audio variants: " + folder);
            foreach (var clip in clips)
                if (clip.length < .05f || clip.channels < 1) throw new Exception("Invalid sample: " + clip.name);
        }
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Footstep Verification").AddComponent<FootstepVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
