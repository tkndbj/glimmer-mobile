using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What a glade costs, what it pays, under what rule, and what it takes to go on.
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
    /// golden bands, the chest cadence, the free opening — rather than written into the
    /// copy, for <see cref="StreakInfoOverlay"/>'s reason: a panel explaining the game is the
    /// first thing to go stale when the game is retuned, and the only defence is for it to
    /// have no numbers of its own to get wrong. The chapter matters here — <c>chapterRewards</c>
    /// already overrides the opening one — so the panel is told which map raised it and
    /// quotes that chapter's figures rather than the default curve's.
    /// </para>
    /// <para>
    /// The fourth answer is the chapter gate, and it is the only one on the panel a player
    /// cannot arrive at by playing. What a star pays, what a replay pays and what a golden
    /// glade pays are all reported by the victory screen given enough runs; that clearing every
    /// glade of a chapter is <em>not</em> what opens the next one can only be discovered by
    /// clearing a chapter and finding that nothing happened. It replaced a note about finished
    /// runs feeding the chests, which the chest panel already says beside the chests.
    /// </para>
    /// <para>
    /// <b>The panel's height is derived, and the sections it holds vary.</b> The panel is
    /// measured to what it is actually saying rather than to a typed number — the same
    /// judgement <c>SettingsOverlay</c> and <c>AccountOverlay</c> make. The arithmetic is
    /// <see cref="PanelStack"/>'s, in Domain, because a hand-written height is how the four
    /// sections that shipped came to be overlapping the button under them. Five is what the
    /// shortest canvas holds and five is what this panel now draws everywhere: the cost section
    /// stopped being conditional when the heart gate grew its second clause, and the two
    /// sentences it can carry are the same shape, so the count no longer varies. That is a fact
    /// about today's content rather than a licence to add a sixth — <c>PanelStackTests</c> is
    /// what says how many fit.
    /// </para>
    /// </summary>
    public sealed class GladeRewardsOverlay : ModalView
    {
        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        ChapterId _chapter;

        /// <summary>One answer, before it is placed: a glyph, a heading and a paragraph.</summary>
        readonly struct Answer
        {
            public readonly string Icon, TitleKey, Text;

            public Answer(string icon, string titleKey, string text)
            {
                Icon = icon;
                TitleKey = titleKey;
                Text = text;
            }
        }

        /// <summary>Which chapter's reward rule to quote. Defaults to the table's own.</summary>
        public void For(ChapterId chapter) => _chapter = chapter;

        protected override void Build()
        {
            var answers = Answers();

            MakePanel(new Vector2(PanelStack.Width, PanelStack.HeightFor(answers.Count)),
                      Loc.Get("ui.levels.info_title"));

            for (int i = 0; i < answers.Count; i++) Section(i, answers[i]);

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, PanelStack.ButtonHeight), new Vector2(.5f, 0f),
                             new Vector2(0f, PanelStack.ButtonCentre), () => Close());
        }

        /// <summary>
        /// Everything this panel has to say about the chapter it was raised from, in reading
        /// order.
        ///
        /// <para>
        /// Gathered before anything is drawn because the count decides the panel's height, and
        /// asking twice — once to measure and once to fill — is how two layouts drift apart.
        /// </para>
        /// <para>
        /// What a glade costs leads, and it is always said. It is the only answer on the panel
        /// about the glade the player is looking at <em>right now</em> rather than about the
        /// rules behind it. Which sentence depends on where they are standing: in a mode's first
        /// chapter it is the opening window, and everywhere else it is the rule that a glade
        /// already finished is free to play again — which is the half of the heart gate a player
        /// otherwise has no way at all of discovering, since the only evidence of it is a panel
        /// that does <em>not</em> appear when they leave.
        /// </para>
        /// </summary>
        List<Answer> Answers()
        {
            var table = ProgressionRules.Table;
            var rule = table.RuleFor(_chapter);
            var answers = new List<Answer>(5);

            // What a glade costs, which is the one answer that is different for the player in
            // front of it. In a mode's first chapter it is the opening window, which subsumes
            // the replay rule and says so; everywhere else it is the replay rule alone, which
            // is true of every chapter that will ever ship and so is stated as a rule rather
            // than as a count. See HeartStake.
            int free = HeartStake.FreeLevelsIn(GameContent.Index, _chapter);
            answers.Add(free > 0
                            ? new Answer("ic_heart", "ui.levels.rewards_free_title",
                                         Loc.Format("ui.levels.rewards_free_body", free))
                            : new Answer("ic_heart", "ui.levels.rewards_replay_title",
                                         Loc.Get("ui.levels.rewards_replay_body")));

            answers.Add(new Answer("ic_star", "ui.levels.rewards_stars_title",
                                   Loc.Format("ui.levels.rewards_stars_body",
                                              Compact.Number(rule.CreditsFor(1)),
                                              Compact.Number(rule.CreditsFor(2)),
                                              Compact.Number(rule.CreditsFor(3)))));

            answers.Add(new Answer("ic_restart", "ui.levels.rewards_again_title",
                                   Loc.Get("ui.levels.rewards_again_body")));

            answers.Add(new Answer("crest_gold", "ui.levels.rewards_golden_title",
                                   Loc.Format("ui.levels.rewards_golden_body", BestGoldenPercent(table))));

            // The gate, and it is the one answer on this panel a player cannot work out by
            // playing. Everything above is discoverable from the victory screen given enough
            // runs; "clearing this chapter is not what opens the next one" is discoverable
            // only by clearing a chapter and finding nothing happened. The number comes from
            // the published rule rather than the copy, for this panel's standing reason, and
            // it is quoted per level because that is how the rule is written - see
            // ChapterGateLimits.DefaultStarsPerLevel.
            answers.Add(new Answer("ic_key", "ui.levels.rewards_open_title", OpenBody()));

            return answers;
        }

        /// <summary>
        /// What opens the next chapter, in the numbers of the chapter the player is standing in.
        ///
        /// <para>
        /// Three readings rather than one because a published file can move the rule underneath
        /// this panel, and every one of the other two produces a sentence that is wrong rather
        /// than merely vague. A gate retuned to zero would otherwise print "0 stars", which
        /// reads as a bug; a chapter the index cannot name would print "0 of 0", which reads as
        /// a broken chapter. The rule itself is per level, so it can always be stated even when
        /// the totals cannot.
        /// </para>
        /// </summary>
        string OpenBody()
        {
            var gate = ChapterGateRules.Table;
            if (gate.IsOpenToAll) return Loc.Get("ui.levels.rewards_open_free");

            int levels = LevelsInChapter();
            if (levels <= 0) return Loc.Format("ui.levels.rewards_open_rule", gate.StarsPerLevel);

            return Loc.Format("ui.levels.rewards_open_body", gate.StarsPerLevel,
                              gate.RequiredStars(levels), LevelRecord.MaxStars * levels);
        }

        /// <summary>
        /// How many glades the chapter this panel was raised from holds, so the gate can be
        /// quoted as the totals a player will actually count towards.
        ///
        /// <para>
        /// Read from the index rather than assumed to be ten: chapters ship every two to four
        /// weeks and nothing promises the next one is the size of the last. Falls back to the
        /// chapter the map is showing, and then to the rule alone — a panel that cannot name a
        /// chapter still states the rule correctly, because the rule is per level.
        /// </para>
        /// </summary>
        int LevelsInChapter()
        {
            var index = GameContent.Index;
            var entry = _chapter.IsValid ? index?.FindChapter(_chapter) : null;
            return entry?.LevelCount ?? 0;
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
        /// One answer, placed.
        ///
        /// The glyph is the one the game already uses for the thing being explained, so
        /// reading the panel also teaches what the marks on the map and the victory screen
        /// mean — half of what somebody opened it to find out.
        ///
        /// Every coordinate comes from <see cref="PanelStack"/>, which measures downward from
        /// the panel's top edge; <c>UIKit</c> takes the opposite sign, so it is negated here,
        /// once.
        /// </summary>
        void Section(int row, Answer answer)
        {
            float top = PanelStack.TopOf(row);

            var host = UIKit.Box("S" + answer.TitleKey, Panel,
                                 new Vector2(PanelStack.Width - PanelStack.HostInset,
                                             PanelStack.SectionHeight),
                                 new Vector2(.5f, 1f),
                                 new Vector2(0f, -(top + PanelStack.SectionHeight * .5f)));

            var seat = UIKit.Img("Seat", host, Art.Disc(96), new Color(.94f, .84f, .64f, .85f),
                                 Vector2.one * PanelStack.SeatSize, new Vector2(0f, 1f),
                                 new Vector2(PanelStack.SeatSize * .6f, -PanelStack.SeatCentre));

            var glyph = UIKit.Img("Icon", seat.transform, Art.S("Ui/" + answer.Icon), Color.white,
                                  Vector2.one * 70f, new Vector2(.5f, .5f), Vector2.zero);
            glyph.preserveAspect = true;

            float textX = PanelStack.TextLeft + PanelStack.TextWidth * .5f;

            UIKit.Shrinkable(
                UIKit.Titled("H", host, Loc.Get(answer.TitleKey).ToUpperInvariant(), 34, Head,
                             TextAnchor.MiddleLeft,
                             new Vector2(PanelStack.TextWidth, PanelStack.HeadHeight),
                             new Vector2(0f, 1f), new Vector2(textX, -PanelStack.HeadCentre), 0f, 0f), 22);

            // Shrinkable as well as wrapped, for the reason the streak's panel is: these are
            // the longest strings in the game and a translation half again the length of the
            // English would otherwise run out of its paragraph and into the row below it. It
            // is also what makes PanelStack's arithmetic true rather than hopeful — the box is
            // a fixed depth because the text shrinks into it instead of growing.
            UIKit.Shrinkable(
                UIKit.Titled("B", host, answer.Text, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(PanelStack.TextWidth, PanelStack.BodyHeight),
                             new Vector2(0f, 1f),
                             new Vector2(textX, -(PanelStack.BodyTop + PanelStack.BodyHeight * .5f)),
                             0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
