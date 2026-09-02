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

        static ChapterDefinition Chapter(int strips, float teaserX = 0f)
        {
            var mapStrips = new string[strips];
            for (int i = 0; i < strips; i++) mapStrips[i] = "strip" + i;

            return new ChapterDefinition(ChapterId.Parse("t_chapter"), null,
                                         Color.white, Color.black, "sky_00", mapStrips, teaserX);
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
                Level("t_c", .30f, .70f),
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
        /// chapter that runs its last glade up to where the marker lands collides with a node
        /// whose coordinates appear nowhere in the content file.
        /// </summary>
        /// <remarks>
        /// The offending glade is placed by <em>asking the rule</em> rather than at a typed
        /// fraction. It used to sit at a literal 0.95, which was the ceiling at the time; when
        /// the ceiling became a distance from the top of the map (<see cref="ChapterMap.TeaserHeadroom"/>)
        /// the marker moved and this fixture quietly stopped crowding anything — the test went
        /// green-adjacent by testing nothing. Passing a highest of 1 forces the clamp, so this
        /// is wherever the marker actually is for a chapter this tall, for ever.
        /// </remarks>
        [Test]
        public void AGladeCrowdingTheEndOfChapterMarkerWarns()
        {
            float onTopOfTheMarker = ChapterMap.TeaserPosition(1f, 3).y;

            var levels = new List<LevelDefinition>
            {
                Level("t_a", .5f, .30f),
                Level("t_b", ChapterMap.TeaserX, onTopOfTheMarker),
            };

            var issues = ChapterMapValidator.Validate(Chapter(3), levels);
            Assert.IsTrue(Mentions(issues, "end-of-chapter marker"), Describe(issues));
        }

        /// <summary>
        /// The shape every mode's first chapter shipped in: the last glade on the right, under
        /// a marker on its default side, 308 canvas units straight up. The discs clear each
        /// other by 88 units, so the distance check passed — and the marker's name plate sat on
        /// the player's standing above the tenth glade. Mirrored, with the tenth glade on the
        /// left, the same chapter is clean.
        /// </summary>
        [Test]
        public void AMarkerAboveTheLastGladeStandsOnItsStandingMark()
        {
            float marker = ChapterMap.TeaserPosition(1f, 6).y;

            var shipped = new List<LevelDefinition>
            {
                Level("t_a", .34f, marker - .13f),
                Level("t_b", ChapterMap.TeaserX, marker - .043f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(6), shipped);
            Assert.IsTrue(Mentions(issues, "standing mark above 't_b'"), Describe(issues));
            Assert.IsFalse(Mentions(issues, "canvas units from the end-of-chapter marker"),
                           "the discs clear each other, which is the whole point:\n  " + Describe(issues));

            var mirrored = new List<LevelDefinition>
            {
                Level("t_a", ChapterMap.TeaserX, marker - .13f),
                Level("t_b", .30f, marker - .043f),
            };

            var clean = ChapterMapValidator.Validate(Chapter(6), mirrored);
            Assert.AreEqual(0, clean.Count, Describe(clean));
        }

        /// <summary>
        /// The same rule between two glades: one standing 360 units straight above another
        /// clears its disc by 140 and still hangs its plate over the lower glade's standing.
        /// On the other side of the map, or 576 units up, it is clean — which is why every
        /// shipped chapter alternates sides.
        /// </summary>
        [Test]
        public void AGladeStandingOnAnothersMarkWarns()
        {
            var stacked = new List<LevelDefinition>
            {
                Level("t_a", .30f, .20f),
                Level("t_b", .30f, .25f),
            };

            var issues = ChapterMapValidator.Validate(Chapter(6), stacked);
            Assert.IsTrue(Mentions(issues, "'t_b' stands on the standing mark above 't_a'"), Describe(issues));

            var opposite = new List<LevelDefinition>
            {
                Level("t_a", .30f, .20f),
                Level("t_b", .70f, .25f),
            };
            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(6), opposite).Count);

            var higher = new List<LevelDefinition>
            {
                Level("t_a", .30f, .20f),
                Level("t_b", .30f, .28f),
            };
            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(6), higher).Count);
        }

        /// <summary>
        /// The crown and the body are what the screen draws, measured in Domain where the
        /// validator can reach them. The screen may not hold the map's geometry (8a) and
        /// Domain may not read the screen, so this is the only place the two meet: resize the
        /// standing pill, the plate or the rock and this names the number that stopped
        /// covering it.
        /// </summary>
        [Test]
        public void TheCrownAndBodyCoverWhatTheMapDraws()
        {
            Assert.GreaterOrEqual(ChapterMap.CrownHalfWidth, LevelsScreen.RankMarkTwoLine.x * .5f,
                                  "the standing pill is wider than the crown");
            Assert.LessOrEqual(ChapterMap.CrownBottom, LevelsScreen.RankMarkBottom,
                               "the standing pill starts below the crown");

            float pillTop = LevelsScreen.RankMarkBottom + LevelsScreen.RankMarkTwoLine.y;
            float medalTop = LevelsScreen.RankMarkBottom + LevelsScreen.RankMarkTwoLine.y * .5f
                             + LevelsScreen.MedalY + LevelsScreen.MedalSize * .5f;
            Assert.GreaterOrEqual(ChapterMap.CrownTop, Mathf.Max(pillTop, medalTop),
                                  "the standing mark reaches above the crown");

            Assert.GreaterOrEqual(ChapterMap.BodyHalfWidth,
                                  Mathf.Max(LevelsScreen.PerchWidth, LevelsScreen.PlateWidth) * .5f,
                                  "a perch is wider than the body");
            Assert.GreaterOrEqual(ChapterMap.BodyBelow, -LevelsScreen.PlateY + LevelsScreen.PlateHeight * .5f,
                                  "the name plate hangs below the body");
            Assert.GreaterOrEqual(ChapterMap.BodyAbove,
                                  Mathf.Max(LevelsScreen.NodeSize * .5f + 2f,
                                            LevelsScreen.PerchRockY + LevelsScreen.PerchRockHeight * .5f),
                                  "the disc or the rock reaches above the body");
        }

        /// <summary>
        /// A chapter may say which side the marker caps its trail on, and the check has to
        /// follow it there. Reading the default instead would be invisible in the ordinary
        /// direction — it would simply stop noticing a glade the marker now sits on — which
        /// is the one thing this check exists for.
        /// </summary>
        [Test]
        public void AnAuthoredTeaserMovesTheMarkerAndTheClearanceCheckWithIt()
        {
            float onTopOfTheMarker = ChapterMap.TeaserPosition(1f, 3).y;

            var levels = new List<LevelDefinition>
            {
                Level("t_a", .5f, .30f),
                Level("t_b", .30f, onTopOfTheMarker),
            };

            Assert.AreEqual(0, ChapterMapValidator.Validate(Chapter(3), levels).Count,
                            "the default marker is on the other side of the map");

            var moved = ChapterMapValidator.Validate(Chapter(3, .30f), levels);
            Assert.IsTrue(Mentions(moved, "end-of-chapter marker"), Describe(moved));
        }

        /// <summary>
        /// Zero is what <c>JsonUtility</c> writes into a field a chapter authored before this
        /// existed, so it has to keep meaning "the default" rather than "the left edge" — the
        /// convention <c>par</c> and <c>budgetFactor</c> already use.
        /// </summary>
        [Test]
        public void AnUnauthoredTeaserKeepsTheDefaultSide()
        {
            Assert.AreEqual(ChapterMap.TeaserX, ChapterMap.TeaserAcross(0f));
            Assert.AreEqual(ChapterMap.TeaserX, ChapterMap.TeaserAcross(-1f));
            Assert.AreEqual(ChapterMap.TeaserX, ChapterMap.TeaserAcross(1.4f));
            Assert.AreEqual(.30f, ChapterMap.TeaserAcross(.30f));

            Assert.AreEqual(ChapterMap.TeaserX, Chapter(3).TeaserX);
            Assert.AreEqual(.30f, Chapter(3, .30f).TeaserX);
            Assert.AreEqual(.30f, ChapterMap.TeaserPosition(.5f, 3, .30f).x);
            Assert.AreEqual(ChapterMap.TeaserPosition(.5f, 3).y, ChapterMap.TeaserPosition(.5f, 3, .30f).y,
                            "only the across-axis is authorable");
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

        /// <summary>
        /// The end-of-chapter marker has to clear the header, and the header is the one thing
        /// about it that no content file and no validator can see.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The marker's coordinate is authored nowhere — it is placed for the author by
        /// <see cref="ChapterMap.TeaserPosition"/>, and the clearance check only ever compares
        /// it against glades. So when the mode switcher was added <em>beneath</em> the plaque
        /// the headroom had been sized against, the marker went on landing at the same
        /// distance from the top of the map and sat half behind the new control, in every
        /// chapter of every mode, with every gate green and no wrong number in any file.
        /// </para>
        /// <para>
        /// This is the arithmetic that catches the next one. It has to live here rather than
        /// beside either half: <see cref="ChapterMap"/> is Domain and may not read the screen,
        /// and the screen may not hold a second copy of the map's geometry (invariant 8a).
        /// Adding a row to the header now fails a test instead of quietly swallowing the
        /// marker.
        /// </para>
        /// </remarks>
        [Test]
        public void TheEndOfChapterMarkerClearsTheHeaderOnTheWorstDisplay()
        {
            float needed = LevelsScreen.HeaderUnderside + ChapterMap.TeaserTopInset + ChapterMap.TeaserReach;

            Assert.GreaterOrEqual(ChapterMap.TeaserHeadroom, needed,
                                  $"the header reaches {LevelsScreen.HeaderUnderside} down the safe area, a " +
                                  $"cutout costs another {ChapterMap.TeaserTopInset} and the marker reaches " +
                                  $"{ChapterMap.TeaserReach} above its own centre, so a headroom below " +
                                  $"{needed} draws it behind the chrome");
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
