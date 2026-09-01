using System.Collections.Generic;
using System.Reflection;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// <b>A piece that has fallen out of a cell leaves nothing standing in it.</b>
    ///
    /// <para>
    /// This is the fixture for the fault reported as <em>"the flowers at top stay still, and new
    /// flowers fall through them"</em> — and the flower falling through was that same flower. A
    /// fall is drawn by painting the arriving piece into the cell it is falling <em>into</em> and
    /// offsetting it up to where it came from, which is the right shape and means the departing
    /// cell has to stop drawing the piece in the same breath. It never did.
    /// </para>
    /// <para>
    /// <b>Nothing else here could have caught it.</b> The model is exactly right — every drop has
    /// carried the cell it came from since the grove learned to fall — and <c>Run.Board</c> is the
    /// position the whole chain <em>ends</em> in, so no question asked of the board can answer
    /// "this square's flower is in mid-air". The board settles, par is right, every gate is green,
    /// and only the drawing is wrong. So it runs in the Editor, over the real score of a real tap
    /// on the shipped finale, for <see cref="BudCanvasTests"/>' reason: the subject is what a
    /// <c>BudView</c> draws.
    /// </para>
    /// <para>
    /// The ordering that makes the fix safe — a cell is emptied as a source before it is filled as
    /// a destination, which within a column is routinely the same square — lives in
    /// <c>BudStageTests.ACellIsEmptiedBeforeAnythingIsPaintedIntoIt</c>, because it is a fact
    /// about the score rather than about the view.
    /// </para>
    /// </summary>
    public sealed class BudFallTests
    {
        GameObject _root;
        BudView _view;

        [SetUp]
        public void Build()
        {
            _root = new GameObject("BudFallProbe", typeof(Canvas), typeof(CanvasScaler),
                                   typeof(GraphicRaycaster));

            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0f;

            var host = (RectTransform)new GameObject("Host", typeof(RectTransform)).transform;
            host.SetParent(_root.transform, false);
            host.anchorMin = Vector2.zero;
            host.anchorMax = Vector2.one;
            host.offsetMin = Vector2.zero;
            host.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();

            _view = new GameObject("BudView").AddComponent<BudView>();
            _view.transform.SetParent(_root.transform, false);
            _view.Begin(host, Grove(), 8);
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void Drop()
        {
            if (_root) Object.DestroyImmediate(_root);
        }

        // ------------------------------------------------------------------ the rule
        /// <summary>
        /// Every fall of the finale's best opening tap — eight waves and twenty-seven flowers —
        /// dealt to the real view in score order, and after each one the square the piece came
        /// out of draws nothing at all.
        ///
        /// <para>
        /// Only the falls are dealt, which is enough and is honest: a burst leaves bare ground and
        /// bare ground is never a source, so nothing a <c>Split</c> would have done can change the
        /// answer — and dealing one would put three hundred transient graphics on a canvas this
        /// fixture has no reason to build.
        /// </para>
        /// <para>
        /// It was watched failing against the code that shipped, at the first fall of the first
        /// wave, which is the standing requirement of a check here.
        /// </para>
        /// </summary>
        [Test]
        public void NothingIsLeftStandingInACellItsPieceHasFallenOutOf()
        {
            var score = Play(out int waves);
            int falls = 0;

            foreach (var cue in score.Cues)
            {
                if (cue.Kind != BudCueKind.Fall) continue;

                Call("Fall", cue);
                falls++;

                if (cue.From < 0) continue;

                Assert.Less(Alpha(cue.From, "Bud"), .01f,
                            $"cell {cue.From} is still drawing the flower that fell out of it into "
                            + $"{cue.Cell} — the board shows it standing still while a copy of it "
                            + "comes down through the grove");
                Assert.Less(Alpha(cue.From, "Halo"), .01f,
                            $"cell {cue.From} still draws the heart of a flower that has left");
                Assert.Less(Alpha(cue.From, "Pod"), .01f,
                            $"cell {cue.From} still draws the cocoon that fell out of it");
                Assert.Less(Alpha(cue.From, "Critter"), .01f,
                            $"cell {cue.From} still draws the critter that fell out of it");
            }

            Assert.Greater(falls, 20,
                           $"the tap this is held over ran {waves} waves and moved {falls} pieces, "
                           + "which is too little of a grove for it to say anything");
        }

        /// <summary>
        /// <b>And a square is emptied to <em>rest</em>, not merely out of sight.</b>
        ///
        /// <para>
        /// <c>BudRestTests</c>' lesson, owed by anything in this file that hides a picture:
        /// <c>Tween.Breathe</c> captures whatever scale it finds as the size to breathe around,
        /// for ever, so a flower hidden mid-swell is a size the <em>next</em> piece painted into
        /// that square inherits. Hiding alone would leave that waiting to be found.
        /// </para>
        /// </summary>
        [Test]
        public void AndTheSquareItLeavesIsPutBackToRestRatherThanJustHidden()
        {
            int cell = FirstFlower();
            var bud = Picture(cell, "Bud");

            // Standing exactly where a wind-up cut off half way leaves one.
            bud.transform.localScale = Vector3.one * 2.27f;
            bud.transform.localRotation = Quaternion.Euler(0, 0, 28f);

            Call("EmptyCell", cell);

            Assert.AreEqual(1f, bud.transform.localScale.x, .001f,
                            "the emptied square kept the size the departing flower had reached");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, bud.transform.localEulerAngles.z), .5f,
                            "the emptied square kept the angle the departing flower had reached");
            Assert.Less(bud.color.a, .01f, "the emptied square is still drawing a flower");
        }

        // ------------------------------------------------------------------ driving it
        /// <summary>
        /// The view's own grove tapped where it goes off hardest, and the score that tap writes.
        ///
        /// The tap runs through <c>BudView.Run</c> rather than through a board of this fixture's
        /// own, because <c>Fall</c> paints what the <em>view's</em> model holds — a score played
        /// against a board the view has never seen would prove nothing about either.
        /// </summary>
        BudScore Play(out int waves)
        {
            var run = _view.Run;
            var layout = run.Layout;

            int best = -1, deepest = 0;
            for (int i = 0; i < layout.Count; i++)
            {
                if (!run.CanTap(i)) continue;

                var probe = run.Preview(i);
                if (probe.Waves <= deepest) continue;

                deepest = probe.Waves;
                best = i;
            }

            Assert.GreaterOrEqual(best, 0, "the finale has no legal opening tap");

            var pulses = new List<BudPulse>();
            var washes = new List<BudWash>();
            var drops = new List<BudDrop>();

            var chain = run.Tap(best, pulses, washes, drops);
            waves = chain.Waves;

            return BudStage.Of(chain.Waves, pulses.ToArray(), washes.ToArray(), drops.ToArray(),
                               layout.Width);
        }

        object Call(string name, params object[] args)
        {
            var m = typeof(BudView).GetMethod(name, BindingFlags.NonPublic
                                                    | BindingFlags.Instance);
            Assert.IsNotNull(m, "BudView." + name + " has gone");

            return m.Invoke(_view, args);
        }

        object CellOf(int index)
        {
            var f = typeof(BudView).GetField("_cells", BindingFlags.NonPublic
                                                       | BindingFlags.Instance);
            Assert.IsNotNull(f, "BudView._cells has gone");

            var cells = (System.Array)f.GetValue(_view);
            return cells.GetValue(index);
        }

        Image Picture(int index, string part)
        {
            var cell = CellOf(index);
            var f = cell.GetType().GetField(part);
            Assert.IsNotNull(f, "Cell." + part + " has gone");

            return (Image)f.GetValue(cell);
        }

        /// <summary>How opaque one of a cell's pictures is, or nothing where it has none.</summary>
        float Alpha(int index, string part)
        {
            var image = Picture(index, part);
            return image == null ? 0f : image.color.a;
        }

        int FirstFlower()
        {
            var board = _view.Run.Board;
            for (int i = 0; i < board.Count; i++) if (board.IsFlower(i)) return i;

            Assert.Fail("the grove holds no flowers at all");
            return -1;
        }

        /// <summary>The deepest shipped grove, so the busiest layout this mode has.</summary>
        static BudLayout Grove()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath,
                                                 "Content/chapters/b01_thicket.json");
            Assert.IsTrue(System.IO.File.Exists(path), "the Thicket is not where it should be");

            var chapter = JsonUtility.FromJson<ChapterDto>(System.IO.File.ReadAllText(path));

            LevelDto pick = null;
            foreach (var level in chapter.levels)
                if (level.id == "b01_thicketheart") pick = level;

            Assert.IsNotNull(pick, "b01_thicketheart has gone");

            var problems = new List<string>();
            Assert.IsTrue(new BudMode().TryRead(pick, LevelId.Parse(pick.id), problems,
                                                out var rules),
                          string.Join("; ", problems));

            return ((BudRules)rules).Layout;
        }
    }
}
