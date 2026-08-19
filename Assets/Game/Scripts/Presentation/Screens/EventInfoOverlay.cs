using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The three things the vine cannot draw for itself.
    ///
    /// <para>
    /// Same brief as <see cref="StreakInfoOverlay"/>, and the same two rules. It answers only
    /// what the page in front of it leaves genuinely unanswerable, because a panel that
    /// restates the board is a panel players learn to skip. And <b>every number in it is read
    /// from the event rather than written into the copy</b> — an author retunes a track by
    /// editing <c>manifest.json</c>, with no build and no review, so a figure typed into a
    /// sentence here would be wrong within one content push and nothing would catch it.
    /// </para>
    /// <para>
    /// The three are chosen by what the vine gets asked about. That a glade only counts if it
    /// is <em>first</em> cleared inside the window is the one rule players cannot deduce and
    /// will feel cheated by — a rail node stays blank for a glade they have plainly finished.
    /// That the window stops progress but never takes a flower away is the reassurance a
    /// countdown creates and cannot answer. And that a reward has to be tapped is the whole
    /// change this page exists for.
    /// </para>
    /// </summary>
    public sealed class EventInfoOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        GroveEvent _event;

        /// <summary>Set through <c>Flow.Modal</c>'s configure callback, before Build runs.</summary>
        public void For(GroveEvent groveEvent) => _event = groveEvent;

        protected override void Build()
        {
            _event ??= GroveEvents.Featured;

            MakePanel(new Vector2(PanelW, 1240f), Loc.Get("ui.event.info_title"));

            int goal = _event == null ? 0 : _event.FinalGoal;
            int rungs = _event == null ? 0 : _event.Milestones.Count;
            long total = _event == null ? 0 : _event.TotalCredits;

            Section(-208f, "ic_star", "ui.event.info_count_title",
                    Loc.Format("ui.event.info_count_body", goal, rungs, Compact.Number(total)));

            Section(-544f, "ic_gift", "ui.event.info_collect_title",
                    Loc.Get("ui.event.info_collect_body"));

            Section(-880f, "ic_hint", "ui.event.info_window_title",
                    Loc.Get("ui.event.info_window_body"));

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

        void Section(float y, string icon, string titleKey, string body)
        {
            var host = UIKit.Box("S", Panel, new Vector2(810f, 300f), new Vector2(.5f, 1f),
                                 new Vector2(0f, y));

            UIKit.Img("Seat", host, Art.Disc(96), new Color(1f, .95f, .84f, .9f),
                      new Vector2(112f, 112f), new Vector2(0f, 1f), new Vector2(66f, -58f));

            var glyph = UIKit.Img("Icon", host, Art.S("Ui/" + icon), Pal.Bloom,
                                  new Vector2(76f, 76f), new Vector2(0f, 1f), new Vector2(66f, -58f));
            glyph.preserveAspect = true;

            UIKit.Shrinkable(
                UIKit.Titled("Head", host, Loc.Get(titleKey).ToUpperInvariant(), 34, Head,
                             TextAnchor.MiddleLeft, new Vector2(BodyW, 44f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -34f), 0f, 0f), 22);

            UIKit.Shrinkable(
                UIKit.Titled("Body", host, body, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(BodyW, 190f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -152f), 0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
