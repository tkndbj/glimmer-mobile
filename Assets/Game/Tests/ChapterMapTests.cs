using System.Collections.Generic;
using GlimmerGrove.Content;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The map checks exist because a chapter can be perfectly valid level by level and
    /// still be laid out unplayably — two glades on the same rock, a trail running back
    /// down the hill. Nothing else in the pipeline can see either: the JSON parses, the
    /// boards are solvable, the art resolves, the build is green.
    ///
    /// These tests pin the two things that make the check worth trusting: that it fires
    /// on the real mistakes, and that it measures in canvas units rather than in raw
    /// fractions — because a chapter with six strips is six times as tall, and a checker
    /// that forgot would nag every large chapter about glades half a screen apart.
    /// </summary>
    public sealed class ChapterMapTests
    {
        /// <summary>A heart and a critter facing each other. Valid, so only placement is in play.</summary>
        static readonly string[] Rows = { "*E#R/1 @W#R/0" };

        static ChapterDefinition Chapter(int strips)
        {
            var mapStrips = new string[strips];
            for (int i = 0; i < strips; i++) mapStrips[i] = "strip" + i;

            return new ChapterDefinition(ChapterId.Parse("t_chapter"), null,
                                         Color.white, Color.black, "play_0", mapStrips);
        }

        static LevelDefinition Level(string id, float x, float y)
            => new LevelDefinition(
                LevelId.Parse(id), ChapterId.Parse("t_chapter"),
                new LevelLayout(2, 1, Rows), LevelTuning.Default(3),
                new LevelPresentation(new Vector2(x, y), null, null, null));

        static bool Mentions(List<LevelIssue> issues, string fragment)
        {
            foreach (var issue in issues)
                if (issue.Message.Contains(fragment)) return true;
            return false;
        }

        static string Describe(List<LevelIssue> issues)
            => issues.Count == 0 ? "no issues" : string.Join("\n  ", issues.ConvertAll(i => i.ToString()));

        [Test]
        public void AWellSpacedChapterPasses()
        {
            var levels = new List<LevelDefinition>
            {
                Level("t_a", .33f, .20f),
                Level("t_b", .67f, .45f),
                Level("t_c", .32f, .70f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(3), levels);
            Assert.AreEqual(0, issues.Count, Describe(issues));
        }

        [Test]
        public void TwoGladesOnTheSameSpotWarn()
        {
            var levels = new List<LevelDefinition>
            {
                Level("t_a", .5f, .30f),
                Level("t_b", .5f, .30f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(3), levels);
            Assert.IsTrue(Mentions(issues, "overlap"), Describe(issues));
        }

        /// <summary>
        /// The whole reason this check needs the chapter and not just the levels. The
        /// same two fractions are a collision in a one-strip chapter and comfortably
        /// apart in a six-strip one, because mapY is a fraction of the chapter's own
        /// height. A validator comparing raw fractions would get exactly one of these
        /// two cases right and never know which.
        /// </summary>
        [Test]
        public void ClosenessIsMeasuredAgainstTheChaptersHeightNotTheFractions()
        {
            var levels = new List<LevelDefinition>
            {
                Level("t_a", .5f, .40f),
                Level("t_b", .5f, .55f),
            };

            // one strip: 0.15 of 1200 units is 180 apart, closer than a 196-wide disc
            var cramped = ChapterMapValidator.Validate(Chapter(1), levels);
            Assert.IsTrue(Mentions(cramped, "overlap"), Describe(cramped));

            // six strips: the same fractions are 1080 units apart, which is fine
            var roomy = ChapterMapValidator.Validate(Chapter(6), levels);
            Assert.IsFalse(Mentions(roomy, "overlap"), Describe(roomy));
        }

        [Test]
        public void AGladeBelowTheOneBeforeItWarns()
        {
            var levels = new List<LevelDefinition>
            {
                Level("t_a", .3f, .60f),
                Level("t_b", .7f, .25f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(3), levels);
            Assert.IsTrue(Mentions(issues, "runs back down the map"), Describe(issues));
        }

        /// <summary>
        /// Play order is the index's, and this validator is handed that order. Two
        /// glades ascending are fine; the same two handed over the other way round are
        /// a backwards trail. If this ever stops holding, the check is reading the
        /// body's order and is worthless on any chapter whose file is not in play order.
        /// </summary>
        [Test]
        public void TheWarningFollowsPlayOrderNotThePositions()
        {
            var low = Level("t_a", .3f, .25f);
            var high = Level("t_b", .7f, .60f);

            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(3),
                new List<LevelDefinition> { low, high }).Count);

            var backwards = ChapterMapValidator.Validate(Chapter(3),
                new List<LevelDefinition> { high, low });
            Assert.IsTrue(Mentions(backwards, "runs back down the map"), Describe(backwards));
        }

        /// <summary>
        /// The end-of-chapter marker is placed for the author rather than by them, so a
        /// chapter that runs its last glade up to the ceiling collides with a node whose
        /// coordinates appear nowhere in the content file.
        /// </summary>
        [Test]
        public void AGladeCrowdingTheEndOfChapterMarkerWarns()
        {
            var levels = new List<LevelDefinition>
            {
                Level("t_a", .5f, .30f),
                Level("t_b", ChapterMap.TeaserX, .95f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(3), levels);
            Assert.IsTrue(Mentions(issues, "end-of-chapter marker"), Describe(issues));
        }

        [Test]
        public void AnEmptyChapterIsNotAnIssue()
        {
            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(3), new List<LevelDefinition>()).Count);
            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(3), null).Count);
            Assert.AreEqual(0, ChapterMapValidator.Validate(null, new List<LevelDefinition>()).Count);
        }

        /// <summary>
        /// The geometry has to keep matching the screen that draws it. If somebody
        /// retunes the strip height or the disc size, this is the test that notices the
        /// validator and the map have started disagreeing about what a collision is.
        /// </summary>
        [Test]
        public void TheGeometryMatchesWhatTheMapDraws()
        {
            Assert.AreEqual(1200f, ChapterMap.StripHeight, "strips are 1200 canvas units on screen");
            Assert.AreEqual(1080f, ChapterMap.Width, "the map spans the canvas reference width");
            Assert.AreEqual(196f, ChapterMap.NodeDiameter, "LevelsScreen draws the glade disc at 196");

            Assert.AreEqual(2400f, ChapterMap.Height(2));
            Assert.AreEqual(1200f, ChapterMap.Height(0), "a chapter is never shorter than one strip");
        }

        /// <summary>Every chapter that actually ships must be laid out cleanly.</summary>
        [Test]
        public void EveryShippedChapterIsLaidOutCleanly()
        {
            var source = new Content.Sources.BundledContentSource();
            var result = new LevelRepository(source).LoadEverythingAsync().GetAwaiter().GetResult();

            var index = result.Catalog.Index;
            if (index.IsEmpty) Assert.Ignore("no bundled content available in this run");

            foreach (var chapter in index.Chapters)
            {
                if (!result.Catalog.TryResidentChapter(chapter.Id, out var body)) continue;

                var levels = new List<LevelDefinition>();
                foreach (var level in body.InIndexOrder(chapter.LevelIds)) levels.Add(level);

                var issues = ChapterMapValidator.Validate(body.Definition, levels);
                Assert.AreEqual(0, issues.Count, $"{chapter.Id}:\n  {Describe(issues)}");
            }
        }
    }
}
