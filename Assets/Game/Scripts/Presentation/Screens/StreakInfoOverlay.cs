using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The three questions the streak page cannot answer by drawing itself.
    ///
    /// <para>
    /// A board of nights shows <em>what</em> is on offer perfectly well and says nothing
    /// about the rules behind it. Three things were being guessed at. That a night is
    /// earned by finishing a glade — not by opening the app, and not only by winning.
    /// What a heart boost actually does, which no glyph can carry and which the amount
    /// ("24h") only half explains. And what happens at the end of the ladder, which is the
    /// one place a player could reasonably fear the reward stops.
    /// </para>
    /// <para>
    /// Every number in it is read from the rules rather than written into the copy —
    /// the length of the ladder, the refill hours, the boost ceiling, what the tail pays.
    /// A panel that explains the game is the easiest thing in a project to leave behind
    /// when the content is retuned, and the only defence is for it to have no numbers of
    /// its own to get wrong.
    /// </para>
    /// </summary>
    public sealed class StreakInfoOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, 1240f), Loc.Get("ui.streak.info_title"));

            var ladder = DailyStreak.Ladder;
            int rungs = Mathf.Max(1, ladder.Length);

            // What the night after the last one pays, asked of the table rather than
            // assumed. The ladder laps, so this is night one's rung — but that is the
            // table's rule to state, not this panel's to remember, and asking is what keeps
            // the sentence true if the lap is ever retuned.
            var beyond = ladder.Rung(rungs + 1).AsDrop();
            string beyondPay = beyond.IsValid
                ? RewardArt.Amount(beyond) + " " + RewardArt.Name(beyond.Kind)
                : Loc.Get("ui.streak.info_cycle_nothing");

            Section(-208f, "ic_play", "ui.streak.info_earn_title",
                    Loc.Get("ui.streak.info_earn_body"));

            Section(-544f, "ic_heart_boost", "ui.streak.info_boost_title",
                    Loc.Format("ui.streak.info_boost_body",
                               HeartRules.RefillSeconds / 3600,
                               HeartRules.BoostedRefillSeconds / 3600,
                               HeartRules.MaxBoostHours));

            Section(-880f, "crest_gold", "ui.streak.info_cycle_title",
                    Loc.Format("ui.streak.info_cycle_body", rungs, beyondPay));

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

        /// <summary>
        /// One answer: a glyph, a heading and a paragraph.
        ///
        /// The glyph on each row is the one the game already uses for the thing being
        /// explained — the boost row wears <c>ic_heart_boost</c> — so reading this panel
        /// also teaches what the icons on the board mean, which is half of what a player
        /// came here to find out.
        /// </summary>
        void Section(float y, string icon, string titleKey, string body)
        {
            var host = UIKit.Box("S" + titleKey, Panel, new Vector2(PanelW - 90f, 300f),
                                 new Vector2(.5f, 1f), new Vector2(0f, y));

            var seat = UIKit.Img("Seat", host, Art.Disc(96), new Color(.94f, .84f, .64f, .85f),
                                 new Vector2(112f, 112f), new Vector2(0f, 1f), new Vector2(66f, -58f));

            var glyph = UIKit.Img("Icon", seat.transform, Art.S("Ui/" + icon), Color.white,
                                  new Vector2(76f, 76f), new Vector2(.5f, .5f), Vector2.zero);
            glyph.preserveAspect = true;

            UIKit.Shrinkable(
                UIKit.Titled("H", host, Loc.Get(titleKey).ToUpperInvariant(), 34, Head,
                             TextAnchor.MiddleLeft, new Vector2(BodyW, 44f),
                             new Vector2(0f, 1f), new Vector2(148f + BodyW * .5f, -34f), 0f, 0f), 22);

            // Shrinkable as well as wrapped: these are the longest strings in the game, and
            // a translation half again the length of the English would otherwise run out
            // of the paragraph and into the row below it.
            UIKit.Shrinkable(
                UIKit.Titled("B", host, body, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(BodyW, 190f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -152f), 0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
