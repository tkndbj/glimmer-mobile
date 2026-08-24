using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// A tier's whole colour scheme: the thing's own colour, two more that light the room
    /// around it, and the deep hue the room is built out of.
    ///
    /// <para>
    /// Three colours rather than one, because that is the difference between a light and a
    /// place. A tint over black gives a bright shape floating on nothing; a partner lighting
    /// the ground, an accent crossing it and a deep hue underneath give somewhere for the thing
    /// to arrive into — which is the entire job of a reveal screen.
    /// </para>
    /// <para>
    /// Every colour is one already in <see cref="Pal"/>, so the loudest moments in the game
    /// cannot drift away from the game's own palette by inventing shades of their own, and
    /// retuning the palette retunes every celebration at once.
    /// </para>
    /// <para>
    /// <b>Shared rather than owned by one screen, and that is a design decision before it is a
    /// tidying.</b> It began inside <c>CompanionRevealOverlay</c>, and when the grove's shop
    /// grew a ceremony of its own the obvious move was a second table beside the first — which
    /// is invariant 5b's mistake in the place it is least visible, since two colour ladders
    /// that disagree do not fail a build, they just quietly teach the player two different
    /// things. Gold means <em>the best one</em>, and it has to mean that whether what arrived
    /// was a friend or a house.
    /// </para>
    /// </summary>
    public readonly struct Chroma
    {
        public readonly Color Tint, Partner, Accent, Deep;

        public Chroma(Color tint, Color partner, Color accent, Color deep)
        {
            Tint = tint; Partner = partner; Accent = accent; Deep = deep;
        }

        /// <summary>
        /// The three lights in order, wrapping — for anything spawning a run of them, so a row
        /// of rings or sparks cycles the scheme instead of repeating one colour.
        /// </summary>
        public Color Nth(int i)
        {
            switch (((i % 3) + 3) % 3)
            {
                case 0: return Tint;
                case 1: return Partner;
                default: return Accent;
            }
        }

        /// <summary>
        /// The tier's scheme: pale, green, blue, purple, gold.
        ///
        /// <para>
        /// The <see cref="Tint"/> ladder is deliberately the rarity ladder every player already
        /// knows from every other game they have installed — common through to legendary —
        /// because this is the one part of a reveal that has to be understood without being
        /// taught. The first version ran cream → mint → sun → gold → magenta, which put the
        /// game's own premium colour in fourth place and ended on a pink nobody reads as "the
        /// best one". Gold last is worth more than gold in the middle, and it agrees with what
        /// gold means everywhere else in this UI.
        /// </para>
        /// <para>
        /// The partner is always across the wheel from the tint and the accent always warm,
        /// because a scheme built from neighbours is the monochrome problem again wearing three
        /// names. The deep hue is the tint's own family driven down to about a tenth of its
        /// value — dark enough for cream text and a lit rim to read against, and still
        /// unmistakably a colour rather than the absence of one.
        /// </para>
        /// <para>
        /// Out-of-range tiers resolve rather than throw: a content drop that lengthens whatever
        /// ladder feeds this must not be able to reach a celebration with no colours in it.
        /// </para>
        /// </summary>
        public static Chroma Of(int tier)
        {
            switch (tier)
            {
                case 1: return new Chroma(Pal.Cream, Pal.Aqua, Pal.Sun, Pal.Hex("#0B2230"));
                case 2: return new Chroma(Pal.Mint, Pal.Aqua, Pal.Sun, Pal.Hex("#0A2A22"));
                case 3: return new Chroma(Pal.Azure, Pal.Bloom, Pal.Aqua, Pal.Hex("#111A46"));
                case 4: return new Chroma(Pal.Bloom, Pal.Azure, Pal.Sun, Pal.Hex("#2B0E3E"));
                default: return new Chroma(Pal.Gold, Pal.Ember, Pal.Bloom, Pal.Hex("#331409"));
            }
        }
    }
}
