using System;
using System.Collections.Generic;
using GlimmerGrove.Daily;
using GlimmerGrove.Gifts;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The gift panel (invariant 63): a present from the owner, what it holds, and one key that
    /// takes it - after which the coins and gems fly into the hub's own readouts.
    ///
    /// <para>
    /// <b>Raised by <see cref="GiftMoment"/> alone</b>, on an idle hub, with every gift still
    /// waiting. It draws the <em>sum</em> - one card for the coins, one for the gems, whichever the
    /// gifts carry - because a player owed three gifts is owed one pile, and the claim queues each
    /// gift under its own id underneath (<see cref="GiftLedger.Claim"/>), so the server's record is
    /// per gift however the panel adds them up.
    /// </para>
    /// <para>
    /// <b>The payout is the chest's</b> (<c>ChestOverlay.Collect</c>): the flight snapshots the
    /// readouts <em>before</em> the grant and holds them while the panel stands
    /// (<see cref="RewardFlight.Hold"/>), the key grants, the chrome fades, and the cards empty into
    /// the pills. The grant is what matters and it happens first: a process killed mid-cascade has
    /// still claimed the gift, and the hub repaints to the new balance on its own. A hub with no
    /// readout to fly into closes instead - the gift is banked either way.
    /// </para>
    /// <para>
    /// <b>No way out but the key and the back key.</b> The scrim does not dismiss (a stray tap must
    /// not put a present away), and the back key only defers: the gift waits on the server and the
    /// panel comes back next session (<see cref="GiftPrompt"/>). Nothing here can lose it.
    /// </para>
    /// </summary>
    public sealed class GiftOverlay : ModalView
    {
        /// <summary>The gifts waiting, oldest first. Set by the caller's configure callback.</summary>
        public IReadOnlyList<AdminGift> Gifts { get; set; }

        // ------------------------------------------------------------------ geometry
        /// <summary>The window: the victory panel's width, on <see cref="VictoryFrame"/>.</summary>
        const float PanelW = VictoryFrame.PanelWidth;

        /// <summary>
        /// Down from the panel's top edge: the present's centre, the sentence's centre, and the
        /// cards' <em>top edge</em>. The present spans 60-280 and the sentence 285-395, so the cards
        /// start below 395. <c>UIKit.Img</c> pivots at centre (44d), so a card is placed by its
        /// centre, <c>CardsTop + CardH / 2</c>: it was placed at <c>CardsTop</c> itself, which put
        /// its top 150 units higher than this said, over the sentence.
        /// </summary>
        const float ArtY = 170f, ArtSize = 220f;
        const float BodyY = 340f, BodyW = 740f, BodyH = 110f;
        const float CardsTop = 430f, CardW = 300f, CardH = 250f, CardGap = 40f;

        /// <summary>Down from a card's top edge: the mark, the amount and its name (centres).</summary>
        const float IconY = 62f, AmountY = 160f, KindY = 214f, LineW = 280f;

        /// <summary>Up from the panel's foot: the key, in the deals sheet's band.</summary>
        const float KeyW = 440f, KeyH = 120f, KeyY = 85f, FootH = 170f, Tail = 30f;

        const float PanelH = CardsTop + CardH + Tail + FootH;

        readonly List<AdminGift> _gifts = new List<AdminGift>();

        /// <summary>The two cards, by currency; null for a currency the gifts do not carry.</summary>
        RectTransform _creditCard, _gemCard;

        /// <summary>Everything that is not a card: faded when the cards empty.</summary>
        readonly List<Graphic> _chrome = new List<Graphic>();
        readonly List<RectTransform> _chromeGroups = new List<RectTransform>();

        Btn _key;

        /// <summary>Opened before the grant so it can snapshot what each pill read - see <see cref="RewardFlight.Begin"/>.</summary>
        RewardFlight _flight;

        bool _claiming, _leaving;

        protected override void Build()
        {
            _gifts.Clear();
            if (Gifts != null) foreach (var gift in Gifts) if (gift != null && gift.Any) _gifts.Add(gift);
            if (_gifts.Count == 0) { Flow.Dismiss(this); return; }

            long credits = 0L, gems = 0L;
            foreach (var gift in _gifts) { credits += gift.Credits; gems += gift.Gems; }

            // What each pill reads before the grant, captured rather than derived, and held while
            // the panel stands so nothing moves behind it (`ChestOverlay.Build`'s two lines).
            _flight = RewardFlight.Begin();
            _flight.Hold(this);

            // Not dismissed by the scrim: a stray tap beside a present must not put it away.
            Scrim = UIKit.Scrim(Content, .78f, null);
            var frame = VictoryFrame.Build(Content, PanelH, Loc.Get("ui.gift.title"), PanelW);
            Backing = frame.Backing;
            Panel = frame.Panel;

            // The light show and the frame are chrome; the cards built below are not.
            for (int i = 0; i < frame.Fit.childCount; i++)
            {
                var child = (RectTransform)frame.Fit.GetChild(i);
                if (child != Panel) _chromeGroups.Add(child);
            }
            _chrome.Add(frame.Backing);
            for (int i = 0; i < Panel.childCount; i++) _chromeGroups.Add((RectTransform)Panel.GetChild(i));

            // ------------------------------------------------------------ the present
            // The halo is placed from the panel's centre (`UIKit.Halo` anchors there); the present
            // and everything under it from its top edge.
            var halo = UIKit.Halo(Panel, Pal.Gold, ArtSize * 1.9f, .38f, new Vector2(0f, PanelH * .5f - ArtY));
            _chromeGroups.Add((RectTransform)halo.transform);

            var art = UIKit.Img("Gift", Panel, Art.S("Ui/ic_gift"), Color.white, Vector2.one * ArtSize,
                                new Vector2(.5f, 1f), new Vector2(0f, -ArtY));
            art.preserveAspect = true;
            art.raycastTarget = false;
            // Breathing starts once the pop lands (below), never here: `Breathe` breathes around
            // the scale it finds, and here that is nought - it wrote nought every frame against
            // the pop, and whichever tween ran last that frame won, so the present came and went.
            art.transform.localScale = Vector3.zero;
            _chromeGroups.Add((RectTransform)art.transform);

            var body = UIKit.Titled("Body", Panel, Loc.Get("ui.gift.body"), 34, Pal.Cream, TextAnchor.MiddleCenter,
                                    new Vector2(BodyW, BodyH), new Vector2(.5f, 1f), new Vector2(0f, -BodyY),
                                    outline: 3f, shadow: 3f, wrap: true);
            UIKit.Shrinkable(body, 24);
            _chrome.Add(body);

            // ------------------------------------------------------------ the cards
            int cards = (credits > 0 ? 1 : 0) + (gems > 0 ? 1 : 0);
            float step = CardW + CardGap;
            float x = cards == 2 ? -step * .5f : 0f;

            if (credits > 0)
            {
                _creditCard = BuildCard(ChestDropKind.Credits, credits, new Vector2(x, -CardsTop - CardH * .5f));
                x += step;
            }
            if (gems > 0) _gemCard = BuildCard(ChestDropKind.Gems, gems, new Vector2(x, -CardsTop - CardH * .5f));

            // ------------------------------------------------------------ the key
            // The money face rather than the kit's green: the window is green, and a green key on
            // it reads as no key at all (`DealOverlay.DealRow`'s rule).
            _key = UIKit.TextButton("Claim", Panel, Skins.Buy, Loc.Get("ui.gift.claim").Upper(), 44,
                                    new Vector2(KeyW, KeyH), new Vector2(.5f, 0f), new Vector2(0f, KeyY), Claim);
            _key.transform.localScale = Vector3.zero;
            _chromeGroups.Add((RectTransform)_key.transform);

            // The entrance, in the deals sheet's order, with the present landing after the word.
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
            cue.Then(.26f, () =>
            {
                if (_key) Tween.Pop(_key.transform, 0f, .45f);
            });
        }

        public override bool OnBack()
        {
            // Deferred, never lost: the gift waits on the server and is offered again next session.
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
        /// <summary>
        /// One prize standing on the window itself (the owner, 2026-10-11: no dark plate, no edge):
        /// a soft glow in the prize's colour, its mark, the amount and its name, each line held to
        /// one line inside the card. The card is a bare box, kept as the flight's source. Mirrored
        /// by <c>Tools/render_gift.py</c>.
        /// </summary>
        RectTransform BuildCard(ChestDropKind kind, long amount, Vector2 pos)
        {
            var tint = RewardArt.Tint(kind, null);

            var rt = UIKit.Box("Card", Panel, new Vector2(CardW, CardH), new Vector2(.5f, 1f), pos);

            UIKit.Halo(rt, tint, CardW, .30f);

            var icon = UIKit.Img("Icon", rt, RewardArt.Icon(kind, null), Color.white,
                                 new Vector2(176f, 114f), new Vector2(.5f, 1f), new Vector2(0f, -IconY));
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RewardArt.Glyph(icon, kind, 11f);
            Tween.Breathe(icon.transform, .05f, 2.2f);

            UIKit.OneLineLabel(
                UIKit.Titled("Amount", rt, "+" + amount.ToString("N0"), 52, Pal.Cream,
                             TextAnchor.MiddleCenter, new Vector2(LineW, 62f),
                             new Vector2(.5f, 1f), new Vector2(0f, -AmountY), outline: 3f, shadow: 3f),
                LineW, 52, 30);

            UIKit.OneLineLabel(
                UIKit.Titled("Kind", rt, RewardArt.Name(kind, null), 28, Pal.A(tint, .95f),
                             TextAnchor.MiddleCenter, new Vector2(LineW, 44f),
                             new Vector2(.5f, 1f), new Vector2(0f, -KindY), outline: 2f, shadow: 2f),
                LineW, 28, 20);

            return rt;
        }

        // ------------------------------------------------------------------ claiming
        /// <summary>
        /// Takes every gift: the ledger queues each currency under its id and the balance moves at
        /// once; then the cards empty into the hub's readouts. Pressed once - the key is spent on
        /// the tap, and a second tap finds nothing left to grant anyway.
        /// </summary>
        void Claim()
        {
            if (_claiming || _leaving) return;
            _claiming = true;
            if (_key) _key.Interactable = false;

            var claim = GiftLedger.Claim(_gifts);

            // Beaten to it - another phone claimed it since the panel opened, or the awards were
            // already queued. Nothing to show and nothing lost: the hold is released as the panel goes.
            if (!claim.Any) { Leave(); return; }

            Audio.Sfx("chest", .8f);
            Audio.Sfx("win", .45f, 1f, .06f);

            bool flying = false;
            if (claim.Credits > 0 && _creditCard)
                flying |= _flight.Add(new ChestDrop(ChestDropKind.Credits, (int)Math.Min(claim.Credits, int.MaxValue)), _creditCard);
            if (claim.Gems > 0 && _gemCard)
                flying |= _flight.Add(new ChestDrop(ChestDropKind.Gems, (int)Math.Min(claim.Gems, int.MaxValue)), _gemCard);

            // No readout to fly into (never on the hub, where this is raised): the gift is banked
            // and the panel simply goes.
            if (!flying) { Leave(); return; }

            _leaving = true;
            FadeChrome();
            _flight.Play(Content, () => Flow.Dismiss(this));
        }

        /// <summary>
        /// Clears everything that is not a prize, the scrim included - the hub's pills are under it,
        /// and a token cannot be seen landing on something that is not visible.
        /// </summary>
        void FadeChrome()
        {
            if (Scrim) Tween.Fade(Scrim, 0f, RewardFlight.ClearAt);

            for (int i = 0; i < _chrome.Count; i++)
                if (_chrome[i]) Tween.Fade(_chrome[i], 0f, RewardFlight.ClearAt * .8f);

            for (int i = 0; i < _chromeGroups.Count; i++)
            {
                var rt = _chromeGroups[i];
                if (!rt || rt == _creditCard || rt == _gemCard) continue;
                Tween.Fade(UIKit.Group(rt), 0f, RewardFlight.ClearAt * .8f);
            }
        }
    }
}
