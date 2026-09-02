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

        /// <summary>
        /// What rides above a glade: the standing mark — record and rank — that
        /// <c>LevelsScreen.RankMark</c> hangs over a cleared node. A rectangle centred on the
        /// node, <see cref="CrownHalfWidth"/> either side of it and reaching from
        /// <see cref="CrownBottom"/> to <see cref="CrownTop"/> above its centre.
        ///
        /// <para>
        /// Named here because the disc was the only footprint the clearance check knew, and
        /// the disc is not what collides. <see cref="MinimumNodeSeparation"/> guarantees 220
        /// units; the mark reaches 302 above a node and the end-of-chapter marker hangs a name
        /// plate 227 below its own centre — so the Shallows shipped its marker 308 units
        /// directly above its tenth glade, every gate green, with the plate sitting on the
        /// player's standing on the one glade in the chapter that earns a look.
        /// <c>ChapterMapTests</c> holds these numbers to what the screen draws. The glow behind
        /// a top-tier mark is deliberately not counted: it is light, not a thing.
        /// </para>
        /// </summary>
        public const float CrownHalfWidth = 204f, CrownBottom = 106f, CrownTop = 302f;

        /// <summary>
        /// A perch's own body — rock, disc and the name plate under it — as the rectangle
        /// another node's crown must stay out of: <see cref="BodyHalfWidth"/> either side,
        /// <see cref="BodyBelow"/> under the centre and <see cref="BodyAbove"/> over it.
        /// </summary>
        public const float BodyHalfWidth = 180f, BodyBelow = 227f, BodyAbove = 100f;

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
        /// Sized for the worst case rather than the ordinary one: the underside of the whole
        /// header column on a display whose safe area pushes it down, plus the marker's own
        /// reach above its centre. A display with nothing in the way simply gets more room
        /// than it needs, which is invisible; the other way round is a control the player
        /// cannot see.
        /// </para>
        /// <para>
        /// <b>The column, not the plaque</b> — and getting that wrong is what this number was
        /// last changed for. It was sized against the banner's underside when the banner was
        /// the last thing in the header; the mode switcher then arrived beneath it, and the
        /// marker went on landing at the same 500 units in <em>every</em> chapter of
        /// <em>every</em> mode, half behind a control that had not existed when the figure was
        /// chosen. Nothing could catch it: the marker's coordinate is authored nowhere, so no
        /// content file was wrong, and the clearance check only ever compared it against
        /// glades. <see cref="TeaserTopInset"/> and <see cref="TeaserReach"/> are the other two
        /// terms named so a test can add them to what the header actually measures, which is
        /// the guard that did not exist before — see <c>ChapterMapTests</c>.
        /// </para>
        /// </summary>
        public const float TeaserHeadroom = 700f;

        /// <summary>
        /// The top safe-area inset this headroom is sized against, in canvas units.
        ///
        /// The header hangs from the safe area and the map does not, so every unit a notch
        /// pushes the chrome down is a unit the marker has to give up. Roughly what the
        /// deepest cutouts shipping today cost at this canvas width; a device with none
        /// simply gets more air than it needs.
        /// </summary>
        public const float TeaserTopInset = 180f;

        /// <summary>
        /// How far the marker reaches above its own centre, plus the air that keeps it from
        /// merely touching the control above it. Its disc is <see cref="NodeDiameter"/> and
        /// everything else it carries — the plate, the shadow — hangs below.
        /// </summary>
        public const float TeaserReach = NodeDiameter * .5f + 52f;

        /// <summary>
        /// Where the end-of-chapter marker sits across the map when a chapter does not say.
        /// Every chapter's last glade stands on the left (<c>Tools/chapters/mapart.py</c>), so
        /// this is the other side of the map from it — a marker above the last glade sits its
        /// plate on that glade's standing mark, which is what <see cref="Overshadows"/> refuses.
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

        /// <summary>
        /// Whether the perch standing at <paramref name="body"/> covers any of the standing
        /// mark above the glade at <paramref name="crown"/>. Both are authored fractions and
        /// the answer is in canvas units, which is why the strip count is needed — see
        /// <see cref="Separation"/>. A rectangle test rather than a distance, because the
        /// mark is four times wider than it is tall and a radius that cleared its corners
        /// would refuse every alternating layout that ships.
        /// </summary>
        public static bool Overshadows(Vector2 body, Vector2 crown, int stripCount)
        {
            float dx = Mathf.Abs(body.x - crown.x) * Width;
            if (dx >= BodyHalfWidth + CrownHalfWidth) return false;

            float dy = (body.y - crown.y) * Height(stripCount);
            return dy - BodyBelow < CrownTop && dy + BodyAbove > CrownBottom;
        }
    }
}
