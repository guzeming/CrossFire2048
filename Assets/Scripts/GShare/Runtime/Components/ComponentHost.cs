using System;
using System.Collections.Generic;
using System.Linq;

namespace OperationBlacktide.Shared.Components
{
    /// <summary>Composition is frozen at Start; dependencies determine initialization/tick order.</summary>
    public class ComponentHost : IDisposable
    {
        private readonly List<GameComponent> components = new List<GameComponent>();
        private readonly List<GameComponent> ordered = new List<GameComponent>();
        public IReadOnlyList<GameComponent> Components => components.AsReadOnly();
        public bool Started { get; private set; }
        public bool IsDisposed { get; private set; }

        public T Add<T>(T component) where T : GameComponent
        {
            if (IsDisposed) throw new ObjectDisposedException(GetType().Name);
            if (Started) throw new InvalidOperationException("Composition is already started.");
            if (component == null || component.Host != null) throw new ArgumentException("Component must have exactly one owner.");
            if (components.Any(c => c.GetType() == component.GetType())) throw new InvalidOperationException("Duplicate component: " + component.GetType().Name);
            component.Host = this;
            components.Add(component);
            return component;
        }

        public T Get<T>() where T : GameComponent => components.OfType<T>().SingleOrDefault();

        public void Start()
        {
            if (IsDisposed) throw new ObjectDisposedException(GetType().Name);
            if (Started) return;
            ordered.Clear();
            var visiting = new HashSet<GameComponent>();
            foreach (var component in components) Visit(component, visiting);
            Started = true;
            int initialized = 0;
            try { for (; initialized < ordered.Count; initialized++) ordered[initialized].Initialize(); }
            catch (Exception initializationError)
            {
                // Dispose the failing component as well as every successfully initialized dependency.
                IsDisposed = true;
                var errors = new List<Exception> { initializationError };
                for (int i = Math.Min(initialized, ordered.Count - 1); i >= 0; i--)
                    try { ordered[i].Dispose(); } catch (Exception cleanupError) { errors.Add(cleanupError); }
                if (errors.Count > 1) throw new AggregateException(errors);
                throw;
            }
        }

        private void Visit(GameComponent component, HashSet<GameComponent> visiting)
        {
            if (ordered.Contains(component)) return;
            if (!visiting.Add(component)) throw new InvalidOperationException("Cyclic component dependency: " + component.GetType().Name);
            foreach (Type type in component.Dependencies)
            {
                var matches = components.Where(type.IsInstanceOfType).ToArray();
                if (matches.Length != 1) throw new InvalidOperationException("Expected one dependency: " + type.Name);
                Visit(matches[0], visiting);
            }
            visiting.Remove(component);
            ordered.Add(component);
        }

        public virtual void Tick(float deltaTime)
        {
            if (IsDisposed) throw new ObjectDisposedException(GetType().Name);
            if (!Started) throw new InvalidOperationException("Start the host before ticking.");
            if (float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            foreach (var component in ordered) component.Tick(deltaTime);
        }

        public virtual void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            var owned = Started ? ordered : components;
            List<Exception> errors = null;
            for (int i = owned.Count - 1; i >= 0; i--)
                try { owned[i].Dispose(); } catch (Exception e) { (errors ?? (errors = new List<Exception>())).Add(e); }
            if (errors != null) throw new AggregateException(errors);
        }
    }
}
