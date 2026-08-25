using System.Collections;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The chrome every mode's screen shares: the backdrop, the header, three readouts and the
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

        Text _left, _middle, _right, _leftCap, _middleCap, _rightCap;

        // ------------------------------------------------------------------ subclass hooks
        /// <summary>Builds the board. The host is resolved and non-zero by the time this runs.</summary>
        protected abstract void Play();

        /// <summary>What the three readouts say. Called whenever the board reports a change.</summary>
        protected abstract void Readouts(out string leftCap, out string left,
                                         out string middleCap, out string middle,
                                         out string rightCap, out string right);

        /// <summary>How far the board sits from the screen's edges. Overridden by modes that want more room.</summary>
        protected virtual Vector4 HostInset => new Vector4(24f, 250f, 24f, 330f);

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
        protected override void Build() => StartCoroutine(Resolve());

        IEnumerator Resolve()
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
            _ready = true;
        }

        IEnumerator Bail()
        {
            _ready = true;
            while (Flow.Busy) yield return null;
            Flow.Go<LevelsScreen>();
        }

        void BuildHeader()
        {
            var bar = UIKit.Box("Header", Safe, new Vector2(0f, 210f), new Vector2(.5f, 1f),
                                new Vector2(0f, -105f));
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, 210f);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .04f, .08f, .58f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, -40, 0, 0);
            ((RectTransform)shade.transform).localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", bar, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -4f), LeaveToMap);
            UIKit.IconButton("Again", bar, Skins.Nav, "ic_restart", new Vector2(118f, 118f),
                             new Vector2(1f, .5f), new Vector2(-102f, -4f), RestartLevel);

            UIKit.Shrinkable(UIKit.Titled("Name", bar, Loc.Get(Level.NameKey).ToUpperInvariant(),
                                          50, Pal.Cream, TextAnchor.MiddleCenter,
                                          new Vector2(600f, 58f), new Vector2(.5f, .5f),
                                          new Vector2(0f, 8f), 4f, 4f));
            UIKit.Shrinkable(UIKit.Titled("Tag", bar, Loc.Get(Level.TaglineKey), 26,
                                          new Color(.94f, .97f, 1f, .80f), TextAnchor.MiddleCenter,
                                          new Vector2(740f, 40f), new Vector2(.5f, .5f),
                                          new Vector2(0f, -42f), 3f, 3f));
        }

        /// <summary>
        /// Three readouts, captioned by whichever mode is running.
        ///
        /// Captions rather than icons, because the modes count entirely different things and a
        /// shared icon set would be three lies.
        /// </summary>
        void BuildReadouts()
        {
            var row = UIKit.Box("Readouts", Safe, new Vector2(0f, 100f), new Vector2(.5f, 1f),
                                new Vector2(0f, -262f));
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.sizeDelta = new Vector2(0f, 100f);

            _left = Readout(row, -300f, out _leftCap);
            _middle = Readout(row, 0f, out _middleCap);
            _right = Readout(row, 300f, out _rightCap);
        }

        static Text Readout(RectTransform row, float x, out Text caption)
        {
            var value = UIKit.Titled("Value", row, "0", 46, Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(280f, 56f), new Vector2(.5f, .5f),
                                     new Vector2(x, 12f), 4f, 3f);
            UIKit.Shrinkable(value, 22);

            caption = UIKit.Titled("Cap", row, "", 22, new Color(.92f, .96f, 1f, .55f),
                                   TextAnchor.MiddleCenter, new Vector2(280f, 28f),
                                   new Vector2(.5f, .5f), new Vector2(x, -30f), 3f, 0f);
            UIKit.Shrinkable(caption, 14);
            return value;
        }

        /// <summary>Re-reads the readouts. Subclasses call it whenever their board moves.</summary>
        protected void Repaint()
        {
            if (Level == null) return;

            Readouts(out string lc, out string lv, out string mc, out string mv,
                     out string rc, out string rv);

            if (_leftCap) _leftCap.text = lc;
            if (_left) _left.text = lv;
            if (_middleCap) _middleCap.text = mc;
            if (_middle) _middle.text = mv;
            if (_rightCap) _rightCap.text = rc;
            if (_right) _right.text = rv;
        }

        /// <summary>Says how the run ended. Modes call it from their own ending.</summary>
        protected void Finish(string headline)
        {
            Audio.Sfx("shatter", .7f);
            Scenery.Toast(Content, headline, Pal.Cream, 4.2f);
        }

        // ------------------------------------------------------------------ the way out
        public override void RetryAfterDefeat() => RestartLevel();
        public override void Resume() { }
        public override void LeaveToMap() => Flow.Go<LevelsScreen>();
        public override void LeaveToHome() => Flow.Go<HomeScreen>();
        public override bool OnBack() { LeaveToMap(); return true; }

        public override void OnPresented()
        {
            if (Level == null) return;

            string lesson = Loc.Get(Level.LessonKey);
            Tween.After(.4f, () => { if (this) Scenery.Toast(Content, lesson, Pal.Cream, 6f); }, this);
        }
    }
}
