using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// What the map compares before redrawing a node after a sync (<c>LevelsScreen.OnLearned</c>).
    ///
    /// <para>
    /// A sync that "learned" something is any merge that changed the file anywhere, and one
    /// runs within a minute of any write - so the map met one routinely while a player was only
    /// looking at it, and rebuilt every node each time. These hold the rule that decides which
    /// nodes a redraw may touch: a node whose reading did not move is left standing, a node
    /// whose reading moved is redrawn, and a reading moves for every fact a node draws -
    /// including <c>unlocked</c>, which is not a field of the save but a rule over its
    /// neighbours.
    /// </para>
    /// </summary>
    [TestFixture]
    public sealed class MapReadingTests
    {
        static CatalogIndex TwoChapters()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "one_a", "one_b", "one_c" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "c02_two", order = 20, version = 1,
                levels = new[] { "two_a", "two_b", "two_c" },
            }, 1);
            return builder.Build();
        }

        /// <summary>Drives the save directly rather than through <c>RecordRun</c>, which would write a file.</summary>
        static void Holding(params (string level, int stars, int moves)[] records)
        {
            var dto = new SaveFileDto { levels = new LevelRecordDto[records.Length] };

            for (int i = 0; i < records.Length; i++)
                dto.levels[i] = new LevelRecordDto
                {
                    levelId = records[i].level,
                    stars = records[i].stars,
                    bestMoves = records[i].moves,
                    clears = 1,
                };

            PlayerProgress.LoadFrom(dto);
        }

        [SetUp]
        public void StartFromTheShippedRules()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [TearDown]
        public void ClearTheSave()
        {
            ProgressionRules.Reset();
            PlayerProgress.LoadFrom(new SaveFileDto());
        }

        [Test]
        public void AnUnchangedSaveReadsTheSameAndIsNotRedrawn()
        {
            var index = TwoChapters();
            Holding(("one_a", 3, 12));

            var drawn = GladeReading.Of(index, LevelId.Parse("one_a"));
            var again = GladeReading.Of(index, LevelId.Parse("one_a"));

            Assert.AreEqual(drawn, again);
            Assert.IsFalse(MapRedraw.Needed(true, drawn, again),
                           "a node whose answer did not move is left standing");
            Assert.AreEqual(new GladeReading(3, 12, true), drawn, "it reads stars, moves and open");
        }

        [Test]
        public void ABetterRecordFromTheOtherDeviceMovesTheReading()
        {
            var index = TwoChapters();
            var id = LevelId.Parse("one_a");

            Holding(("one_a", 2, 14));
            var drawn = GladeReading.Of(index, id);

            Holding(("one_a", 2, 11));
            Assert.IsTrue(MapRedraw.Needed(true, drawn, GladeReading.Of(index, id)), "fewer moves is a redraw");

            Holding(("one_a", 3, 14));
            Assert.IsTrue(MapRedraw.Needed(true, drawn, GladeReading.Of(index, id)), "a third star is a redraw");
        }

        [Test]
        public void AClearOnTheGladeBehindMovesTheNextGladesReading()
        {
            var index = TwoChapters();
            var next = LevelId.Parse("one_b");

            Holding();
            var drawn = GladeReading.Of(index, next);
            Assert.IsFalse(drawn.Unlocked);

            // Nothing about one_b's own record changed; only its neighbour's did. The reading
            // still moves, because it asks the unlock rule rather than the record.
            Holding(("one_a", 1, 20));
            var now = GladeReading.Of(index, next);
            Assert.IsTrue(now.Unlocked);
            Assert.IsTrue(MapRedraw.Needed(true, drawn, now));
        }

        [Test]
        public void ANodeThatIsNotStandingIsAlwaysRedrawn()
        {
            var reading = new GladeReading(1, 9, true);
            Assert.IsTrue(MapRedraw.Needed(false, reading, reading));
        }

        [Test]
        public void TheChapterEndReadsTheGateItPrints()
        {
            var index = TwoChapters();
            var chapter = ChapterId.Parse("c01_one");

            Holding(("one_a", 1, 20));
            var drawn = ChapterEndReading.Of(index, chapter);
            Assert.IsTrue(drawn.Onward);
            Assert.IsFalse(drawn.Reachable);

            // The same stars held is the same plate.
            Holding(("one_a", 1, 15));
            Assert.IsFalse(MapRedraw.Needed(true, drawn, ChapterEndReading.Of(index, chapter)),
                           "a better move count on a glade changes nothing the teaser prints");

            // One more star is one more on the plate's "held" figure.
            Holding(("one_a", 2, 20));
            Assert.IsTrue(MapRedraw.Needed(true, drawn, ChapterEndReading.Of(index, chapter)));

            // The gate opening is the seal turning into a signpost.
            Holding(("one_a", 3, 20), ("one_b", 3, 20), ("one_c", 3, 20));
            var open = ChapterEndReading.Of(index, chapter);
            Assert.IsTrue(open.Reachable);
            Assert.IsTrue(MapRedraw.Needed(true, drawn, open));
        }

        [Test]
        public void TheLastChapterOfTheCatalogReadsAsAConstant()
        {
            var index = TwoChapters();
            var last = ChapterId.Parse("c02_two");

            Holding();
            var drawn = ChapterEndReading.Of(index, last);
            Assert.AreEqual(ChapterEndReading.End, drawn);

            Holding(("two_a", 3, 10), ("two_b", 3, 10));
            Assert.IsFalse(MapRedraw.Needed(true, drawn, ChapterEndReading.Of(index, last)),
                           "the 'more soon' seal has nothing to redraw for");
        }
    }
}
