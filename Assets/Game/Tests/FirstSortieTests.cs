using System.Collections.Generic;
using System.IO;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The first sortie (invariant 53h): where the tutorial's finale hands a new player on to,
    /// when the map's chapter lesson may be shown, and the two layouts the sortie draws -
    /// <see cref="FinaleStack"/> on every canvas the game is drawn on and <see cref="GuideDock"/>
    /// on every width. The pictures themselves are <c>render_tutorial.py --finale</c> and
    /// <c>render_loadout.py --guide</c>; this is the arithmetic they draw.
    /// </summary>
    public sealed class FirstSortieTests
    {
        // ------------------------------------------------------------------ the route
        [Test]
        public void APlayedTutorialLeadsToTheLoadoutAndASkippedOneToTheMap()
        {
            Assert.AreEqual(SortieRoute.Loadout, FirstSortie.After(finished: true));
            Assert.AreEqual(SortieRoute.Map, FirstSortie.After(finished: false));
        }

        [Test]
        public void TheChapterLessonWaitsForTheFirstChapterToBeCleared()
        {
            Assert.AreEqual(10, ChapterGateLesson.AfterCleared, "the owner's number (2026-10-26)");

            for (int cleared = 0; cleared < ChapterGateLesson.AfterCleared; cleared++)
                Assert.IsFalse(ChapterGateLesson.Due(cleared), $"offered after {cleared}");

            Assert.IsTrue(ChapterGateLesson.Due(10));
            Assert.IsTrue(ChapterGateLesson.Due(250), "a veteran who never met it still can");
        }

        [Test]
        public void TheMapsLoadoutLessonIsRetiredAndTheChapterLessonIsLive()
        {
            CollectionAssert.Contains(Mechanic.Retired, Mechanic.MapLoadout);
            CollectionAssert.DoesNotContain(Mechanic.All, Mechanic.MapLoadout);
            CollectionAssert.Contains(Mechanic.All, Mechanic.MapChapterGate);
            Assert.AreEqual("map_kit", Mechanic.MapLoadout.Id, "a spent id keeps its spelling for ever");
        }

        // ------------------------------------------------------------------ the finale
        /// <summary>
        /// Safe-area heights from the squarest canvas (<c>HubFoot.ShortestCanvas</c>, less the
        /// tallest notch and home bar any shipping phone reports) to the tallest phone.
        /// </summary>
        static readonly float[] SafeHeights = { 1720f, 1790f, 1890f, 1920f, 2060f, 2160f, 2280f, 2340f, 2400f, 2560f };

        [Test]
        public void TheFinaleClearsTheWardLineOnEveryCanvas()
        {
            foreach (float h in SafeHeights)
            {
                var stack = FinaleStack.For(h, FinaleStack.ShippedAspect);

                Assert.GreaterOrEqual(stack.StackTop, FinaleStack.Top - .01f, $"{h}: hung above the safe area");
                Assert.LessOrEqual(stack.Bottom, h * .5f - FinaleStack.Floor + .01f,
                                   $"{h}: the key reaches the ward line ({stack.Bottom} of {h})");
                Assert.Greater(stack.Scale, .85f, $"{h}: drawn at {stack.Scale:0.00}, too small to read as the same picture");
                Assert.LessOrEqual(stack.Scale, 1f);
            }
        }

        [Test]
        public void TheFinaleIsDrawnAtFullSizeOnEveryPhoneThatHasTheRoom()
        {
            // 16:9 and taller, with nothing in the way: the picture as authored.
            Assert.AreEqual(1f, FinaleStack.For(1920f, FinaleStack.ShippedAspect).Scale, 1e-4f);
            Assert.AreEqual(1f, FinaleStack.For(2400f, FinaleStack.ShippedAspect).Scale, 1e-4f);
        }

        [Test]
        public void TheFinaleStacksTopToBottomWithoutOverlap()
        {
            var stack = FinaleStack.For(2160f, FinaleStack.ShippedAspect);

            float logoBottom = stack.LogoY + stack.LogoH * .5f;
            float lineTop = stack.LineY - FinaleStack.LineH * .5f;
            float lineBottom = stack.LineY + FinaleStack.LineH * .5f;
            float keyTop = stack.KeyY - FinaleStack.KeyH * .5f;

            Assert.LessOrEqual(logoBottom, lineTop);
            Assert.LessOrEqual(lineBottom, keyTop);
            Assert.AreEqual(stack.Height, stack.KeyY + FinaleStack.KeyH * .5f, 1e-3f, "the key is the stack's foot");
        }

        [Test]
        public void TheFinaleFitsTheNarrowestCanvas()
        {
            // The line's box is the widest thing in the stack, and the canvas is never narrower
            // than a phone's 1080 (CanvasFit widens, never narrows).
            Assert.LessOrEqual(FinaleStack.LogoW, Boot.RefWidth - 2f * 60f);
            Assert.LessOrEqual(FinaleStack.LineW, Boot.RefWidth - 2f * 60f);
        }

        [Test]
        public void AMissingWordmarkLaysTheStackOutOnTheShippedShape()
        {
            var shipped = FinaleStack.For(2160f, FinaleStack.ShippedAspect);
            var missing = FinaleStack.For(2160f, 0f);

            Assert.AreEqual(shipped.Height, missing.Height, 1e-3f);
            Assert.AreEqual(shipped.StackTop, missing.StackTop, 1e-3f);
        }

        // ------------------------------------------------------------------ the dock
        static readonly float[] Widths = { 1080f, 1180f, 1350f, 1620f };

        /// <summary>Two lines of the 34-point sentence, and a German third.</summary>
        static readonly float[] Bodies = { 44f, 88f, 132f };

        [Test]
        public void TheBubbleStaysInTheAirTheFigureLeaves()
        {
            foreach (float w in Widths)
            foreach (float body in Bodies)
            {
                var dock = GuideDock.For(w, GuideDock.ShippedAspect, body);

                float figureLeft = dock.FigureCentre.x - dock.FigureSize.x * .5f;
                float bubbleRight = dock.BubbleCentre.x + dock.BubbleSize.x * .5f;
                float bubbleBottom = dock.BubbleCentre.y - dock.BubbleSize.y * .5f;
                float gun = GuideDock.Foot + GuideDock.FigureH * GuideDock.GunLine;

                Assert.LessOrEqual(bubbleRight, figureLeft + dock.FigureSize.x * GuideDock.HeadClear + .01f,
                                   $"{w}/{body}: the bubble reaches past the empty corner into the figure");
                Assert.GreaterOrEqual(bubbleBottom, gun - .01f,
                                      $"{w}/{body}: the bubble comes down over the blaster");
            }
        }

        [Test]
        public void TheDockStaysOnTheDisplay()
        {
            foreach (float w in Widths)
            foreach (float body in Bodies)
            {
                var dock = GuideDock.For(w, GuideDock.ShippedAspect, body);

                float bubbleLeft = dock.BubbleCentre.x - dock.BubbleSize.x * .5f;
                float figureRight = dock.FigureCentre.x + dock.FigureSize.x * .5f;
                float beakRight = dock.BeakCentre.x + GuideDock.Beak * .5f;

                Assert.GreaterOrEqual(bubbleLeft, GuideDock.Left - .01f, $"{w}/{body}: off the left edge");
                Assert.LessOrEqual(figureRight, w - GuideDock.Margin + .01f, $"{w}/{body}: off the right edge");
                Assert.Less(beakRight, figureRight, $"{w}/{body}: the beak points past him");
                Assert.GreaterOrEqual(dock.Band, dock.BubbleCentre.y + dock.BubbleSize.y * .5f,
                                      $"{w}/{body}: the band the shelf clears is shorter than the bubble");
                Assert.GreaterOrEqual(dock.Band, GuideDock.Foot + GuideDock.FigureH);
            }
        }

        [Test]
        public void TheBeakStandsOnTheBubblesStraightEdgeAtTheVisor()
        {
            foreach (float body in Bodies)
            {
                var dock = GuideDock.For(1080f, GuideDock.ShippedAspect, body);

                float bottom = dock.BubbleCentre.y - dock.BubbleSize.y * .5f;
                float top = dock.BubbleCentre.y + dock.BubbleSize.y * .5f;

                Assert.GreaterOrEqual(dock.BeakCentre.y - GuideDock.Beak * .5f, bottom + GuideDock.Corner - .01f);
                Assert.LessOrEqual(dock.BeakCentre.y + GuideDock.Beak * .5f, top - GuideDock.Corner + .01f);

                float visor = GuideDock.Foot + GuideDock.FigureH * GuideDock.Visor;
                Assert.Less(Mathf.Abs(dock.BeakCentre.y - visor), GuideDock.Beak,
                            $"{body}: the beak points {dock.BeakCentre.y - visor} away from his visor");
            }
        }

        [Test]
        public void TheBubbleKeepsItsShapeOnEveryShippingWidth()
        {
            // Anchored to the corner, so a display changing width moves the shelf and not him -
            // which only holds if nothing in the dock depends on the width at shipping sizes.
            var narrow = GuideDock.For(1080f, GuideDock.ShippedAspect, 88f);
            var wide = GuideDock.For(1620f, GuideDock.ShippedAspect, 88f);

            Assert.AreEqual(narrow.BubbleSize, wide.BubbleSize);
            Assert.AreEqual(narrow.Band, wide.Band, 1e-3f);
            Assert.AreEqual(1080f - narrow.FigureCentre.x, 1620f - wide.FigureCentre.x, 1e-3f);
            Assert.AreEqual(1080f - narrow.BubbleCentre.x, 1620f - wide.BubbleCentre.x, 1e-3f);
            Assert.AreEqual(GuideDock.BubbleMaxW, narrow.BubbleSize.x, 1e-3f);
        }

        [Test]
        public void TheBattleKeyIsTheHubsOwn()
        {
            var hub = HubFoot.For(false);
            Assert.AreEqual(hub.PlayW, GuideDock.KeyW);
            Assert.AreEqual(hub.PlayH, GuideDock.KeyH);
            Assert.Less(GuideDock.KeyBand, GuideDock.For(1080f, GuideDock.ShippedAspect, 88f).Band,
                        "the key gives room back to the shelf as the guide leaves");
        }

        // ------------------------------------------------------------------ BATTLE

        /// <summary>
        /// The loadout's BATTLE opens the ladder's first level rather than the map (2026-10-28: ~12%
        /// of v48 installs finished the loadout's lessons and never started a level from the map).
        /// The level is read off the manifest and the save, never named in code (invariant 1).
        /// </summary>
        [Test]
        public void BattleOpensTheFirstLadderLevelReadFromContent()
        {
            PlayerProgress.LoadFrom(new SaveFileDto());
            try
            {
                var index = ShippedIndex();
                var level = FirstSortie.BattleLevel(index);

                Assert.AreEqual(index.FirstIn(index.DefaultMode), level, "BATTLE is not the ladder's first level");
                Assert.AreEqual("s01_firstwatch", level.Value, "the shipped ladder's first level moved; is that intended?");
                Assert.AreEqual(GameTrack.Main, index.TrackOf(level), "BATTLE must open the main ladder, not a lane");

                // Derived, not pinned: a player who somehow already cleared it is sent on, as the hub would.
                PlayerProgress.RecordRun(level, 3, 20);
                Assert.AreNotEqual(level, FirstSortie.BattleLevel(index));
            }
            finally
            {
                PlayerProgress.LoadFrom(new SaveFileDto());
            }

            Assert.IsFalse(FirstSortie.BattleLevel(null).IsValid, "no catalog falls back to the map");
        }

        /// <summary>
        /// The key goes through <c>PlayRoute</c> (the one door into a run, with its heart gate) and
        /// falls back to the map; a skipped tutorial still goes to the map (the finale's route).
        /// </summary>
        [Test]
        public void TheBattleKeyOpensTheRunThroughPlayRoute()
        {
            string source = File.ReadAllText(Path.Combine(TestJson.RepoRoot(),
                "Assets", "Game", "Scripts", "Presentation", "Screens", "LoadoutScreen.Sortie.cs"));
            int at = source.IndexOf("void Battle()", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0);
            string body = source.Substring(at, source.IndexOf('}', at) - at);

            StringAssert.Contains("SortieAnalytics.Track(SortieAnalytics.Battle)", body, "sortie_battle is kept");
            StringAssert.Contains("FirstSortie.BattleLevel(GameContent.Index)", body);
            StringAssert.Contains("PlayRoute.Open(level)", body);
            StringAssert.Contains("Flow.Go<LevelsScreen>()", body, "the fallback is the map");
            Assert.AreEqual(SortieRoute.Map, FirstSortie.After(finished: false), "a skipped tutorial goes to the map");
        }

        static CatalogIndex ShippedIndex()
        {
            var manifest = TestJson.Object(TestJson.Parse(File.ReadAllText(Path.Combine(
                TestJson.RepoRoot(), "Assets", "StreamingAssets", "Content", "manifest.json"))));

            var builder = new CatalogIndexBuilder();
            foreach (var child in TestJson.Children(manifest, "chapters"))
            {
                var row = TestJson.Object(child);
                var levels = new List<string>();
                foreach (var level in TestJson.Children(row, "levels")) levels.Add((string)level);

                builder.Add(new ManifestChapterDto
                {
                    id = TestJson.Str(row, "id"),
                    order = TestJson.Int(row, "order"),
                    version = TestJson.Int(row, "version", 1),
                    disabled = row.TryGetValue("disabled", out var off) && off is bool b && b,
                    minAppVersion = TestJson.Int(row, "minAppVersion"),
                    mode = TestJson.Str(row, "mode"),
                    track = TestJson.Str(row, "track"),
                    levels = levels.ToArray(),
                }, int.MaxValue);
            }
            return builder.Build();
        }

        // ------------------------------------------------------------------ the art
        [Test]
        public void TheShippedArtIsTheShapeTheLayoutsAssume()
        {
            var logo = PngSize(AssetManifest.GameLogo);
            Assert.AreEqual(FinaleStack.ShippedAspect, logo.x / logo.y, 1e-4f,
                            "re-cut the wordmark? update FinaleStack.ShippedAspect");

            foreach (var reel in new[] { AssetManifest.GuideIdle, AssetManifest.GuideWalk })
            {
                var frame = PngSize(reel + "/f00");
                Assert.AreEqual(GuideDock.ShippedAspect, frame.x / frame.y, 1e-4f,
                                $"re-cut {reel}? update GuideDock.ShippedAspect");
            }
        }

        /// <summary>A PNG's size, read off its header: no decoder, so the fixture runs offline.</summary>
        static Vector2 PngSize(string address)
        {
            string path = Path.Combine(TestJson.RepoRoot(), "Assets", "Game",
                                       address.Replace('/', Path.DirectorySeparatorChar) + ".png");
            Assert.IsTrue(File.Exists(path), $"{address} is not on disk");

            var head = new byte[24];
            using (var stream = File.OpenRead(path))
                Assert.AreEqual(24, stream.Read(head, 0, 24));

            int width = (head[16] << 24) | (head[17] << 16) | (head[18] << 8) | head[19];
            int height = (head[20] << 24) | (head[21] << 16) | (head[22] << 8) | head[23];
            return new Vector2(width, height);
        }
    }
}
