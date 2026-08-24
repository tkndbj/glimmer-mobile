using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What a glade actually pays, and under what rule.
    ///
    /// <para>
    /// The victory panel reports one run's payout perfectly well and says nothing about
    /// the rule behind it. Four things were being guessed at, and the second is the one
    /// that costs a player money if they guess wrong: that a glade is paid for by
    /// <em>stars</em> rather than by clears, so replaying one earns only the stars it did
    /// not already hold. Without that stated, the two honest readings are "grinding the
    /// first glade prints coins" and "replaying is pointless", and both are wrong.
    /// </para>
    /// <para>
    /// Every number in it is read from the tables — the chapter's own reward rule, the
    /// golden bands, the chest cadence — rather than written into the copy, for
    /// <see cref="StreakInfoOverlay"/>'s reason: a panel explaining the game is the first
    /// thing to go stale when the game is retuned, and the only defence is for it to have
    /// no numbers of its own to get wrong. The chapter matters here — <c>chapterRewards</c>
    /// already overrides the opening one — so the panel is told which map raised it and
    /// quotes that chapter's figures rather than the default curve's.
    /// </para>
    /// </summary>
    public sealed class GladeRewardsOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        /// <summary>Where the first row sits, and the pitch between rows.</summary>
        const float FirstRow = -180f, RowPitch = 244f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        ChapterId _chapter;

        /// <summary>Which chapter's reward rule to quote. Defaults to the table's own.</summary>
        public void For(ChapterId chapter) => _chapter = chapter;

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, 1150f), Loc.Get("ui.levels.rewards_title"));

            var table = ProgressionRules.Table;
            var rule = table.RuleFor(_chapter);

            Section(0, "ic_star", "ui.levels.rewards_stars_title",
                    Loc.Format("ui.levels.rewards_stars_body",
                               Compact.Number(rule.CreditsFor(1)),
                               Compact.Number(rule.CreditsFor(2)),
                               Compact.Number(rule.CreditsFor(3))));

            Section(1, "ic_restart", "ui.levels.rewards_again_title",
                    Loc.Get("ui.levels.rewards_again_body"));

            Section(2, "crest_gold", "ui.levels.rewards_golden_title",
                    Loc.Format("ui.levels.rewards_golden_body", BestGoldenPercent(table)));

            Section(3, "ic_chest", "ui.levels.rewards_finish_title",
                    Loc.Format("ui.levels.rewards_finish_body", DailyChests.RunsPerChest));

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 118f), new Vector2(.5f, 0f),
                             new Vector2(0f, 74f), () => Close());
        }

        /// <summary>
        /// The most a golden glade can pay, as a percentage of the ordinary reward.
        ///
        /// Asked of the published bands rather than of <see cref="GoldenRules.MaxPercent"/>,
        /// which is the ceiling a content file may not exceed and not a promise anybody is
        /// paid — quoting it would advertise ten times the coins on a table whose best band
        /// is five.
        /// </summary>
        static int BestGoldenPercent(ProgressionTable table)
        {
            int best = GoldenRules.MinPercent;
            var bands = table.Golden.Bands;
            for (int i = 0; i < bands.Count; i++)
                if (bands[i].Weight > 0 && bands[i].Percent > best) best = bands[i].Percent;
            return best;
        }

        /// <summary>
        /// One answer: a glyph, a heading and a paragraph.
        ///
        /// The glyph is the one the game already uses for the thing being explained, so
        /// reading the panel also teaches what the marks on the map and the victory screen
        /// mean — half of what somebody opened it to find out.
        /// </summary>
        void Section(int row, string icon, string titleKey, string body)
        {
            float y = FirstRow - row * RowPitch;

            var host = UIKit.Box("S" + titleKey, Panel, new Vector2(PanelW - 90f, 230f),
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

            // Shrinkable as well as wrapped, for the reason the streak's panel is: these are
            // the longest strings in the game and a translation half again the length of the
            // English would otherwise run out of its paragraph and into the row below it.
            UIKit.Shrinkable(
                UIKit.Titled("B", host, body, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(BodyW, 118f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -140f), 0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
