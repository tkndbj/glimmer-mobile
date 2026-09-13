using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// One lesson a <em>screen</em> has to teach: what is being taught, what on that screen it
    /// is about, and anything its sentence has to be told.
    ///
    /// <para>
    /// A description rather than a call, which is <see cref="Lesson"/>'s shape and for its
    /// reason: a screen says what it wants taught and <see cref="ScreenLessons"/> owns the order,
    /// the beat between panels and the chaining.
    /// </para>
    /// </summary>
    public struct ScreenLesson
    {
        /// <summary>What is being taught. Its strings come from its id.</summary>
        public Mechanic Mechanic;

        /// <summary>The control to ring, or null to teach about the screen as a whole.</summary>
        public RectTransform Target;

        /// <summary>
        /// What the body's placeholders stand for, or null for a sentence with none.
        /// See <c>TipOverlay.BodyArgs</c>.
        /// </summary>
        public object[] Args;

        public ScreenLesson(Mechanic mechanic, RectTransform target, object[] args = null)
        {
            Mechanic = mechanic;
            Target = target;

            // Empty and absent are one thing here, so a caller may hand over whatever `params`
            // gave it without the overlay having to know the difference.
            Args = args != null && args.Length > 0 ? args : null;
        }
    }

    /// <summary>
    /// Teaches a screen's own lessons, one after another.
    ///
    /// <para>
    /// <b>The same job <see cref="RunLessons"/> does for a board, without the half that is about
    /// a run.</b> That class owns a hold and a latch because a board must not advance while a tip
    /// is up; a screen has neither - nothing here is running - so what is left is the part both
    /// wanted and the part this project had already written twice: chain on dismissal, leave a
    /// beat between panels, and never raise two at once. The grove had one copy and the map had a
    /// third of one, and the map and the shelf each growing their own would have made four places
    /// that can disagree about when a lesson is allowed to appear.
    /// </para>
    /// <para>
    /// <b>Chained on dismissal rather than raised together</b>, which is that class's rule: two
    /// modals at once means meeting the second before reading the first.
    /// <c>TipOverlay.Dismissed</c> fires exactly once however the panel goes away - accepted,
    /// backed out of, or destroyed under a navigation - so a chain cannot stall.
    /// </para>
    /// </summary>
    public static class ScreenLessons
    {
        /// <summary>A beat between two lessons, so the second does not read as the first flickering.</summary>
        public const float Between = .18f;

        /// <summary>
        /// How often to look again while something else is over the screen.
        ///
        /// <para>
        /// <c>RunLessons.WhileCovered</c>'s number and its reason, which this had gone without:
        /// a chain is walked across seconds of a player's time and a panel they asked for —
        /// a chest, an offer, a reward — can arrive between two of its links. Raised anyway the
        /// tip lands underneath it (<c>ModalLayer.Teaching</c> is the bottom of the stack), where
        /// it cuts a spotlight nobody can see and is marked seen on a frame nobody looked at.
        /// Waiting costs nothing: none of these screens is running anything.
        /// </para>
        /// </summary>
        const float WhileCovered = .2f;

        /// <summary>
        /// Queues a lesson about <paramref name="target"/> when the player has never met it.
        ///
        /// <para>
        /// <b>Nothing is queued over a control that is not there.</b> Every one of these screens
        /// draws chrome conditionally - a mode pill only while the catalog holds two modes, a
        /// track pill only while a mode holds two ladders, a loadout bar only for a mode that has
        /// a line - and <c>TipLedger</c> is a once-in-a-lifetime record joined across every device
        /// the player owns. A tip raised over an absent control draws no ring, teaches nothing and
        /// is spent for good, on the very install that will need it later. So an absent target is
        /// not a decision at all: nothing is queued and nothing is marked.
        /// </para>
        /// <para>
        /// It is marked seen by <c>TipOverlay</c> on the OK button rather than here, for that
        /// overlay's reason: a player interrupted mid-tip is taught next time instead of never.
        /// </para>
        /// </summary>
        public static void Offer(List<ScreenLesson> queue, Mechanic mechanic,
                                 RectTransform target, params object[] args)
        {
            if (queue == null || target == null) return;
            if (TipLedger.HasSeen(mechanic)) return;

            queue.Add(Compose(mechanic, target, args));
        }

        /// <summary>
        /// Queues a lesson about the screen itself, which has nothing to ring.
        ///
        /// <para>
        /// Separate from <see cref="Offer"/> rather than a null target handed to it, because the
        /// two are opposite readings of the same absence: a missing control means <em>do not teach
        /// this</em>, and a lesson about a whole screen means <em>there is nothing to point
        /// at</em>. One method answering both would make the first silently unenforceable.
        /// </para>
        /// </summary>
        public static void OfferScreen(List<ScreenLesson> queue, Mechanic mechanic)
        {
            if (queue == null) return;
            if (TipLedger.HasSeen(mechanic)) return;

            queue.Add(Compose(mechanic, null, null));
        }

        /// <summary>
        /// Queues a lesson whether or not the player has met it: what an info key asks for.
        ///
        /// <para>
        /// The ledger is what stops a lesson arriving unasked a second time, and a player who
        /// pressed a button has asked. It still refuses an absent control, for <see cref="Offer"/>'s
        /// other reason - a ring round nothing is a panel about nothing.
        /// </para>
        /// </summary>
        public static void Add(List<ScreenLesson> queue, Mechanic mechanic,
                               RectTransform target, params object[] args)
        {
            if (queue == null || target == null) return;

            queue.Add(Compose(mechanic, target, args));
        }

        /// <summary>
        /// One lesson, with its call site held to the number of values it says it needs.
        ///
        /// <para>
        /// <c>ContentValidation</c> holds the <em>string</em> to <see cref="Mechanic.Args"/>; this
        /// is the other end of the same rule. A caller that forgets an argument hands
        /// <c>TipOverlay</c> a body it cannot compose, <c>Loc.Format</c> catches that and returns
        /// the pattern, and a player is shown a literal "{0}" on a panel they get once. Reported
        /// rather than refused, because losing the lesson is the worse of the two outcomes and
        /// the sentence is still readable without its number.
        /// </para>
        /// </summary>
        static ScreenLesson Compose(Mechanic mechanic, RectTransform target, object[] args)
        {
            int given = args == null ? 0 : args.Length;

            if (given != mechanic.Args)
                Debug.LogError($"lesson '{mechanic.Id}' is composed with {mechanic.Args} value(s) "
                               + $"and was given {given}; its body will draw its placeholders");

            return new ScreenLesson(mechanic, target, args);
        }

        /// <summary>
        /// Shows <paramref name="queue"/> in order, each on the dismissal of the one before.
        /// </summary>
        /// <param name="owner">
        /// The screen. Every wait is bound to it, so a chain dies with the screen rather than
        /// raising a panel over whatever replaced it.
        /// </param>
        /// <param name="finished">Run once the last panel has gone, or null.</param>
        /// <param name="beforeEach">
        /// Run immediately before each panel, for a screen that has to put something away first -
        /// the grove closes its draft, or a bar floating over the field would be lit by a hole cut
        /// for something else. Null for a screen with nothing to tidy.
        /// </param>
        public static void Show(MonoBehaviour owner, IList<ScreenLesson> queue,
                                System.Action finished = null, System.Action beforeEach = null)
            => Step(owner, queue, 0, finished, beforeEach);

        static void Step(MonoBehaviour owner, IList<ScreenLesson> queue, int index,
                         System.Action finished, System.Action beforeEach)
        {
            // A screen torn down mid-chain simply stops, and nothing has to be released: unlike a
            // run there is no clock being held, so there is no caller left waiting.
            if (owner == null) return;

            if (queue == null || index >= queue.Count) { finished?.Invoke(); return; }

            // Something the player asked for is on top. Wait for it rather than teaching
            // underneath it — see WhileCovered, and RunLessons.ShowLesson, whose rule this is.
            if (Flow.HasModalAbove(ModalLayer.Teaching))
            {
                Tween.After(WhileCovered,
                            () => Step(owner, queue, index, finished, beforeEach), owner);
                return;
            }

            beforeEach?.Invoke();

            var lesson = queue[index];

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = lesson.Mechanic;
                v.Target = lesson.Target;
                v.BodyArgs = lesson.Args;

                v.Dismissed = () => Tween.After(
                    Between, () => Step(owner, queue, index + 1, finished, beforeEach), owner);
            });
        }
    }
}
