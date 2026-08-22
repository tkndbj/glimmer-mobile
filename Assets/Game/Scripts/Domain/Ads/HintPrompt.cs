namespace GlimmerGrove.Ads
{
    /// <summary>What tapping the hint button should do.</summary>
    public enum HintTap
    {
        /// <summary>Spend one from the pool and turn a conduit.</summary>
        Reveal,

        /// <summary>The pool is empty. Open the offer panel.</summary>
        Offer,

        /// <summary>Every conduit the player can turn is already right. Say so.</summary>
        NothingToReveal
    }

    /// <summary>
    /// The hint button's two decisions: what a tap means, and whether running dry is worth
    /// a panel of its own.
    ///
    /// <para>
    /// Here rather than inside <c>PlayScreen</c> for <c>RenameRules</c>' reason — a
    /// <c>switch</c> in a <c>MonoBehaviour</c> is the one place in this project nothing can
    /// be proved about, and every state these rules are about (an empty pool, a spent daily
    /// cap, a board with nothing left to point at) is one the Editor never sits in. Pure,
    /// with no clock and no Unity type, so all of it runs in the offline suite.
    /// </para>
    /// <para>
    /// The property worth stating, and what the tests assert: <b>the pool never decides
    /// whether the button works</b>. An empty pool routes to the offer; only the board can
    /// produce a refusal, and that refusal is a sentence rather than a dead control. A
    /// greyed hint button is how a player learns the feature is broken and stops looking at
    /// it, which costs more than the video it failed to show — <c>AdOfferState</c>'s whole
    /// argument, one screen further in.
    /// </para>
    /// </summary>
    public static class HintPrompt
    {
        /// <summary>
        /// What a tap on the hint button means right now.
        ///
        /// <para>
        /// The board is asked <em>before</em> the pool, and the order is the safety: a
        /// player is never sold a video for a hint that could not have been spent, and a
        /// board with nothing to point at cannot cost anybody one either.
        /// </para>
        /// </summary>
        /// <param name="boardHasHint">Whether the board has a conduit left to point at.</param>
        /// <param name="poolHasHint">Whether the account holds at least one hint.</param>
        public static HintTap OnTap(bool boardHasHint, bool poolHasHint)
            => !boardHasHint ? HintTap.NothingToReveal
             : poolHasHint ? HintTap.Reveal
             : HintTap.Offer;

        /// <summary>
        /// Whether spending a hint that emptied the pool should raise the offer by itself.
        ///
        /// <para>
        /// The moment is the argument: the player has just decided a hint was worth having
        /// and has none left, which is the highest intent this placement ever sees. Waiting
        /// for them to tap a button whose badge has quietly become a question mark spends
        /// that moment.
        /// </para>
        /// <para>
        /// Three conditions, and each removes a way of being a nuisance rather than an
        /// offer. The run has to still be <b>live</b> — a hint that solved the glade, a
        /// clock that ran out behind the reveal, or a screen already leaving must not have a
        /// panel thrown over them. The pool has to be genuinely <b>empty</b>, so this fires
        /// once per pool rather than after every hint. And there has to be something to
        /// <b>offer</b>: a panel with no video behind it is worth opening when a player
        /// asked for it — it still carries the countdown — and is nagging when nobody did.
        /// </para>
        /// </summary>
        /// <param name="runIsLive">The board is still playable and the screen still owns it.</param>
        /// <param name="poolHasHint">Whether the account still holds one.</param>
        /// <param name="offerWorthShowing">
        /// <c>RewardedAds.ShouldOffer</c> — true while the placement is ready, loading,
        /// cooling down or capped for the day, and false when it cannot help at all.
        /// </param>
        public static bool OffersAfterSpending(bool runIsLive, bool poolHasHint, bool offerWorthShowing)
            => runIsLive && !poolHasHint && offerWorthShowing;
    }
}
