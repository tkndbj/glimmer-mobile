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
        /// <b>Retired: nothing sends one and these two ids must never be reused.</b>
        ///
        /// The weaver spun webs over the field and the thief took gems off it. Neither was ever
        /// authored into a shipped wave — the mechanic was built, validated, arted and never sent
        /// (invariant 40a) — and both were withdrawn whole by the owner. Kept as members rather
        /// than deleted because these ordinals reach analytics on every run the endless lane ever
        /// recorded, which is the rule <c>DefeatReason</c> keeps its retired members for.
        /// </summary>
        Weaver,

        /// <summary><b>Retired with <see cref="Weaver"/>.</b> See above.</summary>
        Thief,

        /// <summary>
        /// The bomber: an ordinary raider that leaves a live bomb standing where it dies.
        ///
        /// <para>
        /// <b>It walks the hill like anything else, and everything about it happens after it is
        /// dead.</b> That is the whole design: the player does not fight a bomber differently, they
        /// deal with what it leaves — a bomb sitting on the hill at the spot it fell, which goes
        /// off the instant it is tapped and takes a firepot's worth of everything around it.
        /// </para>
        /// <para>
        /// <b>It is the one thing in this mode that makes the player touch the hill.</b> Every
        /// other input goes into the gem field; a bomb has to be found on the enemy's own ground
        /// and hit, so the hill stops being a thing that is only watched. That is the connection
        /// between the two halves of this screen, and it is the reason this raider exists.
        /// </para>
        /// <para>
        /// <b>What stops it being free damage is invariant 39</b>: the blast is charged against
        /// the graded count exactly as a firepot's is, so a player who leans on bombs pays for
        /// every point of it in the one currency this mode grades — and cannot buy a star with
        /// something a grade reaching a public board (19a) was not earned by.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Bomber,

        /// <summary>
        /// The gravemaw: it eats what the hill owes the player.
        ///
        /// <para>
        /// <b>The first boss whose spell is answered by a tap on the hill rather than by the gem
        /// field.</b> A felled raider leaves a cog and a felled bomber leaves a live bomb, and both
        /// lie where they fell until somebody reaches for them — which invariant 40i made the whole
        /// point of the bomber ("the decision is <em>when</em>"). A gravemaw puts a clock on that
        /// decision: every loose thing still lying on the hill when it casts is gone.
        /// </para>
        /// <para>
        /// <b>It takes no ward health at all</b>, so it is the second of the six that cannot bring
        /// the line down on its own and the second that rides the last authored wave rather than
        /// walking on alone (<see cref="SiegeTuning.EndangersTheLine"/>).
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Gravemaw,

        /// <summary>
        /// The bonecaller: it raises the dead, and what it takes is the hill the player has already
        /// cleared.
        ///
        /// <para>
        /// <b>The one boss in this mode that puts raiders <em>on</em> the board.</b> Everything
        /// else a boss does is subtraction — health, fire, rank, a clock, and now the loose things
        /// on the ground. A raise is addition, and it is the only verb left that a player answers
        /// by having got <em>ahead</em>: a hill somebody has cleared is a hill that gets refilled,
        /// and a hill they are behind on is one the raise makes very much worse.
        /// </para>
        /// <para>
        /// <b>It is capped, and the cap is what lets par stay arithmetic.</b> Par is the hill's
        /// health over the most one match could deliver (invariant 37a), so a boss that could add
        /// bodies for as long as it lived would make par a number nothing could compute. It raises
        /// <see cref="SiegeTuning.RaiseSize"/> creepers at most <see cref="SiegeTuning.Raises"/>
        /// times, and par counts every one of them whether they are ever raised or not — which
        /// overstates a run that kills it early, and invariant 22 says that is the direction to err
        /// in.
        /// </para>
        /// <para>
        /// <b>It does endanger the line, and not by swinging.</b> What it raises walks and swings,
        /// so a run can be lost to a bonecaller that never touches a ward itself — which is why
        /// <see cref="SiegeTuning.EndangersTheLine"/> stopped being "does its spell take health".
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Bonecaller,
    }
}
