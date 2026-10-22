using System;
using System.Linq;
using OperationBlacktide.Client.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyFootsteps
{
    [Serializable] private sealed class Bank { public string kind; public string[] steps, landings; }
    [Serializable] private sealed class Banks { public Bank[] banks; }
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
        var banks = JsonUtility.FromJson<Banks>(Resources.Load<TextAsset>("Combat/FootstepAudio/banks").text).banks;
        if (banks.Length != 15) throw new Exception("Missing surface footstep banks");
        int count = 0;
        foreach (string path in banks.SelectMany(b => b.steps.Concat(b.landings)).Distinct())
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null || clip.length < .01f) throw new Exception("Invalid material footstep: " + path);
            clip.LoadAudioData();
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0) || !samples.Any(s => Mathf.Abs(s) > .001f))
                throw new Exception("Silent/undecodable footstep: " + path);
            count++;
        }
        if (count != 222) throw new Exception("Unexpected footstep clip count: " + count);
        Debug.Log("FOOTSTEP_ASSETS_PASS: 15 materials, 222 decoded non-silent clips");
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Footstep Verification").AddComponent<FootstepVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}
