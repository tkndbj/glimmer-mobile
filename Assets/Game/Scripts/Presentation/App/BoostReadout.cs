using System;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The XP boost's clock, on the map: a green arrow, the letters XP, and how long is left.
    ///
    /// <para>
    /// <b>A readout, which is to say watched rather than drawn</b> — invariant 44j's rule, and it
    /// bites twice here. A boost window opens and closes while this screen is standing (a video
    /// watched from the shop, a purchase syncing in from another device), and the number itself
    /// moves every second whether anything happens or not. Built as a snapshot it would be a
    /// photograph: correct when the map opened and quietly wrong from then on, which compiles,
    /// draws and passes every fixture, because nothing moves during a test.
    /// </para>
    /// <para>
    /// So it does both: it subscribes to <see cref="XpBoost.Changed"/> for the state, and ticks
    /// its own caption for the clock. The two are separate on purpose — a tick that also
    /// re-read the state would make the subscription pointless, and a subscription without a
    /// tick would freeze the countdown at whatever it said when the window opened.
    /// </para>
    /// <para>
    /// <b>It shows nothing at all when no boost is running.</b> An empty clock is a control that
    /// answers no question, and this sits under the back key on a painted map where every pixel
    /// is somebody's artwork.
    /// </para>
    /// </summary>
    public sealed class BoostReadout : MonoBehaviour
    {
        /// <summary>
        /// Builds the readout under <paramref name="parent"/> and watches it for as long as it
        /// lives.
        ///
        /// <para>
        /// <b>Safe to call more than once</b>, for <c>WalletWatch.Attach</c>'s reason: a screen
        /// that rebuilds its chrome calls this again from the same place, and a second component
        /// would be a second subscription drawing the same clock twice.
        /// </para>
        /// </summary>
        public static BoostReadout Attach(Component host, RectTransform parent,
                                          Vector2 anchor, Vector2 position)
        {
            if (host == null || parent == null) return null;

            var readout = host.GetComponent<BoostReadout>();
            if (readout != null) { readout.Repaint(); return readout; }

            readout = host.gameObject.AddComponent<BoostReadout>();
            readout.Build(parent, anchor, position);

            // AddComponent has already run OnEnable against a null root, so the subscription is
            // standing and this is the paint that matters.
            readout.Hook();
            readout.Repaint();

            return readout;
        }

        // ------------------------------------------------------------------ the parts
        RectTransform _root;
        Text _clock;

        bool _hooked;
        long _shownAt = -1;

        /// <summary>
        /// The whole block, laid out from its own top so the two lines cannot drift apart.
        ///
        /// The arrow and the letters share a line and the clock sits under it, which is the
        /// arrangement asked for. Both lines are centred on the same x as the back key above,
        /// so the three read as one column down the corner rather than as three placements.
        /// </summary>
        void Build(RectTransform parent, Vector2 anchor, Vector2 position)
        {
            _root = UIKit.Box("BoostReadout", parent, new Vector2(Width, Height), anchor, position);

            var mark = UIKit.Img("Mark", _root, Art.S("Ui/ic_boost_up"), Color.white,
                                 Vector2.one * MarkSize, new Vector2(.5f, 1f),
                                 new Vector2(-(LabelWidth * .5f), -MarkSize * .5f));
            mark.preserveAspect = true;
            mark.raycastTarget = false;

            var label = UIKit.Titled("Xp", _root, "XP", 34, Green, TextAnchor.MiddleLeft,
                                     new Vector2(LabelWidth, MarkSize), new Vector2(.5f, 1f),
                                     new Vector2(MarkSize * .5f + 6f, -MarkSize * .5f),
                                     outline: 0f, shadow: 2f);
            label.raycastTarget = false;

            _clock = UIKit.Titled("Clock", _root, string.Empty, 30, Pal.Cream,
                                  TextAnchor.MiddleCenter,
                                  new Vector2(Width, ClockHeight), new Vector2(.5f, 1f),
                                  new Vector2(0f, -(MarkSize + ClockHeight * .5f + 2f)),
                                  outline: 0f, shadow: 2f);
            _clock.raycastTarget = false;
        }

        // ------------------------------------------------------------------ the watch
        void OnEnable() { Hook(); Repaint(); }

        void OnDisable()
        {
            if (!_hooked) return;

            XpBoost.Changed -= Repaint;
            _hooked = false;
        }

        void Hook()
        {
            if (_hooked) return;

            XpBoost.Changed += Repaint;
            _hooked = true;
        }

        /// <summary>
        /// The clock, once a second and no oftener.
        ///
        /// <b>Throttled on the figure rather than on a timer</b>: the caption only changes when
        /// the whole second it prints changes, so comparing the seconds-left is both the cheapest
        /// test and the one that cannot drift out of step with what is on screen. Rebuilding a
        /// string every frame for a number that moves once a minute is what makes a map stutter.
        /// </summary>
        void Update()
        {
            if (_root == null || !_root.gameObject.activeSelf) return;

            long left = XpBoost.SecondsLeft;
            if (left == _shownAt) return;

            // The window closing is a state change rather than a tick, and the subscription does
            // not fire for it — nothing *happened*, time merely passed. So the tick has to be
            // the thing that takes the readout down.
            if (left <= 0L) { Repaint(); return; }

            _shownAt = left;
            if (_clock != null) _clock.text = Profile.Countdown(left);
        }

        /// <summary>
        /// Draws the state: whether there is a boost at all, and what its clock says.
        ///
        /// Raised by the subscription, so it must be cheap and must never assume it is being
        /// called because something changed — a merge landing repaints every readout in the game.
        /// </summary>
        void Repaint()
        {
            if (_root == null) return;

            long left = XpBoost.SecondsLeft;
            bool running = left > 0L;

            if (_root.gameObject.activeSelf != running) _root.gameObject.SetActive(running);
            if (!running) { _shownAt = -1; return; }

            _shownAt = left;
            if (_clock != null) _clock.text = Profile.Countdown(left);
        }

        // ------------------------------------------------------------------ the layout
        /// <summary>
        /// Written down rather than scattered, for <c>ShopOverlays</c>'s reason: absolute offsets
        /// inside a build method are how two rows come to print through one another.
        /// </summary>
        const float MarkSize = 46f, LabelWidth = 48f, ClockHeight = 34f;
        const float Width = MarkSize + LabelWidth, Height = MarkSize + ClockHeight + 2f;

        /// <summary>
        /// The arrow's own green, so the letters and the mark read as one object.
        ///
        /// Taken from the cut art rather than from <c>Pal</c>, because the arrow is a hue turn of
        /// a bought sprite (<c>Tools/make_boost_icon.py</c>) and the palette's mint is a different
        /// green — two greens an inch apart read as a mistake where one reads as a decision.
        /// </summary>
        static readonly Color Green = new Color(.57f, .89f, .48f);
    }
}
