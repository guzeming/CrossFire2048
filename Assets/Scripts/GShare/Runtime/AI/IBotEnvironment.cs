using System;
using OperationBlacktide.Shared.Battle;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.AI
{
    public interface IBotEnvironment
    {
        bool IsOccluded(WorldVector origin, WorldVector direction, float distance);
        WorldVector[] FindPath(WorldVector start, WorldVector goal);
    }
}
