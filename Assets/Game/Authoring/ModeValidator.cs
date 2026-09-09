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
            new PrismValidator(),
            new SiegeValidator(),
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

            // Invariant 5d asked of the cogs, and it is a *certainty* rather than a reading: a
            // level that deals a cog on a line where every ward is already at the top of the
            // ladder would be dealing an object that rejects nothing. It cannot happen today
            // (a ward starts at rank nought), so what this really catches is the reverse — a
            // level that never deals one and stands one on its opening field is fine, and a level
            // that deals them onto a two-ward line is worth saying out loud.
            if (layout.Cogs > 0 && layout.Wards.Length < 3)
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    $"this siege deals cogs onto a line of {layout.Wards.Length} wards, so which "
                    + "one an upgrade goes to is very nearly a coin toss - a cog asks the player "
                    + "which colour to spend, and a short line is a short question"));

            // Invariant 5d asked of each boss's own spell, which is the reading four bosses
            // needed and two would never have: each of the four takes a *different* thing, so
            // "there is a boss" stopped being a fact anything could act on and each one has to be
            // asked whether the thing it takes is there to take.
            if (layout.HasBoss) Bossed(layout, issues);

            // Certain: a run that cannot be lost. A level whose raiders could never break a ward
            // is one where the line is a picture of a threat rather than a threat (invariant 24
            // read one step further in - an opening level may want exactly that, and should say
            // so by being the opening level rather than by accident).
            if (!Threatens(layout))
                issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                    "no wave here holds enough raiders to bring a ward down even if every one of "
                    + "them reached the line, so this siege cannot be lost"));
        }

        /// <summary>
        /// Whether this level's boss has anything to take.
        ///
        /// <para>
        /// <b>One question per spell, because the four take four different things.</b> Invariant
        /// 5d is usually asked of a board — does this mechanic reject any arrangement — and a boss
        /// is the one object here placed by the *level* rather than dealt, so the same question is
        /// asked of what the level surrounds it with. Every one of these is a warning: a chapter's
        /// first rung may legitimately carry a boss as the thing being taught rather than the
        /// thing being answered.
        /// </para>
        /// </summary>
        static void Bossed(SiegeLayout layout, ICollection<LevelIssue> issues)
        {
            string who = SiegeTuning.NameOf(layout.BossKind);

            switch (SiegeTuning.SpellOf(layout.BossKind))
            {
                // A douse takes one ward out of a line for five seconds. On a line of two that is
                // half the player's answer gone every few seconds, which is not a decision about
                // which colour to feed - it is a coin toss between the one that is left and
                // nothing.
                case SiegeSpell.Douse when layout.Wards.Length < 3:
                    issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                        $"this siege ends with a {who}, which puts a ward out every few seconds, "
                        + $"onto a line of {layout.Wards.Length} - a douse asks which colour is "
                        + "worth feeding next, and with one ward left standing there is no next"));
                    break;

                // A sunder takes a rank the player earned. A level that never deals a cog has no
                // ranks on it, so the overlord's own half of its spell is decoration and what is
                // left is a warlord with a bigger number - which is exactly the fault four bosses
                // exist to fix.
                case SiegeSpell.Sunder when layout.Cogs <= 0 && !Standing(layout):
                    issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                        $"this siege ends with an {who}, whose spell knocks a rank off the ward it "
                        + "hits, but nothing here ever deals a cog - so there is never a rank to "
                        + "take and half of what makes it an overlord rejects nothing"));
                    break;

                // Half a warbringer's roar is a rally, and a rally needs something to rally. It
                // comes early on purpose (`SiegeTuning.RestBefore`), so what this really catches
                // is a level that sends it after a wave too small to still be walking.
                case SiegeSpell.Rally when Coming(layout) < 4:
                    issues.Add(new LevelIssue(LevelIssueSeverity.Warning,
                        $"this siege ends with a {who}, half of whose roar sets the hill charging, "
                        + $"and only {Coming(layout)} raiders come before it - it will arrive onto "
                        + "an empty hill and rally nothing"));
                    break;
            }
        }

        /// <summary>Whether a cog is standing on the authored field.</summary>
        static bool Standing(SiegeLayout layout)
        {
            for (int i = 0; layout.Grid != null && i < layout.Grid.Count; i++)
                if (layout.Grid.At(i) == SiegeLayout.Cog) return true;

            return false;
        }

        /// <summary>
        /// How many raiders are in the last wave before the boss.
        ///
        /// The last one rather than all of them, because a warbringer's quiet
        /// (<c>SiegeTuning.WarbringerAfter</c>) is shorter than <c>BetweenWaves</c> - so the only
        /// wave that can still be walking when it arrives is the one immediately in front of it.
        /// </summary>
        static int Coming(SiegeLayout layout)
        {
            int last = layout.BossWave >= 0 ? layout.BossWave - 1 : layout.Waves.Length - 1;
            return last < 0 || last >= layout.Waves.Length ? 0 : layout.Waves[last].Length;
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
        /// <para>
        /// <b>Two blows a raider, and the one it used to count was wrong.</b> The first version
        /// counted a single blow each and called itself conservative — "a level this names really
        /// cannot be lost". It is not: a raider that reaches the line goes on swinging every
        /// <see cref="SiegeTuning.BlowEvery"/> until something kills it, so eight creepers standing
        /// at an unfed line are worth several times what one blow each counts. The old bar named
        /// half a shipped chapter as unlosable and every one of those rungs bleeds the line when it
        /// is played.
        /// </para>
        /// <para>
        /// Two is a floor that is actually a floor — a raider that arrives at all gets a second
        /// swing in unless the ward it walked to is already firing at it — and what genuinely
        /// measures the threat is <c>SiegeRuleTests.AnUnhurriedPlayerHoldsThisLine</c>, which plays
        /// every rung and fails on a line that finishes untouched (invariant 37j).
        /// </para>
        /// </summary>
        const int SwingsBeforeAnswered = 2;

        static bool Threatens(SiegeLayout layout)
        {
            // A warlord or an overlord holds the middle of the hill and throws *health* off the
            // line for as long as it is alive, so it is a threat by construction - there is no
            // arrangement of a siege that sends one in which the line is safe.
            //
            // **Two of the four bosses are not**, and that is the clause a fourth boss bought. A
            // blightcaller takes fuel and a warbringer takes time; neither can bring a ward down,
            // so a level whose only threat were one of them could not be lost at all — which is
            // invariant 5d asked of a fail state, and exactly the reading "there is a boss, so the
            // line is in danger" would have got wrong in silence.
            if (layout.HasBoss && SiegeTuning.EndangersTheLine(layout.BossKind)) return true;

            for (int w = 0; w < layout.Waves.Length; w++)
            {
                int blow = 0;
                for (int i = 0; i < layout.Waves[w].Length; i++)
                    blow += SiegeTuning.BlowOf(layout.KindAt(w, i));

                if (blow * SwingsBeforeAnswered >= SiegeTuning.WardHealth) return true;
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
}
