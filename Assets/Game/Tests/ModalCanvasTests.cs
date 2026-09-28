using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every modal is drawn by a canvas of its own (<c>Flow.Isolate</c>).
    ///
    /// <para>
    /// <b>The fault this holds</b> is the loadout shelf flickering behind the utility panel on
    /// every tap of its stepper: the panel and the screen shared one canvas, so each change on
    /// the panel re-meshed the whole page under it. The three clauses below are the three ways
    /// the fix can be undone without anything else noticing - no canvas (the flicker is back),
    /// a sorting override (the panel leaves the hierarchy's draw order and <c>Restack</c> stops
    /// deciding what is on top), and no raycaster (every control on every panel goes dead,
    /// because a nested canvas's graphics are invisible to the root's raycaster).
    /// </para>
    /// </summary>
    public sealed class ModalCanvasTests
    {
        GameObject _root;
        Canvas _was;

        [SetUp]
        public void SetUp()
        {
            _was = Flow.Canvas;

            _root = new GameObject("Root", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1
                                            | AdditionalCanvasShaderChannels.Normal;

            Flow.Canvas = canvas;
        }

        [TearDown]
        public void TearDown()
        {
            Flow.Canvas = _was;
            if (_root) Object.DestroyImmediate(_root);
        }

        RectTransform Panel()
        {
            var node = new GameObject("Panel", typeof(RectTransform));
            node.transform.SetParent(_root.transform, false);
            return (RectTransform)node.transform;
        }

        [Test]
        public void AModalIsARebuildBoundary()
        {
            var node = Panel();
            Flow.Isolate(node);

            var canvas = node.GetComponent<Canvas>();
            Assert.IsNotNull(canvas, "the panel shares the screen's canvas, so every change on it re-meshes the page behind");
            Assert.IsFalse(canvas.isRootCanvas, "the panel's canvas is not nested under the game's");
        }

        [Test]
        public void AModalKeepsTheHierarchysDrawOrder()
        {
            var node = Panel();
            Flow.Isolate(node);

            Assert.IsFalse(node.GetComponent<Canvas>().overrideSorting,
                           "a sorting override takes the panel out of the order Restack decides");
        }

        [Test]
        public void AModalCanBeTapped()
        {
            var node = Panel();
            Flow.Isolate(node);

            Assert.IsNotNull(node.GetComponent<GraphicRaycaster>(),
                             "a nested canvas with no raycaster of its own answers no tap at all");
        }

        [Test]
        public void AModalDrawsWithTheRootsShaderChannels()
        {
            var node = Panel();
            Flow.Isolate(node);

            Assert.AreEqual(Flow.Canvas.additionalShaderChannels,
                            node.GetComponent<Canvas>().additionalShaderChannels);
        }
    }
}
