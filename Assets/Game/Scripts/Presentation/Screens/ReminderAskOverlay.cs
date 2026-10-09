using GlimmerGrove.Analytics;
using GlimmerGrove.Localization;
using GlimmerGrove.Notifications;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Our own "remind me" panel, put in front of the OS's dialog (invariant 50p).
    ///
    /// <para>
    /// <b>An offer, not a confirmation</b> - <c>ContinueOverlay</c>'s standing - so the count of
    /// three confirmations is unchanged: "not now" costs nothing, and it is the answer that keeps
    /// the OS's one-shot dialog unspent. Raised by <see cref="ReminderMoment"/> and by nothing
    /// else, which has already marked it shown and decided the <see cref="Route"/>.
    /// </para>
    /// <para>
    /// <b>Two routes, one panel.</b> <see cref="AskRoute.System"/>: the OS will still draw its
    /// dialog, and yes raises it. <see cref="AskRoute.Settings"/>: the OS never will again, so
    /// the sentence says where the switch is and yes opens the OS settings page - the only
    /// control that can change that answer. The panel closes first in both, because both hand
    /// the screen to something native and a modal left drawn underneath reads as stuck.
    /// </para>
    /// <para>
    /// <b>The picture is the promise</b>: the grandest closed chest from the task ladder, in a
    /// glow, breathing - kit art, nothing drawn in code. The scrim does not dismiss, so a stray
    /// tap is never taken as an answer; back is "not now".
    /// </para>
    /// </summary>
    public sealed class ReminderAskOverlay : ModalView
    {
        /// <summary>What yes does. Set by <see cref="ReminderMoment"/>.</summary>
        public AskRoute Route = AskRoute.System;

        const float PanelW = 880f, PanelH = 880f;

        /// <summary>
        /// The chest's box, and how far it is lifted so the drawn chest - not the sprite's empty
        /// lid room - sits on the seat: (244/2 - (82+238)/2) rows at 280/244 a row is about 44.
        /// </summary>
        const float ChestBox = 280f, ChestLift = 44f;

        static readonly Color Ink = new Color(.36f, .25f, .18f);

        bool _answered;

        protected override void Build()
        {
            bool settings = Route == AskRoute.Settings;

            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.reminders.ask_title"), dismissOnScrim: false);

            // The chest. A closed-chest sprite is 176x244 with the chest in its lower 156 rows (the
            // lid's room when it opens), so it is drawn in a box sized for that and lifted by
            // ChestLift, which puts the visible chest - about 170 across - on the seat's centre.
            // Seat -225 from the top; the chest spans about -135..-315 and the glow -75..-375,
            // clearing the ribbon above and the sentence below, which begins at -345 (the glow is
            // light, and fades out well before it reaches the type).
            var seat = UIKit.Box("Chest", Panel, new Vector2(200f, 200f), new Vector2(.5f, 1f),
                                 new Vector2(0f, -225f));

            UIKit.Img("Glow", seat, Art.Glow(96, 2.2f), new Color(1f, .82f, .36f, .5f),
                      Vector2.one * 300f, new Vector2(.5f, .5f), Vector2.zero);

            var chest = UIKit.Img("Icon", seat, Art.S(ChestIcon()), Color.white,
                                  Vector2.one * ChestBox, new Vector2(.5f, .5f), new Vector2(0f, ChestLift));
            chest.preserveAspect = true;
            Tween.Breathe(chest.transform, .04f, 2.2f);

            // The sentence, centred in a band sized to three lines at 32: a translation that runs
            // to two or four stays centred, and Shrinkable takes a longer one down before it can
            // reach the key. Band -345..-525; the yes key's top edge is at -(880 - 301) = -579.
            UIKit.Shrinkable(
                UIKit.Titled("Why", Panel,
                             Loc.Get(settings ? "ui.reminders.settings_body" : "ui.reminders.ask_body"), 32,
                             Ink, TextAnchor.MiddleCenter, new Vector2(700f, 180f),
                             new Vector2(.5f, 1f), new Vector2(0f, -435f),
                             outline: 0f, shadow: 0f, wrap: true), 22);

            // The two answers, anchored to the foot where ForfeitOverlay puts its own, so every
            // two-key panel in the game reads alike. Green is yes: this panel's affirmative is
            // the thing it is offering.
            UIKit.Shrinkable(UIKit.TextButton("Yes", Panel, "btn_green",
                                              Loc.Get(settings ? "ui.reminders.open_settings" : "ui.reminders.ask_yes"),
                                              46, new Vector2(620f, 138f), new Vector2(.5f, 0f),
                                              new Vector2(0f, 232f), Yes).Label, 26);

            UIKit.Shrinkable(UIKit.TextButton("Later", Panel, "btn_blue", Loc.Get("ui.reminders.not_now"),
                                              42, new Vector2(620f, 126f), new Vector2(.5f, 0f),
                                              new Vector2(0f, 92f), Later).Label, 24);
        }

        /// <summary>
        /// The grandest closed chest the task ladder names, or the kit's plain chest when no ladder
        /// is authored - absent content must cost the picture, never the panel.
        /// </summary>
        static string ChestIcon()
        {
            var tiers = ProgressionRules.Table?.Tasks?.Tiers;
            return tiers != null && tiers.Count > 0 ? tiers[tiers.Count - 1].Icon : "Ui/ic_chest";
        }

        void Yes()
        {
            if (_answered) return;
            _answered = true;

            Telemetry.Track("notification_ask_answered", "route", NotificationAsk.Id(Route), "answer", "yes");

            if (Route == AskRoute.Settings)
            {
                Close(Notify.OpenSettings);
                return;
            }

            // Yes is also an answer to our own switch, so a player who had never touched it is
            // recorded as wanting reminders before the OS is asked.
            Close(() =>
            {
                NotificationOptIn.SetWanted(true);
                Notify.Ask();
            });
        }

        void Later()
        {
            if (_answered) return;
            _answered = true;

            Telemetry.Track("notification_ask_answered", "route", NotificationAsk.Id(Route), "answer", "later");
            Close();
        }

        /// <summary>Back is "not now", the answer that spends nothing.</summary>
        public override bool OnBack() { Later(); return true; }
    }
}
