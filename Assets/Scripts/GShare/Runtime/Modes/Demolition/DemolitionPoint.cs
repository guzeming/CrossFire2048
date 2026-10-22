using System;
using System.Collections.Generic;
using System.Linq;
using OperationBlacktide.Shared.Characters;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.Components;
using OperationBlacktide.Shared.GameLogic;

namespace OperationBlacktide.Shared.Modes.Demolition
{
    public struct DemolitionPoint
    {
        public float X, Y, Z;
        public DemolitionPoint(float x, float y, float z) { X = x; Y = y; Z = z; }
        public float DistanceSquared(DemolitionPoint b) => (X-b.X)*(X-b.X) + (Y-b.Y)*(Y-b.Y) + (Z-b.Z)*(Z-b.Z);
    }
}
