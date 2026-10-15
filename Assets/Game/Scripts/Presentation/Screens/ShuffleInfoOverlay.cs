using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// How the Shuffle lane works, in five short sentences, opened by the run header's "i"
    /// (<see cref="ShuffleScreen.Explain"/>) in place of the siege tips the other lanes replay.
    ///
    /// <para>
    /// The sentences carry no numbers that content can retune except the hand's rhythm, which
    /// is code (MODES.md 59f): a retuned deck or ramp changes nothing said here.
    /// </para>
    /// </summary>
    public sealed class ShuffleInfoOverlay : ModalView
    {
        const float PanelW = 900f, PanelH = 1300f;
        const float RowTop = -196f, RowStep = 168f, RowH = 150f;
        const float Seat = 92f, BodyX = 150f, BodyW = 690f;

        static readonly Color Body = new Color(.28f, .18f, .12f);
        static readonly Color SeatTint = new Color(.94f, .84f, .64f, .9f);

        /// <summary>In reading order. Every line is one plain cause and effect.</summary>
        static readonly string[] Lines =
        {
            "ui.shuffle.info_match",
            "ui.shuffle.info_raiders",
            "ui.shuffle.info_cards",
            "ui.shuffle.info_secrets",
            "ui.shuffle.info_far",
        };

        protected override void Build()
        {
            MakePanel(new Vector2(PanelW, PanelH), Loc.Get("ui.shuffle.info_title"));

            for (int i = 0; i < Lines.Length; i++)
                Row(i, Loc.Get(Lines[i]));

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

        void Row(int index, string text)
        {
            var host = UIKit.Box("Row" + index, Panel, new Vector2(PanelW - 80f, RowH),
                                 new Vector2(.5f, 1f), new Vector2(0f, RowTop - index * RowStep));

            var seat = UIKit.Img("Seat", host, Art.Disc(96), SeatTint,
                                 new Vector2(Seat, Seat), new Vector2(0f, .5f), new Vector2(Seat * .5f + 10f, 0f));

            UIKit.Titled("N", seat.transform, (index + 1).ToString(), 46, Pal.Cream,
                         TextAnchor.MiddleCenter, new Vector2(Seat, Seat), new Vector2(.5f, .5f),
                         Vector2.zero, 3f, 0f);

            // Shrinkable as well as wrapped: a translation half again the English's length must
            // stay inside its row rather than run into the next.
            UIKit.Shrinkable(
                UIKit.Titled("B", host, text, 34, Body, TextAnchor.MiddleLeft,
                             new Vector2(BodyW, RowH), new Vector2(0f, .5f),
                             new Vector2(BodyX + BodyW * .5f - 40f, 0f), 0f, 0f, wrap: true), 22);
        }

        public override bool OnBack() { Close(); return true; }
    }
}
