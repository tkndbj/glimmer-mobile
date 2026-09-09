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
        /// Spins a web over one cell of the field, locking the gem under it. The weaver's.
        ///
        /// <b>The first spell aimed at the board rather than at a ward</b>, so it carries no ward
        /// index at all — <c>SiegeSpellLanded.Ward</c> is the cell instead, which is why
        /// <see cref="SiegeTuning.AimsAtAWard"/> exists rather than every reader assuming.
        /// </summary>
        Weave,

        /// <summary>Takes one gem off the field and leaves a sack standing there. The thief's.</summary>
        Snatch,
    }
}
