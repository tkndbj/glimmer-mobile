using System.Collections.Generic;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// How a mode is proved fit to ship.
    ///
    /// <para>
    /// <b>A mode is now declared three times, and the third one is this.</b> What it <em>is</em>
    /// lives in <see cref="LevelMode"/> (Domain — its rules, its parser, its tuning). What it
    /// <em>looks like</em> lives in <c>ModeLook</c> (Presentation — its screen, its perch, its
    /// colour), split off because Domain may never reference Presentation. This is the same split
    /// made once more for the same kind of reason: a mode's checks run on a build machine and
    /// never on a phone, so they belong in an assembly no player installs.
    /// </para>
    /// <para>
    /// <b>It was a <c>virtual</c> on <see cref="LevelMode"/>, and that one word shipped the whole
    /// validator.</b> <c>LevelValidator</c> is six hundred lines that prove a board is solvable,
    /// that its arms mate, that its taproots bind, that its star bands are landable — none of
    /// which a player's device has any use for, because content is proved on the machine that
    /// builds it. It could not move while it was reached through a member of a class the runtime
    /// does use: the authoring entry point called into the mode, and the mode called back into
    /// the authoring entry point, so the pair had to live wherever the runtime could see them.
    /// Cutting that cycle is the whole of this file.
    /// </para>
    /// <para>
    /// <b>An unregistered mode is an error, which is the opposite of what <c>ModeLooks</c>
    /// does.</b> A mode missing a <em>look</em> draws as the classic one, because a map with an
    /// odd-looking node is a better failure than a map that will not open. A mode missing a
    /// <em>validator</em> must never fall back to anything, because the fallback would be
    /// "validated nothing" and would be indistinguishable, on every screen and in every log, from
    /// content that passed. So <see cref="LevelValidator.Validate"/> reports it, and
    /// <c>ModeValidatorTests</c> refuses a build where any shipped mode is missing one — the
    /// check exists because the failure is silent, which is the same reason invariant 20h exists
    /// one file over.
    /// </para>
    /// </summary>
    public abstract class ModeValidator
    {
        public abstract GameMode Mode { get; }

        /// <summary>
        /// Proves a level of this mode is worth shipping, adding to <paramref name="issues"/>.
        /// </summary>
        public abstract void Validate(LevelDefinition level, List<LevelIssue> issues);
    }

    /// <summary>
    /// Every mode's checks, registered once.
    ///
    /// Mirrors <see cref="LevelModes"/> and <c>ModeLooks</c>. Registering a mode here is the
    /// third and last thing adding a mode costs, and the suite names it if you forget.
    /// </summary>
    public static class ModeValidators
    {
        static readonly ModeValidator[] _all =
        {
            new GladeValidator(),
            new FallValidator(),
            new KeeperValidator(),
            new WeaveValidator(),
        };

        public static IReadOnlyList<ModeValidator> All => _all;

        /// <summary>This mode's checks, or null when nothing has been registered for it.</summary>
        public static ModeValidator Of(GameMode mode)
        {
            for (int i = 0; i < _all.Length; i++)
                if (_all[i].Mode.Equals(mode)) return _all[i];
            return null;
        }
    }

    /// <summary>
    /// The classic glade. Its checks are the bulk of <see cref="LevelValidator"/> and stay there
    /// rather than being poured into this file: they are what that file is about, and six hundred
    /// lines do not become better organised by being moved under a different heading.
    /// </summary>
    sealed class GladeValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Glade;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
            => LevelValidator.ValidateGlade(level, issues);
    }

    /// <summary>
    /// Lightfall. A well is authored rather than generated, so unlike a weave everything here is
    /// in the file — but whether it can be <em>emptied</em> is not, and that is what most of
    /// this proves.
    ///
    /// <para>
    /// The mode's checks used to be two lines about width and height, which was the honest
    /// amount for a score attack with no goal in it. A level with a goal, a derived par and two
    /// fail states has considerably more that can be silently wrong, and every one of these
    /// failures looks like a perfectly authored board in the JSON.
    /// </para>
    /// </summary>
    sealed class FallValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Fall;

        /// <summary>
        /// Where a well stops being cheap to prove, and where it stops being shippable.
        ///
        /// <para>
        /// <b>These are about the <em>player's</em> device, not about this one.</b>
        /// <see cref="FallSolver.NodeBudget"/> is a quarter of a million because it has to make a
        /// genuinely hard board <em>provable</em> — a board it cannot prove is a board with no
        /// par, and everything a player is graded against derives from par. These two are the
        /// separate question of what that proof costs where it is actually paid: once per level,
        /// on the phone, when somebody opens it (invariant 26d).
        /// </para>
        /// <para>
        /// Measured rather than guessed. Forty thousand positions is about twenty milliseconds
        /// of desktop .NET, so a few tens on a phone running IL2CPP — invisible behind a screen
        /// transition. A hundred and twenty thousand is about sixty-five, so a quarter of a
        /// second on a phone, which is a pause somebody notices on the way into a level and is
        /// therefore refused rather than warned about. The cost is not linear in anything an
        /// author controls directly: it goes as the column count to the power of par, so par 7
        /// on a six-wide well is four times par 6 on the same board. Shorten the well, start it
        /// fuller, or narrow it.
        /// </para>
        /// </summary>
        const int NodeWarning = 40_000, NodeCeiling = 120_000;

        /// <summary>
        /// Above this many shortest solutions, the board is not deciding much — see
        /// <see cref="FallSurvey.Ways"/> and invariant 5d.
        /// </summary>
        const int TooManyWays = 400;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var well = (FallRules)level.Rules;
            var layout = well.Layout;
            bool floating = false;

            // Row nought is where a mote floods the well, so a fill standing in it is a level
            // that begins in its own fail state. Refused here rather than in the parser because
            // the parser is what a *player's* build runs: a level that reaches a device this way
            // should still open, and be caught on the machine that built it.
            for (int x = 0; x < layout.Width; x++)
            {
                if (layout.At(x, FallLayout.Brim) == Energy.None) continue;

                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"there is a mote standing in column {x} of the brim row, which is the row " +
                    "that ends the run — this level begins lost"));
                break;
            }

            // Gravity is applied whenever anything bursts, so a mote with nothing under it is
            // a mote the author drew in one place and the player meets in another. It is only
            // ever a slip, and it is invisible in the file.
            for (int x = 0; x < layout.Width && !floating; x++)
            {
                bool air = false;
                for (int y = layout.Height - 1; y >= 0; y--)
                {
                    bool here = layout.At(x, y) != Energy.None;
                    if (!here) { air = true; continue; }
                    if (!air) continue;

                    issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                        $"the mote at column {x} row {y} has nothing under it, so the well would " +
                        "settle differently from the way it is written the first time anything " +
                        "bursts"));
                    floating = true;
                    break;
                }
            }

            // A procession that cannot supply a channel some mote is missing makes that mote
            // unfinishable however many drops are bought, so the well can never be emptied. The
            // search below would catch it, but not in words anybody could act on.
            // Every channel, not merely every channel the board wants *now*, and the
            // difference is a well that can be neither won nor lost. A drop that lands on bare
            // ground puts a fresh pure mote in the well, and that mote wants the two channels it
            // does not hold — so a procession of two colours can be walked into a position no
            // amount of play recovers from. On a well with a supply that is a loss, which is
            // survivable; on the opening well, which is authored without one, it is a board that
            // sits there for ever refusing to end. Invariant 20g's state, reached by arithmetic.
            //
            // It costs authoring nothing: the procession repeats, so this is one character.
            if (layout.Deal.Channels != Energy.All)
            {
                int absent = Energy.All & ~layout.Deal.Channels;
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this procession never deals {Energy.Letter(absent)}, so a mote that ends " +
                    "up wanting it could never be finished — and a drop onto bare ground makes " +
                    "one. A deal has to carry all three channels"));
            }

            // Room above par is `spare`, in drops, so a budgetFactor on a well is a number
            // that does nothing. Refused rather than ignored, for ChapterDto.order's reason —
            // and refused rather than honoured, because two ways to say one thing is how they
            // come to disagree. A negative factor still means "cannot be lost", which is not an
            // override and is what the first well in the game is authored with.
            if (level.Tuning.BudgetFactorIsIgnored)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this well authors budgetFactor {level.Tuning.BudgetFactor:0.##}, which " +
                    "does nothing: a well's room above par is 'spare', counted in drops, " +
                    "because a wrong drop costs the same wherever it happens. Use 'spare', or " +
                    "a negative budgetFactor if it is meant to be unlosable"));

            var survey = FallSolver.Survey(layout);

            if (!survey.Proved)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this well could not be proved inside {FallSolver.NodeBudget} positions " +
                    $"(it looked at {survey.Nodes}) or within {FallSolver.MaxDrops} drops. It " +
                    "may be unsolvable, or simply too big to prove — either way it cannot ship, " +
                    "because the player's device runs the same search to work out par"));
                return;
            }

            if (!survey.IsSolvable)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "no sequence of drops empties this well without flooding it, so nobody can " +
                    "finish it — every arrangement was searched and none won"));
                return;
            }

            if (survey.Nodes > NodeCeiling)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"proving this well took {survey.Nodes} positions, above the {NodeCeiling} " +
                    "a level may cost. The player's device runs this same search when somebody " +
                    "opens the level, so this is about a quarter of a second of nothing " +
                    "happening on the way in. Cost goes as the column count to the power of " +
                    "par, so the cheapest fixes are a narrower well or a shorter answer — " +
                    "start it fuller rather than making it bigger"));
            else if (survey.Nodes > NodeWarning)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"proving this well took {survey.Nodes} positions against the " +
                    $"{NodeWarning} a level is expected to cost. It ships — the refusal is " +
                    $"at {NodeCeiling} — but the player's device runs this same search when " +
                    "somebody opens the level"));

            // The same three-line check every mode with a fail line gets. Shared rather than
            // restated: a second copy of "is this ladder ordered" is a second thing to keep in
            // step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            // Invariant 5d, counted. A well almost anything clears is one where the colours and
            // the ordering decide nothing, however pretty it looks.
            if (survey.Ways > TooManyWays)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{survey.Ways} different sequences of {survey.Par} drops empty this well, " +
                    "so almost any tidy play wins and the procession is deciding nothing — " +
                    "fill it fuller, mix the colours less neatly, or shorten the deal"));

            // Reported rather than gated. On a chapter's opening levels thoughtlessness is
            // supposed to work — that is what teaching the verb looks like — so this is a
            // reading for the author, and a chapter's ladder is where it stops being true.
            if (survey.Greedy >= 0 && survey.Greedy <= level.Tuning.MoveBudget && survey.Par > 3)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"a player who never looks ahead empties this well in {survey.Greedy} drops " +
                    $"against a supply of {level.Tuning.MoveBudget}, so it can be cleared by " +
                    "always taking the biggest burst going — fine early in a chapter, and worth " +
                    "knowing later in one"));

            // Zero headroom means the tallest column is one careless drop from the brim before
            // the player has touched anything. Legitimate as a finale and alarming anywhere
            // else, so it is said out loud rather than refused.
            if (layout.Headroom <= 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "the fill reaches the row below the brim, so the very first careless drop " +
                    "on the tallest column ends the run — deliberate on a finale, a mistake " +
                    "anywhere else"));
        }
    }

    /// <summary>
    /// Groovekeeper. A grove is authored rather than generated, so unlike a weave everything here
    /// is in the file — but whether every bed on it can be <em>opened</em> is not, and that is
    /// what most of this proves.
    ///
    /// <para>
    /// The mode's checks used to be three lines about width, height and a tile count, which was
    /// the honest amount for a score attack with no goal in it. A level with a goal, a derived par
    /// and two fail states has considerably more that can be silently wrong, and every one of
    /// these failures looks like a perfectly authored grove in the JSON.
    /// </para>
    /// </summary>
    sealed class KeeperValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Keeper;

        /// <summary>
        /// Where a grove stops being cheap to prove, and where it stops being shippable.
        ///
        /// <para>
        /// <b>These are about the <em>player's</em> device, not about this one.</b>
        /// <see cref="KeeperSolver.NodeBudget"/> is large because it has to make a genuinely hard
        /// grove <em>provable</em> — a grove it cannot prove is a grove with no par, and
        /// everything a player is graded against derives from par. These two are the separate
        /// question of what that proof costs where it is actually paid: once per level, on the
        /// phone, when somebody opens it (invariant 26d).
        /// </para>
        /// <para>
        /// Lower than Lightfall's pair, and deliberately: a position here costs more to expand
        /// than a well's does, because the floor this search prunes on walks every bed and every
        /// standing tile. Cost goes roughly as the open cell count to the power of par, so the
        /// cheap fixes are more stone, fewer beds, or a bed one step nearer the sprig — never a
        /// bigger grove.
        /// </para>
        /// </summary>
        const int NodeWarning = 30_000, NodeCeiling = 90_000;

        /// <summary>
        /// Above this many shortest answers the grove is not deciding much — see
        /// <see cref="KeeperSurvey.Ways"/> and invariant 5d.
        /// </summary>
        const int TooManyWays = 300;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (KeeperRules)level.Rules;
            var layout = grove.Layout;

            // A procession that cannot supply a colour some heartbed insists on makes that bed
            // unopenable however many tiles are bought, so the grove can never be finished. The
            // search below would catch it, but not in words anybody could act on.
            int wanted = layout.Wanted;
            if ((wanted & ~layout.Deal.Channels) != Energy.None)
            {
                int absent = wanted & ~layout.Deal.Channels;
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"a heartbed here insists on {Energy.Letter(absent)} and this procession " +
                    "never deals it, so that bed could never be opened by anybody"));
            }

            // Note what is deliberately *not* checked: that the procession carries all three
            // channels. Lightfall refuses a deal that does not, and has to — a drop onto bare
            // ground there makes a fresh mote wanting the two colours it lacks, so a two-colour
            // procession can be walked into a position no amount of play recovers from. Nothing
            // here does that. A tile that cannot bloom is simply a tile, the sprigs standing on
            // the ground are permanent, and two of the ten grooves that ship are finished with a
            // two-colour basket precisely because the third colour is already on the board. What
            // matters is that every bed can be opened, and the search below proves exactly that.

            // Room above par is `spare`, in tiles, so a budgetFactor on a grove is a number that
            // does nothing. Refused rather than ignored, for ChapterDto.order's reason — and
            // refused rather than honoured, because two ways to say one thing is how they come to
            // disagree. A negative factor still means "cannot be lost", which is not an override
            // and is what the first grove in the game is authored with.
            if (level.Tuning.BudgetFactorIsIgnored)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove authors budgetFactor {level.Tuning.BudgetFactor:0.##}, which " +
                    "does nothing: a grove's room above par is 'spare', counted in tiles, " +
                    "because a wrong tile costs the same wherever it happens. Use 'spare', or a " +
                    "negative budgetFactor if it is meant to be unlosable"));

            var survey = KeeperSolver.Survey(layout);

            if (!survey.Proved)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove could not be proved inside {KeeperSolver.NodeBudget} positions " +
                    $"(it looked at {survey.Nodes}) or within {KeeperSolver.MaxTiles} tiles. It " +
                    "may be unsolvable, or simply too big to prove — either way it cannot ship, " +
                    "because the player's device runs the same search to work out par"));
                return;
            }

            if (!survey.IsSolvable)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "no sequence of tiles opens every bed on this grove, so nobody can finish " +
                    "it — every arrangement was searched and none won"));
                return;
            }

            if (survey.Nodes > NodeCeiling)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"proving this grove took {survey.Nodes} positions, above the {NodeCeiling} " +
                    "a level may cost. The player's device runs this same search when somebody " +
                    "opens the level, so this is about a quarter of a second of nothing " +
                    "happening on the way in. Cost goes roughly as the open cell count to the " +
                    "power of par, so the cheapest fixes are more stone or a shorter answer"));
            else if (survey.Nodes > NodeWarning)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"proving this grove took {survey.Nodes} positions against the " +
                    $"{NodeWarning} a level is expected to cost. It ships — the refusal is at " +
                    $"{NodeCeiling} — but the player's device runs this same search when " +
                    "somebody opens the level"));

            // The same three-line check every mode with a fail line gets. Shared rather than
            // restated: a second copy of "is this ladder ordered" is a second thing to keep in
            // step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            // A basket bigger than the ground can hold is a fail state that fires the wrong way:
            // the run ends Overgrown with tiles still in the basket, which reads to a player as
            // the game stopping rather than as running out of anything.
            int room = layout.Room - layout.Sprigs;
            if (level.Tuning.HasBudget && level.Tuning.MoveBudget > room)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this grove is dealt {level.Tuning.MoveBudget} tiles onto {room} cells of " +
                    "bare ground, so a careless run runs out of somewhere to plant before it " +
                    "runs out of tiles — which ends it on the one fail state a continue cannot " +
                    "rescue"));

            // Invariant 5d, counted. A grove almost any tidy play finishes is one where the
            // ground and the procession decide nothing, however pretty it looks.
            if (survey.Ways > TooManyWays)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{survey.Ways} different groves of {survey.Par} tiles open every bed here, " +
                    "so almost any tidy play wins and the ground is deciding nothing — add " +
                    "stone, move a bed further from the sprig, or make one of them a heartbed"));

            // Reported rather than gated. On a chapter's opening levels thoughtlessness is
            // supposed to work — that is what teaching the verb looks like — so this is a reading
            // for the author, and a chapter's ladder is where it stops being true.
            int greedy = KeeperSolver.Greedy(layout, level.Tuning.MoveBudget);
            if (greedy >= 0 && greedy <= level.Tuning.MoveBudget && survey.Par > 3)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"a player who never looks ahead finishes this grove in {greedy} tiles " +
                    $"against a basket of {level.Tuning.MoveBudget}, so it can be cleared by " +
                    "always taking the biggest flourish going — fine early in a chapter, and " +
                    "worth knowing later in one"));
        }
    }

    /// <summary>
    /// Lightweave. A weave board is <em>generated</em>, so unlike a glade there is nothing in the
    /// file to read: every check here has to deal the board and look at what came out.
    /// </summary>
    sealed class WeaveValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Weave;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (WeaveRules)level.Rules;
            var layout = grove.LayoutFor(level.Id);

            // The board carries its own proof, so the validator plays it rather than trusting it.
            // Played all the way to IsSolved, which is what proves the beads too: a bead the
            // carved route does not thread is a board its own solution cannot finish.
            // A board rather than a run: the validator is proving a grove can be finished, not
            // playing one, so it neither spends light nor writes down a stroke.
            var board = new WeaveBoard(layout);
            if (!board.DrawSolution())
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "this grove's own solution does not join every pair and thread every bead " +
                    "without crossing, so the generator produced a board nobody can finish"));

            // The same three-line check every glade gets, asked of a mode that now has a fail
            // line to get wrong. Shared rather than restated: a second copy of "is this ladder
            // ordered" is a second thing to keep in step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            // What no glade has to prove, because a glade's par *is* its minimum: a weave's is
            // the sum of the pairs' own floors plus an allowance, and the arrangement the player
            // actually has to draw costs that floor plus whatever detour the board forces. So
            // the ink has to cover the floor before anything else — a grove whose cheapest
            // possible finish is dearer than the light it is dealt cannot be won by anybody, and
            // it would look perfectly authored in the file.
            //
            // The forced detour is deliberately not asked for here: it is WeaveSolver's
            // exponential search, which is an authoring instrument and never a gate (the seed
            // sweep and WeaveLadderTests are where it runs). This is the half that is cheap and
            // certain.
            if (level.Tuning.HasBudget && layout.StraightTotal > level.Tuning.MoveBudget)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove is dealt {level.Tuning.MoveBudget} cells of ink and its pairs " +
                    $"cannot be joined in fewer than {layout.StraightTotal} even with nobody in " +
                    "anybody's way, so no arrangement of it can be drawn — lower the pair count " +
                    "or raise budgetFactor"));

            // A grove that could not be walled the way its rung asked is a rung one barrier
            // easier than the ladder claims, and nothing else anywhere would say so — the board
            // is still perfectly solvable, still full, still measured. The same silent failure a
            // short bead count is, and it is reported the same way.
            if (layout.Hedges.Count < grove.HedgeCount)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this grove asks for {grove.HedgeCount} hedge(s) and could only grow " +
                    $"{layout.Hedges.Count}: a hedge has to reach a side of the grove, leave a " +
                    "way past its tip and seal nothing off, and this seed's attempts could not " +
                    "place that many — re-seed it with Survey Lightweave's SeedSearch"));

            // Invariant 5d, counted, for this mechanic. A barrier that changes no pair's shortest
            // route rejects no arrangement: the player draws the line they were going to draw and
            // never touches it, and the rung is a plain grove wearing a mechanic. The generator
            // holds out for a fence that bites, so this is the audit of that rather than a second
            // opinion about it — and it is a warning for the reason nothing else here is a gate,
            // that the board is generated and a build cannot be failed over a property of a seed.
            if (grove.HedgeCount > 0 && !layout.HedgesBite)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the {layout.Hedges.Count} hedge(s) on this grove change no pair's shortest " +
                    "route, so they are scenery: every channel can be drawn exactly as it would " +
                    "have been on open ground — re-seed the level, or grow one more"));

            // The same claim asked of how many channels rather than of the sum, which is the half
            // that was missing while the Wildhedge was authored. HedgesBite is a total over the
            // grove, so one pair detouring two cells satisfies it for a board of six — and that
            // is exactly what shipped: eight of the chapter's ten groves reached precisely one
            // pair, three barriers apiece, five channels drawn as though the fence were not
            // there. It passed every check in this file and came back from play as "it is like
            // they are not there". A barrier's whole value is the gap it leaves, and a gap is
            // only a decision when more than one channel wants it — see WeaveGenerator.MinBitten.
            int wantedBitten = WeaveGenerator.MinBitten(grove.PairCount, grove.HedgeCount);
            if (layout.PairsBitten < wantedBitten)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the fence on this grove sends {layout.PairsBitten} of its " +
                    $"{grove.PairCount} channels a longer way and {wantedBitten} is the fewest " +
                    "that makes it a shared obstacle rather than one pair's detour — re-seed the " +
                    "level with Survey Lightweave's SeedSearch"));

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

            // Whether the grading can be missed at all, which is a question only this mode has to
            // ask and which nothing was asking. Two channels may never share a cell, so a run
            // that never redraws cannot spend more light than the grove has ground — and if the
            // three-star line sits above that number, every completion takes three stars however
            // sloppily it is drawn. It is invariant 22's stranded band from the other end: there
            // the bottom rung could not be landed in, here the top one cannot be missed.
            //
            // Measured when it was written: true of twenty-eight of the thirty groves the mode
            // ships, and of all ten of the Wildhedge's. That is a property of par against the
            // grove's size rather than of a seed — the star lines are par x 1.20 and 1.40, par is
            // roughly four fifths of the grove plus a cell of looking per decision, so the line
            // clears the whole board on anything much above a beginner's grove. So it is reported
            // once per level and cannot be re-seeded away: what moves it is the shape of the
            // grove, or WeaveLayout.Par's allowance, and both are decisions somebody has to take
            // deliberately rather than a number to guess at.
            if (level.Tuning.GoldThreshold >= layout.Count)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"three stars is dealt at {level.Tuning.GoldThreshold} cells of ink on a " +
                    $"grove of {layout.Count} cells, and no two channels may share one — so a " +
                    "run that does not redraw cannot spend enough light to miss it, whatever it " +
                    "draws. The grading of this grove is decoration: give it fewer pairs' worth " +
                    "of floor, or a smaller grove"));
        }
    }
}
