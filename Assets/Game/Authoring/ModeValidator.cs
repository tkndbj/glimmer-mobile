using System;
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
            new MarchValidator(),
            new EmberValidator(),
            new PrismValidator(),
            new SiegeValidator(),
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

            // Glass is only ever removed by light reaching it, and light only ever comes from a
            // burst — so a well with no mote in it can never charge a lens and can never lose
            // one. The search below proves it unwinnable, but only after spending the whole node
            // budget failing to, and it says so in words about a search rather than in words
            // about the board.
            //
            // A whorl counts on the wrong side of this rather than being left out of it, and the
            // reason is worth stating: a whorl is always *removable* — a drop opens one and one
            // with nothing beside it closes — but it emits no light whatever. It gives back the
            // motes it drew in, so a well holding only glass and whorls has nothing to draw and
            // nothing to cook, and the glass in it is there for ever.
            if (layout.Lenses > 0 && layout.Lenses + layout.Whorls == layout.Motes)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"every one of this well's {layout.Motes} cells is glass or a whorl, and " +
                    "glass is only ever filled by light that has already travelled. With no mote " +
                    "to cook there can never be a burst, so nothing here can ever be charged or " +
                    "got rid of"));

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

            // Invariant 5d, counted, for the one object in this mode that is not made of light.
            // A lens costs three drops of three colours to fill, and then fires sideways in
            // white. Glass whose both shots leave the well the moment it sets off has taken the
            // whole of that plan and bought nothing with it, and a chapter authored that way is
            // a chapter of ordinary wells with beads in them.
            //
            // Counted out of two rather than out of four, because a lens filled the ordinary way
            // fires sideways: a well has gravity, so a lens rests on something and its downward
            // beam always lands, on the cell holding it up, having crossed nothing. All four are
            // only fired by a lens another lens strikes, and where a lens has fallen to by then
            // is not a position any cheap check can enumerate.
            //
            // Geometry of the authored position rather than a proof — a well collapses under a
            // chain, so a lens fires from wherever it has fallen to — so it is said out loud
            // rather than refused. Note what needs no check at all: a lens can only leave the
            // well by firing, so a board the search proves emptiable is a board where every lens
            // on it is filled and fired. Glass cannot go uncharged and ship.
            if (layout.Lenses > 0 && survey.Aim < 1)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this well stands {layout.Lenses} lens(es) and not one of them is pointing " +
                    "sideways at anything: both shots would leave the well the moment it set " +
                    "off, so three drops of charging would buy nothing and the board would play " +
                    "the same without the glass. Stand a lens on the floor with its light across " +
                    "a gap, or looking into a blob the wash cannot reach"));

            // Invariant 5d, counted, for the object the third chapter is built on — and unlike
            // the lens's `aim` this one is exact rather than geometry, because **gravity never
            // moves a whorl sideways**. A whorl draws in the cells to its left and its right, so
            // the two columns it can ever draw from are the two it is authored between, whatever
            // the well collapses into. One standing against a wall therefore has one side for
            // its whole life and can never merge a pair: it can shift a single mote one column
            // in and then close, which is a hole the player had to poke rather than a decision
            // they made.
            //
            // A warning rather than a refusal, because moving one mote inward is a real if small
            // effect and the first board of a chapter may legitimately carry one as scenery. The
            // reading that actually condemns a board is `kindled` — how often a merge reached
            // white along a shortest solution — and that needs the search's winning line, which
            // is what `Tools/verify/fall.py` and the ladder fixtures report.
            int walled = 0;
            for (int at = 0; at < layout.Count; at++)
            {
                if (!FallCell.IsWhorl(layout.At(at))) continue;

                int x = at % layout.Width;
                if (x == 0 || x == layout.Width - 1) walled++;
            }

            if (walled > 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{walled} of this well's {layout.Whorls} whorl(s) stand against a wall, and " +
                    "gravity never moves a whorl sideways — so those can only ever draw in one " +
                    "mote and can never merge a pair, which is the whole of what a whorl is for. " +
                    "Stand them at least one column in"));

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
    /// Everything a prototype mode has to prove, once.
    ///
    /// <para>
    /// <b>A shared validator because they share a shape, not a rule.</b> Such a mode authors a
    /// whole board, searches it for par with <see cref="ProtoSearch"/> and derives every graded
    /// number from the answer - so "was it proved", "how many ways", "does careless play finish
    /// it", "is the ladder ordered" and "does it cost too much to prove on a phone" are one set of
    /// questions with one set of words. What a subclass adds is the handful of things only its own
    /// rules can ask. It was written for five modes and four of them were withdrawn; the split
    /// held, which is the argument for it.
    /// </para>
    /// <para>
    /// <b>The node figures are about the player's device, not this one.</b>
    /// <see cref="ProtoSearch.NodeBudget"/> is large because it has to make a genuinely hard board
    /// <em>provable</em>; these two are the separate question of what that proof costs where it is
    /// actually paid - once per level, on a phone, on the way into it (invariant 26d).
    /// </para>
    /// </summary>
    /// <summary>
    /// Thornwatch. What a siege has to prove, given that nothing about it can be searched.
    ///
    /// <para>
    /// <b>It is not a <see cref="ProtoValidator"/> and cannot be</b>, because every one of that
    /// class's checks rests on a breadth-first walk of a fixed future: par, <c>ways</c>,
    /// <c>careless</c> and the node budget are all readings of a state graph. A hill with raiders
    /// walking down it while nobody is touching the board has no such graph. What is left is
    /// arithmetic and composition, and the honest thing is to say so rather than to run a search
    /// over a position that pretends to be one.
    /// </para>
    /// <para>
    /// So these are all <em>certainties</em> — things provable about the file rather than
    /// measurements of play. The one reading that is not provable here is the one the mode has to
    /// be judged on by somebody playing it: whether par is a line a good run can land under. That
    /// is on the owed list rather than in this file, because no check can answer it.
    /// </para>
    /// </summary>
    sealed class SiegeValidator : ModeValidator
    {
        public override GameMode Mode => GameMode.Siege;

        /// <summary>
        /// The shortest siege worth shipping, in matches.
        ///
        /// A level whose par is under this is over before the fuel has faded once, so nothing it
        /// is built on ever gets to bite - which is invariant 5d asked of a level rather than of a
        /// mechanic.
        /// </summary>
        const int ShortestPar = 6;

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var rules = (SiegeRules)level.Rules;
            var layout = rules.Layout;

            // The same three-line check every mode gets, shared rather than restated. It returns
            // early on an unbudgeted level, which every siege is - what is proved here is that
            // three stars still asks for fewer matches than two.
            LevelValidator.CheckStarBands(level, issues);

            // Certain, and the one thing that could make a siege unlosable by accident: with a
            // budget turned off, the ward line is the only fail state there is.
            if (level.Tuning.HasBudget)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "this siege authors a move budget. A siege is lost when the last ward falls, "
                    + "so an allowance would be a second fail state and its readout would count "
                    + "down to an ending that never happens - author budgetFactor -1"));

            int par = level.Tuning.Par;

            if (par < ShortestPar)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this siege is over in {par} matches at best, which is under the "
                    + $"{ShortestPar} it takes for a ward's fuel to fade and be wanted again - so "
                    + "nothing this mode is built on ever gets to bite. Send more, or send "
                    + "tougher"));

            if (layout.Waves.Length < 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "this siege sends one wave, so it never lets up and never comes back - a wave "
                    + "arrives when the last one is gone, and the gap between them is the only "
                    + "time a player has to look at the field"));

            // Invariant 5d, asked of the ward line. A ward whose colour never walks down the hill
            // is one whose bolts are always worth half, so the gems that feed it are worth half
            // too - and a quarter of the field is then material that only breaks up other
            // people's runs.
            for (int w = 0; w < layout.Wards.Length; w++)
            {
                if (Sends(layout, layout.Wards[w])) continue;

                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"nothing coming down this hill wears '{layout.Wards[w]}', so that ward's "
                    + "bolts are always worth half and the gems that feed it are worth half with "
                    + "them. Either send some, or take the ward off the line"));
            }

            // The other half of the same rule, and the one that decides whether the *colour* of a
            // match is a decision at all: a hill of one colour is a hill where every match that
            // is not that colour is wasted and every one that is, is obvious.
            if (Colours(layout) < 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "everything coming down this hill wears one colour, so which ward to feed is "
                    + "not a question - the whole decision in this mode is which colour is wanted "
                    + "next"));

            // Certain: a run that cannot be lost. A level whose raiders could never break a ward
            // is one where the line is a picture of a threat rather than a threat (invariant 24
            // read one step further in - an opening level may want exactly that, and should say
            // so by being the opening level rather than by accident).
            if (!Threatens(layout))
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no wave here holds enough raiders to bring a ward down even if every one of "
                    + "them reached the line, so this siege cannot be lost"));
        }

        /// <summary>Whether anything on the hill wears this colour.</summary>
        static bool Sends(SiegeLayout layout, char colour)
        {
            for (int w = 0; w < layout.Waves.Length; w++)
                for (int i = 0; i < layout.Waves[w].Length; i++)
                    if (char.ToLowerInvariant(layout.Waves[w][i]) == colour) return true;

            return false;
        }

        /// <summary>How many colours walk down this hill.</summary>
        static int Colours(SiegeLayout layout)
        {
            var seen = new HashSet<char>();

            for (int w = 0; w < layout.Waves.Length; w++)
                for (int i = 0; i < layout.Waves[w].Length; i++)
                    seen.Add(char.ToLowerInvariant(layout.Waves[w][i]));

            return seen.Count;
        }

        /// <summary>
        /// Whether one wave could ever fell a ward.
        ///
        /// Deliberately generous - it assumes every raider of a wave arrives and swings at the
        /// same ward, which is the best case for the raiders and so under-reports. A level this
        /// names really cannot be lost; one it does not name may still be easy.
        /// </summary>
        static bool Threatens(SiegeLayout layout)
        {
            for (int w = 0; w < layout.Waves.Length; w++)
            {
                int blow = 0;
                for (int i = 0; i < layout.Waves[w].Length; i++)
                    blow += SiegeTuning.BlowOf(char.IsUpper(layout.Waves[w][i]));

                if (blow >= SiegeTuning.WardHealth) return true;
            }

            return false;
        }
    }

    abstract class ProtoValidator : ModeValidator
    {
        const int NodeWarning = 30_000, NodeCeiling = 90_000;

        /// <summary>
        /// Above this many shortest answers the board is not deciding much (invariant 5d).
        ///
        /// A warning at the top and nothing at the bottom, which is the ordinary reading: none of
        /// these five is commissioned to be effortless the way Budburst is, so a single forced
        /// answer is a legitimate opening board rather than a fault.
        /// </summary>
        const int TooManyWays = 300;

        /// <summary>What this mode calls the thing a move is spent on, for the messages.</summary>
        protected abstract string Noun { get; }

        /// <summary>Anything only this mode's rules can ask. Runs after the board is proved.</summary>
        protected virtual void Inspect(ProtoLevelRules rules, LevelDefinition level,
                                       ProtoAnswer answer, List<LevelIssue> issues) { }

        public override void Validate(LevelDefinition level, List<LevelIssue> issues)
        {
            var rules = (ProtoLevelRules)level.Rules;

            // Room above par is `spare`, counted in moves, so a budgetFactor here is a number that
            // does nothing. Refused rather than ignored, for ChapterDto.order's reason - and
            // refused rather than honoured, because two ways to say one thing is how they come to
            // disagree. A negative factor still means "cannot be lost", which is not an override
            // and is what the first board of every mode is authored with (invariant 24).
            if (level.Tuning.BudgetFactorIsIgnored)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this board authors budgetFactor {level.Tuning.BudgetFactor:0.##}, which " +
                    "does nothing: room above par here is 'spare', counted in moves, because a " +
                    "wrong move costs the same wherever it happens. Use 'spare', or a negative " +
                    "budgetFactor if it is meant to be unlosable"));

            var answer = ProtoSearch.Solve(rules.Opening());

            if (!answer.Proved)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this board could not be proved inside {ProtoSearch.NodeBudget} positions " +
                    $"(it looked at {answer.Nodes}) or within {ProtoSearch.MaxDepth} moves. It " +
                    "may be unsolvable, or simply too big to prove - either way it cannot ship, " +
                    "because the player's device runs the same search to work out par"));
                return;
            }

            if (!answer.Solvable)
            {
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    "no sequence of moves finishes this board, so nobody can clear it - every " +
                    "arrangement was searched and none won"));
                return;
            }

            if (answer.Nodes > NodeCeiling)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"proving this board took {answer.Nodes} positions, above the {NodeCeiling} " +
                    "a level may cost. The player's device runs this same search when somebody " +
                    "opens the level, so this is about a quarter of a second of nothing " +
                    "happening on the way in. Cost goes roughly as the board size to the power " +
                    "of par, so the cheapest fixes are a smaller board or a shorter answer"));
            else if (answer.Nodes > NodeWarning)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"proving this board took {answer.Nodes} positions against the " +
                    $"{NodeWarning} a level is expected to cost. It ships - the refusal is at " +
                    $"{NodeCeiling} - but the player's device runs this same search when " +
                    "somebody opens the level"));

            // The same three-line check every mode with a fail line gets. Shared rather than
            // restated: a second copy of "is this ladder ordered" is a second thing to keep in
            // step with LevelTuning (invariant 9a).
            LevelValidator.CheckStarBands(level, issues);

            if (answer.Ways > TooManyWays)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{answer.Ways} different runs of {answer.Par} moves finish this board, so " +
                    "almost any play wins and the arrangement is deciding nothing - add stone, " +
                    $"move a {Noun} further out of reach, or take a move away from the answer"));

            // Reported rather than gated. On a chapter's opening levels thoughtlessness is
            // supposed to work - that is what teaching the verb looks like - so this is a reading
            // for the author, and a chapter's ladder is where it stops being true.
            int careless = ProtoSearch.Careless(rules.Opening(), level.Tuning.MoveBudget);
            if (careless > 0 && answer.Par > 2)
            {
                // Spelt out rather than printed, because a board authored without a fail line
                // carries `int.MaxValue` and "an allowance of 2147483647" is a sentence that
                // reads as a bug in the checker rather than as a fact about the level.
                string allowance = level.Tuning.HasBudget
                                 ? level.Tuning.MoveBudget.ToString() : "none at all";

                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"a player who never looks ahead finishes this board in {careless} moves " +
                    $"against an allowance of {allowance}, so it can be cleared by " +
                    "always taking the biggest thing going - fine early in a chapter, and worth " +
                    "knowing later in one"));
            }

            Inspect(rules, level, answer, issues);
        }
    }

    /// <summary>
    /// Hollowmarch. What a haul-road has to prove on top of being solvable.
    ///
    /// <para>
    /// Everything below is a reading the search cannot give in words an author could act on.
    /// "No sequence of cores finishes this board" is true and useless; "the warden at position
    /// nine can never be scrapped, because no shot on this road ever destroys five pods and so
    /// no Spark can ever be forged" is the sentence that names the thing to move. The refusals
    /// about the board's opening state — a road that forks, a line already three alike, nothing
    /// to fire at — live in the mode's own reader, because a player's build runs that reader and
    /// must not open a board that cannot be played.
    /// </para>
    /// </summary>
    sealed class MarchValidator : ProtoValidator
    {
        public override GameMode Mode => GameMode.March;
        protected override string Noun => "pod";

        /// <summary>
        /// How much road a line may have behind it before the march stops meaning anything.
        ///
        /// The march is this mode's allowance drawn on the board, so a road long enough that the
        /// raiders could never reach the gate inside the allowance is a road where the pressure
        /// is a picture of a threat rather than a threat. Generous, because a teaching board
        /// wants exactly that picture and nothing more (invariant 24 read one step further in).
        /// </summary>
        const int Slack = 6;

        protected override void Inspect(ProtoLevelRules rules, LevelDefinition level,
                                        ProtoAnswer answer, List<LevelIssue> issues)
        {
            var layout = ((MarchRules)rules).Layout;
            var board = MarchBoard.Build(layout);

            int budget = level.Tuning.HasBudget ? level.Tuning.MoveBudget : 0;
            var reading = MarchReading.Of(layout);

            // Certain, and the search would only say "unsolvable". A warden wears two plates, so
            // it takes two separate blasts beside it or one lance — and a lance exists only if
            // some shot on this road can ever take ForgeAt pods. That is a sentence an author
            // can act on where "no sequence of cores wins" is not.
            if (reading.Wardens > 0 && reading.Forged == 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this road walks {reading.Wardens} warden(s) and no shortest run of it " +
                    $"ever forges a Spark. A warden wears two plates, so it wants either two " +
                    "blasts beside it or the one core that cuts plating - check it can be " +
                    "reached at all"));

            // Invariant 5d, asked of the thing this mode's payoff is made of. The reading is
            // taken over **every** shortest solution rather than over the opening move, and that
            // is the whole point of it: a line whose very first shot sets off a four-wave chain
            // is a line that is *over* in three shots, so an opening-move reading selects for
            // short boards and quietly punishes the good ones (invariant 26h's `kindled`, and
            // Budburst's `fired` before it).
            if (reading.Chained < 2 && answer.Par > 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no shortest run of this board ever sets off a second wave, so nothing on " +
                    "it chains - which is this mode with its payoff taken out. Move a pod so " +
                    "that closing one gap brings three more together"));

            // A Spark that cannot be made on the board as dealt is a Spark the author placed
            // rather than one the player earned, and invariant 20m says that is not a payoff at
            // all. Only asked of a road long enough to have room for one: on a teaching board a
            // player is still working out what a match is.
            if (reading.Forged == 0 && answer.Par > 4)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no shortest run of this board forges a Spark, so the only way one ever " +
                    "reaches the player's hands here is by luck. Leave a run where a chain can " +
                    $"take {MarchLayout.ForgeAt} pods at once"));

            // The march is the allowance made visible, so the two have to be in step. Read
            // against `Menace` — when a *goal* reaches the gate and the line jams — rather than
            // against when the first plain pod goes through, because losing a crate costs
            // material and losing nothing at all costs the mode its pressure.
            if (budget > 0 && reading.Menace > budget + Slack)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"the line is {reading.Menace} steps from jamming at the gate and the run " +
                    $"only lasts {budget}, so the march can never arrive and the raiders " +
                    "walking is scenery. Author the line nearer the portal"));

            // The other end of the same rule, and this one is a real cost rather than a missing
            // one: pods that go through the gate are match material the player never gets, so a
            // board that spills most of its line before par is a board whose own clock is
            // taking the answer away.
            if (budget > 0 && reading.Runway == 0 && answer.Par > 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "the line is authored already at the gate, so it starts losing a pod every " +
                    "shot from the first one. That is material leaving before the player has " +
                    "had a turn"));

            // A road with no raider is a line with nothing dividing it, so every run of one
            // colour runs into the next and most shots are as good as any other. Raiders are
            // what make *which* match a decision.
            if (reading.Haulers + reading.Wardens == 0 && answer.Par > 4)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "this road walks no haulers at all, so nothing divides the line and the " +
                    "colours alone decide it. A hauler is what makes which match a choice"));

            // Three colours across a long line makes a run of three almost unavoidable, so the
            // board keeps going off without being aimed at - 20j's solvent arriving through the
            // front door rather than through the cascade.
            if (reading.Colours < 4 && reading.Pods >= 16)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{reading.Pods} pods dealt from {reading.Colours} colours is a line that " +
                    "matches itself - add the fourth colour to the deal"));

            // The deal has to be able to reach every colour standing on the road, or a pod of
            // the missing one can never be matched however the run goes. Certain, like the
            // warden: the search would answer "unsolvable" and name nothing.
            for (int i = 0; i < layout.Line.Length; i++)
            {
                char hue = MarchLayout.Hue(layout.Line[i]);
                if (hue == '\0' || layout.Cores.IndexOf(hue) >= 0) continue;

                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"the line carries a '{hue}' pod and the magazine deals '{layout.Cores}', " +
                    "so nothing can ever be matched to it"));
                break;
            }
        }
    }

    /// <summary>
    /// Emberforge. What a wall has to prove on top of being solvable.
    ///
    /// <para>
    /// Everything below is a reading the search cannot give in words an author could act on.
    /// "No sequence of moves finishes this board" is true and useless; "the wall carries four
    /// yellow shards and it takes three in a line to fuse one, so most of that colour is
    /// confetti" is the sentence that names the thing to move. The refusals about the wall's
    /// opening state — three alike already in a line, no goal at all, nothing to swap — live in
    /// the mode's own reader, because a player's build runs that reader and must not open a
    /// board that cannot be played.
    /// </para>
    /// </summary>
    sealed class EmberValidator : ProtoValidator
    {
        public override GameMode Mode => GameMode.Ember;
        protected override string Noun => "cage";

        /// <summary>
        /// The most embers a wall may be <em>dealt</em>.
        ///
        /// <para>
        /// Invariant 20m counted, and it is the number this mode is most likely to get wrong,
        /// because dealing one is the cheapest way to make an opening board feel generous. A
        /// payoff the author placed is not a payoff — so one is a demonstration, two is a leg
        /// up, and three is a wall where the biggest thing that happens was nobody's
        /// achievement. Budburst's <c>MaxDealtSpecials</c>, and the same figure for the same
        /// reason.
        /// </para>
        /// </summary>
        const int MaxDealt = 2;

        /// <summary>
        /// How much room a wall wants above what a spendthrift can survive on it.
        ///
        /// <para>
        /// The wall is finite, so this mode has two fail states and only one of them is the
        /// meter. A board where the most extravagant possible play runs the material out
        /// <em>before</em> the allowance runs out is a board whose readout is counting down to an
        /// ending that will not be the one that happens — which reads as the game deciding on the
        /// player's behalf. Nought, because the honest bar is simply "the wall outlasts the
        /// meter"; anything more would be asking a finite wall to be generous, which is the one
        /// thing this mode is not.
        /// </para>
        /// </summary>
        const int LifeSlack = 0;

        protected override void Inspect(ProtoLevelRules rules, LevelDefinition level,
                                        ProtoAnswer answer, List<LevelIssue> issues)
        {
            var layout = ((EmberRules)rules).Layout;

            int budget = level.Tuning.HasBudget ? level.Tuning.MoveBudget : 0;
            var reading = EmberReading.Of(layout, budget);

            // Certain, and the search would only say "unsolvable". A colour with fewer than
            // three shards can never be fused into anything, so every one of them is material
            // that only breaks up other people's runs.
            if (reading.Lonely > 1)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{reading.Lonely} of this wall's colours have fewer than " +
                    $"{EmberLayout.FuseAt} shards on it, so they can never be fused into " +
                    "anything and only break up the runs of the colours that can. Either give " +
                    "them enough to matter or take them off the wall"));

            // Invariant 20m. An ember the player did not make is a payoff the author placed.
            if (reading.Dealt > MaxDealt)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this wall is dealt {reading.Dealt} embers. The whole payoff of this mode " +
                    "is the thing the player makes, so a wall handing several over has had its " +
                    $"point taken out - {MaxDealt} is a demonstration and more is a gift"));

            // Invariant 5d, asked of the thing this mode's payoff is made of, and taken over
            // **every** shortest solution rather than over the opening move (see EmberReading).
            if (reading.Chained < 2 && answer.Par > 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no shortest run of this wall ever sets off a second beat, so nothing on it " +
                    "chains - which is this mode with its payoff taken out. Leave two embers " +
                    "where one blast can reach the other, or stand a cage where a chain has to " +
                    "reach it"));

            // The other half of the same rule: a wall whose answer never makes an ember is a
            // wall being finished with what it was handed.
            if (reading.Forged == 0 && answer.Par > 1)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no shortest run of this wall ever fuses an ember, so it is finished with " +
                    "what it was dealt rather than with anything the player made"));

            // The fail state the readout is not showing. See EmberReading.Life.
            if (budget > 0 && reading.Life + LifeSlack < budget)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"a player spending everything as fast as they can runs this wall out of " +
                    $"moves in {reading.Life} against an allowance of {budget}, so the meter is " +
                    "counting down to an ending that will not be the one that happens. Pack more " +
                    "shards in, or take the allowance down with 'spare'"));

            // Stone is the only thing on the wall that shapes a beam for ever, so a wall
            // without any is one where every cross is worth the same wherever it is fired.
            if (reading.Stone + reading.Wardens == 0 && answer.Par > 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "this wall holds no stone and no warden, so nothing stops a beam and every " +
                    "cross reaches the same distance wherever it goes off. Stone is what makes " +
                    "which row a decision"));

            // Certain: a goal nothing can ever reach. A cage walled off by stone on every side
            // of both its lines can never be caught by any beam, and the search would answer
            // "unsolvable" while naming nothing.
            int walled = Unreachable(layout);
            if (walled > 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"{walled} goal(s) on this wall stand where no beam could ever arrive - " +
                    "stone blocks every approach along their own row and column, so nothing " +
                    "that happens anywhere on this board can reach them"));
        }

        /// <summary>
        /// Goals that no blast could reach from anywhere, because stone stands between them and
        /// every cell of their own row and column.
        ///
        /// <para>
        /// Deliberately a <em>necessary</em> condition rather than a sufficient one: it asks
        /// whether a beam fired from some cell on the goal's own lines would arrive, and ignores
        /// the diagonals a star adds and whether an ember could ever stand there. So it
        /// under-reports, which is the right direction for a check that errors — everything it
        /// names really is unreachable, and the search catches the rest as "unsolvable".
        /// </para>
        /// </summary>
        static int Unreachable(EmberLayout layout)
        {
            var grid = layout.Grid;
            int w = grid.Width, h = grid.Height, walled = 0;

            for (int cell = 0; cell < grid.Count; cell++)
            {
                if (!EmberLayout.IsGoal(grid.At(cell))) continue;

                int gx = cell % w, gy = cell / w;
                bool reachable = false;

                // Four approaches, each walked outward from the goal until something would stop
                // a beam coming the other way. Anything but stone lets light through, and the
                // goal itself is what the beam is looking for.
                for (int d = 0; d < EmberLayout.CrossRays && !reachable; d++)
                {
                    int x = gx, y = gy;

                    while (true)
                    {
                        x += EmberLayout.StepX[d];
                        y += EmberLayout.StepY[d];
                        if (x < 0 || y < 0 || x >= w || y >= h) break;

                        char c = grid.At(y * w + x);
                        if (EmberLayout.Stops(c)) break;

                        // A cell a gem could stand on now, or could fall into later, is a cell
                        // an ember could go off in.
                        reachable = true;
                        break;
                    }
                }

                if (!reachable) walled++;
            }

            return walled;
        }
    }

    /// <summary>
    /// Prismvale. What a field of gems has to prove on top of being solvable.
    ///
    /// <para>
    /// Everything below is a reading the search cannot give in words an author could act on. "No
    /// sequence of swaps finishes this board" is true and useless; "the critter at row 3 column 5
    /// stands in a run of gems no lantern touches, so nothing that happens anywhere on this board
    /// could ever reach it" is the sentence that names the thing to move. The refusals about the
    /// board's opening state — no critter, no lantern, a vein already touching a sleeper — live in
    /// the mode's own reader, because a player's build runs that reader and must not open a board
    /// that cannot be played.
    /// </para>
    /// <para>
    /// <b>There is no <c>life</c> clause here and there must not be.</b> Every other mode on this
    /// shape gets one because its material runs out; nothing here is ever consumed, so the
    /// allowance is the only fail state and a check asking "does the board outlast the meter"
    /// could only ever answer yes.
    /// </para>
    /// </summary>
    sealed class PrismValidator : ProtoValidator
    {
        public override GameMode Mode => GameMode.Prism;
        protected override string Noun => "critter";

        /// <summary>
        /// How much of a board may already be lit when it is dealt, as a fraction of its gems.
        ///
        /// <para>
        /// <b>Invariant 5g, which was found on glades and applies here word for word.</b> A board
        /// dealt with most of its veins already running starts half done, and nothing else would
        /// notice: it is still solvable, still correctly par'd and still fully validated. A
        /// little is the opposite of a fault — one lit gem beside each lantern is how this mode
        /// teaches itself without a sentence — so this is generous and is a warning.
        /// </para>
        /// <para>
        /// A fraction rather than a count, because a board with forty gems and one with fourteen
        /// are not the same board with the same number lit.
        /// </para>
        /// </summary>
        const int DealtLitPercent = 35;

        /// <summary>
        /// Lanterns standing against no gem at all before it is worth saying so.
        ///
        /// One is scenery and nobody minds. Several is a board that looks far richer than it
        /// plays — a lantern is the brightest thing on the field, so a walled-off one reads as a
        /// route that is not there.
        /// </summary>
        const int TooManyIdle = 1;

        protected override void Inspect(ProtoLevelRules rules, LevelDefinition level,
                                        ProtoAnswer answer, List<LevelIssue> issues)
        {
            var layout = ((PrismRules)rules).Layout;

            int budget = level.Tuning.HasBudget ? level.Tuning.MoveBudget : 0;
            var reading = PrismReading.Of(layout, budget);

            // Certain, exact, and it names the cell. This is the one thing the search genuinely
            // cannot say: it answers "unsolvable" and points at nothing.
            var marooned = layout.Marooned;
            if (marooned.Length > 0)
            {
                int cell = marooned[0];
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"the critter at row {cell / layout.Width} column {cell % layout.Width} " +
                    "stands in a run of gems that no lantern is touching — or against no gem at " +
                    "all — so nothing that happens anywhere on this board could ever wake it. " +
                    "Which cells hold gems never changes, so no arrangement fixes this: move the " +
                    "critter, move a lantern, or fill in the bare ground between them"));
            }

            // Invariant 5d, asked of the thing this mode's colour rule is made of, and taken over
            // **every** shortest solution rather than over the opening move (see PrismReading).
            if (reading.Hues > 1 && reading.Used < 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this board stands {reading.Hues} lantern colours and no shortest run of it " +
                    "ever wakes a critter with more than one of them, so the rest are decoration " +
                    "and the colour decided nothing. Move a critter nearer a lantern it is not " +
                    "already being served by, or take the spare lanterns off the board"));

            // Invariant 5g, counted. A board that starts half done passes every other gate,
            // because "how much of this is already finished" is a question nothing else asks.
            if (reading.Gems > 0 && reading.Dealt * 100 > reading.Gems * DealtLitPercent)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{reading.Dealt} of this board's {reading.Gems} gems are already lit as it " +
                    $"is dealt, which is over {DealtLitPercent}% of it — so the player is handed " +
                    "a board somebody else has half finished. A gem or two beside each lantern " +
                    "is how the mode teaches itself; a whole vein is a level already played"));

            // Bare ground is the only thing on the board that shapes a vein, so a board without
            // any is one where every gem of a colour can reach every other one and the routing
            // rejects nothing. Exactly Emberforge's argument about its own stone.
            if (reading.Bare == 0 && answer.Par > 2)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "this board holds no bare ground, so nothing shapes a vein and every gem of " +
                    "a colour can reach every other one. Bare ground is what makes which route a " +
                    "decision"));

            // Material that looks like an option and is not.
            if (reading.Idle > TooManyIdle)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"{reading.Idle} lanterns on this board have no gem at all standing against " +
                    "them, so no vein could ever start there. A lantern is the brightest thing on " +
                    "the field, so each of these reads as a route that is not there"));
        }
    }

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

        /// <summary>The least par a grove may ship at. See the star-band note in <c>Validate</c>.</summary>
        const int LeastPar = 3;

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

            // Three things are checkable by *looking*, and each would otherwise come back from
            // the search as "nobody can finish this" — which is true and tells the author nothing
            // about what to move.
            Reachable(layout, issues);
            Settled(layout, issues);
            Standing(layout, issues);

            // **Old wood is retired from this mode.** The parser still understands `#` — the
            // character is shared vocabulary with Groovekeeper and a second rule about it would
            // be a second thing to keep in step — but a barrier is the one object here that can
            // only ever make a chain *shorter*, and a mode whose whole product is the chain has
            // nothing to gain from one. Warned on a still grove, because the refusal belongs to
            // whoever is authoring; **refused outright on a living one**, because there it is not
            // a taste at all — everything on a living grove falls, and a barrier that fell would
            // be a wall sliding down the board.
            if (layout.Stones > 0)
                issues.Add(new LevelIssue(
                    layout.Grows ? LevelIssueSeverity.Error : LevelIssueSeverity.Warning,
                    $"this grove stands {layout.Stones} cell(s) of old wood on it. Budburst does " +
                    "not use it: a chain stops dead at a barrier, so the only thing wood can do " +
                    "to a cascade is cut it short, which is the opposite of what this mode is " +
                    "for — and on a grove that falls there is nothing for it to stand on. Use a " +
                    "flower, or a cocoon"));

            // **A living grove is a full rectangle**, and that is not tidiness. Everything falls
            // into the holes under it and new flowers grow into whatever is left, so an authored
            // hole is a hole that exists for exactly as long as it takes the player to tap once —
            // it is drawn on the opening board, and then it is gone for ever. A board whose
            // shape only survives its own first frame is a board nobody authored.
            if (layout.Grows)
            {
                int gaps = layout.Count - layout.Flowers - layout.Cocoons - layout.Stones;
                if (gaps > 0)
                    issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                        $"this grove leaves {gaps} cell(s) of bare ground, and it is a grove that " +
                        "grows: the first chain fills every one of them and they never come back. " +
                        "A living grove is authored as a full rectangle"));
            }

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

            // **Par 3 is the floor, and it is arithmetic rather than taste.** At par 2 both star
            // lines round onto 3 (ceil(2.4) and ceil(2.8)), so the two-star band is empty and a
            // careless player drops straight from three stars to one. `CheckStarBands` reads
            // the factors and cannot see it; only the derived lines can (CRAFT.md).
            if (survey.Par < LeastPar)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove is par {survey.Par}, below the {LeastPar} a Budburst grove needs: " +
                    "at par 2 the three-star and two-star lines both round onto 3 and the middle " +
                    "band is empty. Add a cocoon the chain cannot reach in one, or move one away"));

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

            // And whether the objects are worth anything, which is the reading this mode's
            // second chapter added. See `Deciding` — invariant 26g's test, not a difficulty.
            Deciding(layout, survey, issues);

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
        /// Everything about a dealt special that can be read off the picture rather than
        /// searched for: it stands on a flower (the parser refuses anything else), and there are
        /// not so many of them that the board is dealt solved.
        /// </summary>
        static void Standing(BudLayout layout, List<LevelIssue> issues)
        {
            if (layout.Specials > MaxDealtSpecials)
                issues.Add(new LevelIssue(LevelIssueSeverity.Error,
                    $"this grove deals {layout.Specials} specials already forged, above the " +
                    $"{MaxDealtSpecials} a grove may. A special is something the player makes; " +
                    "a grove deals one only to teach what firing it does"));
        }

        /// <summary>The most specials a grove may deal already forged.</summary>
        const int MaxDealtSpecials = 2;

        /// <summary>
        /// Whether the specials decide anything, measured rather than argued about.
        ///
        /// <para>
        /// <b>This is invariant 26g's test rather than a difficulty reading</b>, and five
        /// withdrawn mechanics are why it exists at all. Two numbers: whether the board as dealt
        /// lets the player <em>forge</em> a special at all, and whether any shortest play
        /// <em>fires</em> one. A grove where neither is true is a grove of the first chapter
        /// wearing the second's name.
        /// </para>
        /// <para>
        /// A warning rather than a refusal, for <c>CheckDecidableTiles</c>' reason: a grove may
        /// forge nothing on the board <em>as dealt</em> and everything two taps in, once the
        /// grove has fallen into place.
        /// </para>
        /// </summary>
        static void Deciding(BudLayout layout, BudSurvey survey, List<LevelIssue> issues)
        {
            if (!layout.Grafts && !layout.HasSpecials) return;

            var reading = BudObjectReading.Of(layout, survey);

            if (reading.Forgeable == 0 && !layout.HasSpecials)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no opening move on this grove makes a bunch of five, so the player cannot " +
                    "forge a special on the board as dealt. Author a four somewhere the hand's " +
                    "colour completes"));

            if (reading.Fired == 0)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"none of the {reading.Ways} shortest plays fires a special, so on this grove " +
                    "the specials are never the best thing to do. Put the cocoons where a bolt's " +
                    "line or a sun's square reaches them"));
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
