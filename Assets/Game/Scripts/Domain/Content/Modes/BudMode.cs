using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// <b>Budburst.</b> A grove of coloured flowers with critters shut in cocoons, and a basket
    /// of pure colour dealt one per tap. Tap a flower and the colour in hand <em>mixes</em> into
    /// it — red with green in hand becomes yellow — and any bunch of three or more touching
    /// flowers of one colour bursts, washing its colour into everything it touches, which makes
    /// more bunches. That is the chain, and a cocoon beside any of it cracks open.
    ///
    /// <para>
    /// <b>The level is a grid and a basket, and no difficulty number.</b> Par is the fewest taps
    /// that free every critter, found by <see cref="BudSolver"/>, and both star lines and the tap
    /// budget derive from it — so everything a player is graded on comes out of the picture they
    /// can see.
    /// </para>
    /// <para>
    /// Par is resolved lazily (invariant 26d): a chapter body holds a grove per level and the
    /// map never asks any of them what par is.
    /// </para>
    /// </summary>
    public sealed class BudMode : LevelMode
    {
        public override GameMode Mode => GameMode.Bud;

        public override bool Claims(LevelDto dto) => dto.bud != null && dto.bud.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var grove = dto.bud;
            int width = grove.width, height = grove.height;

            if (width < BudLayout.MinWidth || width > BudLayout.MaxWidth)
            {
                problems.Add($"{id}: a grove is {BudLayout.MinWidth}..{BudLayout.MaxWidth} " +
                             $"wide; this one says {width}");
                return false;
            }

            if (height < BudLayout.MinHeight || height > BudLayout.MaxHeight)
            {
                problems.Add($"{id}: a grove is {BudLayout.MinHeight}.." +
                             $"{BudLayout.MaxHeight} tall; this one says {height}");
                return false;
            }

            if (!BudDeal.TryParse(grove.colours, out var deal, out string dealError))
            {
                problems.Add($"{id}: {dealError}");
                return false;
            }

            if (!BudLayout.TryReadRows(grove.rows, width, height,
                                       out var ground, out var value, out string error))
            {
                problems.Add($"{id}: {error}");
                return false;
            }

            var layout = new BudLayout(width, height, ground, value, deal);

            // Two refusals rather than one, because they read as completely different mistakes to
            // whoever wrote the file — and the search would report both as "this cannot be
            // finished", which is true and useless.
            if (layout.Cocoons == 0)
            {
                problems.Add($"{id}: nobody is shut in on this grove, so it is already finished");
                return false;
            }

            if (layout.Flowers == 0)
            {
                problems.Add($"{id}: this grove has no flower on it, so there is nothing to tap " +
                             "and no way to start it");
                return false;
            }

            rules = new BudRules(layout, grove.spare);
            return true;
        }

        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var grove = (BudRules)rules;
            string id = dto.id;

            return new LevelTuning(() => BudSetup.Par(id, grove.Layout),
                                   dto.goldFactor, dto.silverFactor, dto.budgetFactor,
                                   grove.Spare);
        }

        public override string RecordStem => "ui.rank.taps";
    }

    /// <summary>One grove's rules: the board it is, and how many wasted taps it forgives.</summary>
    public sealed class BudRules : ILevelRules
    {
        public readonly BudLayout Layout;

        /// <summary>
        /// Taps above par a grove forgives when it authors none of its own.
        ///
        /// <para>
        /// <b>A count, because the cost of a mistake here is a count</b> (invariant 26e). A wasted
        /// tap is one colour spent and whatever small chain it took with it — about the same wherever
        /// it happens — where a fraction of par would give a par-3 grove almost no room at all.
        /// </para>
        /// <para>
        /// Five rather than four, and the reason is arithmetic: a budget of <c>par + spare</c> has
        /// to clear <c>ceil(par × 1.40)</c> or the bottom star band is stranded and every clear is
        /// worth two stars or three (invariant 22). Four holds to par seven; five holds to ten,
        /// which is past anything <see cref="BudSolver.MaxTaps"/> lets a grove ship at.
        /// </para>
        /// </summary>
        public const int DefaultSpare = 5;

        public readonly int Spare;

        public BudRules(BudLayout layout, int spare = 0)
        {
            Layout = layout;
            Spare = spare > 0 ? spare : DefaultSpare;
        }

        public GameMode Mode => GameMode.Bud;

        public int Width => Layout.Width;
        public int Height => Layout.Height;
    }
}
