using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Buying the hearts a lost run needs to be tried again: the price, the withdrawal switch,
    /// and the three states a player can be in when they are shown it.
    ///
    /// <para>
    /// <b>What makes this worth its own suite rather than a case on the continue's.</b> The two
    /// offers sit on the same screen a minute apart and are easy to reason about as one thing,
    /// and they are not: a continue sells the run where it stands and can therefore only ever
    /// pay one star, while this sells a heart, so the board is rebuilt and the attempt is
    /// graded like any other. Every case below is about a way that distinction, or the money
    /// behind it, could quietly stop holding.
    /// </para>
    /// <para>
    /// Everything here runs offline. <see cref="HeartRescue.Offer"/> is pure — the table, what
    /// the player holds and whether there is a shop are all passed in — precisely because it is
    /// the function that decides whether somebody is asked for money.
    /// </para>
    /// </summary>
    public sealed class HeartRescueTests
    {
        [SetUp]
        public void Open()
        {
            SaveService.Unload();
            SaveService.LoadWith(new MemoryStore());
        }

        [TearDown]
        public void Restore()
        {
            SaveService.Unload();
            ProgressionRules.Reset();
        }

        static HeartRuleTable Read(HeartsDto dto, List<string> problems = null)
            => HeartRuleTable.Resolve(dto, problems ?? new List<string>());

        /// <summary>The shipped numbers: 20 gems for 2 hearts.</summary>
        static HeartRuleTable Shipped() => Read(null);

        // ================================================================ the content block
        [Test]
        public void AnAbsentBlockKeepsTheBuiltInPrice()
        {
            var table = Read(null);

            Assert.AreEqual(HeartLimits.DefaultRescueGems, table.RescueGems);
            Assert.AreEqual(HeartLimits.DefaultRescueHearts, table.RescueHearts);
        }

        /// <summary>
        /// The reason both fields carry a negative sentinel rather than defaulting to zero.
        ///
        /// <c>JsonUtility</c> instantiates a <c>[Serializable]</c> class field even when the
        /// JSON has no such key, so a file written before this pair existed arrives as an
        /// object with two zeroes in it. Read literally that says "hand over no hearts", which
        /// would withdraw the feature on every client that had not yet taken a content push —
        /// silently, with nothing anywhere saying so. Same trap as <c>ContinueDto.enabled</c>,
        /// and the same fix.
        /// </summary>
        [Test]
        public void AnOlderFileWithNoRescueFieldsInheritsRatherThanWithdrawing()
        {
            var table = Read(new HeartsDto { refillCap = 5, ceiling = 50, defeatCost = 1 });

            Assert.AreEqual(HeartLimits.DefaultRescueGems, table.RescueGems);
            Assert.AreEqual(HeartLimits.DefaultRescueHearts, table.RescueHearts);
        }

        /// <summary>Nought hearts is the switch, and it is the only way to say "no offer".</summary>
        [Test]
        public void NoughtHeartsWithdrawsTheOfferWithoutComplaint()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { rescueHearts = 0 }, problems);

            Assert.AreEqual(0, table.RescueHearts);
            Assert.IsEmpty(problems);
            Assert.IsFalse(HeartRescue.Offer(table, 0, 10_000L, true).Exists);
        }

        /// <summary>
        /// A price of nought is refused rather than obeyed, which is the mirror of the rule
        /// above and not a duplicate of it.
        ///
        /// A free heart is not a cheap rescue: it is the one gate in this game that can stop
        /// somebody playing, no longer gating. There is a field next door that withdraws the
        /// offer properly, so a zero here can only ever be a mistake.
        /// </summary>
        [Test]
        public void AFreeRescueIsRefusedAndNamed()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { rescueGems = 0L }, problems);

            Assert.AreEqual(HeartLimits.DefaultRescueGems, table.RescueGems);
            Assert.IsNotEmpty(problems);
            StringAssert.Contains("rescueGems", problems[0]);
        }

        [Test]
        public void APriceAboveTheCeilingIsClampedAndNamed()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { rescueGems = HeartLimits.MaxRescueGems + 1L }, problems);

            Assert.AreEqual(HeartLimits.MaxRescueGems, table.RescueGems);
            Assert.IsNotEmpty(problems);
        }

        /// <summary>
        /// A purchase the ceiling could never accept is a button that takes gems and hands back
        /// nothing anybody can see. Reachable from an honest push, because lowering the ceiling
        /// is documented as safe and this is the one number that has to come down with it.
        /// </summary>
        [Test]
        public void MoreHeartsThanTheCeilingHoldsIsHeldAtTheCeiling()
        {
            var problems = new List<string>();
            var table = Read(new HeartsDto { refillCap = 3, ceiling = 8, rescueHearts = 40 },
                             problems);

            Assert.AreEqual(8, table.RescueHearts);
            Assert.IsNotEmpty(problems);
        }

        // ==================================================================== the three states
        [Test]
        public void GemsInHandIsOneTapFromPlaying()
        {
            var offer = HeartRescue.Offer(Shipped(), 0, 20L, gemsForSale: true);

            Assert.IsTrue(offer.Exists);
            Assert.IsTrue(offer.Affordable);
            Assert.AreEqual(GemChoice.Spend, offer.Choice);
            Assert.AreEqual(HeartLimits.DefaultRescueHearts, offer.Hearts);
            Assert.AreEqual(HeartLimits.DefaultRescueGems, offer.Gems);
        }

        [Test]
        public void ShortOfGemsIsAnOfferToBuySome()
        {
            var offer = HeartRescue.Offer(Shipped(), 0, 19L, gemsForSale: true);

            Assert.IsTrue(offer.Exists);
            Assert.IsFalse(offer.Affordable);
            Assert.AreEqual(GemChoice.BuyGems, offer.Choice);
        }

        /// <summary>
        /// Short of gems with nowhere to buy them is <em>no offer at all</em>, not a greyed
        /// button.
        ///
        /// This project's rule is that a control which can never work is worse than no control,
        /// and it bites hardest here: the panel is already telling somebody they cannot play,
        /// and a dead button on it is the game appearing to offer a way out that does nothing.
        /// </summary>
        [Test]
        public void ShortOfGemsWithNoShopIsWithdrawnRatherThanDrawnDead()
        {
            var offer = HeartRescue.Offer(Shipped(), 0, 19L, gemsForSale: false);

            Assert.IsFalse(offer.Exists);
            Assert.AreEqual(GemChoice.Unavailable, offer.Choice);
        }

        /// <summary>
        /// Holding the gems means the offer stands whether or not a store is reachable — the
        /// purchase is a gem debit, which works on a plane.
        /// </summary>
        [Test]
        public void HoldingTheGemsNeedsNoStoreAtAll()
        {
            var offer = HeartRescue.Offer(Shipped(), 0, 20L, gemsForSale: false);

            Assert.IsTrue(offer.Exists);
            Assert.AreEqual(GemChoice.Spend, offer.Choice);
        }

        /// <summary>
        /// The shop's refusal, at the one moment it can actually be met.
        ///
        /// Taking gems for hearts that evaporate on arrival is the thing a player notices
        /// exactly once. Unreachable at a defeat that emptied the bar, and reachable the
        /// moment a content push lowers the ceiling under somebody holding a surplus from
        /// chests — which is a push documented as safe, so nothing warns about it.
        /// </summary>
        [Test]
        public void HeartsThatWouldOverflowTheCeilingAreNotSold()
        {
            var table = Read(new HeartsDto { refillCap = 5, ceiling = 6, rescueHearts = 2 });

            Assert.IsTrue(HeartRescue.Offer(table, 4, 10_000L, true).Exists);
            Assert.IsFalse(HeartRescue.Offer(table, 5, 10_000L, true).Exists);
        }

        // ==================================================================== the two prices
        /// <summary>
        /// A rescue and a continue are priced from different blocks, and this is the case that
        /// notices if one of them is ever quietly read for the other.
        ///
        /// They ship at the same twenty on purpose — the two offers are met on one screen a
        /// minute apart, and a player who declined one price and is then shown a different one
        /// for the other reads the pair as haggling — but they are separate fields, so a retune
        /// of either must leave the other exactly where it was.
        /// </summary>
        [Test]
        public void RetuningTheRescueLeavesTheContinueAlone()
        {
            var hearts = Read(new HeartsDto { rescueGems = 75L, rescueHearts = 5 });
            var carryOn = ContinueTable.Resolve(null, new List<string>());

            Assert.AreEqual(75L, hearts.RescueGems);
            Assert.AreEqual(5, hearts.RescueHearts);
            Assert.AreEqual(ContinueLimits.DefaultGems, carryOn.Gems);
            Assert.AreEqual(ContinueLimits.DefaultTurns, carryOn.Turns);
        }

        /// <summary>
        /// The debit is written down under its own reason, so a support case reading a ledger
        /// can tell a bought heart from a bought continue. Free text, and permanent in the
        /// sense that anything reading it back is reading history.
        /// </summary>
        [Test]
        public void TheTwoDebitsAreToldApartInTheLedger()
            => Assert.AreNotEqual(RunContinue.SpendReason, HeartRescue.SpendReason);

        // ==================================================================== the purchase
        //
        // Everything above decides whether somebody is *asked* for money. Everything below is
        // the line that actually takes it, and it runs against a save that never reaches a
        // disk — SaveService.LoadWith takes an ISaveStore, which is the seam
        // GroveStockPurchaseTests established for exactly this. A fresh account is seeded
        // Currency.SeedGems, which is deliberately less than the shipped price, so every
        // fixture below says out loud what it is holding.

        static readonly LevelId Glade = LevelId.Parse("c02_the_millers_knot");

        /// <summary>What the player is holding right now.</summary>
        static long Gems => PlayerProgression.Gems;
        static int Hearts => Wallet.Hearts.Count;

        /// <summary>
        /// Puts a known number of gems in the wallet.
        ///
        /// <c>GrantLocally</c> is the account seed's door and nothing else's (invariant 10a) —
        /// which is exactly why it is right here and wrong everywhere else: a test needs an
        /// opening balance, not an award, and reaching for the claim path would be pretending
        /// this money came from somewhere it did not. <c>EconomyAndMergeTests</c> establishes
        /// its balances the same way.
        /// </summary>
        static void Hold(long gems)
        {
            long already = PlayerProgression.Gems;
            if (gems > already) Wallet.Ledger(Currency.Gems).GrantLocally(gems - already);

            Assert.AreEqual(gems, PlayerProgression.Gems, "the fixture did not take");
        }

        /// <summary>Every debit this account has booked against gems, in order.</summary>
        static List<SpendEntryDto> Spends()
        {
            var dto = new SaveFileDto();
            Wallet.WriteInto(dto);

            var rows = new List<SpendEntryDto>();
            foreach (var ledger in dto.wallet?.currencies ?? Array.Empty<CurrencyLedgerDto>())
            {
                if (!string.Equals(ledger.currency, Currency.Gems, StringComparison.Ordinal)) continue;
                foreach (var spend in ledger.pendingSpends ?? Array.Empty<SpendEntryDto>()) rows.Add(spend);
            }

            return rows;
        }

        [Test]
        public void AFreshAccountCannotAffordTheShippedPriceAndTheFixturesSayWhatTheyHold()
        {
            // Not a tautology: every case below is priced against this, so a retune of the seed
            // or the price that quietly made the offer free would otherwise show up as a
            // handful of mysterious passes rather than one honest failure here.
            Assert.Less(Currency.SeedGems, HeartLimits.DefaultRescueGems,
                        "if the seed covered the price, no test below would be testing a refusal");
        }

        [Test]
        public void BuyingChargesThePriceOnceAndGrantsTheHearts()
        {
            Hold(100L);
            int before = Hearts;

            var offer = HeartRescue.Offer(Shipped(), before, Gems, gemsForSale: true);
            Assert.IsTrue(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Defeat));

            Assert.AreEqual(100L - HeartLimits.DefaultRescueGems, Gems);
            Assert.AreEqual(before + HeartLimits.DefaultRescueHearts, Hearts);
            Assert.AreEqual(1, Spends().Count, "one purchase is one debit");
        }

        /// <summary>
        /// The refusal that matters: nothing is charged and nothing is granted.
        ///
        /// <para>
        /// A half-applied purchase is the worst outcome available here, and it has two shapes.
        /// Granting without charging hands out free hearts to anybody whose balance moved while
        /// the panel was open. Charging without granting takes gems for nothing. Both are
        /// asserted, because a version of this that returned false after doing one of them
        /// would still pass a test that only looked at the return value.
        /// </para>
        /// </summary>
        [Test]
        public void ARefusedPurchaseChargesNothingAndGrantsNothing()
        {
            Hold(HeartLimits.DefaultRescueGems);
            int before = Hearts;

            // The offer is taken while the price is affordable, and the balance then moves out
            // from under it — a sync landing, another device spending. This is the ordinary
            // case rather than a contrived one, which is why TryBuy re-decides at the charge.
            var offer = HeartRescue.Offer(Shipped(), before, Gems, gemsForSale: true);
            Assert.IsTrue(offer.Affordable, "the fixture must start affordable");

            Assert.IsTrue(PlayerProgression.TrySpend(Currency.Gems, Gems, "test:drain"));
            Assert.AreEqual(0L, Gems);

            Assert.IsFalse(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Defeat), "an unaffordable debit is refused");
            Assert.AreEqual(0L, Gems, "a refused purchase must not go further into debt");
            Assert.AreEqual(before, Hearts, "a refused purchase must not grant hearts");
            Assert.AreEqual(1, Spends().Count, "only the drain, never the rescue");
        }

        /// <summary>
        /// An offer that does not exist is refused before the ledger is touched, so a panel
        /// that somehow held a stale <c>None</c> cannot charge for it.
        /// </summary>
        [Test]
        public void AnOfferThatDoesNotExistIsNeverCharged()
        {
            Hold(100L);
            int before = Hearts;

            Assert.IsFalse(HeartRescue.TryBuy(HeartRescueOffer.None, Glade, HeartRescueWhere.Defeat));
            Assert.AreEqual(100L, Gems);
            Assert.AreEqual(before, Hearts);
            CollectionAssert.IsEmpty(Spends());
        }

        /// <summary>
        /// Two taps are two purchases, and that is correct rather than a gap.
        ///
        /// <para>
        /// The debit carries a <em>generated</em> id, not one derived from the offer, because a
        /// rescue is a spend rather than an award (invariant 10a draws that line): a player who
        /// loses twice genuinely buys twice, and an id derived from the level would make the
        /// second purchase collapse into the first and hand over free hearts. What stops a
        /// double <em>tap</em> becoming two purchases is the latch in <c>HeartRescueFlow</c>
        /// and the panel closing behind it — a UI concern, deliberately not this one.
        /// </para>
        /// </summary>
        [Test]
        public void TwoPurchasesAreTwoDistinctDebitsRatherThanOneCollapsed()
        {
            Hold(100L);

            var offer = HeartRescue.Offer(Shipped(), Hearts, Gems, gemsForSale: true);
            Assert.IsTrue(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Defeat));
            Assert.IsTrue(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Defeat));

            var spends = Spends();
            Assert.AreEqual(2, spends.Count);
            Assert.AreNotEqual(spends[0].id, spends[1].id, "two debits must not share an id");
            Assert.AreEqual(100L - HeartLimits.DefaultRescueGems * 2L, Gems);
        }

        /// <summary>The debit names the level, so a support case can find the run it paid for.</summary>
        [Test]
        public void TheDebitNamesWhatItWasSpentOn()
        {
            Hold(100L);

            var offer = HeartRescue.Offer(Shipped(), Hearts, Gems, gemsForSale: true);
            Assert.IsTrue(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Defeat));

            StringAssert.StartsWith(HeartRescue.SpendReason, Spends()[0].reason);
            StringAssert.Contains(Glade.Value, Spends()[0].reason);
        }

        // ==================================================================== the redraw rule
        /// <summary>
        /// Short of gems, then holding them: the one change the panel exists to notice.
        /// </summary>
        [Test]
        public void GemsArrivingThatCoverThePriceIsWorthARedraw()
        {
            var table = Shipped();
            var shown = HeartRescue.Offer(table, 0, 0L, gemsForSale: true);
            var now = HeartRescue.Offer(table, 0, 100L, gemsForSale: true);

            Assert.AreEqual(GemChoice.BuyGems, shown.Choice);
            Assert.IsTrue(HeartRescue.WorthRedrawing(shown, now));
        }

        /// <summary>
        /// Gems arriving that still do not cover it change nothing the player can act on. A
        /// panel that tore itself down every time a sync landed would look broken.
        /// </summary>
        [Test]
        public void GemsArrivingThatStillDoNotCoverThePriceAreNotWorthARedraw()
        {
            var table = Shipped();
            var shown = HeartRescue.Offer(table, 0, 1L, gemsForSale: true);
            var now = HeartRescue.Offer(table, 0, 5L, gemsForSale: true);

            Assert.IsFalse(HeartRescue.WorthRedrawing(shown, now));
        }

        [Test]
        public void AnUnchangedOfferIsNotWorthARedraw()
        {
            var offer = HeartRescue.Offer(Shipped(), 0, 100L, gemsForSale: true);
            Assert.IsFalse(HeartRescue.WorthRedrawing(offer, offer));
        }

        /// <summary>
        /// An offer that has stopped existing leaves the panel exactly as it is.
        ///
        /// The store going down while somebody is reading the panel would otherwise redraw it
        /// without its button, and a control vanishing from under a thumb is worse than one
        /// that turns out to be refused — the purchase re-decides at the charge anyway.
        /// </summary>
        [Test]
        public void AnOfferThatHasGoneAwayLeavesThePanelStanding()
        {
            var table = Shipped();
            var shown = HeartRescue.Offer(table, 0, 1L, gemsForSale: true);
            var now = HeartRescue.Offer(table, 0, 1L, gemsForSale: false);

            Assert.IsFalse(now.Exists);
            Assert.IsFalse(HeartRescue.WorthRedrawing(shown, now));
        }

        /// <summary>
        /// An offer that appears where there was none is drawn. Reachable when the store
        /// connects a moment after a defeat, which is exactly when a player is looking.
        /// </summary>
        [Test]
        public void AnOfferThatBecomesPossibleIsDrawn()
        {
            var table = Shipped();
            var now = HeartRescue.Offer(table, 0, 100L, gemsForSale: true);

            Assert.IsTrue(HeartRescue.WorthRedrawing(HeartRescueOffer.None, now));
        }

        // ------------------------------------------------------------------ plumbing
        /// <summary>A save that never reaches a disk. <c>GroveStockPurchaseTests</c>' store.</summary>
        sealed class MemoryStore : ISaveStore
        {
            SaveFileDto _file;

            public SaveFileDto Load() => _file ?? new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new LevelRecordDto[0],
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),

                // Otherwise the load reaches LegacyPlayerPrefsImport, which is PlayerPrefs,
                // which is the Editor. There is no legacy build to import from in a test.
                legacyImportDone = true,
            };

            public bool Save(SaveFileDto dto)
            {
                _file = dto;
                return true;
            }

            public void Delete() => _file = null;
        }

        // ============================================ the second panel it is offered on
        //
        // A refused restart raises the same offer over a board that is still standing
        // (RestartGateOverlay). The price, the amount and the debit are identical there — the
        // panel is what differs, and HeartRescueWhere is the only thing that knows. What is
        // worth pinning is the pair of heart counts that panel is reached at, because the
        // ceiling clause below is written for a bar the defeat emptied and this one is not
        // always empty.

        [Test]
        public void TheOfferStandsAtBothHeartCountsARefusedRestartProduces()
        {
            var table = Shipped();

            // Nought is the empty bar. One is the count that produces the other refusal — a
            // charged restart pays for the run being left and then needs a heart for the one
            // that follows, so a player holding exactly one is stopped with a heart in hand.
            // The offer has to exist for both or the panel is a countdown and a way out.
            Assert.IsTrue(HeartRescue.Offer(table, 0, 10_000L, true).Exists, "an empty bar");
            Assert.IsTrue(HeartRescue.Offer(table, 1, 10_000L, true).Exists, "one heart in hand");
        }

        [Test]
        public void TheTwoPanelsSellExactlyTheSameThing()
        {
            // The price is not a function of where it was met, and that is deliberate: the two
            // are reachable on one screen a minute apart, and a player quoted one price and then
            // another reads the pair as haggling (invariant 23a). HeartRescueWhere labels the
            // event and never reaches Offer, which is what makes this true by construction — the
            // case is here so that giving it a price would fail rather than merely read oddly.
            var table = Shipped();

            var defeat = HeartRescue.Offer(table, 0, 10_000L, true);
            var restart = HeartRescue.Offer(table, 0, 10_000L, true);

            Assert.AreEqual(defeat.Gems, restart.Gems);
            Assert.AreEqual(defeat.Hearts, restart.Hearts);
            Assert.AreEqual(defeat.Choice, restart.Choice);
        }

        [Test]
        public void ARescueTakenOverARunIsTheSameDebitAsOneTakenOnADefeat()
        {
            // Same ledger, same reason string, same grant — only the funnel label differs. A
            // second call site for a proven path is the whole claim this feature rests on: no
            // schema version, no merge rule, no server work.
            Hold(100L);
            int hearts = Hearts;

            var offer = HeartRescue.Offer(Shipped(), hearts, Gems, gemsForSale: true);
            Assert.IsTrue(HeartRescue.TryBuy(offer, Glade, HeartRescueWhere.Restart));

            Assert.AreEqual(100L - HeartLimits.DefaultRescueGems, Gems);
            Assert.AreEqual(hearts + HeartLimits.DefaultRescueHearts, Hearts);
            Assert.AreEqual(1, Spends().Count, "one purchase is one debit, wherever it was made");
        }

    }
}
