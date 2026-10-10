using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// A panel's readout stacked over the screen's (<see cref="ResourceSlots.RegisterOver"/>): the
    /// gem shelf's purse takes the payout while it is up, and the screen's own pill is the target
    /// again the moment the panel goes - however it goes - rather than being overwritten for the rest
    /// of the visit, which is what a plain <see cref="ResourceSlots.Register"/> would have done.
    /// And the receipt queue that panel waits on starts empty.
    /// </summary>
    public sealed class ResourceSlotStackTests
    {
        static (GameObject root, RectTransform icon, Text number) Pill(string name)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var icon = new GameObject("Icon", typeof(RectTransform)).GetComponent<RectTransform>();
            icon.SetParent(root.transform, false);
            var number = new GameObject("V", typeof(RectTransform)).AddComponent<Text>();
            number.transform.SetParent(root.transform, false);
            return (root, icon, number);
        }

        [Test]
        public void APanelsPurseTakesThePayoutAndHandsItBack()
        {
            var screen = Pill("Screen");
            var panel = Pill("Panel");
            try
            {
                ResourceSlots.Register(ResourceSlots.Kind.Gems, screen.icon, screen.number, null, Color.white, n => n.ToString());
                ResourceSlots.RegisterOver(panel.root.transform, ResourceSlots.Kind.Gems, panel.icon, panel.number, null,
                                           Color.white, n => n.ToString());

                Assert.IsTrue(ResourceSlots.TryGet(ResourceSlots.Kind.Gems, out var top));
                Assert.AreSame(panel.number, top.Number, "the panel's purse is where a payout lands while it is up");

                // A screen rebuilt under the panel still loses to it.
                ResourceSlots.Register(ResourceSlots.Kind.Gems, screen.icon, screen.number, null, Color.white, n => n.ToString());
                Assert.IsTrue(ResourceSlots.TryGet(ResourceSlots.Kind.Gems, out top));
                Assert.AreSame(panel.number, top.Number);

                // The panel goes (edit mode runs no OnDestroy, so this is the sweep's path; play mode
                // takes the lease's): the screen's pill is the target again.
                Object.DestroyImmediate(panel.root);
                Assert.IsTrue(ResourceSlots.TryGet(ResourceSlots.Kind.Gems, out var back));
                Assert.AreSame(screen.number, back.Number, "the screen's own pill, not an orphan");
            }
            finally
            {
                if (panel.root) Object.DestroyImmediate(panel.root);
                Object.DestroyImmediate(screen.root);
            }
        }

        [Test]
        public void TheReceiptQueueStartsEmpty()
        {
            ReceiptQueue.Reset();
            Assert.IsFalse(ReceiptQueue.Pending, "a panel waiting on receipts closes at once when none is due");
        }
    }
}
