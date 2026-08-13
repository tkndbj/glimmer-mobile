namespace GlimmerGrove
{
    /// <summary>
    /// The three light channels, as additive bit masks.
    ///
    /// These live with the board rather than with the UI palette. The rule of the
    /// puzzle — that joined networks mix their colours — is a gameplay fact, and the
    /// parser and validator need it long before anything is drawn. Keeping the bits
    /// here is what lets the whole content pipeline be checked with no renderer,
    /// no Editor and no UI assembly present.
    ///
    /// <see cref="Pal"/> maps these masks onto actual colours; that is presentation
    /// and belongs on the other side of the line.
    /// </summary>
    public static class Energy
    {
        public const int None = 0;
        public const int R = 1;
        public const int G = 2;
        public const int B = 4;

        /// <summary>A lamp wanting <see cref="Any"/> accepts whatever light reaches it.</summary>
        public const int Any = 0;

        public const int All = R | G | B;

        /// <summary>How many distinct mixes exist, including dark.</summary>
        public const int Count = 8;

        /// <summary>
        /// Reads an authored colour letter. Y, M and C are the two-channel mixes and
        /// W is all three, matching how the light actually blends on the board.
        /// </summary>
        public static bool TryParse(char c, out int mask)
        {
            switch (c)
            {
                case 'R': mask = R; return true;
                case 'G': mask = G; return true;
                case 'B': mask = B; return true;
                case 'Y': mask = R | G; return true;
                case 'M': mask = R | B; return true;
                case 'C': mask = G | B; return true;
                case 'W': mask = All; return true;
                case 'A': mask = Any; return true;
                default: mask = None; return false;
            }
        }

        /// <summary>The letter an authored cell would use for this mask.</summary>
        public static char Letter(int mask)
        {
            switch (mask & All)
            {
                case R: return 'R';
                case G: return 'G';
                case B: return 'B';
                case R | G: return 'Y';
                case R | B: return 'M';
                case G | B: return 'C';
                case All: return 'W';
                default: return 'A';
            }
        }
    }
}
