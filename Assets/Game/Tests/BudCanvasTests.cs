using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The two things a Budburst grove's <em>layers</em> have to be true about, both of which are
    /// invisible in a compile, a validator and a screenshot alike.
    ///
    /// <para>
    /// <b>This exists because splitting the board into canvases shipped a grove that could not be
    /// tapped.</b> A wave puts up to three hundred transient graphics on the screen and every one
    /// animates its colour, so they were given <c>Canvas</c> layers of their own to keep the
    /// rebuild off the board — which is right, and one layer too many got one. A nested
    /// <c>Canvas</c> is a rebuild boundary <em>and</em> a raycast boundary: <c>Graphic.canvas</c>
    /// resolves to the nearest enabled canvas above it, <c>GraphicRegistry</c> files the graphic
    /// under that one, and a <c>GraphicRaycaster</c> only ever looks up the canvas it is sitting
    /// on. So the moment the field had a canvas, all fifty-six of the grove's hit targets left
    /// the root raycaster's list at once and the mode stopped answering taps — while drawing
    /// perfectly, which is why nothing anywhere caught it.
    /// </para>
    /// <para>
    /// Measured rather than argued: with the field nested, 56 tap targets and <b>0</b> reachable;
    /// without, 56 and 56.
    /// </para>
    /// <para>
    /// It needs the Editor because the subject is Unity's own UI graph, so it runs in the Test
    /// Runner rather than offline — which is the honest place for it. There is no arithmetic here
    /// to lift into Domain: the fact being checked is a fact about <c>GraphicRegistry</c>.
    /// </para>
    /// </summary>
    public sealed class BudCanvasTests
    {
        GameObject _root;
        BudView _view;
        Canvas _canvas;

        [SetUp]
        public void Build()
        {
            _root = new GameObject("BudCanvasProbe", typeof(Canvas), typeof(CanvasScaler),
                                   typeof(GraphicRaycaster));

            _canvas = _root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

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
            Assert.IsTrue(new BudMode().TryRead(pick, LevelId.Parse(pick.id), problems, out var rules),
                          string.Join("; ", problems));

            return ((BudRules)rules).Layout;
        }

        /// <summary>
        /// <b>Every cell the player can tap has to be reachable by a raycaster.</b>
        ///
        /// The check is deliberately stated as the general rule rather than as "the field has no
        /// canvas": the fix could equally have been a <c>GraphicRaycaster</c> beside the nested
        /// one, and a test naming the shape of today's fix would have to be rewritten to allow
        /// tomorrow's while proving nothing more.
        /// </summary>
        [Test]
        public void EveryTapTargetIsReachableByARaycaster()
        {
            int targets = 0, orphaned = 0;
            string first = null;

            foreach (var graphic in _root.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.raycastTarget) continue;

                targets++;

                var canvas = graphic.canvas;
                if (canvas != null && canvas.GetComponent<GraphicRaycaster>() != null) continue;

                orphaned++;
                if (first == null)
                    first = graphic.name + " under "
                          + (canvas == null ? "no canvas at all" : "'" + canvas.name + "'");
            }

            Assert.Greater(targets, 0, "the grove has no tap target on it at all");
            Assert.AreEqual(0, orphaned,
                            $"{orphaned} of {targets} tap targets answer no raycaster — {first}");
        }

        /// <summary>
        /// <b>And a nested layer is still clipped by the grove's own edge.</b>
        ///
        /// The other half of the same trap, and it fires on the opposite mistake:
        /// <c>MaskUtilities.GetRectMaskForClippable</c> walks up looking for a
        /// <c>RectMask2D</c> and <em>stops</em> at the first canvas that overrides sorting. So
        /// turning <c>overrideSorting</c> on — the usual next step after nesting, and the one
        /// that would let a layer's draw order be set explicitly — silently puts every burst
        /// effect back outside the board, which is the thing the clip was tightened to stop.
        /// </summary>
        [Test]
        public void EveryLayerInsideTheGroveIsStillClippedToIt()
        {
            var field = _root.transform.Find("Host/Thicket/Field");
            Assert.IsNotNull(field, "the grove's clip has been renamed or removed");

            var mask = field.GetComponent<RectMask2D>();
            Assert.IsNotNull(mask, "the grove is no longer clipped to itself");

            var buds = field.Find("Buds");
            Assert.IsNotNull(buds, "the grove has no 'Buds' layer");

            var flowers = buds.GetComponentsInChildren<MaskableGraphic>(true);
            Assert.Greater(flowers.Length, 0, "the grove draws nothing");

            foreach (var graphic in flowers)
                Assert.AreSame(mask, MaskUtilities.GetRectMaskForClippable(graphic),
                               $"{graphic.name} is not clipped to the grove");

            // **`Near` is empty until a wave runs**, so what has to be proved about it is that
            // something *put* there would be clipped — which is the whole of what the layer is
            // for and the thing `overrideSorting` would silently undo. A probe rather than an
            // assertion about the canvas's flags, because the flag is the cause and this is the
            // consequence: whatever else changes about how the layer is built, the answer to
            // "would a burst effect leave the grove" stays the question being asked.
            var near = field.Find("Near");
            Assert.IsNotNull(near, "the grove has no 'Near' layer for what a burst throws");

            var probe = new GameObject("Probe", typeof(RectTransform), typeof(Image));
            probe.transform.SetParent(near, false);
            Canvas.ForceUpdateCanvases();

            var clipped = MaskUtilities.GetRectMaskForClippable(probe.GetComponent<Image>());
            Object.DestroyImmediate(probe);

            Assert.AreSame(mask, clipped,
                           "a burst effect on 'Near' would be drawn outside the grove — the "
                         + "usual cause is overrideSorting on a nested canvas, which stops "
                         + "MaskUtilities walking up to the mask");
        }

        /// <summary>
        /// And the clip really is the plate's lip on all four sides, which is what "nothing is
        /// ever drawn off the board" reduces to. It was 1.2 cells at the sides and 1.5 below.
        /// </summary>
        [Test]
        public void TheClipIsTheGrovesOwnEdgeOnEverySide()
        {
            var field = (RectTransform)_root.transform.Find("Host/Thicket/Field");
            var buds = (RectTransform)field.Find("Buds");

            float sides = (field.rect.width - buds.rect.width) * .5f;
            float ends = (field.rect.height - buds.rect.height) * .5f;

            Assert.AreEqual(sides, ends, 1f, "the clip is not the same margin on every side");
            Assert.Less(sides, 20f, $"the clip stands {sides:0}px off the board");
            Assert.AreEqual(0f, field.anchoredPosition.y, .5f, "the clip is not centred on the board");
        }
    }
}
