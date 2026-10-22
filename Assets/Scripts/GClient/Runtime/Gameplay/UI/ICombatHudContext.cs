using OperationBlacktide.Client.Gameplay.Characters;
using OperationBlacktide.Client.Gameplay.Combat;
using System.Collections.Generic;

namespace OperationBlacktide.Client.Gameplay.UI
{
    /// <summary>Mode-owned state and menu action consumed by the shared combat HUD.</summary>
    public interface ICombatHudContext
    {
        CharacterMotor Player { get; }
        CharacterVitals Vitals { get; }
        bool IsInputBlocked { get; }
        float ElapsedSeconds { get; }
        string StatusText { get; }
        int LocalHudId { get; }
        IReadOnlyList<CombatHudParticipant> HudParticipants { get; }
        void OpenMenu();
    }
}
