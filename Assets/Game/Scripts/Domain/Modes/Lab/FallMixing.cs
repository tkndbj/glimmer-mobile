using System.Collections.Generic;

namespace GlimmerGrove.Modes
{
    /// <summary>One way two pure colours make a blend, and the one colour that then bursts it.</summary>
    public readonly struct FallRecipe
    {
        /// <summary>The two pure channels that blend into <see cref="Blend"/>.</summary>
        public readonly int First, Second;

        /// <summary>What they make — a two-channel mote.</summary>
        public readonly int Blend;

        /// <summary>The one channel that blend is still missing, which finishes and bursts it.</summary>
        public readonly int Finish;

        public FallRecipe(int first, int second, int blend, int finish)
        {
            First = first;
            Second = second;
            Blend = blend;
            Finish = finish;
        }
    }

    /// <summary>
    /// The colour arithmetic of a well, written out once so a screen can draw it.
    ///
    /// <para>
    /// <b>It exists because the rule is easy to state and hard to hold.</b> "A mote adds its
    /// colour rather than matching it" is one sentence, and a player still has to remember, mid
    /// drop, that yellow is the one that wants blue. That is not difficulty — it is recall, and
    /// recall is the thing a board should be answering for the player rather than testing them
    /// on. So the well draws a legend under its tray, and this is what it draws.
    /// </para>
    /// <para>
    /// <b>Derived, never typed.</b> Every pair of distinct pure channels is a recipe, its blend
    /// is the two of them together and its finisher is whatever is left — so there is no table
    /// here that could come to disagree with <c>FallBoard</c>'s actual rule, which is the same
    /// <c>|</c> on the same masks. A hand-written "yellow needs blue" is a second answer waiting
    /// to be wrong.
    /// </para>
    /// </summary>
    public static class FallMixing
    {
        static readonly FallRecipe[] _all = Build();

        /// <summary>
        /// The three recipes, in the order a legend reads them: red and green, red and blue,
        /// green and blue.
        /// </summary>
        public static IReadOnlyList<FallRecipe> Recipes => _all;

        static FallRecipe[] Build()
        {
            int[] pure = { Energy.R, Energy.G, Energy.B };
            var found = new List<FallRecipe>(3);

            for (int i = 0; i < pure.Length; i++)
                for (int j = i + 1; j < pure.Length; j++)
                {
                    int blend = pure[i] | pure[j];
                    found.Add(new FallRecipe(pure[i], pure[j], blend, Energy.All & ~blend));
                }

            return found.ToArray();
        }
    }
}
