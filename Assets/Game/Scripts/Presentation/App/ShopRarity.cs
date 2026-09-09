using GlimmerGrove.Utilities;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What colour a sellable thing wears on its card.
    ///
    /// <para>
    /// <b>This used to be a six-rung rarity ramp, and the ramp went with the decoration it
    /// lit.</b> Every card carried a seat of coloured light behind its plate and a slow fan of
    /// rays across it, climbing plain → green → blue → violet → orange → gold with the rung, so
    /// that a shelf read as a ladder from the far side of the screen. It was withdrawn by the
    /// owner after playing the restyled shop: against the kit's own opaque frames the fan is a
    /// smudge behind the picture rather than a light under it, and the picture — which already
    /// grows with the rung (<c>ShopArt</c>) — was saying the same thing more clearly. What is
    /// left is the half nothing else could say.
    /// </para>
    /// <para>
    /// <b>A utility's colour is not a rung and never was.</b> Four utilities are four answers to
    /// four different moments on a hill rather than four sizes of one thing, so there is no
    /// order here to be drawn — they are laid out in <c>UtilityItem.Order</c>, which is authored
    /// and is about the action bar. The colours are the ones the icons are actually painted in
    /// (see <c>Tools/make_utility_art.py</c>), so the name on a card and the picture above it
    /// are one statement rather than two.
    /// </para>
    /// </summary>
    public static class ShopRarity
    {
        /// <summary>The colour a utility's card writes its name in.</summary>
        public static Color Of(UtilityItem item)
            => Colour(item == null ? UtilityKind.None : item.Kind);

        static Color Colour(UtilityKind kind)
        {
            switch (kind)
            {
                case UtilityKind.Blast: return Pal.Ember;   // the firepot's flame
                case UtilityKind.Mend:  return Pal.Mint;    // the mending's flask
                case UtilityKind.Surge: return Pal.Azure;   // the surge's disc
                case UtilityKind.Storm: return Pal.Sun;     // the stormcall's bolts
                default: return Pal.Rope;
            }
        }
    }
}
