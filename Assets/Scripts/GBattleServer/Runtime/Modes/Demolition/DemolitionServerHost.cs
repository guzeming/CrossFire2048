using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using LiteNetLib;
using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Modes.Demolition;
using OperationBlacktide.Simulation;
using UnityEngine;
using UnityEngine.AI;

namespace OperationBlacktide.BattleServer
{
    public sealed class DemolitionServerHost : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker TickMarker = new Unity.Profiling.ProfilerMarker("Blacktide.Server.Tick");
        sealed class Pawn
        {
            public DemolitionPlayer Player;
            public CharacterController Motor;
            public BattleState State;
            public NetPeer Peer;
            public BattleBotBrain Brain;
            public readonly Queue<DemolitionInput> Inputs = new Queue<DemolitionInput>();
            public uint LastSequence;
            public int LastInput, Packets, Window;
            public Vector3 SafePosition;
            public bool HasAim;
            public Vector3 AimPoint;
            public OpeningOrder Order;
            public int TeamSlot;
            public readonly DemolitionBotNavigation Navigation = new DemolitionBotNavigation();
        }
        readonly Dictionary<int, Pawn> pawns = new Dictionary<int, Pawn>();
        DemolitionManifest manifest;
        DemolitionMap map;
        ArenaMap arena;
        DemolitionGame game;
        NetManager network;
        byte[] key;
        int tick;
        float accumulator;
        bool completed;
        double finishAt = -1;
        string stopFile;
        int matchSeed, openingLiveAt = -1, bombResponder;
        BombPhase lastBombPhase;

