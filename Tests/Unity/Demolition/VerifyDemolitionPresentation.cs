#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Content;
using OperationBlacktide.Client.Editor;
using OperationBlacktide.Client.Features.Demolition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class VerifyDemolitionPresentation
{
    public static void Run()
    {
        BuildDemolitionPresentation.Build();
        var data = Resources.Load<DemolitionPresentationAssets>("Gameplay/Demolition/Presentation");
        var catalog = Resources.Load<EquipmentCatalog>("Loadout/EquipmentCatalog");
        foreach (string id in new[] { "tm_phoenix", "ctm_sas" })
        {
            var root = UnityEngine.Object.Instantiate(catalog.Agent(id).prefab);
            var animator = root.GetComponentInChildren<Animator>(); animator.enabled = false;
            foreach (var clip in new[] { data.plant, data.defuseEnter, data.defuseLoop })
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    Check(binding.path.Length > 0 && animator.transform.Find(binding.path) != null, "Unbound C4 curve: " + id + "/" + binding.path);
                var hand = animator.GetComponentsInChildren<Transform>().Single(t => t.name == "hand_L");
                clip.SampleAnimation(animator.gameObject, 0); var start = hand.position; float motion = 0;
                for (int i = 1; i <= 12; i++)
                { clip.SampleAnimation(animator.gameObject, clip.length * i / 12); motion = Mathf.Max(motion, Vector3.Distance(start, hand.position)); }
                Check(motion > .005f, "C4 clip has no hand motion: " + id + "/" + clip.name);
            }
            UnityEngine.Object.DestroyImmediate(root);
        }
        foreach (var sound in new[] { data.draw, data.initiate, data.click, data.planted, data.defuseStart, data.defused, data.beep, data.urgentBeep, data.explode })
            Check(sound != null && sound.length > .01f, "Missing C4 sound");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("C4 verification").AddComponent<DemolitionPresentationVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
#endif
