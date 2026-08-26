using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// One offer to buy the hearts a lost run needs to be tried again: what it costs, what it
    /// hands over, and what the player is in a position to do about it.
    ///
    /// <para>
    /// Shaped exactly like <see cref="ContinueOffer"/> and deliberately not the same type. The
    /// two offers sit on different panels, are decided by different facts and buy different
    /// things — one carries <em>this</em> run on where it stood, the other pays for a fresh
    /// one — and folding them together would mean a field that is meaningless on one branch,
    /// which is how a panel comes to print a number nobody authored.
    /// </para>
    /// </summary>
    public readonly struct HeartRescueOffer
    {
        /// <summary>Hearts handed over. The number the button prints.</summary>
        public readonly int Hearts;

        /// <summary>What it costs, in gems.</summary>
        public readonly long Gems;

        /// <summary>What the player can do about it. See <see cref="GemChoice"/>.</summary>
        public readonly GemChoice Choice;

        HeartRescueOffer(int hearts, long gems, GemChoice choice)
        {
            Hearts = hearts;
            Gems = gems;
            Choice = choice;
        }

        /// <summary>No offer at all: the defeat panel says what it has always said.</summary>
        public static readonly HeartRescueOffer None =
            new HeartRescueOffer(0, 0L, GemChoice.Unavailable);

        /// <summary>True when there is something worth putting in front of the player.</summary>
        public bool Exists => Choice != GemChoice.Unavailable && Hearts > 0 && Gems > 0L;

        /// <summary>True when the gems are already in hand.</summary>
        public bool Affordable => Choice == GemChoice.Spend;

        internal static HeartRescueOffer Make(int hearts, long gems, GemChoice choice)
            => new HeartRescueOffer(hearts, gems, choice);

        public override string ToString()
            => Exists ? $"+{Hearts} hearts for {Gems} gems ({Choice})" : "none";
    }

    /// <summary>
    /// Whether a player who has just run out of hearts can buy their way back to the board,
    /// at what price, and the debit that does it.
    ///
    /// <para>
    /// <b>It is not a continue, and the difference is the whole design.</b> A continue
    /// (<c>RunContinue</c>) sells the <em>run</em> — the board stays exactly as it stood, with
    /// its counter already past the two-star line, so a bought run can only ever score one
    /// star. This sells a <em>heart</em>, which is the gate rather than the run: the board is
    /// rebuilt from nothing, the attempt is a fresh one, and it is graded like any other. That
    /// is why the two cannot share a price, an amount or a panel, and why this one is offered
    /// only where the continue has already been declined or was never made.
    /// </para>
    /// <para>
    /// <b>Why nothing about it reaches the server or the save file.</b> The gems leave through
    /// <see cref="PlayerProgression.TrySpend"/>, which carries an idempotency key, lands
    /// locally so it works on a plane, and is refused by <c>submitSpends</c> on the next sync
    /// if the server-derived balance could not cover it — the same two lines that buy a
    /// companion or a continue. What they buy is a heart, which is <em>already</em> a
    /// produced/spent ledger merged by <c>max</c> (invariant 11b) and is exactly what the shop
    /// has always sold for gems (invariant 18). So this is a second call site for two proven
    /// paths rather than a new one: <b>no schema version, no merge rule, no server work.</b>
    /// </para>
    /// <para>
    /// <b>Why it cannot inflate a reward.</b> Hearts pay nothing. Stars come from turns against
    /// par (invariant 22), credits and XP are derived from the star ledger (invariant 9), and a
    /// bought heart buys an attempt at the same board under the same rules a free one would
    /// have bought. The only thing gems purchase here is <em>sooner</em>.
    /// </para>
    /// <para>
    /// The price is content (<c>hearts.rescueGems</c>, <c>hearts.rescueHearts</c>) for the
    /// continue's reason and the heart gate's: it is charged to real players at the worst
    /// moment in a session, it is the number most certain to be wrong on the first guess, and
    /// finding out must not cost a store review. <c>rescueHearts: 0</c> withdraws it.
    /// </para>
    /// </summary>
    public static class HeartRescue
    {
        /// <summary>What the debit is written down as, for a support case reading a ledger.</summary>
        public const string SpendReason = "hearts_rescue:";

        /// <summary>
        /// Builds the offer for a player who has just lost a run and has nothing left to spend.
        ///
        /// <para>
        /// Pure: the table and every varying fact are passed in, so every branch is proved
        /// offline against plain integers. That is deliberate — this is the function that
        /// decides whether somebody is asked for money, and the facts that decide it are
        /// exactly the ones that differ between a test and a phone.
        /// </para>
        /// </summary>
        /// <param name="table">The published heart rules. Never null in a running game.</param>
        /// <param name="heartsHeld">What the player holds now, after the loss was charged.</param>
        /// <param name="gemsHeld">The player's gem balance.</param>
        /// <param name="gemsForSale">
        /// Whether a shop is reachable that could sell them some. False in a build with no
        /// store SDK and while the store has not connected — and a "get gems" button in either
        /// of those leads nowhere, which is worse than no button at all.
        /// </param>
        public static HeartRescueOffer Offer(HeartRuleTable table, int heartsHeld,
                                             long gemsHeld, bool gemsForSale)
        {
            if (table == null) return HeartRescueOffer.None;

            int hearts = table.RescueHearts;
            long price = table.RescueGems;

            // Nought hearts is how the block withdraws the offer, and it is the one place a
            // zero here is a decision rather than a mistake — an offer that hands over nothing
            // is not a cheap offer, it is no offer. A price of nought is refused by the reader
            // instead (see HeartRuleTable), because a free heart is a gate that no longer gates.
            if (hearts <= 0 || price <= 0L) return HeartRescueOffer.None;

            // The same refusal a paid heart pack takes in the shop, and for the same reason
            // (GoodOfferState.HeartsNearlyFull): taking gems for hearts that evaporate on
            // arrival is the kind of thing a player notices exactly once. Unreachable at a
            // defeat that emptied the bar, and reachable the moment a content push lowers the
            // ceiling under somebody holding a surplus from chests.
            if (heartsHeld < 0) heartsHeld = 0;
            if (heartsHeld + hearts > table.Ceiling) return HeartRescueOffer.None;

            var choice = GemPrice.ChoiceFor(gemsHeld, price, gemsForSale);
            if (choice == GemChoice.Unavailable) return HeartRescueOffer.None;

            return HeartRescueOffer.Make(hearts, price, choice);
        }

        /// <summary>The live reading, for a caller with no reason to be holding a table.</summary>
        public static HeartRescueOffer Offer(int heartsHeld, long gemsHeld, bool gemsForSale)
            => Offer(HeartRules.Table, heartsHeld, gemsHeld, gemsForSale);

        /// <summary>
        /// Whether a panel showing <paramref name="shown"/> is worth tearing down and drawing
        /// again now that the offer reads <paramref name="now"/>.
        ///
        /// <para>
        /// <b>Here rather than in the panel, because it is the rule and not the plumbing.</b>
        /// A balance moves for reasons that have nothing to do with the player — a background
        /// sync, another device, a server grant landing — and a panel that rebuilt on every one
        /// of them would flicker at somebody reading it. So the question is not "did the number
        /// change" but "did what they can <em>do</em> change", which is a comparison of two
        /// offers and is exactly the kind of branch this project keeps out of a
        /// <c>MonoBehaviour</c> (<c>HintPrompt</c>, <c>AccountGate</c>, <c>GroveUnveil</c>).
        /// </para>
        /// <para>
        /// <b>An offer that has stopped existing is left standing rather than rebuilt away.</b>
        /// That is the clause worth stating: the store going down while a panel is open would
        /// otherwise redraw the panel without its button, and a control disappearing from under
        /// a thumb is worse than one that turns out to be refused. There is always another way
        /// out of the panel, and the purchase re-decides against the ledger at the instant of
        /// the charge anyway.
        /// </para>
        /// </summary>
        public static bool WorthRedrawing(HeartRescueOffer shown, HeartRescueOffer now)
        {
            if (!now.Exists) return false;
            return now.Choice != shown.Choice;
        }

        /// <summary>
        /// Takes the gems and grants the hearts.
        ///
        /// <para>
        /// The debit goes first, and the order is the shop's: if the process dies between the
        /// two the player has lost gems and not received hearts, which is a window of one disk
        /// write. The other order would hand out hearts for nothing whenever a debit was
        /// refused, which is not a window but a rule.
        /// </para>
        /// <para>
        /// Affordability is re-decided through the ledger rather than trusted from the offer
        /// this was handed, and that is reachable rather than defensive: the balance moves
        /// while a panel is open — a sync landing, another device spending — and routing
        /// through <see cref="PlayerProgression.TrySpend"/> is what makes the decision happen
        /// at the instant of the charge, which is the only instant that means anything.
        /// </para>
        /// <para>
        /// Returns false without charging anything when it cannot be met, so a caller has
        /// exactly one thing to test before letting somebody back onto the board.
        /// </para>
        /// </summary>
        public static bool TryBuy(HeartRescueOffer offer, LevelId level)
        {
            if (!offer.Exists) return false;

            if (!PlayerProgression.TrySpend(Currency.Gems, offer.Gems,
                                            SpendReason + level.Value))
                return false;

            Wallet.GrantHearts(offer.Hearts);

            LevelAnalytics.TrackHeartRescueBought(level, offer);

            // The debit is owed to the server. Requesting rather than syncing outright is the
            // debounce doing its job, exactly as a gem-priced good does it.
            CloudSaveService.RequestSync();

            return true;
        }
    }
}
