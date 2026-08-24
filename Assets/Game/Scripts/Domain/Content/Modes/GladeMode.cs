using System.Collections.Generic;
using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The classic grove: a grid of conduits turned until the light reaches every critter.
    ///
    /// A level that names no mode is read as this one, which is what keeps every chapter
    /// authored before modes existed working with its file untouched.
    /// </summary>
    public sealed class GladeMode : LevelMode
    {
        public override GameMode Mode => GameMode.Glade;

        /// <summary>A glade is the default, so it claims anything with rows and no other block.</summary>
        public override bool Claims(LevelDto dto) => dto.rows != null && dto.rows.Length > 0;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;

            int width = dto.width > 0 ? dto.width : WidestRow(dto.rows);
            int height = dto.height > 0 ? dto.height : dto.rows.Length;

            LevelLayout layout;
            try
            {
                layout = new LevelLayout(width, height, dto.rows);
            }
            catch (System.Exception e)
            {
                problems.Add($"level '{id}' has an unusable grid: {e.Message}");
                return false;
            }

            rules = new GladeRules(layout);
            return true;
        }

        /// <summary>
        /// Par is derivable from the board, so an omitted par is not an error — it is the
        /// recommended way to author, since a hand-typed one can drift.
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var glade = (GladeRules)rules;

            int par = dto.par;
            if (par <= 0)
            {
                var parsed = LevelGridParser.Parse(glade.Layout);
                par = parsed.Ok ? PuzzleFactory.MinimumMoves(parsed.Cells) : 1;
            }

            return new LevelTuning(
                par,
                dto.goldFactor > 0f ? dto.goldFactor : LevelTuning.DefaultGoldFactor,
                dto.silverFactor > 0f ? dto.silverFactor : LevelTuning.DefaultSilverFactor,
                dto.budgetFactor,
                dto.timeFactor);
        }

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
            => LevelValidator.ValidateGlade(level, issues);

        static int WidestRow(string[] rows)
        {
            int widest = 0;
            foreach (string row in rows)
            {
                int cells = (row ?? string.Empty)
                    .Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries).Length;
                if (cells > widest) widest = cells;
            }
            return widest;
        }
    }

    /// <summary>A glade's rules: the conduit grid, and nothing else.</summary>
    public sealed class GladeRules : ILevelRules
    {
        public readonly LevelLayout Layout;

        public GladeRules(LevelLayout layout) => Layout = layout;

        public GameMode Mode => GameMode.Glade;
    }
}
