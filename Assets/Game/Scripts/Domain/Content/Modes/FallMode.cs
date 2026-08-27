using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Lightfall: a well of motes that has to be emptied, and an ordered procession to empty it
    /// with.
    ///
    /// <para>
    /// <b>It authors a board and nothing that can be graded.</b> A level says how big the well
    /// is, what is standing in it and what it deals; par is the fewest drops that empty it,
    /// found by search, and the two star lines and the supply all fall out of par. So there is
    /// no number in a level file that can come to disagree with how the level actually plays —
    /// the same reason a glade omits its par, and the same reason it matters more here: a
    /// falling-piece board looks perfectly authored whether or not anybody can finish it.
    /// </para>
    /// <para>
    /// <b>This replaced a score attack, and the difference is the whole feature.</b> The mode
    /// used to deal random colours into an empty well until a column filled up: no goal, no
    /// ending worth reaching, nothing a chapter could ramp, and a board that could not be
    /// validated because it had no fixed future. What a level is now is a puzzle with one
    /// answer's worth of slack in it.
    /// </para>
    /// </summary>
    public sealed class FallMode : LevelMode
    {
        public override GameMode Mode => GameMode.Fall;

        public override bool Claims(LevelDto dto) => dto.fall != null && dto.fall.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            var well = dto.fall;
            int width = well.width, height = well.height;

            if (width < FallLayout.MinWidth || width > FallLayout.MaxWidth)
            {
                problems.Add($"{id}: a well is {FallLayout.MinWidth}..{FallLayout.MaxWidth} " +
                             $"wide; this one says {width}");
                return false;
            }

            if (height < FallLayout.MinHeight || height > FallLayout.MaxHeight)
            {
                problems.Add($"{id}: a well is {FallLayout.MinHeight}..{FallLayout.MaxHeight} " +
                             $"tall; this one says {height}");
                return false;
            }

            if (!FallDeal.TryParse(well.motes, out var deal, out string dealError))
            {
                problems.Add($"{id}: {dealError}");
                return false;
            }

            if (!FallLayout.TryReadRows(well.rows, width, height, out var fill, out string fillError))
            {
                problems.Add($"{id}: {fillError}");
                return false;
            }

            var layout = new FallLayout(width, height, fill, deal);

            if (layout.Motes == 0)
            {
                problems.Add($"{id}: an empty well is already won");
                return false;
            }

            rules = new FallRules(layout, well.spare);
            return true;
        }

        /// <summary>
        /// Par is searched and everything else derives from it.
        ///
        /// <para>
        /// <b>The failure case is answered generously and loudly, not silently.</b> A well the
        /// search cannot prove is content the build gate is supposed to have refused, so
        /// reaching here means an authoring bug has shipped. The safe direction is the one that
        /// cannot cheat a player: par falls back to the procession's own length, which puts both
        /// star lines and the supply above it — so the level is winnable and generously graded
        /// rather than unwinnable and correctly graded. <c>FallSetup</c> logs the id.
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var well = (FallRules)rules;
            string id = dto.id;

            // Handed over as a search rather than run here, and that is worth a line. A chapter
            // body holds ten wells and this is the one mode whose par costs milliseconds rather
            // than microseconds - so running all ten while the map is opening would be a hitch
            // on a screen that never asks the question. See LevelTuning.Par.
            return new LevelTuning(() => FallSetup.Par(id, well.Layout),
                                   dto.goldFactor, dto.silverFactor, dto.budgetFactor,
                                   well.Spare);
        }

        /// <summary>A Lightfall record is a count of motes dropped.</summary>
        public override string RecordStem => "ui.rank.motes";
    }

    /// <summary>A well: its size, what is standing in it, what it deals, and its room to err.</summary>
    public sealed class FallRules : ILevelRules
    {
        public readonly FallLayout Layout;

        /// <summary>
        /// Wasted drops this well forgives, above par.
        ///
        /// <para>
        /// <b>Five, which is two mistakes and a little.</b> A wrong drop costs one from the
        /// supply and leaves a pure mote in the well that has to be cooked to white like
        /// everything else, so a mistake is about two drops rather than one. Two is the right
        /// number to forgive on a board where nothing is hidden — the ghost under the thumb says
        /// where the mote lands, whether it enriches and whether it bursts, so what kills a run
        /// is a misjudgement rather than a surprise, and a misjudgement is worth one more go
        /// rather than the run.
        /// </para>
        /// <para>
        /// It is the same on the second well and the tenth, deliberately. The budget is a fail
        /// line and difficulty is the boards' job (invariant 5d) — a per-chapter ramp on the fail
        /// line was tried on the glades and removed for exactly that reason.
        /// </para>
        /// </summary>
        public const int DefaultSpare = 5;

        public readonly int Spare;

        public FallRules(FallLayout layout, int spare = 0)
        {
            Layout = layout;
            Spare = spare > 0 ? spare : DefaultSpare;
        }

        public GameMode Mode => GameMode.Fall;

        public int Width => Layout.Width;
        public int Height => Layout.Height;
    }
}
