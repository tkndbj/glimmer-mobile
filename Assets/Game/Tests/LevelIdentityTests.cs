using System.Collections.Generic;
using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Identity and ordering: the two things a content update is most likely to get
    /// wrong. A level's id must survive anything; its position must survive nothing.
    /// </summary>
    public sealed class LevelIdentityTests
    {
        [TestCase("c01_first_light")]
        [TestCase("a")]
        [TestCase("level_42")]
        public void ValidIdsParse(string raw)
        {
            Assert.IsTrue(LevelId.TryParse(raw, out var id, out string error), error);
            Assert.AreEqual(raw, id.Value);
        }

        [TestCase("", TestName = "empty")]
        [TestCase("   ", TestName = "blank")]
        [TestCase("Capitals", TestName = "uppercase")]
        [TestCase("has space", TestName = "space")]
        [TestCase("has-dash", TestName = "dash")]
        [TestCase("_leading", TestName = "leading underscore")]
        [TestCase("trailing_", TestName = "trailing underscore")]
        public void InvalidIdsAreRejected(string raw)
        {
            Assert.IsFalse(LevelId.TryParse(raw, out _, out _));
        }

        [Test]
        public void DefaultIdIsNotValidAndCompsEqual()
        {
            Assert.IsFalse(LevelId.None.IsValid);
            Assert.AreEqual(LevelId.None, default(LevelId));
            Assert.AreNotEqual(LevelId.None, LevelId.Parse("a"));
        }

        [Test]
        public void IdsWorkAsDictionaryKeys()
        {
            var map = new Dictionary<LevelId, int> { [LevelId.Parse("one")] = 1 };
            Assert.AreEqual(1, map[LevelId.Parse("one")]);
        }

        // ------------------------------------------------------------- ordering
        // Ordering is the index's job, and the index is built from the manifest alone.
        // That is what these exercise: the boot path must know the whole shape of the
        // game without opening a single chapter body.

        static ManifestChapterDto Chapter(string id, int order, params string[] levelIds)
            => new ManifestChapterDto { id = id, order = order, version = 1, levels = levelIds };

        static CatalogIndex IndexOf(params ManifestChapterDto[] chapters)
        {
            var builder = new CatalogIndexBuilder();
            foreach (var chapter in chapters) builder.Add(chapter, 1);
            return builder.Build();
        }

        [Test]
        public void PlayOrderFollowsChapterOrderNotManifestOrder()
        {
            // deliberately listed out of order
            var index = IndexOf(Chapter("c02", 20, "b1"),
                                Chapter("c01", 10, "a1", "a2"));

            Assert.AreEqual(3, index.Count);
            Assert.AreEqual("a1", index.At(0).Value);
            Assert.AreEqual("a2", index.At(1).Value);
            Assert.AreEqual("b1", index.At(2).Value);
        }

        [Test]
        public void InsertingALevelDoesNotDisturbOtherIds()
        {
            var before = IndexOf(Chapter("c01", 10, "a1", "a2"));
            var after = IndexOf(Chapter("c01", 10, "a1", "tutorial", "a2"));

            // positions shift, which is exactly why nothing may be keyed on them
            Assert.AreEqual(1, before.OrderOf(LevelId.Parse("a2")));
            Assert.AreEqual(2, after.OrderOf(LevelId.Parse("a2")));

            // identity does not, which is why records stay attached to the right level
            Assert.IsTrue(after.Contains(LevelId.Parse("a2")));
        }

        [Test]
        public void NextAndPreviousWalkAcrossChapterBoundaries()
        {
            var index = IndexOf(Chapter("c01", 10, "a1"), Chapter("c02", 20, "b1"));

            Assert.AreEqual("b1", index.Next(LevelId.Parse("a1")).Value);
            Assert.AreEqual("a1", index.Previous(LevelId.Parse("b1")).Value);
            Assert.IsFalse(index.Next(LevelId.Parse("b1")).IsValid);
            Assert.IsTrue(index.IsLast(LevelId.Parse("b1")));
        }

        [Test]
        public void ALevelIdClaimedByTwoChaptersIsReportedAndKeptOnce()
        {
            var builder = new CatalogIndexBuilder();
            builder.Add(Chapter("c01", 10, "shared"), 1);
            builder.Add(Chapter("c02", 20, "shared"), 1);

            var index = builder.Build();

            // A save record names a level, not a chapter. Two chapters owning one id
            // would make it ambiguous which reward rule that record was paid at.
            Assert.AreEqual(1, index.Count);
            Assert.AreEqual("c01", index.ChapterOf(LevelId.Parse("shared")).Value);
            Assert.IsTrue(builder.HasProblems);
        }

        [Test]
        public void EveryLevelKnowsItsChapterWithoutReadingABody()
        {
            var index = IndexOf(Chapter("c01", 10, "a1", "a2"), Chapter("c02", 20, "b1"));

            Assert.AreEqual("c01", index.ChapterOf(LevelId.Parse("a2")).Value);
            Assert.AreEqual("c02", index.ChapterOf(LevelId.Parse("b1")).Value);

            // The index is the game's IChapterMap, which is what stops a forged save
            // minting currency from level ids that do not exist.
            Assert.IsTrue(index.TryGetChapter(LevelId.Parse("a1"), out _));
            Assert.IsFalse(index.TryGetChapter(LevelId.Parse("invented"), out _));
        }

        [Test]
        public void ARetiredOrTooNewChapterIsSkippedEntirely()
        {
            var retired = new ManifestChapterDto { id = "c09", order = 90, disabled = true,
                                                   levels = new[] { "z1" } };
            var future = new ManifestChapterDto { id = "c10", order = 100, minAppVersion = 99,
                                                  levels = new[] { "z2" } };

            var builder = new CatalogIndexBuilder();
            builder.Add(Chapter("c01", 10, "a1"), 1);
            builder.Add(retired, 1);
            builder.Add(future, 1);

            var index = builder.Build();

            Assert.AreEqual(1, index.Count);
            Assert.IsFalse(builder.HasProblems, "skipping content meant for other clients is not a fault");
        }

    }
}
