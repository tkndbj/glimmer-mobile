using System;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one thing standing between a started run and the player walking away from it.
    ///
    /// <para>
    /// Restarting and leaving used to be free, and now cost a heart — see
    /// <see cref="Persistence.RunGuard"/> for why they had to. A control that was free
    /// yesterday and silently charges today is indistinguishable from a bug on the player's
    /// side of the screen, so it asks first. That is the whole job: state the price, name the
    /// action, and make staying the easy answer.
    /// </para>
    /// <para>
    /// <b>It is only ever raised on a committed run.</b> A player who opens a glade, looks at
    /// it and backs out is not charged and never sees this — see <c>PlayScreen.Committed</c>.
    /// A confirmation on an action that costs nothing is pure friction, and it would teach
    /// players to dismiss the one that does cost something without reading it.
    /// </para>
    /// <para>
    /// The green button is <em>staying</em>, not leaving. Green is the affirmative everywhere
    /// else in this game, and here the affirmative is "keep playing" — putting it on the
    /// destructive half would spend the game's own colour language on losing a heart.
    /// </para>
    /// </summary>
    public sealed class ForfeitOverlay : ModalView
    {
        /// <summary>What the player is about to do, and what it will cost them.</summary>
        public enum Kind { Leave, Restart }

        public Kind Choice = Kind.Leave;

        /// <summary>Run when the player accepts the price. Never called on a dismissal.</summary>
        public Action OnConfirm;

        /// <summary>
        /// Put back afterwards when the player declines, so the board comes off its latch.
        /// Held rather than assumed, because the two callers pause the run differently.
        /// </summary>
        public Action OnCancel;

        bool _answered;

        /// <summary>
        /// Written out rather than assembled from the enum, so the build's string checker can
        /// see every key — the reason <c>WinOverlay.RankKeys</c> is written out too.
        /// </summary>
        static string TitleKey(Kind kind)
            => kind == Kind.Restart ? "ui.forfeit.restart_title" : "ui.forfeit.leave_title";

        static string BodyKey(Kind kind)
            => kind == Kind.Restart ? "ui.forfeit.restart_body" : "ui.forfeit.leave_body";

        static string ConfirmKey(Kind kind)
            => kind == Kind.Restart ? "ui.forfeit.restart_go" : "ui.forfeit.leave_go";

        protected override void Build()
        {
            // No scrim dismissal. This is a question with a price on it, and a stray tap
            // outside the panel is not an answer to it — the same call AccountOverlay's
            // destructive prompt makes.
            MakePanel(new Vector2(880f, 800f), Loc.Get(TitleKey(Choice)), dismissOnScrim: false);

            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Get(BodyKey(Choice)), 32,
                             new Color(.36f, .25f, .18f), TextAnchor.UpperCenter,
                             new Vector2(680f, 190f), new Vector2(.5f, 1f), new Vector2(0f, -196f),
                             outline: 0f, shadow: 0f, wrap: true), 22);

            // The heart being spent, drawn once rather than described. A row of five would be
            // the defeat panel's picture of the gate; this is a single price tag.
            //
            // The band it sits in is measured, not guessed, because everything around it is
            // anchored to a different edge: the body ends at -291 from the top, and the Stay
            // button's top edge is at -(800 - 301) = -499. The seat spans -330..-450 and the
            // glow -305..-475, so it clears the copy above it and the button below it with
            // room either side. That glow is the tall part — anything moving the seat has to
            // count it, since it reaches 25px past the box on every side and was what the
            // button used to be sitting on top of.
            var seat = UIKit.Box("Cost", Panel, new Vector2(200f, 120f), new Vector2(.5f, 1f),
                                 new Vector2(0f, -390f));

            UIKit.Img("Glow", seat, Art.Glow(96, 2.2f), new Color(.91f, .38f, .35f, .30f),
                      Vector2.one * 170f, new Vector2(.5f, .5f), Vector2.zero);

            var heart = UIKit.Img("Heart", seat, Art.S("Ui/ic_heart"), Pal.Rose,
                                  Vector2.one * 92f, new Vector2(.5f, .5f), new Vector2(-40f, 0f));
            heart.preserveAspect = true;

            UIKit.Titled("Minus", seat, "-1", 52, Pal.Rose, TextAnchor.MiddleLeft,
                         new Vector2(90f, 68f), new Vector2(.5f, .5f), new Vector2(46f, 0f), 4f, 3f);

            Tween.Breathe(heart.transform, .05f, 1.9f);

            UIKit.TextButton("Stay", Panel, "btn_green", Loc.Get("ui.forfeit.stay"), 46,
                             new Vector2(620f, 138f), new Vector2(.5f, 0f), new Vector2(0f, 232f),
                             Cancel);

            UIKit.TextButton("Go", Panel, "btn_red", Loc.Get(ConfirmKey(Choice)), 42,
                             new Vector2(620f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 92f),
                             Confirm);
        }

        void Confirm()
        {
            if (_answered) return;
            _answered = true;

            var go = OnConfirm;
            OnConfirm = null;
            OnCancel = null;
            Close(() => go?.Invoke());
        }

        void Cancel()
        {
            if (_answered) return;
            _answered = true;

            var back = OnCancel;
            OnConfirm = null;
            OnCancel = null;
            Close(() => back?.Invoke());
        }

        /// <summary>Back is the cheap answer, which is the one that keeps the heart.</summary>
        public override bool OnBack() { Cancel(); return true; }
    }
}
