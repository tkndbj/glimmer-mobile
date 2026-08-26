using UnityEngine;

namespace GlimmerGrove.Content
{
    /// <summary>
    /// The geometry of one chapter's stretch of map, in canvas units.
    ///
    /// These numbers live in Domain rather than beside the screen that draws them
    /// because they are an authoring contract, not a rendering detail. Whether two
    /// glades collide, or whether the trail between them runs backwards, is a question
    /// about the content, and it has to be answerable by the build gate — which cannot
    /// reach into Presentation. The alternative, a validator holding its own copy of
    /// the numbers, would agree with the screen right up until somebody changed one.
    ///
    /// A level's <c>mapX</c>/<c>mapY</c> are fractions of its own chapter's map, so
    /// turning them into a distance needs that chapter's strip count: the same
    /// fractional gap is six times the distance in a six-strip chapter as in a
    /// one-strip chapter. That is precisely why these checks cannot be made one level
    /// at a time, and why <c>ChapterMapValidator</c> exists alongside
    /// <see cref="LevelValidator"/> rather than inside it.
    /// </summary>
    public static class ChapterMap
    {
        /// <summary>
        /// Canvas units across. This is the UI canvas reference width itself, not a copy
        /// of it — <c>Boot.RefWidth</c> reads it from here, because Domain cannot read
        /// anything from Presentation and a second copy would drift the moment either
        /// side was retuned.
        /// </summary>
        public const float Width = 1080f;

        /// <summary>Canvas units per background strip. A chapter is as tall as its strips.</summary>
        public const float StripHeight = 1200f;

        /// <summary>The tappable glade disc — the part a player actually sees collide.</summary>
        public const float NodeDiameter = 196f;

        /// <summary>Air between two discs, below which they read as one lump rather than two glades.</summary>
        public const float NodeClearance = 24f;

        /// <summary>Centres closer together than this overlap on screen.</summary>
        public const float MinimumNodeSeparation = NodeDiameter + NodeClearance;

        /// <summary>How far above the highest glade the end-of-chapter marker floats.</summary>
        public const float TeaserGap = 0.22f;

        /// <summary>
        /// How much of the top of the map the end-of-chapter marker must leave clear, in
        /// canvas units.
        ///
        /// <para>
        /// A distance rather than a fraction, and that is the whole of it. What the marker
        /// has to clear is the header — a fixed number of canvas units however long the
        /// chapter is — while a fraction of a four-strip map is a different distance from
        /// the same fraction of a six-strip one. As a ceiling of 0.95 it sat 240 units from
        /// the top of the Mill Vale and 360 from the top of the Shallows, so one constant
        /// put the marker completely behind the banner in one chapter and clipped it in the
        /// other, and no authored coordinate was wrong in either.
        /// </para>
        /// <para>
        /// Sized for the worst case rather than the ordinary one: the banner's underside on
        /// a display whose safe area pushes the whole header down, plus the marker's own
        /// radius. A display with nothing in the way simply gets more room than it needs,
        /// which is invisible; the other way round is a control the player cannot see.
        /// </para>
        /// </summary>
        public const float TeaserHeadroom = 500f;

        /// <summary>
        /// Where the end-of-chapter marker sits across the map when a chapter does not say.
        /// The Shallows ends on the right, so this is above its last glade.
        /// </summary>
        public const float TeaserX = 0.66f;

        /// <summary>
        /// An authored teaser x as it will actually be used: 0 (or anything outside the
        /// map) means "not authored" and takes <see cref="TeaserX"/>.
        ///
        /// The same convention <c>par</c> and <c>budgetFactor</c> already use, and for the
        /// same reason — <c>JsonUtility</c> writes a zero into every field a file predating
        /// it never had, so zero is the one value that cannot mean a choice. Nothing is
        /// lost by it: a marker at the very left edge of the map is half off it.
        /// </summary>
        public static float TeaserAcross(float authored)
            => authored > 0f && authored <= 1f ? authored : TeaserX;

        /// <summary>The map's height in canvas units. Always at least one strip.</summary>
        public static float Height(int stripCount)
            => Mathf.Max(StripHeight, stripCount * StripHeight);

        /// <summary>An authored position as it will actually be drawn, clamped onto the map.</summary>
        public static Vector2 Place(Vector2 authored)
            => new Vector2(Mathf.Clamp01(authored.x), Mathf.Clamp01(authored.y));

        /// <summary>
        /// Where the end-of-chapter marker sits, given the highest glade below it and how
        /// many strips the chapter is. Both are needed: the gap above the last glade is a
        /// fraction of the map, and the room kept clear at the top is a distance.
        /// </summary>
        public static Vector2 TeaserPosition(float highestY, int stripCount)
            => TeaserPosition(highestY, stripCount, TeaserX);

        /// <inheritdoc cref="TeaserPosition(float,int)"/>
        /// <param name="across">
        /// The chapter's own <c>teaserX</c>; 0 takes the default. Only this axis is
        /// authorable — see <see cref="ChapterDefinition.TeaserX"/>.
        /// </param>
        public static Vector2 TeaserPosition(float highestY, int stripCount, float across)
        {
            float ceiling = Mathf.Clamp01(1f - TeaserHeadroom / Height(stripCount));
            return new Vector2(TeaserAcross(across), Mathf.Min(ceiling, highestY + TeaserGap));
        }

        /// <summary>
        /// The distance between two authored positions, in canvas units.
        ///
        /// The axes scale differently — x across a fixed width, y across however many
        /// strips the chapter declares — so this is the only honest way to ask whether
        /// two glades are too close. Comparing the raw fractions would call a pair in a
        /// tall chapter cramped when they are half a screen apart.
        /// </summary>
        public static float Separation(Vector2 a, Vector2 b, int stripCount)
        {
            float dx = (a.x - b.x) * Width;
            float dy = (a.y - b.y) * Height(stripCount);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }
    }
}
