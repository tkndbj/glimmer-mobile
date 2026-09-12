using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a turret hits for and what it can take, drawn as two labelled bars with the figures
    /// beside them — and able to <em>move</em>, because an upgrade's whole payoff is these two
    /// numbers going up.
    ///
    /// <para>
    /// <b>A live component rather than a draw call, and it had to become one.</b> It shipped as a
    /// static builder reading a <c>WardModel</c>: the bars were laid out once when a panel opened,
    /// so they never carried a turret's stars and never moved when one was bought — a player
    /// upgraded a turret and watched nothing happen, which is the worst possible answer on the one
    /// screen that exists to say what an upgrade is worth.
    /// </para>
    /// <para>
    /// <b>It reads a <see cref="WardBuild"/> and never a model</b>, so the figures are the ones a
    /// player actually plays with. That is the same narrowing <c>SiegeTuning.DamageTo</c> made: a
    /// card showing a model's numbers on an upgraded turret is a card that is confidently wrong.
    /// </para>
    /// <para>
    /// <b>Three screens draw it and none of them owns it</b> — the preview panel, the upgrade panel
    /// and the celebration — so the design lives here and what each screen adds goes on top
    /// (<c>PieceCard</c>'s rule, invariant 16l). The numbers themselves are read back through
    /// <see cref="SiegeTuning"/> rather than derived, because a card that applied the power curve
    /// its own way would be a second opinion about what a turret does.
    /// </para>
    /// </summary>
    public sealed class WardStatBars
    {
        /// <summary>How tall the pair is, so a screen can leave room for it.</summary>
        public const float Height = 134f;

        /// <summary>One row, and the gap between the two.</summary>
        const float RowH = 54f, Gap = 26f;

        /// <summary>How wide the caption and the figure are; the bar takes what is left.</summary>
        const float CaptionW = 196f, FigureW = 104f;

        /// <summary>
        /// How wide a "+12" sits beside the figure, and how far it stands off it.
        ///
        /// <b>The gain is written left-aligned in that slot rather than right-aligned</b>, which
        /// is the whole of what closed a gap reported as too big. Right-aligned, a two-character
        /// "+3" hugs the row's own edge and leaves most of a 96-unit reserve as white space
        /// between the figure and its gain - so the two read as two columns rather than as one
        /// reading, and the gap <em>grows</em> the shorter the gain is, which is exactly backwards.
        /// Left-aligned it is <see cref="GainPad"/> whatever the gain says.
        /// </summary>
        const float GainW = 96f, GainPad = 14f;

        /// <summary>How tall the bar itself is inside its row.</summary>
        const float BarH = 26f;

        /// <summary>Half the bar's height, so the ends are semicircles. <c>Art.Round</c> is pixels.</summary>
        const int BarRound = 13;

        /// <summary>
        /// The two colours a bar is filled in.
        ///
        /// <b>Told apart by hue and by what they mean</b>, not by length alone: damage is the warm
        /// one because it is what a turret does to the hill, and health the cool one because it is
        /// what the hill does to it. Neither is one of the board's four gem colours — a bar wearing
        /// one of those would read as a claim about which colour the turret is for.
        /// </summary>
        static readonly Color Hurt = new Color(.85f, .35f, .22f);
        static readonly Color Hold = new Color(.27f, .58f, .38f);

        /// <summary>
        /// What a gain is written in.
        ///
        /// <b>Green, and on a parchment panel it has to be a <em>dark</em> green.</b> This is the
        /// one figure on the row that is not a fact about the turret but a promise about what the
        /// next star adds, so it wears the colour every game writes a gain in. It was the kit's
        /// gold, which is what earning looks like on a dark ground and is very nearly the
        /// parchment's own value on a light one - the panel these bars are drawn on is cream
        /// (250, 219, 193), so a gold figure on it is one the eye slides off. A ceremony drawn on
        /// a dark room wants the opposite and writes its own gains in <c>Pal.Mint</c>: the rule is
        /// the contrast against the ground, never the hue.
        /// </summary>
        static readonly Color Gain = new Color(.10f, .56f, .25f);

        sealed class Row
        {
            public RectTransform Node;
            public Image Trough, Fill;
            public Text Figure, Delta;
            public float BarWidth, BarH;
            public int Shown;
        }

        readonly Row _hurt = new Row();
        readonly Row _hold = new Row();
        readonly bool _gains;

        /// <summary>
        /// What the design is multiplied by.
        ///
        /// <b>One design, drawn at whatever size the screen has room for</b> - <c>PieceCard</c>'s
        /// rule (invariant 16l), and it is here for the same reason it is there: two screens want
        /// this pair at two sizes, and a second table of numbers for the big one is two designs a
        /// week later. It scales the type as well as the boxes, so the large draw is rasterised at
        /// its own size rather than upscaled off the small one.
        /// </summary>
        readonly float _scale;

        WardCatalog _catalog;

        WardStatBars(bool gains, float scale)
        {
            _gains = gains;
            _scale = scale > 0f ? scale : 1f;
        }

        /// <summary>How tall the pair is at <paramref name="scale"/>.</summary>
        public static float HeightAt(float scale) => Height * (scale > 0f ? scale : 1f);

        /// <summary>
        /// Lays the pair out, centred on <paramref name="midY"/> measured down from the panel's
        /// top, and hands back the handle that moves them.
        ///
        /// <b>Stated as a middle</b>, because <c>UIKit.Box</c> pivots at centre whatever it is
        /// anchored to — the trap <c>WardPreviewOverlay</c>'s own bands record.
        ///
        /// <paramref name="gains"/> leaves room beside each figure for a "+12", which is what the
        /// upgrade panel needs and what the other two do not.
        /// </summary>
        public static WardStatBars Build(RectTransform panel, float midY, float width, Color ink,
                                         bool gains = false, float scale = 1f)
        {
            var bars = new WardStatBars(gains, scale);
            if (panel == null) return bars;

            float s = bars._scale;
            float rowH = RowH * s, gap = Gap * s;
            float top = midY - Height * s * .5f;

            bars.Lay(bars._hurt, panel, top + rowH * .5f, width, ink, Hurt, "ui.ward.damage");
            bars.Lay(bars._hold, panel, top + rowH + gap + rowH * .5f, width, ink, Hold,
                     "ui.ward.health");

            return bars;
        }

        /// <summary>
        /// Sets both bars to what <paramref name="build"/> is worth, with no animation.
        ///
        /// <b>Safe to call on every repaint</b>, which is what a panel does: it writes the same
        /// numbers into the same widgets rather than rebuilding them, so nothing restarts and
        /// nothing flickers (invariant 16k's rule about a redraw not disturbing what is drawn).
        /// </summary>
        public void Set(WardBuild build, WardCatalog catalog)
        {
            _catalog = catalog ?? WardCatalog.Default;

            Write(_hurt, Damage(build), Ceiling(true), 0);
            Write(_hold, Health(build), Ceiling(false), 0);
        }

        /// <summary>
        /// Shows what <paramref name="from"/> is worth now, with the gain <paramref name="to"/>
        /// would add written beside each figure.
        ///
        /// <b>The bar is drawn at the <em>current</em> length with the gain marked beside it</b>
        /// rather than at the new one, so a player reads "here is where you are, here is what this
        /// buys" — a bar already drawn at the upgraded length would be showing them something they
        /// have not paid for.
        /// </summary>
        public void Offer(WardBuild from, WardBuild to, WardCatalog catalog)
        {
            _catalog = catalog ?? WardCatalog.Default;

            Write(_hurt, Damage(from), Ceiling(true), Damage(to) - Damage(from));
            Write(_hold, Health(from), Ceiling(false), Health(to) - Health(from));
        }

        /// <summary>
        /// Runs both bars from <paramref name="from"/> up to <paramref name="to"/>, counting the
        /// figures as they go.
        ///
        /// <b>The bar and the number move together and land together</b>, because they are one
        /// reading: a figure that finished before its bar would read as two different claims about
        /// the same turret. The ease settles rather than stopping, so the climb is fast where the
        /// eye lands and slow where it is read.
        /// </summary>
        public void Climb(WardBuild from, WardBuild to, WardCatalog catalog, float seconds = .9f)
        {
            _catalog = catalog ?? WardCatalog.Default;

            Run(_hurt, Damage(from), Damage(to), Ceiling(true), seconds);
            Run(_hold, Health(from), Health(to), Ceiling(false), seconds);
        }

        /// <summary>Where the damage bar sits, so a screen can aim a flourish at it.</summary>
        public RectTransform DamageRow => _hurt.Node;

        /// <summary>Where the health bar sits.</summary>
        public RectTransform HealthRow => _hold.Node;

        // ----------------------------------------------------------------- the numbers
        static int Damage(WardBuild build) => SiegeTuning.DamageFine(0, build.PowerHundredths);

        static int Health(WardBuild build) => SiegeTuning.HealthOf(build);

        /// <summary>
        /// What a bar is measured against: the roster's best, <em>at the top of the star ladder</em>.
        ///
        /// <b>The ceiling has to include the upgrades or a maxed turret overflows its own bar.</b>
        /// It was the roster's raw best, which was right while nothing could exceed it and became a
        /// five-star turret drawing past the end of its trough the day stars shipped. Read off the
        /// roster rather than typed, so a drop that adds a harder-hitting turret shortens every
        /// other bar by itself.
        /// </summary>
        int Ceiling(bool damage)
        {
            var roster = _catalog ?? WardCatalog.Default;

            int tenths = damage ? roster.MostPowerTenths : roster.MostGuardTenths;
            int best = tenths * WardStars.ScaleTenths(WardStars.Most);

            return damage
                 ? SiegeTuning.DamageFine(0, best)
                 : SiegeTuning.WardHealth * best / (WardModel.Baseline * 10);
        }

        // ----------------------------------------------------------------- drawing
        void Lay(Row row, RectTransform panel, float midY, float width, Color ink, Color fill,
                 string caption)
        {
            float s = _scale;
            float rowH = RowH * s, captionW = CaptionW * s, figureW = FigureW * s;
            float barH = BarH * s, gainW = _gains ? GainW * s : 0f, gainPad = GainPad * s;
            int round = Mathf.Max(2, Mathf.RoundToInt(BarRound * s));

            row.Node = UIKit.Box("Stat", panel, new Vector2(width, rowH),
                                 new Vector2(.5f, 1f), new Vector2(0f, -midY));
            row.BarH = barH;

            UIKit.Label("Caption", row.Node, Loc.Get(caption), Type(26), Pal.A(ink, .72f),
                        TextAnchor.MiddleLeft, new Vector2(captionW, rowH),
                        new Vector2(0f, .5f), new Vector2(captionW * .5f, 0f),
                        FontStyle.Bold);

            row.BarWidth = width - captionW - figureW - gainW;

            float barX = captionW + row.BarWidth * .5f - width * .5f;

            row.Trough = UIKit.Img("Trough", row.Node, Art.Round(round), Pal.A(ink, .14f),
                                   new Vector2(row.BarWidth, barH), new Vector2(.5f, .5f),
                                   new Vector2(barX, 0f));
            row.Trough.type = Image.Type.Sliced;
            row.Trough.raycastTarget = false;

            // **Filled from the left edge rather than by scaling the whole bar**, so the rounded
            // end stays the size it was drawn at: a scaled nine-slice squashes its own corners,
            // which is the fault invariant 44a records about the readout plate.
            row.Fill = UIKit.Img("Fill", row.Trough.rectTransform, Art.Round(round), fill,
                                 new Vector2(barH, barH), new Vector2(0f, .5f),
                                 new Vector2(barH * .5f, 0f));
            row.Fill.type = Image.Type.Sliced;
            row.Fill.raycastTarget = false;

            float figureX = -(gainW + figureW * .5f);

            row.Figure = UIKit.Label("Figure", row.Node, "0", Type(32), ink,
                                     TextAnchor.MiddleRight, new Vector2(figureW, rowH),
                                     new Vector2(1f, .5f), new Vector2(figureX, 0f),
                                     FontStyle.Bold);

            if (!_gains) return;

            // **Left-aligned in a slot inset by the padding**, so the gain starts a fixed distance
            // from where the figure ends however many characters it carries. See <see cref="GainW"/>.
            float slot = gainW - gainPad;

            row.Delta = UIKit.Label("Gain", row.Node, string.Empty, Type(30), Gain,
                                    TextAnchor.MiddleLeft, new Vector2(slot, rowH),
                                    new Vector2(1f, .5f), new Vector2(-slot * .5f, 0f),
                                    FontStyle.Bold);
        }

        /// <summary>A design point size at this draw's scale, never below something readable.</summary>
        int Type(int points) => Mathf.Max(10, Mathf.RoundToInt(points * _scale));

        void Write(Row row, int value, int most, int gain)
        {
            if (row.Node == null) return;

            Tween.KillChannel(row.Node, "climb");

            Paint(row, value, most);

            if (row.Delta == null) return;

            row.Delta.text = gain > 0 ? "+" + gain : string.Empty;
        }

        void Run(Row row, int from, int to, int most, float seconds)
        {
            if (row.Node == null) return;

            Tween.KillChannel(row.Node, "climb");

            if (row.Delta != null) row.Delta.text = string.Empty;

            Paint(row, from, most);

            var node = row.Node;

            Tween.Run(seconds, Ease.OutCubic, t =>
            {
                if (!node) return;

                int now = Mathf.RoundToInt(Mathf.Lerp(from, to, t));
                if (now == row.Shown) return;

                Paint(row, now, most);
            }, node, "climb");
        }

        void Paint(Row row, int value, int most)
        {
            row.Shown = value;

            if (row.Figure != null) row.Figure.text = value.ToString();
            if (row.Fill == null) return;

            float share = most <= 0 ? 1f : Mathf.Clamp01(value / (float)most);
            float wide = Mathf.Max(row.BarH, row.BarWidth * share);

            row.Fill.rectTransform.sizeDelta = new Vector2(wide, row.BarH);
            row.Fill.rectTransform.anchoredPosition = new Vector2(wide * .5f, 0f);
        }
    }
}
