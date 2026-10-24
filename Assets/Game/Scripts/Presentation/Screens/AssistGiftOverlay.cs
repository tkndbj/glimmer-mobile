using System.Collections.Generic;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The helping hand's gift (invariant 66): utilities for a player who keeps losing one level,
    /// shown as their own pictures, and one key that takes them.
    ///
    /// <para>
    /// <b>Raised by <see cref="AssistMoment"/> alone</b> - over the defeat panel that earned it, or on
    /// a calm hub if the player moved on first. It draws what <see cref="AssistLedger.OwedGift"/>
    /// holds, so the panel and the grant read one list.
    /// </para>
    /// <para>
    /// <b>The grant is what matters and it happens first</b> (<see cref="AssistLedger.ClaimGift"/>):
    /// the utilities land in the stock and the save is written before anything moves, so a process
    /// killed mid-flourish has still handed them over. What follows is a report: each card lifts and
    /// fades and the panel goes, leaving whatever was under it exactly as it was.
    /// </para>
    /// <para>
    /// <b>No way out but the key and the back key</b>, which is <see cref="GiftOverlay"/>'s rule: the
    /// scrim does not dismiss, and the back key only defers - the gift stays owed and the next calm
    /// hub offers it again. Nothing here can lose it.
    /// </para>
    /// </summary>
    public sealed class AssistGiftOverlay : ModalView
    {
        // ------------------------------------------------------------------ geometry
        /// <summary>The window: the victory panel's width, on <see cref="VictoryFrame"/>.</summary>
        const float PanelW = VictoryFrame.PanelWidth;

        /// <summary>
        /// Down from the panel's top edge: the present's centre, the sentence's centre, and the
        /// cards' top edge - <see cref="GiftOverlay"/>'s bands, so the two gifts read as one family.
        /// </summary>
        const float ArtY = 170f, ArtSize = 220f;
        const float BodyY = 340f, BodyW = 740f, BodyH = 110f;
        const float CardsTop = 430f, CardH = 250f, CardGap = 40f, CardMaxW = 300f, CardsRoom = 780f;

        /// <summary>
        /// A card's picture and its count, side by side: the picture as large as the card allows up
        /// to <see cref="IconMax"/> (two-thirds of its width), centred on the card's height and
        /// <see cref="Inset"/> from its left edge; the count beside it, a little under the picture's
        /// centre, like a tally. Down from a card's top edge.
        /// </summary>
        const float IconMax = 200f, IconShare = .66f, Inset = 10f, IconY = CardH * .5f;
        const float CountY = 160f, CountH = 80f, CountGap = -20f;
        const int CountSize = 60, CountFloor = 34;

        /// <summary>Up from the panel's foot: the key, in the gift panel's band.</summary>
        const float KeyW = 440f, KeyH = 120f, KeyY = 85f, FootH = 170f, Tail = 30f;

        const float PanelH = CardsTop + CardH + Tail + FootH;

        readonly List<AssistItem> _items = new List<AssistItem>();
        readonly List<RectTransform> _cards = new List<RectTransform>();

        Btn _key;
        bool _claiming, _leaving;

        protected override void Build()
        {
            _items.Clear();
            _cards.Clear();
            foreach (var item in AssistLedger.OwedGift)
                if (!string.IsNullOrEmpty(item.Id) && item.Count > 0) _items.Add(item);
            if (_items.Count == 0) { Flow.Dismiss(this); return; }

            // Not dismissed by the scrim: a stray tap beside a present must not put it away.
            Scrim = UIKit.Scrim(Content, .78f, null);
            var frame = VictoryFrame.Build(Content, PanelH, Loc.Get("ui.assist.title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // ------------------------------------------------------------ the present
            UIKit.Halo(Panel, Pal.Gold, ArtSize * 1.9f, .38f, new Vector2(0f, PanelH * .5f - ArtY));

            var art = UIKit.Img("Gift", Panel, Art.S("Ui/ic_gift"), Color.white, Vector2.one * ArtSize,
                                new Vector2(.5f, 1f), new Vector2(0f, -ArtY));
            art.preserveAspect = true;
            art.raycastTarget = false;
            // Breathing starts once the pop lands, never here (`GiftOverlay.Build`'s reason).
            art.transform.localScale = Vector3.zero;

            var body = UIKit.Titled("Body", Panel, Loc.Get("ui.assist.body"), 34, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(BodyW, BodyH), new Vector2(.5f, 1f), new Vector2(0f, -BodyY),
                                    outline: 3f, shadow: 3f, wrap: true);
            UIKit.Shrinkable(body, 24);

            // ------------------------------------------------------------ the cards
            int n = _items.Count;
            float cardW = Mathf.Min(CardMaxW, (CardsRoom - CardGap * (n - 1)) / n);
            float step = cardW + CardGap;
            float x = -step * (n - 1) * .5f;

            foreach (var item in _items)
            {
                var card = BuildCard(item, cardW, new Vector2(x, -CardsTop - CardH * .5f));
                card.localScale = Vector3.zero;
                _cards.Add(card);
                x += step;
            }

            // ------------------------------------------------------------ the key
            // The money face rather than the kit's green, for `GiftOverlay`'s reason: the window is
            // green, and a green key on it reads as no key at all.
            _key = UIKit.TextButton("Claim", Panel, Skins.Buy, Loc.Get("ui.assist.claim").Upper(), 44,
                                    new Vector2(KeyW, KeyH), new Vector2(.5f, 0f), new Vector2(0f, KeyY), Claim);
            _key.transform.localScale = Vector3.zero;

            // The entrance, in the gift panel's order, with the cards arriving one after another.
            Audio.Sfx("menu", .55f);

            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => { if (Panel) Tween.Scale(Panel, 1f, .5f, Ease.OutBack); });
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
            cue.Then(.10f, () =>
            {
                if (!art) return;
                Tween.Pop(art.transform, 0f, .6f)
                     .OnDone(() => { if (art) Tween.Breathe(art.transform, .04f, 2.4f); });
                Audio.Sfx("chime2", .45f, 1.06f);
            });
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                cue.Then(i == 0 ? .22f : .12f, () => { if (card) Tween.Pop(card, 0f, .45f); });
            }
            cue.Then(.20f, () => { if (_key) Tween.Pop(_key.transform, 0f, .45f); });
        }

        public override bool OnBack()
        {
            // Deferred, never lost: the gift stays owed and the next calm hub offers it again.
            if (!_claiming) Leave();
            return true;
        }

        void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            Close();
        }

        // ------------------------------------------------------------------ one card
        /// <summary>The picture's side on a card this wide.</summary>
        static float IconSide(float width) => Mathf.Min(IconMax, width * IconShare);

        /// <summary>The picture's centre across a card this wide, from the card's centre.</summary>
        static float IconX(float width) => -width * .5f + Inset + IconSide(width) * .5f;

        /// <summary>
        /// One utility standing on the window itself, as the gift panel's prizes do: a soft glow in
        /// the item's shop colour, its own picture large, and "x1" beside it. No name: the picture is
        /// the one the action bar draws, so it is already what the player will look for.
        /// </summary>
        RectTransform BuildCard(AssistItem item, float width, Vector2 pos)
        {
            var tint = RewardArt.Tint(ChestDropKind.Utility, item.Id);
            float side = IconSide(width);
            float iconX = IconX(width);

            var rt = UIKit.Box("Card_" + item.Id, Panel, new Vector2(width, CardH), new Vector2(.5f, 1f), pos);

            UIKit.Halo(rt, tint, width, .30f, new Vector2(iconX, 0f));

            var icon = UIKit.Img("Icon", rt, RewardArt.Icon(ChestDropKind.Utility, item.Id), Color.white,
                                 Vector2.one * side, new Vector2(.5f, 1f), new Vector2(iconX, -IconY));
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            Tween.Breathe(icon.transform, .05f, 2.2f);

            // The count against the picture's right edge - tucked into the art's own transparent margin
            // (a negative gap), so the pair reads as one - in what the picture leaves, on one line.
            float countW = width * .5f - (iconX + side * .5f) - CountGap;
            float countX = width * .5f - countW * .5f;
            UIKit.OneLineLabel(
                UIKit.Titled("Count", rt, "x" + item.Count, CountSize, Pal.Cream,
                             TextAnchor.MiddleLeft, new Vector2(countW, CountH),
                             new Vector2(.5f, 1f), new Vector2(countX, -CountY), outline: 3f, shadow: 3f),
                countW, CountSize, CountFloor);

            return rt;
        }

        // ------------------------------------------------------------------ claiming
        /// <summary>
        /// Takes the gift: the ledger grants every utility and writes the save, then each card lifts
        /// into a burst of its own colour and the panel goes. Pressed once - the key is spent on the
        /// tap, and a second tap would find nothing owed anyway.
        /// </summary>
        void Claim()
        {
            if (_claiming || _leaving) return;
            _claiming = true;
            if (_key) _key.Interactable = false;

            var landed = AssistLedger.ClaimGift();

            // Beaten to it, or every utility already at its ceiling: nothing to celebrate and nothing
            // lost - the panel simply goes.
            if (landed.Count == 0) { Leave(); return; }

            Audio.Sfx("chest", .8f);
            Audio.Sfx("win", .45f, 1f, .06f);

            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (!card) continue;

                var tint = RewardArt.Tint(ChestDropKind.Utility, _items[i].Id);
                // From the picture's centre: the card is placed by its centre under the panel's top
                // edge, and the burst from the panel's centre (`UIKit.Box` pivots at centre, 44d).
                Burst.Sparks(Panel, card.anchoredPosition + new Vector2(IconX(card.sizeDelta.x), PanelH * .5f + CardH * .5f - IconY),
                             tint, 16);

                Tween.Punch(card, .22f, .3f);
                Tween.Move(card, card.anchoredPosition + new Vector2(0f, 120f), .55f, Ease.InQuad);
                Tween.Fade(UIKit.Group(card), 0f, .55f);
            }

            _leaving = true;
            var cue = new Cue(this);
            cue.Then(.6f, () => Close(quiet: true));
        }
    }
}
