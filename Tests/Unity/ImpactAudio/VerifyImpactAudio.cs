using System;
using System.Linq;
using OperationBlacktide.Client.Gameplay.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyImpactAudio
{
    public static void Run()
    {
        int count = 0;
        foreach (ImpactKind kind in Enum.GetValues(typeof(ImpactKind)))
        {
            var clips = Resources.LoadAll<AudioClip>("Combat/ImpactAudio/" + kind);
            Check(clips.Length > 0, "Missing impact group " + kind);
            foreach (var clip in clips)
            {
                Check(clip.length > .01f && clip.channels > 0, "Invalid imported audio " + clip.name);
                clip.LoadAudioData();
                var samples = new float[clip.samples * clip.channels];
                Check(clip.GetData(samples, 0) && samples.Any(s => Mathf.Abs(s) > .001f), "Silent/undecodable clip " + clip.name);
                count++;
            }
        }
        Check(count == 92, "Unexpected clip count " + count);
        CheckMaterial("D2_0047_hr_dust_metal_trim_01", ImpactKind.Metal);
        CheckMaterial("D2_0168_dust_door_46x106_01", ImpactKind.WoodPanel);
        Debug.Log("IMPACT_ASSETS_PASS: 92 non-silent decoded WAVs, 21 groups, source material mappings.");
        EditorSceneManager.OpenScene("Assets/Scenes/DustII.unity");
        new GameObject("Impact Audio Verification").AddComponent<ImpactAudioVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }

    private static void CheckMaterial(string name, ImpactKind expected)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Maps/DustII/Materials/" + name + ".mat");
        Check(material != null && ImpactSurfaceResolver.ResolveMaterial(material) == expected, "Source surface mismatch " + name);
        var faded = new Material(material) { name = material.name + " (Occluded Building)" };
        Check(ImpactSurfaceResolver.ResolveMaterial(faded) == expected, "Camera fade changed acoustic material");
        UnityEngine.Object.DestroyImmediate(faded);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
