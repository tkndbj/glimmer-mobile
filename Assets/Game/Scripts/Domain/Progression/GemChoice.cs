namespace GlimmerGrove
{
    /// <summary>
    /// What a player can actually do about a gem price they have just been shown.
    ///
    /// <para>
    /// Three states rather than a pair of booleans on a panel, and it is here rather than in
    /// an overlay for the reason <c>HintPrompt</c>, <c>AccountGate</c> and <c>GroveUnveil</c>
    /// are: a <c>switch</c> inside a <c>MonoBehaviour</c> is the one place in this project
    /// nothing can be proved. The distinction that matters is the third one — a player who
    /// cannot pay <em>and</em> cannot buy is looking at a dead end, and this project's rule is
    /// that a control which can never work is worse than no control.
    /// </para>
    /// <para>
    /// <b>It is not about continuing, and it used to be called <c>ContinueChoice</c>.</b> The
    /// second gem-priced offer at a fail state — hearts, so a lost run can be tried again —
    /// needed exactly this branch, and the choice was between a second copy of four lines and
    /// one honest name. Invariant 9a's argument, at the smallest scale it appears at: a rule
    /// that exists twice is a rule that comes to disagree with itself, and the analytics
    /// strings are hand-written (<c>LevelAnalytics</c>) rather than derived from these names,
    /// so nothing on the wire moved with the rename.
    /// </para>
    /// </summary>
    public enum GemChoice
    {
        /// <summary>Nothing can be done: there is no offer worth putting in front of anybody.</summary>
        Unavailable = 0,

        /// <summary>The gems are in hand. One tap and it is bought.</summary>
        Spend = 1,

        /// <summary>
        /// Short of gems, but there is a shop that sells them.
        ///
        /// <para>
        /// Ask about the blocking condition before the price — the house rule
        /// <c>HintPrompt</c> and <c>CompanionPurchaseState</c> already follow. What is
        /// different at a fail state is where it leads: <b>a short balance must not
        /// navigate</b>, because a run is frozen behind the panel and leaving it forfeits a
        /// heart. The gems are brought to the player instead (<c>GemShopOverlay</c>).
        /// </para>
        /// </summary>
        BuyGems = 2,
    }

    /// <summary>
    /// The one rule that turns a balance and a price into a <see cref="GemChoice"/>.
    ///
    /// <para>
    /// Four lines and its own type, because it is the branch worth pinning: the difference
    /// between offering a purchase and offering nothing is the difference between a second
    /// chance and a panel that wastes a tap at the worst moment in a session. Two features ask
    /// it — carrying a lost run on (<c>RunContinue</c>) and buying the hearts to try it again
    /// (<see cref="Progression.HeartRescue"/>) — and a third will.
    /// </para>
    /// </summary>
    public static class GemPrice
    {
        /// <summary>
        /// What a player holding <paramref name="gemsHeld"/> can do about a price of
        /// <paramref name="price"/>.
        /// </summary>
        /// <param name="gemsForSale">
        /// Whether a shop is reachable that could sell them some. False in a build with no
        /// store SDK and while the store has not connected — and a "buy gems" button in
        /// either of those leads nowhere, which is worse than no button at all.
        /// </param>
        public static GemChoice ChoiceFor(long gemsHeld, long price, bool gemsForSale)
        {
            if (price <= 0L) return GemChoice.Unavailable;
            if (gemsHeld >= price) return GemChoice.Spend;
            return gemsForSale ? GemChoice.BuyGems : GemChoice.Unavailable;
        }
    }
}
