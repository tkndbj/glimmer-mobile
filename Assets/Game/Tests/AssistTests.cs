using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The helping hand (invariant 66): what counts as a genuine loss, when the gift is owed, when a
    /// level is eased, how an eased run is graded, and that the shipped block is one the game reads.
    /// Every rule is a pure function of a log, a table and a day, so none of this needs the Editor.
    /// </summary>
    public sealed class AssistTests
    {
        const int Today = 20750;
        const long Now = Today * 86400L + 3600L;

        // ------------------------------------------------------------ the shipped block
        [Test]
        public void TheShippedBlockReadsAndHandsOverUtilitiesTheCatalogHolds()
        {
            var problems = new List<string>();
            var table = AssistTable.Resolve(ShippedAssist(), ShippedUtilities(), problems);

            Assert.IsEmpty(problems, string.Join("; ", problems));
            Assert.IsTrue(table.IsOn, "progression.json carries no assist block, or it was refused");
            Assert.IsTrue(table.Gives && table.Eases);

            // The owner's brief, 2026-10-25: a gift after the first loss in chapter one and the second
            // anywhere later, once a day; a hidden 7% after two, on at most two levels a day.
            Assert.AreEqual(1, table.OpeningLosses);
            Assert.AreEqual(2, table.GiftLosses);
            Assert.AreEqual(1, table.GiftsPerDay);
            Assert.AreEqual(2, table.EaseLosses);
            Assert.AreEqual(7, table.EasePercent);
            Assert.AreEqual(2, table.EasesPerDay);

            var ids = new List<string>();
            foreach (var item in table.Gift) ids.Add(item.Id);
            CollectionAssert.AreEquivalent(new[] { "firepot", "stormcall" }, ids);
        }

        [Test]
        public void AFaultyBlockSwitchesTheFeatureOffAndSaysWhy()
        {
            void Refused(string expect, AssistDto dto)
            {
                var problems = new List<string>();
                var table = AssistTable.Resolve(dto, ShippedUtilities(), problems);
                Assert.IsFalse(table.IsOn, expect);
                Assert.IsTrue(problems.Exists(p => p.Contains(expect)), $"expected '{expect}' in: {string.Join("; ", problems)}");
            }

            Refused("easePercent", Block(ease: 0));
            Refused("easePercent", Block(ease: AssistTable.MaxEasePercent + 1));
            Refused("giftLosses", Block(giftLosses: 0));
            Refused("does not hold", Block(gift: new[] { Item("nonesuch", 1) }));
            Refused("second time", Block(gift: new[] { Item("firepot", 1), Item("firepot", 1) }));
            Refused("1 to", Block(gift: new[] { Item("firepot", 0) }));
            Refused("has to hold something", Block(gift: new AssistItemDto[0]));

            var problems2 = new List<string>();
            Assert.IsFalse(AssistTable.Resolve(null, ShippedUtilities(), problems2).IsOn, "absent is off");
            Assert.IsEmpty(problems2, "absent is not a fault");
        }

        // ------------------------------------------------------------ a genuine loss
        [Test]
        public void OnlyALossThatFoughtCounts()
        {
            var table = Table();

            Assert.IsTrue(table.IsGenuine(felled: 6, raiders: 22, matches: 5), "a quarter felled, five matches");
            Assert.IsFalse(table.IsGenuine(felled: 5, raiders: 22, matches: 40), "under a quarter felled");
            Assert.IsFalse(table.IsGenuine(felled: 22, raiders: 22, matches: 4), "too few matches");
            Assert.IsFalse(table.IsGenuine(felled: 0, raiders: 22, matches: 0), "a board left to fall on its own");
            Assert.IsFalse(table.IsGenuine(felled: 3, raiders: 0, matches: 10), "a level that sends nothing");
        }

        // ------------------------------------------------------------ the gift
        [Test]
        public void ChapterOneOwesTheGiftOnTheFirstLossAndLaterChaptersOnTheSecond()
        {
            var table = Table();

            var opening = new AssistHistory(null);
            Assert.IsTrue(opening.NoteLoss("s01_thornhollow", true, table, Today, Now));
            Assert.AreEqual("s01_thornhollow", opening.Owed);

            var later = new AssistHistory(null);
            Assert.IsFalse(later.NoteLoss("s05_ashpit", false, table, Today, Now), "one loss is not enough past chapter one");
            Assert.IsTrue(later.NoteLoss("s05_ashpit", false, table, Today, Now));
        }

        [Test]
        public void TheGiftIsOwedOnceADayAndNeverTwiceAtOnce()
        {
            var table = Table();
            var history = new AssistHistory(null);

            Assert.IsTrue(history.NoteLoss("s01_thornhollow", true, table, Today, Now));
            Assert.IsFalse(history.NoteLoss("s01_bramblerun", true, table, Today, Now), "a gift is still owed");

            Assert.AreEqual("s01_thornhollow", history.TakeGift());
            Assert.IsFalse(history.GiftOwed);
            Assert.IsFalse(history.NoteLoss("s01_bramblerun", true, table, Today, Now), "today has had its gift");

            Assert.IsTrue(history.NoteLoss("s01_bramblerun", true, table, Today + 1, Now + 86400L), "a new day, a new gift");
            history.TakeGift();
            Assert.IsFalse(history.NoteLoss("s01_bramblerun", true, table, Today + 1, Now + 86400L),
                           "and only one on that day either - the count restarts at the day, not above yesterday's");
        }

        // ------------------------------------------------------------ the easing
        [Test]
        public void ALevelIsEasedAfterTwoLossesAndStaysEasedUntilCleared()
        {
            var table = Table();
            var history = new AssistHistory(null);

            history.NoteLoss("s05_ashpit", false, table, Today, Now);
            Assert.AreEqual(0, history.EaseFor("s05_ashpit", table, Today, out _), "one loss eases nothing");

            history.NoteLoss("s05_ashpit", false, table, Today, Now);
            Assert.AreEqual(7, history.EaseFor("s05_ashpit", table, Today, out bool fresh));
            Assert.IsTrue(fresh);

            Assert.AreEqual(7, history.EaseFor("s05_ashpit", table, Today + 3, out fresh), "carried over the days");
            Assert.IsFalse(fresh, "a level already eased spends nothing");

            history.NoteWin("s05_ashpit");
            Assert.AreEqual(0, history.EaseFor("s05_ashpit", table, Today + 3, out _), "a clear forgets the easing");
            Assert.AreEqual(0, history.LossesOn("s05_ashpit"), "and the losses");
        }

        [Test]
        public void TwoLevelsADayAreNewlyEasedAndAnEarlierDaysEasingIsNotCounted()
        {
            var table = Table();
            var history = new AssistHistory(null);

            foreach (var level in new[] { "a1", "a2", "a3", "a4" })
            {
                history.NoteLoss(level, false, table, Today, Now);
                history.NoteLoss(level, false, table, Today, Now);
            }

            Assert.AreEqual(7, history.EaseFor("a1", table, Today, out _), "the first today");
            Assert.AreEqual(7, history.EaseFor("a2", table, Today, out _), "the second today");
            Assert.AreEqual(0, history.EaseFor("a3", table, Today, out _), "today has eased two");

            // Tomorrow: a1 and a2 stay eased without being counted, so two more may be.
            Assert.AreEqual(7, history.EaseFor("a1", table, Today + 1, out _), "yesterday's stays eased");
            Assert.AreEqual(7, history.EaseFor("a3", table, Today + 1, out _), "the first tomorrow");
            Assert.AreEqual(7, history.EaseFor("a4", table, Today + 1, out _), "the second tomorrow");
        }

        [Test]
        public void AnEasedRunIsGradedAtFullStrength()
        {
            Assert.AreEqual(28, AssistTable.FullStrength(28, 0), "nothing eased, nothing scaled");
            Assert.AreEqual(31, AssistTable.FullStrength(28, 7), "28 / 0.93 = 30.1, rounded toward the stricter grade");
            Assert.AreEqual(100, AssistTable.FullStrength(93, 7), "exact");
            Assert.AreEqual(93, AssistTable.KeptPercent(7));
            Assert.AreEqual(100, AssistTable.KeptPercent(0));
        }

        [Test]
        public void TheLogForgetsTheOldestUneasedLevelFirst()
        {
            var table = Table();
            var history = new AssistHistory(null);

            history.NoteLoss("eased", false, table, Today, 1L);
            history.NoteLoss("eased", false, table, Today, 1L);
            Assert.AreEqual(7, history.EaseFor("eased", table, Today, out _));

            for (int i = 0; i < AssistHistory.MaxLevels; i++)
                history.NoteLoss("level" + i, false, table, Today, 10L + i);

            Assert.IsTrue(history.IsEased("eased"), "an eased level outlives the plain ones");
            Assert.AreEqual(AssistHistory.MaxLevels, history.Log.levels.Count);
        }

        // ------------------------------------------------------------ helpers
        static AssistTable Table()
        {
            var problems = new List<string>();
            var table = AssistTable.Resolve(Block(), ShippedUtilities(), problems);
            Assert.IsTrue(table.IsOn, string.Join("; ", problems));
            return table;
        }

        static AssistDto Block(int giftLosses = 2, int ease = 7, AssistItemDto[] gift = null)
            => new AssistDto
            {
                openingLosses = 1,
                giftLosses = giftLosses,
                giftsPerDay = 1,
                gift = gift ?? new[] { Item("firepot", 1), Item("stormcall", 1) },
                easeLosses = 2,
                easePercent = ease,
                easesPerDay = 2,
                minFelledPercent = 25,
                minMatches = 5,
            };

        static AssistItemDto Item(string id, int count) => new AssistItemDto { id = id, count = count };

        /// <summary>The shipped <c>assist</c> block, read with the test parser (JsonUtility is native).</summary>
        static AssistDto ShippedAssist()
        {
            var map = ShippedProgression();
            Assert.IsTrue(map.ContainsKey("assist"), "progression.json carries no assist block");
            var block = TestJson.Object(map["assist"]);

            var gift = new List<AssistItemDto>();
            foreach (var child in TestJson.Children(block, "gift"))
            {
                var row = TestJson.Object(child);
                gift.Add(Item(TestJson.Str(row, "id"), TestJson.Int(row, "count")));
            }

            return new AssistDto
            {
                openingLosses = TestJson.Int(block, "openingLosses"),
                giftLosses = TestJson.Int(block, "giftLosses"),
                giftsPerDay = TestJson.Int(block, "giftsPerDay"),
                gift = gift.ToArray(),
                easeLosses = TestJson.Int(block, "easeLosses"),
                easePercent = TestJson.Int(block, "easePercent"),
                easesPerDay = TestJson.Int(block, "easesPerDay"),
                minFelledPercent = TestJson.Int(block, "minFelledPercent"),
                minMatches = TestJson.Int(block, "minMatches"),
            };
        }

        /// <summary>The shipped utility catalog, read with the test parser, so a gift row is checked against what ships.</summary>
        static UtilityCatalog ShippedUtilities()
        {
            var items = new List<UtilityDto>();
            foreach (var child in TestJson.Children(TestJson.Object(ShippedProgression()["utilities"]), "items"))
            {
                var row = TestJson.Object(child);
                items.Add(new UtilityDto
                {
                    id = TestJson.Str(row, "id"),
                    kind = TestJson.Str(row, "kind"),
                    magnitude = TestJson.Int(row, "magnitude"),
                    gemPrice = TestJson.Int(row, "gemPrice"),
                    maxHeld = TestJson.Int(row, "maxHeld"),
                    order = TestJson.Int(row, "order"),
                    minLevel = TestJson.Int(row, "minLevel"),
                    cooldownSeconds = TestJson.Int(row, "cooldownSeconds"),
                });
            }

            var problems = new List<string>();
            var catalog = UtilityCatalog.Resolve(new UtilitiesDto { items = items.ToArray() }, problems);
            Assert.IsEmpty(problems, string.Join("; ", problems));
            return catalog;
        }

        static Dictionary<string, object> ShippedProgression()
            => TestJson.Object(TestJson.Parse(File.ReadAllText(
                Path.Combine(TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "progression.json"))));
    }
}
