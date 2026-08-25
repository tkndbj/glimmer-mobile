using System.Collections.Generic;
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
    /// would also own the order they go up in, the latch on its board and the moment its clock
    /// is allowed to start — and it would own all three again in the next mode. Here a mode
    /// says what it has to teach and <see cref="RunScreen"/> owns the sequence.
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
    /// The screen a run is played on, whichever mode it belongs to.
    ///
    /// <para>
    /// <b>It exists so the panels around a run do not have to be written twice.</b> The defeat
    /// panel, the pause menu and the forfeit prompt all need to be able to say "try again",
    /// "restart", "back to the map" and "carry on" — and they used to say them to a
    /// <c>PlayScreen</c> specifically, which meant a second mode either duplicated three panels
    /// or went without them. Duplicating was the worse option by some distance: those panels
    /// carry the heart accounting, and two copies of a rule about charging players is exactly
    /// what invariant 9a is about.
    /// </para>
    /// <para>
    /// A base class rather than an interface, and that is a Unity detail worth stating: the
    /// panels hold a reference across frames and test it with <c>if (Screen)</c>, which is
    /// <c>UnityEngine.Object</c>'s lifetime check and the only one that answers correctly for a
    /// screen that has been destroyed underneath them. An interface reference would test as
    /// non-null on a dead object and call into it.
    /// </para>
    /// <para>
    /// <b>It also owns the moment a run is allowed to begin</b>, which used to be each mode's
    /// own business and was wrong in both of them. See <see cref="Hold"/>.
    /// </para>
    /// </summary>
    public abstract class RunScreen : View
    {
        /// <summary>Another go after the run was declared lost.</summary>
        public abstract void RetryAfterDefeat();

        /// <summary>Put the level back as it started. The run continues and is still owed for.</summary>
        public abstract void RestartLevel();

        /// <summary>Leave for the map, confirming first if the run has been paid for.</summary>
        public abstract void LeaveToMap();

        /// <summary>Leave for the hub, on the same terms.</summary>
        public abstract void LeaveToHome();

        /// <summary>Hand the level back after a panel that latched it.</summary>
        public abstract void Resume();

        // ------------------------------------------------------------ when a run may begin
        /// <summary>
        /// Why this run may not be under way yet. A mode's clock asks this before it starts
        /// and before it advances.
        ///
        /// <para>
        /// <b>It is held from construction and released here, never by a mode.</b> Both modes
        /// used to find the start edge by polling their own board's latch, which is a boolean
        /// several things write — and the one that wrote last was an animation. A first-timer's
        /// tip latched the board at the moment the screen was presented; the board's intro
        /// sweep, scheduled earlier from a different object, unlatched it a beat later; and the
        /// countdown then ran for as long as the player took to read a lesson they are only
        /// ever shown once. On the weave the leak was smaller and had the same cause — a grove
        /// is playable from the frame it is built, so the clock ran for the whole of the iris
        /// opening over it, before the player had seen anything.
        /// </para>
        /// <para>
        /// So the answer is a latch nothing else writes, and the modes ask it <em>in addition
        /// to</em> their own — a board that is still animating is not playable either, and that
        /// stays their business. See <see cref="RunHold"/> for why a leak here is the safe
        /// direction.
        /// </para>
        /// </summary>
        protected RunHold Hold { get; } = new RunHold(RunHold.Opening);

        /// <summary>
        /// Gives a run's clock one frame of play, if it is allowed one. Returns whether it got
        /// it, so a caller can skip everything else it does per running frame.
        ///
        /// <para>
        /// <b>A funnel rather than a convention.</b> Both edges a clock has — the moment it
        /// starts and every frame it advances — are behind one call that asks
        /// <see cref="Hold"/> first, so a mode cannot run its clock without the question being
        /// asked. The alternative is each mode remembering to consult a latch in its own
        /// <c>Update</c>, which is exactly the shape of rule this project has paid for twice:
        /// the pause menu that only unlatched from its buttons, and the asset scope only one of
        /// two screens released.
        /// </para>
        /// <para>
        /// <paramref name="playable"/> is the mode's own half of the answer, and it stays the
        /// mode's business: a board still flying in, a cascade playing out, a panel over the
        /// top. This one adds the half no mode can see — whether the run has been allowed to
        /// begin at all.
        /// </para>
        /// </summary>
        protected bool Tick(RunClock clock, bool playable)
        {
            if (clock == null || !playable || Hold.Held) return false;

            // Idempotent, so the start edge cannot be missed or taken twice — which is why the
            // clock can be polled like this rather than told once by whoever spots the edge.
            clock.Start();

            // Unscaled, like every clock here: a run must not stretch because something paused
            // the game underneath it.
            clock.Advance(Time.unscaledDeltaTime);
            return true;
        }

        /// <summary>
        /// What this run must teach before it begins, in the order it should be taught. Empty
        /// for the overwhelming majority of runs, which teach nothing.
        ///
        /// <para>
        /// Filled rather than returned so a mode that teaches nothing allocates nothing, and
        /// asked <em>once</em>, at the moment the screen is presented — by which time the board
        /// exists and can be pointed at. A mode resolves its own targets here: the scan lives
        /// in Domain and knows nothing about tiles or pills.
        /// </para>
        /// </summary>
        protected virtual void Lessons(List<Lesson> into) { }

        /// <summary>
        /// The level's flavour line, or null for a mode that has none.
        ///
        /// Shown only when nothing is being taught. A brand new idea outranks a line of
        /// flavour: both at once is two things to read before the first move, and the tip is
        /// the one that is only ever offered once.
        /// </summary>
        protected virtual string Flavour => null;

        /// <summary>How long the flavour line stays up.</summary>
        protected virtual float FlavourSeconds => 6f;

        /// <summary>
        /// How long to wait before the first lesson appears, so the board it points at has
        /// finished arriving. A mode whose entrance is longer overrides it.
        /// </summary>
        protected virtual float LessonDelay => .6f;

        /// <summary>
        /// Latches this mode's board while a lesson is up, and hands it back afterwards.
        ///
        /// <para>
        /// Called exactly once each way, from the two edges of the teaching sequence, so no
        /// mode has to pair them itself. An implementation must refuse to hand back a board
        /// whose run has already ended — a lesson dismissed over a finished board must not
        /// make it live again.
        /// </para>
        /// </summary>
        protected virtual void Latch(bool latched) { }

        /// <summary>A beat between two lessons, so the second does not read as the first flickering.</summary>
        const float BetweenLessons = .18f;

        /// <summary>Long enough for the screen to settle before a line of flavour lands on it.</summary>
        const float FlavourDelay = .4f;

        readonly List<Lesson> _lessons = new List<Lesson>(2);
        int _taught;

        /// <summary>
        /// Teaches whatever this run brings that the player has never met, then lets the run
        /// begin — in that order, for every mode, without any of them arranging it.
        ///
        /// <para>
        /// <b>Sealed.</b> The ordering is the whole point of it, and an override that forgot to
        /// chain would put back a countdown running behind a modal. A mode contributes through
        /// <see cref="Lessons"/> and <see cref="Flavour"/> instead, which are declarations and
        /// cannot be got in the wrong order.
        /// </para>
        /// </summary>
        public sealed override void OnPresented()
        {
            _lessons.Clear();
            _taught = 0;
            Lessons(_lessons);

            if (_lessons.Count == 0)
            {
                Begin();
                SayFlavour();
                return;
            }

            // Taken before the opening hold is released rather than after, so the run is never
            // momentarily free between the two. One frame of "free" is one frame of clock, and
            // on a mode whose start edge is polled it is also the edge itself.
            Hold.Take(RunHold.Teaching);
            Hold.Release(RunHold.Opening);

            Latch(true);
            Tween.After(LessonDelay, ShowLesson, this);
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
            if (!this) return;

            if (_taught >= _lessons.Count) { Taught(); return; }

            var lesson = _lessons[_taught++];

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = lesson.Mechanic;
                v.Target = lesson.Target;
                v.Trace = lesson.Trace;
                v.TraceTint = lesson.Tint;
                v.TraceCells = lesson.Cells;
                v.Dismissed = () => Tween.After(BetweenLessons, ShowLesson, this);
            });
        }

        /// <summary>The last lesson is closed: hand the board back, then let the run begin.</summary>
        void Taught()
        {
            Latch(false);
            Begin();
        }

        /// <summary>
        /// Lets the run begin. Named reasons rather than <c>ReleaseAll</c>, so a hold added
        /// later for something else is not swept away by a path that knows nothing about it.
        /// </summary>
        void Begin()
        {
            Hold.Release(RunHold.Teaching);
            Hold.Release(RunHold.Opening);
        }

        void SayFlavour()
        {
            string line = Flavour;
            if (string.IsNullOrEmpty(line)) return;

            float hold = FlavourSeconds;
            Tween.After(FlavourDelay,
                        () => { if (this) Scenery.Toast(Content, line, Pal.Cream, hold); }, this);
        }
    }
}
