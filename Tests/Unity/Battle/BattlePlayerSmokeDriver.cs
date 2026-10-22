using OperationBlacktide.Client.Features.Lobby;
#if BLACKTIDE_PLAYER_SMOKE
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using OperationBlacktide.Client.App;
using OperationBlacktide.Client.Features.Account;
using OperationBlacktide.Client.Features.Battle;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Client.UI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Protocol;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Compiled only into a disposable standalone validation build, never the shipping client.
public sealed class BattlePlayerSmokeDriver : MonoBehaviour
{
    private static bool created;
    private string output, role;
    private int port;
    private bool finished;
    private float deadline;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (created) return;
        created = true;
        var go = new GameObject("Standalone battle regression");
        DontDestroyOnLoad(go); go.AddComponent<BattlePlayerSmokeDriver>();
    }
    private void Awake()
    {
        string[] args = Environment.GetCommandLineArgs();
        output = args[Array.IndexOf(args, "--test-output") + 1];
        role = args[Array.IndexOf(args, "--test-role") + 1];
        port = int.Parse(File.ReadAllText(Path.Combine(output, "account-port.txt")));
        deadline = Time.realtimeSinceStartup + 110;
        Application.logMessageReceived += OnLog;
        Application.runInBackground = true;
    }
    private void Update() { if (!finished && Time.realtimeSinceStartup > deadline) Fail("Full flow timeout"); }
    private void OnLog(string message, string trace, LogType kind)
    {
        if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) Fail(message + "\n" + trace);
    }
    private void Fail(string message)
    {
        if (finished) return;
        finished = true;
        File.WriteAllText(Path.Combine(output, role + "-failure.txt"), message);
        LoginCredentialStore.Forget("127.0.0.1", port);
        Application.Quit(1);
    }
    private IEnumerator Start()
    {
        yield return null;
        var flow = GameSceneFlow.Instance;
        Check(flow != null && SceneManager.GetActiveScene().path == GameSceneFlow.LoginScenePath, "real login scene");
        var tcp = flow.GetComponent<TcpGameClient>();
        Field(tcp, "appConfig").SetValue(tcp, null);
        Field(tcp, "serverPort").SetValue(tcp, port);
        bool registered = false;
        flow.Auth.RegisterCompleted += response => { Check(response.Code == AuthResultCode.Ok, "registration"); registered = true; };
        var request = flow.Auth.RegisterAsync("PlayerSmoke" + role, "SmokeOnly_382");
        yield return new WaitUntil(() => registered || request.IsFaulted);
        Check(!request.IsFaulted, "register request");
        request = flow.Auth.LoginAsync("PlayerSmoke" + role, "SmokeOnly_382");
        yield return new WaitUntil(() => SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath && !flow.IsLoading);
        yield return null;
        Check(flow.Auth.Session.IsLoggedIn && FindObjectOfType<LobbyPanel>()?.IsOpen == true, "login opens lobby");
        for (int round = 0; round < 2; round++)
        {
            var panel = FindObjectOfType<LobbyPanel>();
            typeof(LobbyPanel).GetMethod("OnModeClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, new object[] { 1 });
            ((Button)Field(panel, "enterGameButton").GetValue(panel)).onClick.Invoke();
            yield return new WaitUntil(() => SceneManager.GetActiveScene().path == GameSceneFlow.BattleScenePath && !flow.IsLoading);
            yield return null;
            var arena = FindObjectOfType<BattleArenaClient>();
            arena.InputProvider = () => default;
            yield return new WaitUntil(() => arena.Ready && arena.PlayerCount == 2);
            Check(UIManager.Instance.GetStackCount(UILayer.Normal) == 0 && UIManager.Instance.GetStackCount(UILayer.Popup) == 0
                && FindObjectOfType<LobbyPanel>() == null, "lobby panels and blockers closed");
            Check(arena.ShotPresentationReady, "standalone tracer shader included");
            if (round == 0)
            {
                int snapshots = arena.SnapshotCount;
                arena.InputProvider = () => new BattleInput { Yaw = 90, Buttons = InputButtons.Fire };
                yield return new WaitForSecondsRealtime(1.2f);
                arena.InputProvider = () => default;
                yield return new WaitForSecondsRealtime(.3f);
                Check(arena.Ready && arena.ShotCount > 0 && arena.AuthoritativeState.Ammo < 30 && arena.SnapshotCount > snapshots,
                    "standalone sustained fire stays connected");
                ScreenCapture.CaptureScreenshot(Path.Combine(output, role + "-battle.png"));
                File.WriteAllText(Path.Combine(output, role + "-fired.txt"), "PASS");
                yield return new WaitUntil(() => File.Exists(Path.Combine(output, (role == "A" ? "B" : "A") + "-fired.txt")));
                yield return new WaitForSecondsRealtime(.4f);
            }
            else
            {
                File.WriteAllText(Path.Combine(output, role + "-rejoined.txt"), "PASS");
                // Runner terminates its own isolated server once both clients rejoin.
                yield return new WaitUntil(() => !arena.Ready);
                Check(UIManager.Instance.GetStackCount(UILayer.Normal) == 0, "disconnect does not restore stale lobby overlay");
            }
            UIRoot.Instance.HandleBackInput(); // Same path used by the Esc key.
            yield return new WaitUntil(() => SceneManager.GetActiveScene().path == GameSceneFlow.LobbyScenePath && !flow.IsLoading);
            yield return null;
            Check(FindObjectOfType<LobbyPanel>()?.IsOpen == true && UIManager.Instance.GetStackCount(UILayer.Normal) == 1,
                "Esc returns to one working lobby");
            Check(FindObjectOfType<BattleArenaClient>() == null && GameObject.Find("Battle HUD") == null,
                "battle objects and HUD cleaned up");
        }
        LoginCredentialStore.Forget("127.0.0.1", port);
        File.WriteAllText(Path.Combine(output, role + "-result.txt"), "BATTLE_PLAYER_SMOKE_PASS");
        Debug.Log("BATTLE_PLAYER_SMOKE_PASS " + role);
        finished = true; Application.Quit(0);
    }
    private static FieldInfo Field(object instance, string name) => instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    private void Check(bool condition, string text)
    {
        if (!condition) { Fail(text); throw new InvalidOperationException(text); }
        Debug.Log("PASS " + role + " " + text);
    }
    private void OnDestroy() { Application.logMessageReceived -= OnLog; }
}
#endif
