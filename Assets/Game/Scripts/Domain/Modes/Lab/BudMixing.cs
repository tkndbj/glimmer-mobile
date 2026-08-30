using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One way two colours make a third, as a grove's legend reads it.</summary>
    public readonly struct BudRecipe
    {
        /// <summary>The flower standing on the board.</summary>
        public readonly int Flower;

        /// <summary>The colour in hand, which is added to it.</summary>
        public readonly int Hand;

        /// <summary>What it becomes.</summary>
        public readonly int Made;

        public BudRecipe(int flower, int hand, int made)
        {
            Flower = flower;
            Hand = hand;
            Made = made;
        }
    }

    /// <summary>
    /// The colour arithmetic of a grove, written out once so a screen can draw it.
    ///
    /// <para>
    /// <b><c>FallMixing</c>'s argument, and it is the same argument.</b> "The colour in hand is
    /// added to the flower you tap" is one sentence and the whole mode — and a player still has
    /// to remember, mid-tap, that the pink one came from red and blue and that tapping it with
    /// green ends it. That is not difficulty, it is recall, and recall is the thing a board
    /// should answer for the player rather than test them on. So the grove draws a legend above
    /// it, and this is what the legend draws.
    /// </para>
    /// <para>
    /// <b>Derived, never typed.</b> Every pair of distinct pure channels is a recipe and what it
    /// makes is the two of them together — the same <c>|</c> on the same masks that
    /// <c>BudBoard.Mix</c> runs — so there is no table here that could come to disagree with the
    /// rule it describes. A hand-written "red and blue make pink" is a second answer waiting to
    /// be wrong, and this mode has four colours' worth of them.
    /// </para>
    /// <para>
    /// <b>Three, not four.</b> A blend tapped with the one channel it lacks makes white, and
    /// white is the one flower that can never be mixed into again — which is worth knowing and
    /// is <em>not</em> on the legend. It is on the board instead: white is the only flower drawn
    /// with a different silhouette and the only one that moves while nobody is tapping, so it
    /// announces itself where the player is already looking. A fourth chip would cost the grove
    /// a row of cells on the shortest screen this game is drawn on to say something the board
    /// says better.
    /// </para>
    /// </summary>
    public static class BudMixing
    {
        static readonly BudRecipe[] _all = Build();

        /// <summary>
        /// The three recipes, in the order a legend reads them: red and green, red and blue,
        /// green and blue.
        /// </summary>
        public static IReadOnlyList<BudRecipe> Recipes => _all;

        static BudRecipe[] Build()
        {
            int[] pure = { Energy.R, Energy.G, Energy.B };
            var found = new List<BudRecipe>(3);

            for (int i = 0; i < pure.Length; i++)
                for (int j = i + 1; j < pure.Length; j++)
                    found.Add(new BudRecipe(pure[i], pure[j], pure[i] | pure[j]));

            return found.ToArray();
        }
    }
}
