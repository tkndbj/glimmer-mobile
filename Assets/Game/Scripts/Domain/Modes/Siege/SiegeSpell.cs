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
    }
}
