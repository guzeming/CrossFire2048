#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Net.Sockets;
using System.Text;
using OperationBlacktide.Client.Features.Battle;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Protocol;
using MessageType = OperationBlacktide.Shared.Protocol.MessageType;
using UnityEditor;
using UnityEngine;

public sealed class BattleClientVerificationDriver : MonoBehaviour
{
    private BattleConnection opponent;
    private BattleState opponentState;
    private TcpClient accountA, accountB;
    private string output;
    private uint sequence;
    private float pending;
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
        yield return null; yield return null;
        int port = int.Parse(File.ReadAllText(Path.Combine(output, "account-port.txt")));
        BattleJoinResponse a = Login(port, "VisualAlice", out accountA), b = Login(port, "VisualBob", out accountB);
        var arena = FindObjectOfType<BattleArenaClient>();
        arena.InputProvider = () => default;
        arena.Connect(a);
        yield return new WaitForSecondsRealtime(.4f);
        opponent = new BattleConnection();
        opponent.Snapshot += (_, states) => { foreach (var state in states) if (state.Id == opponent.PlayerId) opponentState = state; };
        opponent.Connect(b.Host, b.Port, b.Ticket);
        yield return new WaitForSecondsRealtime(2);
        Check(arena.Ready && opponent.Ready && arena.PlayerCount == 2 && arena.SnapshotCount > 10, "Two players replicated in Dust II");
        Check(FindObjectOfType<OperationBlacktide.Simulation.ArenaMap>().SpawnCount == 8, "Dust II has eight validated spawns");
        Capture("dust2-two-players.png");
        float start = arena.LocalState.Z;
        arena.InputProvider = () => new BattleInput { Z = -1 };
        yield return new WaitForSecondsRealtime(1);
        arena.InputProvider = () => default;
        yield return new WaitForSecondsRealtime(.5f);
        Check(start - arena.LocalState.Z > 3 && start - arena.LocalState.Z < 5.5f, "Client prediction moves and reconciles");
        Check(Vector3.Distance(new Vector3(arena.AuthoritativeState.X, arena.AuthoritativeState.Y, arena.AuthoritativeState.Z),
            new Vector3(arena.LocalState.X, arena.LocalState.Y, arena.LocalState.Z)) < .15f, "Prediction settles to authoritative position");
        float yaw = Mathf.Atan2(opponentState.X - arena.LocalState.X, opponentState.Z - arena.LocalState.Z) * Mathf.Rad2Deg;
        arena.InputProvider = () => new BattleInput { Yaw = yaw, Buttons = InputButtons.Fire };
        yield return new WaitForSecondsRealtime(.6f);
        arena.InputProvider = () => new BattleInput { Yaw = yaw };
        yield return new WaitForSecondsRealtime(.3f);
        Check(arena.ShotCount > 0 && arena.AuthoritativeState.Ammo < 30 && opponentState.Health == 0, "Unity shots/damage are server-confirmed");
        arena.InputProvider = () => new BattleInput { Yaw = yaw, Buttons = InputButtons.Reload };
        yield return new WaitForSecondsRealtime(.15f);
        arena.InputProvider = () => new BattleInput { Yaw = yaw };
        yield return new WaitForSecondsRealtime(2.8f);
        Check(arena.AuthoritativeState.Ammo == 30 && arena.AuthoritativeState.Reserve < 90, "Unity HUD state follows server reload");
        Capture("battle-client.png");
        arena.InputProvider = () => new BattleInput { X = 1 };
        yield return new WaitForSecondsRealtime(2);
        arena.InputProvider = () => default;
        yield return new WaitForSecondsRealtime(.5f);
        Check(arena.LocalState.X > -9 && arena.LocalState.X < -7.5f, "Dust II wall blocks predicted movement");
        Check(Mathf.Abs(arena.LocalState.X - arena.AuthoritativeState.X) < .15f, "Wall collision agrees with server");
        Capture("dust2-wall-collision.png");
        File.WriteAllText(Path.Combine(output, "client-result.txt"), "BATTLE_CLIENT_VERIFY_PASS");
        Debug.Log("BATTLE_CLIENT_VERIFY_PASS"); EditorApplication.Exit(0);
    }
    private void Capture(string name)
    {
        var camera = Camera.main;
        var texture = new RenderTexture(1280, 720, 24); camera.targetTexture = texture; camera.Render();
        RenderTexture.active = texture;
        var pixels = new Texture2D(1280, 720, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.Combine(output, name), pixels.EncodeToPNG());
        camera.targetTexture = null; RenderTexture.active = null; Destroy(texture); Destroy(pixels);
    }
    private void Update()
    {
        opponent?.Poll();
        if (opponent?.Ready != true) return;
        pending += Time.unscaledDeltaTime;
        while (pending >= BattleProtocol.Step) { pending -= BattleProtocol.Step; opponent.Send(new[] { new BattleInput { Sequence = ++sequence, Yaw = 180 } }); }
    }
    private static BattleJoinResponse Login(int port, string name, out TcpClient client)
    {
        client = new TcpClient("127.0.0.1", port) { ReceiveTimeout = 5000, SendTimeout = 5000 };
        var writer = new StreamWriter(client.GetStream(), new UTF8Encoding(false)) { AutoFlush = true };
        var reader = new StreamReader(client.GetStream());
        T Request<T>(MessageType type, object value)
        {
            writer.WriteLine(NetworkMessageCodec.Encode(type, value));
            return NetworkMessageCodec.DecodePayload<T>(NetworkMessageCodec.DecodeEnvelope(reader.ReadLine()));
        }
        Request<RegisterResponse>(MessageType.RegisterRequest, new RegisterRequest { Username = name, Password = "VisualTest_987" });
        Check(Request<LoginResponse>(MessageType.LoginRequest, new LoginRequest { Username = name, Password = "VisualTest_987" }).Code == AuthResultCode.Ok, "visual account login");
        var ticket = Request<BattleJoinResponse>(MessageType.BattleJoinRequest, new BattleJoinRequest()); Check(ticket.Success, "visual account ticket"); return ticket;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("PASS " + message); }
    private void OnDestroy() { opponent?.Dispose(); accountA?.Dispose(); accountB?.Dispose(); Application.logMessageReceived -= OnLog; }
}
#endif
