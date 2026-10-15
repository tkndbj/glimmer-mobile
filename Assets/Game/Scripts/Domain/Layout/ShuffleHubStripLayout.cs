namespace GlimmerGrove.Layout
{
    /// <summary>
    /// The Shuffle hub's best run (MODES.md 59o): one plate under the best-wave medal and over
    /// BATTLE - a header row (the caption, the grade's letter and the build score, centred as one
    /// group) and under it a chip per stat in rows of <see cref="Columns"/>, across the plate's
    /// whole width. The compact twin of <see cref="ShuffleReportLayout"/>, which the victory panel
    /// and a profile draw at full size.
    ///
    /// <para>
    /// Every figure is in the plate's own space: x from its centre, y downward from its top edge
    /// (negative), as the report's are. <b>Its height is a fixed slot</b>
    /// (<see cref="EndlessHubLayout.StatsHeight"/>), the slot the Infinite lane spends on its
    /// checkpoint bar made taller, so the hub's column is one shape whatever the record holds and
    /// a first graded run moves nothing on the screen. <b>Sized to be read</b> (the owner,
    /// 2026-10-15: "a little bigger, too hard to read"): full-width chips with a 34 figure and a 22
    /// label. <c>EndlessHubTests</c> holds every piece inside the plate and clear of its neighbours;
    /// <c>Tools/render_endless.py --shuffle --measure</c> draws it in every language.
    /// </para>
    /// </summary>
    public sealed class ShuffleHubStripLayout
    {
        public static readonly ShuffleHubStripLayout Default = new ShuffleHubStripLayout();

        /// <summary>The plate: as wide as the lines' plate above it, and the slot's height.</summary>
        public float Width = EndlessHubLayout.PanelWidth, Height = EndlessHubLayout.StatsHeight;

        /// <summary>
        /// The header row: its centre and height, and its three pieces, laid side by side and
        /// centred as one group by their drawn widths (<see cref="Centred"/>) - the caption, the
        /// letter (aspect kept inside its box) and the score, <see cref="GroupGap"/> apart.
        /// </summary>
        public float HeaderY = -48f, HeaderH = 72f, GroupGap = 22f, HeaderMost = 780f;
        public int CaptionSize = 26, CaptionFloor = 16;
        public float LetterW = 170f, LetterH = 66f;
        public int LetterSize = 56, LetterFloor = 32;
        public int ScoreSize = 46, ScoreFloor = 26;

        /// <summary>The chips: the grid's top, one chip's size, the gap, how many to a row.</summary>
        public float GridTop = -92f, ChipW = 264f, ChipH = 78f, Gap = 10f;
        public int Columns = 3;

        /// <summary>A chip's picture and two lines, from the chip's centre.</summary>
        public float IconSize = 58f, IconX = -96f;
        public float TextX = 33f, TextW = 182f;
        public float FigureY = 15f, FigureH = 40f;
        public int FigureSize = 34, FigureFloor = 22;
        public float LabelY = -21f, LabelH = 30f;
        public int LabelSize = 22, LabelFloor = 14;

        /// <summary>The line a keeper with no graded run reads, across the whole plate.</summary>
        public float EmptyW = 760f, EmptyH = 200f;
        public int EmptySize = 36, EmptyFloor = 22;

        /// <summary>How many rows <paramref name="stats"/> chips take.</summary>
        public int Rows(int stats) => stats <= 0 ? 0 : (stats + Columns - 1) / Columns;

        /// <summary>The grid's left edge: the full rows centred on the plate.</summary>
        public float GridLeft => -(Columns * ChipW + (Columns - 1) * Gap) * .5f;

        /// <summary>Chip <paramref name="index"/>'s centre, in plate space.</summary>
        public (float x, float y) ChipCentre(int index)
        {
            int row = index / Columns, col = index % Columns;
            return (GridLeft + ChipW * .5f + col * (ChipW + Gap),
                    GridTop - ChipH * .5f - row * (ChipH + Gap));
        }

        /// <summary>
        /// Where pieces of drawn widths <paramref name="widths"/> stand, side by side and
        /// <see cref="GroupGap"/> apart, so the whole group is centred on the plate: each piece's
        /// centre x. The one rule the screen and its mirror both place the header by.
        /// </summary>
        public float[] Centred(params float[] widths) => CentredRow(GroupGap, widths);

        /// <summary><see cref="Centred"/> for any gap: shared with the report's score group.</summary>
        public static float[] CentredRow(float gap, params float[] widths)
        {
            var centres = new float[widths.Length];
            float total = 0f;
            for (int i = 0; i < widths.Length; i++) total += widths[i] + (i > 0 ? gap : 0f);

            float x = -total * .5f;
            for (int i = 0; i < widths.Length; i++)
            {
                centres[i] = x + widths[i] * .5f;
                x += widths[i] + gap;
            }

            return centres;
        }

        /// <summary>
        /// Whether every piece is inside the plate and clear of its neighbours for
        /// <paramref name="stats"/> chips, naming the fault when not. More chips than the plate has
        /// rows for is a fault, not an overflow: a stat added to <c>ShuffleStats</c> past six is a
        /// layout decision this test makes somebody take.
        /// </summary>
        public bool IsClear(int stats, out string fault)
        {
            float half = Width * .5f;

            if (HeaderY + HeaderH * .5f > 0f) { fault = "the header stands above the plate"; return false; }
            if (HeaderY - HeaderH * .5f < GridTop) { fault = "the header reaches into the chips"; return false; }
            if (LetterH > HeaderH) { fault = "the letter is taller than the header"; return false; }
            if (HeaderMost > Width) { fault = "the header group may run wider than the plate"; return false; }

            if (-GridLeft > half) { fault = "a row of chips runs off the plate"; return false; }
            if (IconX - IconSize * .5f < -ChipW * .5f) { fault = "a chip's picture runs off it"; return false; }
            if (IconX + IconSize * .5f > TextX - TextW * .5f) { fault = "a chip's picture runs into its text"; return false; }
            if (TextX + TextW * .5f > ChipW * .5f) { fault = "a chip's text runs off it"; return false; }
            if (IconSize > ChipH) { fault = "a chip's picture is taller than the chip"; return false; }
            if (FigureY + FigureH * .5f > ChipH * .5f || LabelY - LabelH * .5f < -ChipH * .5f) { fault = "a chip's text runs off its top or foot"; return false; }
            if (FigureY - FigureH * .5f < LabelY + LabelH * .5f) { fault = "a chip's figure overlaps its label"; return false; }

            int rows = Rows(stats);
            float foot = GridTop - rows * ChipH - (rows > 0 ? (rows - 1) * Gap : 0f);
            if (foot < -Height) { fault = $"{stats} chips take {rows} rows, past the plate's foot"; return false; }

            if (EmptyW > Width || EmptyH > Height) { fault = "the empty line is larger than the plate"; return false; }

            fault = null;
            return true;
        }
    }
}
