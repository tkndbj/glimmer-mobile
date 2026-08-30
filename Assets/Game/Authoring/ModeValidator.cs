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
            new BudValidator(),
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
    /// Budburst. The whole grove is in the file, so everything about it can be read — but
    /// whether the chains can be made to reach every cocoon is not, and that is what most of this
    /// proves.
    ///
    /// <para>
    /// <b>Two of its checks are the house rules read backwards, and that is deliberate.</b>
    /// Everywhere else a board almost anything finishes is a warning (invariant 5d) and a
    /// careless player finishing is a warning too. This mode's brief is a board almost anything
    /// finishes: the star line is where the skill lives, and the thing worth refusing is a
    /// grove that a player who just taps the biggest thing <em>cannot</em> finish.
    /// </para>
    /// </summary>
    sealed class BudValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Bud;

        /// <summary>
        /// Where a grove stops being cheap to prove, and where it stops being shippable. About
        /// the <em>player's</em> device: the search runs once per level, on the phone, when
        /// somebody opens it (invariant 26d).
        /// </summary>
        const int NodeWarning = 20_000, NodeCeiling = 60_000;

        /// <summary>
        /// A grove with only one shortest play is a puzzle, and a puzzle is what this mode is
        /// deliberately not. Warned rather than refused: an opening level may legitimately have
        /// one obvious best tap.
        /// </summary>
        const int TooFewWays = 2;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var grove = (BudRules)level.Rules;
            var layout = grove.Layout;

            // Room above par is `spare`, in taps, so a budgetFactor on a grove is a number that
            // does nothing. Refused rather than ignored, for ChapterDto.order's reason. A negative
            // factor still means "cannot be lost", which is what an opening level authors.
            if (level.Tuning.BudgetFactorIsIgnored)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove authors budgetFactor {level.Tuning.BudgetFactor:0.##}, which " +
                    "does nothing: room above par is 'spare', counted in taps. Use 'spare', or a " +
                    "negative budgetFactor if it is meant to be unlosable"));

            // Two things are checkable by *looking*, and both would otherwise come back from the
            // search as "nobody can finish this" — which is true and tells the author nothing
            // about what to move.
            Reachable(layout, issues);
            Settled(layout, issues);

            // **Old wood is retired from this mode.** The parser still understands `#` — the
            // character is shared vocabulary with Groovekeeper and a second rule about it would
            // be a second thing to keep in step — but a barrier is the one object here that can
            // only ever make a chain *shorter*, and a mode whose whole product is the chain has
            // nothing to gain from one. Warned rather than refused, because the refusal belongs
            // to whoever is authoring rather than to the parser (`bud_wood` is a retired lesson
            // id and must never be reused).
            if (layout.Stones > 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this grove stands {layout.Stones} cell(s) of old wood on it. Budburst does " +
                    "not use it: a chain stops dead at a barrier, so the only thing wood can do " +
                    "to a cascade is cut it short, which is the opposite of what this mode is " +
                    "for. Use bare ground, or a cocoon"));

            var survey = BudSolver.Survey(layout);

            if (!survey.Proved)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove could not be proved inside {BudSolver.NodeBudget} positions " +
                    $"(it looked at {survey.Nodes}) or within {BudSolver.MaxTaps} taps. It may be " +
                    "unsolvable, or simply too expensive to prove — either way it cannot ship, " +
                    "because the player's device runs the same search to work out par"));
                return;
            }

            if (!survey.IsSolvable)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "no order of taps frees every critter on this grove, so nobody can finish " +
                    "it — every play was searched and none won"));
                return;
            }

            if (survey.Nodes > NodeCeiling)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"proving this grove took {survey.Nodes} positions, above the {NodeCeiling} " +
                    "a level may cost. The player's device runs this same search when somebody " +
                    "opens the level. Cost goes as the bud count to the power of par, so the " +
                    "cheapest fix is a shorter answer — a cocoon nearer the powder"));
            else if (survey.Nodes > NodeWarning)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"proving this grove took {survey.Nodes} positions against the " +
                    $"{NodeWarning} a level is expected to cost (the refusal is at {NodeCeiling})"));

            // The same three-line check every mode with a fail line gets. Shared rather than
            // restated: a second copy of "is this ladder ordered" is a second thing to keep in
            // step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            // Invariant 5d, read backwards. One shortest play is a grove that has to be solved
            // rather than played.
            if (survey.Ways < TooFewWays)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"there is only one play of {survey.Par} taps that frees every critter here, " +
                    "so this grove is a puzzle rather than a place to make a mess. Add a bud, " +
                    "ripen one, or move a cocoon so more than one chain reaches it"));

            // And the bar this mode actually has. A player who never looks past this tap is the
            // player this mode is for, and a grove they cannot finish inside the satchel is
            // asking for more than the mode promises.
            int careless = BudSolver.Careless(layout, level.Tuning.MoveBudget);

            if (careless < 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "a player who always taps whatever sets off the biggest chain never finishes " +
                    "this grove. That is the bar this mode is held to rather than a difficulty " +
                    "reading — everywhere else it would be a compliment, and here it means the " +
                    "board is asking to be solved"));
            else if (careless > level.Tuning.MoveBudget)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"a careless player takes {careless} taps against a satchel of " +
                    $"{level.Tuning.MoveBudget}, so they run out"));
        }

        /// <summary>
        /// A cocoon nothing can ever burst beside is a critter nobody can free, and saying which
        /// one is worth far more than the search's own verdict.
        /// </summary>
        static void Reachable(BudLayout layout, List<LevelIssue> issues)
        {
            var beside = new List<int>(4);

            for (int i = 0; i < layout.Count; i++)
            {
                if (!layout.IsCocoon(i)) continue;

                // A cocoon is cracked by a burst on a cell beside it, and a burst can only ever
                // happen where a flower is standing now — nothing here grows one back.
                bool reachable = false;
                layout.Beside(i, beside);
                for (int j = 0; j < beside.Count; j++)
                    if (layout.IsFlower(beside[j])) { reachable = true; break; }

                if (reachable) continue;

                int x = i % layout.Width, y = i / layout.Width;
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"the cocoon at {x},{y} has no flower beside it, and nothing in a grove ever " +
                    "grows one — so no bunch can ever crack it and that critter can never be " +
                    "freed"));
            }
        }

        /// <summary>
        /// A grove has to be authored settled. Three alike already touching would go off before
        /// anybody had touched the board, which is a level that plays itself.
        /// </summary>
        static void Settled(BudLayout layout, List<LevelIssue> issues)
        {
            if (!new BudBoard(layout).AnyBunch()) return;

            issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                $"this grove already has {BudLayout.Bunch} or more alike touching, so it would " +
                "go off before the player had done anything. Author it settled — every bunch on " +
                "the board should be one somebody made"));
        }
    }
}
