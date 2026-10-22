using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.GameLogic
{
    /// <summary>One match and one authoritative clock. Composition comes from a validated preset.</summary>
    public sealed class GameLogic : ComponentHost
    {
        public string Id { get; }
        public bool IsAuthority { get; }
        public CombatWorld World => Get<CombatWorld>();
        public MatchClockComponent Clock => Get<MatchClockComponent>();
        public RespawnComponent Respawn => Get<RespawnComponent>();
        public SupplyComponent Supply => Get<SupplyComponent>();
        public GameLogic(GameLogicInfo info, bool authority, params GameComponent[] extensions)
        {
            info.Validate(); Id = info.id; IsAuthority = authority;
            foreach (string component in info.components)
                switch (component)
                {
                    case "world": Add(new CombatWorld()); break;
                    case "clock": Add(new MatchClockComponent()); break;
                    case "respawn": Add(new RespawnComponent(info.respawnDelay, authority)); break;
                    case "supply": Add(new SupplyComponent(info)); break;
                }
            // Mode-specific capabilities compose before the host freezes its lifecycle graph.
            if (extensions != null) foreach (var extension in extensions) Add(extension);
            Start();
        }
    }
}
