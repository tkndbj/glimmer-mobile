using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Lightweave: join each crystal to the critter that wants its colour, without any two
    /// channels crossing.
    ///
    /// <para>
    /// A level authors a grove size, how many pairs, and optionally a seed. The endpoints are
    /// <em>generated</em> — see <see cref="WeaveGenerator"/>, which carves the solution first, so
    /// every board is solvable by construction rather than by hope.
    /// </para>
    /// <para>
    /// <b>Par is the length of that solution</b>, which is what makes the clock and the star
    /// thresholds derive themselves: a bigger grove asks for more time without anybody typing a
    /// number, and there is no figure in the file that can drift away from the board it
    /// describes. Invariant 5, for a mode whose difficulty is generated.
    /// </para>
    /// <para>
    /// Since the generator holds out for a grove with no spare ground in it, par comes out as the
    /// grove's own cell count — so the clock is a function of the authored size alone and is the
    /// same for every seed of a level. That is a feature rather than a coincidence to be relied
    /// on: two players on the same level get the same board <em>and</em> the same clock, and a
    /// board that somehow could not be filled is graded on what it actually is.
    /// </para>
    /// </summary>
    public sealed class WeaveMode : LevelMode
    {
        public override GameMode Mode => GameMode.Weave;

        public override bool Claims(LevelDto dto) => dto.weave != null && dto.weave.IsAuthored;

        public override bool TryRead(LevelDto dto, LevelId id, ICollection<string> problems,
                                     out ILevelRules rules)
        {
            rules = null;
            var grove = dto.weave;

            int width = grove.width > 0 ? grove.width : 7;
            int height = grove.height > 0 ? grove.height : 9;
            int pairs = grove.pairs > 0 ? grove.pairs : 4;

            if (width < 4 || width > 9 || height < 4 || height > 12)
            {
                problems.Add($"weave level '{id}' is {width}x{height}; a grove is 4..9 by 4..12");
                return false;
            }

            if (pairs < 2 || pairs > WeaveGenerator.Palette.Length)
            {
                problems.Add($"weave level '{id}' asks for {pairs} pairs; it is 2.." +
                             $"{WeaveGenerator.Palette.Length}, one per colour the grove has");
                return false;
            }

            rules = new WeaveRules(width, height, pairs, grove.seed);
            return true;
        }

        /// <summary>
        /// Time is the whole grade here, so the move thresholds are turned off and the clock's
        /// are what count. The factors say: solve it in about the length of its own solution for
        /// three stars, half again for two, and three times that before the run is lost.
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var grove = (WeaveRules)rules;
            int par = grove.Par(LevelId.Parse(dto.id));

            return new LevelTuning(par, WeaveRules.GoldFactor, WeaveRules.SilverFactor,
                                   LevelTuning.Unlimited,
                                   dto.timeFactor > 0f ? dto.timeFactor : WeaveRules.TimeFactor);
        }

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (WeaveRules)level.Rules;
            var layout = grove.LayoutFor(level.Id);

            // The board carries its own proof, so the validator plays it rather than trusting it.
            var run = new WeaveRun(layout);
            if (!run.DrawSolution())
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "this grove's own solution does not join every pair without crossing, so " +
                    "the generator produced a board nobody can finish"));

            // Never seen on the shipped shape — the generator holds out for a complete grove and
            // finds one within a handful of attempts. It is here for the size somebody authors
            // later that cannot be filled, where the fallback is a slack board that still needs
            // saying out loud rather than a build failure.
            if (layout.Coverage < WeaveGenerator.MinCoverage)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the solution covers {layout.Coverage:P0} of this grove, leaving " +
                    $"{layout.Count - layout.SolutionLength} cell(s) spare; under " +
                    $"{WeaveGenerator.MinCoverage:P0} there is so much free ground that almost " +
                    "any route works and the board stops being a puzzle"));
        }

        public override string RecordStem => "ui.rank.points";
    }

    /// <summary>A weave grove: its size, how many pairs, and the deal that lays them out.</summary>
    public sealed class WeaveRules : ILevelRules
    {
        /// <summary>Seconds of clock per cell of the solution. Three stars, two stars, and the loss.</summary>
        public const float GoldFactor = 1f, SilverFactor = 1.5f, TimeFactor = 3.0f;

        public readonly int Width, Height, PairCount, Seed;

        WeaveLayout _layout;

        public WeaveRules(int width, int height, int pairs, int seed)
        {
            Width = width;
            Height = height;
            PairCount = pairs;
            Seed = seed;
        }

        public GameMode Mode => GameMode.Weave;

        public uint SeedFor(LevelId id) => ContentSeed.For(Seed, id);

        /// <summary>
        /// The grove this level deals. Built once and kept: generating is a few hundred walks,
        /// which is nothing once but wasteful per frame, and every reader has to agree about
        /// which board they are talking about.
        /// </summary>
        public WeaveLayout LayoutFor(LevelId id)
            => _layout ??= WeaveGenerator.Build(Width, Height, PairCount, SeedFor(id));

        /// <summary>How many cells the solution runs through. The clock scales off it.</summary>
        public int Par(LevelId id) => LayoutFor(id).SolutionLength;
    }
}
