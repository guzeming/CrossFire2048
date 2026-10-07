using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class VerifyCameraCutaway
{
    public static void Run()
    {
        ShaderUtil.allowAsyncCompilation = false;
        // Runtime material swaps need their source keyword combinations retained in player builds.
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        const string path = "Assets/Resources/Training/TrainingCutawayVariants.shadervariants";
        var variants = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(path);
        if (variants == null)
        {
            variants = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(variants, path);
        }
        variants.Clear();
        var sets = new HashSet<string> { "_SURFACE_TYPE_TRANSPARENT", "_EMISSION _SURFACE_TYPE_TRANSPARENT" };
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Art/Maps/DustII" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            sets.Add(string.Join(" ", material.shaderKeywords.Where(k => k != "_ALPHAPREMULTIPLY_ON" && k != "_ALPHAMODULATE_ON").Concat(new[] { "_SURFACE_TYPE_TRANSPARENT" }).Distinct().OrderBy(k => k)));
        }
        foreach (string set in sets)
            variants.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline,
                set.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries)));
        EditorUtility.SetDirty(variants);
        AssetDatabase.SaveAssets();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/DustII.unity", true) };
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Camera Cutaway Verification").AddComponent<CameraCutawayVerificationDriver>();
        EditorApplication.EnterPlaymode();
    }
}

