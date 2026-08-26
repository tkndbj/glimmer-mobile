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
        /// A weave is graded on cells, and now it is <em>lost</em> on them too.
        ///
        /// <para>
        /// <b>The budget is the ordinary one and it buys the mode a fail state.</b> A weave has
        /// no turns, so when the clock went (invariant 22) this mode was left unable to be lost
        /// at all — only forfeited, which invariant 22a wrote down as the thing to fix before
        /// the mode grew, and named the fix: a budget in the unit it is graded in, not a clock
        /// coming back. That unit is cells, so the budget is cells — see <see cref="WeaveInk"/>
        /// — and it comes from exactly the same <c>par × budgetFactor</c> every glade uses,
        /// through the same <c>LevelTuning.MoveBudget</c>, so the three lines a run is measured
        /// against stay one decision in three numbers rather than becoming two decisions in six.
        /// </para>
        /// <para>
        /// <b>A level still authors no number.</b> <c>budgetFactor</c> is read for the reason
        /// every glade reads it — nought means the default, a deliberate negative turns the
        /// budget off — but no shipped grove writes one, and none should have to: par falls out
        /// of the board, both star lines fall out of par, and now so does the ink.
        /// </para>
        /// <para>
        /// <b>The star factors stay global on purpose</b>, shared with every glade: earned
        /// credits derive from the star ledger, so a mode quietly grading its own stars
        /// differently would deflate or inflate the economy by a number nobody wrote down. What
        /// this mode counts is the light its channels spent, against <c>par</c> — the sum of
        /// every pair's own shortest route plus a cell of looking per pair and per bead
        /// (<c>WeaveLayout.Par</c>). A taut arrangement lands under the three-star line and
        /// sprawl does not.
        /// </para>
        /// <para>
        /// It used to be graded on a clock, and that had two faults. Every star came from the
        /// countdown, so the move slots were passed a run that always reported one "move" and
        /// silently decided nothing — and the record and the published deciles were handed
        /// <c>par</c> as the move count, which is a constant, so every player who ever finished
        /// a grove held an identical one. A cell count fixes both: it is a real number, the
        /// player can improve it deliberately, and it is the same unit the population is
        /// ranked in.
        /// </para>
        /// <para>
        /// So a level authors <em>no</em> difficulty number at all. The board is generated from
        /// its seed, par falls out of the board, and the star lines and the ink fall out of par.
        /// </para>
        /// </summary>
        public override LevelTuning Tune(LevelDto dto, ILevelRules rules)
        {
            var grove = (WeaveRules)rules;
            int par = grove.Par(LevelId.Parse(dto.id));

            return new LevelTuning(par, LevelTuning.DefaultGoldFactor,
                                   LevelTuning.DefaultSilverFactor,
                                   dto.budgetFactor);
        }

        /// <summary>
        /// A weave record is its <em>time</em>, and nothing else.
        ///
        /// <para>
        /// It borrowed Lightfall's "points" stem, which read "56 points" on the map node — where
        /// 56 was the grove's cell count. That number was the same for every player who had ever
        /// finished the grove and it was not a score, so the one line summarising a run carried
        /// no information at all. What replaced it is the count of cells the channels took,
        /// which is what a run is now graded on and the only reading here a player can improve
        /// on deliberately — a tighter arrangement is a better one. The record says so, and it
        /// is a number the published deciles can rank, which a clock reading never was.
        /// </para>
        /// </summary>
        public override string RecordStem => "ui.rank.woven";
    }

    /// <summary>A weave grove: its size, how many pairs and beads, and the deal that lays them out.</summary>
    public sealed class WeaveRules : ILevelRules
    {
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
