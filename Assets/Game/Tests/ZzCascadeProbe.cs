using System.Collections.Generic;
using System.Text;
using UnityEngine;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>TEMPORARY probe: what does one match really clear, and how deep does it chain?</summary>
    public sealed partial class SiegeRuleTests
    {
        static readonly List<string> Report = new List<string>();

        static void Probe(string name, Rung[] chapter)
        {
            float[] rhythms = { 2.25f, 2.40f, 2.55f };

            var depths = new Dictionary<int, int>();
            int swaps = 0, gems = 0, deepest = 0;

            for (int i = 0; i < chapter.Length; i++)
            {
                var one = new Dictionary<int, int>();
                int s1 = 0, g1 = 0, d1 = 0;

                foreach (float rhythm in rhythms)
                {
                    var board = SiegeBoard.Build(chapter[i].Built(), Bare());
                    ProbeHold(board, rhythm, depths, ref swaps, ref gems, ref deepest);
                    var b2 = SiegeBoard.Build(chapter[i].Built(), Bare());
                    ProbeHold(b2, rhythm, one, ref s1, ref g1, ref d1);
                }

                int free = 0;
                foreach (var kv in one) if (kv.Key > 1) free += kv.Value * (kv.Key - 1);
                Report.Add($"   {chapter[i].Id,-18} {chapter[i].Gems,-4} {s1,4} matches  "
                           + $"{(float)g1 / s1:0.00} gems each  chain mean "
                           + $"{1f + (float)free / s1:0.00}  deepest x{d1}");
            }

            var sb = new StringBuilder();
            sb.Append($"{name,-12} {swaps,5} matches, {(float)gems / swaps:0.00} gems each, deepest chain x{deepest}   ");
            for (int d = 1; d <= 12; d++)
            {
                depths.TryGetValue(d, out int n);
                if (n > 0) sb.Append($"x{d}:{100f * n / swaps:0}% ");
            }
            int tail = 0;
            foreach (var kv in depths) if (kv.Key > 12) tail += kv.Value;
            if (tail > 0) sb.Append($"x13+:{100f * tail / swaps:0}% ");

            Report.Add(sb.ToString());
        }

        static void ProbeHold(SiegeBoard board, float rhythm, Dictionary<int, int> depths,
                              ref int swaps, ref int gems, ref int deepest)
        {
            const float Frame = 1f / 60f;
            float since = rhythm;

            for (int i = 0; i < 60 * 600; i++)
            {
                board.Advance(Frame);
                if (board.IsFinished || board.Stranded) break;

                for (int fuse = board.Bombs.Count - 1; fuse >= 0; fuse--)
                    board.Detonate(board.Bombs[fuse].Id, null);

                for (int loot = board.Cogs.Count - 1; loot >= 0; loot--)
                    board.Take(board.Cogs[loot].Id);

                for (int w = 0; w < board.Wards.Count; w++)
                    if (board.Wards[w].Armed) board.Overcharge(w, null);

                since += Frame;
                if (since < rhythm) continue;

                if (Buried(board, out int under))
                {
                    while (board.Dig(under)) { }
                    since = 0f;
                    continue;
                }

                if (!Aimed(board, out int a, out int b)) continue;

                var turn = board.Swap(a, b);
                since = 0f;
                if (turn == null) continue;

                swaps++;
                gems += turn.Worth;

                int depth = turn.Beats.Count;
                if (depth > deepest) deepest = depth;
                depths.TryGetValue(depth, out int n);
                depths[depth] = n + 1;
            }
        }

        [Test]
        public void ZzCascadeProbe()
        {
            Probe("Thornwatch", Chapter);
            Probe("Broodmarch", Broodmarch);
            Probe("Barrowfell", Barrowfell);
            Probe("Ashenhold", Ashenhold);
            Probe("Thundercrag", Thundercrag);
            Probe("Dustcrown", Dustcrown);
            Assert.Fail(string.Join(System.Environment.NewLine, Report.ToArray()));
        }
    }
}
