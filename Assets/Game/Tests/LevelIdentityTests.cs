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
        static LevelDefinition Level(string id, string chapter)
            => new LevelDefinition(
                LevelId.Parse(id), ChapterId.Parse(chapter),
                new LevelLayout(1, 1, new[] { "-EW/0" }), LevelTuning.Default(1),
                new LevelPresentation(Vector2.zero, null, null, null), null, null, null);

        static ChapterDefinition Chapter(string id, int order, params string[] levelIds)
        {
            var ids = new List<LevelId>();
            foreach (var l in levelIds) ids.Add(LevelId.Parse(l));
            return new ChapterDefinition(ChapterId.Parse(id), order, null,
                                         Color.white, Color.black, "bg", new[] { "strip0" }, ids);
        }

        [Test]
        public void PlayOrderFollowsChapterOrderNotInsertionOrder()
        {
            var builder = new LevelCatalogBuilder();

            // deliberately added out of order
            builder.AddChapter(Chapter("c02", 20, "b1"), new[] { Level("b1", "c02") });
            builder.AddChapter(Chapter("c01", 10, "a1", "a2"),
                               new[] { Level("a1", "c01"), Level("a2", "c01") });

            var catalog = builder.Build();

            Assert.AreEqual(3, catalog.Count);
            Assert.AreEqual("a1", catalog.At(0).Id.Value);
            Assert.AreEqual("a2", catalog.At(1).Id.Value);
            Assert.AreEqual("b1", catalog.At(2).Id.Value);
        }

        [Test]
        public void InsertingALevelDoesNotDisturbOtherIds()
        {
            var before = new LevelCatalogBuilder();
            before.AddChapter(Chapter("c01", 10, "a1", "a2"),
                              new[] { Level("a1", "c01"), Level("a2", "c01") });
            var oldCatalog = before.Build();

            var after = new LevelCatalogBuilder();
            after.AddChapter(Chapter("c01", 10, "a1", "tutorial", "a2"),
                             new[] { Level("a1", "c01"), Level("tutorial", "c01"), Level("a2", "c01") });
            var newCatalog = after.Build();

            // positions shift, which is exactly why nothing may be keyed on them
            Assert.AreEqual(1, oldCatalog.OrderOf(LevelId.Parse("a2")));
            Assert.AreEqual(2, newCatalog.OrderOf(LevelId.Parse("a2")));

            // identity does not, which is why records stay attached to the right level
            Assert.IsTrue(newCatalog.Contains(LevelId.Parse("a2")));
            Assert.AreEqual("a2", newCatalog.Find(LevelId.Parse("a2")).Id.Value);
        }

        [Test]
        public void NextAndPreviousWalkAcrossChapterBoundaries()
        {
            var builder = new LevelCatalogBuilder();
            builder.AddChapter(Chapter("c01", 10, "a1"), new[] { Level("a1", "c01") });
            builder.AddChapter(Chapter("c02", 20, "b1"), new[] { Level("b1", "c02") });
            var catalog = builder.Build();

            Assert.AreEqual("b1", catalog.Next(LevelId.Parse("a1")).Id.Value);
            Assert.AreEqual("a1", catalog.Previous(LevelId.Parse("b1")).Id.Value);
            Assert.IsNull(catalog.Next(LevelId.Parse("b1")));
            Assert.IsTrue(catalog.IsLast(LevelId.Parse("b1")));
        }

        [Test]
        public void ALevelNoChapterListsIsReportedAndDropped()
        {
            var builder = new LevelCatalogBuilder();
            builder.AddChapter(Chapter("c01", 10, "a1"),
                               new[] { Level("a1", "c01"), Level("orphan", "c01") });

            var catalog = builder.Build();

            Assert.AreEqual(1, catalog.Count, "an unlisted level must not silently appear");
            Assert.IsTrue(builder.HasProblems);
        }

        [Test]
        public void AChapterListingAnUnknownLevelIsReported()
        {
            var builder = new LevelCatalogBuilder();
            builder.AddChapter(Chapter("c01", 10, "a1", "ghost"), new[] { Level("a1", "c01") });

            var catalog = builder.Build();

            Assert.AreEqual(1, catalog.Count);
            Assert.IsTrue(builder.HasProblems);
        }
    }
}
