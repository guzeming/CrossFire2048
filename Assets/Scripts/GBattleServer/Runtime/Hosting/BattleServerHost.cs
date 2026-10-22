using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using LiteNetLib;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Diagnostics;
using OperationBlacktide.Simulation;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.GameLogic;
using UnityEngine;
using UnityEngine.AI;

namespace OperationBlacktide.BattleServer
{
    // One room, one Unity physics world, one authoritative simulation clock.
    public sealed class BattleServerHost : MonoBehaviour
    {
        private sealed class Pawn
        {
            public NetPeer Peer;
            public BattleTicket Identity;
            public CharacterController Motor;
            public BattleState State;
            public Character Character;
            public Weapon Weapon => Character.Equipment.Current;
            public readonly Queue<BattleInput> Inputs = new Queue<BattleInput>();
            public readonly BattleState[] History = new BattleState[32];
            public readonly int[] HistoryTicks = Enumerable.Repeat(-1, 32).ToArray();
            public uint LastReceived;
            public int NextFire, PacketCount, PacketWindow, Denied, NextDenialLog;
            public int Slot;
            public Vector2 LastMove;
            public BattleBotBrain Brain;
        }
        private readonly Dictionary<int, Pawn> pawns = new Dictionary<int, Pawn>();
        private readonly Dictionary<int, Pawn> connections = new Dictionary<int, Pawn>();
        private readonly List<BattleState> observations = new List<BattleState>(BattleProtocol.MaxPlayers);
        private readonly Dictionary<string, long> usedTickets = new Dictionary<string, long>();
        private GameLogic gameLogic;
        private WeaponInfo battleWeapon;
        private NetManager network;
        private ServerEventLog log;
        private ArenaMap arena;
        private byte[] key;
        private int tick, nextId = 1, joinRejects;
        private string stopFile;
        private float accumulator;
        private int configuredBots;
        private bool stopping;
        public int Tick => tick;
        public int HumanCount => connections.Count;
        public int BotCount => pawns.Count - connections.Count;

        internal static void Bootstrap()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (!args.Contains("--battle-server")) return;
            var host = new GameObject("Authoritative battle server").AddComponent<BattleServerHost>();
            try { host.StartServer(int.Parse(Argument(args, "--port", "7778")), Argument(args, "--battle-key", "data/battle.key"), Argument(args, "--logs", "Logs/Server"),
                int.Parse(Argument(args, "--bots", "0"))); }
            catch (Exception ex) { Debug.LogException(ex); Application.Quit(1); }
        }
        public static string Argument(string[] args, string name, string fallback)
        { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback; }

