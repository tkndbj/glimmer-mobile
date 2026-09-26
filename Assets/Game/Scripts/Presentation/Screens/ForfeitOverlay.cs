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
    /// <para>
    /// <b>It is also the daily challenges' confirmation, and that is what keeps the count at
    /// three.</b> A challenge stakes one of the day's plays rather than a heart (invariant 56g:
    /// spent at the first move), and leaving a board that has been moved on is the same
    /// question — a committed run being abandoned — with a different price on the tag. So
    /// <see cref="Stake"/> picks the sentence and the picture, and nothing else about the
    /// panel changes; a fourth confirmation would be this one with a new name.
    /// </para>
    /// </summary>
    public sealed class ForfeitOverlay : ModalView
    {
        /// <summary>What the player is about to do, and what it will cost them.</summary>
        public enum Kind { Leave, Restart }

        /// <summary>What is on the price tag: a heart (a run), or one of today's plays (a challenge).</summary>
        public enum Stakes { Heart, Play }

        public Kind Choice = Kind.Leave;

        /// <summary>What leaving costs. Only a challenge sets <see cref="Stakes.Play"/>, and only for a leave.</summary>
        public Stakes Stake = Stakes.Heart;

        /// <summary>
        /// Whether this run was bought at the gate rather than owed for at its ending
        /// (<c>HeartPrice.Entry</c>) — a lane with no ladder, invariant 43.
        ///
        /// <para>
        /// <b>It changes the sentence and nothing else.</b> The price tag below still says -1 and
        /// is still true: a restart takes a heart whichever way the run was priced, because the
        /// fresh one is bought at the gate like every other. What is not true of such a lane is
        /// the ordinary body's reassurance that this is "the same as running out of turns" —
        /// running out of turns there costs nothing, and a panel that says otherwise is teaching
        /// a player a rule the game does not have.
        /// </para>
        /// <para>
        /// Only the restart reads it. Leaving a prepaid run costs nothing, so this panel is never
        /// raised for it (<c>RunScreen.ConfirmForfeit</c>).
        /// </para>
        /// </summary>
        public bool Prepaid;

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
        static string TitleKey(Kind kind, Stakes stake)
        {
            if (stake == Stakes.Play) return "ui.forfeit.play_title";
            return kind == Kind.Restart ? "ui.forfeit.restart_title" : "ui.forfeit.leave_title";
        }

        static string BodyKey(Kind kind, Stakes stake, bool prepaid)
        {
            if (stake == Stakes.Play) return "ui.forfeit.play_body";
            if (kind != Kind.Restart) return "ui.forfeit.leave_body";
            return prepaid ? "ui.forfeit.restart_watch_body" : "ui.forfeit.restart_body";
        }

        static string ConfirmKey(Kind kind)
            => kind == Kind.Restart ? "ui.forfeit.restart_go" : "ui.forfeit.leave_go";

        protected override void Build()
        {
            // No scrim dismissal. This is a question with a price on it, and a stray tap
            // outside the panel is not an answer to it — the same call AccountOverlay's
            // destructive prompt makes.
            MakePanel(new Vector2(880f, 800f), Loc.Get(TitleKey(Choice, Stake)), dismissOnScrim: false);

            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel, Loc.Get(BodyKey(Choice, Stake, Prepaid)), 32,
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

            // The tag is the same shape whatever is on it: a glow, the thing, and -1. A play
            // wears the Battle key's mark in gold — the mark a challenge is entered under —
            // where a run wears the heart in rose.
            bool play = Stake == Stakes.Play;
            var ink = play ? Pal.Gold : Pal.Rose;
            var glowTint = play ? new Color(1f, .78f, .24f, .28f) : new Color(.91f, .38f, .35f, .30f);

            UIKit.Img("Glow", seat, Art.Glow(96, 2.2f), glowTint,
                      Vector2.one * 170f, new Vector2(.5f, .5f), Vector2.zero);

            // The play's tag is drawn larger than the heart's, with a few units of air between
            // the mark and the figure (the owner's reading of the first cut: "make them bigger",
            // "a little gap"). The heart keeps the geometry the run's panel shipped with.
            float tokenSize = play ? 120f : 92f;
            float tokenX = play ? -58f : -40f;
            const float gap = 8f;

            var token = UIKit.Img(play ? "Play" : "Heart", seat, Art.S(play ? "Ui/ic_battle" : "Ui/ic_heart"), ink,
                                  Vector2.one * tokenSize, new Vector2(.5f, .5f), new Vector2(tokenX, 0f));
            token.preserveAspect = true;

            var minusBox = play ? new Vector2(110f, 84f) : new Vector2(90f, 68f);
            float minusX = play ? tokenX + tokenSize * .5f + gap + minusBox.x * .5f : 46f;
            UIKit.Titled("Minus", seat, "-1", play ? 64 : 52, ink, TextAnchor.MiddleLeft,
                         minusBox, new Vector2(.5f, .5f), new Vector2(minusX, 0f), 4f, 3f);

            Tween.Breathe(token.transform, .05f, 1.9f);

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
