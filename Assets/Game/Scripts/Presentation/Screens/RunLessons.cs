using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// One lesson a run has to teach before it starts: what is being taught, and what on the
    /// board it is about.
    ///
    /// <para>
    /// A description rather than a call, for the reason <see cref="View.Ready"/> and
    /// <see cref="View.WantsMultiTouch"/> are declarations: a mode that put its own tips up
    /// would also own the order they go up in, the latch on its board and the moment its run is
    /// allowed to start — and it would own all three again in the next mode. Here a mode says
    /// what it has to teach and <see cref="RunLessons"/> owns the sequence.
    /// </para>
    /// </summary>
    public struct Lesson
    {
        /// <summary>What is being taught. Its strings come from its id.</summary>
        public Mechanic Mechanic;

        /// <summary>The thing on the board to ring, or null to teach without pointing.</summary>
        public RectTransform Target;

        /// <summary>An ordered route for a coaching hand, or null for a lesson that is a sentence.</summary>
        public RectTransform[] Trace;

        /// <summary>The colour a demonstration is drawn in.</summary>
        public Color Tint;

        /// <summary>How far a demonstration reaches in board cells, which decides its pace.</summary>
        public int Cells;

        /// <summary>A lesson that rings one thing and says a sentence about it.</summary>
        public static Lesson At(Mechanic mechanic, RectTransform target)
            => new Lesson { Mechanic = mechanic, Target = target, Tint = Pal.Cream, Cells = 1 };
    }

    /// <summary>
    /// Everything about teaching a run: what it shows a first-timer, the key that shows it
    /// again, and the hold that keeps the run still while either is on screen.
    ///
    /// <para>
    /// <b>It is a collaborator rather than more of <see cref="RunScreen"/>.</b> That class
    /// already owns two things a mode must never own — what an exit costs, and when a run may
    /// begin — and teaching is a third that is bigger than both put together: a queue, a chain,
    /// a modal, a latch, a toast and a header control. Folding it in made one type that had to
    /// be read whole before any part of it could be changed, which is the shape this project
    /// keeps taking apart (<c>WeaveRun</c> into five, <c>ChestOverlay</c> into
    /// <c>RewardFlight</c>). Split, "when may a run start" is thirty lines beside the stake and
    /// this is the rest.
    /// </para>
    /// <para>
    /// <b>What it takes from the run and what it leaves there.</b> It holds the screen, which
    /// is a <c>MonoBehaviour</c>, so <c>if (_run)</c> is Unity's own lifetime check and answers
    /// correctly for a screen destroyed under a tip that is still open — the reason
    /// <see cref="RunScreen"/> is a base class rather than an interface, kept. The mode's
    /// declarations (<c>Lessons</c>, <c>Teachable</c>, <c>Flavour</c>, <c>Latch</c>) stay on
    /// the screen where a mode overrides them; this only reads them.
    /// </para>
    /// <para>
    /// <b>The hold is shared and named.</b> This takes and releases <c>RunHold.Teaching</c> and
    /// touches nothing else, so a run held for two reasons releases them independently and
    /// neither can cancel the other by writing <c>false</c> over it. Whether the opening
    /// transition is over stays <see cref="RunScreen"/>'s.
    /// </para>
    /// </summary>
    public sealed class RunLessons
    {
        readonly RunScreen _run;
        readonly RunHold _hold;

        /// <summary>The queue being taught. Trimmed to what the player has never met.</summary>
        readonly List<Lesson> _lessons = new List<Lesson>(2);

        /// <summary>
        /// A list of its own for answering questions.
        ///
        /// <see cref="Offer"/> can be asked while a chain is part-way through
        /// <see cref="_lessons"/>, and answering a question must never disturb the queue being
        /// taught.
        /// </summary>
        readonly List<Lesson> _probe = new List<Lesson>(2);

        int _taught;

        /// <summary>True while a lesson is pending or on screen, whoever raised it.</summary>
        bool _teaching;

        /// <summary>Whether this run has anything to teach at all, first-timer or not.</summary>
        bool _teaches;

        /// <summary>A beat between two lessons, so the second does not read as the first flickering.</summary>
        const float BetweenLessons = .18f;

        /// <summary>Long enough for the screen to settle before a line of flavour lands on it.</summary>
        const float FlavourDelay = .4f;

        public RunLessons(RunScreen run, RunHold hold)
        {
            _run = run;
            _hold = hold;
        }

        /// <summary>True while a lesson is pending or up. The run must not advance.</summary>
        public bool Teaching => _teaching;

        // ------------------------------------------------------------------ the opening
        /// <summary>
        /// Teaches whatever this run brings that the player has never met, and says the
        /// level's flavour line when there is nothing to teach.
        ///
        /// <para>
        /// <b>It takes its hold before it returns</b>, which is the whole of what the caller
        /// has to know: <see cref="RunScreen.OnPresented"/> releases the opening hold
        /// immediately afterwards, and if this has already taken the teaching one there is
        /// never a frame in between where the run is free. One free frame is one frame of a
        /// run the player has not been shown.
        /// </para>
        /// <para>
        /// The board's whole lesson list is asked for and then <em>trimmed</em> to what is new,
        /// rather than the mode being asked only for what is new. The untrimmed count is what
        /// decides whether this run gets a review key, and this is the one moment the answer is
        /// cheap: the board has just been built and nothing has moved on it yet.
        /// </para>
        /// </summary>
        public void Open()
        {
            if (!_run) return;

            _lessons.Clear();
            _taught = 0;
            _run.Lessons(_lessons);

            // Before the trim. A glade that teaches something the player already knows still
            // teaches it — that is exactly who the review key is for.
            Offer(_lessons.Count > 0);

            for (int i = _lessons.Count - 1; i >= 0; i--)
                if (TipLedger.HasSeen(_lessons[i].Mechanic)) _lessons.RemoveAt(i);

            if (_lessons.Count == 0) { SayFlavour(); return; }

            _teaching = true;
            _hold.Take(RunHold.Teaching);

            _run.Latch(true);
            Refresh();
            Tween.After(_run.LessonDelay, ShowLesson, _run);
        }

        // ------------------------------------------------------------------ showing one again
        /// <summary>
        /// Puts every lesson this board carries back up, at the player's asking.
        ///
        /// <para>
        /// <b>The same panels, raised the same way.</b> It re-asks the mode rather than
        /// replaying a list kept from the opening, because a restart rebuilds the very tiles a
        /// lesson rings — a cached <c>RectTransform</c> would by then be a destroyed object,
        /// and the tip would quietly lose its ring and its coaching hand and become a sentence
        /// in a box. Re-asking costs one board scan on a control pressed by hand.
        /// </para>
        /// <para>
        /// It goes through exactly the sequence a first-timer gets — the same hold, the same
        /// latch, the same chaining — so the run is frozen for the whole of it and there is no
        /// second copy of the rule about when a run is allowed to run. Refused rather than
        /// queued while one is already up or while the board is mid-animation: a lesson is a
        /// modal over the board being discussed, and both of those are states where the board
        /// is owned by something else.
        /// </para>
        /// </summary>
        public void Review()
        {
            if (!_run || _teaching || !_run.Teachable) return;

            _lessons.Clear();
            _taught = 0;
            _run.Lessons(_lessons);

            // A run whose lessons have gone away — a board that could not be read, a view torn
            // down underneath the header — takes the control with them rather than leaving a
            // key that does nothing.
            if (_lessons.Count == 0) { Offer(false); return; }

            _teaching = true;
            _hold.Take(RunHold.Teaching);

            _run.Latch(true);
            Refresh();

            // No delay. The opening one exists so the board a tip points at has finished
            // arriving; by now it arrived minutes ago, and a button that does nothing for half
            // a second reads as a button that did not register.
            ShowLesson();
        }

        /// <summary>
        /// Shows one lesson and, when it is dismissed, the next.
        ///
        /// <para>
        /// Chained on dismissal rather than shown together: a board that introduces two ideas
        /// would otherwise stack two modals and the player would meet the second before reading
        /// the first. <see cref="TipOverlay.Dismissed"/> fires exactly once however that panel
        /// goes away, so the chain cannot stall — and therefore neither can the hold — on a tip
        /// that was destroyed rather than accepted.
        /// </para>
        /// </summary>
        void ShowLesson()
        {
            if (!_run) return;

            if (_taught >= _lessons.Count) { Taught(); return; }

            var lesson = _lessons[_taught++];

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = lesson.Mechanic;
                v.Target = lesson.Target;
                v.Trace = lesson.Trace;
                v.TraceTint = lesson.Tint;
                v.TraceCells = lesson.Cells;
                v.Dismissed = () => Tween.After(BetweenLessons, ShowLesson, _run);
            });
        }

        /// <summary>The last lesson is closed: hand the board back, then let the run begin.</summary>
        void Taught()
        {
            _teaching = false;
            if (_run) _run.Latch(false);

            // Named rather than ReleaseAll, so a hold added later for something else is not
            // swept away by a path that knows nothing about it.
            _hold.Release(RunHold.Teaching);
            Refresh();
        }

        void SayFlavour()
        {
            string line = _run.Flavour;
            if (string.IsNullOrEmpty(line)) return;

            float hold = _run.FlavourSeconds;
            Tween.After(FlavourDelay,
                        () => { if (_run) Scenery.Toast(_run.Content, line, Pal.Cream, hold); },
                        _run);
        }

        // ------------------------------------------------------------------ the review control
        /// <summary>How big the "i" is, and how far it stands from the key beside it.</summary>
        const float ReviewSize = 118f, ReviewGap = 16f;

        Btn _key;
        bool _live = true;

        /// <summary>
        /// Puts the "show me that again" key in a mode's header, beside whatever already lives
        /// in the top-right corner.
        ///
        /// <para>
        /// <b>Built by the mode, owned here.</b> Each mode draws its own header — the glade's
        /// carries a pause key, a mode screen's carries a restart — so there is no shared bar
        /// to hang this off, and reaching into a subclass's layout to find one would be the
        /// worse arrangement by a distance. What a mode passes is a position; everything about
        /// when the key appears, whether it is live, and what pressing it does stays here,
        /// because all three are questions about the lessons rather than about the header.
        /// </para>
        /// <para>
        /// It is built <b>hidden</b> and shown by <see cref="Offer"/> once the board has been
        /// read, since a header is drawn before anybody knows what the board contains. Both
        /// orders work: a header built after the screen was presented reads the answer that is
        /// already in hand, and one built before is switched on when it arrives.
        /// </para>
        /// <para>
        /// <see cref="Skins.Aside"/> rather than <see cref="Skins.Nav"/>, which is the rule this
        /// project already follows everywhere an "i" sits in a top corner: a key that explains
        /// and a key that moves you should not be the same button in two places.
        /// </para>
        /// </summary>
        /// <param name="bar">The header row it belongs to.</param>
        /// <param name="beside">
        /// Where the right-hand key sits in that row, as an offset from the row's right edge.
        /// The "i" is placed one button and a gap further in, so the two read as a pair and
        /// neither has to know the other's size.
        /// </param>
        public void BuildKey(RectTransform bar, Vector2 beside)
        {
            if (bar == null || !_run) return;

            _key = UIKit.IconButton("Lessons", bar, Skins.Aside, "ic_info",
                                    new Vector2(ReviewSize, ReviewSize),
                                    new Vector2(1f, .5f),
                                    new Vector2(beside.x - ReviewSize - ReviewGap, beside.y),
                                    Review);

            _key.gameObject.SetActive(_teaches);
            Refresh();

            // The board may already be readable — a glade parses its puzzle before it draws its
            // header — in which case the answer is available now and the key never has to
            // appear in front of the player. Where it is not, Offer is called again the moment
            // the board exists, which on every mode here is still behind the iris.
            Ask();
        }

        /// <summary>
        /// Re-asks what this run teaches and shows or hides the review key accordingly.
        ///
        /// <para>
        /// Called by a mode as soon as its board can answer, and again when the screen is
        /// presented. <b>Both, rather than one or the other</b>, because the two modes build in
        /// different orders — a glade has its puzzle parsed before its header is drawn, a mode
        /// screen has its board only after — and a key that switches itself on after the iris
        /// has opened is a control appearing in front of the player for no reason they can see.
        /// Idempotent, so calling it on every path costs a board scan and changes nothing.
        /// </para>
        /// </summary>
        public void Ask()
        {
            if (!_run) return;

            _probe.Clear();
            _run.Lessons(_probe);
            Offer(_probe.Count > 0);
            _probe.Clear();
        }

        /// <summary>Shows or hides the review key, and remembers the answer for a later header.</summary>
        void Offer(bool teaches)
        {
            _teaches = teaches;
            if (_key) _key.gameObject.SetActive(teaches);

            // Every path that changes whether the key is there also settles whether it is live,
            // so a mode that only ever calls one of the two cannot leave a key greyed for the
            // rest of a run — which is what a board built after its header would have done.
            Refresh();
        }

        /// <summary>
        /// Greys the review key while the board is not in a state to take a lesson over it.
        ///
        /// <para>
        /// Called from a mode's own repaint rather than polled, because both modes already
        /// repaint on exactly the edges that matter — a glade's latch raises
        /// <c>BoardView.OnChanged</c>, and a weave repaints whenever its view reports a change.
        /// Cheap to call often and guarded against setting the same value twice, since
        /// <see cref="Btn.Interactable"/> repaints the face.
        /// </para>
        /// <para>
        /// Greyed rather than hidden: a control that comes and goes as a cascade plays is a
        /// control the player cannot learn the position of.
        /// </para>
        /// </summary>
        public void Refresh()
        {
            if (!_key || !_run) return;

            bool live = !_teaching && _run.Teachable;
            if (live == _live) return;

            _live = live;
            _key.Interactable = live;
        }
    }
}
