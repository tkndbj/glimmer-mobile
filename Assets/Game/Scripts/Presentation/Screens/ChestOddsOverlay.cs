using System.Text;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Tasks;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What one chest tier holds, said before anybody has earned it.
    ///
    /// <para>
    /// A disclosure panel, and the reason the chest ladder on the tasks page is tappable
    /// at all: a randomised reward should be able to explain itself wherever it is shown
    /// (invariant 10b), and a player deciding whether a weekly task is worth the week
    /// deserves to know what a royal chest is before they start. The numbers are read off
    /// the same <see cref="ChestDefinition"/> the roll uses, so the panel cannot drift from
    /// the odds — a panel written from copy is the first thing to rot on a retune.
    /// </para>
    /// </summary>
    public sealed class ChestOddsOverlay : ModalView
    {
        /// <summary>The tier to explain. Set by the caller before Build.</summary>
        [System.NonSerialized] public ChestTier Tier;

        const float PanelW = 900f;
        const float BodyW = 760f;

        // A prize's row is narrower than the headings above it, because the icon column and
        // the odds column are pinned to its edges: at the body's own width the picture sits a
        // third of the panel away from the words it belongs to, and the line stops reading as
        // one thing. The odds column is reserved on **every** line, including the guaranteed
        // ones that never print in it, so both lists centre their words on the same axis.
        const float LineW = 620f;

        // The panel's own stack, from its top edge down. Written out because the height is the
        // sum of them and a panel that guesses its height is a panel with a hole in it or a
        // button on its own rim (`render_arrival.py`'s recorded trap).
        // The ribbon stands 22 proud of the panel's top edge and is 130 tall, so it covers
        // the first 108 units of the panel's own face. Anything drawn above that is drawn
        // behind the title — which is what the chest was, in the first cut of this rebuild.
        const float HeadRoom = 124f;
        const float ChestH = 250f;
        const float RankH = 54f;
        const float SectionH = 64f;
        const float LineH = 86f;
        const float SectionGap = 18f;
        const float FootRoom = 200f;

        static readonly Color Body = new Color(.23f, .15f, .10f);
        static readonly Color Head = new Color(.28f, .18f, .12f);
        static readonly Color Odds = new Color(.78f, .42f, .10f);

        /// <summary>
        /// Where the cursor is as the panel is written, so the two passes — measure, then
        /// build — cannot disagree about the layout.
        /// </summary>
        float _y;

        protected override void Build()
        {
            if (Tier == null)
            {
                Tween.After(0f, () => Flow.Dismiss(this), this);
                return;
            }

            MakePanel(new Vector2(PanelW, Height(Tier)), Loc.Get(Tier.NameKey));

            _y = -HeadRoom;

            var chest = UIKit.Img("Chest", Panel, Art.S(Tier.Icon), Color.white,
                                  new Vector2(ChestH / ChestPack.Fill * ChestPack.Aspect, ChestH / ChestPack.Fill),
                                  new Vector2(.5f, 1f),
                                  new Vector2(0f, _y - ChestH * .5f + ChestH * ChestPack.Lift));
            chest.preserveAspect = true;

            var halo = UIKit.Halo(chest.transform, Pal.Gold, ChestH * 1.9f, .38f);
            ((RectTransform)halo.transform).anchoredPosition = new Vector2(0f, -ChestH * ChestPack.Lift);
            Tween.Breathe(chest.transform, .04f, 2.4f);
            _y -= ChestH;

            UIKit.Shrinkable(
                UIKit.Titled("Rank", Panel, Loc.Format("ui.chest.rank", Tier.Rank,
                                                       Progression.ProgressionRules.Table.Tasks.Tiers.Count)
                                                .ToUpperInvariant(),
                             28, Head, TextAnchor.MiddleCenter, new Vector2(BodyW, RankH),
                             new Vector2(.5f, 1f), new Vector2(0f, _y - RankH * .5f), 0f, 0f), 17);
            _y -= RankH;

            Section("ui.chest.always", Tier.Chest.Guaranteed, null);

            if (Tier.Chest.Options.Count > 0)
                Section("ui.chest.one_of", null, Tier.Chest);

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

        /// <summary>
        /// How tall the panel has to be for this tier. Both lists vary by tier — a wood chest
        /// guarantees one band and a royal one three — so the height is counted rather than
        /// reserved for the tallest, which is what stops the short panels carrying a hole.
        ///
        /// <para>
        /// Public so <c>ChestPackTests</c> can hold every shipped tier to
        /// <see cref="Layout.PanelStack.TallestPanel"/>: a panel is centred, so one that grows
        /// past that draws its own title off the top of the shortest canvas — and the content
        /// that decides this height is retunable without a build.
        /// </para>
        /// </summary>
        public static float Height(ChestTier tier)
        {
            float y = HeadRoom + ChestH + RankH;

            y += SectionH + LineH * tier.Chest.Guaranteed.Count + SectionGap;
            if (tier.Chest.Options.Count > 0)
                y += SectionH + LineH * tier.Chest.Options.Count + SectionGap;

            return y + FootRoom;
        }

        /// <summary>
        /// A heading and its prizes. One of <paramref name="bands"/> or <paramref name="chest"/>
        /// is given: the second draws the odds column beside each line.
        ///
        /// <para>
        /// <b>A prize is a picture and a sentence, not a bullet point.</b> This panel used to
        /// print a gold dot in front of a left-aligned line, which is a paragraph wearing a
        /// list's clothes — a player reading "1 Mending" has no idea what a mending is, and the
        /// picture that would tell them is already in the build and already what the action bar
        /// and the chest ceremony draw (<see cref="RewardArt.Token"/>). So each line is the
        /// prize's own art in the kit's inset seat, the amount and the noun beside it, and the
        /// odds at the far end; and the block is centred on the panel rather than ranged left,
        /// because a panel whose title, chest and button are all centred and whose body is not
        /// reads as two layouts.
        /// </para>
        /// </summary>
        void Section(string titleKey, System.Collections.Generic.IReadOnlyList<ChestBand> bands,
                     ChestDefinition chest)
        {
            UIKit.Shrinkable(
                UIKit.Titled("H" + titleKey, Panel, Loc.Get(titleKey).ToUpperInvariant(), 34, Head,
                             TextAnchor.MiddleCenter, new Vector2(BodyW, SectionH),
                             new Vector2(.5f, 1f), new Vector2(0f, _y - SectionH * .5f), 0f, 0f), 21);
            _y -= SectionH;

            int count = chest != null ? chest.Options.Count : bands.Count;
            for (int i = 0; i < count; i++)
            {
                var band = chest != null ? chest.Options[i].Band : bands[i];
                Line(band, chest == null ? -1 : Mathf.RoundToInt(chest.ChanceOf(i)));
            }

            _y -= SectionGap;
        }

        /// <summary>One prize: its picture in a seat, what it is, and how likely it is.</summary>
        void Line(ChestBand band, int chance)
        {
            float mid = _y - LineH * .5f;
            bool odds = chance >= 0;

            // The seat, so a picture cut with air around it (every one of these is) reads as
            // something standing in a slot rather than floating at whatever size its own
            // margins leave it.
            UIKit.Img("Seat", Panel, Art.S("Ui/" + Skins.Slot), Color.white,
                      new Vector2(74f, 74f), new Vector2(.5f, 1f),
                      new Vector2(-LineW * .5f + 44f, mid));

            RewardArt.Token(band.Kind, band.Item, out var sprite, out var tint);
            var art = UIKit.Img("Art", Panel, sprite, tint, new Vector2(54f, 54f),
                                new Vector2(.5f, 1f), new Vector2(-LineW * .5f + 44f, mid));
            art.preserveAspect = true;

            // The words fill the room the seat and the odds column leave, and are centred in
            // it — so a one-word prize and a three-word one sit under each other.
            float left = -LineW * .5f + 92f;
            float right = LineW * .5f - 118f;

            UIKit.Shrinkable(
                UIKit.Titled("L", Panel, Band(band), 32, Body, TextAnchor.MiddleCenter,
                             new Vector2(right - left, 44f), new Vector2(.5f, 1f),
                             new Vector2((left + right) * .5f, mid), 0f, 0f), 19);

            if (odds)
                UIKit.Shrinkable(
                    UIKit.Titled("P", Panel, chance + "%", 32, Odds, TextAnchor.MiddleRight,
                                 new Vector2(110f, 44f), new Vector2(.5f, 1f),
                                 new Vector2(LineW * .5f - 55f, mid), 0f, 0f), 19);

            _y -= LineH;
        }

        /// <summary>"70–110 Coins", "1 Mending", "24h Heart Boost" — the band in the player's words.</summary>
        public static string Band(ChestBand band)
        {
            string name = RewardArt.Name(band.Kind, band.Item);

            if (band.Kind == ChestDropKind.HeartBoost) return band.Max + "h " + name;
            if (band.IsFixed) return band.Min + " " + name;
            return band.Min + "–" + band.Max + " " + name;
        }

        /// <summary>
        /// The published odds for a chest's variable slot as one line, read from the same
        /// table the roll used. Generated rather than written, so it cannot drift from the
        /// weights. Shared with the opening ceremony's footer.
        /// </summary>
        public static string OddsLine(ChestDefinition chest)
        {
            if (chest == null || chest.Options.Count == 0) return string.Empty;

            var text = new StringBuilder(Loc.Get("ui.chest.odds"));

            for (int i = 0; i < chest.Options.Count; i++)
            {
                text.Append("  ·  ")
                    .Append(RewardArt.Name(chest.Options[i].Band.Kind, chest.Options[i].Band.Item))
                    .Append(' ')
                    .Append(Mathf.RoundToInt(chest.ChanceOf(i)))
                    .Append('%');
            }

            return text.ToString();
        }

        public override bool OnBack() { Close(); return true; }
    }
}
