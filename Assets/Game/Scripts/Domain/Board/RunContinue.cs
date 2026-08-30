using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove
{
    /// <summary>
    /// The unit a mode's allowance is measured in, and therefore what a continue hands over.
    ///
    /// <para>
    /// A permanent enum with explicit values for <c>DefeatReason</c>'s reason: it reaches
    /// analytics, where an ordinal that moved would silently re-label history. It is
    /// deliberately <em>not</em> a mode id — two modes could share a unit, and the panel
    /// speaks in the unit rather than in the mode.
    /// </para>
    /// </summary>
    public enum ContinueUnit
    {
        /// <summary>Turns on a glade's move budget (invariant 22).</summary>
        Turns = 0,

        /// <summary>
        /// <b>Retired.</b> Cells of light in a Lightweave pot of ink. The mode is gone; the
        /// member stays because the ordinal reaches analytics on every continue ever bought,
        /// so re-pointing it at another unit would silently re-label that history — the same
        /// rule <c>DefeatReason.OutOfInk</c> is kept under.
        /// </summary>
        Ink = 1,

        /// <summary>
        /// Motes in a well's supply. Lightfall's turn is a drop, and its budget is the same
        /// <c>par x budgetFactor</c> every other mode is dealt, counted in motes.
        /// </summary>
        Motes = 2,

        /// <summary>
        /// Tiles in a grove's basket. Groovekeeper's turn is a tile, planted or composted, and
        /// its budget is par plus the slack every mode with a countable mistake is dealt.
        /// </summary>
        Tiles = 3,

        /// <summary>Taps in a thicket's satchel (<c>BudSatchel</c>).</summary>
        Taps = 4,
    }

    /// <summary>
    /// One offer to carry a lost run on: what it costs, what it hands over, and what the
    /// player is in a position to do about it.
    ///
    /// <para>
    /// A plain reading of five numbers rather than a call into anything, for
    /// <c>ChapterGate</c>'s reason: the panel draws it, the screen decides from it and a test
    /// pins it, and one struct is what stops those three coming to disagree about what is
    /// being sold.
    /// </para>
    /// </summary>
    public readonly struct ContinueOffer
    {
        /// <summary>The unit this run is measured in, which is what the panel speaks in.</summary>
        public readonly ContinueUnit Unit;

        /// <summary>
        /// The whole allowance handed over, deficit included — the number the panel prints
        /// and the number the mode is given.
        ///
        /// <para>
        /// <b>Deficit included, and that is the rule that makes this an offer rather than a
        /// charge.</b> A weave is not lost when its meter reads zero; it is lost when what is
        /// left cannot cover the cheapest possible finish, so there may be light in the pot
        /// and none of it spendable. Handing over the authored allowance alone would put the
        /// player back on a board that is still provably unwinnable, which would end the run
        /// again in the same frame — having taken their gems. So the shortfall is cleared
        /// first and the authored figure is working room on top of it.
        /// </para>
        /// <para>
        /// A glade has no such notion — every turn is a turn, and a board with one turn left
        /// is playable — so its deficit is nought and this is exactly what the table authored.
        /// That is why the deficit is asked of the <em>mode</em> rather than worked out here.
        /// </para>
        /// </summary>
        public readonly int Amount;

        /// <summary>What it costs, in gems.</summary>
        public readonly long Gems;

        /// <summary>
        /// Continues already bought on this run, which is what the price is derived from.
        ///
        /// Carried on the offer rather than looked up, so the panel, the debit and the
        /// analytics event all quote the same attempt number.
        /// </summary>
        public readonly int Taken;

        /// <summary>What the player can do about it. See <see cref="GemChoice"/>.</summary>
        public readonly GemChoice Choice;

        ContinueOffer(ContinueUnit unit, int amount, long gems, int taken, GemChoice choice)
        {
            Unit = unit;
            Amount = amount;
            Gems = gems;
            Taken = taken;
            Choice = choice;
        }

        /// <summary>No offer at all: the run ends the way it always did.</summary>
        public static readonly ContinueOffer None =
            new ContinueOffer(ContinueUnit.Turns, 0, 0L, 0, GemChoice.Unavailable);

        /// <summary>True when there is something worth putting in front of the player.</summary>
        public bool Exists => Choice != GemChoice.Unavailable && Amount > 0 && Gems > 0L;

        /// <summary>True when the gems are already in hand.</summary>
        public bool Affordable => Choice == GemChoice.Spend;

        internal static ContinueOffer Make(ContinueUnit unit, int amount, long gems, int taken,
                                           GemChoice choice)
            => new ContinueOffer(unit, amount, gems, taken, choice);

        public override string ToString()
            => Exists ? $"+{Amount} {Unit} for {Gems} gems ({Choice}, {Taken} taken)" : "none";
    }

    /// <summary>
    /// Whether a lost run can be carried on, at what price, and the debit that does it.
    ///
    /// <para>
    /// <b>Why it is one place rather than one per mode.</b> Every mode here has a fail state
    /// and every fail state costs the player a heart, so "what a way out of a run costs" has
    /// been <c>RunScreen</c>'s and never a mode's since Lightweave shipped a restart that was
    /// free (see <c>RunStakeTests</c>). A continue is the same rule read from the other end —
    /// it is the one way out that costs money instead — and two copies of it would be two
    /// prices, two idempotency keys and two chances to charge somebody for a board that was
    /// still lost. What a mode contributes is the only thing it alone knows: how much
    /// allowance it takes to make its own board playable again.
    /// </para>
    /// <para>
    /// <b>Why the economy is safe without a line of server work.</b> The gems leave through
    /// <see cref="PlayerProgression.TrySpend"/>, which carries an idempotency key, lands
    /// locally so it works on a plane, and is refused by <c>submitSpends</c> on the next sync
    /// if the server-derived balance could not cover it — the same two lines that buy a
    /// companion. What they buy is turns on a board: it mints nothing, is stored nowhere, and
    /// is gone when the run ends. And it cannot inflate a reward, because stars are held
    /// against par rather than against the budget (invariant 22), so a run that had to be
    /// bought is already past the two-star line and can only ever pay one star — less than
    /// replaying the glade for nothing would.
    /// </para>
    /// <para>
    /// Note the analytics id. <c>run_continue</c> is a <b>retired</b> placement id and must
    /// never be reused (invariant 22); it named a rewarded video that bought seconds on a
    /// clock that no longer exists, and re-pointing it would silently re-label history in a
    /// mediation dashboard. This feature is <c>continue_*</c> throughout, including the spend
    /// reason, which is free text a support case reads.
    /// </para>
    /// </summary>
    public static class RunContinue
    {
        /// <summary>
        /// A mode's answer for "no amount of allowance would help".
        ///
        /// <para>
        /// Its own value rather than a large deficit, because the two are different
        /// statements and only one of them may be sold. A weave whose every pair is walled in
        /// is unwinnable at any price; charging for that would be charging for nothing, and it
        /// is the one case where the honest response is to let the run end.
        /// </para>
        /// </summary>
        public const int NoContinue = -1;

        /// <summary>What the debit is written down as, for a support case reading a ledger.</summary>
        public const string SpendReason = "continue:";

        /// <summary>
        /// Builds the offer for a run that has just been lost.
        ///
        /// <para>
        /// Pure — every input is passed in and nothing static is read — so every branch of it
        /// is proved offline against plain integers. That is deliberate: this is the function
        /// that decides whether somebody is asked for money, and the three inputs that decide
        /// it (what they hold, what it costs, whether there is a shop) are exactly the three
        /// that vary between a test and a phone.
        /// </para>
        /// </summary>
        /// <param name="unit">The unit the mode measures its allowance in.</param>
        /// <param name="deficit">
        /// How much allowance must be restored before a grant is usable room, or
        /// <see cref="NoContinue"/> when the board cannot be rescued at any price.
        /// </param>
        /// <param name="taken">Continues already bought on this run.</param>
        /// <param name="gemsHeld">The player's gem balance.</param>
        /// <param name="gemsForSale">
        /// Whether a shop is reachable that could sell them some. False in a build with no
        /// store SDK, in the Editor, and while the store has not connected — and a "buy gems"
        /// button in any of those leads nowhere, which is worse than no button at all.
        /// </param>
        public static ContinueOffer Offer(ContinueUnit unit, int deficit, int taken,
                                          long gemsHeld, bool gemsForSale)
        {
            var table = ContinueRules.Table;
            if (table == null || !table.Enabled) return ContinueOffer.None;

            if (deficit < 0) return ContinueOffer.None;            // NoContinue, and anything odd
            if (taken < 0) taken = 0;

            int room = table.AmountFor(unit);
            if (room <= 0) return ContinueOffer.None;

            // Saturating, for ContinueTable.PriceFor's reason. A deficit is bounded by the
            // board and the allowance by ContinueLimits, so this cannot overflow in practice —
            // it is guarded because "in practice" is what a content push changes.
            int amount = deficit > int.MaxValue - room ? int.MaxValue : deficit + room;

            long price = table.PriceFor(taken);
            if (price <= 0L) return ContinueOffer.None;

            var choice = GemPrice.ChoiceFor(gemsHeld, price, gemsForSale);
            if (choice == GemChoice.Unavailable) return ContinueOffer.None;

            return ContinueOffer.Make(unit, amount, price, taken, choice);
        }

        /// <summary>
        /// Takes the gems for a continue the player has agreed to.
        ///
        /// <para>
        /// Re-checks affordability through the ledger rather than trusting the offer it was
        /// handed, and that is reachable rather than defensive: the balance moves while the
        /// panel is open — a sync landing, another device spending, a purchase arriving — and
        /// the whole point of routing through <see cref="PlayerProgression.TrySpend"/> is that
        /// the decision is taken against the balance at the instant of the debit.
        /// </para>
        /// <para>
        /// Returns false without charging anything when it cannot be afforded, so a caller has
        /// exactly one thing to test before handing over an allowance.
        /// </para>
        /// </summary>
        public static bool TryBuy(ContinueOffer offer, LevelId level)
        {
            if (!offer.Exists) return false;

            if (!PlayerProgression.TrySpend(Currency.Gems, offer.Gems,
                                            SpendReason + level.Value))
                return false;

            LevelAnalytics.TrackContinueBought(level, offer);

            // The debit is owed to the server. Requesting rather than syncing outright is the
            // debounce doing its job, exactly as a gem-priced good does it — and a run is a
            // place where several of these can land inside a minute.
            CloudSaveService.RequestSync();

            return true;
        }
    }
}
