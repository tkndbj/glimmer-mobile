using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Which chapter a mode's map opens on when nobody named one.
    ///
    /// <para>
    /// The bug this exists to stop is quiet: every way back to the map except the chapter
    /// arrows arrives with no chapter, so the fallback is what a player actually meets after
    /// every single level. Get the recall wrong in the forgiving direction and they are
    /// returned to the newest chapter after replaying an early one — the original complaint —
    /// and wrong in the other direction and the map opens on a chapter that is not in the lane
    /// the switcher is showing, whose own arrows then lead somewhere else.
    /// </para>
    /// <para>
    /// These reach <c>PlayerPrefs</c>, so they run in the Editor's Test Runner rather than
    /// offline. There is no way round that and no point faking it: the whole feature is one
    /// value surviving a screen being destroyed.
    /// </para>
    /// </summary>
    public sealed class ChapterChoiceTests
    {
        /// <summary>
        /// The stored key, written down so that renaming it is a deliberate act with a failing
        /// test in front of it — a rename silently forgets where every player on the device
        /// was — and so this fixture has an honest way to tidy up after itself.
        /// </summary>
        const string GladeKey = "glimmer_map_chapter_glade";
        const string BudKey = "glimmer_map_chapter_bud";

        [SetUp]
        public void Clear() => Tidy();

        [TearDown]
        public void Tidy()
        {
            PlayerPrefs.DeleteKey(GladeKey);
            PlayerPrefs.DeleteKey(BudKey);
            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------------ the point
        [Test]
        public void AModeOpensOnTheChapterItWasLastLookingAt()
        {
            var index = Catalog();

            ChapterChoice.Write(index.FindChapter(ChapterId.Parse("c01_one")));

            var back = ChapterChoice.Read(index, GameMode.Glade);
            Assert.IsNotNull(back);
            Assert.AreEqual(ChapterId.Parse("c01_one"), back.Id,
                            "returning to the map must land where the player left it, not on " +
                            "whatever they have unlocked most recently");
        }

        [Test]
        public void TheKeyIsThePlayersDeviceAndIsNamedAfterTheMode()
        {
            var index = Catalog();

            ChapterChoice.Write(index.FindChapter(ChapterId.Parse("c02_two")));

            Assert.AreEqual("c02_two", PlayerPrefs.GetString(GladeKey, string.Empty));
        }

        [Test]
        public void NothingRememberedIsAnswerNull()
        {
            // The caller's cue to fall back to wherever the player is up to. It has to be
            // distinguishable from a remembered chapter, so it cannot be a first chapter.
            Assert.IsNull(ChapterChoice.Read(Catalog(), GameMode.Glade));
        }

        // ------------------------------------------------------------------ the lanes
        [Test]
        public void EachModeRemembersItsOwnPlace()
        {
            var index = Catalog();

            ChapterChoice.Write(index.FindChapter(ChapterId.Parse("c01_one")));
            ChapterChoice.Write(index.FindChapter(ChapterId.Parse("b01_thicket")));

            // One shared slot would make crossing the switcher and coming back land on the
            // other mode's chapter, which is a chapter this map cannot even show.
            Assert.AreEqual(ChapterId.Parse("c01_one"), ChapterChoice.Read(index, GameMode.Glade).Id);
            Assert.AreEqual(ChapterId.Parse("b01_thicket"), ChapterChoice.Read(index, GameMode.Bud).Id);
        }

        [Test]
        public void AChapterThatHasChangedModeIsNotHonoured()
        {
            ChapterChoice.Write(Catalog().FindChapter(ChapterId.Parse("c01_one")));

            // Same id, different lane — a re-filed chapter after a content drop. Opening the
            // glade map on it would put the header, the arrows and the switcher in three
            // different modes at once.
            var moved = new CatalogIndexBuilder();
            moved.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1, mode = "bud",
                levels = new[] { "one_a" },
            }, 1);

            Assert.IsNull(ChapterChoice.Read(moved.Build(), GameMode.Glade));
        }

        [Test]
        public void AChapterThisBuildNoLongerHasIsNotHonoured()
        {
            ChapterChoice.Write(Catalog().FindChapter(ChapterId.Parse("c02_two")));

            // A rollback, a disabled chapter, or a drop that has not downloaded. The map must
            // fall back rather than open onto a chapter with no body to read.
            var shorter = new CatalogIndexBuilder();
            shorter.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "one_a" },
            }, 1);

            Assert.IsNull(ChapterChoice.Read(shorter.Build(), GameMode.Glade));
        }

        [Test]
        public void NothingIsWrittenForAChapterThatIsNotThere()
        {
            ChapterChoice.Write(null);

            Assert.AreEqual(string.Empty, PlayerPrefs.GetString(GladeKey, string.Empty),
                            "a map that failed to resolve a chapter must not overwrite the " +
                            "one the player was last on");
        }

        // ------------------------------------------------------------------ fixtures
        /// <summary>Two glade chapters and one in a mode of its own.</summary>
        static CatalogIndex Catalog()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "c01_one", order = 10, version = 1,
                levels = new[] { "one_a", "one_b" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "c02_two", order = 20, version = 1,
                levels = new[] { "two_a", "two_b" },
            }, 1);
            builder.Add(new ManifestChapterDto
            {
                id = "b01_thicket", order = 30, version = 1, mode = "bud",
                levels = new[] { "thicket_a" },
            }, 1);
            return builder.Build();
        }
    }
}
