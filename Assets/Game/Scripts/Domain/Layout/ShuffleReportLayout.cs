namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where a graded Shuffle run is drawn (MODES.md 59o): the grade on its emblem, the build score
    /// beside it and the line under the score, then a grid of stat tiles. One block, drawn twice -
    /// in the victory panel's window and on a keeper's public profile - so both read the same
    /// figures off one layout and a mirror (<c>Tools/render_shuffle.py --report</c>) reads them too.
    ///
    /// <para>
    /// Every figure is in the block's own space: x from its centre, y downward from its top edge
    /// (negative), so a host places the block by its top and nothing inside it moves. <b>The grid
    /// grows by rows of <see cref="Columns"/></b>, so a stat added to <c>ShuffleStats</c> costs a
    /// taller block and no layout change; <c>ShuffleGradeTests</c> holds every piece inside
    /// <see cref="Width"/> and clear of its neighbours for any count up to <c>ShuffleStats.MaxRows</c>.
    /// </para>
    /// </summary>
    public sealed class ShuffleReportLayout
    {
        public static readonly ShuffleReportLayout Default = new ShuffleReportLayout();

        /// <summary>The block's width: the victory window's text column (<c>WinOverlay</c>'s rows).</summary>
        public float Width = 780f;

        /// <summary>
        /// The grade's letter (the owner's picture, drawn with its aspect kept inside this box: the
        /// single letters near square, AA, S+ and S++ wide), and the type it falls back to.
        /// </summary>
        public float EmblemX = -265f, EmblemY = -104f, EmblemW = 250f, EmblemH = 180f;
        public int LetterSize = 96, LetterFloor = 52;
        public float LetterLift = 0f;

        /// <summary>
        /// The column right of the emblem: its centre, its widest, and its three lines. The x
        /// figures are the envelope the pair may use; the letter and the column are drawn
        /// <see cref="GroupGap"/> apart and centred together by their drawn widths
        /// (<see cref="CentredPair"/>), so a short score sits in the middle rather than hugging
        /// the left.
        /// </summary>
        public float ColumnX = 115f, ColumnW = 470f, GroupGap = 24f;
        public float CaptionY = -44f, CaptionH = 40f;
        public int CaptionSize = 28, CaptionFloor = 18;
        public float ScoreY = -104f, ScoreH = 76f;
        public int ScoreSize = 70, ScoreFloor = 36;
        public float NoteY = -166f, NoteH = 44f;
        public int NoteSize = 32, NoteFloor = 20;

        /// <summary>Where the grid starts, and one tile: its size, the gap between two, and how many to a row.</summary>
        public float GridTop = -222f, TileW = 250f, TileH = 100f, Gap = 15f;
        public int Columns = 3;

        /// <summary>A tile's picture and its two lines, from the tile's centre.</summary>
        public float IconSize = 64f, IconX = -84f;
        public float TextX = 42f, TextW = 150f;
        public float FigureY = 16f, FigureH = 50f;
        public int FigureSize = 40, FigureFloor = 22;
        public float LabelY = -26f, LabelH = 30f;
        public int LabelSize = 22, LabelFloor = 14;

        /// <summary>Air under the grid's last row.</summary>
        public float Tail = 6f;

        /// <summary>
        /// Where two pieces of drawn widths <paramref name="a"/> and <paramref name="b"/> stand,
        /// <paramref name="gap"/> apart and centred on the block: each one's centre x.
        /// </summary>
        public static float[] CentredPair(float gap, float a, float b) => ShuffleHubStripLayout.CentredRow(gap, a, b);

        /// <summary>How many rows <paramref name="stats"/> tiles take.</summary>
        public int Rows(int stats) => stats <= 0 ? 0 : (stats + Columns - 1) / Columns;

        /// <summary>The block's whole height for <paramref name="stats"/> tiles.</summary>
        public float HeightFor(int stats)
        {
            int rows = Rows(stats);
            float grid = rows <= 0 ? 0f : rows * TileH + (rows - 1) * Gap;
            return -GridTop + grid + Tail;
        }

        /// <summary>
        /// Tile <paramref name="index"/>'s centre. A short last row is centred under the others,
        /// so five stats read as three over two rather than a gap at the end.
        /// </summary>
        public (float x, float y) TileCentre(int index, int stats)
        {
            int row = index / Columns, col = index % Columns;
            int inRow = row == Rows(stats) - 1 && stats % Columns != 0 ? stats % Columns : Columns;

            float span = inRow * TileW + (inRow - 1) * Gap;
            float x = -span * .5f + TileW * .5f + col * (TileW + Gap);
            float y = GridTop - TileH * .5f - row * (TileH + Gap);
            return (x, y);
        }

        /// <summary>Whether every piece is inside the block and clear of its neighbours, naming the fault when not.</summary>
        public bool IsClear(int stats, out string fault)
        {
            float half = Width * .5f;

            if (EmblemX - EmblemW * .5f < -half) { fault = "the emblem runs off the left"; return false; }
            if (EmblemX + EmblemW * .5f > ColumnX - ColumnW * .5f) { fault = "the emblem runs into the score column"; return false; }
            if (ColumnX + ColumnW * .5f > half) { fault = "the score column runs off the right"; return false; }
            if (EmblemY + EmblemH * .5f > 0f) { fault = "the emblem stands above the block"; return false; }
            if (EmblemY - EmblemH * .5f < GridTop) { fault = "the emblem reaches into the grid"; return false; }
            if (CaptionY + CaptionH * .5f > 0f) { fault = "the caption stands above the block"; return false; }
            if (CaptionY - CaptionH * .5f < ScoreY + ScoreH * .5f) { fault = "the caption overlaps the score"; return false; }
            if (ScoreY - ScoreH * .5f < NoteY + NoteH * .5f) { fault = "the score overlaps the note"; return false; }
            if (NoteY - NoteH * .5f < GridTop) { fault = "the note reaches into the grid"; return false; }

            if (Columns * TileW + (Columns - 1) * Gap > Width) { fault = "a row of tiles is wider than the block"; return false; }
            if (IconX - IconSize * .5f < -TileW * .5f) { fault = "a tile's picture runs off it"; return false; }
            if (IconX + IconSize * .5f > TextX - TextW * .5f) { fault = "a tile's picture runs into its text"; return false; }
            if (TextX + TextW * .5f > TileW * .5f) { fault = "a tile's text runs off it"; return false; }
            if (FigureY + FigureH * .5f > TileH * .5f || LabelY - LabelH * .5f < -TileH * .5f) { fault = "a tile's text runs off its top or foot"; return false; }
            if (FigureY - FigureH * .5f < LabelY + LabelH * .5f) { fault = "a tile's figure overlaps its label"; return false; }

            for (int i = 0; i < stats; i++)
            {
                var (x, y) = TileCentre(i, stats);
                if (x - TileW * .5f < -half - .5f || x + TileW * .5f > half + .5f) { fault = $"tile {i} runs off the side"; return false; }
                if (y - TileH * .5f < -HeightFor(stats) + Tail - .5f) { fault = $"tile {i} runs off the foot"; return false; }
            }

            fault = null;
            return true;
        }
    }
}
