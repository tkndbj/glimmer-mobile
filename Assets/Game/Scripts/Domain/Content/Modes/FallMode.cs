using System.Collections.Generic;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Lightfall: motes drop into columns and cook toward white.
    ///
    /// Authors a well and nothing else. Its difficulty lives in <c>FallBoard</c>'s constants and
    /// in the deal, so there is no number in a level file that can come to disagree with how the
    /// game actually plays — which is the same reason a glade omits its par.
    /// </summary>
    public sealed class FallMode : LevelMode
    {
        public override GameMode Mode => GameMode.Fall;

        public override bool Claims(LevelDto dto) => dto.fall != null && dto.fall.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            var well = dto.fall;
            rules = new FallRules(
                well.width > 0 ? well.width : 6,
                well.height > 0 ? well.height : 11,
                well.seed);
            return true;
        }

        /// <summary>
        /// A score attack has no par, so the ladder is a placeholder that nothing reads. The
        /// mode grades itself — see <c>FallBoard.Score</c>.
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules) => LevelTuning.Default(1);

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var well = (FallRules)level.Rules;

            if (well.Width < 4 || well.Width > 8)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a well is 4..8 wide; this one is {well.Width}"));

            if (well.Height < 6 || well.Height > 14)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a well is 6..14 tall; this one is {well.Height}"));
        }

        public override string RecordStem => "ui.rank.points";
    }

    /// <summary>A well: how wide, how tall, and what it deals.</summary>
    public sealed class FallRules : ILevelRules
    {
        public readonly int Width, Height, Seed;

        public FallRules(int width, int height, int seed)
        {
            Width = width;
            Height = height;
            Seed = seed;
        }

        public GameMode Mode => GameMode.Fall;

        public uint SeedFor(LevelId id) => ContentSeed.For(Seed, id);
    }
}
