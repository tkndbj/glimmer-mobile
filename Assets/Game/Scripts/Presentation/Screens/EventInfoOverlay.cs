using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The three things the ladder cannot draw for itself.
    ///
    /// <para>
    /// Same brief as <see cref="TasksInfoOverlay"/>, and the same two rules. It answers only
    /// what the page in front of it leaves genuinely unanswerable, because a panel that
    /// restates the screen is a panel players learn to skip. And <b>every number in it is
    /// read from the season rather than written into the copy</b> — an author retunes a
    /// ladder by editing <c>manifest.json</c>, with no build and no review, so a figure typed
    /// into a sentence here would be wrong within one content push and nothing would catch it.
    /// </para>
    /// <para>
    /// The three are chosen by what a season gets asked about. <b>Where marks come from</b>
    /// is the one rule nothing on this page can show — the chests that grow it are opened on
    /// another screen — and a player who does not know it is a player watching a bar that
    /// never moves. <b>That the window stops the growing but never takes a chest away</b> is
    /// the reassurance a countdown creates and cannot answer. And <b>what the pass is</b>, on
    /// the page selling it.
    /// </para>
    /// </summary>
    public sealed class EventInfoOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        /// <summary>Set through <c>Flow.Modal</c>'s configure callback, before Build runs.</summary>
        [System.NonSerialized] public GroveEvent Season;

        protected override void Build()
        {
            Season ??= GroveEvents.Featured;

            MakePanel(new Vector2(PanelW, 1240f), Loc.Get("ui.mark.info_title"));

            int rungs = Season == null ? 0 : Season.Milestones.Count;
            int top = Season == null ? 0 : Season.FinalGoal;
            int tiers = ProgressionRules.Table.Tasks.Tiers.Count;

            Section(-208f, "ic_list", "ui.mark.info_grow_title",
                    Loc.Format("ui.mark.info_grow_body", rungs, top));

            Section(-544f, "ic_gift", "ui.mark.info_tracks_title",
                    Loc.Format("ui.mark.info_tracks_body", tiers));

            Section(-880f, "ic_hint", "ui.mark.info_window_title",
                    Loc.Get("ui.mark.info_window_body"));

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

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

            // Shrinkable as well as wrapped: these are among the longest strings in the game,
            // and a translation half again the length of the English would otherwise run out
            // of the paragraph and into the row below it.
            UIKit.Shrinkable(
                UIKit.Titled("B", host, body, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(BodyW, 190f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -152f), 0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
