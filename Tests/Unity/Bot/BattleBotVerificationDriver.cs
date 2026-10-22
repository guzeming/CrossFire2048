#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using OperationBlacktide.BattleServer;
using OperationBlacktide.Client.Network;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public sealed class BattleBotVerificationDriver : MonoBehaviour
{
    private sealed class Client : IDisposable
    {
        public readonly BattleConnection Connection = new BattleConnection();
        public Dictionary<int, BattleState> States = new Dictionary<int, BattleState>();
        public uint Sequence;
        public void Dispose() => Connection.Dispose();
    }
    private readonly List<Client> clients = new List<Client>();
    private readonly Dictionary<int, Vector3> firstPositions = new Dictionary<int, Vector3>();
    private BattleServerHost host;
    private string output;
    private int botShots, botHits;
    private bool botMoved, botRespawned;
    private float accumulator;

    private void Awake()
    {
        output = BattleServerHost.Argument(Environment.GetCommandLineArgs(), "--test-output", ".");
        Application.logMessageReceived += OnLog;
    }
    private void OnLog(string message, string trace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        File.WriteAllText(Path.Combine(output, "bot-failure.txt"), message + "\n" + trace); EditorApplication.Exit(1);
    }
    private IEnumerator Start()
    {
        yield return null;
        var map = ArenaMap.Initialize();
        VerifyBrain(map);
        var udp = new UdpClient(0); int port = ((System.Net.IPEndPoint)udp.Client.LocalEndPoint).Port; udp.Dispose();
        byte[] key = new byte[32]; new System.Security.Cryptography.RNGCryptoServiceProvider().GetBytes(key);
        string keyFile = Path.Combine(output, "test.key"); File.WriteAllBytes(keyFile, key);
        host = new GameObject("Bot test server").AddComponent<BattleServerHost>();
        host.StartServer(port, keyFile, Path.Combine(output, "logs"), 7);
        Check(host.BotCount == 0, "Empty room does not simulate bots");
        Client a = Connect(key, port, "Human A");
        yield return Until(() => a.Connection.Ready && a.States.Count == 8, "First player plus seven bots");
        Check(host.HumanCount == 1 && host.BotCount == 7 && a.States.Values.Count(s => s.IsBot) == 7, "Bot metadata replicated");
        Check(a.States.Values.Count(s => s.Team == BattleTeam.CT) == 4 && a.States.Values.Count(s => s.Team == BattleTeam.T) == 4,
            "Server allocates four CT and four T slots");
        yield return Until(() => botShots > 0 && botHits > 0 && botMoved && botRespawned, "Bots move, fire, damage and respawn", 20);
        Check(a.States.Values.All(s => s.Health >= 0 && s.Health <= 100 && s.Ammo >= 0), "Snapshot health and ammunition bounds");
        Client b = Connect(key, port, "Human B");
        yield return Until(() => b.Connection.Ready && host.HumanCount == 2 && host.BotCount == 6 &&
            a.States.Values.Count(s => !s.IsBot) == 2, "Second human replaces a bot");
        Check(b.States[b.Connection.PlayerId].Team == BattleTeam.T, "Replacement preserves slot team");
        for (int i = 2; i < 8; i++) Connect(key, port, "Human " + i);
        yield return Until(() => host.HumanCount == 8 && host.BotCount == 0 && clients.All(c => c.Connection.Ready),
            "All bot slots can be reclaimed by humans");
        b.Dispose(); clients.Remove(b);
        yield return Until(() => host.HumanCount == 7 && host.BotCount == 1, "Leaving human restores one bot");
        foreach (var client in clients) client.Dispose(); clients.Clear();
        yield return Until(() => host.HumanCount == 0 && host.BotCount == 0, "Empty-room bot cleanup");
        Destroy(host.gameObject); yield return null;
        File.WriteAllText(Path.Combine(output, "bot-result.txt"), "BATTLE_BOTS_VERIFY_PASS shots=" + botShots + " hits=" + botHits);
        Debug.Log("BATTLE_BOTS_VERIFY_PASS"); EditorApplication.Exit(0);
    }

    private static void VerifyBrain(ArenaMap map)
    {
        Vector3 spawn = map.Spawn(0), enemy = map.Spawn(1);
        var self = new BattleState { Id = 101, Team = BattleTeam.CT, Health = 100, Ammo = 30, Reserve = 90,
            X = spawn.x, Y = spawn.y, Z = spawn.z, Yaw = 0 };
        var other = new BattleState { Id = 102, Team = BattleTeam.T, Health = 100, X = enemy.x, Y = enemy.y, Z = enemy.z };
        var points = Enumerable.Range(0, map.SpawnCount).Select(map.Spawn).ToArray();
        var brain = new BattleBotBrain(self.Id, points); brain.Reset(spawn, 0);
        var command = brain.Think(self, new[] { self, other }, 0);
        Check(brain.Mode == BattleBotMode.Engage && !command.Has(InputButtons.Fire), "Sight acquisition includes reaction delay");
        Check(!BattleBotBrain.CanSee(self, spawn + Vector3.back * 6), "Outside-FOV enemy is unseen");
        Check(!BattleBotBrain.CanSee(self, spawn + Vector3.forward * 40), "Outside-range enemy is unseen");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = (spawn + enemy) * .5f + Vector3.up * 1.4f;
        wall.transform.localScale = new Vector3(3, 3, .4f); Physics.SyncTransforms();
        Check(!BattleBotBrain.CanSee(self, enemy), "Opaque wall blocks sight");
        other.Z += 3;
        brain.Think(self, new[] { self, other }, 12);
        Check(brain.Mode == BattleBotMode.Search && brain.LastKnownPosition == enemy, "Occluded target cannot update remembered position");
        brain.Think(self, new[] { self, other }, 200);
        Check(brain.Mode == BattleBotMode.Patrol && brain.TargetId == 0, "Target memory expires");
        DestroyImmediate(wall); Physics.SyncTransforms();
        other.Z = enemy.z;
        var ally = self; ally.Id = 103; ally.Z = (spawn.z + enemy.z) * .5f;
        brain.Reset(spawn, 0); brain.Think(self, new[] { self, other, ally }, 0);
        command = brain.Think(self, new[] { self, other, ally }, 60);
        Check(!command.Has(InputButtons.Fire) && command.X * command.X + command.Z * command.Z > 0,
            "Bot yields a teammate's firing lane");
        other.Team = BattleTeam.CT;
        brain.Reset(spawn, 210); brain.Think(self, new[] { self, other }, 210);
        Check(brain.TargetId == 0, "Teammate is never acquired as enemy");
        self.Ammo = 0;
        Check(brain.Think(self, new[] { self }, 220).Has(InputButtons.Reload), "Empty magazine requests normal reload");
        self.ReloadTicks = 10;
        Check(!brain.Think(self, new[] { self }, 221).Has(InputButtons.Fire), "Reload blocks firing");
        self.Health = 0;
        Check(brain.Think(self, new[] { self }, 222).Buttons == InputButtons.None && brain.Mode == BattleBotMode.Dead,
            "Dead bot cannot act");
        self.Health = 100; self.Ammo = 30; self.ReloadTicks = 0;
        var motor = CharacterSimulation.CreatePawn(self.Id); CharacterSimulation.Restore(motor, self);
        brain.Reset(spawn, 230);
        float travelled = 0;
        for (int tick = 230; tick < 590; tick++)
        {
            Vector3 before = motor.transform.position;
            CharacterSimulation.Step(motor, ref self, brain.Think(self, new[] { self }, tick));
            travelled += Vector3.ProjectOnPlane(motor.transform.position - before, Vector3.up).magnitude;
        }
        Check(travelled > 5 && self.Y > map.KillHeight && NavMesh.SamplePosition(motor.transform.position, out _, 1, NavMesh.AllAreas),
            "NavMesh commands move the real authoritative motor on Dust II: " + travelled);
        DestroyImmediate(motor.gameObject);
        Debug.Log("BOT_BRAIN_VERIFY_PASS");
    }

    private Client Connect(byte[] key, int port, string name)
    {
        var client = new Client();
        client.Connection.Snapshot += (_, states) =>
        {
            client.States = states.ToDictionary(s => s.Id);
            foreach (var state in states.Where(s => s.IsBot))
            {
                if (!firstPositions.TryGetValue(state.Id, out var first)) firstPositions[state.Id] = CharacterSimulation.Position(state);
                else if (state.Life == 1 && Vector3.Distance(first, CharacterSimulation.Position(state)) > .5f) botMoved = true;
                if (state.Life > 1) botRespawned = true;
            }
        };
        client.Connection.Shot += (shooter, victim, _) =>
        {
            if (!client.States.TryGetValue(shooter, out var from) || !from.IsBot) return;
            botShots++;
            if (victim != 0 && client.States.TryGetValue(victim, out var to))
            { Check(from.Team != to.Team, "Bot damage only hits enemy team"); botHits++; }
        };
        client.Connection.Connect("127.0.0.1", port, BattleTicket.Issue(key, name, name));
        clients.Add(client); return client;
    }
    private void Update()
    {
        foreach (var client in clients) client.Connection.Poll();
        accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
        while (accumulator >= BattleProtocol.Step)
        {
            accumulator -= BattleProtocol.Step;
            foreach (var client in clients)
                if (client.Connection.Ready) client.Connection.Send(new[] { new BattleInput { Sequence = ++client.Sequence } });
        }
    }
    private static IEnumerator Until(Func<bool> condition, string message, float seconds = 8)
    {
        float end = Time.realtimeSinceStartup + seconds;
        while (!condition()) { Check(Time.realtimeSinceStartup < end, "Timeout: " + message); yield return null; }
        Debug.Log("PASS " + message);
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private void OnDestroy()
    {
        foreach (var client in clients) client.Dispose();
        Application.logMessageReceived -= OnLog;
    }
}
#endif
