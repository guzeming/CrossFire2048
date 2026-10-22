#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.App;
using OperationBlacktide.EditorTools;
using OperationBlacktide.Simulation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildDemolitionPerformance
{
    public static void Run()
    {
        var args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "--test-output") + 1];
        // Use the checked-in playable map and its baked navigation; don't regenerate gameplay assets for a benchmark.
        foreach (string path in new[] { GameSceneFlow.BattleScenePath, BuildBattleArena.ServerScene })
        {
            EditorSceneManager.OpenScene(path);
            UnityEngine.Object.FindObjectOfType<DemolitionMap>().Validate();
        }
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/PerformanceBootstrap.unity");
        var backend = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        bool timing = PlayerSettings.enableFrameTimingStats;
        var pipeline = GraphicsSettings.defaultRenderPipeline;
        var qualityPipelines = Enumerable.Range(0, QualitySettings.names.Length).Select(QualitySettings.GetRenderPipelineAssetAt).ToArray();
        int quality = QualitySettings.GetQualityLevel();
        try
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.enableFrameTimingStats = true;
            Debug.Log("LAUNCHER_PROGRESS 55 正在编译并打包：性能测试客户端");
            Build(new[] { "Assets/PerformanceBootstrap.unity", GameSceneFlow.BattleScenePath }, Path.Combine(output, "Client/Blacktide.Performance.exe"), false);
            GraphicsSettings.defaultRenderPipeline = null;
            for (int i = 0; i < qualityPipelines.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = null; }
            QualitySettings.SetQualityLevel(quality, false);
            Debug.Log("LAUNCHER_PROGRESS 78 正在编译并打包：性能测试战斗服");
            Build(new[] { BuildBattleArena.ServerScene }, Path.Combine(output, "Server/Blacktide.PerformanceServer.exe"), true);
            File.WriteAllText(Path.Combine(output, "build-result.txt"), "DEMOLITION_PERFORMANCE_BUILD_PASS");
            Debug.Log("DEMOLITION_PERFORMANCE_BUILD_PASS");
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);
            PlayerSettings.enableFrameTimingStats = timing;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int i = 0; i < qualityPipelines.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = qualityPipelines[i]; }
            QualitySettings.SetQualityLevel(quality, false);
        }
    }
    static void Build(string[] scenes, string path, bool server)
    {
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = scenes, locationPathName = path, target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development, extraScriptingDefines = server ?
                new[] { "BLACKTIDE_PERFORMANCE", "BLACKTIDE_SERVER" } : new[] { "BLACKTIDE_PERFORMANCE" }
        });
        if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Performance build failed: " + path);
    }
}
#endif
