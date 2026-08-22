using System;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// "Report this name?" — the one confirmation in this game that guards an act taken
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
    /// <b>The copy is what stops this being a weapon.</b> It says what a report is for and,
    /// more importantly, says what it does not do: nothing about the reported grove changes for
    /// the reporter. Without that line the control reads as a block button, which is what it
    /// would then be used as — and a report queue full of "I did not like their score" is a
    /// queue that hides nothing real.
    /// </para>
    /// <para>
    /// The affirmative is <b>red and second</b>, which inverts <c>ForfeitOverlay</c>'s layout
    /// on purpose. There the green "keep playing" is the affirmative because continuing is what
    /// the player wants; here the cheap answer is to walk away, so it takes the green and the
    /// resting position.
    /// </para>
    /// </summary>
    public sealed class ReportNameOverlay : ModalView
    {
        /// <summary>Run when the player confirms. Never called on a dismissal.</summary>
        public Action OnConfirm;

        bool _answered;

        protected override void Build()
        {
            // No scrim dismissal, for ForfeitOverlay's reason: this is a question with a
            // consequence, and a stray tap outside the panel is not an answer to it.
            MakePanel(new Vector2(880f, 700f), Loc.Get("ui.visit.report_confirm_title"),
                      dismissOnScrim: false);

            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Get("ui.visit.report_confirm_body"), 30,
                             new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                             new Vector2(680f, 240f), new Vector2(.5f, 1f), new Vector2(0f, -196f),
                             outline: 0f, shadow: 0f, wrap: true), 21);

            UIKit.TextButton("Cancel", Panel, "btn_green", Loc.Get("ui.common.cancel"), 44,
                             new Vector2(620f, 138f), new Vector2(.5f, 0f), new Vector2(0f, 232f),
                             Cancel);

            UIKit.TextButton("Go", Panel, "btn_red", Loc.Get("ui.visit.report_confirm_yes"), 40,
                             new Vector2(620f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 92f),
                             Confirm);
        }

        void Confirm()
        {
            if (_answered) return;
            _answered = true;

            var go = OnConfirm;
            OnConfirm = null;
            Close(() => go?.Invoke());
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
