using System;
using GlimmerGrove.Daily;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The arithmetic behind a reward flying into the hub's pills.
    ///
    /// <para>
    /// The flight itself is images on an arc and can only be judged by watching it. What
    /// cannot be judged by watching it is whether the number underneath is honest, and that
    /// is what this pins - because every way it goes wrong is invisible in a screenshot and
    /// visible to a player as the game having eaten something. A pill that stops one short
    /// of the balance reads as a reward that was not fully paid; a pill that walks backwards
    /// mid-cascade reads as one being taken away again; a prize that throws seven tokens for
    /// three hearts reads as the count in the air disagreeing with the count on the pill.
    /// </para>
    /// <para>
    /// The currency cases are the ones worth having. A chest's credits are in the ledger
    /// before the animation starts, so <c>live</c> never moves and the old chest code could
    /// safely capture it once. An ad's are granted by the server (invariant 10d), so
    /// <c>live</c> is read at every landing and may rise part way through - which is the one
    /// arrangement nobody would think to try by hand.
    /// </para>
    /// </summary>
    public sealed class RewardFlightTests
    {
        static ChestDrop Drop(ChestDropKind kind, int amount) => new ChestDrop(kind, amount);

        // ------------------------------------------------------------- token counts
        [Test]
        public void AGenerousPrizeThrowsTheBudgetAndNoMore()
        {
            Assert.AreEqual(RewardFlight.TokensPerDrop,
                            RewardFlight.TokenCount(Drop(ChestDropKind.Credits, 1_000)));
            Assert.AreEqual(RewardFlight.TokensPerDrop,
                            RewardFlight.TokenCount(Drop(ChestDropKind.Credits,
                                                         RewardFlight.TokensPerDrop)));
        }

        /// <summary>
        /// Three hearts throw three hearts. Throwing seven and landing them in fractions is
        /// the one case where the count in the air and the count on the pill visibly
        /// disagree, which is exactly the thing the flight exists to make legible.
        /// </summary>
        [Test]
        public void APrizeSmallerThanTheBudgetThrowsItself()
        {
            Assert.AreEqual(3, RewardFlight.TokenCount(Drop(ChestDropKind.Hearts, 3)));
            Assert.AreEqual(1, RewardFlight.TokenCount(Drop(ChestDropKind.Hearts, 1)));
        }

        /// <summary>
        /// A boost is one thing however many hours it runs for, so it is one token - not
        /// twelve, which is what its amount would otherwise buy it.
        /// </summary>
        [Test]
        public void AHeartBoostIsOneTokenWhateverItsDuration()
        {
            Assert.AreEqual(1, RewardFlight.TokenCount(Drop(ChestDropKind.HeartBoost, 12)));
        }

        [Test]
        public void NothingIsThrownForAPrizeThatIsNotOne()
        {
            Assert.AreEqual(0, RewardFlight.TokenCount(ChestDrop.None));
            Assert.AreEqual(0, RewardFlight.TokenCount(Drop(ChestDropKind.Credits, 0)));
        }

        // ------------------------------------------------------------- the readout
        // What a pill reads is the live balance less what is still in the air, owed per reward
        // (ReadoutLedger) so several can land in one pill at once - the page no longer waits for
        // one cascade to finish before it takes the tap that starts the next. Each reward owes
        // the amount it banked; the server's half is a ceiling owed only as it arrives.

        const double Before = 10.0, After = 1_000.0;

        static ReadoutLedger.Entry Played(long sequence, long banked, int landed, int tokens)
        {
            var entry = new ReadoutLedger.Entry(sequence, 0L);
            entry.Bank(banked, 0L);
            entry.Play(tokens, After);
            entry.Land(landed);
            return entry;
        }

        static ReadoutLedger.Entry Held(long sequence, long banked, long balance, Func<bool> holder = null)
        {
            var entry = new ReadoutLedger.Entry(sequence, balance);
            entry.Bank(banked, balance);
            entry.Holder = holder;
            return entry;
        }

        static ReadoutLedger Of(params ReadoutLedger.Entry[] entries)
        {
            var ledger = new ReadoutLedger();
            foreach (var entry in entries) ledger.Add(entry);
            return ledger;
        }

        /// <summary>
        /// The last token writes the balance itself rather than an interpolation. Without
        /// this a 1,000-credit prize thrown as seven lands on 999 for ever - one short, on
        /// the number a player is most likely to be watching.
        /// </summary>
        [Test]
        public void TheLastTokenWritesTheBalanceRatherThanTheInterpolation()
        {
            const int Tokens = 7;
            Assert.AreEqual(1_000L, Of(Played(1, 1_000L, Tokens, Tokens)).Shown(1_000L, Before));

            // And the step before it is genuinely short of the total, or the last landing
            // would have nothing left to say.
            Assert.Less(Of(Played(1, 1_000L, Tokens - 1, Tokens)).Shown(1_000L, Before), 1_000L);
        }

        [Test]
        public void TheReadoutClimbsWithTheLandings()
        {
            var entry = Played(1, 700L, 0, 7);
            var ledger = Of(entry);
            long previous = long.MinValue;

            for (int landed = 0; landed <= 7; landed++)
            {
                entry.Land(landed);
                long shown = ledger.Shown(800L, Before);
                Assert.GreaterOrEqual(shown, previous, "landing " + landed + " went backwards");
                Assert.GreaterOrEqual(shown, 100L);
                Assert.LessOrEqual(shown, 800L);
                previous = shown;
            }

            Assert.AreEqual(800L, previous);
        }

        /// <summary>A held panel owes its whole prize: the pill stands where it was before the grant.</summary>
        [Test]
        public void AHeldRewardKeepsItsWholePrizeOffThePill()
        {
            Assert.AreEqual(4_000L, Of(Held(1, 1_000L, 5_000L)).Shown(5_000L, Before));
        }

        /// <summary>
        /// A gift panel holds its pills from the moment it opens and grants only on the key. Until
        /// then it has banked nothing, so it keeps nothing off the pill - but it is still held,
        /// because the grant may come.
        /// </summary>
        [Test]
        public void AHoldWithNothingBankedYetOwesNothingButIsStillHeld()
        {
            var ledger = Of(Held(1, 0L, 5_000L));
            Assert.AreEqual(5_000L, ledger.Shown(5_000L, Before));
            Assert.IsTrue(ledger.Busy(Before));
        }

        /// <summary>
        /// A held entry is owed only while its panel stands, asked on every read - so a panel
        /// destroyed without any callback running (edit mode sends no OnDestroy) still lets the
        /// pill go, and the entry is dropped.
        /// </summary>
        [Test]
        public void AHoldEndsWithItsPanelWhetherOrNotAnythingSaysSo()
        {
            bool standing = true;
            var ledger = Of(Held(1, 1_000L, 5_000L, () => standing));
            Assert.AreEqual(4_000L, ledger.Shown(5_000L, Before));

            standing = false;
            Assert.AreEqual(5_000L, ledger.Shown(5_000L, Before));
            Assert.IsFalse(ledger.Busy(Before));

            ledger.Prune(5_000L, Before);
            Assert.AreEqual(0, ledger.Count);
        }

        /// <summary>
        /// The deadline is in the entry, not only in a timer: once it passes, a cascade whose
        /// tokens all died owes nothing, and any repaint at all writes the balance.
        /// </summary>
        [Test]
        public void TheDeadlineEndsAnEntryWhateverBecameOfItsTokens()
        {
            var ledger = Of(Played(1, 700L, 0, 7));
            Assert.AreEqual(100L, ledger.Shown(800L, Before));
            Assert.IsTrue(ledger.Busy(Before));

            Assert.AreEqual(800L, ledger.Shown(800L, After));
            Assert.IsFalse(ledger.Busy(After));

            ledger.Prune(800L, After);
            Assert.AreEqual(0, ledger.Count);
        }

        [Test]
        public void APillWithNothingInTheAirReadsTheBalance()
        {
            Assert.AreEqual(730L, new ReadoutLedger().Shown(730L, Before));

            var closed = Played(1, 500L, 0, 7);
            closed.Close();
            Assert.AreEqual(730L, Of(closed).Shown(730L, Before));
        }

        /// <summary>
        /// Two chests opened back to back: the first is still landing when the second is
        /// granted. Each owes only what it banked, so the pill climbs by the first's tokens and
        /// then the second's, is never shown the sum early, never walked backwards, and ends
        /// exactly on the balance.
        /// </summary>
        [Test]
        public void TwoRewardsLandingInOnePillEachOweOnlyTheirOwn()
        {
            // 100 before the first chest (+50), 150 before the second (+30), 180 now.
            var first = Played(1, 50L, 3, 7);
            var second = Held(2, 30L, 180L);    // its panel is up, nothing thrown yet
            var ledger = Of(first, second);

            Assert.AreEqual(180L - (50L - 50L * 3 / 7) - 30L, ledger.Shown(180L, Before),
                            "the first walks; the second is all owed");

            first.Land(7);
            Assert.AreEqual(150L, ledger.Shown(180L, Before), "the first is paid in full, the second still held");

            second.Play(7, After);
            second.Land(7);
            Assert.AreEqual(180L, ledger.Shown(180L, Before));
        }

        /// <summary>
        /// The newer reward can finish first - a small prize thrown after a large one. Amounts
        /// add, so the order they finish in changes nothing about what either owes.
        /// </summary>
        [Test]
        public void TheNewerRewardCanFinishFirst()
        {
            var first = Played(1, 50L, 0, 7);
            var second = Played(2, 30L, 7, 7);
            var ledger = Of(first, second);

            Assert.AreEqual(180L - 50L, ledger.Shown(180L, Before));

            second.Close();
            ledger.Prune(180L, Before);
            Assert.AreEqual(180L - 50L, ledger.Shown(180L, Before), "closing changes nothing it had not already paid");

            first.Land(7);
            Assert.AreEqual(180L, ledger.Shown(180L, Before));
        }

        /// <summary>
        /// Money that is no reward's - a purchase landing, a heart refilling - shows at once, while
        /// the cascade under way goes on owing exactly its own. The snapshot ledger handed it to
        /// whichever reward was newest, which walked a purchase in with a chest's tokens.
        /// </summary>
        [Test]
        public void MoneyThatIsNoRewardsShowsAtOnce()
        {
            var ledger = Of(Played(1, 50L, 0, 7));
            Assert.AreEqual(100L, ledger.Shown(150L, Before));
            Assert.AreEqual(600L, ledger.Shown(650L, Before), "a 500 purchase shows in full, the chest still owes 50");
        }

        /// <summary>
        /// The case the snapshot ledger got wrong: a receipt's flight is made after its grant (it
        /// is raised by it), so its snapshot had to be derived, and a chest opened a moment
        /// earlier sorted ahead of it with a segment that ended below where it began - owing
        /// nothing, while the receipt owed the chest's coins as well as its own. With amounts each
        /// owes its own, whatever order they were opened in.
        /// </summary>
        [Test]
        public void AReceiptOpenedAfterAChestOwesOnlyThePurchase()
        {
            // 100, then a 500 purchase (600), then a chest +50 (650), then the receipt's flight.
            var chest = Held(1, 50L, 650L);
            var receipt = Held(2, 500L, 650L);
            var ledger = Of(chest, receipt);

            Assert.AreEqual(100L, ledger.Shown(650L, Before));

            receipt.Play(7, After);
            receipt.Land(7);
            Assert.AreEqual(600L, ledger.Shown(650L, Before), "the receipt paid its own 500 and none of the chest's 50");
        }

        /// <summary>A spend landing mid-cascade shows at once, and the cascade still owes what it owed.</summary>
        [Test]
        public void ASpendMidCascadeShowsAtOnce()
        {
            var ledger = Of(Played(1, 70L, 0, 7));
            Assert.AreEqual(500L, ledger.Shown(570L, Before));
            Assert.AreEqual(480L, ledger.Shown(550L, Before));
        }

        /// <summary>A balance spent below what is still owed reads nought, never a negative figure.</summary>
        [Test]
        public void TheReadoutNeverGoesBelowNought()
        {
            Assert.AreEqual(0L, Of(Played(1, 900L, 0, 7)).Shown(500L, Before));
        }

        [Test]
        public void TheArithmeticIsExactForAnyBalanceAPlayerCanHold()
        {
            const long Big = 9_000_000_000_000L;
            var entry = Played(1, Big, 0, 7);
            var ledger = Of(entry);

            long previous = -1L;
            for (int landed = 0; landed <= 7; landed++)
            {
                entry.Land(landed);
                long shown = ledger.Shown(Big, Before);
                Assert.Greater(shown, previous);
                previous = shown;
            }

            Assert.AreEqual(Big, previous);
        }

        // ------------------------------------------------------------- the server's half
        // An ad's coins are paid by the server a few seconds after the video (invariant 10d), so
        // its entry carries a ceiling and owes only what has arrived of it.

        static ReadoutLedger.Entry Awaiting(long sequence, long ceiling, long balance)
        {
            var entry = new ReadoutLedger.Entry(sequence, balance);
            entry.Bank(0L, balance);
            entry.Await(ceiling);
            return entry;
        }

        /// <summary>
        /// The server never answering is an ordinary outcome, not an error state: the tokens
        /// still arrive and the pill still flashes, and the figure simply has not moved. What
        /// it must never do is drift off the balance in either direction.
        /// </summary>
        [Test]
        public void CoinsTheServerHasNotPaidLeaveThePillWhereItWas()
        {
            var entry = Awaiting(1, 1_000L, 4_000L);
            var ledger = Of(entry);
            Assert.AreEqual(4_000L, ledger.Shown(4_000L, Before), "held, nothing arrived: no dip");

            entry.Play(7, After);
            for (int landed = 0; landed <= 7; landed++)
            {
                entry.Land(landed);
                Assert.AreEqual(4_000L, ledger.Shown(4_000L, Before));
            }
        }

        /// <summary>
        /// The coins arriving while the panel stands are kept off the pill until they fly - the
        /// up, down and up of 2026-09-28 was this money showing behind the scrim and being rewound.
        /// </summary>
        [Test]
        public void CoinsArrivingBehindThePanelWaitForTheirTokens()
        {
            var ledger = Of(Awaiting(1, 1_000L, 4_000L));
            Assert.AreEqual(4_000L, ledger.Shown(5_000L, Before));
        }

        /// <summary>
        /// The sync can land part way through the cascade. The reading must not fall back to where
        /// it started when that happens: it walks from wherever it had got to.
        /// </summary>
        [Test]
        public void ABalanceThatArrivesMidCascadeIsPickedUpWithoutGoingBackwards()
        {
            const int Tokens = 7;
            var entry = Awaiting(1, 1_000L, 4_000L);
            entry.Play(Tokens, After);
            var ledger = Of(entry);

            long previous = 4_000L;
            for (int landed = 1; landed <= Tokens; landed++)
            {
                entry.Land(landed);

                // Nothing from the server for the first three landings, then the grant lands.
                long live = landed < 4 ? 4_000L : 5_000L;

                long shown = ledger.Shown(live, Before);
                Assert.GreaterOrEqual(shown, previous, "landing " + landed + " went backwards");
                previous = shown;
            }

            Assert.AreEqual(5_000L, previous);
        }

        /// <summary>The ceiling is a ceiling: money beyond it is someone else's and shows at once.</summary>
        [Test]
        public void NoMoreThanTheCeilingIsEverHeldBack()
        {
            var ledger = Of(Awaiting(1, 100L, 4_000L));
            Assert.AreEqual(4_400L, ledger.Shown(4_500L, Before));
        }

        /// <summary>
        /// The bonus wheel states a larger prize after the video's flight stated the bare offer. The
        /// larger ceiling stands; the two do not add.
        /// </summary>
        [Test]
        public void TheLargestCeilingStatedStands()
        {
            var entry = Awaiting(1, 100L, 4_000L);
            entry.Await(300L);
            entry.Await(200L);
            Assert.AreEqual(300L, entry.Awaited);
        }

        /// <summary>
        /// A chest opened while an ad's coins are still owed banks its own coins on the same pill.
        /// Those are explained by the chest and must not be taken for the server's payment - and
        /// stay explained once the chest has finished and been dropped from the ledger.
        /// </summary>
        [Test]
        public void ANewerRewardsCoinsAreNeverMistakenForTheServers()
        {
            var ad = Awaiting(1, 100L, 4_000L);
            var chest = Played(2, 50L, 0, 7);
            var ledger = Of(ad, chest);

            Assert.AreEqual(4_000L, ledger.Shown(4_050L, Before), "the chest's 50 is the chest's");

            chest.Land(7);
            chest.Close();
            ledger.Prune(4_050L, Before);
            Assert.AreEqual(1, ledger.Count);
            Assert.AreEqual(4_050L, ledger.Shown(4_050L, Before), "and still the chest's once it has gone");

            Assert.AreEqual(4_050L, ledger.Shown(4_150L, Before), "the server's 100 arrives and waits for its tokens");
        }

        /// <summary>Entries are kept oldest first whatever order they are entered in, and entering one twice is a no-op.</summary>
        [Test]
        public void EnteringIsOrderedAndIdempotent()
        {
            var older = Held(1, 10L, 100L);
            var newer = Held(2, 20L, 100L);
            var ledger = Of(newer, older, newer);

            Assert.AreEqual(2, ledger.Count);
            Assert.AreEqual(70L, ledger.Shown(100L, Before));
        }

        // ------------------------------------------------------------- the readouts
        /// <summary>
        /// Through the real readouts: an entry entered is paying until it is closed, and once
        /// everything is closed the readout is free and reads the balance. Closing is idempotent.
        /// </summary>
        [Test]
        public void ClosingEveryEntryFreesTheReadout()
        {
            const ResourceSlots.Kind Kind = ResourceSlots.Kind.Gems;
            try
            {
                long balance = ResourceSlots.Balance(Kind);
                var older = new ReadoutLedger.Entry(10, balance);
                var newer = new ReadoutLedger.Entry(20, balance);

                ResourceSlots.Enter(Kind, newer);
                ResourceSlots.Enter(Kind, older);
                Assert.IsTrue(ResourceSlots.IsPaying(Kind));

                newer.Close();
                ResourceSlots.Settle(Kind);
                Assert.IsTrue(ResourceSlots.IsPaying(Kind), "the older one is still held");

                older.Close();
                older.Close();
                ResourceSlots.Settle(Kind);
                Assert.IsFalse(ResourceSlots.IsPaying(Kind));
                Assert.AreEqual(ResourceSlots.Balance(Kind), ResourceSlots.Walked(Kind));
            }
            finally
            {
                ResourceSlots.ResetPayouts();
            }
        }

        /// <summary>
        /// Two flights at once through the real readouts: the first to finish must not hand the
        /// pill back while the second still holds it - the fault a single flag per readout had,
        /// and the reason a page could not take a second chest while the first was landing.
        /// </summary>
        [Test]
        public void TheFirstFlightToFinishDoesNotReleaseTheSecondsHold()
        {
            var a = new UnityEngine.GameObject("first");
            var b = new UnityEngine.GameObject("second");
            try
            {
                var first = RewardFlight.Begin();
                first.Hold(a.transform);
                var second = RewardFlight.Begin();
                second.Hold(b.transform);

                first.Play(null, null);
                for (int k = 0; k < 3; k++)
                    Assert.IsTrue(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "still held by the second: " + (ResourceSlots.Kind)k);

                second.Play(null, null);
                for (int k = 0; k < 3; k++)
                    Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "released: " + (ResourceSlots.Kind)k);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
                ResourceSlots.ResetPayouts();
            }
        }

        // ------------------------------------------------------------- the hold
        // A panel keeps its reward off the pills from the moment it opens, so its coins cannot
        // count up twice. What must never happen is a pill left held, so every ending lets go.

        [Test]
        public void AHeldPanelHoldsEveryPillUntilItPays()
        {
            var owner = new UnityEngine.GameObject("prize");
            try
            {
                var flight = RewardFlight.Begin();
                flight.Hold(owner.transform);

                for (int k = 0; k < 3; k++)
                    Assert.IsTrue(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "held: " + (ResourceSlots.Kind)k);

                bool told = false;
                flight.Play(null, () => told = true);

                Assert.IsTrue(told, "the caller is let go");
                for (int k = 0; k < 3; k++)
                    Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k), "released: " + (ResourceSlots.Kind)k);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
                ResourceSlots.ResetPayouts();
            }
        }

        [Test]
        public void AFlightThatIsNeverHeldEntersNothingUntilItPlays()
        {
            RewardFlight.Begin();
            for (int k = 0; k < 3; k++)
                Assert.IsFalse(ResourceSlots.IsPaying((ResourceSlots.Kind)k));
        }
    }
}
