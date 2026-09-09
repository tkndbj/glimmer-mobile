using System;
using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>
    /// One raider a wave names: what colour it wears and what kind it is.
    ///
    /// <b>A pair rather than a character</b>, because a wave's text stopped being one character
    /// per raider the moment a third kind arrived (<see cref="SiegeLayout.Shield"/>). Everything
    /// that used to index into the string indexes into these instead, so a modifier can never be
    /// counted as a raider.
    /// </summary>
    public readonly struct SiegeSpec
    {
        /// <summary>The colour it wears, lower case, or <c>'\0'</c> for nothing at all.</summary>
        public readonly char Colour;

        /// <summary>What it is.</summary>
        public readonly SiegeKind Kind;

        public SiegeSpec(char colour, SiegeKind kind)
        {
            Colour = colour;
            Kind = kind;
        }
    }
}
