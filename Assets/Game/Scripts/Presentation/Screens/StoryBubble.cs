using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The band along the bottom of a run where the story happens.
    ///
    /// <para>
    /// <b>A band rather than a modal, and that is the whole design.</b> A dialogue box that stops
    /// the game is a cutscene, and a cutscene is something a player learns to tap through; this
    /// sits in the strip of screen under the board that every mode already leaves empty, so a
    /// line can arrive <em>while</em> a board is being played on and cost nobody a beat. The one
    /// exception is the opening, which the screen holds the board for — three lines before the
    /// first move is a scene, and it is worth the pause exactly once.
    /// </para>
    /// <para>
    /// <b>Every line is a loc key and none of them is built by concatenation</b> (invariant 6).
    /// The keys are authored in the chapter body and the build gate resolves each one against
    /// <c>loc/en.json</c>; the only strings this file names itself are the five speaker names,
    /// and those are a literal table for the same reason — a key assembled from a speaker id at
    /// runtime is a key the gate cannot see.
    /// </para>
    /// <para>
    /// <b>It is skippable and it never blocks.</b> A tap completes the line being typed, and a
    /// second tap moves on; a line left alone advances on its own. A player who does not care
    /// about the story is never made to wait for it, which is what keeps it from becoming the
    /// thing they remember about the mode.
    /// </para>
    /// </summary>
    public sealed class StoryBubble : MonoBehaviour
    {
        /// <summary>How tall the band is, in canvas units. Sits inside a board host's bottom inset.</summary>
        public const float Height = 200f;

        /// <summary>Characters a second. Fast enough to read with, slow enough to be a voice.</summary>
        const float Rate = 58f;

        /// <summary>The least and the most a finished line is left up before it moves on.</summary>
        const float HoldFloor = 1.15f, HoldCeiling = 4.2f;

        /// <summary>How long the band takes to slide in and out.</summary>
        const float Slide = .26f;

        RectTransform _panel, _portraitBox;
        Image _portrait, _plate;
        Text _name, _line;
        Btn _skip;

        readonly Queue<StoryLine> _queue = new Queue<StoryLine>();
        Coroutine _running;
        Action _done;

        bool _typing;
        string _full = string.Empty;

        /// <summary>Whether anything is being said. The screen holds the board while it is true.</summary>
        public bool Busy => _running != null || _queue.Count > 0;

        /// <summary>
        /// Which mode's art folder the portraits come from, ending in a slash.
        ///
        /// <para>
        /// <b>A property rather than a constant, because the cast is vocabulary and the pixels
        /// are not.</b> <see cref="StoryCast"/> names who may speak — Bolt, the Collector, the
        /// three taken critters — and two modes set in the same raid share every one of those
        /// ids while drawing them from their own scoped folder (invariant 7b: an address owned
        /// by one chapter's scope is never re-claimed by another). It was a literal <c>"March/"</c>
        /// until a second mode wanted the band, which is the point at which a shared thing has
        /// to say who is asking.
        /// </para>
        /// </summary>
        public string Root { get; set; } = "March/";

        // ------------------------------------------------------------------ building
        public static StoryBubble Attach(RectTransform host)
        {
            // Stretched across the foot and a fixed height, so the *vertical* axis is a real
            // position and can be slid. A rect stretched on both axes has no anchoredPosition
            // worth animating - setting one rewrites the offsets that laid it out.
            var node = UIKit.Node("Story", host);
            node.anchorMin = new Vector2(0f, 0f);
            node.anchorMax = new Vector2(1f, 0f);
            node.pivot = new Vector2(.5f, 0f);
            node.sizeDelta = new Vector2(-32f, Height);
            node.anchoredPosition = Home;

            var bubble = node.gameObject.AddComponent<StoryBubble>();
            bubble.Build(node);
            return bubble;
        }

        /// <summary>Where the band rests. Clear of the safe area's own bottom inset.</summary>
        static readonly Vector2 Home = new Vector2(0f, 18f);

        void Build(RectTransform node)
        {
            _panel = node;

            _plate = UIKit.Img("Plate", node, Art.Round(30), new Color(.07f, .11f, .17f, .93f));
            _plate.rectTransform.anchorMin = new Vector2(0f, 0f);
            _plate.rectTransform.anchorMax = new Vector2(1f, 1f);
            _plate.rectTransform.offsetMin = new Vector2(Height * .46f, 0f);
            _plate.rectTransform.offsetMax = Vector2.zero;

            var rim = UIKit.Img("Rim", node, Art.RoundOutline(30, 4f), Pal.A(Pal.Aqua, .5f));
            rim.rectTransform.anchorMin = new Vector2(0f, 0f);
            rim.rectTransform.anchorMax = new Vector2(1f, 1f);
            rim.rectTransform.offsetMin = new Vector2(Height * .46f, 0f);
            rim.rectTransform.offsetMax = Vector2.zero;

            _portraitBox = UIKit.Node("Who", node);
            _portraitBox.anchorMin = _portraitBox.anchorMax = new Vector2(0f, .5f);
            _portraitBox.sizeDelta = new Vector2(Height * 1.02f, Height * 1.02f);
            _portraitBox.anchoredPosition = new Vector2(Height * .52f, Height * .12f);

            var halo = UIKit.Img("Halo", _portraitBox, Art.Glow(96), Pal.A(Pal.Aqua, .30f),
                                 new Vector2(Height * 1.1f, Height * 1.1f));
            halo.rectTransform.anchoredPosition = Vector2.zero;

            _portrait = UIKit.Img("Portrait", _portraitBox, null, Color.white,
                                  new Vector2(Height * .92f, Height * .92f));
            _portrait.rectTransform.anchoredPosition = Vector2.zero;
            _portrait.enabled = false;

            _name = UIKit.Titled("Name", node, string.Empty, 30, Pal.Aqua, TextAnchor.MiddleLeft,
                                 new Vector2(420f, 40f), new Vector2(0f, 1f),
                                 new Vector2(Height * .46f + 226f, -30f), outline: 3f, shadow: 3f);

            _line = UIKit.Label("Line", node, string.Empty, 32, Pal.Cream, TextAnchor.UpperLeft,
                                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, wrap: true);
            _line.rectTransform.anchorMin = new Vector2(0f, 0f);
            _line.rectTransform.anchorMax = new Vector2(1f, 1f);
            _line.rectTransform.offsetMin = new Vector2(Height * .46f + 26f, 20f);
            _line.rectTransform.offsetMax = new Vector2(-26f, -58f);

            // The whole band is the skip key. A separate small button would be a target to find
            // in the middle of a run, which is exactly when nobody is looking for one.
            _skip = UIKit.Button("Skip", node, null, new Vector2(10f, 10f), new Vector2(.5f, .5f),
                                 Vector2.zero, Advance);
            _skip.ClickSfx = null;

            // No press squash: the key *is* the band, so scaling it on a tap would shrink the
            // whole panel and the portrait with it.
            _skip.PressScale = 1f;

            var face = _skip.GetComponent<Image>();
            face.color = new Color(0f, 0f, 0f, 0f);
            face.raycastTarget = true;

            var hit = (RectTransform)_skip.transform;
            hit.anchorMin = Vector2.zero;
            hit.anchorMax = Vector2.one;
            hit.offsetMin = Vector2.zero;
            hit.offsetMax = Vector2.zero;

            Hide(instant: true);
        }

        // ------------------------------------------------------------------ speaking
        /// <summary>
        /// Says a beat. Anything already queued is kept — a critter coming out while the Warden
        /// is still talking is two things that both happened.
        /// </summary>
        public void Speak(StoryBeat beat, Action done = null)
        {
            if (beat == null || beat.Lines.Count == 0) { done?.Invoke(); return; }

            for (int i = 0; i < beat.Lines.Count; i++) _queue.Enqueue(beat.Lines[i]);

            if (done != null) _done = done;
            if (_running == null && isActiveAndEnabled) _running = StartCoroutine(Run());

            // **Nothing here may be able to strand its caller.** A coroutine will not start on a
            // component that is disabled or on a destroyed object, and a screen waiting on
            // `done` to hand a board back would then wait for the life of the run. That is not
            // hypothetical — it is the fault this class shipped with. So a `Speak` that could not
            // start says so immediately rather than silently promising to answer later.
            if (_running != null) return;

            _queue.Clear();
            Hide(instant: true);

            var stranded = _done;
            _done = null;
            stranded?.Invoke();
        }

        /// <summary>Stops everything and clears the band. For a restart.</summary>
        public void Silence()
        {
            _queue.Clear();

            if (_running != null) { StopCoroutine(_running); _running = null; }

            _typing = false;
            _done = null;
            Hide(instant: true);
        }

        IEnumerator Run()
        {
            while (_queue.Count > 0)
            {
                var line = _queue.Dequeue();
                yield return Say(line);
            }

            Hide(instant: false);
            _running = null;

            var done = _done;
            _done = null;
            done?.Invoke();
        }

        IEnumerator Say(StoryLine line)
        {
            Dress(line.Speaker);

            _full = Loc.Get(line.Key);
            _line.text = string.Empty;

            Show();
            Audio.Sfx("tip", .35f, 1.05f);

            _typing = true;

            float shown = 0f;
            while (_typing && shown < _full.Length)
            {
                shown += Time.unscaledDeltaTime * Rate;
                int chars = Mathf.Clamp(Mathf.FloorToInt(shown), 0, _full.Length);
                _line.text = _full.Substring(0, chars);
                yield return null;
            }

            _typing = false;
            _line.text = _full;

            float hold = Mathf.Clamp(_full.Length / Rate + .75f, HoldFloor, HoldCeiling);
            float until = Time.unscaledTime + hold;

            while (_advance == 0 && Time.unscaledTime < until) yield return null;
            _advance = 0;
        }

        int _advance;

        /// <summary>A tap: finish the line being typed, or move on from a finished one.</summary>
        void Advance()
        {
            if (_typing) { _typing = false; return; }
            _advance = 1;
        }

        // ------------------------------------------------------------------ who is talking
        /// <summary>
        /// The cast's names and colours.
        ///
        /// <b>A literal table rather than a key built from the speaker id.</b> Invariant 6 is
        /// about exactly this: a key assembled at runtime is a key the build gate's scan cannot
        /// see, so a missing translation ships as a raw key on screen. Five rows is cheap.
        /// </summary>
        static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { StoryCast.Bolt, "story.who.bolt" },
            { StoryCast.Collector, "story.who.collector" },
            { StoryCast.Mon1, "story.who.mon1" },
            { StoryCast.Mon2, "story.who.mon2" },
            { StoryCast.Mon3, "story.who.mon3" },
        };

        static Color Tint(string speaker)
        {
            if (speaker == StoryCast.Collector) return Pal.Poppy;
            if (speaker == StoryCast.Bolt) return Pal.Aqua;
            return Pal.Bloom;
        }

        void Dress(string speaker)
        {
            var tint = Tint(speaker);

            _name.text = Names.TryGetValue(speaker, out string key) ? Loc.Get(key) : string.Empty;
            _name.color = tint;

            var rim = _panel.Find("Rim");
            if (rim != null)
            {
                var image = rim.GetComponent<Image>();
                if (image != null) image.color = Pal.A(tint, .55f);
            }

            var halo = _portraitBox.Find("Halo");
            if (halo != null)
            {
                var image = halo.GetComponent<Image>();
                if (image != null) image.color = Pal.A(tint, .32f);
            }

            // The portrait is a flipbook of the same cast the board draws, so whoever is talking
            // is visibly the thing that was just standing on the board. Absent art leaves a
            // coloured disc rather than the white rectangle a null sprite draws (invariant 7b).
            var frames = Art.Frames(Root + Folder(speaker));

            if (frames != null && frames.Length > 0)
            {
                var first = frames[0];
                float ratio = first.rect.height > 0f ? first.rect.width / first.rect.height : 1f;
                float box = Height * .92f;

                _portrait.rectTransform.sizeDelta =
                    ratio >= 1f ? new Vector2(box, box / ratio) : new Vector2(box * ratio, box);

                _portrait.enabled = true;
                Flipbook.Attach(_portrait, frames, 15f);
            }
            else
            {
                Flipbook.Detach(_portrait);
                _portrait.sprite = Art.Disc(96);
                _portrait.color = Pal.A(tint, .8f);
                _portrait.rectTransform.sizeDelta = new Vector2(Height * .6f, Height * .6f);
                _portrait.enabled = true;
            }

            Tween.KillAll(_portraitBox);
            _portraitBox.localScale = Vector3.one * .82f;
            Tween.Scale(_portraitBox, 1f, .22f, Ease.OutBack);
        }

        /// <summary>
        /// Which folder of frames a speaker's portrait comes from.
        ///
        /// The speaker ids and the art folders are the same words on purpose — the cast is the
        /// board's cast — so this is one line rather than a second table to keep in step. The
        /// build gate refuses a speaker <c>StoryCast</c> does not know, which is what makes the
        /// identity safe to rely on.
        /// </summary>
        static string Folder(string speaker) => StoryCast.Knows(speaker) ? speaker : StoryCast.Bolt;

        // ------------------------------------------------------------------ coming and going
        bool _up;
        CanvasGroup _group;

        /// <summary>
        /// **This band is hidden by going transparent, never by being deactivated**, and that is
        /// not a style choice — it is the whole reason the first cut said nothing at all.
        ///
        /// <para>
        /// The component lives on the node it draws, so <c>SetActive(false)</c> switched off the
        /// very object <see cref="Speak"/> then tried to <c>StartCoroutine</c> on. Unity refuses
        /// that and returns null, so the queue never drained, the band never appeared, and — far
        /// worse — the <c>done</c> callback the screen was waiting on to hand the board back
        /// never fired, so the screen's opening scene left the board **latched for the life of the
        /// screen**. One line, no exception, and it read as two unrelated faults: no dialogue,
        /// and a board that ignores every tap.
        /// </para>
        /// <para>
        /// The general rule, which this project has now paid for twice in different clothes: a
        /// <c>MonoBehaviour</c> that hides itself must not disable the object it needs to be
        /// alive on. Alpha for the look, <c>blocksRaycasts</c> for the input, and the object
        /// stays awake.
        /// </para>
        /// </summary>
        CanvasGroup Group => _group != null ? _group : (_group = UIKit.Group(_panel));

        void Show()
        {
            if (_up) return;
            _up = true;

            var group = Group;
            Tween.KillAll(group);
            group.blocksRaycasts = true;
            Tween.Fade(group, 1f, Slide);

            _panel.anchoredPosition = Home + new Vector2(0f, -Height * .6f);
            Tween.Move(_panel, Home, Slide, Ease.OutBack);
        }

        void Hide(bool instant)
        {
            _up = false;

            var group = Group;
            Tween.KillAll(group);

            // Nothing to click on while there is nothing to read, so the invisible band cannot
            // swallow a tap meant for whatever is behind it.
            group.blocksRaycasts = false;

            var away = Home + new Vector2(0f, -Height * .4f);

            if (instant)
            {
                group.alpha = 0f;
                _panel.anchoredPosition = away;
                return;
            }

            Tween.Fade(group, 0f, Slide);
            Tween.Move(_panel, away, Slide, Ease.InQuad);
        }

        void OnDestroy()
        {
            Tween.KillAll(this);
            Tween.KillAll(_panel);
        }
    }
}
