using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A chapter's declared mode against the mode its levels actually are.
    ///
    /// <para>
    /// <b>The failure this guards is silent and it shipped once.</b> A chapter's mode lives in
    /// <c>manifest.json</c>, and the first Budburst chapter to be adopted by <c>Sync Manifest</c>
    /// went in without it — so it was indexed as a glade chapter, and every other check in the
    /// pipeline passed: the levels parsed, the boards were proved solvable, the strings resolved,
    /// the art resolved, the build went green. The only symptom was one line in a log saying the
    /// chapter opened on the wrong chapter's stars, because <c>LevelUnlock.GateFor</c> looks for
    /// the chapter before this one <em>in the same mode</em>.
    /// </para>
    /// <para>
    /// So the field is now derived by the sync and proved by the build gate, and both ask
    /// <see cref="ChapterModeValidator"/>. This is the failing case that makes it a check rather
    /// than an assertion.
    /// </para>
    /// </summary>
    public sealed class ChapterModeTests
    {
        /// <summary>A heart and a critter facing each other: valid, so only the mode is in play.</summary>
        static readonly string[] Rows = { "*E#R/1 @W#R/0" };

        static LevelDefinition Glade(string id)
            => new LevelDefinition(
                LevelId.Parse(id), ChapterId.Parse("t_chapter"),
                new LevelLayout(2, 1, Rows), LevelTuning.Default(3),
                new LevelPresentation(new Vector2(.3f, .3f), null, null, null));

        /// <summary>A grove, built the way content builds one — the smallest there is.</summary>
        static LevelDefinition Bud(string id)
        {
            BudLayout.TryReadRows(new[] { ".Y..", "YRo.", "....", "...." },
                                  4, 4, out var ground, out var value, out _);
            BudDeal.TryParse("G", out var deal, out _);

            var rules = new BudRules(new BudLayout(4, 4, ground, value, deal));

            return new LevelDefinition(
                LevelId.Parse(id), ChapterId.Parse("t_chapter"), rules,
                LevelTuning.Default(1),
                new LevelPresentation(new Vector2(.3f, .3f), null, null, null));
        }

        static ChapterId Chapter => ChapterId.Parse("t_chapter");

        // ------------------------------------------------------------------ deriving
        [Test]
        public void AChapterOfOneModeDerivesThatMode()
        {
            Assert.IsTrue(ChapterModeValidator.TryDerive(
                new List<LevelDefinition> { Bud("t_a"), Bud("t_b") }, out var mode));
            Assert.AreEqual(GameMode.Bud, mode);

            Assert.IsTrue(ChapterModeValidator.TryDerive(
                new List<LevelDefinition> { Glade("t_c"), Glade("t_d") }, out mode));
            Assert.AreEqual(GameMode.Glade, mode);
        }

        [Test]
        public void AChapterOfTwoModesDerivesNothing()
        {
            // Refused rather than answered with whichever came first: a guess written into the
            // manifest is a guess nothing downstream would ever question.
            Assert.IsFalse(ChapterModeValidator.TryDerive(
                new List<LevelDefinition> { Glade("t_a"), Bud("t_b") }, out _));
        }

        [Test]
        public void AnEmptyChapterDerivesNothingAndIsNotAnError()
        {
            // Nothing to be wrong about, and a chapter with no levels is somebody else's error.
            Assert.IsFalse(ChapterModeValidator.TryDerive(new List<LevelDefinition>(), out _));
            Assert.IsFalse(ChapterModeValidator.TryDisagreement(
                Chapter, GameMode.Bud, new List<LevelDefinition>(), out _));
            Assert.IsFalse(ChapterModeValidator.TryDisagreement(Chapter, GameMode.Bud, null, out _));
        }

        // ------------------------------------------------------------------ the guard
        [Test]
        public void AChapterThatAgreesWithItsLevelsPasses()
        {
            Assert.IsFalse(ChapterModeValidator.TryDisagreement(
                Chapter, GameMode.Bud,
                new List<LevelDefinition> { Bud("t_a"), Bud("t_b") }, out _));

            Assert.IsFalse(ChapterModeValidator.TryDisagreement(
                Chapter, GameMode.Glade,
                new List<LevelDefinition> { Glade("t_a") }, out _));
        }

        [Test]
        public void TheExactMistakeThatShippedIsAnError()
        {
            // Budburst levels in a chapter the manifest never labelled: the default is the classic
            // mode, so an unlabelled entry reads as a glade chapter and nothing else notices.
            Assert.IsTrue(ChapterModeValidator.TryDisagreement(
                Chapter, GameMode.Default,
                new List<LevelDefinition> { Bud("t_a"), Bud("t_b") }, out var issue));

            Assert.AreEqual(LevelIssueSeverity.Error, issue.Severity);
            StringAssert.Contains("bud", issue.Message);
            StringAssert.Contains("glade", issue.Message);
            StringAssert.Contains("Sync Manifest", issue.Message,
                "the message has to name the fix, because the state it describes looks fine "
                + "everywhere else in the pipeline");
        }

        [Test]
        public void AChapterHoldingTwoModesIsAnErrorWhicheverModeItClaims()
        {
            foreach (var claimed in new[] { GameMode.Glade, GameMode.Bud })
            {
                Assert.IsTrue(ChapterModeValidator.TryDisagreement(
                    Chapter, claimed,
                    new List<LevelDefinition> { Glade("t_a"), Bud("t_b") }, out var issue),
                    $"a chapter of two modes claiming '{claimed}' passed");

                Assert.AreEqual(LevelIssueSeverity.Error, issue.Severity);
                StringAssert.Contains("more than one mode", issue.Message);
            }
        }

        [Test]
        public void TheMessageNamesBothModesItFound()
        {
            // Naming only one of them leaves whoever reads it hunting through the chapter for the
            // other, which on a ten-level body is the whole of the work.
            Assert.IsTrue(ChapterModeValidator.TryDisagreement(
                Chapter, GameMode.Glade,
                new List<LevelDefinition> { Glade("t_a"), Glade("t_b"), Bud("t_c") },
                out var issue));

            StringAssert.Contains(GameMode.Glade.Value, issue.Message);
            StringAssert.Contains(GameMode.Bud.Value, issue.Message);
        }
    }
}
