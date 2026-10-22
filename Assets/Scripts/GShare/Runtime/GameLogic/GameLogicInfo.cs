using System;
using System.Collections.Generic;

namespace OperationBlacktide.Shared.GameLogic
{
    [Serializable]
    public sealed class GameLogicInfo
    {
        public int version = 1;
        public string id;
        public string[] components = { "world", "clock", "respawn", "supply" };
        public float respawnDelay = 3;
        public bool infiniteAmmoReserve, infiniteThrowables, autoRechargeTaser;

        public void Validate()
        {
            if (version != 1 || string.IsNullOrWhiteSpace(id) || components == null || float.IsNaN(respawnDelay) ||
                float.IsInfinity(respawnDelay) || respawnDelay < 0) throw new ArgumentException("Invalid game logic configuration.");
            var found = new HashSet<string>();
            foreach (string component in components)
                if ((component != "world" && component != "clock" && component != "respawn" && component != "supply") || !found.Add(component))
                    throw new ArgumentException("Unknown or duplicate gameplay component: " + component);
            if (!found.Contains("world") || !found.Contains("clock")) throw new ArgumentException("GameLogic requires world and clock components.");
        }
    }
}
