#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using OperationBlacktide.Client.Features.Battle;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Protocol;
using UnityEditor;
using UnityEngine;

public sealed class BotClientVerificationDriver : MonoBehaviour
{
    private string output;
    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs(); output = args[Array.IndexOf(args, "--test-output") + 1];
        Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText(Path.Combine(output, "client-failure.txt"), message + "\n" + trace); EditorApplication.Exit(1);
    }
    private IEnumerator Start()
    {
        yield return null;
        var arena = FindObjectOfType<BattleArenaClient>();
        arena.InputProvider = () => default;
        arena.Connect(new BattleJoinResponse { Success = true, Host = "127.0.0.1", Port = int.Parse(File.ReadAllText(Path.Combine(output, "port.txt"))),
            Ticket = BattleTicket.Issue(File.ReadAllBytes(Path.Combine(output, "test.key")), "bot-visual-human", "Bot visual human") });
        float end = Time.realtimeSinceStartup + 15;
        while (!arena.Ready || arena.PlayerCount != 8)
        { if (Time.realtimeSinceStartup > end) throw new Exception("Bot client connection timeout"); yield return null; }
        if (arena.BotCount != 7) throw new Exception("Client bot count is incorrect");
        yield return new WaitForSecondsRealtime(2);
        var actors = FindObjectsOfType<Transform>(true).Where(t => t.name.StartsWith("SelectedAgent_")).ToArray();
        if (!actors.Any(t => t.name.EndsWith("tm_phoenix")) || !actors.Any(t => t.name.EndsWith("ctm_sas")))
            throw new Exception("Client does not show both team models");
        if (arena.ShotCount == 0 || !arena.ShotPresentationReady) throw new Exception("Bot shot presentation missing");
        var camera = Camera.main;
        var rt = new RenderTexture(1280, 720, 24); var previous = RenderTexture.active;
        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
        File.WriteAllBytes(Path.Combine(output, "bots-client.png"), image.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = previous; rt.Release(); Destroy(rt); Destroy(image);
        File.WriteAllText(Path.Combine(output, "client-result.txt"), "BATTLE_BOTS_CLIENT_PASS");
        Debug.Log("BATTLE_BOTS_CLIENT_PASS"); EditorApplication.Exit(0);
    }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
#endif
