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
