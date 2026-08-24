using System;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// How loud the ceremony is when something bought in the grove's shop arrives.
    ///
    /// <para>
    /// <b>The spectacle scales with the price, and the alternative is worse in both
    /// directions.</b> One ceremony for everything is either exhausting or cheap: a player
    /// buys a 60-credit pebble and a 28,000-credit sanctum out of the same grid, and a full
    /// fanfare on the pebble is something they will be sick of by the twentieth purchase —
    /// there are 146 decor pieces — while a fanfare small enough to bear 146 times says
    /// nothing at all about the house. This is <c>CompanionRevealOverlay</c>'s tier idea moved
    /// from a keeper gate onto a price, and it is the same argument the win panel makes about
    /// spending a screen flash on every clear: an effect used everywhere singles out nothing.
    /// </para>
    /// <para>
    /// <b>It is in Domain and tested for <c>GroveGrowth</c>'s reason.</b> A band table is the
    /// kind of thing that compiles, validates and reads perfectly while putting the entire
    /// catalog in one tier — which looks, on screen, exactly like a feature nobody finished.
    /// Nothing but running it over the real price range can see that.
    /// </para>
    /// </summary>
    public static class GroveUnveil
    {
        /// <summary>How many tiers there are. The colours come from <c>Chroma</c>.</summary>
        public const int Tiers = 5;

        /// <summary>
        /// Where one tier ends and the next begins, in credits.
        ///
        /// <para>
        /// Absolute amounts rather than a fraction of the catalog's most expensive piece, which
        /// was the first attempt and is wrong here: the dearest thing in the grove is the top
        /// home at 28,000 and the dearest decor is 4,000, so every fraction-of-max band put all
        /// 146 decor pieces in the bottom tier. Credits are also the unit the player is
        /// actually thinking in — at the shipped ~593 credits a day, these read as roughly
        /// half a day, a day, three days and a fortnight.
        /// </para>
        /// <para>
        /// Re-pricing the catalog therefore moves pieces between tiers, and that is the
        /// intended behaviour rather than a hazard: a piece that costs more <em>should</em>
        /// arrive louder. What must stay true is that the shipped catalog spans several tiers
        /// — see the tests.
        /// </para>
        /// </summary>
        public static readonly int[] Bands = { 250, 700, 2000, 7000 };

        /// <summary>
        /// The tier something of this price arrives at, 1 through <see cref="Tiers"/>.
        ///
        /// Free and unpriced things answer 1 rather than refusing: nothing here decides whether
        /// a celebration happens, only how loud it is, and a ceremony with no tier would be a
        /// ceremony with no colours.
        /// </summary>
        public static int TierOf(int cost)
        {
            for (int i = 0; i < Bands.Length; i++)
                if (cost < Bands[i]) return i + 1;

            return Tiers;
        }

        /// <summary>The tier a piece arrives at. See <see cref="TierOf(int)"/>.</summary>
        public static int TierOf(HomesteadPiece piece) => TierOf(piece.IsValid ? piece.Cost : 0);

        /// <summary>
        /// Everything the ceremony varies, in one reading.
        ///
        /// <para>
        /// One call rather than a property per effect, for <c>GroveScore.Of</c>'s reason: a
        /// screen that asked separately for its ray count and its hold time could be handed two
        /// answers from two different tiers if anything ever made the tier depend on state. It
        /// cannot today, and a struct costs nothing to keep it that way.
        /// </para>
        /// </summary>
        public readonly struct Fanfare
        {
            /// <summary>1 through <see cref="Tiers"/>.</summary>
            public readonly int Tier;

            /// <summary>Spokes in the fan of light behind the piece.</summary>
            public readonly int Rays;

            /// <summary>Drifting masses of colour in the room. Zero at the bottom tier.</summary>
            public readonly int Aurora;

            /// <summary>Rings that leave the impact.</summary>
            public readonly int Shockwaves;

            /// <summary>Sparks thrown by the landing.</summary>
            public readonly int Sparks;

            /// <summary>How white the screen goes on impact.</summary>
            public readonly float Flash;

            /// <summary>Seconds the finished picture is held before it puts itself away.</summary>
            public readonly float Hold;

            /// <summary>A second counter-turning fan. The point where the room starts to move.</summary>
            public bool HasSecondFan => Tier >= 3;

            /// <summary>
            /// Falling colour, and a struck seal on the name plate. The top two tiers only —
            /// every rung of the home ladder and the handful of decor pieces priced past two
            /// thousand credits, which is about three days of ordinary play for one object.
            ///
            /// <para>
            /// Deliberately <em>not</em> restricted to homes. The first cut of this said "only
            /// a home reaches these" and it was simply untrue of the shipped catalog — the
            /// dearest decor is four thousand credits, and a player who saved a week for one
            /// fence should not be told it matters less than a house. What keeps these worth
            /// having is the band, not the kind of thing that crosses it.
            /// </para>
            /// </summary>
            public bool HasConfetti => Tier >= 4;

            /// <summary>A struck seal on the name plate. See <see cref="HasConfetti"/>.</summary>
            public bool HasSeal => Tier >= 4;

            public Fanfare(int tier, int rays, int aurora, int shockwaves, int sparks,
                           float flash, float hold)
            {
                Tier = tier; Rays = rays; Aurora = aurora; Shockwaves = shockwaves;
                Sparks = sparks; Flash = flash; Hold = hold;
            }
        }

        /// <summary>
        /// The longest a purchase may hold the screen, from the first frame to the last.
        ///
        /// <para>
        /// <c>GroveGrowth.MaxSpread</c>'s rule for the second time. The shop is a grid of 150
        /// cells and buying is the thing a player came to do; a ceremony that has to be waited
        /// out turns the second purchase into a chore, and a content drop that adds something
        /// dearer than the sanctum must not be able to lengthen it.
        /// </para>
        /// </summary>
        public const float MaxSeconds = 3f;

        /// <summary>
        /// When the name plate lands, and how long the room takes to fade afterwards — the two
        /// fixed costs the hold sits between.
        ///
        /// <para>
        /// They live here rather than beside the drawing so that <see cref="Seconds"/> is the
        /// <em>real</em> length of the ceremony rather than an estimate of it. A ceiling
        /// checked against a number the sequence does not actually use is a ceiling that goes
        /// quietly wrong the first time somebody retunes a beat, which is <c>Cue</c>'s whole
        /// argument about absolute delays drifting apart.
        /// </para>
        /// </summary>
        public const float PlateAt = .62f, Outro = .24f;

        /// <summary>How long this tier's ceremony takes, start to finish.</summary>
        public static float Seconds(int tier) => PlateAt + FanfareOf(tier).Hold + Outro;

        static readonly Fanfare[] Table =
        {
            //           tier rays aurora waves sparks flash hold
            new Fanfare(  1,   8,    0,     1,    12,   .18f, .80f),
            new Fanfare(  2,  10,    2,     1,    16,   .26f, 1.00f),
            new Fanfare(  3,  12,    2,     2,    22,   .36f, 1.25f),
            new Fanfare(  4,  14,    3,     2,    28,   .50f, 1.50f),
            new Fanfare(  5,  18,    3,     3,    36,   .70f, 1.90f),
        };

        /// <summary>What this tier's ceremony is made of. Out-of-range tiers clamp.</summary>
        public static Fanfare FanfareOf(int tier)
            => Table[Math.Min(Table.Length, Math.Max(1, tier)) - 1];

        /// <summary>What this piece's ceremony is made of.</summary>
        public static Fanfare FanfareFor(HomesteadPiece piece) => FanfareOf(TierOf(piece));
    }
}
