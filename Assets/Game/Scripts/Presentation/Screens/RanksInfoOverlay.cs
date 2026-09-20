using System.Collections.Generic;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Social;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The three things the boards screen cannot answer by drawing itself.
    ///
    /// <para>
    /// A list of a hundred names shows perfectly well <em>who</em> is ahead and says nothing
    /// about the rules behind it. Three things were being guessed at, and the middle one is the
    /// question this panel was asked for: <b>a board is a tally taken once a day, not a live
    /// reading</b>. Without that stated, a keeper who buys half the grove catalogue and comes
    /// straight here sees a list that has not moved, and the only two readings available are
    /// "the boards are broken" and "what I built did not count" — both wrong, and both the kind
    /// of conclusion somebody reaches once and never revisits.
    /// </para>
    /// <para>
    /// The other two are the ones a player cannot arrive at by looking. <b>What the one board
    /// is ordered on</b> is said by the rows and by nothing else — they print the figure their
    /// own board is ordered on and no other (<c>LeaderboardBoard.IsEndless</c>) — and this used
    /// to be the section naming <em>both</em> ladders, which is why it survived the finest
    /// groves being held rather than going with the tabs: a list that descends by a number
    /// nobody has been told about reads as shuffled whether there is one board or two. And that
    /// the list stops at
    /// <see cref="LeaderboardBoard.MaxRows"/> is invisible from inside it: a keeper who is not
    /// on the board has no way of telling "I am 412th" from "I am not ranked", which is exactly
    /// what the published distribution answers on their own profile (invariant 19c).
    /// </para>
    /// <para>
    /// <b>Every number is read rather than written into the copy</b>, for
    /// <see cref="StreakInfoOverlay"/>'s reason — a panel that explains the game is the first
    /// thing to go stale when the game is retuned. The row count comes off
    /// <c>LeaderboardBoard</c> and the cadence off <c>LeaderboardBoard.RebuildMinutes</c>; when
    /// the screen has a board in hand, <em>when this one was actually built</em> comes off the
    /// document itself, which is the one figure here that cannot drift at all.
    /// </para>
    /// <para>
    /// The panel's height is <see cref="PanelStack"/>'s arithmetic rather than a typed number,
    /// which is what <c>GladeRewardsOverlay</c> pays for: a hand-written height is how the
    /// panel it was lifted from came to be drawing its last paragraph through its own button.
    /// </para>
    /// </summary>
    public sealed class RanksInfoOverlay : ModalView
    {
        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        /// <summary>
        /// The board the screen is showing, so the freshness line can name a real tally.
        ///
        /// <para>
        /// Handed over rather than fetched, for <c>KeeperOverlay.Entry</c>'s reason: the screen
        /// behind has already paid for this document and a panel that asked again would spend a
        /// read to print a sentence. <see cref="LeaderboardBoard.None"/> is the ordinary state
        /// on a device that has never reached the boards, and the panel simply drops the half of
        /// the sentence it cannot honestly say.
        /// </para>
        /// </summary>
        public LeaderboardBoard Board = LeaderboardBoard.None;

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

        protected override void Build()
        {
            var answers = Answers();

            MakePanel(new Vector2(PanelStack.Width, PanelStack.HeightFor(answers.Count)),
                      Loc.Get("ui.board.info_title").ToUpperInvariant());

            for (int i = 0; i < answers.Count; i++) Section(i, answers[i]);

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, PanelStack.ButtonHeight), new Vector2(.5f, 0f),
                             new Vector2(0f, PanelStack.ButtonCentre), () => Close());
        }

        /// <summary>
        /// Everything the panel has to say, in reading order.
        ///
        /// Gathered before anything is drawn because the count decides the panel's height, and
        /// asking twice — once to measure and once to fill — is how two layouts drift apart.
        /// </summary>
        List<Answer> Answers()
        {
            var answers = new List<Answer>(3);

            // The ladder, named by the same string the board itself is named by rather than
            // by a sentence of its own, so the panel and the screen cannot come to disagree.
            //
            // **It was two ladders and the finest groves are held** (see `LeaderboardScreen`).
            // The keys are kept and their text changed rather than minted afresh, which is what
            // invariant 5f allows for a string and refuses for an id: a loc key names a
            // sentence, so when the Grovement comes back the sentence comes back with it.
            answers.Add(new Answer("ic_trophy", "ui.board.info_boards_title",
                                   Loc.Format("ui.board.info_boards_body",
                                              Loc.Get("ui.board.endless"))));

            answers.Add(new Answer("ic_restart", "ui.board.info_tally_title", TallyBody()));

            // `ic_profile` rather than `ic_rank`, and the render is what said so: the sentence
            // sends the reader to their own profile for the percentile, and the rank badge is a
            // coloured emblem that fights the two flat white glyphs above it — a column of
            // three marks that is one mark's worth of decoration is not a column.
            answers.Add(new Answer("ic_profile", "ui.board.info_place_title",
                                   Loc.Format("ui.board.info_place_body",
                                              LeaderboardBoard.MaxRows)));

            return answers;
        }

        /// <summary>
        /// When the boards move, and — when this device has actually read one — when the board
        /// behind the panel last did.
        ///
        /// <para>
        /// Two readings rather than one, because the second half is a fact about a document
        /// this screen may not have. A board that has never been fetched, a device with no
        /// backend and a board the job has not written yet all arrive here carrying
        /// <c>BuiltUnix</c> of nought, and printing "last built 56 years ago" is worse than
        /// saying nothing: the cadence on its own is true everywhere and is the answer somebody
        /// opened this for.
        /// </para>
        /// <para>
        /// The age is clamped at nought rather than trusted. It is the difference between a
        /// stamp the server wrote and a clock the handset keeps, and a phone running a few
        /// minutes slow would otherwise print a tally taken in the future.
        /// </para>
        /// </summary>
        string TallyBody()
        {
            long built = Board?.BuiltUnix ?? 0L;
            if (built <= 0L)
                return Loc.Format("ui.board.info_tally_body", LeaderboardBoard.RebuildMinutes);

            long age = GameClock.NowUnix() - built;
            if (age < 0L) age = 0L;

            return Loc.Format("ui.board.info_tally_built", LeaderboardBoard.RebuildMinutes,
                              Profile.LongCountdown(age));
        }

        /// <summary>
        /// One answer, placed.
        ///
        /// The glyph is the one the game already uses for the thing being explained, so reading
        /// the panel also teaches what the marks elsewhere mean — half of what somebody opened
        /// it to find out. Every coordinate comes from <see cref="PanelStack"/>, which measures
        /// downward from the panel's top edge; <c>UIKit</c> takes the opposite sign, so it is
        /// negated here, once.
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

            // Shrinkable as well as wrapped, for the reason every panel of this shape is: these
            // are among the longest strings in the game, and a translation half again the length
            // of the English would otherwise run out of its paragraph and into the row below it.
            // It is also what makes PanelStack's arithmetic true rather than hopeful — the box
            // is a fixed depth because the text shrinks into it instead of growing.
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
