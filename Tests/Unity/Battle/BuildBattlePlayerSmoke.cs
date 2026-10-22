#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using OperationBlacktide.EditorTools;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildBattlePlayerSmoke
{
    public static void Run()
    {
        string[] args = Environment.GetCommandLineArgs();
        string output = args[Array.IndexOf(args, "--test-output") + 1];
        BuildBattleArena.Prepare();
        var previous = PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone);
        try
        {
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = Path.Combine(output, "Player/Blacktide.Smoke.exe"),
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development,
                extraScriptingDefines = new[] { "BLACKTIDE_PLAYER_SMOKE" }
            });
            if (result.summary.result != BuildResult.Succeeded) throw new Exception("Smoke player build failed");
            Debug.Log("BATTLE_PLAYER_BUILD_PASS");
        }
        finally { PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, previous); }
    }
}
#endif
