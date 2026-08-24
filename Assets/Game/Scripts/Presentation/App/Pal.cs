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
        public static readonly Color Ember = Hex("#FF6B57");   // R
        public static readonly Color Verdant = Hex("#54E48C");   // G
        public static readonly Color Azure = Hex("#4FC1FF");   // B
        public static readonly Color Sun = Hex("#FFC93C");   // R|G
        public static readonly Color Bloom = Hex("#FF74D4");   // R|B
        public static readonly Color Aqua = Hex("#3BE9D8");   // G|B
        public static readonly Color Radiance = Hex("#FFF4CE");   // R|G|B
        public static readonly Color Dormant = Hex("#3A5064");   // unpowered conduit

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
        /// Where the light must never reach. Deep violet, well off the energy wheel — the
        /// three channels and their blends are all bright, so a dark cool tint cannot be
        /// mistaken for any of them, and it is the same night the streak page is graded in.
        /// </summary>
        public static readonly Color Dusk = Hex("#6B5BC4");

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

        static readonly Color[] Table =
        {
            Dormant,                       // 0
            Ember, Verdant, Sun,           // R, G, R|G
            Azure, Bloom, Aqua, Radiance   // B, R|B, G|B, R|G|B
        };

        public static Color EnergyColour(int mask) => Table[mask & 7];

        static readonly string[] Names =
        {
            "Dormant", "Ember", "Verdant", "Sunfire", "Azure", "Blossom", "Tidal", "Radiance"
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
