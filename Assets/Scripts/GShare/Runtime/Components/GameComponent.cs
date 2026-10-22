using System;
using System.Collections.Generic;

namespace OperationBlacktide.Shared.Components
{
    /// <summary>Engine-independent capability. The host owns its entire lifetime.</summary>
    public abstract class GameComponent : IDisposable
    {
        public ComponentHost Host { get; internal set; }
        public virtual IEnumerable<Type> Dependencies => Array.Empty<Type>();
        public virtual void Initialize() { }
        public virtual void Tick(float deltaTime) { }
        public virtual void Dispose() { }
    }
}
