using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.GameLogic
{
    public sealed class RespawnComponent : GameComponent
    {
        private readonly float delay;
        private readonly bool authority;
        private readonly Dictionary<int, double> pending = new Dictionary<int, double>();
        private CombatWorld world;
        private MatchClockComponent clock;
        public override IEnumerable<Type> Dependencies => new[] { typeof(CombatWorld), typeof(MatchClockComponent) };
        public event Action<Character> Requested;
        public RespawnComponent(float delay, bool authority) { this.delay = delay; this.authority = authority; }
        public override void Initialize()
        {
            world = Host.Get<CombatWorld>(); clock = Host.Get<MatchClockComponent>();
            world.CharacterDied += OnDeath; world.CharacterRemoved += OnRemoved;
        }
        private void OnDeath(Character character) { if (authority) pending[character.Id] = clock.ElapsedSeconds + delay; }
        private void OnRemoved(Character character) { pending.Remove(character.Id); }
        public override void Tick(float deltaTime)
        {
            foreach (int id in pending.Where(p => p.Value <= clock.ElapsedSeconds + .000001).Select(p => p.Key).ToArray())
            {
                pending.Remove(id);
                var character = world.Find(id);
                if (character != null && !character.Vitals.IsAlive) Requested?.Invoke(character);
            }
        }
        public override void Dispose()
        {
            if (world != null) { world.CharacterDied -= OnDeath; world.CharacterRemoved -= OnRemoved; }
            pending.Clear(); Requested = null;
        }
    }
}
