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
        const string PrismKey = "glimmer_map_chapter_prism";

        /// <summary>
        /// <c>ModeChoice</c>'s key, cleared here for the same reason the two above are — and it
        /// caught something real. Without it, "nothing is remembered" is not a state this fixture
        /// can reach: the Editor's own <c>PlayerPrefs</c> hold whichever mode the last map opened
        /// on, and a case about the fallback quietly tests the remembered value instead.
        /// </summary>
        const string ModeKey = "glimmer_map_mode";

        [SetUp]
        public void Clear() => Tidy();

        [TearDown]
        public void Tidy()
        {
            PlayerPrefs.DeleteKey(GladeKey);
            PlayerPrefs.DeleteKey(PrismKey);
            PlayerPrefs.DeleteKey(ModeKey);
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
            ChapterChoice.Write(index.FindChapter(ChapterId.Parse("p01_prismvale")));

            // One shared slot would make crossing the switcher and coming back land on the
            // other mode's chapter, which is a chapter this map cannot even show.
            Assert.AreEqual(ChapterId.Parse("c01_one"), ChapterChoice.Read(index, GameMode.Glade).Id);
            Assert.AreEqual(ChapterId.Parse("p01_prismvale"), ChapterChoice.Read(index, GameMode.Prism).Id);
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
                id = "c01_one", order = 10, version = 1, mode = "prism",
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

        // ------------------------------------------------------------------ the front door
        /// <summary>
        /// The map opens on the first row of the switcher, and the two are one answer.
        ///
        /// <para>
        /// They were briefly two — the catalog's default preferred the classic mode where the
        /// switcher led with whatever the registry led with — and two answers means a map opening
        /// on one mode while the control above it offers a different one first. Nothing else in
        /// the suite would notice, because each half is individually correct.
        /// </para>
        /// </summary>
        [Test]
        public void TheMapOpensOnWhateverTheSwitcherOffersFirst()
        {
            var index = Catalog();

            Assert.AreEqual(index.Modes[0], index.DefaultMode);
            Assert.AreEqual(index.DefaultMode, ModeChoice.Read(index),
                            "nothing remembered lands on the front door");
        }

        /// <summary>
        /// A catalog with no glade chapters still opens on something, which is the state this
        /// game ships in: the classic mode and Lightfall are hidden (invariant 38), so the
        /// parsing default names a mode with nothing in it.
        /// </summary>
        [Test]
        public void ACatalogWithoutTheClassicModeStillHasAFrontDoor()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(new ManifestChapterDto
            {
                id = "p01_prismvale", order = 10, version = 1, mode = "prism",
                levels = new[] { "prismvale_a" },
            }, 1);

            var index = builder.Build();

            Assert.AreNotEqual(GameMode.Default, index.DefaultMode,
                               "the parsing default is not a mode this catalog can open");
            Assert.AreEqual(GameMode.Prism, index.DefaultMode);
            Assert.AreEqual(GameMode.Prism, ModeChoice.Read(index));
        }

        /// <summary>
        /// Nothing remembered is not the same as the classic mode remembered, and for a long time
        /// it was: <c>GameMode.TryParse</c> answers <em>true</em> for an empty string with the
        /// glade, because a chapter with no <c>mode</c> field is a glade. Reading a stored
        /// preference through it therefore could not tell a player who has never touched the
        /// switcher from one who chose the glade. Harmless while the glade was also the fallback;
        /// a map opening on the wrong mode the moment the front door moved.
        /// </summary>
        [Test]
        public void AnEmptyPreferenceIsNothingRememberedRatherThanTheClassicMode()
        {
            var index = Catalog();

            PlayerPrefs.SetString(ModeKey, string.Empty);
            PlayerPrefs.Save();

            Assert.AreEqual(index.DefaultMode, ModeChoice.Read(index));
            Assert.AreNotEqual(GameMode.Default, ModeChoice.Read(index),
                               "an empty string parses as the glade; it must not read as one");
        }

        /// <summary>
        /// A remembered mode still wins, which is why moving the front door does not move a
        /// player who has already chosen. It is the same rule the chapter choice keeps: the
        /// fallback is for somebody who has said nothing, not an override of somebody who has.
        /// </summary>
        [Test]
        public void ARememberedModeBeatsTheFrontDoor()
        {
            var index = Catalog();

            // The classic mode: in this catalog, and deliberately not its front door.
            ModeChoice.Write(GameMode.Glade);

            Assert.AreNotEqual(GameMode.Glade, index.DefaultMode,
                               "this case only says anything while the two differ");
            Assert.AreEqual(GameMode.Glade, ModeChoice.Read(index));
        }

        /// <summary>
        /// An empty catalog answers with no mode at all rather than with one dressed up as an
        /// answer. It is a content failure, and a caller has to be able to see it.
        /// </summary>
        [Test]
        public void AnEmptyCatalogHasNoFrontDoor()
        {
            Assert.IsFalse(CatalogIndex.Empty.DefaultMode.IsValid);
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
                id = "p01_prismvale", order = 30, version = 1, mode = "prism",
                levels = new[] { "prismvale_a" },
            }, 1);
            return builder.Build();
        }
    }
}
