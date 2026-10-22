using System.Text.Json;
using OperationBlacktide.Shared.AI;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

static class Program
{
    static int count;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); count++; }
    static void Reject(Action action, string message)
    { try { action(); } catch (ArgumentException) { count++; return; } catch (InvalidOperationException) { count++; return; } throw new Exception(message); }
    static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    static WeaponInfo Info(string id = "test") => new() { id = id, cycleTime = .1f, magazineSize = 3, reserveAmmo = 2, reloadDuration = 1, range = 80, damage = 25, rangeModifier = 1 };

    static void Main(string[] args)
    {
        DemolitionTacticsValidation.Run(Check);
        string root = Path.GetFullPath(args.Length == 0 ? "." : args[0]);
        var info = JsonSerializer.Deserialize<GameLogicInfo>(File.ReadAllText(Path.Combine(root, "GConfig/Modes/battle.json")), Json);
        var training = JsonSerializer.Deserialize<GameLogicInfo>(File.ReadAllText(Path.Combine(root, "GConfig/Modes/training.json")), Json);
        info.Validate(); training.Validate();
        var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "GConfig/Weapons.json")));
        var ids = new HashSet<string>();
        foreach (var item in data.RootElement.GetProperty("weapons").EnumerateArray())
        { var weapon = item.Deserialize<WeaponInfo>(Json); weapon.Validate(); Check(ids.Add(weapon.id), "Duplicate weapon ID"); }
        var battle = JsonSerializer.Deserialize<WeaponInfo>(File.ReadAllText(Path.Combine(root, "GConfig/BattleWeapon.json")), Json); battle.Validate();
        Check(battle.magazineSize == BattleProtocol.Capacity && battle.reserveAmmo == BattleProtocol.Reserve, "Battle defaults migrated unexpectedly");
        Reject(() => new Weapon("bad", new WeaponInfo { id = "bad", cycleTime = float.NaN }), "Nonfinite weapon accepted");
        Reject(() => new GameLogic(new GameLogicInfo { id = "bad", components = new[] { "world", "clock", "clock" } }, true), "Duplicate gameplay component accepted");

        using (var host = new ComponentHost())
        {
            var trace = new List<string>(); host.Add(new Dependent(trace)); host.Add(new Prerequisite(trace)); host.Start(); host.Tick(.1f); host.Dispose();
            Check(string.Join(",", trace) == "base+,child+,baseT,childT,child-,base-", "Dependency/lifetime ordering changed");
        }
        using (var host = new ComponentHost()) { host.Add(new Dependent(new List<string>())); Reject(host.Start, "Missing dependency accepted"); }
        using (var host = new ComponentHost()) { host.Add(new CycleA()); host.Add(new CycleB()); Reject(host.Start, "Cycle accepted"); }
        using (var host = new ComponentHost())
        {
            var trace = new List<string>(); host.Add(new Prerequisite(trace)); host.Add(new FailingComponent());
            try { host.Start(); throw new Exception("Initialization failure ignored"); }
            catch (AggregateException error) { Check(error.InnerExceptions.Count == 2 && trace.Last() == "base-" && host.IsDisposed, "Failed initialization leaked dependencies"); }
        }

        using (var inventory = new EquipmentOwner())
        {
            var first = new Weapon("one", Info()); var second = new Weapon("two", Info());
            inventory.Equipment.AddWeapon(first); inventory.Equipment.AddWeapon(second);
            using (var other = new EquipmentOwner()) Reject(() => other.Equipment.AddWeapon(first), "Weapon owned by two inventories");
            inventory.Equipment.Equip("one"); Check(first.TryFire(0), "Initial shot denied");
            Check(!first.TryFire(.01) && first.Ammo.Magazine == 2, "Cadence consumed ammo");
            inventory.Equipment.Equip("two"); Check(second.Ammo.Magazine == 3, "Same-type weapons share ammo");
            inventory.Equipment.Equip("one"); Check(first.Ammo.Magazine == 2, "Switching reset ammo");
            Check(first.Ammo.TryReload(), "Reload denied"); inventory.Tick(1);
            Check(first.Ammo.Magazine == 3 && first.Ammo.Reserve == 1, "Finite reload incorrect");
            var transferred = inventory.Equipment.Remove("one");
            using var receiver = new EquipmentOwner(); receiver.Equipment.AddWeapon(transferred); receiver.Equipment.Equip("one");
            Check(receiver.Equipment.Current.Ammo.Reserve == 1, "Pickup lost weapon state");
        }
        using (var server = new GameLogic(info, true))
        using (var otherRoom = new GameLogic(info, true))
        using (var replica = new GameLogic(info, false))
        {
            var a = server.World.Add(new Character(1)); var b = otherRoom.World.Add(new Character(1)); var remote = replica.World.Add(new Character(1));
            Reject(() => replica.World.Add(b), "Character belongs to two worlds");
            int respawns = 0, replicaRespawns = 0;
            server.Respawn.Requested += c => { respawns++; c.Vitals.Restore(); };
            replica.Respawn.Requested += c => replicaRespawns++;
            a.Vitals.ApplyDamage(100); remote.Vitals.ApplyDamage(100);
            server.Tick(2.9f); replica.Tick(4);
            Check(respawns == 0 && b.Vitals.Health == 100 && replicaRespawns == 0, "Room/authority isolation failed");
            server.Tick(.11f); Check(respawns == 1 && a.Vitals.Health == 100, "Delayed respawn failed");
            a.Vitals.ApplyDamage(100); server.World.Remove(1); server.Tick(4); Check(respawns == 1, "Removed character respawned");
        }
        using (var mode = new GameLogic(training, true))
        {
            var player = mode.World.Add(new Character(1)); player.Equipment.AddWeapon(new Weapon("gun", Info(), mode.Supply.InfiniteAmmoReserve));
            var gun = player.Equipment.Equip("gun");
            gun.Ammo.TryConsume(); gun.Ammo.TryReload(); mode.Tick(1);
            Check(gun.Ammo.Magazine == 3 && gun.Ammo.Reserve == 2, "Training supply policy leaked or reload failed");
        }
        using (var mode = new GameLogic(new GameLogicInfo { id = "minimal", components = new[] { "world", "clock" } }, true))
        {
            var player = mode.World.Add(new Character(1)); player.Vitals.ApplyDamage(100); mode.Tick(10);
            Check(mode.Respawn == null && mode.Supply == null && !player.Vitals.IsAlive, "Optional capabilities were implicitly re-enabled");
        }
        using (var ai = new AIController(1, new[] { new WorldVector(0, 0, 5) }, new FakeWorld()))
        {
            var self = new BattleState { Id = 1, Health = 100, Ammo = 30, Reserve = 90, Team = BattleTeam.CT };
            var enemy = new BattleState { Id = 2, Health = 100, Z = 10, Team = BattleTeam.T };
            ai.Decision.Reset(default, 0);
            var early = ai.Decision.Think(self, new[] { enemy }, 0);
            Check(!early.Has(InputButtons.Fire) && ai.Decision.TargetId == 2, "AI skipped reaction delay");
            var late = ai.Decision.Think(self, new[] { enemy }, 55);
            Check(late.Has(InputButtons.Fire), "AI cannot shoot without an engine");
            self.Ammo = 0; Check(ai.Decision.Think(self, new[] { enemy }, 56).Has(InputButtons.Reload), "AI did not reload");
            enemy.Team = self.Team; ai.Decision.Reset(default, 60); ai.Decision.Think(self, new[] { enemy }, 60);
            Check(ai.Decision.TargetId == 0, "AI targeted teammate");
        }
        var move = new MovementComponent(); float vertical = 0;
        var velocity = move.Velocity(1, 1, 0, false, true, true, ref vertical, 1f / 60);
        Check(Math.Abs(Math.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z) - 4.5) < .001 && velocity.y > 0, "Portable diagonal/jump rules failed");
        Console.WriteLine("CORE_VERIFY_PASS checks=" + count + "; shared rules/configs/lifetimes/authority/AI, no Unity runtime");
    }
    sealed class EquipmentOwner : ComponentHost
    { public EquipmentComponent Equipment; public EquipmentOwner() { Equipment = Add(new EquipmentComponent()); Start(); } }
    sealed class Prerequisite : GameComponent
    {
        readonly List<string> trace; public Prerequisite(List<string> trace) { this.trace = trace; }
        public override void Initialize() => trace.Add("base+"); public override void Tick(float dt) => trace.Add("baseT"); public override void Dispose() => trace.Add("base-");
    }
    sealed class Dependent : GameComponent
    {
        readonly List<string> trace; public Dependent(List<string> trace) { this.trace = trace; }
        public override IEnumerable<Type> Dependencies => new[] { typeof(Prerequisite) };
        public override void Initialize() => trace.Add("child+"); public override void Tick(float dt) => trace.Add("childT"); public override void Dispose() => trace.Add("child-");
    }
    sealed class CycleA : GameComponent { public override IEnumerable<Type> Dependencies => new[] { typeof(CycleB) }; }
    sealed class FailingComponent : GameComponent
    {
        public override IEnumerable<Type> Dependencies => new[] { typeof(Prerequisite) };
        public override void Initialize() => throw new InvalidOperationException("Initialization");
        public override void Dispose() => throw new InvalidOperationException("Cleanup");
    }
    sealed class CycleB : GameComponent { public override IEnumerable<Type> Dependencies => new[] { typeof(CycleA) }; }
    sealed class FakeWorld : IBotEnvironment
    { public bool IsOccluded(WorldVector a, WorldVector b, float d) => false; public WorldVector[] FindPath(WorldVector a, WorldVector b) => new[] { a, b }; }
}
