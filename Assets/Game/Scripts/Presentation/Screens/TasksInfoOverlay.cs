using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Tasks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How the tasks work, in three sentences a child could follow.
    ///
    /// The numbers are read from the rules rather than the copy (<c>CRAFT.md</c>): how many
    /// tasks a day, how many a week and how many chest tiers there are all come off the
    /// table, so a slate retuned by content changes the sentence without touching a
    /// translation.
    /// </summary>
    public sealed class TasksInfoOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, 1240f), Loc.Get("ui.tasks.info_title"));

            var table = ProgressionRules.Table.Tasks;
            int perDay = TaskLedger.Active(TaskPeriod.Daily).Count;
            int perWeek = TaskLedger.Active(TaskPeriod.Weekly).Count;

            Section(-208f, "ic_list", "ui.tasks.info_tasks_title",
                    Loc.Format("ui.tasks.info_tasks_body", perDay, perWeek));

            Section(-544f, "ic_restart", "ui.tasks.info_reset_title",
                    Loc.Get("ui.tasks.info_reset_body"));

            Section(-880f, "Chest/" + table.Tiers[table.Tiers.Count - 1].Id, "ui.tasks.info_chest_title",
                    Loc.Format("ui.tasks.info_chest_body", table.Tiers.Count));

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
