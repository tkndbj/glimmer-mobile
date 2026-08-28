using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Groovekeeper: tiles of light laid out so that unlike edges bloom, and beds that have to be
    /// bloomed before the grove is finished.
    ///
    /// <para>
    /// <b>It authors a board and nothing that can be graded.</b> A level says how big the ground
    /// is, what stands on it, which cells are beds and what the basket deals; par is the fewest
    /// tiles that open every bed, found by search, and both star lines and the basket all fall out
    /// of par. So there is no number in a level file that can come to disagree with how the level
    /// actually plays — the same reason a glade omits its par.
    /// </para>
    /// <para>
    /// <b>This replaced a score attack, and the difference is the whole feature.</b> The mode used
    /// to deal random colours onto empty ground until the tiles ran out: no goal, no ending worth
    /// reaching, nothing a chapter could ramp, and a board that could not be validated because it
    /// had no fixed future. What it shipped was <c>LevelTuning.Default(1)</c> — a par of one that
    /// nothing read — and two players on the same "level" were not playing the same board. That is
    /// invariant 26's fault exactly, found for the second time, and the answer is the same one:
    /// author the board, author the procession, and search for everything else.
    /// </para>
    /// </summary>
    public sealed class KeeperMode : LevelMode
    {
        public override GameMode Mode => GameMode.Keeper;

        public override bool Claims(LevelDto dto) => dto.keeper != null && dto.keeper.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var grove = dto.keeper;
            int width = grove.width, height = grove.height;

            if (width < KeeperLayout.MinWidth || width > KeeperLayout.MaxWidth)
            {
                problems.Add($"{id}: a grove is {KeeperLayout.MinWidth}..{KeeperLayout.MaxWidth} " +
                             $"wide; this one says {width}");
                return false;
            }

            if (height < KeeperLayout.MinHeight || height > KeeperLayout.MaxHeight)
            {
                problems.Add($"{id}: a grove is {KeeperLayout.MinHeight}.." +
                             $"{KeeperLayout.MaxHeight} tall; this one says {height}");
                return false;
            }

            if (!KeeperDeal.TryParse(grove.tiles, out var deal, out string dealError))
            {
                problems.Add($"{id}: {dealError}");
                return false;
            }

            if (!KeeperLayout.TryReadRows(grove.rows, width, height,
                                          out var ground, out var wants, out var sprigs,
                                          out string groundError))
            {
                problems.Add($"{id}: {groundError}");
                return false;
            }

            var layout = new KeeperLayout(width, height, ground, wants, sprigs, deal);

            if (layout.Sprigs == 0)
            {
                problems.Add($"{id}: this grove has no sprig, so there is nothing to lay the " +
                             "first tile beside and no way to start it");
                return false;
            }

            if (layout.Beds == 0)
            {
                problems.Add($"{id}: this grove has no bed, so it is already finished");
                return false;
            }

            rules = new KeeperRules(layout, grove.spare);
            return true;
        }

        /// <summary>
        /// Par is searched and everything else derives from it.
        ///
        /// <para>
        /// <b>The failure case is answered generously and loudly, not silently.</b> A grove the
        /// search cannot prove is content the build gate is supposed to have refused, so reaching
        /// here means an authoring bug has shipped. The safe direction is the one that cannot
        /// cheat a player: par falls back to the room the ground has, which puts both star lines
        /// and the basket above it — so the level is winnable and generously graded rather than
        /// unwinnable and correctly graded. <see cref="KeeperSetup"/> logs the id.
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var grove = (KeeperRules)rules;
            string id = dto.id;

            // Handed over as a search rather than run here, for FallMode's reason: a chapter body
            // holds ten groves and this is a search rather than a walk, so running all ten while
            // the map is opening would be a hitch on a screen that never asks the question. See
            // LevelTuning.Par.
            return new LevelTuning(() => KeeperSetup.Par(id, grove.Layout),
                                   dto.goldFactor, dto.silverFactor, dto.budgetFactor,
                                   grove.Spare);
        }

        /// <summary>A Groovekeeper record is a count of tiles spent.</summary>
        public override string RecordStem => "ui.rank.tiles";
    }

    /// <summary>A grove: its ground, its beds, what it deals, and its room to err.</summary>
    public sealed class KeeperRules : ILevelRules
    {
        public readonly KeeperLayout Layout;

        /// <summary>
        /// Wasted tiles this grove forgives, above par.
        ///
        /// <para>
        /// <b>Five, which is two mistakes and a little.</b> A wrong tile costs one from the basket
        /// <em>and</em> takes a cell of ground that a bed beside it may have needed, so a mistake
        /// here is worth about two tiles rather than one — Lightfall's arithmetic exactly, for the
        /// same shape of mistake (see <c>LevelTuning.Slack</c>). Two is the right number to
        /// forgive on a board where nothing is hidden: the ground, the procession and the colour
        /// every tile is still waiting for are all drawn, and the ghost under a thumb says what a
        /// cell would open before anything is committed, so what kills a run is a misjudgement
        /// rather than a surprise.
        /// </para>
        /// <para>
        /// <b>The fifth is not generosity, it is the two-star line.</b> A budget of
        /// <c>par + spare</c> has to clear <c>ceil(par × 1.40)</c> or the bottom band is stranded
        /// and every clear is worth two stars or three — invariant 22's fault, arrived at from the
        /// budget's side rather than the star line's. Four works up to par seven and collides at
        /// par eight, which is exactly where this chapter's finale sits; it was caught by
        /// <c>CheckStarBands</c> rather than by anybody noticing. Five holds to par twelve, and
        /// the check is what says so if a later chapter ever goes deeper.
        /// </para>
        /// <para>
        /// It is the same on the second grove and the tenth, deliberately. The budget is a fail
        /// line and difficulty is the boards' job (invariant 5d) — a per-chapter ramp on the fail
        /// line was tried on the glades and removed for exactly that reason.
        /// </para>
        /// </summary>
        public const int DefaultSpare = 5;

        public readonly int Spare;

        public KeeperRules(KeeperLayout layout, int spare = 0)
        {
            Layout = layout;
            Spare = spare > 0 ? spare : DefaultSpare;
        }

        public GameMode Mode => GameMode.Keeper;

        public int Width => Layout.Width;
        public int Height => Layout.Height;
    }
}
