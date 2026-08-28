using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The light behind a shop card: one colour and one strength per rung of its shelf.
    ///
    /// <para>
    /// <b>Every card is lit now, and that is a change of mind worth stating.</b> The rule this
    /// replaces was that motion is the loudest thing on a scrolling page, so spending it on
    /// every card singles out none — which is true when the only thing the light says is
    /// <em>look here</em>. What it says now is <em>how much</em>, and that is a different
    /// statement: it climbs with the picture in front of it and with the figure under it, so a
    /// shelf reads as a ladder from the far side of the screen rather than as six cards of
    /// which one is shouting. The hierarchy the old rule protected is kept by <em>strength</em>
    /// rather than by presence — the bottom rung is a dim wash and the top is a lit fan — and
    /// by the gold seat and gold edge, which are still the featured card's alone.
    /// </para>
    /// <para>
    /// <b>The colours are a rarity ramp, and they are read against the art rather than chosen
    /// for it.</b> Coins are gold and gems are violet, so a ladder tinted in the currency's own
    /// colour would be a halo nobody can see at the two rungs that matter most. The ramp is the
    /// one every player of anything already knows — plain, green, blue, violet, orange, gold —
    /// and it lands on <see cref="Pal.Gold"/> at the top, which is the colour this game already
    /// uses for the best thing on any list (the seal, the crest, the featured edge).
    /// </para>
    /// <para>
    /// <b>Which rung is <see cref="ShopLadder"/>'s answer, never a second copy of it.</b> The
    /// card's picture and the card's light are one statement about where a product sits, and
    /// two roundings of one fraction is exactly how they would come to disagree — see that
    /// class. This holds the appearance and none of the arithmetic.
    /// </para>
    /// </summary>
    public static class ShopRarity
    {
        /// <summary>How many rungs the ramp has. The money shelves are cut to this.</summary>
        public const int Rungs = 6;

        /// <summary>How a card's seat and fan of light are drawn at one rung.</summary>
        public readonly struct Look
        {
            /// <summary>The rung's colour, worn by both the seat and the rays.</summary>
            public readonly Color Colour;

            /// <summary>How solid the seat behind the plate is.</summary>
            public readonly float Seat;

            /// <summary>How solid the fan of rays across the plate is.</summary>
            public readonly float Rays;

            /// <summary>Seconds for one full turn of the fan. Longer is calmer.</summary>
            public readonly float Turn;

            public Look(Color colour, float seat, float rays, float turn)
            {
                Colour = colour;
                Seat = seat;
                Rays = rays;
                Turn = turn;
            }
        }

        // The ramp, dimmest first, and the strengths were set by rendering a shelf and looking
        // at it rather than by picking round numbers. The floor is what that found: below about
        // .08 on the rays every hue on a dark plate reads as the same grey, so a bottom rung set
        // to "barely there" is not a quiet rung, it is a rung with no colour — which would make
        // the first three cards of a shelf indistinguishable and the whole ladder pointless.
        static readonly Look[] Ramp =
        {
            new Look(Pal.Rope,    .17f, .08f, 96f),
            new Look(Pal.Verdant, .21f, .10f, 84f),
            new Look(Pal.Azure,   .26f, .12f, 72f),
            new Look(Pal.Bloom,   .31f, .14f, 60f),
            new Look(Pal.Amber,   .36f, .16f, 50f),
            new Look(Pal.Gold,    .42f, .19f, 40f),
        };

        /// <summary>The look for one rung, clamped so a longer ladder cannot fall off the end.</summary>
        public static Look At(int rung)
            => Ramp[Mathf.Clamp(rung, 0, Ramp.Length - 1)];

        /// <summary>The look for one product, by where it sits on its own shelf.</summary>
        public static Look Of(StoreProduct product)
            => At(ShopLadder.Rung(product, Rungs));

        /// <summary>
        /// The look for a gem-priced good.
        ///
        /// <para>
        /// A good is not on a money ladder — hearts and a faster clock are two different things
        /// rather than two sizes of one — so it takes its own resource's colour at a fixed
        /// middling strength instead of a rung. That keeps the supplies shelf coherent beside
        /// the three heart containers, which <em>are</em> a ladder, without inventing a rarity
        /// for something that has none.
        /// </para>
        /// </summary>
        public static Look Of(StoreGood good)
            => new Look(good != null && good.Kind == StoreGoodKind.HeartBoost ? Pal.Sun : Pal.Rose,
                        .26f, .12f, 72f);
    }
}
