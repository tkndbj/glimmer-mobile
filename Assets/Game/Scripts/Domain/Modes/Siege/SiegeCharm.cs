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
    /// <b>Three, and each one takes a different thing</b> — invariant 37z's test about bosses,
    /// asked of a payoff. A prism decides a <em>colour</em>, a lance decides a piece of the
    /// <em>board</em>, and a stormglass decides a moment on the <em>hill</em>. Three readings of
    /// "clears a lot of gems" would be one charm in three tints, which is the failure that rule
    /// was written for.
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
        /// <b>It runs out, and that is worth knowing before a fifth chapter is commissioned</b>
        /// (invariant 37br's argument about boss verbs). Three charms means three chapters before
        /// the cadence needs a fourth charm, which is code rather than content.
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

        /// <summary>Whether this charm's payoff lands on the hill rather than on the field.</summary>
        public static bool ReachesTheHill(SiegeCharm charm) => charm == SiegeCharm.Storm;
    }
}
