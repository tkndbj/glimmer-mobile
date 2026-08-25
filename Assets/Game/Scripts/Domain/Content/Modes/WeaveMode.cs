using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// Lightweave: join each crystal to the critter that wants its colour, without any two
    /// channels crossing, threading every bead on the way.
    ///
    /// <para>
    /// A level authors a grove size, how many pairs, how many beads, and optionally a seed. Where
    /// any of it stands is <em>generated</em> — see <see cref="WeaveGenerator"/>, which carves the
    /// solution first, so every board is solvable by construction rather than by hope.
    /// </para>
    /// <para>
    /// <b>Par is the grove's own floor plus its decisions</b> — see <see cref="WeaveLayout.Par"/>.
    /// That is what makes the clock and the star thresholds derive themselves: a wider grove,
    /// another pair or another bead each raise the work and raise par with it, and there is no
    /// figure in the file that can drift away from the board it describes. Invariant 5, for a
    /// mode whose difficulty is generated.
    /// </para>
    /// <para>
    /// <b>It used to be the carved solution's length, and that stopped being honest.</b> The
    /// generator's arrangement fills the grove, and while filling the grove was the win condition
    /// that length was exactly the work. It no longer is: the player draws whatever route they
    /// like, which on most boards is a good deal less. Grading somebody against the length of a
    /// route nobody is asked to draw is grading them against a fiction — so par moved to the one
    /// number that is still a fact about what has to be drawn.
    /// </para>
    /// <para>
    /// A board is also held to <see cref="WeaveGenerator.MinSlack"/>: there must be no
    /// arrangement in which every pair takes its own shortest route at once. Any one route may
    /// be perfectly direct — what the board denies is all of them being direct together, so the
    /// question is who yields and the player answers it.
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
            int beads = grove.beads > 0 ? grove.beads : 0;

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

            // Refused rather than clamped, for HintPrompt's reason one screen over: a level
            // quietly given fewer beads than it authored is a rung whose difficulty is not the
            // one anybody chose, and nothing downstream would ever say so.
            if (beads > WeaveGenerator.MostBeads(pairs))
            {
                problems.Add($"weave level '{id}' asks for {beads} beads on {pairs} pairs; it is " +
                             $"at most {WeaveGenerator.MostBeads(pairs)}, one per channel — a " +
                             "channel with two is a tour to remember rather than a route to find");
                return false;
            }

            rules = new WeaveRules(width, height, pairs, beads, grove.seed);
            return true;
        }

        /// <summary>
        /// Time is the whole grade here: there is no move budget, and the clock decides the stars.
        ///
        /// <para>
        /// <b>The move factors are deliberately the ordinary defaults and deliberately do
        /// nothing.</b> A weave run reports one "move", so <c>StarsForMoves</c> always answers
        /// three and <c>StarsFor</c> takes the clock's reading every time. This used to pass a
        /// pair of weave-specific constants into those two slots, which read exactly as though
        /// they were the mode's star thresholds and were not: they are compared against a move
        /// count that is always 1, so retuning them moved nothing at all, silently. The clock's
        /// own lines are <c>LevelTuning.TimeGoldFactor</c> and <c>TimeSilverFactor</c> — global,
        /// shared with every glade, and global on purpose, because earned credits derive from the
        /// star ledger and a mode quietly grading its own stars differently would deflate or
        /// inflate the economy by a number nobody wrote down.
        /// </para>
        /// <para>
        /// So what a level authors is <c>timeFactor</c> and only <c>timeFactor</c>, which moves
        /// where a run is <em>lost</em> and never what a clear is worth.
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var grove = (WeaveRules)rules;
            int par = grove.Par(LevelId.Parse(dto.id));

            return new LevelTuning(par, LevelTuning.DefaultGoldFactor,
                                   LevelTuning.DefaultSilverFactor,
                                   LevelTuning.Unlimited,
                                   dto.timeFactor > 0f ? dto.timeFactor : WeaveRules.TimeFactor);
        }

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (WeaveRules)level.Rules;
            var layout = grove.LayoutFor(level.Id);

            // The board carries its own proof, so the validator plays it rather than trusting it.
            // Played all the way to IsSolved, which is what proves the beads too: a bead the
            // carved route does not thread is a board its own solution cannot finish.
            var run = new WeaveRun(layout);
            if (!run.DrawSolution())
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "this grove's own solution does not join every pair and thread every bead " +
                    "without crossing, so the generator produced a board nobody can finish"));

            if (layout.Beads.Count < grove.BeadCount)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this grove asks for {grove.BeadCount} bead(s) and could only place " +
                    $"{layout.Beads.Count}: a bead is only worth placing on a cell off every " +
                    "shortest route between its pair's ends, and this seed's carved routes did " +
                    "not offer that many — re-seed it with Survey Lightweave's SeedSearch"));

            // Never seen on the shipped shape — the generator holds out for a carve that reaches
            // every cell and finds one within a handful of attempts. It is here for the size
            // somebody authors later that cannot be filled, where the fallback is a board whose
            // endpoints all sit in their own quiet corner.
            if (layout.Coverage < WeaveGenerator.MinCoverage)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the carve covers {layout.Coverage:P0} of this grove, leaving " +
                    $"{layout.Count - layout.SolutionLength} cell(s) untouched; under " +
                    $"{WeaveGenerator.MinCoverage:P0} the endpoints stop being spread across the " +
                    "grove and the channels stop having to get past each other"));

            // The acceptance bar, asked again of the board that actually shipped. It is the same
            // predicate the generator held out for rather than a second opinion about it, which
            // is the point: a grove that has drifted past the bar is one the generator could not
            // satisfy and settled for, and that is worth saying out loud rather than inferring
            // from a difficulty survey somebody has to remember to run.
            //
            // A warning rather than an error, for the reason nothing else here is a gate: the
            // board is generated, so a build cannot be failed over a number that is a property of
            // a seed somebody would then have to guess their way out of.
            bool taut = WeaveSolver.AnyTautSolution(layout, out bool decided);
            if (decided && taut)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "every pair of this grove can take its own shortest route at once, so it is " +
                    "finished by drawing the obvious line at each critter in turn and asks the " +
                    "player nothing — re-seed the level with Survey Lightweave's SeedSearch, " +
                    "which holds a candidate to the exact bar"));
        }

        /// <summary>
        /// A weave record is its <em>time</em>, and nothing else.
        ///
        /// <para>
        /// It borrowed Lightfall's "points" stem, which read "56 points" on the map node — where
        /// 56 was the grove's cell count. That number was the same for every player who had ever
        /// finished the grove and it was not a score, so the one line summarising a run carried
        /// no information at all. A count of cells is a slightly better number now that routes
        /// differ between runs, and it is still the wrong one: a player who wanders is not doing
        /// better than one who does not, and the mode is graded on the clock precisely because
        /// speed is the only reading here that means anything. The record says so.
        /// </para>
        /// </summary>
        public override string RecordStem => "ui.rank.woven";
    }

    /// <summary>A weave grove: its size, how many pairs and beads, and the deal that lays them out.</summary>
    public sealed class WeaveRules : ILevelRules
    {
        /// <summary>
        /// Seconds of clock per cell of par, for a level that authors none.
        ///
        /// <para>
        /// <b>Retuned when par stopped meaning "the whole grove".</b> Par used to be the carved
        /// solution's length, which fills the grove — so a 7x9 board carried a par of 63 whatever
        /// its pairs were doing, and the limit came out of that. Par is now the sum of the pairs'
        /// own floors, which on the same board is nearer 45: the same multiplier against a
        /// smaller par would have quietly cut every clock in the chapter by a third, on the drop
        /// that also made the boards harder. The multiplier moved so the limits did not.
        /// </para>
        /// <para>
        /// The clock is the one rule in this game a player can fail through no fault of their
        /// reasoning, so it is the puzzle that is hard and the clock that is fair. There are
        /// deliberately no star factors beside it — see <c>WeaveMode.Tune</c>, which is where a
        /// pair of them used to sit doing nothing.
        /// </para>
        /// </summary>
        public const float TimeFactor = 5.0f;

        public readonly int Width, Height, PairCount, BeadCount, Seed;

        WeaveLayout _layout;

        public WeaveRules(int width, int height, int pairs, int beads, int seed)
        {
            Width = width;
            Height = height;
            PairCount = pairs;
            BeadCount = beads;
            Seed = seed;
        }

        public GameMode Mode => GameMode.Weave;

        public uint SeedFor(LevelId id) => ContentSeed.For(Seed, id);

        /// <summary>
        /// The grove this level deals. Built once and kept: generating is a few hundred walks and
        /// a bounded search, which is nothing once but wasteful per frame, and every reader has to
        /// agree about which board they are talking about.
        /// </summary>
        public WeaveLayout LayoutFor(LevelId id)
            => _layout ??= WeaveGenerator.Build(Width, Height, PairCount, SeedFor(id), BeadCount);

        /// <summary>What this grove is graded against — see <c>WeaveLayout.Par</c>.</summary>
        public int Par(LevelId id) => LayoutFor(id).Par;
    }
}
