using System;
using System.Linq;

namespace OperationBlacktide.Shared.AI
{
    public enum OpeningLane { LongA, ShortA, UpperB, MidB, GuardLongA, GuardShortA, GuardBPlatform, GuardBDoors, GuardMid }
    public enum OpeningRole { Entry, Support, BombCarrier, Anchor, Rotator }
    public sealed class OpeningOrder
    {
        public int PlayerId;
        public OpeningLane Lane;
        public OpeningRole Role;
        public float Delay;
    }
    public sealed class TeamOpeningPlan
    {
        public string Name;
        public int Site;
        public OpeningOrder[] Orders;
    }

    /// <summary>Round plans use spawn travel costs and team roles, never hidden opponent positions.</summary>
    public static class DemolitionTactics
    {
        static readonly OpeningLane[][] Attacks = {
            new[] { OpeningLane.LongA, OpeningLane.LongA, OpeningLane.LongA, OpeningLane.ShortA, OpeningLane.ShortA },
            new[] { OpeningLane.UpperB, OpeningLane.UpperB, OpeningLane.UpperB, OpeningLane.MidB, OpeningLane.MidB },
            new[] { OpeningLane.ShortA, OpeningLane.ShortA, OpeningLane.ShortA, OpeningLane.LongA, OpeningLane.LongA },
            new[] { OpeningLane.UpperB, OpeningLane.UpperB, OpeningLane.UpperB, OpeningLane.UpperB, OpeningLane.MidB }
        };
        static readonly string[] Names = { "A long split", "B tunnel split", "A short split", "B tunnel rush" };
        static readonly OpeningLane[] Defense = { OpeningLane.GuardLongA, OpeningLane.GuardShortA,
            OpeningLane.GuardBPlatform, OpeningLane.GuardBDoors, OpeningLane.GuardMid };

        public static int Seed(string match)
        {
            uint hash = 2166136261;
            foreach (char c in match ?? "") hash = unchecked((hash ^ c) * 16777619);
            return (int)(hash & 0x7fffffff);
        }

        public static int[] SpawnSlots(int count, int round, int seed)
        {
            if (count < 5) throw new ArgumentException("Five team spawns are required.");
            var slots = Enumerable.Range(0, count).ToArray();
            uint state = unchecked((uint)seed ^ (uint)(round * 747796405)) | 1;
            for (int i = count - 1; i > 0; i--)
            {
                state ^= state << 13; state ^= state >> 17; state ^= state << 5;
                int j = (int)(state % (i + 1)); int value = slots[i]; slots[i] = slots[j]; slots[j] = value;
            }
            return slots.Take(5).ToArray();
        }

        public static TeamOpeningPlan Plan(bool attack, int round, int seed, int[] playerIds, int carrier,
            Func<int, OpeningLane, float> travelDistance)
        {
            if (playerIds == null || playerIds.Length != 5 || playerIds.Distinct().Count() != 5)
                throw new ArgumentException("Opening plans require five unique teammates.");
            int preferred = (int)(((uint)seed + (uint)Math.Max(0, round - 1)) % Attacks.Length);
            float best = float.PositiveInfinity; OpeningLane[] chosen = null; int[] assignment = null; int layout = 0;
            for (int option = 0; option < (attack ? Attacks.Length : 1); option++)
            {
                var lanes = attack ? Attacks[option] : Defense;
                var costs = new float[5, 5];
                for (int i = 0; i < 5; i++) for (int j = 0; j < 5; j++)
                    costs[i, j] = attack && playerIds[i] == carrier && lanes[j] != lanes[0]
                        ? float.PositiveInfinity : travelDistance(playerIds[i], lanes[j]);
                float value = float.PositiveInfinity; int[] placed = null;
                Assign(costs, 0, 0, 0, new int[5], ref value, ref placed);
                // Compare mean arrival distance. A modest round preference prevents always picking
                // the geometrically shortest lane, while a large spawn disadvantage still overrides it.
                float score = value / playerIds.Length + (attack && option != preferred ? 10 : 0);
                if (score >= best) continue;
                best = score; chosen = lanes; assignment = placed; layout = option;
            }
            if (assignment == null) throw new InvalidOperationException("No reachable opening plan for these spawns.");
            var orders = playerIds.Select((id, i) => new OpeningOrder { PlayerId = id, Lane = chosen[assignment[i]],
                Role = attack ? id == carrier ? OpeningRole.BombCarrier : OpeningRole.Support :
                    chosen[assignment[i]] == OpeningLane.GuardMid ? OpeningRole.Rotator : OpeningRole.Anchor }).ToArray();
            foreach (var group in orders.GroupBy(o => o.Lane))
            {
                var line = group.OrderBy(o => o.PlayerId == carrier ? 1 : 0)
                    .ThenBy(o => travelDistance(o.PlayerId, o.Lane)).ThenBy(o => o.PlayerId).ToArray();
                for (int i = 0; i < line.Length; i++)
                {
                    line[i].Delay = i * .45f;
                    if (attack && i == 0 && line[i].PlayerId != carrier) line[i].Role = OpeningRole.Entry;
                }
            }
            return new TeamOpeningPlan { Name = attack ? Names[layout] : "2A / 2B / mid", Site = attack ? layout % 2 : -1, Orders = orders };
        }

        static void Assign(float[,] costs, int row, int used, float total, int[] current, ref float best, ref int[] result)
        {
            if (total >= best) return;
            if (row == 5) { best = total; result = (int[])current.Clone(); return; }
            for (int slot = 0; slot < 5; slot++)
            {
                if ((used & (1 << slot)) != 0) continue;
                float cost = costs[row, slot]; if (float.IsNaN(cost) || float.IsInfinity(cost) || cost < 0) continue;
                current[row] = slot; Assign(costs, row + 1, used | (1 << slot), total + cost, current, ref best, ref result);
            }
        }
    }
}
