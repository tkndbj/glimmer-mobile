namespace GlimmerGrove.Layout
{
    /// <summary>
    /// Where everything on the Shuffle lane's unlock panel sits (MODES.md 59n): the headline,
    /// the tier, the emblem, the power's name and what it does, the cards it was made of, and
    /// the key that closes it. Every figure is a centre measured up from the middle of the
    /// canvas, and a height.
    ///
    /// <para>
    /// <b>Domain, so the overlap is a test</b> (<c>ShuffleSecretTests</c>, invariant 8a's rule):
    /// the panel is a column of six things over a moving room, and a caption drawn through the
    /// emblem or a key off the foot of the shortest phone is exactly what no compile can see.
    /// <c>Tools/render_shuffle.py --unlock</c> draws it with the real face in every language.
    /// </para>
    /// </summary>
    public sealed class ShuffleUnlockLayout
    {
        public static readonly ShuffleUnlockLayout Default = new ShuffleUnlockLayout();

        /// <summary>The widest any caption runs, against a 1080 canvas.</summary>
        public float Wide = 960f;

        public float TitleY = 576f, TitleH = 150f;
        public int TitleSize = 92, TitleFloor = 50;

        public float TierY = 470f, TierH = 56f;
        public int TierSize = 40, TierFloor = 24;

        /// <summary>The emblem's centre and size, and how far out the cards that make it start.</summary>
        public float EmblemY = 214f, EmblemSize = 280f, Gather = 330f;

        /// <summary>A card's picture flying into the emblem.</summary>
        public float PartSize = 150f;

        public float NameY = -14f, NameH = 110f;
        public int NameSize = 76, NameFloor = 42;

        public float NoteY = -182f, NoteH = 190f, NoteWide = 900f;
        public int NoteSize = 42, NoteFloor = 26;

        /// <summary>The row of cards it was made of: each picture's size and the room for a sign between two.</summary>
        public float RecipeY = -384f, RecipeIcon = 112f, RecipeGap = 64f;

        public float ButtonY = -564f, ButtonW = 520f, ButtonH = 150f;

        /// <summary>How wide the recipe row runs for <paramref name="parts"/> pictures.</summary>
        public float RecipeWidth(int parts) => parts <= 0 ? 0f : parts * RecipeIcon + (parts - 1) * RecipeGap;

        /// <summary>Where picture <paramref name="index"/> of <paramref name="parts"/> stands across the recipe row.</summary>
        public float RecipeX(int index, int parts)
            => -RecipeWidth(parts) * .5f + RecipeIcon * .5f + index * (RecipeIcon + RecipeGap);

        /// <summary>The top of the whole column and its foot, from the middle of the canvas.</summary>
        public float Top => TitleY + TitleH * .5f;

        public float Foot => ButtonY - ButtonH * .5f;

        /// <summary>Whether every piece clears its neighbours and the column fits the shortest canvas, naming the fault when not.</summary>
        public bool IsClear(int mostParts, out string fault)
        {
            fault = null;

            float[] tops = { TitleY + TitleH * .5f, TierY + TierH * .5f, EmblemY + EmblemSize * .5f,
                             NameY + NameH * .5f, NoteY + NoteH * .5f, RecipeY + RecipeIcon * .5f,
                             ButtonY + ButtonH * .5f };
            float[] feet = { TitleY - TitleH * .5f, TierY - TierH * .5f, EmblemY - EmblemSize * .5f,
                             NameY - NameH * .5f, NoteY - NoteH * .5f, RecipeY - RecipeIcon * .5f,
                             ButtonY - ButtonH * .5f };
            string[] names = { "headline", "tier", "emblem", "name", "note", "recipe", "key" };

            for (int i = 0; i + 1 < tops.Length; i++)
            {
                if (feet[i] >= tops[i + 1]) continue;
                fault = $"the {names[i]} ends at {feet[i]} and the {names[i + 1]} begins at {tops[i + 1]}";
                return false;
            }

            float room = Content.ChapterMap.Width;
            if (Wide > room || NoteWide > room || ButtonW > room)
            {
                fault = $"a caption is wider than the {room} canvas";
                return false;
            }

            if (RecipeWidth(mostParts) > Wide)
            {
                fault = $"{mostParts} cards run {RecipeWidth(mostParts)} across a {Wide} column";
                return false;
            }

            if (EmblemY + Gather + PartSize * .5f > CanvasHalf || EmblemY - Gather - PartSize * .5f < -CanvasHalf)
            {
                fault = "the cards gathering into the emblem start off the canvas";
                return false;
            }

            if (Top > CanvasHalf || Foot < -CanvasHalf)
            {
                fault = $"the column runs {Foot}..{Top} on a canvas {CanvasHalf * 2f} tall";
                return false;
            }

            return true;
        }

        /// <summary>Half the shortest canvas this game lays out for.</summary>
        static float CanvasHalf => PanelStack.TightestCanvas * .5f;
    }
}
