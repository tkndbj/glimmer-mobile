using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What is walking down the hill: an ordinary raider, a brute, or the warlord.
    ///
    /// <b>An enum rather than a second bool, and the third member is why.</b> It was
    /// <c>bool Brute</c>, which is exactly right for two kinds and becomes a pair of flags that
    /// can both be true the moment there is a third — and every table keyed on it (health, march,
    /// blow, how far it comes) would then have had to agree about which flag wins.
    /// </summary>
    public enum SiegeKind
    {
        Creeper,
        Brute,

        /// <summary>The warlord: it holds the middle of the hill and hits the line from there.</summary>
        Boss,

        /// <summary>
        /// The overlord: what the last rung of a chapter ends on.
        ///
        /// <b>Appended rather than inserted</b>, because these ordinals reach analytics on every
        /// run this mode has ever recorded — the same rule <c>DefeatReason</c> keeps its retired
        /// members for.
        /// </summary>
        Overlord,

        /// <summary>The blightcaller: it puts a ward out rather than taking it down.</summary>
        Blightcaller,

        /// <summary>The warbringer: it roars, and the hill charges.</summary>
        Warbringer,

        /// <summary>
        /// The bulwark: it carries a shield, so it walks slowly and shrugs off anything that is
        /// not its own colour.
        ///
        /// <para>
        /// <b>Appended</b>, for the reason every member of this enum is: the ordinals reach
        /// analytics on every run this mode has recorded.
        /// </para>
        /// <para>
        /// <b>It is the first raider whose answer is a colour rather than a quantity.</b> A
        /// creeper and a brute differ only in how much of the same thing they need — health,
        /// speed, blow — so a player beats both by matching more. A bulwark halves every bolt
        /// that is not its own colour and takes its own colour in full (<see
        /// cref="SiegeTuning.ShieldSoakTenths"/>), which puts a <b>fourfold</b> spread between
        /// feeding the right ward and feeding any other: the elemental double is already 2x, and
        /// the shield makes the wrong answer 0.5x. That is the one raider on this hill that
        /// punishes taking the biggest match on the field, which is the mistake this mode is
        /// about.
        /// </para>
        /// </summary>
        Bulwark,

        /// <summary>
        /// The weaver: it stops halfway down the hill and spins webs over the field.
        ///
        /// <para>
        /// <b>The first raider in this mode that attacks the board rather than the line.</b>
        /// Every kind before it differs from a creeper in how much of the same thing it needs —
        /// more health, a shield, a spell aimed at a ward — so the hill could only ever hurt the
        /// wards, and the field was a fuel tap the player operated while looking somewhere else.
        /// A weaver reaches the other way: it takes cells out of the field, so the answer to it
        /// is on the board the player is already touching.
        /// </para>
        /// <para>
        /// <b>It never reaches the line and takes no ward health at all</b>
        /// (<see cref="SiegeTuning.EndangersTheLine"/> answers false), which is what makes it a
        /// <em>pressure</em> rather than a threat: a level whose only raiders were weavers could
        /// not be lost, so <c>ModeValidator</c> refuses one. What it costs is time — every web is
        /// a cell that can never be matched until it is dead.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Weaver,

        /// <summary>
        /// The thief: it stops further down the hill than a weaver and takes gems off the field
        /// altogether, leaving a sack where each one stood.
        ///
        /// <para>
        /// <b>A weaver's web and a thief's sack are the two halves of one idea and they are
        /// deliberately not the same.</b> A web is <em>inconvenient</em> — the gem is still there,
        /// still that colour, and a player can still see what they are being denied. A sack is
        /// <em>dead</em>: it is not a colour at all, so it can never line up with anything and it
        /// falls to the bottom of the field like silt. Answering a weaver is playing around it;
        /// answering a thief is killing it before the field silts up.
        /// </para>
        /// <para>
        /// <b>Both are undone by its death, and that is what makes killing one a payoff rather
        /// than a relief</b> (invariant 20m: the event is the reward, so it gets the biggest
        /// drawing). Every sack bursts back into a gem in the same beat.
        /// </para>
        /// </summary>
        Thief,
    }
}
