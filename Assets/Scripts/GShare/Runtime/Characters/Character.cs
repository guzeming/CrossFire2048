using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.Characters
{
    public sealed class Character : ComponentHost
    {
        public int Id { get; }
        public GameLogic.CombatWorld World { get; internal set; }
        public int Team { get; set; }
        public VitalsComponent Vitals { get; }
        public EquipmentComponent Equipment { get; }
        public MovementComponent Movement { get; }
        public Character(int id, int team = 0, int maxHealth = 100, int maxArmor = 100, params GameComponent[] extensions)
        {
            Id = id; Team = team;
            Vitals = Add(new VitalsComponent(maxHealth, maxArmor));
            Equipment = Add(new EquipmentComponent());
            Movement = Add(new MovementComponent());
            if (extensions != null) foreach (var extension in extensions) Add(extension);
            Start();
        }
    }
}
