namespace GlimmerGrove.Store
{
    /// <summary>
    /// Which rung of a fixed-length ladder a product sits on.
    ///
    /// <para>
    /// <b>It exists because a card now says the same thing twice.</b> A shop cell draws a
    /// painted picture of what arrives and, behind it, a coloured fan of light — and the two
    /// are one statement: <em>this is the sixth of six</em>. The picture is chosen by tier and
    /// the light was, for a day, chosen by a copy of the same arithmetic one file over. Two
    /// copies of a rounding rule is invariant 9a's complaint at the smallest scale it appears
    /// at: a shelf resized from six rungs to five would have moved one of them and not the
    /// other, and the result — a card wearing the fifth picture under the sixth colour — is
    /// not wrong in any way a compile, a validator or a screenshot could name.
    /// </para>
    /// <para>
    /// <b>It is a fraction of the shelf, never a count.</b> A shelf of four and a shelf of six
    /// both have to read as a full ladder, or inserting a rung would leave the top of a shelf
    /// drawn as its middle. That is why this takes the shelf's own size rather than a tier
    /// alone, and why a shelf of one is the top rung rather than the bottom: a product with
    /// nothing to be compared against is the best thing on its shelf by default, which is the
    /// reading a one-time bundle needs.
    /// </para>
    /// <para>
    /// <b>The fraction is never computed as a float.</b> <c>StoreProduct.TierFraction</c> is one
    /// and this deliberately does not read it: the same rung has to be picked on a desktop, on
    /// Mono and under IL2CPP, and a product landing exactly halfway between two rungs is
    /// decided by whichever way a single-precision multiply happened to fall — the hazard
    /// *Hard-won facts* names twice, once in a board generator and once in a star threshold.
    /// Whole numbers rounded half up cannot disagree with themselves.
    /// </para>
    /// <para>
    /// Domain rather than beside the shop screen, for <c>ChapterMap</c>'s reason (invariant
    /// 8a): it is arithmetic, the two callers are in different files, and a rule that decides
    /// what a paying player is shown should be provable without an Editor.
    /// </para>
    /// </summary>
    public static class ShopLadder
    {
        /// <summary>
        /// The rung <paramref name="product"/> lands on, from 0 to <paramref name="rungs"/>-1.
        ///
        /// <para>
        /// A one-time product takes the top rung whatever it costs. Its tier is honest — the
        /// starter bundle is the cheapest thing in the shop — but drawing it as the cheapest
        /// thing in the shop would be telling the truth about the price and a lie about the
        /// offer, and it is the one product on its shelf a player sees once.
        /// </para>
        /// </summary>
        public static int Rung(StoreProduct product, int rungs)
        {
            if (rungs <= 1 || product == null) return 0;
            if (product.IsOneTime) return rungs - 1;

            return Rung(product.Tier, product.ShelfSize, rungs);
        }

        /// <summary>
        /// The rung a product of <paramref name="tier"/> lands on, 1 being the smallest of
        /// <paramref name="shelfSize"/> products. Whole numbers throughout, rounded half up.
        /// </summary>
        public static int Rung(int tier, int shelfSize, int rungs)
        {
            if (rungs <= 1) return 0;

            // A shelf nothing has ranked yet, and a shelf of one, are both "the top" — see the
            // one-time bundle above. Anything else is (tier-1) steps up a shelf of (size-1).
            if (shelfSize <= 1 || tier <= 0) return rungs - 1;

            int steps = shelfSize - 1;
            int rung = ((tier - 1) * (rungs - 1) * 2 + steps) / (steps * 2);

            return rung < 0 ? 0 : rung > rungs - 1 ? rungs - 1 : rung;
        }
    }
}
