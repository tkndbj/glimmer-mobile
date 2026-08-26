using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which glades cost a heart.
    ///
    /// <para>
    /// The heart gate is the only rule in this game that can stop somebody playing, so the
    /// window that suspends it is worth proving rather than eyeballing — in both directions.
    /// Too narrow and a beginner is charged for our teaching; too wide and the gate the whole
    /// free-play economy is paced by quietly stops existing for a chapter at a time.
    /// </para>
    /// <para>
    /// Everything here runs against a driven catalog and a published rules table, with no
    /// Editor and no content files, which is the point: whoever retunes <c>graceLevels</c> can
    /// prove what they did on their own machine.
    /// </para>
    /// </summary>
    public sealed class HeartStakeTests
    {
        [TearDown]
        public void Restore() => ProgressionRules.Reset();

        /// <summary>Publishes a rules table whose only interesting field is the free window.</summary>
        static void Grace(int levels)
        {
            var dto = new ProgressionDto
            {
                schemaVersion = ProgressionSchema.Version,
                xpToNext = new[] { 100 },
                tailXpToNext = 100,
                tailXpIncrement = 10,
                hearts = new HeartsDto { graceLevels = levels },
            };

            Assert.IsTrue(ProgressionTable.TryBuild(dto, out var table, new List<string>()));
            ProgressionRules.Publish(table);
        }

        /// <summary>
        /// Two chapters of the ordinary mode and two of a second one, which is the shape the
        /// whole rule turns on: a mode's own first chapter, not the catalog's.
        /// </summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "g1", "g2", "g3", "g4", "g5" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "c02_two", order = 20, version = 1,
                levels = new[] { "g6", "g7", "g8" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "w01_weave", order = 30, version = 1, mode = "weave",
                levels = new[] { "w1", "w2", "w3", "w4" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "w02_loom", order = 40, version = 1, mode = "weave",
                levels = new[] { "w5", "w6" },
            }, 1);
            return builder.Build();
        }

        static bool Free(CatalogIndex index, string level)
            => HeartStake.IsFree(index, LevelId.Parse(level));

        // ------------------------------------------------------------------ the window
        [Test]
        public void TheShippedWindowIsTheFirstThreeOfAMode()
        {
            // Written down so that moving the built-in number is a deliberate act with a
            // failing test in front of it rather than a one-character edit nobody reviews.
            Assert.AreEqual(3, HeartLimits.DefaultGraceLevels);
            Assert.AreEqual(3, HeartRuleTable.Default.GraceLevels);
        }

        [Test]
        public void TheFirstThreeOfTheFirstChapterAreFreeAndTheFourthIsNot()
        {
            Grace(3);
            var index = Catalog();

            Assert.IsTrue(Free(index, "g1"));
            Assert.IsTrue(Free(index, "g2"));
            Assert.IsTrue(Free(index, "g3"));
            Assert.IsFalse(Free(index, "g4"), "the window closes on the fourth board");
            Assert.IsFalse(Free(index, "g5"));
        }

        [Test]
        public void LaterChaptersAreUntouched()
        {
            // The half of the rule that has to hold for the economy to be unmoved: a chapter
            // shipping in a year is priced exactly as the four before it were.
            Grace(3);
            var index = Catalog();

            Assert.IsFalse(Free(index, "g6"), "the head of the second chapter is not a beginning");
            Assert.IsFalse(Free(index, "g7"));
            Assert.IsFalse(Free(index, "g8"));
        }

        [Test]
        public void EveryModeGetsItsOwnOpening()
        {
            // Why the window is per mode rather than once per account. Lightweave is dragged
            // rather than tapped and is lost on ink rather than turns, so somebody arriving at
            // it having finished four glade chapters is a beginner again in every sense that
            // decides whether a heart should be taken off them.
            Grace(3);
            var index = Catalog();

            Assert.IsTrue(Free(index, "w1"));
            Assert.IsTrue(Free(index, "w2"));
            Assert.IsTrue(Free(index, "w3"));
            Assert.IsFalse(Free(index, "w4"));
            Assert.IsFalse(Free(index, "w5"), "and not the mode's second chapter");
        }

        [Test]
        public void TheWindowStopsAtTheEndOfTheFirstChapter()
        {
            // A published number longer than the chapter it lands in must not spill into the
            // next one — "the first chapter is free" is what the information panel prints, and
            // a window running past it would make that sentence untrue.
            Grace(HeartLimits.MaxGraceLevels);
            var index = Catalog();

            Assert.IsTrue(Free(index, "g5"), "the whole of a five-level first chapter");
            Assert.IsFalse(Free(index, "g6"), "and nothing beyond it");
            Assert.IsFalse(Free(index, "w5"), "in either mode");
        }

        [Test]
        public void NoughtTurnsTheWindowOffEntirely()
        {
            Grace(0);
            var index = Catalog();

            Assert.IsFalse(Free(index, "g1"), "the very first board of the game");
            Assert.IsFalse(Free(index, "w1"));
        }

        // ------------------------------------------------------------- the safe direction
        [Test]
        public void AnythingTheCatalogDoesNotCarryIsPricedLikeEveryOtherGlade()
        {
            // The safe direction, and it is the opposite of the unlock rule's. A typo must not
            // be the thing that hands a glade out free, because a free glade is an economy
            // decision and a locked one is only a nuisance.
            Grace(3);
            var index = Catalog();

            Assert.IsFalse(Free(index, "nonesuch"));
            Assert.IsFalse(HeartStake.IsFree(index, LevelId.None));
            Assert.IsFalse(HeartStake.IsFree(null, LevelId.Parse("g1")),
                           "and a catalog that has not loaded charges rather than gives away");
        }

        // ------------------------------------------------------------- what the panel prints
        [Test]
        public void TheCountIsReportedPerChapterForThePanel()
        {
            Grace(3);
            var index = Catalog();

            Assert.AreEqual(3, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(0, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c02_two")));
            Assert.AreEqual(3, HeartStake.FreeLevelsIn(index, ChapterId.Parse("w01_weave")),
                            "a mode's own first chapter, not the catalog's");
            Assert.AreEqual(0, HeartStake.FreeLevelsIn(index, ChapterId.Parse("w02_loom")));
        }

        [Test]
        public void TheCountNeverExceedsTheChapterItDescribes()
        {
            // The panel says "the first N levels of this chapter", so N has to be a number the
            // chapter can actually hold. Two windows, one shorter chapter.
            Grace(4);
            var index = Catalog();

            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("w01_weave")),
                            "exactly the whole of a four-level chapter");

            Grace(HeartLimits.MaxGraceLevels);
            Assert.AreEqual(5, HeartStake.FreeLevelsIn(index, ChapterId.Parse("c01_one")));
            Assert.AreEqual(4, HeartStake.FreeLevelsIn(index, ChapterId.Parse("w01_weave")));
        }

        [Test]
        public void TheCountAndTheRuleAlwaysAgree()
        {
            // The panel's number and the charge are two readings of one window, and a panel
            // that promises a free board the run then charges for is worse than no panel. Held
            // together by walking every level of every mode rather than by trusting that they
            // are written from the same field.
            Grace(3);
            var index = Catalog();

            foreach (var mode in index.Modes)
            {
                var chapters = index.ChaptersIn(mode);
                for (int c = 0; c < chapters.Count; c++)
                {
                    var chapter = chapters[c];
                    int promised = HeartStake.FreeLevelsIn(index, chapter.Id);
                    int actual = 0;

                    for (int i = 0; i < chapter.LevelIds.Count; i++)
                        if (HeartStake.IsFree(index, chapter.LevelIds[i])) actual++;

                    Assert.AreEqual(promised, actual, $"{chapter.Id.Value} says one thing and prices another");
                }
            }
        }

        [Test]
        public void TheFreeOnesAreThePrefixOfTheChapterRatherThanAnyThreeOfIt()
        {
            // A window that was not a prefix would read as arbitrary on the map: the player
            // meets these boards in order, so the free ones have to be the ones at the front.
            Grace(3);
            var index = Catalog();

            var chapter = index.FindChapter(ChapterId.Parse("c01_one"));
            bool charging = false;

            for (int i = 0; i < chapter.LevelIds.Count; i++)
            {
                bool free = HeartStake.IsFree(index, chapter.LevelIds[i]);
                if (!free) charging = true;
                else Assert.IsFalse(charging, "a free glade appeared after a charged one");
            }
        }
    }
}