        public void StartServer(int port, string keyFile, string logDirectory, int bots = 0)
        {
            if (bots < 0 || bots >= BattleProtocol.MaxPlayers) throw new ArgumentOutOfRangeException(nameof(bots), "Expected 0 to 7 bots.");
            configuredBots = bots;
            gameLogic = new GameLogic(GameConfig.LoadMode("battle"), true);
            battleWeapon = GameConfig.LoadBattleWeapon();
            if (gameLogic.Respawn != null)
                gameLogic.Respawn.Requested += character => { if (pawns.TryGetValue(character.Id, out var pawn)) Respawn(pawn); };
            key = File.ReadAllBytes(keyFile);
            stopFile = Argument(Environment.GetCommandLineArgs(), "--stop-file", "");
            if (key.Length != 32) throw new InvalidDataException("Expected 32-byte server signing key.");
            log = new ServerEventLog(logDirectory, "battle");
            Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            arena = ArenaMap.Initialize();
            if (bots > 0)
                for (int i = 0; i < arena.SpawnCount; i++)
                    if (!NavMesh.SamplePosition(arena.Spawn(i), out _, 1, NavMesh.AllAreas))
                        throw new InvalidOperationException("Bot navigation is missing. Rebuild the battle server.");
            var listener = new EventBasedNetListener();
            network = new NetManager(listener) { AutoRecycle = true, IPv6Enabled = false, DisconnectTimeout = 5000, PingInterval = 500, MaxFragmentsCount = 2 };
            listener.ConnectionRequestEvent += Join;
            listener.NetworkReceiveEvent += Receive;
            listener.PeerDisconnectedEvent += (peer, info) => Leave(peer.Id, info.Reason.ToString());
            if (!network.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, port)) throw new IOException("Battle UDP port unavailable: " + port);
            log.Write("server_start", "loopback UDP " + port + "; tick=60 snapshot=20 room=" + BattleTicket.Room + " map=" + BattleProtocol.MapId + "; bots=" + bots, room: BattleTicket.Room);
            Debug.Log("BATTLE_SERVER_READY port=" + port);
        }
        private void Join(ConnectionRequest request)
        {
            try
            {
                if (request.Data.AvailableBytes > BattleProtocol.MaxPacket || !request.Data.TryGetString(out string token) || token.Length > 1024)
                { request.Reject(); joinRejects++; return; }
                if (request.Data.AvailableBytes != 0 || connections.Count >= BattleProtocol.MaxPlayers ||
                    !BattleTicket.TryRead(key, token, out var identity) || usedTickets.ContainsKey(identity.Nonce) ||
                    pawns.Values.Any(p => p.Identity.UserId == identity.UserId))
                { request.Reject(); joinRejects++; return; }
                usedTickets[identity.Nonce] = identity.Expires;
                NetPeer peer = request.Accept();
                if (peer == null) return;
                // Human connections take precedence over an automatically filled bot slot.
                if (pawns.Count >= BattleProtocol.MaxPlayers)
                    RemoveBot(pawns.Values.Where(p => p.Brain != null).OrderByDescending(p => p.Slot).First());
                int slot = Enumerable.Range(0, BattleProtocol.MaxPlayers).First(s => pawns.Values.All(p => p.Slot != s));
                var pawn = new Pawn { Peer = peer, Identity = identity, Slot = slot, Motor = CharacterSimulation.CreatePawn(nextId) };
                pawn.State.Id = nextId++;
                pawns.Add(pawn.State.Id, pawn); connections.Add(peer.Id, pawn);
                Respawn(pawn);
                peer.Send(BattleProtocol.Write(BattlePacket.Welcome, w => { w.Write(pawn.State.Id); w.Write(tick); w.Write(BattleProtocol.MapId); }), DeliveryMethod.ReliableOrdered);
                Event(pawn, "join", "Player joined " + BattleTicket.Room + " map=" + BattleProtocol.MapId);
                SynchronizeBots();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is IndexOutOfRangeException)
            { request.Reject(); joinRejects++; }
        }
        private void Receive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
        {
            if (!connections.TryGetValue(peer.Id, out Pawn p)) return;
            if (tick - p.PacketWindow >= 60) { p.PacketCount = 0; p.PacketWindow = tick; }
            if (++p.PacketCount > 120) { Deny(p, "packet_rate"); return; }
            try
            {
                if (packet.AvailableBytes > BattleProtocol.MaxPacket) throw new InvalidDataException("packet_size");
                using var reader = BattleProtocol.Read(packet.GetRemainingBytes(), out var kind);
                if (kind != BattlePacket.Inputs) throw new InvalidDataException("client_packet_type");
                int count = reader.ReadByte();
                if (count < 1 || count > 3) throw new InvalidDataException("input_count");
                var inputs = new BattleInput[count];
                for (int i = 0; i < count; i++) inputs[i] = BattleProtocol.ReadInput(reader);
                BattleProtocol.End(reader);
                foreach (BattleInput input in inputs)
                {
                    if (input.Sequence <= p.LastReceived) continue; // redundant resends / reordering
                    if (input.Sequence - p.LastReceived > 120) { Deny(p, "sequence_jump"); continue; }
                    if (p.Inputs.Count >= 8) { Deny(p, "input_queue_full"); continue; }
                    p.LastReceived = input.Sequence; p.Inputs.Enqueue(input);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException) { Deny(p, "malformed_input"); }
        }
        private void Update()
        {
            if (network == null) return;
            network.PollEvents();
            accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
            int steps = 0;
            while (accumulator >= BattleProtocol.Step && steps++ < 6)
            { accumulator -= BattleProtocol.Step; Simulate(); }
        }
        private void Simulate()
        {
            tick++;
            if (tick % 30 == 0 && stopFile.Length > 0 && File.Exists(stopFile)) { Application.Quit(0); return; }
            gameLogic.Tick(BattleProtocol.Step);
            Physics.SyncTransforms();
            observations.Clear();
            foreach (Pawn p in pawns.Values) observations.Add(p.State);
            foreach (Pawn p in pawns.Values)
            {
                bool wasReloading = p.State.ReloadTicks > 0;
                SyncWeaponState(p);
                p.State.Health = p.Character.Vitals.Health;
                if (wasReloading && p.State.ReloadTicks == 0) Event(p, "reload_complete", "ammo=" + p.State.Ammo);
                if (p.Brain != null)
                {
                    BattleBotMode previous = p.Brain.Mode;
                    BattleInput command = p.Brain.Think(p.State, observations, tick);
                    p.LastReceived = command.Sequence; p.Inputs.Enqueue(command);
                    if (previous != p.Brain.Mode) Event(p, "bot_state", p.Brain.Mode + "; target=" + p.Brain.TargetId);
                }
                if (p.Inputs.Count > 0)
                {
                    BattleInput input = p.Inputs.Dequeue();
                    CharacterSimulation.Step(p.Motor, ref p.State, input);
                    if (p.State.Health > 0)
                    {
                        Vector2 movement = new Vector2(input.X, input.Z);
                        if ((movement - p.LastMove).sqrMagnitude > .1f)
                        { Event(p, movement.sqrMagnitude > .01f ? "move" : "stop", "input=" + movement.ToString("F2") + "; position=" + p.Motor.transform.position.ToString("F2") + "; sprint=" + input.Has(InputButtons.Sprint)); p.LastMove = movement; }
                        if (input.Has(InputButtons.Jump)) Event(p, "jump", "Jump requested; grounding validated by motor");
                        if (input.Has(InputButtons.Reload) && p.Weapon.Ammo.TryReload())
                        { SyncWeaponState(p); Event(p, "reload_start", "duration=" + battleWeapon.reloadDuration + "s"); }
                        if (input.Has(InputButtons.Fire)) Fire(p);
                    }
                }
                else if (p.State.Health > 0)
                {
                    CharacterSimulation.Step(p.Motor, ref p.State, new BattleInput { Sequence = p.State.Ack, Yaw = p.State.Yaw });
                    if (p.LastMove != Vector2.zero) { p.LastMove = Vector2.zero; Event(p, "stop", "No input"); }
                }
                p.History[tick % 32] = p.State; p.HistoryTicks[tick % 32] = tick;
                if (p.State.Y < arena.KillHeight && p.State.Health > 0) { Kill(p); Event(p, "death", "Out of world"); }
            }
            if (tick % BattleProtocol.SnapshotInterval == 0) Snapshot();
            if (tick % 300 == 0)
            {
                log.Write("status", "humans=" + HumanCount + "; bots=" + BotCount + "; rejected_joins=" + joinRejects, tick: tick, room: BattleTicket.Room); joinRejects = 0;
                foreach (string nonce in usedTickets.Where(kv => kv.Value < DateTimeOffset.UtcNow.ToUnixTimeSeconds()).Select(kv => kv.Key).ToArray()) usedTickets.Remove(nonce);
            }
        }
        private void Fire(Pawn shooter)
        {
            if (tick < shooter.NextFire || !shooter.Weapon.TryFire(gameLogic.Clock.ElapsedSeconds)) return;
            SyncWeaponState(shooter);
            Vector3 origin = shooter.Motor.transform.position + Vector3.up * 1.25f;
            Vector3 direction = shooter.Motor.transform.forward;
            // Server-measured RTT + remote interpolation delay, capped at 200 ms. No client timestamp is trusted.
            int rewind = shooter.Brain != null ? 0 : Mathf.Clamp(Mathf.RoundToInt((shooter.Peer.Ping * .5f + 100) * .06f), 0, 12);
            int historyTick = tick - rewind;
            var restored = new List<Pawn>();
            int hitId = 0; Vector3 end = origin + direction * battleWeapon.range;
            try
            {
                shooter.Motor.enabled = false;
                foreach (Pawn target in pawns.Values)
                {
                    if (target == shooter || target.State.Health <= 0) continue;
                    int index = (historyTick % 32 + 32) % 32;
                    if (target.HistoryTicks[index] == historyTick && target.History[index].Life == target.State.Life)
                    { CharacterSimulation.Restore(target.Motor, target.History[index]); restored.Add(target); }
                }
                Physics.SyncTransforms();
                if (Physics.Raycast(origin, direction, out RaycastHit hit, battleWeapon.range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point;
                    Pawn target = pawns.Values.FirstOrDefault(p => p != shooter && p.Motor == hit.collider && p.State.Health > 0);
                    // Teammates still block shots physically, but friendly fire is disabled in this test mode.
                    if (target != null && target.State.Team != shooter.State.Team)
                    {
                        hitId = target.State.Id; target.Character.Vitals.ApplyDamage(battleWeapon.damage);
                        target.State.Health = target.Character.Vitals.Health;
                        Event(shooter, "hit", "target=" + target.Identity.Username + "; damage=" + battleWeapon.damage + "; hp=" + target.State.Health + "; rewindTicks=" + rewind);
                        Event(target, "damage", "attacker=" + shooter.Identity.Username + "; hp=" + target.State.Health);
                        if (target.State.Health == 0)
                        { Kill(target); Event(target, "death", "killer=" + shooter.Identity.Username); }
                    }
                }
            }
            finally
            {
                foreach (Pawn p in restored)
                { CharacterSimulation.Restore(p.Motor, p.State); if (p.State.Health <= 0) p.Motor.enabled = false; }
                shooter.Motor.enabled = true; Physics.SyncTransforms();
            }
            Event(shooter, "fire", "ammo=" + shooter.State.Ammo + "; target=" + hitId);
            network.SendToAll(BattleProtocol.Write(BattlePacket.Shot, w =>
            { w.Write(shooter.State.Id); w.Write(hitId); w.Write(origin.x); w.Write(origin.y); w.Write(origin.z); w.Write(end.x); w.Write(end.y); w.Write(end.z); }), DeliveryMethod.ReliableOrdered);
        }
        private void Respawn(Pawn p)
        {
            if (p.Character == null)
            {
                p.Character = gameLogic.World.Add(new Character(p.State.Id));
                p.Character.Equipment.AddWeapon(new Weapon("primary", battleWeapon, gameLogic.Supply?.InfiniteAmmoReserve ?? false));
                p.Character.Equipment.Equip("primary");
            }
            p.Character.Vitals.Restore(); p.Character.Equipment.Reset();
            Vector3 pos = arena.Spawn(p.Slot);
            p.State = new BattleState { Id = p.State.Id, Life = p.State.Life + 1, Ack = p.LastReceived, X = pos.x, Y = pos.y, Z = pos.z,
                Yaw = p.Slot % 2 == 0 ? 0 : 180, Health = 100, Ammo = BattleProtocol.Capacity, Reserve = BattleProtocol.Reserve,
                Team = p.Slot % 2 == 0 ? BattleTeam.CT : BattleTeam.T, IsBot = p.Brain != null };
            p.Character.Team = (int)p.State.Team; SyncWeaponState(p);
            p.Inputs.Clear(); p.NextFire = tick + 30; p.LastMove = Vector2.zero;
            CharacterSimulation.Restore(p.Motor, p.State); Event(p, "respawn", "life=" + p.State.Life);
            p.Brain?.Reset(pos, tick);
        }

        private void Kill(Pawn p)
        {
            p.Character.Vitals.ApplyDamage(p.Character.Vitals.MaxHealth, true);
            p.Weapon.Ammo.CancelReload(); p.State.Health = 0; p.State.ReloadTicks = 0;
            p.Inputs.Clear(); p.Motor.enabled = false;
        }

        private static void SyncWeaponState(Pawn p)
        {
            var ammo = p.Weapon.Ammo;
            p.State.Ammo = ammo.Magazine; p.State.Reserve = ammo.Reserve;
            p.State.ReloadTicks = ammo.IsReloading ? Math.Max(1, (int)Math.Ceiling((ammo.ReloadDuration - ammo.ReloadElapsed) * BattleProtocol.TickRate - .0001)) : 0;
        }

        private void SynchronizeBots()
        {
            int desired = HumanCount == 0 ? 0 : Math.Min(configuredBots, BattleProtocol.MaxPlayers - HumanCount);
            foreach (Pawn bot in pawns.Values.Where(p => p.Brain != null).OrderByDescending(p => p.Slot).Take(Math.Max(0, BotCount - desired)).ToArray())
                RemoveBot(bot);
            while (BotCount < desired)
            {
                int slot = Enumerable.Range(0, BattleProtocol.MaxPlayers).First(s => pawns.Values.All(p => p.Slot != s));
                int id = nextId++;
                var patrol = Enumerable.Range(0, arena.SpawnCount).Select(arena.Spawn).ToArray();
                var bot = new Pawn { Slot = slot, Motor = CharacterSimulation.CreatePawn(id), State = new BattleState { Id = id },
                    Identity = new BattleTicket { UserId = "bot-" + id, Username = "BOT " + id }, Brain = new BattleBotBrain(id, patrol) };
                pawns.Add(id, bot); Respawn(bot); Event(bot, "bot_join", "team=" + bot.State.Team);
            }
        }

        private void RemoveBot(Pawn bot)
        {
            Event(bot, "bot_leave", "Slot released"); pawns.Remove(bot.State.Id);
            bot.Brain.Dispose(); gameLogic.World.Remove(bot.State.Id);
            bot.Motor.gameObject.SetActive(false); Destroy(bot.Motor.gameObject);
        }
        private void Snapshot()
        {
            // P2 controlled arena: full room state. Visibility filtering is a separate P4 requirement.
            foreach (Pawn recipient in connections.Values) recipient.Peer.Send(BuildSnapshot(recipient), DeliveryMethod.Sequenced);
        }
        private byte[] BuildSnapshot(Pawn recipient) => BattleProtocol.Write(BattlePacket.Snapshot, w =>
        {
            w.Write(tick); w.Write((byte)pawns.Count);
            foreach (Pawn p in pawns.Values)
            {
                BattleState view = p.State;
                if (p != recipient) { view.Ack = 0; view.Ammo = 0; view.Reserve = 0; }
                BattleProtocol.WriteState(w, view);
            }
        });
        private void Deny(Pawn p, string reason)
        {
            p.Denied++;
            if (tick < p.NextDenialLog) return;
            Event(p, "input_denied", reason + "; count=" + p.Denied, "WARN"); p.Denied = 0; p.NextDenialLog = tick + 60;
        }
        private void Event(Pawn p, string action, string message, string level = "INFO") =>
            log.Write(action, message, p.Identity.Username, p.Identity.UserId, tick, p.State.Ack, level, BattleTicket.Room, p.State.Id);
        private void Leave(int peerId, string reason)
        {
            if (!connections.TryGetValue(peerId, out Pawn p)) return;
            Event(p, "leave", reason); connections.Remove(peerId); pawns.Remove(p.State.Id);
            gameLogic.World.Remove(p.State.Id);
            p.Motor.gameObject.SetActive(false); Destroy(p.Motor.gameObject);
            if (!stopping) SynchronizeBots();
        }
        private void OnDestroy()
        {
            stopping = true; network?.Stop(); network = null;
            foreach (Pawn p in pawns.Values) { p.Brain?.Dispose(); if (p.Motor != null) Destroy(p.Motor.gameObject); }
            pawns.Clear(); connections.Clear(); gameLogic?.Dispose();
            log?.Write("server_stop", "Battle server stopped", tick: tick); log?.Dispose();
        }
    }
}
