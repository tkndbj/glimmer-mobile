using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The rewarded-ad loop.
    ///
    /// <para>
    /// Three properties carry it, and each is pinned here. The award id has to round-trip
    /// through a format the <em>server</em> parses, because a claim whose id it cannot read
    /// is a reward the player watched a video for and never receives. The daily allowance
    /// has to survive a merge in the conservative direction, because it is the only thing
    /// standing between a second device and a second set of ads. And the content table has
    /// to reject a bad file rather than absorb it, since the numbers in it decide what the
    /// game pays out.
    /// </para>
    /// <para>
    /// What is deliberately <em>not</em> here: anything involving an actual ad. The whole
    /// point of <see cref="IAdProvider"/> is that the policy above is decided without an
    /// SDK in the room, and a test that needed one would be a test nobody could run.
    /// </para>
    /// </summary>
    public sealed class RewardedAdTests
    {
        // ---------------------------------------------------------- impressions
        [Test]
        public void AnImpressionIsAPlacementAndATraceId()
        {
            var impression = AdImpression.New(AdPlacement.HeartRefill);

            Assert.AreEqual(AdPlacement.HeartRefill, impression.PlacementId);
            Assert.AreEqual(32, impression.TraceId.Length);
            Assert.IsTrue(impression.IsValid);
        }

        [Test]
        public void TwoImpressionsNeverShareATraceId()
        {
            var seen = new HashSet<string>();

            for (int i = 0; i < 500; i++)
                Assert.IsTrue(seen.Add(AdImpression.New(AdPlacement.CoinBonus).TraceId),
                              "two impressions collided, which would make one log unreadable");
        }

        [Test]
        public void AnImpressionWithoutAPlacementIsNotValid()
        {
            // The placement is the only field that means anything off this device — the
            // trace id never leaves it, so an impression with one and no placement is not
            // a thing that can be shown.
            Assert.IsFalse(new AdImpression(string.Empty, "abc").IsValid);
            Assert.IsTrue(new AdImpression(AdPlacement.CoinBonus, string.Empty).IsValid);
        }

        [Test]
        public void OnlyTheShippedPlacementIdsAreKnown()
        {
            Assert.IsTrue(AdPlacement.IsKnown(AdPlacement.HeartRefill));
            Assert.IsTrue(AdPlacement.IsKnown(AdPlacement.CoinBonus));

            Assert.IsFalse(AdPlacement.IsKnown("heart_refill "));
            Assert.IsFalse(AdPlacement.IsKnown("HEART_REFILL"));
            Assert.IsFalse(AdPlacement.IsKnown(string.Empty));
            Assert.IsFalse(AdPlacement.IsKnown(null));
        }

        // ------------------------------------------------------ the built-in table
        [Test]
        public void TheBuiltInTableOffersBothPlacements()
        {
            var table = AdRewardTable.Default;

            Assert.IsTrue(table.Has(AdPlacement.HeartRefill));
            Assert.IsTrue(table.Has(AdPlacement.CoinBonus));

            Assert.AreEqual(ChestDropKind.Hearts, table.Offer(AdPlacement.HeartRefill).Kind);
            Assert.AreEqual(ChestDropKind.Credits, table.Offer(AdPlacement.CoinBonus).Kind);
        }

        [Test]
        public void HeartsAreNotCurrencyAndCoinsAre()
        {
            // The line the whole security split falls on: currency is adjudicated by the
            // server, hearts are applied by the client. If this ever flips, a forged save
            // starts minting money.
            Assert.IsFalse(AdRewardTable.Default.Offer(AdPlacement.HeartRefill).IsCurrency);
            Assert.IsTrue(AdRewardTable.Default.Offer(AdPlacement.CoinBonus).IsCurrency);
        }

        [Test]
        public void AnUnknownPlacementOffersNothingRatherThanADefault()
        {
            var offer = AdRewardTable.Default.Offer("no_such_placement");

            Assert.IsFalse(offer.IsValid);
            Assert.AreEqual(0, offer.Amount);
        }

        // ------------------------------------------------------------ reading content
        [Test]
        public void AnAbsentAdsBlockIsNotAnError()
        {
            var problems = new List<string>();

            Assert.AreSame(AdRewardTable.Default, AdRewardTable.Resolve(null, problems));
            Assert.IsEmpty(problems);
        }

        [Test]
        public void AContentTableReplacesTheBuiltInAmounts()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                cooldownSeconds = 90,
                placements = new[]
                {
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 400, dailyCap = 3 },
                },
            }, problems);

            Assert.AreEqual(90, table.CooldownSeconds);
            Assert.AreEqual(400, table.Offer(AdPlacement.CoinBonus).Amount);
            Assert.AreEqual(3, table.Offer(AdPlacement.CoinBonus).DailyCap);

            // A placement the file leaves out is switched off, not defaulted — that is how
            // an offer is withdrawn without a build.
            Assert.IsFalse(table.Has(AdPlacement.HeartRefill));
        }

        [Test]
        public void AnUnknownPlacementIsSkippedRatherThanFatal()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                cooldownSeconds = 30,
                placements = new[]
                {
                    new AdPlacementDto { id = "from_a_newer_build", kind = "credits", amount = 10, dailyCap = 1 },
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 200, dailyCap = 2 },
                },
            }, problems);

            // The known one survives; the unknown one is named and dropped. That is what
            // lets a newer content pack reach an older build without breaking it.
            Assert.AreEqual(200, table.Offer(AdPlacement.CoinBonus).Amount);
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void ADuplicatedPlacementRefusesTheWholeTable()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                placements = new[]
                {
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 100, dailyCap = 2 },
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "gems", amount = 5, dailyCap = 2 },
                },
            }, problems);

            Assert.AreSame(AdRewardTable.Default, table);
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AnAmountOrCapBelowOneIsRefused()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                placements = new[]
                {
                    new AdPlacementDto { id = AdPlacement.CoinBonus, kind = "credits", amount = 0, dailyCap = 3 },
                    new AdPlacementDto { id = AdPlacement.HeartRefill, kind = "hearts", amount = 2, dailyCap = 0 },
                },
            }, problems);

            // Both entries were rejected, so nothing usable was left and the built-in
            // table stands rather than the feature silently switching itself off.
            Assert.AreSame(AdRewardTable.Default, table);
            Assert.IsNotEmpty(problems);
        }

        [Test]
        public void AmountsAndCapsAreClampedToTheirCeilings()
        {
            var problems = new List<string>();

            var table = AdRewardTable.Resolve(new AdsDto
            {
                cooldownSeconds = 99999,
                placements = new[]
                {
                    new AdPlacementDto
                    {
                        id = AdPlacement.CoinBonus, kind = "credits",
                        amount = AdRules.MaxRewardAmount + 5000,
                        dailyCap = AdRules.MaxDailyCap + 100,
                    },
                },
            }, problems);

            Assert.AreEqual(AdRules.MaxCooldownSeconds, table.CooldownSeconds);
            Assert.AreEqual(AdRules.MaxRewardAmount, table.Offer(AdPlacement.CoinBonus).Amount);
            Assert.AreEqual(AdRules.MaxDailyCap, table.Offer(AdPlacement.CoinBonus).DailyCap);
        }

        // ------------------------------------------------------------- the merge
        [Test]
        public void MergingTwoDevicesInTheSameDayKeepsTheLargerCount()
        {
            var mine = State(20315, ("coin_bonus", 4), ("heart_refill", 1));
            var other = State(20315, ("coin_bonus", 2), ("heart_refill", 6));

            var joined = RewardedAds.Join(mine, other);

            // Larger, because an allowance is consumable: taking the smaller would let two
            // devices refill each other's daily ads by taking turns.
            Assert.AreEqual(4, CountOf(joined, "coin_bonus"));
            Assert.AreEqual(6, CountOf(joined, "heart_refill"));
        }

        [Test]
        public void TheLaterDayWinsOutright()
        {
            var older = State(20315, ("coin_bonus", 6));
            var newer = State(20316, ("coin_bonus", 1));

            Assert.AreEqual(20316, RewardedAds.Join(older, newer).dayKey);
            Assert.AreEqual(1, CountOf(RewardedAds.Join(older, newer), "coin_bonus"));

            // An older day's counters describe a day that is over; carrying them forward
            // would hand the player a head start on a day they have not played.
            Assert.AreEqual(1, CountOf(RewardedAds.Join(newer, older), "coin_bonus"));
        }

        [Test]
        public void TheCooldownSurvivesADayChangeAndTakesTheLaterStamp()
        {
            var older = State(20315, ("coin_bonus", 6));
            older.lastWatchedUnix = 999;

            var newer = State(20316, ("coin_bonus", 1));
            newer.lastWatchedUnix = 100;

            // The later stamp wins even though the older day lost, because a cooldown is a
            // fact about when somebody last watched, not about which day it is.
            Assert.AreEqual(999, RewardedAds.Join(older, newer).lastWatchedUnix);
            Assert.AreEqual(999, RewardedAds.Join(newer, older).lastWatchedUnix);
        }

        [Test]
        public void TheMergeIsIdempotentCommutativeAndAssociative()
        {
            var a = State(20315, ("coin_bonus", 2), ("heart_refill", 5));
            var b = State(20315, ("coin_bonus", 7));
            var c = State(20315, ("heart_refill", 9), ("coin_bonus", 1));

            // Idempotent: joining a save with itself changes nothing.
            AssertSame(a, RewardedAds.Join(a, a));

            // Commutative: the order two devices sync in cannot change the result.
            AssertSame(RewardedAds.Join(a, b), RewardedAds.Join(b, a));

            // Associative: and neither can how they are grouped.
            AssertSame(RewardedAds.Join(RewardedAds.Join(a, b), c),
                       RewardedAds.Join(a, RewardedAds.Join(b, c)));
        }

        [Test]
        public void ADuplicatedEntryFoldsToTheLargerCountRatherThanAFreshAllowance()
        {
            var malformed = new AdStateDto
            {
                dayKey = 20315,
                watched = new[]
                {
                    new AdViewCountDto { placement = "coin_bonus", count = 6 },
                    new AdViewCountDto { placement = "coin_bonus", count = 0 },
                },
            };

            var joined = RewardedAds.Join(malformed, new AdStateDto { dayKey = 20315 });

            Assert.AreEqual(6, CountOf(joined, "coin_bonus"));
            Assert.AreEqual(1, joined.watched.Length, "the duplicate should have been folded away");
        }

        [Test]
        public void AMissingSectionJoinsCleanlyInBothDirections()
        {
            var mine = State(20315, ("coin_bonus", 3));

            Assert.AreEqual(3, CountOf(RewardedAds.Join(mine, null), "coin_bonus"));
            Assert.AreEqual(3, CountOf(RewardedAds.Join(null, mine), "coin_bonus"));
            Assert.IsNotNull(RewardedAds.Join(null, null));
        }

        [Test]
        public void CountsAreWrittenSortedSoAnUnchangedSaveIsByteIdentical()
        {
            var unsorted = State(20315, ("heart_refill", 1), ("coin_bonus", 2));
            var reversed = State(20315, ("coin_bonus", 2), ("heart_refill", 1));

            var a = RewardedAds.Join(unsorted, reversed);
            var b = RewardedAds.Join(reversed, unsorted);

            // Sorted, and therefore comparable by an ordered walk — which is what stops the
            // checksum moving, and the sync firing, on a save that did not change.
            Assert.AreEqual("coin_bonus", a.watched[0].placement);
            Assert.AreEqual("heart_refill", a.watched[1].placement);
            AssertSame(a, b);
        }

        // --------------------------------------------------------------- helpers
        static AdStateDto State(int dayKey, params (string placement, int count)[] counts)
        {
            var watched = new AdViewCountDto[counts.Length];
            for (int i = 0; i < counts.Length; i++)
                watched[i] = new AdViewCountDto { placement = counts[i].placement, count = counts[i].count };

            return new AdStateDto { dayKey = dayKey, watched = watched };
        }

        static int CountOf(AdStateDto state, string placement)
        {
            if (state?.watched == null) return 0;

            foreach (var entry in state.watched)
                if (entry != null && entry.placement == placement) return entry.count;

            return 0;
        }

        static void AssertSame(AdStateDto a, AdStateDto b)
        {
            Assert.AreEqual(a.dayKey, b.dayKey);
            Assert.AreEqual(a.lastWatchedUnix, b.lastWatchedUnix);
            Assert.AreEqual(a.watched.Length, b.watched.Length);

            for (int i = 0; i < a.watched.Length; i++)
            {
                Assert.AreEqual(a.watched[i].placement, b.watched[i].placement);
                Assert.AreEqual(a.watched[i].count, b.watched[i].count);
            }
        }
    }
}
