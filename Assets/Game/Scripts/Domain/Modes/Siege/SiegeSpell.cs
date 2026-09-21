using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// What a boss's spell does when it lands.
    ///
    /// <para>
    /// <b>Its own vocabulary rather than a flag on the kind, because the view reads it too.</b>
    /// Every one of the four is drawn differently, aimed differently and answered differently, and
    /// a rule that only <c>SiegeTuning</c> knew would leave the drawing to guess from health
    /// numbers — which is how a mode ends up with two bosses that look the same.
    /// </para>
    /// <para>
    /// <b>Appended, like <see cref="SiegeKind"/></b>: these reach analytics through
    /// <c>SiegeSpellLanded</c> and an ordinal that moves rewrites history.
    /// </para>
    /// </summary>
    public enum SiegeSpell
    {
        /// <summary>Takes a ward's health. The warlord's, and the one a mending answers.</summary>
        Smite,

        /// <summary>Empties a ward's fuel and smothers it. The blightcaller's.</summary>
        Douse,

        /// <summary>Sets the whole hill charging. The warbringer's, and aimed at no ward.</summary>
        Rally,

        /// <summary>Takes health <em>and</em> a rank the player earned. The overlord's.</summary>
        Sunder,

        /// <summary>
        /// <b>Retired: nothing casts these and the three ids must never be reused.</b>
        ///
        /// A weave locked a cell of the field, a snatch took a gem off it, and a bombard dropped a
        /// bomb onto one. All three were the same withdrawn idea — the hill reaching into the gem
        /// board — and the bomber that survived it drops its bomb <em>on the hill where it dies</em>
        /// rather than casting anything (see <see cref="SiegeKind.Bomber"/>). Kept as members
        /// because these ordinals reach analytics.
        /// </summary>
        Weave,

        /// <summary><b>Retired with <see cref="Weave"/>.</b></summary>
        Snatch,

        /// <summary><b>Retired with <see cref="Weave"/>.</b></summary>
        Bombard,

        /// <summary>
        /// Takes every loose thing off the hill — the cogs and the bombs nobody has picked up.
        /// The gravemaw's, and aimed at no ward.
        /// </summary>
        Devour,

        /// <summary>
        /// Puts a fresh group of raiders at the top of the hill. The bonecaller's, and aimed at no
        /// ward.
        /// </summary>
        Raise,

        /// <summary>
        /// Chains a ward: it keeps its fuel, its rank and its charges, and cannot fire. The
        /// shackler's, and the only spell here that takes no <em>resource</em> at all.
        ///
        /// <b>Deliberately not a douse with the fuel left in.</b> See
        /// <see cref="SiegeKind.Shackler"/> for why the two ask opposite questions, and
        /// <c>SiegeWard.Shackle</c> for the one line that separates them.
        /// </summary>
        Bind,

        /// <summary>
        /// Nothing, to a ward — it is the standing rule that only the caster's own colour may hurt
        /// it. The ironclad's, and aimed at no ward.
        ///
        /// <b>A verb with no event is a contradiction, so this one has a blow behind it.</b> An
        /// ironclad also strikes for <see cref="SiegeTuning.IroncladCast"/>, which is what the
        /// spell's flight and its landing draw; the aegis itself is read off
        /// <see cref="SiegeTuning.EveryWardReaches"/> every time a ward looks for something to
        /// shoot, and the view says it by refusing the three wards that cannot answer.
        /// </summary>
        Aegis,

        /// <summary>
        /// Takes every overcharge a ward has banked and lands them back on it as damage, on top
        /// of the health it takes anyway. The thunderer's, and aimed at the ward holding the most.
        ///
        /// <b>The one spell whose weight the player decides</b>: a ward with nothing banked takes
        /// the base figure and no more, so the tell is an invitation to throw what is held.
        /// </summary>
        Drain,

        /// <summary>
        /// Buries a ward under rubble: it keeps its fuel, its rank and its charges and cannot
        /// fire until the pile is off it. The colossus's.
        ///
        /// <b>Deliberately not a bind with a tap on it.</b> Both end on the clock now
        /// (<c>SiegeTuning.ColossusBury</c>), because a burial that ended only when the player
        /// said so could end never; what still separates them is that a chain is seconds the
        /// player can only wait out and a pile is seconds they can <em>spend</em> — every tap
        /// (<c>SiegeBoard.Dig</c>) takes a piece the clock would have taken later. So the two
        /// stay two fields: a surge lifts a douse, nothing lifts a chain, and only hands shorten
        /// a burial.
        /// </summary>
        Bury,

        /// <summary>
        /// Turns a ward stone-struck: it fires on its own cadence, spends the fuel each shot
        /// costs, and nothing leaves the barrel. The gorgon's, and aimed at the fullest tube.
        ///
        /// <b>Deliberately not a douse with a longer clock.</b> A douse takes the fuel that is
        /// already there and a surge lifts it; a glare takes whatever is poured in next, so what
        /// it really costs is decided after it lands - by the player, in the colour they choose
        /// to feed. A ward with nothing coming to it loses nothing to a glare at all.
        /// </summary>
        Glare,

        /// <summary>
        /// Seals a ward: fill its tube before <c>SiegeTuning.DoomFor</c> runs out or it falls.
        /// The sunlord's, and never aimed at the last ward standing.
        ///
        /// <b>The one spell in this mode with an answer.</b> What it takes is the player's next
        /// few matches rather than anything the line holds, and a player who pays the toll loses
        /// nothing but the tempo. What makes that a real decision rather than a formality is the
        /// colour lock (invariant 37bl): the toll must be paid in the sealed ward's own colour,
        /// so a seal on the colour the hill is <em>not</em> wearing is the expensive one.
        /// </summary>
        Doom,

        /// <summary>
        /// Takes a rank off a ward and drops it on the hill as a cog, where the player can pick
        /// it up again. The harrower's, and aimed at the best-ranked ward standing.
        ///
        /// <b>Deliberately not a sunder with a softer number.</b> An overlord's sunder is a rank
        /// that is <em>gone</em>; a harrow is a rank that is <em>lying over there</em>, which is
        /// the same distinction a seal draws against a devour (invariant 37ec): one is a loss and
        /// the other is a decision. What it costs is a beat of the player's attention on the hill
        /// rather than anything the line holds - so a player who never looks down pays it in full
        /// and one who reaches for it pays nothing but the tap.
        /// </summary>
        Harrow,

        /// <summary>
        /// Strikes every ward that has fired nothing since this boss's last cast, and leaves the
        /// ones that did alone. The hollowking's, and aimed at no ward at all.
        ///
        /// <b>The one spell in this mode whose cost the player sets in advance.</b> A glare and a
        /// drain are answered after they land; a wane is answered <em>before</em> - by having kept
        /// all four tubes doing something - so it is the only verb here that asks the player to
        /// spread rather than to focus, which is the instruction eleven verbs have never given.
        /// It is booked like a rally, one record per post, because it aims at no ward.
        /// </summary>
        Wane,
    }
}
