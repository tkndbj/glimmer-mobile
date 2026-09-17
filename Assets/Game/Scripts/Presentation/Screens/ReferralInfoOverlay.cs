using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Referral;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How inviting works, in three sections. Every number is read from the table rather
    /// than the copy (<c>CRAFT.md</c>: panels that explain the game read from the rules),
    /// so a retuned ladder redraws this panel rather than making it lie.
    /// </summary>
    public sealed class ReferralInfoOverlay : ModalView
    {
        const float PanelW = 900f;
        const float BodyW = 590f;

        static readonly Color Body = new Color(.40f, .30f, .22f);
        static readonly Color Head = new Color(.28f, .18f, .12f);

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, 1240f), Loc.Get("ui.referral.info_title"));

            var table = ReferralLedger.Table;
            var chapter = GameContent.Index?.FindChapter(table.Milestone);
            string chapterName = chapter != null ? Loc.Get(chapter.NameKey) : table.Milestone.Value;

            Section(-208f, "ic_share", "ui.referral.info_share_title",
                    Loc.Get("ui.referral.info_share_body"));

            Section(-544f, "ic_trophy", "ui.referral.info_finish_title",
                    Loc.Format("ui.referral.info_finish_body", chapterName));

            string pays = Loc.Format("ui.referral.pays", table.PerInvitee.Count,
                                     Loc.Get(table.PerInvitee.Tier.NameKey));
            Section(-880f, "Chest/" + table.PerInvitee.Tier.Id, "ui.referral.info_pay_title",
                    Loc.Format("ui.referral.info_pay_body", chapterName, pays, table.MaxBound));

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

            UIKit.Shrinkable(
                UIKit.Titled("B", host, body, 27, Body, TextAnchor.UpperLeft,
                             new Vector2(BodyW, 190f), new Vector2(0f, 1f),
                             new Vector2(148f + BodyW * .5f, -152f), 0f, 0f, wrap: true), 18);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
