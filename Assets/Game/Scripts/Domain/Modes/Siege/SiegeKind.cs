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

        /// <summary>
        /// The shackler: it chains a ward, and what it takes is the line's <em>time</em>.
        ///
        /// <para>
        /// <b>The one axis the first six leave open.</b> Between them they take health, fire, a
        /// rank, the player's clock, what is lying on the ground and the emptiness of the hill —
        /// every one of which is a <em>resource</em>, and every one of which has an answer that is
        /// also a resource. A shackled ward keeps all of them: its fuel, its rank, its health and
        /// its charges are exactly where they were, and for
        /// <see cref="SiegeTuning.ShacklerBind"/> seconds it cannot fire. There is nothing to pour
        /// back, so what the player spends is the only thing this mode never lets them buy.
        /// </para>
        /// <para>
        /// <b>It is the deliberate opposite of a douse and the two must not be read as one.</b> A
        /// blightcaller takes a ward's fuel and its seconds together, and its answer is to pour
        /// more in — <c>SiegeBoard.Surge</c> lifts a douse for exactly that reason. A shackle takes
        /// the seconds and leaves the fuel, so pouring is not an answer and is not refused either:
        /// a ward filled while it is chained <em>banks</em>, and lets go the moment the chain does.
        /// So the decision it asks is the mirror of the blightcaller's — feed the ward that cannot
        /// use it yet, or feed the three that can (invariant 26h: the player decides, and can be
        /// wrong).
        /// </para>
        /// <para>
        /// <b>It takes no ward health at all</b>, so it is the third of the eight that cannot bring
        /// the line down on its own, and it rides the last authored wave rather than walking on
        /// alone (<see cref="SiegeTuning.EndangersTheLine"/>, invariant 37ad).
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Shackler,

        /// <summary>
        /// The ironclad: only the ward wearing its own colour can touch it.
        ///
        /// <para>
        /// <b>Invariant 37bq read backwards, and that is the whole fight.</b> Every other boss in
        /// this mode is answered by the <em>whole</em> line whatever colour it wears, because a
        /// duel is one raider and a lock that left three turrets idle would fight the biggest
        /// number in the mode with a quarter of the loadout. An ironclad puts that back on purpose:
        /// <see cref="SiegeTuning.EveryWardReaches"/> answers false for it alone, so three of the
        /// four wards will not fire at it at all.
        /// </para>
        /// <para>
        /// <b>What makes that a fight rather than a wall is that the fuel is not lost.</b> A ward
        /// with nothing it may shoot banks (<c>SiegeBoard.Aim</c> simply gives it no target), a
        /// full tube converts to an overcharge, and an overcharge is thrown at whatever is
        /// furthest down the hill at this ward's own full weight —
        /// <c>SiegeBoard.Through</c> blunts a bulwark and nothing else. So the answer to an
        /// ironclad is the one mechanic in the mode that has never been the answer to anything:
        /// feed its colour to kill it, and let the other three fill and dump.
        /// </para>
        /// <para>
        /// <b>And par stays exactly as honest as it is for every other boss.</b>
        /// <see cref="SiegeTuning.PerfectMatch"/> assumes a match burns as full-weight bolts; the
        /// one ward that answers this boss lands full-weight bolts and every overcharge lands at
        /// full weight too, so nothing here converts fuel at a rate the arithmetic does not
        /// already assume (invariant 37a).
        /// </para>
        /// <para>
        /// <b>It takes ward health as well</b>, so unlike the shackler it can end a run on its own
        /// and gets a wave to itself as a finale should (invariant 37t).
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Ironclad,

        /// <summary>
        /// The thunderer: it draws the line's banked charges out and throws them back.
        ///
        /// <para>
        /// <b>The ninth thing there was left to take is the <em>charge</em>, and it is the one
        /// resource in this mode the player holds by choice.</b> Health, fire, a rank, the clock,
        /// the ground's loot, the hill's emptiness, the line's seconds and the line's answer were
        /// all spoken for by the eight before it; an overcharge is a full tube the player has
        /// chosen not to throw yet (<see cref="SiegeWard.Charges"/>), and nothing on the hill has
        /// ever been able to touch it. A thunderer's spell lands on the ward holding the most
        /// and takes every charge it holds - and each charge taken comes back as
        /// <c>SiegeTuning.ThundererDrain</c> more off that ward, so what the player was saving is
        /// thrown at them.
        /// </para>
        /// <para>
        /// <b>The decision it asks is the mirror of the ironclad's</b>: the fourth chapter's
        /// finale taught banking, and this is the boss that punishes hoarding. Throw the charge
        /// before the bolt lands (the tell is the warning, invariant 37s) and it takes the base
        /// figure; hold it and the bolt is worth what was held. It is also invariant 37di's
        /// argument read from the other end - the charges banked through the quiet before a boss
        /// were what killed eight bosses on the walk in, and this is the one boss that makes
        /// walking in with two charges banked the wrong idea rather than the only one.
        /// </para>
        /// <para>
        /// <b>Par does not move</b>: what it takes lands on the line, and a charge thrown early
        /// lands at full weight exactly as <c>PerfectMatch</c> assumes (invariant 37a).
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Thunderer,

        /// <summary>
        /// The colossus: it hurls a boulder that buries a ward, and the player digs it out
        /// rather than waiting the stone out.
        ///
        /// <para>
        /// <b>The tenth thing there was left to take is the player's <em>hands</em>.</b> Every
        /// other verb here is answered on the gem field or by waiting; a buried ward keeps its
        /// fuel, its rank and its charges and cannot fire (<see cref="SiegeWard.Buried"/>), and
        /// what frees it is <c>SiegeTuning.RubbleTaps</c> taps on the post - which is the
        /// bomber's rule (invariant 40i, the one thing that makes the player touch the hill)
        /// arriving at the line. A shackle is bought back by nothing and a douse by fuel; rubble
        /// is bought back by attention, which is the resource this mode's clock prices most
        /// sharply (invariant 37bl - the clock punishes deliberation).
        /// </para>
        /// <para>
        /// <b>The stone also weathers</b> (<c>SiegeTuning.ColossusBury</c>), and that is a
        /// ceiling rather than a second answer: a burial nothing but a tap could end was a line
        /// that went quiet post by post and a boss with nothing left to aim at, which is neither
        /// side able to act. Four seconds is the most a boulder can cost; digging is what makes
        /// it cost less.
        /// </para>
        /// <para>
        /// <b>So the decision is dig or match</b>, and it can be wrong both ways (invariant 26h):
        /// three taps spent on a ward whose colour is off the hill is three beats the field went
        /// unplayed, and a ward left buried through a wave is a quarter of the line's answer
        /// gone for as long as the player leaves it. The hold simulation pays for a dig with a
        /// beat it would have matched in, which is what makes the verb visible to the sweep.
        /// </para>
        /// <para>
        /// <b>It takes ward health as well</b> (37dn), so it can end a run on its own and gets
        /// a wave to itself as a finale should - it is the fifth chapter's, and the biggest
        /// body on the hill.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Colossus,

        /// <summary>
        /// The gorgon: its glare turns a ward stone-struck - it keeps firing on its own cadence,
        /// burns the fuel every shot costs, and nothing leaves the barrel
        /// (<see cref="SiegeSpell.Glare"/>).
        ///
        /// <para>
        /// <b>The eleventh thing a boss could take is the ward's <em>work</em></b>, and it is the
        /// last of the three ways a turret can be taken out of a fight (invariant 37cw's rule,
        /// asked a third time). A douse takes what is in the tube and is answered by pouring more
        /// in; a shackle takes the seconds and banks the fuel; a glare takes the fuel <em>as it
        /// arrives</em>, so the only answer is to stop feeding that colour and feed another. The
        /// three are three different instructions to the player, which is what stops them being
        /// one verb in three tints (invariant 37z).
        /// </para>
        /// <para>
        /// <b>It is the one verb whose cost the player sets</b>, which is the thunderer's shape
        /// read the other way round: a thunderer punishes what was banked, a gorgon punishes what
        /// is poured. A player who reads the mask and feeds elsewhere pays nothing at all for it;
        /// one who keeps feeding the colour they had planned to feed pays every gem of it.
        /// </para>
        /// <para>
        /// <b>It smites like every other spell</b> (37dn), so a rung sent nothing but this could
        /// still be lost, and <c>EndangersTheLine</c> is true of it.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Gorgon,

        /// <summary>
        /// The sunlord: it seals a ward and gives the player a deadline - fill that ward's tube
        /// before the seal runs out or the ward falls (<see cref="SiegeSpell.Doom"/>).
        ///
        /// <para>
        /// <b>The twelfth thing a boss could take is the player's own <em>agenda</em>, and it is
        /// the first verb in this mode that can be answered.</b> Every other one is suffered: a
        /// rank, a charge, a tube, six seconds, the use of your hands - and the reply is to keep
        /// playing. A seal is a task with a clock on it, which makes it the only boss verb that
        /// says <em>do this now</em>, and the right thing for the last rung of the last chapter
        /// to ask.
        /// </para>
        /// <para>
        /// <b>It is not a gravemaw's devour with extra steps</b>, and the difference is exactly
        /// invariant 37z's test - what does it take. A devour takes a ward, full stop; a seal
        /// takes the next few matches, and takes a ward only from a player who spent them
        /// somewhere else. One is a loss and the other is a decision.
        /// </para>
        /// <para>
        /// <b>It can never seal the last ward standing</b> (<c>SiegeWard.Doomed</c>), so it
        /// cannot end a run on its own: the line still comes down one blow at a time, which is
        /// the fail state the player has been playing against for six chapters.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Sunlord,

        /// <summary>
        /// The harrower: it tears a rank off a ward and scatters it on the hill, where the
        /// player can go and pick it up (<see cref="SiegeSpell.Harrow"/>).
        ///
        /// <para>
        /// <b>The thirteenth thing a boss could take is a rank the player can get <em>back</em>,
        /// and it is the overlord's verb read the way a sunlord reads a gravemaw.</b> A sunder is
        /// a rank gone; nothing about it is a decision, because there is nothing to decide. A
        /// harrow drops the same rank on the ground as an ordinary cog bound to the ward it came
        /// off - so the question it asks is the one the bomber asks (invariant 40i, the one thing
        /// that makes the player touch the hill): <em>when</em>. Reach for it now and give up the
        /// beat you were going to match in, or leave it lying while the boss takes the next one.
        /// </para>
        /// <para>
        /// <b>It can be wrong both ways</b> (invariant 26h), which is what separates it from
        /// decoration: a tap spent on a cog while the colour the boss is about to want goes
        /// unfed is a rank bought with a bolt, and a hill full of unclaimed cogs at the end of a
        /// duel is a line fighting the finale two rungs under itself.
        /// </para>
        /// <para>
        /// <b>Par does not move.</b> It adds no bodies and heals nothing, which is the first
        /// question to ask of any new verb (<c>SiegeTuning.Par</c>, invariant 37a) - and it
        /// takes ward health like every other spell (37dn), so it can end a run.
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Harrower,

        /// <summary>
        /// The hollowking: when it casts, every ward that has fired nothing since its last cast
        /// is struck, and every ward that has been working is left alone
        /// (<see cref="SiegeSpell.Wane"/>).
        ///
        /// <para>
        /// <b>The fourteenth thing a boss could take is the player's <em>habit</em>, and it is
        /// the only verb in this mode that is answered before it lands.</b> Eleven verbs take
        /// something and ask what you do about it; a wane announces its rule and then bills you
        /// for the last few seconds of play. The instruction it gives is one no boss has ever
        /// given: <em>keep all four working</em>. Every other fight in this mode rewards pouring
        /// into the colour that matters, and against a boss - which wears no colour and is
        /// reached by every ward at full weight (37dn) - a ward stands idle only because its
        /// tube is empty.
        /// </para>
        /// <para>
        /// <b>It is not a rally with a filter.</b> A warbringer's roar takes the same from all
        /// four whatever the player did, so nothing about it is a decision; a wane takes nothing
        /// at all from a line that has been fed evenly, which is invariant 5d's test passed by
        /// arithmetic rather than by argument - there is an arrangement it rejects and an
        /// arrangement it does not.
        /// </para>
        /// <para>
        /// <b>Par does not move</b>, for the harrower's reason, and it takes ward health from
        /// every post it finds hollow - so it is a finale that can end a run on its own, which
        /// is what the last rung of a chapter should be (invariant 37t).
        /// </para>
        /// <para><b>Appended</b>, for the reason every member of this enum is.</para>
        /// </summary>
        Hollowking,
    }
}
