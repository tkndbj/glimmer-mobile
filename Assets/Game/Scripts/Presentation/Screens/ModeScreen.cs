using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The chrome every mode's screen shares: the backdrop, the header, the readouts and the
    /// way out.
    ///
    /// <para>
    /// <b>Shared by inheritance rather than by a host that switches.</b> Each mode's screen is
    /// its own class and owns its own board, its own input and its own rules; what it inherits
    /// is only the furniture. That is what stops a fifth mode turning this into a file with five
    /// branches in it — the previous arrangement was one screen holding all three games behind a
    /// switch, which is exactly the god file that has to be avoided.
    /// </para>
    /// <para>
    /// It resolves the chapter, hands the level to <see cref="Play"/>, and gets out of the way.
    /// A subclass overrides three things: how to build its board, what its readouts say, and
    /// what a restart means.
    /// </para>
    /// </summary>
    public abstract class ModeScreen : RunScreen, IPlaysLevel
    {
        public LevelId LevelId { protected get; set; }

        public override string Track => "mus_play";

        protected LevelDefinition Level { get; private set; }
        protected ChapterDefinition Chapter { get; private set; }

        /// <summary>Where the board goes. Sized and resolved before <see cref="Play"/> is called.</summary>
        protected RectTransform Host { get; private set; }

        /// <summary>
        /// One thing a mode counts, and how it wants it read.
        ///
        /// <para>
        /// A declaration rather than six <c>out</c> parameters, and the six were a real seam
        /// failure rather than an untidiness: the mode that wanted <em>one</em> number had to
        /// hand back four empty strings that were then silently dropped, so what a mode said and
        /// what the row did were only related by a convention nobody could see. A list says how
        /// many there are by having that many in it.
        /// </para>
        /// <para>
        /// The tint is here because a number that means something at a glance is part of what a
        /// mode is saying — Lightweave's ink turns amber and then red — and the alternative is a
        /// second hook, painted from a second place, that can disagree with this one about which
        /// number it is talking about.
        /// </para>
        /// </summary>
        protected readonly struct Readout
        {
            public readonly string Caption, Value;
            public readonly Color Tint;

            public Readout(string caption, string value, Color tint)
            {
                Caption = caption;
                Value = value;
                Tint = tint;
            }

            public Readout(string caption, string value) : this(caption, value, Pal.Cream) { }
        }

        /// <summary>The most a mode may count at once. See <c>ReadoutRow</c>.</summary>
        const int MaxReadouts = ReadoutRow.Most;

        readonly Readout[] _slots = new Readout[MaxReadouts];
        readonly Text[] _values = new Text[MaxReadouts];
        readonly Text[] _captions = new Text[MaxReadouts];
        readonly List<Readout> _reading = new List<Readout>(MaxReadouts);
        int _shown = -1;

        // ------------------------------------------------------------------ subclass hooks
        /// <summary>Builds the board. The host is resolved and non-zero by the time this runs.</summary>
        protected abstract void Play();

        /// <summary>
        /// What this mode counts, in the order it should be read. One to three of them.
        ///
        /// Asked whenever the board reports a change, and again while the header is being built —
        /// so it has to answer before <see cref="Play"/> has run, with whatever a board that does
        /// not exist yet is worth.
        /// </summary>
        protected abstract void Readouts(List<Readout> into);

        /// <summary>How far the board sits from the screen's edges. Overridden by modes that want more room.</summary>
        protected virtual Vector4 HostInset => new Vector4(24f, 250f, 24f, 350f);

        /// <summary>
        /// Where a readout is on screen, for a lesson that has to point at one.
        ///
        /// Narrow on purpose: a mode may say "ring the number I called ink", and everything
        /// about where the row sits and how it is sized stays here. Null for a slot this mode
        /// does not use.
        /// </summary>
        protected RectTransform ReadoutAt(int index)
            => index >= 0 && index < MaxReadouts && _values[index]
                ? (RectTransform)_values[index].transform : null;

        bool _ready;

        /// <summary>
        /// Holds the iris shut until the board exists.
        ///
        /// <para>
        /// A mode screen builds its board from a coroutine — the chapter body has to be fetched
        /// and the host rect has to be laid out before a grove can be sized — and until this was
        /// here, <see cref="Flow"/> read the default <c>true</c> and opened straight away. So
        /// <see cref="OnPresented"/> ran at a moment when <c>Play</c> may not have: the lesson
        /// toast could be thrown over a screen with no board on it, and anything an override
        /// wants to do with the board it just built would find nothing and quietly do nothing.
        /// <c>WeaveScreen</c> raises the one lesson this game has that no board can demonstrate,
        /// which is exactly the shape of thing that must not be skipped by a race.
        /// </para>
        /// <para>
        /// It is also set when the screen bails, because <see cref="Flow"/> stays busy until the
        /// iris opens and a level that could not be read would otherwise sit behind a closed one
        /// for the whole ready timeout before navigating away.
        /// </para>
        /// </summary>
        public override bool Ready => _ready;

        // ------------------------------------------------------------------ lifecycle
        protected override void Build() => StartCoroutine(Prepare());

        /// <summary>
        /// Fetches the chapter, resolves the level and builds the screen around it.
        ///
        /// <b>Named away from <c>Resolve</c> deliberately.</b> It was called that, which is also
        /// what <see cref="RunScreen"/> calls clearing a run's stake — and a private
        /// <c>IEnumerator Resolve()</c> here <em>hides</em> the inherited <c>void Resolve()</c>
        /// from every mode below it. The calls still compiled, still bound, and quietly built an
        /// iterator nobody ran, so a won grove never cleared its <c>RunGuard</c> marker and the
        /// player was charged a heart for it at the next launch. Two members with one name in one
        /// hierarchy is a bug waiting for whoever adds the third.
        /// </summary>
        IEnumerator Prepare()
        {
            var chapterId = GameContent.ChapterOf(LevelId);
            if (!chapterId.IsValid) { yield return Bail(); yield break; }

            var task = GameContent.ChapterAsync(chapterId);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) Debug.LogException(task.Exception);

            var body = task.Result;
            Level = body?.Find(LevelId);
            Chapter = body?.Definition;

            if (Level == null || Chapter == null)
            {
                Debug.LogError($"[{GetType().Name}] level '{LevelId}' could not be read");
                yield return Bail();
                yield break;
            }

            // Registers which addresses belong to this chapter before anything asks for a
            // sprite, or the art would be filed as global and never released.
            _ = AssetLibrary.EnsureChapterAsync(body);
            if (!this) yield break;

            Scenery.Cover(Content, "Bg/" + Level.Presentation.ResolveBackdrop(Chapter), .16f, .34f);
            Fireflies.Spawn(Content, 10, Pal.A(ModeLooks.Of(Level.Mode).Accent, .9f), 4f, 14f);

            BuildHeader();
            BuildReadouts();

            Host = UIKit.Node("Board", Safe);
            Host.offsetMin = new Vector2(HostInset.x, HostInset.y);
            Host.offsetMax = new Vector2(-HostInset.z, -HostInset.w);

            StartCoroutine(Raise());
        }

        IEnumerator Raise()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();

            // A board sized from a rect that has not been laid out yet is a board of nothing.
            int guard = 0;
            while (Host.rect.width < 40f && guard++ < 60) yield return null;
            if (!this) yield break;

            Play();
            Repaint();

            // Before Ready, so a mode that teaches something shows its review key while the
            // iris is still shut rather than switching it on in front of the player.
            Teaching.Ask();

            _ready = true;
        }

        IEnumerator Bail()
        {
            _ready = true;
            while (Flow.Busy) yield return null;
            Flow.Go<LevelsScreen>();
        }

        /// <summary>How tall the header band is, and where the readout row sits under it.</summary>
        const float BarHeight = 210f, ReadoutsY = 282f;

        /// <summary>
        /// The key in the header's right-hand corner: what it is, and what pressing it does.
        ///
        /// <para>
        /// <b>A declaration rather than a flag.</b> It began as <c>bool HeaderRestart</c>, which
        /// is the first of the five booleans that turn a shared base class back into the god file
        /// this one was split out of — the remarks above are explicit that the arrangement before
        /// it was one screen holding three games behind a switch. A mode says what its key is;
        /// nothing here branches on which mode is asking.
        /// </para>
        /// <para>
        /// The default is restart, which is the right control on a mode where restarting costs
        /// nothing — the board goes back and so does the player. Lightweave stopped being one
        /// when it was dealt ink: a restart there hands back a full pot, so it is the cheapest
        /// way out of a grove going wrong and belongs one deliberate tap inside a pause menu
        /// rather than under a thumb that is already reaching across the board.
        /// </para>
        /// </summary>
        protected readonly struct HeaderKey
        {
            public readonly string Icon;
            public readonly Action Press;

            public HeaderKey(string icon, Action press)
            {
                Icon = icon;
                Press = press;
            }
        }

        protected virtual HeaderKey RightKey => new HeaderKey("ic_restart", RestartLevel);

        /// <summary>
        /// The header: a way back, and either a way to start again or a way to pause.
        ///
        /// <para>
        /// <b>The level's name and its tagline used to sit here and no longer do.</b> They were
        /// the two highest things on the screen, so on a phone with a camera cutout they were
        /// the two it took — and the inset cannot buy back what a mode has chosen to draw at the
        /// very top of it. Neither was load-bearing: the player picked the level by name a
        /// screen ago. The tagline used to come back as a flavour line along the bottom of the
        /// board, and that is gone too — a box on every level of every mode is furniture, and
        /// the tips are what a board has to say. What is left is the two controls and, below
        /// them, the readouts.
        /// </para>
        /// </summary>
        void BuildHeader()
        {
            var bar = UIKit.Box("Header", Safe, new Vector2(0f, BarHeight), new Vector2(.5f, 1f),
                                new Vector2(0f, -BarHeight * .5f));
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, BarHeight);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .04f, .08f, .58f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, -40, 0, 0);
            ((RectTransform)shade.transform).localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", bar, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -4f), LeaveToMap);

            var key = RightKey;
            UIKit.IconButton("RightKey", bar, Skins.Nav, key.Icon, new Vector2(118f, 118f),
                             new Vector2(1f, .5f), new Vector2(-102f, -4f), key.Press);

            // Beside the restart key. Built for every mode and shown only by the ones whose
            // board actually teaches something, which is RunScreen's to decide once the board
            // has been read — a mode that declares no lessons never sees it. See
            // RunLessons.BuildKey.
            Teaching.BuildKey(bar, new Vector2(-102f, -4f));
        }

        /// <summary>
        /// Three readouts, captioned by whichever mode is running.
        ///
        /// <para>
        /// Captions rather than icons, because the modes count entirely different things and a
        /// shared icon set would be three lies.
        /// </para>
        /// <para>
        /// One of the three is a clock on any mode that has one, which is why the values grew
        /// with the header's text going away rather than staying where they were. They are sized
        /// as a row and not one at a time: singling the clock out would make the other two read
        /// as captions of it, and on the two modes that have no clock at all there would be
        /// nothing to explain why the middle number is the loud one.
        /// </para>
        /// </summary>
        void BuildReadouts()
        {
            var row = UIKit.Box("Readouts", Safe, new Vector2(0f, 100f), new Vector2(.5f, 1f),
                                new Vector2(0f, -ReadoutsY));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.sizeDelta = new Vector2(0f, 100f);

            // Every slot is built and the unused ones are switched off, rather than the row
            // being rebuilt when a mode's count changes. GridView's bargain: three Texts cost
            // nothing to keep and an object destroyed and remade cannot carry an animation.
            for (int i = 0; i < MaxReadouts; i++)
                _values[i] = Slot(row, out _captions[i]);

            Lay(Fresh());
        }

        /// <summary>
        /// Places as many slots as this mode counts and hides the rest.
        ///
        /// One number sits in the middle; two straddle it; three take the row. Called only when
        /// the count actually changes, which on every mode that ships is once.
        /// </summary>
        void Lay(int count)
        {
            if (count == _shown) return;

            _shown = count;

            for (int i = 0; i < MaxReadouts; i++)
            {
                bool used = i < count;
                if (_values[i]) _values[i].gameObject.SetActive(used);
                if (_captions[i]) _captions[i].gameObject.SetActive(used);
                if (!used) continue;

                // Where, and whether that leaves room, is ReadoutRow's — in Domain, where a
                // test can hold the spacing to what it claims rather than a screenshot on one
                // aspect ratio.
                float x = ReadoutRow.XFor(i, count);

                Place(_values[i], x, 14f);
                Place(_captions[i], x, -34f);
            }
        }

        static void Place(Text text, float x, float y)
        {
            if (text) ((RectTransform)text.transform).anchoredPosition = new Vector2(x, y);
        }

        /// <summary>
        /// Builds one slot at the middle of the row. Where it ends up is <see cref="Lay"/>'s,
        /// which is the only thing that may move it.
        /// </summary>
        static Text Slot(RectTransform row, out Text caption)
        {
            var value = UIKit.Titled("Value", row, "0", 58, Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(280f, 68f), new Vector2(.5f, .5f),
                                     new Vector2(0f, 14f), 4f, 3f);
            UIKit.Shrinkable(value, 26);

            caption = UIKit.Titled("Cap", row, "", 22, new Color(.92f, .96f, 1f, .55f),
                                   TextAnchor.MiddleCenter, new Vector2(280f, 28f),
                                   new Vector2(.5f, .5f), new Vector2(0f, -34f), 3f, 0f);
            UIKit.Shrinkable(caption, 14);
            return value;
        }

        /// <summary>Asks the mode what it counts, clamped to what the row can hold.</summary>
        int Fresh()
        {
            _reading.Clear();
            Readouts(_reading);

            int count = _reading.Count < MaxReadouts ? _reading.Count : MaxReadouts;
            for (int i = 0; i < count; i++) _slots[i] = _reading[i];

            return count;
        }

        /// <summary>Re-reads the readouts. Subclasses call it whenever their board moves.</summary>
        protected void Repaint()
        {
            if (Level == null) return;

            Lay(Fresh());

            for (int i = 0; i < _shown; i++)
            {
                if (_captions[i]) _captions[i].text = _slots[i].Caption;
                if (!_values[i]) continue;

                _values[i].text = _slots[i].Value;
                _values[i].color = _slots[i].Tint;
            }
        }

        /// <summary>Says how the run ended. Modes call it from their own ending.</summary>
        protected void Finish(string headline)
        {
            Audio.Sfx("shatter", .7f);
            Scenery.Toast(Content, headline, Pal.Cream, 4.2f);
        }

        // ------------------------------------------------------------------ the way out
        /// <summary>
        /// Another go after a defeat, which is <see cref="Rewind"/> and <b>never</b>
        /// <c>RestartLevel</c>.
        ///
        /// The distinction is a heart. A defeat has already charged for the run that just ended,
        /// so putting the board back afterwards is free; <c>RestartLevel</c> abandons a run that
        /// is still live and prices it. This used to call the latter, which was harmless only
        /// while a restart was free — the moment <see cref="RunScreen"/> started pricing it, it
        /// would have taken a second heart for one loss.
        /// </summary>
        public override void RetryAfterDefeat() => Rewind();

        /// <summary>
        /// A mode with no stake of its own — the lab boards, which never commit — walks away for
        /// nothing, and that falls out of <c>RunScreen.ConfirmForfeit</c> rather than needing to
        /// be said here.
        /// </summary>
        protected internal override LevelId StakeLevel => Level != null ? Level.Id : LevelId.None;

        protected override bool RunOver => false;

        protected override void NoteAbandoned(string reason) { }

        public override bool OnBack() { LeaveToMap(); return true; }

    }
}
