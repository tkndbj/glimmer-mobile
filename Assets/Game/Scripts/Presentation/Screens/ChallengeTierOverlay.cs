using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The challenge deals: one row per tier the file sells — its stone, its name, what it
    /// allows, how long it runs and what it costs — and a key to buy it.
    ///
    /// <para>
    /// <b>It wears the victory panel's frame</b>, at the owner's instruction on 2026-09-23 and
    /// on this sheet alone: the green window, the fan behind it and the crown over a banner
    /// carrying one word, all built by <see cref="VictoryFrame"/> so the two panels cannot
    /// drift. Every other panel in the game keeps <c>ModalView.MakePanel</c>'s plate. What that
    /// buys is a sheet that reads as an award being offered rather than as a settings page,
    /// which is what a thing sold for gems should look like.
    /// </para>
    /// <para>
    /// <b>A deal is bought here and nowhere else.</b> A gem debit is an ordinary spend
    /// (invariant 18), so there is no store sheet, no receipt and nothing to wait on: the
    /// purchase is <see cref="ChallengeLedger.TryBuyTier"/> and the sheet repaints. What it
    /// buys is a window of days during which every genre allows more plays, which is why the
    /// row that sells it turns into a row that reports it.
    /// </para>
    /// <para>
    /// <b>Generated from the file and keyed on nothing</b> (52k's shape): a tier added to
    /// <c>challenges.json</c> is a row here with no code, its name off its id (56i), its
    /// three figures off the row and its stone off its rung (<see cref="ChallengeArt.DealMark"/>).
    /// The rows are the authored order, cheapest first.
    /// </para>
    /// <para>
    /// <b>An upgrade is priced as the difference and the row says so</b> (56h): under a running
    /// deal a bigger row's key carries the difference alone — a figure and the gem, at the
    /// owner's instruction — and the note under the sentence says it is the difference and that
    /// the current deal's days carry over, because a price with no explanation reads as a
    /// discount somebody will look for again tomorrow. The price is <see cref="ChallengeLedger.PriceOf"/>'s and is derived,
    /// never typed here.
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
        /// <summary>
        /// The stack, measured from the window's top edge. The crest hangs to about 108 below
        /// it (its banner's tails), so the free line sits under that and the rows under the
        /// line; the foot is the DONE key's block at the bottom.
        /// </summary>
        const float FreeY = 150f, RowsTop = 200f, RowH = 230f, RowGap = 14f, Tail = 30f, FootH = 170f;

        /// <summary>
        /// The window is wider than the victory panel's (<see cref="VictoryFrame.PanelWidth"/>),
        /// at the owner's instruction on 2026-09-23 ("bigger, bigger texts"): a row carries a
        /// stone, a name over a sentence and a price side by side, and 900 made the sentence
        /// fold three times. A row keeps 60 units inside the window's sliced border.
        /// </summary>
        const float PanelW = 1000f, RowW = 880f, StoneSize = 160f, StoneX = 104f, TextX = 204f, TextW = 370f;
        static readonly Vector2 KeySize = new Vector2(280f, 116f);
        const float KeyInset = 16f;

        protected override void Build()
        {
            var tiers = ChallengeRules.Table.Tiers;
            float rows = tiers.Count * (RowH + RowGap) - (tiers.Count > 0 ? RowGap : 0f);
            float panelH = RowsTop + rows + Tail + FootH;

            Scrim = UIKit.Scrim(Content, .72f, () => Close());

            var frame = VictoryFrame.Build(Content, panelH, Loc.Get("ui.challenges.deals_title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // The one line every row shares: what the free allowance is, so a deal reads as
            // "more than this" rather than as a number on its own.
            UIKit.Shrinkable(
                UIKit.Titled("Free", Panel, Loc.Format("ui.challenges.free_deal", ChallengeRules.Table.FreePlays), 32,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter, new Vector2(860f, 60f),
                             new Vector2(.5f, 1f), new Vector2(0f, -FreeY), 2f, 2f, wrap: true),
                20);

            float y = -(RowsTop + RowH * .5f);
            for (int i = 0; i < tiers.Count; i++)
            {
                BuildRow(tiers[i], i + 1, y);
                y -= RowH + RowGap;
            }

            UIKit.TextButton("Close", Panel, Skins.Alternate, Loc.Get("ui.challenges.done").ToUpperInvariant(), 36,
                             new Vector2(400f, 110f), new Vector2(.5f, 0f), new Vector2(0f, 30f + 55f),
                             () => Close());

            // A rebuild is the same panel in a new state, not a new panel: replaying the crown
            // would pop and chime at a player who has just bought a deal on the panel already in
            // front of them (the distinction ModalView.MakePanel draws).
            if (Rebuilding)
            {
                frame.Settle();
                return;
            }

            // The entrance, in the victory panel's order: the crown first, the window under a
            // crown still settling so the two read as one movement, then the banner and its word.
            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => Tween.Scale(Panel, 1f, .5f, Ease.OutBack));
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
        }

        /// <summary>
        /// One deal. <paramref name="rung"/> is its place in the authored order from one, which
        /// is what picks its stone.
        /// </summary>
        void BuildRow(ChallengeTier tier, int rung, float y)
        {
            bool held = ChallengeLedger.Holds(tier);
            var governing = ChallengeLedger.HeldTier;
            bool under = governing != null && governing.Plays >= tier.Plays && !held;

            // A dark inset rather than a blue plate, because a plate the colour of every other
            // page fights the green window it sits in; this is the victory panel's own rail
            // treatment, widened to a row. A running deal wears a gold rim, so the state is said
            // by the row's edge as well as by the tag on its right.
            var plate = UIKit.Img("Row_" + tier.Id, Panel, Art.Round(28), new Color(0f, 0f, 0f, .32f),
                                  new Vector2(RowW, RowH), new Vector2(.5f, 1f), new Vector2(0f, y));
            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(28, 3f),
                                 held ? Pal.A(Pal.Gold, .62f) : new Color(1f, .96f, .86f, .14f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;
            var t = plate.transform;

            // The stone, off the rung. Null until synced or past the pictures that ship, and
            // drawn as nothing rather than as a white rectangle either way (7b).
            var stone = UIKit.Img("Stone", t, ChallengeArt.DealMark(rung), Color.white, Vector2.one * StoneSize,
                                  new Vector2(0f, .5f), new Vector2(StoneX, 0f));
            stone.preserveAspect = true;
            stone.enabled = stone.sprite != null;
            UIKit.Halo(stone.transform, held ? Pal.Gold : Pal.Bloom, StoneSize * 1.9f, held ? .34f : .18f);

            UIKit.Shrinkable(
                UIKit.Titled("Name", t, Loc.Get(tier.NameKey), 52, held ? Pal.Gold : Pal.Cream, TextAnchor.MiddleLeft,
                             new Vector2(TextW, 66f), new Vector2(0f, .5f), new Vector2(TextX + TextW * .5f, 60f), 3f, 3f),
                28);

            string days = tier.Days == 1 ? Loc.Get("ui.challenges.days_one") : Loc.Format("ui.challenges.days", tier.Days);
            UIKit.Shrinkable(
                UIKit.Titled("Line", t, Loc.Format("ui.challenges.deal_line", tier.Plays, days), 32,
                             new Color(1f, .96f, .88f, .88f), TextAnchor.UpperLeft, new Vector2(TextW, 84f),
                             new Vector2(0f, .5f), new Vector2(TextX + TextW * .5f, -20f), 2f, 0f, wrap: true),
                20);

            var keyPos = new Vector2(-(KeyInset + KeySize.x * .5f), 0f);

            if (held)
            {
                string line = DailyChallengesScreen.TimeLeft(tier);
                var tag = UIKit.Img("Held", t, Art.Round(24), Pal.A(Pal.Gold, .95f), KeySize, new Vector2(1f, .5f), keyPos);
                tag.raycastTarget = false;
                UIKit.Shrinkable(
                    UIKit.Titled("HeldText", tag.transform, Loc.Get("ui.challenges.deal_held") + "\n" + line, 30, Pal.Ink,
                                 TextAnchor.MiddleCenter, KeySize, default, default, 0f, 0f, wrap: true), 18);
                return;
            }

            // The price is derived: the full figure, or the difference under a running smaller
            // deal — in which case the note under the sentence says what the figure is and what
            // carries. Two keys for the two figures, so a translation may still word them apart.
            var upgrades = ChallengeLedger.Upgrades(tier);
            int price = under ? tier.Gems : ChallengeLedger.PriceOf(tier);
            string caption = upgrades != null ? Loc.Format("ui.challenges.upgrade", price)
                                              : Loc.Format("ui.challenges.buy", price);

            // The caption is the figure and the gem after it is the word (`Btn.IconTrails`),
            // which is how every other price in this game says which currency it is — the
            // season pass key's shape. TextButton makes the label shrinkable itself.
            UIKit.TextButton("Buy", t, under ? Skins.Shut : Skins.Gem, caption, 36, KeySize,
                             new Vector2(1f, .5f), keyPos, () => Buy(tier),
                             Art.S("Ui/ic_gem"), iconTrails: true);

            if (upgrades != null)
                UIKit.Shrinkable(
                    UIKit.Titled("Note", t, Loc.Get("ui.challenges.upgrade_note"), 22, Pal.A(Pal.Gold, .95f),
                                 TextAnchor.UpperLeft, new Vector2(TextW, 48f), new Vector2(0f, .5f),
                                 new Vector2(TextX + TextW * .5f, -88f), 2f, 0f, wrap: true), 14);
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
                    Scenery.Toast(Content, Loc.Format("ui.challenges.deal_too_poor", ChallengeLedger.PriceOf(tier)), Pal.Rose, 2.8f);
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
