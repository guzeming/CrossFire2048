using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Components;

namespace OperationBlacktide.Shared.GameLogic
{
    public sealed class SupplyComponent : GameComponent
    {
        public bool InfiniteAmmoReserve { get; }
        public bool InfiniteThrowables { get; }
        public bool AutoRechargeTaser { get; }
        public SupplyComponent(GameLogicInfo info)
        { InfiniteAmmoReserve = info.infiniteAmmoReserve; InfiniteThrowables = info.infiniteThrowables; AutoRechargeTaser = info.autoRechargeTaser; }
    }
}
