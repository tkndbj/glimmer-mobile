using System;
using GlimmerGrove.Localization;
using GlimmerGrove.Social;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// "What is wrong here?" — the one confirmation in this game that guards an act taken
    /// against another person.
    ///
    /// <para>
    /// <b>Confirmed at all, where almost nothing else here is.</b> This game deliberately
    /// avoids confirmations: the payment sheet is the confirmation for a purchase, a tap that
    /// costs nothing should not ask, and a dialog people learn to dismiss is a dialog that
    /// fails when it matters. The exceptions are the acts that cannot be undone from the screen
    /// that made them — <c>ForfeitOverlay</c> guards the one that costs a heart, and this
    /// guards the one that reaches somebody else's account. A mistapped report is not
    /// retractable by the person who made it.
    /// </para>
    /// <para>
    /// <b>The chooser and the confirmation are one panel, and that is what keeps the count at
    /// three.</b> A keeper puts two things in front of strangers — a name and a grovement — and
    /// the obvious shape is a chooser that opens a confirmation, which is two taps of ceremony
    /// for one act and teaches people to tap through the second. So each affirmative names its
    /// own subject: picking one <em>is</em> the confirmation, because nothing on this panel can
    /// be tapped by accident and mean something other than it says.
    /// </para>
    /// <para>
    /// <b>The copy is what stops this being a weapon, and it is one paragraph.</b> It says what
    /// a report is for and, more importantly, says what it does not do: nothing about the
    /// reported keeper changes for the reporter. Without that line the control reads as a block
    /// button, which is what it would then be used as — and a report queue full of "I did not
    /// like their score" is a queue that hides nothing real. <b>It is said once, above both
    /// keys.</b> The keys used to carry a line each as well, which explained two self-describing
    /// words at the cost of the one sentence that actually matters having three others beside
    /// it.
    /// </para>
    /// <para>
    /// <b>The cheap answer takes the green and the resting position</b>, which inverts
    /// <c>ForfeitOverlay</c>'s layout on purpose: there the green "keep playing" is the
    /// affirmative because continuing is what the player wants, and here walking away is. The
    /// two reports are red, and a subject this device has already reported is drawn dead rather
    /// than hidden — a control that disappears between one visit and the next reads as a bug,
    /// where a spent one reads as an answer.
    /// </para>
    /// </summary>
    public sealed class ReportOverlay : ModalView
    {
        /// <summary>
        /// Run with the subject the player picked. Never called on a dismissal.
        ///
        /// One callback taking a subject rather than one per subject, because a third one is
        /// then a row in <see cref="ReportSubjects.All"/> and nothing else — the shape
        /// <c>ScreenLessons</c> takes for the same reason (invariant 6a: a screen that offers
        /// several owns none of the sequencing).
        /// </summary>
        public Action<ReportSubject> OnConfirm;

        /// <summary>
        /// Which keeper this is about, so the panel can grey the subjects already reported.
        ///
        /// Display only: the caller owns the id that is actually sent, and reading it back off a
        /// panel would be one more place a report could be aimed at the wrong account.
        /// </summary>
        public string KeeperId = string.Empty;

        bool _answered;

        /// <summary>
        /// The room one subject takes: the button and the air after it.
        ///
        /// Named because the panel's height is the sum of what it actually draws rather than a
        /// number somebody typed — a third subject is one row in <see cref="ReportSubjects.All"/>
        /// and no arithmetic anywhere, which is what stops a key being printed through the one
        /// below it the day one is added. <c>AccountOverlay</c>'s measure-then-build shape.
        ///
        /// <para>
        /// <b>Each subject used to carry an explanatory line under its key and no longer does.</b>
        /// "Their name" and "Their groovement" say what they are, and the paragraph above already
        /// says what a report is for and what it does not do — so the two notes were a third and
        /// fourth sentence explaining two words each. What they cost was the panel's height and
        /// the readability of the choice itself: a key, a line, a key, a line reads as four
        /// things, where two keys read as two.
        /// </para>
        /// </summary>
        const float ButtonH = 126f, AfterSubject = 26f;
        const float TitleRow = 172f, BodyH = 150f, AfterBody = 26f;

        /// <summary>
        /// Air between the last subject and the way out.
        ///
        /// <b>Its own term rather than a larger <see cref="AfterSubject"/></b>, because the two
        /// say different things: the gap between two red keys is how far apart two choices read,
        /// and this is how far the green one stands away from them. Without it the panel's height
        /// is exactly its content, so the cancel key sits one subject-gap under the last red one
        /// and reads as a third answer to the same question.
        /// </summary>
        const float BeforeCancel = 26f;

        const float CancelH = 132f, FootMargin = 36f;
        const float Width = 880f;

        protected override void Build()
        {
            int subjects = ReportSubjects.All.Length;

            float height = TitleRow + BodyH + AfterBody
                         + subjects * (ButtonH + AfterSubject)
                         + BeforeCancel + CancelH + FootMargin;

            // No scrim dismissal, for ForfeitOverlay's reason: this is a question with a
            // consequence, and a stray tap outside the panel is not an answer to it.
            MakePanel(new Vector2(Width, height), Loc.Get("ui.report.title"),
                      dismissOnScrim: false);

            float cursor = TitleRow;

            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Get("ui.report.body"), 29,
                             new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                             new Vector2(Width - 180f, BodyH), new Vector2(.5f, 1f),
                             new Vector2(0f, -cursor),
                             outline: 0f, shadow: 0f, wrap: true), 20);

            cursor += BodyH + AfterBody;

            foreach (var subject in ReportSubjects.All)
            {
                Subject(subject, cursor);
                cursor += ButtonH + AfterSubject;
            }

            // Anchored to the foot rather than to the running cursor, so the one control that
            // must always be findable is in the same place whatever the panel is saying.
            UIKit.TextButton("Cancel", Panel, "btn_green", Loc.Get("ui.common.cancel"), 42,
                             new Vector2(Width - 260f, CancelH), new Vector2(.5f, 0f),
                             new Vector2(0f, FootMargin + CancelH * .5f), Cancel);
        }

        /// <summary>One subject: a red key that names it, and nothing else.</summary>
        void Subject(ReportSubject subject, float top)
        {
            bool sent = KeeperReports.AlreadySent(subject, KeeperId);

            var button = UIKit.TextButton(
                "Report_" + ReportSubjects.Wire(subject), Panel,
                sent ? Skins.Resting : "btn_red",
                Loc.Get(sent ? "ui.report.sent" : ReportSubjects.ButtonKey(subject)), 38,
                new Vector2(Width - 260f, ButtonH), new Vector2(.5f, 1f),
                new Vector2(0f, -(top + ButtonH * .5f)),
                () => Confirm(subject));

            UIKit.Shrinkable(button.Label, 22);
            UIKit.FitLabel(button);

            // Stays live-looking but does nothing once spent, rather than vanishing: a control
            // that disappears after a tap leaves somebody wondering whether the tap registered
            // — which is the one question this feature must not leave open, since the obvious
            // response is to report again.
            button.Interactable = !sent;
        }

        void Confirm(ReportSubject subject)
        {
            if (_answered) return;
            _answered = true;

            var go = OnConfirm;
            OnConfirm = null;
            Close(() => go?.Invoke(subject));
        }

        void Cancel()
        {
            if (_answered) return;
            _answered = true;

            OnConfirm = null;
            Close();
        }

        /// <summary>Back is the cheap answer, which here is the one that reports nobody.</summary>
        public override bool OnBack() { Cancel(); return true; }
    }
}
