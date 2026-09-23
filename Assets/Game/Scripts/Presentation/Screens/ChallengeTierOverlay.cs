using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The challenge deals: one row per tier the file sells — its name, what it allows, how
    /// long it runs and what it costs — and a key to buy it.
    ///
    /// <para>
    /// <b>A deal is bought here and nowhere else.</b> A gem debit is an ordinary spend
    /// (invariant 18), so there is no store sheet, no receipt and nothing to wait on: the
    /// purchase is <see cref="ChallengeLedger.TryBuyTier"/> and the sheet repaints. What it
    /// buys is a window of days during which every genre allows more plays, which is why the
    /// row that sells it turns into a row that reports it.
    /// </para>
    /// <para>
    /// <b>Generated from the file and keyed on nothing</b> (52k's shape): a tier added to
    /// <c>challenges.json</c> is a row here with no code, its name off its id (56i) and its
    /// three figures off the row. The rows are the authored order, cheapest first.
    /// </para>
    /// <para>
    /// <b>The refusals are said, not swallowed.</b> A deal already running says so; a smaller
    /// one under a larger says a better deal is running; and the one refusal worth sending
    /// somewhere — too few gems — names the figure, because a player short of gems is a player
    /// one screen away from having them.
    /// </para>
    /// </summary>
    public sealed class ChallengeTierOverlay : ModalView
    {
        const float PanelW = 940f, RowH = 150f, RowGap = 12f, HeadH = 190f, FootH = 120f;

        protected override void Build()
        {
            var tiers = ChallengeRules.Table.Tiers;
            float rows = tiers.Count * (RowH + RowGap);
            var size = new Vector2(PanelW, HeadH + rows + FootH);

            MakePanel(size, Loc.Get("ui.challenges.deals_title"));

            // The one line every row shares: what the free allowance is, so a deal reads as
            // "more than this" rather than as a number on its own.
            UIKit.Shrinkable(
                UIKit.Titled("Free", Panel, Loc.Format("ui.challenges.free_deal", ChallengeRules.Table.FreePlays), 24,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter, new Vector2(PanelW - 120f, 50f),
                             new Vector2(.5f, 1f), new Vector2(0f, -(HeadH - 40f)), 2f, 0f, wrap: true),
                16);

            float y = -(HeadH + RowH * .5f);
            for (int i = 0; i < tiers.Count; i++)
            {
                BuildRow(tiers[i], y);
                y -= RowH + RowGap;
            }

            UIKit.TextButton("Close", Panel, Skins.Alternate, Loc.Get("ui.challenges.done").ToUpperInvariant(), 30,
                             new Vector2(360f, 96f), new Vector2(.5f, 0f), new Vector2(0f, 24f + 48f),
                             () => Close());
        }

        void BuildRow(ChallengeTier tier, float y)
        {
            var plate = UIKit.Img("Row_" + tier.Id, Panel, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                  new Vector2(PanelW - 80f, RowH), new Vector2(.5f, 1f), new Vector2(0f, y));
            plate.type = Image.Type.Sliced;
            plate.raycastTarget = false;
            var t = plate.transform;

            bool held = ChallengeLedger.Holds(tier);
            var governing = ChallengeLedger.HeldTier;
            bool under = governing != null && governing.Plays >= tier.Plays && !held;

            UIKit.Titled("Name", t, Loc.Get(tier.NameKey), 34, held ? Pal.Gold : Pal.Cream, TextAnchor.MiddleLeft,
                         new Vector2(420f, 48f), new Vector2(0f, .5f), new Vector2(34f + 210f, 32f), 3f, 3f);

            string days = tier.Days == 1 ? Loc.Get("ui.challenges.days_one") : Loc.Format("ui.challenges.days", tier.Days);
            UIKit.Shrinkable(
                UIKit.Titled("Line", t, Loc.Format("ui.challenges.deal_line", tier.Plays, days), 22,
                             new Color(1f, .96f, .88f, .85f), TextAnchor.UpperLeft, new Vector2(480f, 66f),
                             new Vector2(0f, .5f), new Vector2(34f + 240f, -20f), 2f, 0f, wrap: true),
                15);

            var keySize = new Vector2(250f, 88f);
            var keyPos = new Vector2(-(28f + keySize.x * .5f), 0f);

            if (held)
            {
                int left = ChallengeLedger.DaysLeft(tier);
                string line = left == 1 ? Loc.Get("ui.challenges.days_left_one") : Loc.Format("ui.challenges.days_left", left);
                var tag = UIKit.Img("Held", t, Art.Round(24), Pal.A(Pal.Gold, .95f), keySize, new Vector2(1f, .5f), keyPos);
                tag.raycastTarget = false;
                UIKit.Shrinkable(
                    UIKit.Titled("HeldText", tag.transform, Loc.Get("ui.challenges.deal_held") + "\n" + line, 22, Pal.Ink,
                                 TextAnchor.MiddleCenter, keySize, default, default, 0f, 0f, wrap: true), 14);
                return;
            }

            var key = UIKit.TextButton("Buy", t, under ? Skins.Shut : Skins.Gem,
                                       Loc.Format("ui.challenges.buy", tier.Gems), 26, keySize, new Vector2(1f, .5f), keyPos,
                                       () => Buy(tier));
            var label = key.GetComponentInChildren<Text>();
            if (label) UIKit.Shrinkable(label, 16);
        }

        void Buy(ChallengeTier tier)
        {
            switch (ChallengeLedger.TryBuyTier(tier))
            {
                case TierBuy.Bought:
                    Audio.Sfx("collect", .8f);
                    Burst.Confetti(Content, 28);
                    Scenery.Toast(Content, Loc.Format("ui.challenges.deal_bought", Loc.Get(tier.NameKey)), Pal.Mint, 2.4f);
                    Rebuild();
                    return;

                case TierBuy.Held:
                    Scenery.Toast(Content, Loc.Get("ui.challenges.deal_running"), Pal.Mint);
                    return;

                case TierBuy.Lower:
                    Scenery.Toast(Content, Loc.Get("ui.challenges.deal_lower"), Pal.Gold, 2.6f);
                    return;

                case TierBuy.NotSold:
                    return;

                default:
                    Scenery.Toast(Content, Loc.Format("ui.challenges.deal_too_poor", tier.Gems), Pal.Rose, 2.8f);
                    return;
            }
        }

        public override bool OnBack()
        {
            Close();
            return true;
        }
    }
}
