using System.Collections.Generic;
using System.IO;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    public sealed class SiegeIntroductionTests
    {
        static readonly string[] Rows =
        {
            "brbrgbgg", "rrggbbrg", "bbgrrggr", "gbrbgrbr", "rggrrbgb",
        };

        static SiegeLayout Layout(bool introduction)
        {
            Assert.IsTrue(ProtoGrid.TryRead(Rows, 8, 5, SiegeLayout.Cells,
                                            out var grid, out string error), error);
            return new SiegeLayout(grid, "rgb", "rgb", new[] { "rgbrgb", "rgbrgbrgb" },
                                   "", introduction: introduction);
        }

        [Test]
        public void AnUnattendedOpeningCannotKillTheLineOrAwardAVictory()
        {
            var board = SiegeBoard.Build(Layout(true));
            for (int i = 0; i < 60 * 60 * 5; i++) board.Advance(1f / 60f);

            Assert.AreEqual(3, board.WardsStanding);
            Assert.Greater(board.GoalsLeft, 0, "the player must still defeat the raiders");
            foreach (var ward in board.Wards)
                Assert.GreaterOrEqual(ward.Health, ward.Full * .75f);
        }

        [Test]
        public void TheSameUnprotectedEncounterStillDefeatsAnIdlePlayer()
        {
            var board = SiegeBoard.Build(Layout(false));
            for (int i = 0; i < 60 * 120 && board.WardsStanding > 0; i++) board.Advance(1f / 60f);
            Assert.AreEqual(0, board.WardsStanding, "ordinary combat must retain its fail state");
        }

        [Test]
        public void APlayerCanReturnAfterALongPauseAndWinByMatching()
        {
            var board = SiegeBoard.Build(Layout(true));
            for (int i = 0; i < 60 * 90; i++) board.Advance(1f / 60f);
            for (int i = 0; i < 60 * 180 && board.GoalsLeft > 0; i++)
            {
                if (i % 180 == 0)
                {
                    var swap = board.FindSwap(0);
                    Assert.IsTrue(swap.Found, "the introduction must remain playable");
                    Assert.IsNotNull(board.Swap(swap.A, swap.B));
                }
                board.Advance(1f / 60f);
            }
            Assert.AreEqual(0, board.GoalsLeft);
            Assert.AreEqual(3, board.WardsStanding);
        }

        [Test]
        public void OpeningDamageIsBoundedAndNeverHealsAnAlreadyWoundedWard()
        {
            Assert.AreEqual(1, SiegeIntroduction.Damage(14, 14, 500));
            Assert.AreEqual(0, SiegeIntroduction.Damage(11, 14, 500));
            Assert.AreEqual(4, SiegeIntroduction.HealthAfter(4, 14, 500));
            Assert.AreEqual(14, SiegeIntroduction.HealthAfter(14, 14, -5));
            Assert.AreEqual(1, SiegeIntroduction.HealthAfter(1, 1, int.MaxValue));
        }

        [Test]
        public void OnlyFirstwatchOptsIntoTheSceneInShippedContent()
        {
            var folder = Path.Combine(Application.dataPath, "StreamingAssets", "Content", "chapters");
            var optedIn = new List<string>();
            foreach (var path in Directory.GetFiles(folder, "*.json"))
            {
                var problems = new List<string>();
                Assert.IsTrue(ContentMapper.TryReadChapter(File.ReadAllText(path), problems,
                                                           out var chapter), string.Join("\n", problems));
                foreach (var level in chapter.Levels)
                {
                    if (!(level.Rules is SiegeRules rules)) continue;
                    var layout = rules.Layout;
                    if (layout.Introduction) optedIn.Add(level.Id.Value);
                }
            }
            CollectionAssert.AreEqual(new[] { "s01_firstwatch" }, optedIn);
        }

        [Test]
        public void AnIntroductionCannotBeAttachedToAnotherLevel()
        {
            var dto = new LevelDto { siege = new SiegeDto { introduction = true } };
            var problems = new List<string>();
            Assert.IsFalse(new SiegeMode().TryRead(dto, LevelId.Parse("s01_ironward"), problems, out _));
            Assert.IsNotEmpty(problems);
        }
    }
}
