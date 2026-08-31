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
    /// <b>A flower is left at the size the board chose for it, never at one somebody else was
    /// still in the middle of.</b>
    ///
    /// <para>
    /// This is the fixture for the fault reported as <em>"some of the falling flowers get
    /// huge"</em>, and it is the one shape no arithmetic can hold: the whole of it is which
    /// <c>UnityEngine.Object</c> owns which tween and what a killed tween leaves behind, so it
    /// runs in the Editor for <see cref="BudCanvasTests"/>' reason rather than offline.
    /// </para>
    /// <para>
    /// <b>What went wrong.</b> <c>ThrowFlower</c> animated the cell's own flower on its way out,
    /// growing it to as much as 2.7 times its size and putting it back only in <c>OnDone</c>.
    /// A fall into that cell is dealt <see cref="BudTempo.Hold"/> after the burst that made it
    /// while a throw runs for <see cref="BudTempo.Burst"/> — half as long again — so
    /// <c>PaintCell</c> cut every throw off half way and left the bud at <b>2.27 wide and 28°
    /// round</b>. Most colours were then set back to one; a <b>white</b> one got
    /// <c>Tween.Breathe</c>, which captures whatever scale it finds as the size to breathe
    /// <em>around</em> — so that flower stayed huge for the rest of the run.
    /// </para>
    /// <para>
    /// Both halves are held here, and both were watched failing against the code that shipped.
    /// </para>
    /// </summary>
    public sealed class BudRestTests
    {
        GameObject _root;
        BudView _view;
        RectTransform _host;

        [SetUp]
        public void Build()
        {
            _root = new GameObject("BudRestProbe", typeof(Canvas), typeof(CanvasScaler),
                                   typeof(GraphicRaycaster));

            _root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 2340);
            scaler.matchWidthOrHeight = 0f;

            _host = (RectTransform)new GameObject("Host", typeof(RectTransform)).transform;
            _host.SetParent(_root.transform, false);
            _host.anchorMin = Vector2.zero;
            _host.anchorMax = Vector2.one;
            _host.offsetMin = Vector2.zero;
            _host.offsetMax = Vector2.zero;
            Canvas.ForceUpdateCanvases();

            _view = new GameObject("BudView").AddComponent<BudView>();
            _view.transform.SetParent(_root.transform, false);
            _view.Begin(_host, Grove(), 8);
            Canvas.ForceUpdateCanvases();
        }

        [TearDown]
        public void Drop()
        {
            if (_root) Object.DestroyImmediate(_root);
        }

        // ------------------------------------------------------------------ the two rules
        /// <summary>
        /// A flower thrown out of a cell that is then repainted mid-throw leaves nothing behind:
        /// the cell's own flower is at rest, square and unrotated, whatever the throw was doing.
        /// </summary>
        [Test]
        public void AFlowerInterruptedOnItsWayOutLeavesTheCellAtRest()
        {
            int cell = FirstFlower();
            var bud = Bud(cell);

            // Thrown at the size a wind-up leaves behind, which is the worst case there is.
            Call("ThrowFlower", cell, CellOf(cell), Color.white, 1f + BudTempo.SwellMost, 0f);

            // Half way through it, which is exactly where a fall into this cell arrives.
            Frames(BudTempo.Burst * .5f);

            // And the fall lands, repainting the cell under it.
            Call("PaintCell", cell, true);
            Frames(.05f);

            Assert.AreEqual(1f, bud.transform.localScale.x, .01f,
                            "the cell's flower was left at the size the departing one had reached");
            Assert.AreEqual(1f, bud.transform.localScale.y, .01f,
                            "the cell's flower was left at the size the departing one had reached");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, bud.transform.localEulerAngles.z), .5f,
                            "the cell's flower was left leaning");
        }

        /// <summary>
        /// <b>And a repaint puts the flower back to rest before it draws anything on it, however
        /// it was left.</b>
        ///
        /// <para>
        /// This is the half that made the fault permanent rather than momentary.
        /// <c>Tween.Breathe</c> reads its target's scale once and oscillates around it for ever,
        /// so a white flower repainted while something else was still growing it kept that size
        /// for the rest of the run — and <c>Tween.KillAll(cell.Bud)</c> could never have stopped
        /// the breathe anyway, because a breathe is owned by the <c>Transform</c> and that call
        /// names the <c>Image</c>.
        /// </para>
        /// <para>
        /// Stated over <em>any</em> flower rather than over a white one, deliberately: the board
        /// this fixture loads deals no white, and a rule that could only be checked on a grove
        /// that happens to grow one is a rule that stops being checked the next time a chapter is
        /// authored. The rotation is held here too — it is the half nothing ever put back, so a
        /// flower interrupted mid-throw stayed leaning for the rest of the run.
        /// </para>
        /// </summary>
        [Test]
        public void AndARepaintPutsTheFlowerBackToRestHoweverItWasLeft()
        {
            int cell = FirstFlower();
            var bud = Bud(cell);

            // Standing exactly where an interrupted throw used to leave one.
            bud.transform.localScale = Vector3.one * 2.27f;
            bud.transform.localRotation = Quaternion.Euler(0, 0, 28f);

            Call("PaintCell", cell, true);

            // Long enough for a breath to have gone round more than once, so a flower breathing
            // around the wrong size has nowhere to hide.
            for (int i = 0; i < 4; i++)
            {
                Frames(.75f);

                Assert.Less(bud.transform.localScale.x, 1.20f,
                            $"the flower settled at {bud.transform.localScale.x:0.00} rather than "
                            + "at its own size");
                Assert.Greater(bud.transform.localScale.x, .80f,
                               "the flower settled at something smaller than itself");
                Assert.AreEqual(0f, Mathf.DeltaAngle(0f, bud.transform.localEulerAngles.z), .5f,
                                "the flower was left leaning by a repaint that only set a scale");
            }
        }

        // ------------------------------------------------------------------ driving it
        /// <summary>
        /// Runs the tween engine for a while, a sixtieth of a second at a time.
        ///
        /// <c>Tween.Tick</c> is public and the engine instantiates outside play mode, which is
        /// what makes a motion rule provable here at all — a <c>MonoBehaviour</c> does not pump
        /// its own <c>Update</c> in the Editor.
        /// </summary>
        static void Frames(float seconds)
        {
            const float Step = 1f / 60f;
            for (float t = 0f; t < seconds; t += Step) Tween.Inst.Tick(Step, Step);
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

        Image Bud(int index)
        {
            var cell = CellOf(index);
            var f = cell.GetType().GetField("Bud");
            Assert.IsNotNull(f, "Cell.Bud has gone");

            var bud = (Image)f.GetValue(cell);
            Assert.IsNotNull(bud, "cell " + index + " has no flower");

            return bud;
        }

        /// <summary>The first cell of the grove that holds a flower, whatever the board is.</summary>
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
