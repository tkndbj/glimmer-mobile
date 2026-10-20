using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What a Shuffle refresh can deal, shown before it is bought (MODES.md 59p): one row per
    /// rarity with each card's chance of it and how many cards of it are left, the promise on the
    /// first card when this hand carries one, and one line saying cards of a rarity are equally
    /// likely. Opened by the "i" beside the refresh key; closes back to the hand.
    ///
    /// <para>
    /// <b>Every number is <see cref="ShuffleOdds"/>'</b>, read off the figures the deck deals with,
    /// so a retune of <c>config/shuffle</c> is the retune printed (<c>ChestOddsOverlay</c>'s rule).
    /// The look is the chest odds panel's: the kit's panel and ribbon, brown ink, the odds in
    /// amber, and each rarity on the trough the hand's cards say it on. Drawn by
    /// <c>Tools/render_shuffle.py --odds</c>, whose <c>--measure</c> holds every caption in nine
    /// languages.
    /// </para>
    /// </summary>
    public sealed class ShuffleOddsOverlay : ModalView
    {
        /// <summary>The odds to show. Set by the caller before Build.</summary>
        [System.NonSerialized] public ShuffleOdds Odds;

        // The panel's own stack, from its top edge down (the ribbon covers the first 108).
        public const float PanelW = 900f, BodyW = 760f;
        public const float HeadRoom = 132f, HeaderH = 52f, RowH = 80f, RowGap = 10f;
        public const float FloorH = 60f, NoteH = 84f, FootRoom = 196f;

        /// <summary>The line saying how a started school leans its cards (MODES.md 59r), under the note, when it does.</summary>
        public const float LeanH = 96f;

        // The three columns across the body, as centres and widths.
        public const float RarityX = -240f, RarityW = 280f;
        public const float ChanceX = 30f, ChanceW = 240f;
        public const float LeftX = 265f, LeftW = 220f;

        static readonly Color Ink = new Color(.23f, .15f, .10f);
        static readonly Color Head = new Color(.28f, .18f, .12f, .78f);
        static readonly Color Amber = new Color(.78f, .42f, .10f);

        /// <summary>How tall the panel is: four rows always, the promise line only when there is one.</summary>
        public static float Height(bool floor) => Height(floor, false);

        /// <summary>How tall the panel is: four rows, the promise line when there is one, and the lean's line when a school leans.</summary>
        public static float Height(bool floor, bool leaning)
            => HeadRoom + HeaderH + ShuffleOdds.Tiers * (RowH + RowGap) + (floor ? FloorH : 0f) + NoteH
               + (leaning ? LeanH : 0f) + FootRoom;

        float _y;

        protected override void Build()
        {
            if (Odds == null)
            {
                Tween.After(0f, () => Flow.Dismiss(this), this);
                return;
            }

            MakePanel(new Vector2(PanelW, Height(Odds.Floor.HasValue, Odds.Leaning)), Loc.Get("ui.shuffle.odds_title"));
            _y = -HeadRoom;

            Header("Rarity", "ui.shuffle.odds_rarity", RarityX, RarityW);
            Header("Chance", "ui.shuffle.odds_chance", ChanceX, ChanceW);
            Header("Left", "ui.shuffle.odds_left", LeftX, LeftW);
            _y -= HeaderH;

            for (int t = 0; t < ShuffleOdds.Tiers; t++) Row((ShuffleTier)t);

            if (Odds.Floor.HasValue)
            {
                var floor = Odds.Floor.Value;
                Line("Floor", Loc.Format("ui.shuffle.odds_floor", Loc.Get(ShuffleChoiceOverlay.TierKey(floor))),
                     30, Ink, FloorH, outline: 0f, floorSize: 20);
            }

            // **Every card of a rarity equally likely - unless a school leans** (MODES.md 59r), when
            // that sentence would be untrue and the lean is said in its place, with its figures read
            // off the deck's own rule, so the chance a refresh is sold at is the chance it deals.
            if (Odds.Leaning)
                Line("Lean", Loc.Format("ui.shuffle.odds_lean", ShuffleSchools.AffinityStep,
                                        ShuffleSchools.AffinityStep * ShuffleSchools.AffinityMostPoints),
                     26, Ink, NoteH + LeanH, outline: 0f, floorSize: 17, wrap: true);
            else
                Line("Note", Loc.Get("ui.shuffle.odds_equal"), 28, Ink, NoteH, outline: 0f, floorSize: 18, wrap: true);

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, 92f), () => Close());
        }

        void Header(string name, string key, float x, float width)
        {
            var text = UIKit.Titled(name, Panel, Loc.Get(key).Upper(), 26, Head, TextAnchor.MiddleCenter,
                                    new Vector2(width, HeaderH), new Vector2(.5f, 1f),
                                    new Vector2(x, _y - HeaderH * .5f), 0f, 0f);
            UIKit.Shrinkable(text, 16);
        }

        /// <summary>One rarity: its word on the trough in its colour, each card's chance, and how many are left.</summary>
        void Row(ShuffleTier tier)
        {
            float mid = _y - RowH * .5f;

            var trough = UIKit.Img("Trough", Panel, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(RarityW, RowH - 12f), new Vector2(.5f, 1f), new Vector2(RarityX, mid));
            if (trough != null) { trough.type = Image.Type.Sliced; trough.raycastTarget = false; }

            UIKit.Shrinkable(
                UIKit.Titled("Word", Panel, Loc.Get(ShuffleChoiceOverlay.TierKey(tier)).Upper(), 30,
                             ShuffleChoiceOverlay.TintOf(tier), TextAnchor.MiddleCenter,
                             new Vector2(RarityW - 28f, RowH - 12f), new Vector2(.5f, 1f),
                             new Vector2(RarityX, mid), 3f, 3f), 18);

            UIKit.Titled("Chance", Panel, Loc.Format("ui.shuffle.percent", Odds.Percent(tier)), 38, Amber,
                         TextAnchor.MiddleCenter, new Vector2(ChanceW, RowH), new Vector2(.5f, 1f),
                         new Vector2(ChanceX, mid), 0f, 0f);

            UIKit.Titled("Left", Panel, Odds.Cards(tier).ToString(), 34, Ink,
                         TextAnchor.MiddleCenter, new Vector2(LeftW, RowH), new Vector2(.5f, 1f),
                         new Vector2(LeftX, mid), 0f, 0f);

            _y -= RowH + RowGap;
        }

        void Line(string name, string text, int size, Color colour, float height, float outline, int floorSize,
                  bool wrap = false)
        {
            var label = UIKit.Titled(name, Panel, text, size, colour, TextAnchor.MiddleCenter,
                                     new Vector2(BodyW, height), new Vector2(.5f, 1f),
                                     new Vector2(0f, _y - height * .5f), outline, outline > 0f ? 3f : 0f, wrap: wrap);
            UIKit.Shrinkable(label, floorSize);
            _y -= height;
        }
    }
}
