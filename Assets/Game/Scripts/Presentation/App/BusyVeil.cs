using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// "This screen is still fetching something." One small plate, a turning arc, a line of
    /// text, and — once there is a figure worth showing — how far through it is.
    ///
    /// <para>
    /// <b>Why a widget rather than a sentence on each screen.</b> Three screens in the grove
    /// alone wait on something before they can draw: the Grovement waits on a body and then on
    /// its art, a visit waits on a network round trip and then on art, and the shop waits on an
    /// atlas. Each of those had its own answer, and every one of them was the same answer —
    /// a caption that says "loading" and then does not change for as long as it takes. The
    /// player cannot tell that from a screen that has stopped, which is the one thing a wait
    /// has to be distinguishable from.
    /// </para>
    /// <para>
    /// <b>It appears late on purpose.</b> A load that finishes inside <see cref="AppearAfter"/>
    /// shows nothing at all — a spinner that flashes for two frames reads as a stutter, and it
    /// makes a fast screen feel slower than a silent one. So the plate is built hidden and only
    /// fades in if the wait turns out to be a wait.
    /// </para>
    /// <para>
    /// <b>It never blocks input, and that is a rule rather than a default.</b> A veil over a
    /// load that hangs would take the back arrow with it, which turns a slow fetch into a
    /// trapped player; and the Grovement's floor is panned with a thumb that must keep working
    /// while the pieces arrive. Everything here has <c>raycastTarget</c> off and the group does
    /// not block, so the screen underneath is live throughout.
    /// </para>
    /// <para>
    /// <b>On the unscaled clock</b>, like every other animation in this project: a modal takes
    /// <c>Time.timeScale</c> to nought (invariant 30h) and so does the ads plugin's Editor
    /// consent stub, and a spinner that stops turning is worse than no spinner.
    /// </para>
    /// </summary>
    public sealed class BusyVeil : MonoBehaviour, IProgress<float>
    {
        /// <summary>How long a wait has to last before it is worth saying anything about.</summary>
        public const float AppearAfter = .35f;

        const float FadeIn = .22f, FadeOut = .18f, SpinPeriod = 1.15f;
        const float PlateWidth = 320f, PlateHeight = 150f;
        const float BarWidth = 208f, BarHeight = 8f;

        CanvasGroup _group;
        Text _caption;
        RectTransform _fill;
        GameObject _bar;

        /// <summary>
        /// The last figure reported, or -1 while there is none.
        ///
        /// <para>
        /// Written by <see cref="Report"/> and read in <see cref="Update"/> rather than applied
        /// where it lands. <c>IProgress&lt;T&gt;</c> promises callers nothing about which thread
        /// they are on, and touching a <c>RectTransform</c> off the main one is a crash rather
        /// than a wrong number — a single float is the whole of what has to cross.
        /// </para>
        /// </summary>
        volatile float _fraction = -1f;
        float _shown = -1f;

        float _waited;
        bool _appeared, _finished;

        /// <summary>
        /// Puts one on a screen. Returns it so the caller can say what it is waiting for and,
        /// when it arrives, that it has.
        /// </summary>
        public static BusyVeil Attach(RectTransform host, string message = null)
        {
            if (host == null) return null;

            var root = UIKit.Node("Busy", host);
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(.5f, .5f);
            root.sizeDelta = new Vector2(PlateWidth, PlateHeight);
            root.anchoredPosition = Vector2.zero;

            var veil = root.gameObject.AddComponent<BusyVeil>();
            veil.BuildInto(root, message);
            return veil;
        }

        void BuildInto(RectTransform root, string message)
        {
            _group = UIKit.Group(root);
            _group.alpha = 0f;

            // Never takes a tap, never takes a drag. See the type's remarks.
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var plate = UIKit.Img("Plate", root, Art.Round(28), new Color(.04f, .08f, .12f, .78f),
                                  new Vector2(PlateWidth, PlateHeight),
                                  new Vector2(.5f, .5f), Vector2.zero);
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = false;

            var arc = UIKit.Img("Arc", root, Art.Arc(96, 9f), Pal.Cream,
                                new Vector2(56f, 56f), new Vector2(.5f, 1f), new Vector2(0f, -34f));
            arc.raycastTarget = false;

            var spin = (RectTransform)arc.transform;
            Tween.Run(SpinPeriod, Ease.Linear,
                      t => { if (spin) spin.localRotation = Quaternion.Euler(0f, 0f, -360f * t); },
                      spin, "spin").Loop(-1, false);

            _caption = UIKit.Shrinkable(
                UIKit.Label("Caption", root, message ?? string.Empty, 24, new Color(1f, .96f, .88f, .86f),
                            TextAnchor.MiddleCenter, new Vector2(PlateWidth - 36f, 34f),
                            new Vector2(.5f, 1f), new Vector2(0f, -80f)), 16);

            BuildBar(root);
        }

        void BuildBar(RectTransform root)
        {
            _bar = UIKit.Img("Track", root, Art.Round(8), new Color(1f, 1f, 1f, .16f),
                             new Vector2(BarWidth, BarHeight),
                             new Vector2(.5f, 1f), new Vector2(0f, -118f)).gameObject;

            var track = (RectTransform)_bar.transform;
            _bar.GetComponent<Image>().type = Image.Type.Sliced;
            _bar.GetComponent<Image>().raycastTarget = false;

            // Pivoted left so growing it is one number rather than a width and a position,
            // which is the arrangement a fill that can also shrink needs.
            var fill = UIKit.Img("Fill", track, Art.Round(8), Pal.Gold,
                                 new Vector2(0f, BarHeight), new Vector2(0f, .5f), Vector2.zero);
            fill.type = Image.Type.Sliced;
            fill.raycastTarget = false;

            _fill = (RectTransform)fill.transform;
            _fill.anchorMin = _fill.anchorMax = new Vector2(0f, .5f);
            _fill.pivot = new Vector2(0f, .5f);
            _fill.anchoredPosition = Vector2.zero;

            // Hidden until there is a figure. An empty bar under a spinner says "nothing has
            // happened", which is a claim this has no way to make.
            _bar.SetActive(false);
        }

        /// <summary>Changes what the wait is described as, without restarting anything.</summary>
        public void Say(string message)
        {
            if (_caption) _caption.text = message ?? string.Empty;
        }

        /// <summary>
        /// How far through, 0..1. Safe to call from wherever a loader happens to finish a
        /// batch — see <see cref="_fraction"/>.
        /// </summary>
        public void Report(float value) => _fraction = Mathf.Clamp01(value);

        /// <summary>
        /// The wait is over. Fades out if it ever appeared, and simply goes if it did not —
        /// which is the common case and the reason <see cref="AppearAfter"/> exists.
        /// </summary>
        public void Done()
        {
            if (_finished) return;
            _finished = true;

            if (!_appeared) { Destroy(gameObject); return; }

            Tween.Fade(_group, 0f, FadeOut).OnDone(() => { if (this) Destroy(gameObject); });
        }

        void Update()
        {
            if (_finished) return;

            if (!_appeared)
            {
                _waited += Time.unscaledDeltaTime;
                if (_waited < AppearAfter) return;

                _appeared = true;
                Tween.Fade(_group, 1f, FadeIn);
            }

            float fraction = _fraction;
            if (fraction < 0f || Mathf.Approximately(fraction, _shown)) return;

            _shown = fraction;
            if (_bar != null && !_bar.activeSelf) _bar.SetActive(true);
            if (_fill) _fill.sizeDelta = new Vector2(BarWidth * fraction, BarHeight);
        }
    }
}
