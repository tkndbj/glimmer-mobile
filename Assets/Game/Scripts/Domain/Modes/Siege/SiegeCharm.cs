using System;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// A power riding on a dealt gem: what it does when that gem is cleared.
    ///
    /// <para>
    /// <b>A power on a gem rather than a fifth kind of gem, and that is the whole of why this
    /// cost the match rule nothing.</b> Every rule on this field asks one of two questions —
    /// <em>what colour is this</em> and <em>what is standing here</em> — and this mode has twice
    /// shipped a second alphabet to answer the second one (a cog in a cell, a thief's sack) and
    /// twice taken it back out (see <see cref="SiegeLayout.RetiredCog"/>,
    /// <see cref="SiegeLayout.RetiredSack"/>). A charm keeps the cell a gem: it falls, it swaps,
    /// it lines up and it is worth its colour's fuel exactly as its neighbours are, and what it
    /// adds happens at the moment it goes. <see cref="Prism"/> is the one that touches the match
    /// rule at all, and it touches it in one predicate.
    /// </para>
    /// <para>
    /// <b>Six, and each one takes a different thing</b> — invariant 37z's test about bosses,
    /// asked of a payoff. A prism decides a <em>colour</em>, a lance decides a piece of the
    /// <em>board</em>, a stormglass decides a moment on the <em>hill</em>, a furnace hands the
    /// <em>line</em> a charge, an hourglass takes the hill's <em>time</em> and an anvil takes its
    /// <em>ground</em>. Six readings of "clears a lot of gems" would be one charm in six tints,
    /// which is the failure that rule was written for.
    /// </para>
    /// <para>
    /// <b>Each of them is a decision the player can get wrong</b> (invariant 26h), and that is
    /// what separates all three from the wick: a prism spent completing a colour whose ward is
    /// full or fallen is a wild thrown away; a lance sprung on a row that carries nothing wanted
    /// clears twelve gems into wards that were not asking; and a stormglass matched over an empty
    /// hill delivers nothing at all, which is invariant 40i's rule about a bomb — <b>the decision
    /// is <em>when</em></b> — arriving on the player's own board instead of on the hill.
    /// </para>
    /// <para>
    /// <b>Appended, never renumbered.</b> These reach analytics through the run's own readings
    /// exactly as <see cref="SiegeKind"/> and <see cref="SiegeSpell"/> do, so an ordinal that
    /// moves rewrites history. A withdrawn charm is retired in place and its letter is refused by
    /// name at parse (invariant 5f), never quietly ignored.
    /// </para>
    /// </summary>
    public enum SiegeCharm
    {
        /// <summary>An ordinary gem. Nought, so an unset array is a field of plain gems.</summary>
        None = 0,

        /// <summary>
        /// The prism: a gem of no colour at all, which joins a run of <em>any</em> colour and is
        /// paid as the colour it joined.
        ///
        /// <para>
        /// <b>The one charm that is a fact about the cell rather than about what happens when it
        /// goes</b>, so it is the one that reaches <see cref="SiegeLayout.Runs"/> — and it reaches
        /// it as a single predicate rather than as a second alphabet, which is what keeps
        /// <c>Lines</c>, <c>AnySwap</c>, <c>Settle</c> and both offline mirrors asking one
        /// question about a run.
        /// </para>
        /// <para>
        /// <b>Its decision is the mode's own decision</b>, which is why it is the charm a player
        /// meets first: a siege is won by choosing which colour to fuel next, and a prism is that
        /// choice handed over whole. It is paid as the colour of the run it completed and never as
        /// the letter it happens to be carrying underneath, because a gem that paid a colour
        /// nobody could see would be a payoff the player cannot aim.
        /// </para>
        /// </summary>
        Prism = 1,

        /// <summary>
        /// The lance: when it goes, it takes its whole row and its whole column with it, and every
        /// gem that goes with it is worth its own colour's fuel.
        ///
        /// <para>
        /// <b>It rides a coloured gem, so the player reads a colour and a power at once</b> — the
        /// prism is colourless because it has to be, and nothing else here may be, since a gem
        /// whose colour cannot be read is a gem that cannot be aimed (invariant 37f).
        /// </para>
        /// <para>
        /// <b>A cross rather than a blast, and the difference is the decision.</b> A radius asks
        /// "is this neighbourhood dense"; a row and a column ask "is this <em>line</em> carrying
        /// what I want", which is a thing a player reads off the board in one glance and can be
        /// wrong about. A lance that takes another lance sets it off too, which is the only chain
        /// on this field that is not a cascade.
        /// </para>
        /// </summary>
        Lance = 2,

        /// <summary>
        /// The stormglass: when it goes, the whole hill takes a bolt, and everything wearing the
        /// charm's own colour takes it twice over.
        ///
        /// <para>
        /// <b>The one charm that reaches the hill, and the only reason it may.</b> Invariant 40h
        /// is that the hill has no business reaching into the gem board; the arrow the other way
        /// is exactly what 40i kept — the player reaching up — and a stormglass is that reach made
        /// out of the player's own material rather than out of a raider's corpse.
        /// </para>
        /// <para>
        /// <b>Its colour decides the double, which is what makes <em>when</em> a real question.</b>
        /// Under the colour lock (invariant 37bl) a ward only ever answers its own colour, and a
        /// stormglass is the one thing in the mode that answers all four at once — so it is worth
        /// most on a full hill and worth nothing at all on an empty one, and the player is the
        /// only one who can see which they are looking at.
        /// </para>
        /// </summary>
        Storm = 3,

        /// <summary>
        /// The furnace: when it goes, the ward of its colour banks a whole overcharge on the
        /// spot, as if a full tube had just been poured into it.
        ///
        /// <para>
        /// <b>The one charm that reaches the <em>line</em>, which is the fourth thing there was
        /// left to take</b> (invariant 37z's test, asked a fourth time): a prism decides a
        /// colour, a lance a piece of the board, a stormglass a moment on the hill - and a
        /// furnace hands the player the mode's own big button, loaded. What it is worth is
        /// decided by the turret standing there (a charge is that ward's capacity thrown at that
        /// ward's weight, <c>SiegeBoard.Overcharge</c>), so it scales with the shelf exactly as
        /// the stormglass was rebuilt to (invariant 37cg) and flattens nothing.
        /// </para>
        /// <para>
        /// <b>Its decision is <em>which colour</em>, and it can be wrong twice over</b>
        /// (invariant 26h): a furnace matched into a fallen ward is a charge with no turret to
        /// hold it, and one matched into a ward already holding <c>SiegeTuning.MostCharges</c>
        /// is a charge that cannot be banked. Both are refused by the ward, and the refusal is
        /// drawn (<c>SiegeView.Forged</c>), because a payoff that silently did nothing would be
        /// a broken gem rather than a wrong choice.
        /// </para>
        /// <para>
        /// <b>It lands after the gem has burst and not before</b>, booked on the same clock a
        /// match's fuel and a stormglass's bolts are (invariant 37s) - so the tube is seen to
        /// fill from the stone rather than the glyph lighting on the frame of the swap.
        /// </para>
        /// </summary>
        Furnace = 4,

        /// <summary>
        /// The hourglass: when it goes, the whole hill stands still for
        /// <c>SiegeTuning.HourglassFor</c> seconds - nothing walks, nothing swings, no boss
        /// casts - while the line keeps firing.
        ///
        /// <para>
        /// <b>The fifth thing there was left to take is <em>time</em>, and it is the hill's time
        /// rather than the player's.</b> A shackler takes the line's seconds (invariant 37cw);
        /// this takes the hill's, which is the mirror image and reads as one: every second the
        /// hill stands is a second of the line's fire landing on something that is not getting
        /// any closer. It is a stop rather than a slow, for the reason <c>WardAbility.Stun</c>
        /// is separated from <c>Frost</c> - a rate is a tuning and a stop is an event.
        /// </para>
        /// <para>
        /// <b>Its decision is <em>when</em>, and an empty hill is the wrong answer</b> - the
        /// stormglass's own test (invariant 40i's rule about a bomb, arriving on the field): it
        /// is worth what is walking when it goes, so a player who springs it on a hill nobody
        /// is on has spent it on nothing, and one who holds it for the crowd has bought the
        /// line three seconds against the whole of it.
        /// </para>
        /// <para>
        /// <b>It scales with the line by construction</b>: a stopped hill is worth exactly what
        /// the standing turrets land in the window, so a bought line gets more out of the same
        /// three seconds than the starter does, which is invariant 37cg met without a number.
        /// A boss's guard runs down through it (invariant 37dl: seconds off the fight, never a
        /// wall), so a boss stood still is a boss hurt, not a boss held.
        /// </para>
        /// </summary>
        Hourglass = 5,

        /// <summary>
        /// The anvil: when it goes, the line drives the whole hill back up the slope by
        /// <c>SiegeTuning.AnvilHeave</c> of its length. Nothing is hurt and nothing is held -
        /// the ground under everything walking is simply taken away from it.
        ///
        /// <para>
        /// <b>The sixth thing there was left to take is <em>distance</em>, and it is the only
        /// one of the six that answers the mode's own fail state.</b> A prism decides a colour,
        /// a lance a piece of the board, a stormglass a moment on the hill, a furnace a charge
        /// on the line and an hourglass the hill's time - every one of them a way of killing
        /// faster. A siege is lost when the last ward falls (invariant 37b), and until this
        /// charm nothing in the player's hands addressed that directly: the only answer to
        /// something already at the line was to kill it before it swung again. An anvil is that
        /// answer, and it is the reason the charm the sixth chapter deals is the first
        /// <em>defensive</em> payoff this mode has.
        /// </para>
        /// <para>
        /// <b>It is not the hourglass said twice, and the difference is which board you read.</b>
        /// An hourglass buys the line three seconds at the range things already stand, so it is
        /// worth most when the hill is <em>full</em>; an anvil undoes progress, so it is worth
        /// most when something is <em>close</em>. The two questions a player asks off the hill
        /// are "is there a crowd" and "is anything about to reach me", and the two charms answer
        /// one each. A stop is time and a shove is ground: <c>SiegeCharms.StopsTheHill</c> and
        /// <c>SiegeCharms.ShovesTheHill</c> are two predicates for that reason and never one.
        /// </para>
        /// <para>
        /// <b>What it is worth scales with the line without a number</b> (invariant 37cg), and it
        /// scales twice over. The seconds it buys are seconds of the standing turrets' fire, so a
        /// bought line gets more out of the same shove than the starter does; and the shove is a
        /// share of the <em>hill</em>, so a slow body is pushed back further in time than a quick
        /// one - a bulwark walks the hill in <c>SiegeTuning.BulwarkMarch</c> seconds against a
        /// creeper's <c>CreeperMarch</c>, so the same ground costs the armour more than twice as
        /// long. The charm is worth most against exactly the thing a surged chapter is made of.
        /// </para>
        /// <para>
        /// <b>Its decision is <em>when</em>, and it can be wrong two ways</b> (invariant 26h). An
        /// anvil sprung over an empty hill throws nothing, which is the stormglass's own test
        /// (invariant 40i, the decision is <em>when</em>) arriving on the field; and <b>a boss
        /// does not move</b>, so one spent on a boss wave is spent on a wave the mode never
        /// stacks anything else onto (invariant 37dn) and buys nothing at all. A boss holding
        /// its ground is a fact about the fight rather than about this charm - a phase, a guard
        /// and a floor are all measured from where it stands (invariant 37di) - and the refusal
        /// is drawn, because a payoff that silently did nothing would be a broken gem rather
        /// than a wrong choice.
        /// </para>
        /// <para>
        /// <b>The shove is paid out over a moment rather than applied to the model in one
        /// frame</b>, and that is a rule rather than a flourish: the view draws a raider where
        /// the model says it is, once a frame, so a knock-back written straight into
        /// <c>March</c> would teleport a hill of bodies and read as a glitch. The model carries
        /// the debt (<c>SiegeRaider.Heave</c>) and works it off in <c>Walk</c> at
        /// <c>SiegeTuning.AnvilPace</c>, so the drawing and the rules agree to the frame and the
        /// hold simulation sees exactly what a player does (invariant 37cq's discipline: the
        /// model is handed the seconds).
        /// </para>
        /// </summary>
        Anvil = 6,
    }

    /// <summary>
    /// The roster: which letter names each charm, in one place.
    ///
    /// <para>
    /// <b>A table rather than a <c>switch</c> per reader</b>, for <see cref="SiegeLayout.Modifiers"/>'s
    /// reason — the shield shipped as a special case in four places and every one of them would
    /// have had to be extended in step, by hand, twice more. A fourth charm is a row here, a case
    /// in <c>SiegeBoard.Detonate</c>, an entry in the art table and a lesson; it cannot be
    /// half-added, because <c>SiegeCharmTests</c> holds this list to the enum.
    /// </para>
    /// <para>
    /// <b>The letters are lower case and disjoint from everything else a body may carry.</b> A
    /// charm is never written into <see cref="SiegeLayout.Grid"/> — it is dealt, never authored
    /// (see <c>SiegeBoard.Deal</c>) — so these letters share no space with a cell, and they are
    /// not a colour, so they share none with <see cref="SiegeLayout.Letters"/> either.
    /// </para>
    /// </summary>
    public static class SiegeCharms
    {
        /// <summary>
        /// Every charm this mode has, with the letter a chapter body names it by.
        ///
        /// <b>In the order they are introduced</b>, which is a fact rather than a convenience:
        /// a chapter deals the first <em>n</em> of these, so the order here <em>is</em> the
        /// ladder, and <see cref="Upto"/> is the only place that reads it.
        /// </summary>
        public static readonly (char Letter, SiegeCharm Charm)[] Roster =
        {
            ('p', SiegeCharm.Prism),
            ('l', SiegeCharm.Lance),
            ('s', SiegeCharm.Storm),
            ('f', SiegeCharm.Furnace),
            ('h', SiegeCharm.Hourglass),
            ('a', SiegeCharm.Anvil),
        };

        /// <summary>Every letter a <c>charms</c> field may be written with.</summary>
        public static readonly string Letters = Spell();

        static string Spell()
        {
            var all = new System.Text.StringBuilder(Roster.Length);
            for (int i = 0; i < Roster.Length; i++) all.Append(Roster[i].Letter);
            return all.ToString();
        }

        /// <summary>What this letter names, or <see cref="SiegeCharm.None"/>.</summary>
        public static SiegeCharm Named(char letter)
        {
            for (int i = 0; i < Roster.Length; i++)
                if (Roster[i].Letter == letter) return Roster[i].Charm;

            return SiegeCharm.None;
        }

        /// <summary>The letter this charm is written with, or <c>'\0'</c>.</summary>
        public static char LetterOf(SiegeCharm charm)
        {
            for (int i = 0; i < Roster.Length; i++)
                if (Roster[i].Charm == charm) return Roster[i].Letter;

            return '\0';
        }

        /// <summary>
        /// The charms a chapter at this ordinal deals: the first <paramref name="ordinal"/> of
        /// the roster.
        ///
        /// <para>
        /// <b>Derived from the ordinal and then written into the body, never looked up at run
        /// time</b> — the same bargain <c>tough</c> strikes (invariant 37by) and for the same
        /// load-bearing reason: the hold simulation builds its layouts from an inline table with
        /// no catalog anywhere near it, so a charm set that came from the chapter index would be
        /// empty in every measurement this mode has. The chapter tool asks this; the board carries
        /// the answer.
        /// </para>
        /// <para>
        /// <b>It runs out, and that is worth knowing before a seventh chapter is
        /// commissioned</b> (invariant 37br's argument about boss verbs). Six charms means six
        /// chapters before the cadence needs a seventh, which is code rather than content - the
        /// fourth and fifth (<see cref="SiegeCharm.Furnace"/>, <see cref="SiegeCharm.Hourglass"/>)
        /// were the bill for the fourth and fifth chapters, paid on 2026-09-16, and the sixth
        /// (<see cref="SiegeCharm.Anvil"/>) was the bill for the sixth, paid on 2026-09-17.
        /// </para>
        /// </summary>
        public static string Upto(int ordinal)
        {
            if (ordinal <= 0) return string.Empty;

            int take = ordinal < Roster.Length ? ordinal : Roster.Length;
            return Letters.Substring(0, take);
        }

        /// <summary>
        /// Whether this charm changes what lines up, rather than what happens when a line goes.
        ///
        /// <b>Asked rather than tested against <see cref="SiegeCharm.Prism"/></b>, so the day a
        /// second colourless charm exists every reader of the match rule is already correct about
        /// it — which is the shape <c>SiegeTuning.AimsAtAWard</c> exists in for the same reason.
        /// </summary>
        public static bool IsWild(SiegeCharm charm) => charm == SiegeCharm.Prism;

        /// <summary>
        /// Whether this charm clears more of the field when it goes.
        ///
        /// <b>Its own predicate because the cascade has to know</b>: a charm that adds cells is
        /// resolved into the beat that took it, and one that does not is a payoff delivered
        /// elsewhere.
        /// </summary>
        public static bool Spreads(SiegeCharm charm) => charm == SiegeCharm.Lance;

        /// <summary>
        /// Whether this charm's payoff lands on the hill rather than on the field.
        ///
        /// <b>Two, and they take different things from it</b>: a stormglass takes health off
        /// everything standing there and an hourglass takes the hill's seconds. Both are booked
        /// to land after the gem has burst (invariant 37s), which is what this predicate is
        /// asked for.
        /// </summary>
        public static bool ReachesTheHill(SiegeCharm charm)
            => charm == SiegeCharm.Storm || charm == SiegeCharm.Hourglass;

        /// <summary>
        /// Whether this charm's payoff lands on the ward line rather than on the field or the
        /// hill. The furnace, and only the furnace: what it hands over is a charge on a turret.
        /// </summary>
        public static bool ReachesTheLine(SiegeCharm charm) => charm == SiegeCharm.Furnace;

        /// <summary>
        /// Whether this charm stops the hill's clock when it lands. The hourglass alone - see
        /// <c>SiegeBoard.Stilled</c> for what a stopped hill is.
        /// </summary>
        public static bool StopsTheHill(SiegeCharm charm) => charm == SiegeCharm.Hourglass;

        /// <summary>
        /// Whether this charm drives the hill back up the slope when it lands. The anvil, and
        /// only the anvil - see <c>SiegeBoard.Heave</c> for what a shoved hill is.
        ///
        /// <b>Its own predicate beside <see cref="StopsTheHill"/> rather than folded into it</b>,
        /// for the reason a douse and a shackle are two fields (invariant 37cw): a stop takes the
        /// hill's <em>seconds</em> and a shove takes its <em>ground</em>, they are answered by
        /// the player at opposite moments, and a reader that asked one question about "the charms
        /// that slow the hill down" would be asking about a category nobody designed.
        /// </summary>
        public static bool ShovesTheHill(SiegeCharm charm) => charm == SiegeCharm.Anvil;
    }
}
