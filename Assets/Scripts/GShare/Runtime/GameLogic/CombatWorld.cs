using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.GameLogic
{
    public sealed class CombatWorld : GameComponent
    {
        private readonly Dictionary<int, Character> characters = new Dictionary<int, Character>();
        private readonly Dictionary<int, Action> deathSubscriptions = new Dictionary<int, Action>();
        public IEnumerable<Character> Characters => characters.Values;
        public event Action<Character> CharacterDied;
        public event Action<Character> CharacterRemoved;
        public Character Find(int id) => characters.TryGetValue(id, out var value) ? value : null;
        public Character Add(Character character)
        {
            if (character == null || character.IsDisposed || character.World != null || characters.ContainsKey(character.Id))
                throw new ArgumentException("A live character must belong to exactly one world.");
            characters.Add(character.Id, character);
            character.World = this;
            Action onDeath = () => CharacterDied?.Invoke(character);
            deathSubscriptions.Add(character.Id, onDeath);
            character.Vitals.Died += onDeath;
            return character;
        }
        public void Remove(int id)
        {
            var character = Find(id);
            if (character == null) return;
            character.Vitals.Died -= deathSubscriptions[id]; deathSubscriptions.Remove(id);
            characters.Remove(id); character.World = null;
            try { CharacterRemoved?.Invoke(character); } finally { character.Dispose(); }
        }
        public override void Tick(float deltaTime)
        { foreach (var character in characters.Values.ToArray()) if (!character.IsDisposed && character.Vitals.IsAlive) character.Tick(deltaTime); }
        public override void Dispose() { foreach (int id in characters.Keys.ToArray()) Remove(id); }
    }
}
