using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Colour language of the grove: which actual colour each energy mask is painted.
    ///
    /// The masks themselves belong to <see cref="Energy"/> on the gameplay side —
    /// what mixes with what is a rule, not a look. These aliases stay so existing UI
    /// code reads naturally.
    /// </summary>
    public static class Pal
    {
        public const int None = Energy.None;
        public const int R = Energy.R;
        public const int G = Energy.G;
        public const int B = Energy.B;
        public const int Any = Energy.Any;   // lamps that accept any live colour

        public static Color Hex(string s)
        {
            ColorUtility.TryParseHtmlString(s.StartsWith("#") ? s : "#" + s, out var c);
            return c;
        }

        // -- energy ------------------------------------------------------------

        /// <summary>
        /// The three channels and their blends, painted the way a player expects paint to
        /// behave rather than the way light does.
        ///
        /// <para>
        /// <b>This is a look, not a rule.</b> <see cref="Energy"/> still mixes by <c>|</c> over
        /// three bits and nothing about the boards, the search, par or the vectors moved — the
        /// only thing that changed is which actual colour each of the seven masks is drawn in.
        /// That separation is why it could change at all: every mode asks
        /// <see cref="EnergyColour"/>, and the two legends that teach the arithmetic
        /// (<c>FallMixing</c>, <c>BudMixing</c>) derive their chips from the same masks, so the
        /// board, the tray, the legend and the tips all moved together with no table anywhere
        /// to fall out of step.
        /// </para>
        /// <para>
        /// <b>Why it moved.</b> The channels were additive — red, green and blue, blending to
        /// yellow, magenta and cyan — which is exactly correct for light and is the one thing
        /// nobody outside a graphics pipeline has ever been taught. Reported from play as
        /// confusion rather than as a bug: a player mixing red and blue expects purple, because
        /// what they have handled all their lives is paint. So the middle channel is drawn as
        /// <see cref="Pollen"/> and the blends fall out of the primary-school wheel that needs
        /// no teaching at all — red+yellow is orange, red+blue is purple, yellow+blue is green.
        /// </para>
        /// <para>
        /// <b>The warm three are separated by value as well as by hue</b>, because red, orange
        /// and yellow are genuine neighbours on that wheel where red, green and blue were not.
        /// Poppy is deep, Marigold mid and Pollen bright, so the ladder survives a small disc,
        /// a dim backdrop and a player who reads it out of the corner of an eye.
        /// </para>
        /// <para>
        /// All three still meet at <see cref="Radiance"/>, which paint does not do. That is
        /// deliberate: white is not a colour on these boards, it is the <em>finished</em> state
        /// — a mote about to burst, a bloomed tile, a flower nothing can add to — and it has to
        /// read as completion in every mode. A muddy brown would be arithmetically honest and
        /// would say the opposite of what the state means.
        /// </para>
        /// </summary>
        public static readonly Color Poppy = Hex("#F2404F");   // R
        public static readonly Color Pollen = Hex("#FFDD57");   // G
        public static readonly Color Azure = Hex("#4FC1FF");   // B
        public static readonly Color Marigold = Hex("#FF8A1F");   // R|G
        public static readonly Color Foxglove = Hex("#B478FF");   // R|B
        public static readonly Color Verdant = Hex("#54E48C");   // G|B
        public static readonly Color Radiance = Hex("#FFF4CE");   // R|G|B
        public static readonly Color Dormant = Hex("#3A5064");   // unpowered conduit

        // -- chrome that used to be energy -------------------------------------
        // Ember, Sun, Bloom and Aqua were the old R, R|G, R|B and G|B. They are kept at their
        // own values and are no longer channel colours: every one of them is drawn somewhere
        // that says nothing about light - the brim line, a rarity ladder, a mode accent, the
        // bonus wheel, a shop ribbon - and repointing those at the paint wheel would have been
        // a retune of half the interface hidden inside a change to the board.
        public static readonly Color Ember = Hex("#FF6B57");
        public static readonly Color Sun = Hex("#FFC93C");
        public static readonly Color Bloom = Hex("#FF74D4");
        public static readonly Color Aqua = Hex("#3BE9D8");

        // -- chrome ------------------------------------------------------------
        public static readonly Color Ink = Hex("#20303F");
        public static readonly Color Cream = Hex("#FFF3DC");
        public static readonly Color Parchment = Hex("#F6E0B4");
        public static readonly Color Slate = Hex("#16222E");
        public static readonly Color Board = new Color(0.055f, 0.105f, 0.145f, 0.82f);
        public static readonly Color Slot = new Color(1f, 1f, 1f, 0.045f);
        public static readonly Color Gold = Hex("#FFC23C");
        public static readonly Color Rose = Hex("#E8615A");
        public static readonly Color Mint = Hex("#7BD86A");

        /// <summary>
        /// The green for a line that has to read as good news <em>on the cream panel paper</em>,
        /// and <see cref="Amber"/>'s argument in the other direction.
        ///
        /// <para>
        /// <see cref="Mint"/> cannot do this job and every one of its forty call sites explains
        /// why: it is drawn on the board, on a halo, on a dark plate or as a fill, and it is
        /// bright because all of those are dark. On cream it is a pale colour on a pale ground —
        /// about 1.8:1 — so the defeat panel's "no heart was spent" line, which is 32pt body copy
        /// with no outline and no shadow because that is what body copy is here, was the one
        /// place in the game where a whole sentence had to be squinted at. Darkening it at the
        /// call site with <c>Pal.A</c> would only have made it translucent; what it needed was a
        /// different colour.
        /// </para>
        /// <para>
        /// About 5.3:1 against the paper, which is a shade lighter than the body brown next to
        /// it — enough to read as an aside rather than as the panel's subject, which is what the
        /// line is. Named for the colour rather than for the one line using it, so the next
        /// positive line on cream does not invent a second dark green a shade away from this one.
        /// </para>
        /// </summary>
        public static readonly Color Moss = Hex("#2A7040");

        /// <summary>
        /// The orange between <see cref="Gold"/> and <see cref="Rose"/>, for a line that has
        /// to read as an achievement on the cream panel paper.
        ///
        /// <para>
        /// It exists because neither neighbour does the job. Gold is nearly the value of the
        /// paper it sits on, so it only holds together under a heavy outline — fine for a
        /// 40pt shout like the golden-glade line, wrong for a sentence. Rose is already spoken
        /// for by the star rank directly above, and two warm reds in one column read as one
        /// thing said twice. Named for the colour rather than for the one line using it, so
        /// the next warm accent does not invent a second orange a shade away from this one.
        /// </para>
        /// </summary>
        public static readonly Color Amber = Hex("#FF8A2B");

        /// <summary>
        /// The binding mark on conduits that share a taproot, and the one colour on the
        /// board that deliberately says nothing about light.
        ///
        /// <para>
        /// Every other tint a tile can wear is an <see cref="EnergyColour"/>, so a taproot
        /// drawn in a hue would be claiming to be a colour of light — and the board's whole
        /// language is that colour means energy. Roots are therefore pale rope, one shade
        /// for all of them, and two conduits are matched by the pips on their mark rather
        /// than by tint. Tapping one also pulses its partners, which is the fast answer;
        /// the pips are for reading the board before touching it.
        /// </para>
        /// </summary>
        public static readonly Color Rope = Hex("#D9C39A");

        /// <summary>
        /// The thorns across a briar's closed ways: the second colour on the board that
        /// deliberately says nothing about light, and <see cref="Rope"/>'s argument again.
        ///
        /// <para>
        /// Dry bramble, desaturated well off the energy wheel. The three channels and their
        /// blends are all bright and saturated, so a dull warm brown cannot be read as a dim
        /// one — which matters more here than anywhere, because the thorns are drawn on an
        /// <em>unlit</em> arm and a player who read them as faint light would have the
        /// mechanic exactly backwards. Not rope either: rope means "these tiles turn
        /// together", and a mark that meant two things would be worth less than both.
        /// </para>
        /// </summary>
        public static readonly Color Thorn = Hex("#8A7060");

        /// <summary>
        /// A lens in a well: the third colour on the board that deliberately says nothing about
        /// light, and <see cref="Rope"/>'s argument for the third time.
        ///
        /// <para>
        /// Glass holds no channels, so it may not wear one. That is the easy half. The hard half
        /// is that it may not wear <see cref="Radiance"/> either, which is the obvious choice for
        /// something clear: Radiance <em>is</em> a mote — it is what a mote holding all three
        /// looks like for the instant before it bursts — and a lens tinted with it would read as
        /// a mote permanently about to go off, on the one object whose whole point is that it
        /// never does.
        /// </para>
        /// <para>
        /// So it is a cool near-white, off the energy wheel in the one direction nothing else on
        /// this board uses: Radiance is warm and every channel colour is saturated, so pale and
        /// cold cannot be read as a dim anything. The silhouette carries the rest — glass is
        /// drawn as a rim and a four-point glint where a mote is a filled disc.
        /// </para>
        /// </summary>
        public static readonly Color Glass = Hex("#DCEBF5");
        static readonly Color[] Table =
        {
            Dormant,                            // 0
            Poppy, Pollen, Marigold,            // R, G, R|G
            Azure, Foxglove, Verdant, Radiance  // B, R|B, G|B, R|G|B
        };

        public static Color EnergyColour(int mask) => Table[mask & 7];

        static readonly string[] Names =
        {
            "Dormant", "Poppy", "Pollen", "Marigold", "Azure", "Foxglove", "Verdant", "Radiance"
        };

        public static string EnergyLabel(int mask) => Names[mask & 7];

        public static Color A(Color c, float a) { c.a = a; return c; }

        /// <summary>
        /// Colours for one glade's board, derived from its slate tint so a new level
        /// only has to name a single colour. The conduits are lifted out of that tint
        /// far enough to stay legible without competing with the live light.
        /// </summary>
        public struct BoardTheme
        {
            public Color Floor, Slot, ArmBase, Hub, Glow;

            public static BoardTheme From(Color tint) => new BoardTheme
            {
                Floor = new Color(tint.r, tint.g, tint.b, .87f),
                Slot = new Color(1f, 1f, 1f, .055f),
                ArmBase = Color.Lerp(tint, new Color(.58f, .72f, .84f), .44f),
                Hub = Color.Lerp(tint, new Color(.66f, .80f, .92f), .54f),
                Glow = Lift(tint, .35f),
            };
        }

        /// <summary>Slightly lifted version of a colour, for highlights.</summary>
        public static Color Lift(Color c, float t)
            => Color.Lerp(c, Color.white, t);
    }
}
