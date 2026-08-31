using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What a Budburst flower looks like, answered once for everything that draws one.
    ///
    /// <para>
    /// <b>Three places draw a flower and they must not disagree.</b> The grove draws
    /// thirty-six of them, the band draws the colour in hand and the two behind it, and the
    /// legend above the board draws nine more explaining what makes what. A legend whose
    /// flowers are a different shape from the grove's is a legend about a different game, and
    /// that is exactly the drift <c>ProgressionLedger</c>'s rule exists to stop — invariant 9a,
    /// at the smallest scale it appears at.
    /// </para>
    /// <para>
    /// In Presentation rather than Domain because it is about <em>drawing</em>: a sprite and a
    /// colour, both of which are <c>UnityEngine</c>. What the mode <em>means</em> by a colour is
    /// <c>Energy</c>'s and <c>BudMixing</c>'s, and neither of those knows this file exists.
    /// </para>
    /// </summary>
    public static class BudFlower
    {
        /// <summary>How many petals an ordinary flower has, and how many white has.</summary>
        public const int SidesOrdinary = 4, SidesWhite = 8;

        /// <summary>How big a flower's sprite is cut, in pixels.</summary>
        const int Size = 128;

        /// <summary>
        /// How many petals a flower is drawn with: four, whatever colour it is — except white.
        ///
        /// <para>
        /// <b>One silhouette is the point.</b> A grove used to draw three petals for a pure
        /// colour, five for a blend and eight for white, so a board of thirty-six flowers was
        /// three different shapes scattered through each other and read as clutter rather than
        /// as a field. Four sides everywhere makes the grove one material, and colour — which is
        /// what the mode is <em>about</em> — becomes the only thing changing across it.
        /// </para>
        /// <para>
        /// <b>White keeps its own shape, and it is the one that has to.</b> White holds every
        /// channel, so it can never be mixed into again: it is the only flower on the board
        /// whose state the player cannot change, and the only one whose difference is a
        /// <em>rule</em> rather than a colour. It is also the only one that moves while nobody
        /// is tapping, so the two readings agree.
        /// </para>
        /// <para>
        /// <b>What this costs, said plainly.</b> The petal count used to be a second reading of
        /// a flower's colour, for the roughly one man in twelve who cannot separate red from
        /// green. That reading has left the flower and lives in two other places now: the legend
        /// above the grove, which says what every blend is made of, and the ghost under the
        /// thumb, which draws the flower in the colour it <em>would</em> become before anything
        /// is spent. If a third is ever wanted, the flower's heart is where it goes — a count of
        /// pips there is legible and does not disturb the silhouette, which is the whole reason
        /// this rule exists.
        /// </para>
        /// </summary>
        public static int Sides(int mask)
            => (mask & Energy.All) == Energy.All ? SidesWhite : SidesOrdinary;

        /// <summary>The sprite a flower of this colour is drawn with.</summary>
        public static Sprite Petals(int mask) => Art.Bloom(Size, Sides(mask), 1f);

        /// <summary>
        /// What a flower's colour looks like. <c>Pal.EnergyColour</c> is the game's own answer
        /// and is used everywhere else colour is mixed, so an orange here is the same orange a
        /// glade's red-and-yellow critter wears — which is the point: the arithmetic a player
        /// already knows from four chapters of glades is the arithmetic this mode runs on.
        /// </summary>
        public static Color Tint(int mask) => Pal.EnergyColour(mask);
    }
}