        internal static void Bootstrap()
        {
            string path = BattleServerHost.Argument(Environment.GetCommandLineArgs(), "--demolition-manifest", "");
            if (path.Length == 0) return;
            try { new GameObject("Demolition authority").AddComponent<DemolitionServerHost>().StartServer(path); }
            catch (Exception e) { Debug.LogException(e); Application.Quit(1); }
        }
        public void StartServer(string path)
        {
            manifest = JsonUtility.FromJson<DemolitionManifest>(File.ReadAllText(path));
            matchSeed = DemolitionTactics.Seed(manifest.MatchId);
            key = File.ReadAllBytes(manifest.KeyFile); if (key.Length != 32) throw new InvalidDataException("Invalid server signing key.");
            stopFile = BattleServerHost.Argument(Environment.GetCommandLineArgs(), "--stop-file", "");
            arena = ArenaMap.Initialize(); map = FindObjectOfType<DemolitionMap>();
            if (map == null) throw new InvalidOperationException("Build demolition map first."); map.Validate();
            var ruleSource = Resources.Load<TextAsset>("Config/DemolitionRules");
            if (ruleSource == null) throw new InvalidOperationException("Import demolition rules before building.");
            game = new DemolitionGame(manifest.Seats, map, JsonUtility.FromJson<DemolitionRules>(ruleSource.text));
            foreach (var p in game.Teams.Players)
            {
                var pawn = new Pawn { Player = p, Motor = CharacterSimulation.CreatePawn(p.Seat.Id) };
                if (p.Seat.IsBot) pawn.Brain = new BattleBotBrain(p.Seat.Id, map.Sites);
                pawns.Add(p.Seat.Id, pawn);
                p.Character.Vitals.Died += () => { p.Deaths++; p.Interact = false; pawn.Motor.enabled = false; pawn.Inputs.Clear(); };
            }
            game.Round.RoundStarted += SpawnRound;
            PlacePlayers();
            var listener = new EventBasedNetListener();
            network = new NetManager(listener) { AutoRecycle = true, IPv6Enabled = false, DisconnectTimeout = 5000, PingInterval = 500, MaxFragmentsCount = 8 };
            listener.ConnectionRequestEvent += Join;
            listener.PeerDisconnectedEvent += (peer, info) =>
            {
                var p = pawns.Values.FirstOrDefault(x => x.Peer == peer); if (p == null) return;
                p.Peer = null; p.Inputs.Clear(); game.Teams.Connection(p.Player.Seat.Id, false);
            };
            listener.NetworkReceiveEvent += Receive;
            if (!network.Start(IPAddress.Loopback, IPAddress.IPv6Loopback, manifest.Port)) throw new IOException("Demolition port unavailable.");
            Application.runInBackground = true; Application.targetFrameRate = 60; QualitySettings.vSyncCount = 0;
            File.WriteAllText(manifest.ReadyFile, manifest.MatchId);
            Debug.Log("DEMOLITION_SERVER_READY " + manifest.MatchId + " port=" + manifest.Port);
        }
        void Join(ConnectionRequest request)
        {
            try
            {
                if (request.Data.AvailableBytes > 1024 || !request.Data.TryGetString(out string token) || request.Data.AvailableBytes != 0 ||
                    !DemolitionTicket.Validate(key, token, manifest.MatchId, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), out string user)) { request.Reject(); return; }
                var p = pawns.Values.FirstOrDefault(x => x.Player.Seat.UserId == user && !x.Player.Seat.IsBot);
                if (p == null || p.Player.Abandoned || p.Peer != null || completed) { request.Reject(); return; }
                p.Peer = request.Accept(); if (p.Peer == null) return;
                game.Teams.Connection(p.Player.Seat.Id, true); p.Inputs.Clear();
                p.Peer.Send(DemolitionProtocol.Write(DemolitionPacket.Welcome, w => { w.Write(p.Player.Seat.Id); w.Write(manifest.MatchId); w.Write(p.LastSequence); }), DeliveryMethod.ReliableOrdered);
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { request.Reject(); }
        }
        void Receive(NetPeer peer, NetPacketReader packet, byte channel, DeliveryMethod method)
        {
            var p = pawns.Values.FirstOrDefault(x => x.Peer == peer); if (p == null) return;
            if (tick-p.Window >= 60) { p.Window = tick; p.Packets = 0; }
            if (++p.Packets > 150) return;
            try
            {
                if (packet.AvailableBytes > DemolitionProtocol.MaxPacket) return;
                using var r = DemolitionProtocol.Read(packet.GetRemainingBytes(), out var kind);
                switch (kind)
                {
                    case DemolitionPacket.Input:
                        var received = DemolitionProtocol.Input(r); var input = received.Command;
                        if (input.Sequence <= p.LastSequence || input.Sequence-p.LastSequence > 240 || p.Inputs.Count >= 8) return;
                        p.LastSequence = input.Sequence; p.LastInput = tick; p.Inputs.Enqueue(received); break;
                    case DemolitionPacket.Buy:
                        var item = (BuyItem)r.ReadByte(); BattleProtocol.End(r);
                        bool bought = game.Economy.Buy(p.Player.Seat.Id, item);
                        peer.Send(DemolitionProtocol.Write(DemolitionPacket.Feedback, w => w.Write(bought ? "购买成功" : "购买失败：检查资金、阵营或购买阶段")), DeliveryMethod.ReliableOrdered); break;
                    case DemolitionPacket.Equip:
                        int slot = r.ReadByte(); BattleProtocol.End(r); game.Economy.Equip(p.Player.Seat.Id, slot); break;
                    case DemolitionPacket.Surrender:
                        BattleProtocol.End(r); bool voted = game.Round.Surrender(p.Player.Seat.Id);
                        peer.Send(DemolitionProtocol.Write(DemolitionPacket.Feedback, w => w.Write(voted ? "已投赞成票" : "有队友离线满 3 分钟后才可投降")), DeliveryMethod.ReliableOrdered); break;
                }
            }
            catch (Exception e) when (e is IOException || e is ArgumentException) { }
        }
        void Update()
        {
            if (network == null || game == null) return;
            network.PollEvents(); accumulator += Mathf.Min(.1f, Time.unscaledDeltaTime);
            while (accumulator >= BattleProtocol.Step) { accumulator -= BattleProtocol.Step; Step(); }
        }
        void Step()
        {
            using var profile = TickMarker.Auto();
            tick++;
            if (tick % 30 == 0 && stopFile.Length > 0 && File.Exists(stopFile)) { Application.Quit(); return; }
            game.Logic.Tick(BattleProtocol.Step);
            bool live = game.Round.Phase == RoundPhase.Live || game.Round.Phase == RoundPhase.Planted;
            if (live && openingLiveAt < 0) openingLiveAt = tick;
            if (live) UpdateBombResponder();
            Physics.SyncTransforms();
            var observations = pawns.Values.Select(p => State(p)).ToArray();
            foreach (var p in pawns.Values)
            {
                var state = State(p); p.State = state;
                var command = new BattleInput { Sequence = p.State.Ack, Yaw = state.Yaw };
                bool interact = false;
                if (live && p.Brain != null && p.Player.Alive)
                {
                    command = Bot(p, observations, out interact); p.LastSequence = command.Sequence;
                }
                else if (p.Inputs.Count > 0)
                { var received = p.Inputs.Dequeue(); command = received.Command; interact = received.Interact; p.HasAim = received.HasAim; p.AimPoint = DemolitionMap.Vector(received.AimPoint); }
                if (!live || !p.Player.Alive || (!p.Player.Connected && p.Brain == null)) { command.X = command.Z = 0; command.Buttons = InputButtons.None; interact = false; }
                if (p.Brain == null && tick-p.LastInput > 12) interact = false;
                p.Player.Interact = interact;
                // Holding an objective action prevents movement/fire/reload for both humans and bots.
                if (interact) { command.X = command.Z = 0; command.Buttons = InputButtons.None; }
                if (p.Player.Alive) CharacterSimulation.Step(p.Motor, ref p.State, command);
                else p.State.Ack = command.Sequence;
                p.Player.Position = DemolitionMap.Point(p.Motor.transform.position);
                if (p.Motor.isGrounded) p.SafePosition = p.Motor.transform.position;
                if (live && p.Player.Alive && p.State.Y < arena.KillHeight)
                { p.Player.Position = DemolitionMap.Point(p.SafePosition); p.Player.Character.Vitals.ApplyDamage(1000, true); }
                if (!live || !p.Player.Alive) continue;
                var weapon = p.Player.Character.Equipment.Current;
                if (command.Has(InputButtons.Reload)) weapon?.Ammo.TryReload();
                if (command.Has(InputButtons.Fire) && weapon != null && weapon.TryFire(game.Logic.Clock.ElapsedSeconds)) Fire(p);
            }
            if (tick % 3 == 0) Snapshot();
            if (game.Round.Phase == RoundPhase.Finished || game.Round.Phase == RoundPhase.Aborted)
            {
                if (finishAt < 0) finishAt = game.Logic.Clock.ElapsedSeconds + 8;
                if (game.Logic.Clock.ElapsedSeconds >= finishAt) Finish(game.Round.Phase == RoundPhase.Finished);
            }
            else if (game.Teams.Players.Where(p => !p.Seat.IsBot).All(p => p.Abandoned)) Finish(false);
        }
        BattleInput Bot(Pawn p, BattleState[] observations, out bool interact)
        {
            interact = false;
            var weapon = p.Player.Character.Equipment.Current;
            var command = p.Brain.Think(p.State, observations, tick, false, weapon?.Info.magazineSize ?? BattleProtocol.Capacity);
            bool attack = game.Teams.IsAttacker(p.Player);
            var route = map.Route(p.Order.Lane);
            Vector3 position = p.Motor.transform.position;
            Vector3 goal = p.Navigation.RouteGoal(route, position), lookAt = route.lookAt;
            bool planted = game.Bomb.Phase == BombPhase.Planted;
            if (planted)
            {
                var hold = map.Hold(game.Bomb.Site, p.TeamSlot);
                goal = hold.position; lookAt = hold.lookAt;
                if (!attack && p.Player.Seat.Id == bombResponder)
                {
                    goal = DemolitionMap.Vector(game.Bomb.Position);
                    if ((position - goal).sqrMagnitude < 6 && map.CanReach(p.Player.Position, game.Bomb.Position)) interact = true;
                }
            }
            else if (game.Bomb.Phase == BombPhase.Dropped && attack && p.Player.Seat.Id == bombResponder)
                goal = DemolitionMap.Vector(game.Bomb.Position);
            else if (attack && p.Navigation.Waypoint == route.waypoints.Length - 1 && p.Player.Seat.Id != game.Bomb.Carrier)
            {
                var hold = map.Hold(route.site, p.TeamSlot); goal = hold.position; lookAt = hold.lookAt;
            }
            bool fighting = p.Brain.Mode == BattleBotMode.Engage;
            if (game.Bomb.Carrier == p.Player.Seat.Id && map.InSite(p.Player.Position, out _) &&
                (!fighting || game.Bomb.Interactor == p.Player.Seat.Id)) interact = true;
            // Keep the turn-rate and aim error from the shared brain, adding only vertical aim.
            p.HasAim = fighting;
            if (fighting)
            {
                Vector3 offset = p.Brain.LastKnownPosition - position;
                p.AimPoint = position + Vector3.up * (1.25f + offset.y) +
                    Quaternion.Euler(0, command.Yaw, 0) * Vector3.forward * Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
            }
            if (interact) return command;
            if (fighting || p.State.ReloadTicks > 0 || command.Has(InputButtons.Reload))
            { SpaceTeammates(p, observations, ref command); return command; }
            if (!planted && tick - openingLiveAt < p.Order.Delay * BattleProtocol.TickRate)
            { command.X = command.Z = 0; return command; }
            p.Navigation.Move(ref command, p.State, goal, lookAt, tick);
            SpaceTeammates(p, observations, ref command);
            return command;
        }
        void UpdateBombResponder()
        {
            bool needDefuser = game.Bomb.Phase == BombPhase.Planted;
            bool needPickup = game.Bomb.Phase == BombPhase.Dropped;
            if (!needDefuser && !needPickup) { bombResponder = 0; lastBombPhase = game.Bomb.Phase; return; }
            if (lastBombPhase == game.Bomb.Phase && pawns.TryGetValue(bombResponder, out var assigned) && assigned.Player.Alive) return;
            lastBombPhase = game.Bomb.Phase;
            Vector3 bomb = DemolitionMap.Vector(game.Bomb.Position);
            bombResponder = pawns.Values.Where(p => p.Brain != null && p.Player.Alive && game.Teams.IsAttacker(p.Player) != needDefuser)
                .OrderBy(p => DemolitionBotNavigation.Distance(p.Motor.transform.position, bomb) - (needDefuser && p.Player.Kit ? 5 : 0))
                .Select(p => p.Player.Seat.Id).FirstOrDefault();
        }
        void SpaceTeammates(Pawn p, BattleState[] observations, ref BattleInput command)
        {
            Vector3 world = Quaternion.Euler(0, command.Yaw, 0) * new Vector3(command.X, 0, command.Z);
            Vector3 separation = Vector3.zero;
            foreach (var ally in observations)
            {
                if (ally.Id == p.State.Id || ally.Team != p.State.Team || ally.Health <= 0) continue;
                Vector3 delta = p.Motor.transform.position - CharacterSimulation.Position(ally);
                if (Mathf.Abs(delta.y) > 1.5f) continue;
                delta.y = 0; float distance = delta.magnitude;
                if (distance >= 1.25f) continue;
                // Stable opposite directions also separate actors whose positions already coincide.
                Vector3 away = distance > .03f ? delta / distance :
                    Quaternion.Euler(0, ((p.State.Id + ally.Id) * 67) % 360, 0) * Vector3.right * (p.State.Id < ally.Id ? 1 : -1);
                separation += away * (1.25f - distance) * 2;
            }
            if (separation.sqrMagnitude > .01f)
            {
                Vector3 desired = Vector3.ClampMagnitude(world + separation, 1);
                Vector3 position = p.Motor.transform.position;
                if (NavMesh.SamplePosition(position, out var origin, .5f, NavMesh.AllAreas) &&
                    !NavMesh.Raycast(origin.position, origin.position + desired * .5f, out _, NavMesh.AllAreas)) world = desired;
                else world *= .25f;
            }
            Vector3 local = Quaternion.Euler(0, -command.Yaw, 0) * world; command.X = local.x; command.Z = local.z;
        }
        void Fire(Pawn shooter)
        {
            Vector3 origin = shooter.Motor.transform.position + Vector3.up*1.25f;
            Vector3 direction = shooter.HasAim ? (shooter.AimPoint-origin).normalized : shooter.Motor.transform.forward;
            if (direction.sqrMagnitude < .01f) direction = shooter.Motor.transform.forward;
            float range = shooter.Player.Character.Equipment.Current.Info.range;
            Vector3 end = origin+direction*range;
            int hitId = 0; shooter.Motor.enabled = false;
            try
            {
                Physics.SyncTransforms();
                if (Physics.Raycast(origin, direction, out var hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    end = hit.point; var target = pawns.Values.FirstOrDefault(p => p.Motor == hit.collider && p.Player.Alive);
                    if (target != null && target.Player.Seat.Team != shooter.Player.Seat.Team)
                    {
                        hitId = target.Player.Seat.Id; target.Player.Character.Vitals.ApplyDamage(shooter.Player.Character.Equipment.Current.Info.damage);
                        if (!target.Player.Alive) { shooter.Player.Kills++; game.Economy.Credit(shooter.Player, 300); }
                    }
                }
            }
            finally { shooter.Motor.enabled = shooter.Player.Alive; }
            network.SendToAll(DemolitionProtocol.Write(DemolitionPacket.Shot, w =>
            { w.Write(shooter.Player.Seat.Id); w.Write(hitId); DemolitionProtocol.Point(w, DemolitionMap.Point(origin)); DemolitionProtocol.Point(w, DemolitionMap.Point(end)); }), DeliveryMethod.ReliableOrdered);
        }
        void SpawnRound()
        {
            PlacePlayers();
            foreach (var p in pawns.Values.Where(p => p.Player.Seat.IsBot))
            {
                game.Economy.Buy(p.Player.Seat.Id, game.Teams.IsAttacker(p.Player) ? BuyItem.AK47 : BuyItem.M4A1);
                game.Economy.Buy(p.Player.Seat.Id, BuyItem.Armor);
                game.Economy.Buy(p.Player.Seat.Id, BuyItem.DefuseKit);
            }
        }
        void PlacePlayers()
        {
            openingLiveAt = -1; bombResponder = 0;
            var attacks = DemolitionTactics.SpawnSlots(map.AttackSpawns.Length, game.Round.Number, matchSeed);
            var defenders = DemolitionTactics.SpawnSlots(map.DefendSpawns.Length, game.Round.Number, matchSeed ^ 0x4f13);
            int a = 0, b = 0;
            foreach (var p in pawns.Values.OrderBy(p => p.Player.Seat.Id))
            {
                bool attack = game.Teams.IsAttacker(p.Player); p.TeamSlot = attack ? a++ : b++;
                int slot = attack ? attacks[p.TeamSlot] : defenders[p.TeamSlot];
                Vector3 pos = attack ? map.AttackSpawns[slot] : map.DefendSpawns[slot];
                float yaw = attack ? map.AttackYaw[slot] : map.DefendYaw[slot];
                p.State = new BattleState { Id = p.Player.Seat.Id, Ack = p.LastSequence, X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw };
                p.Player.Position = DemolitionMap.Point(pos); p.SafePosition = pos; p.Inputs.Clear(); p.HasAim = false;
                CharacterSimulation.Restore(p.Motor, State(p)); p.Motor.enabled = p.Player.Alive;
                p.Brain?.Reset(pos, tick); p.Navigation.Reset();
            }
            foreach (bool attack in new[] { true, false })
            {
                var ids = pawns.Values.Where(p => game.Teams.IsAttacker(p.Player) == attack).Select(p => p.Player.Seat.Id).OrderBy(id => id).ToArray();
                var plan = DemolitionTactics.Plan(attack, game.Round.Number, matchSeed, ids, game.Bomb.Carrier,
                    (id, lane) => DemolitionBotNavigation.Distance(pawns[id].Motor.transform.position, map.Route(lane).waypoints[0]));
                foreach (var order in plan.Orders) pawns[order.PlayerId].Order = order;
                Debug.Log("BOT_OPENING round=" + game.Round.Number + " side=" + (attack ? "T" : "CT") + " plan=" + plan.Name + " orders=" +
                    string.Join(";", plan.Orders.Select(o => o.PlayerId + ":" + o.Lane + ":" + o.Role + "@" + pawns[o.PlayerId].Motor.transform.position)));
            }
        }
        BattleState State(Pawn p)
        {
            var s = p.State; var weapon = p.Player.Character.Equipment.Current;
            s.Id = p.Player.Seat.Id; s.Health = p.Player.Character.Vitals.Health; s.Life = p.Player.Life;
            s.Ammo = weapon?.Ammo.Magazine ?? 0; s.Reserve = weapon?.Ammo.Reserve ?? 0;
            s.ReloadTicks = weapon != null && weapon.Ammo.IsReloading ? Math.Max(1, Mathf.CeilToInt((weapon.Ammo.ReloadDuration-weapon.Ammo.ReloadElapsed)*60)) : 0;
            s.Team = game.Teams.IsAttacker(p.Player) ? BattleTeam.T : BattleTeam.CT; s.IsBot = p.Player.Seat.IsBot; return s;
        }
        void Snapshot()
        {
            var snapshot = new DemolitionSnapshot { Tick = tick, Round = game.Round.Number, Phase = game.Round.Phase, ScoreA = game.Round.ScoreA, ScoreB = game.Round.ScoreB,
                Remaining = game.Round.Remaining, Reason = game.Round.Reason, Bomb = game.Bomb.Phase, Carrier = game.Bomb.Carrier, Site = game.Bomb.Site,
                Interactor = game.Bomb.Interactor, Progress = game.Bomb.Progress, BombPosition = game.Bomb.Position };
            foreach (var recipient in pawns.Values.Where(p => p.Peer != null))
            {
                snapshot.Actors = pawns.Values.Select(p =>
                {
                    var state = State(p); if (p != recipient) { state.Ack = 0; state.Ammo = state.Reserve = 0; }
                    return new DemolitionActorState { Motion = state, Weapon = p.Player.Character.Equipment.Current?.Info.id ?? "", Money = p == recipient ? p.Player.Money : 0,
                        Armor = p.Player.Character.Vitals.Armor, Kills = p.Player.Kills, Deaths = p.Player.Deaths, Kit = p.Player.Kit, Connected = p.Player.Connected };
                }).ToArray();
                // Ten actors exceed the initial UDP MTU; unordered reliability permits fragmentation.
                // The client discards older ticks, so stale snapshots cannot rewind presentation.
                recipient.Peer.Send(DemolitionProtocol.Snapshot(snapshot), DeliveryMethod.ReliableUnordered);
            }
        }
        void Finish(bool success)
        {
            if (completed) return; completed = true;
            var result = new DemolitionResult { MatchId = manifest.MatchId, Completed = success, HasAI = manifest.Seats.Any(s => s.IsBot), ScoreA = game.Round.ScoreA,
                ScoreB = game.Round.ScoreB, Reason = game.Round.Reason };
            File.WriteAllText(manifest.ResultFile+".tmp", JsonUtility.ToJson(result)); File.Move(manifest.ResultFile+".tmp", manifest.ResultFile);
            Debug.Log("DEMOLITION_FINISHED " + result.ScoreA + ":" + result.ScoreB); Application.Quit();
        }
        void OnDestroy() { network?.Stop(); foreach (var p in pawns.Values) p.Brain?.Dispose(); game?.Dispose(); }
    }
}
