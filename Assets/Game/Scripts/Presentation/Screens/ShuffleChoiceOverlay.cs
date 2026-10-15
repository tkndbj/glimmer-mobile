using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A hand of upgrade cards over a held siege: three plates in a row, each a picture, a name,
    /// one line and its tier, and the one the player taps goes into the build.
    ///
    /// <para>
    /// <b>It cannot be dismissed without a card.</b> The scrim closes nothing and the back key
    /// is swallowed, because a hand walked away from is a build a player did not choose, and
    /// the run is held for as long as this stands (<c>RunHold.Covered</c>) - so there is no
    /// clock running under a decision nobody has made.
    /// </para>
    /// <para>
    /// <b>It wears the Deals sheet's frame</b> (<see cref="ChallengeTierOverlay"/>, on
    /// <see cref="VictoryFrame"/>), at the owner's instruction on 2026-10-09: the fan, the green
    /// window, the crown and the banner carrying the word, so a hand and a deal are one piece of
    /// furniture. The window is <see cref="Layout.ShuffleHandLayout"/>'s width and height, and
    /// the layout keeps the wave line under the banner's reach (<c>CrestFoot</c>).
    /// </para>
    /// <para>
    /// <b>Furniture from the kit, light from the palette</b> (invariant 44, the owner's rule
    /// about procedural chrome): every card is the HUD's own card plate with its picture in the
    /// kit's seat and its tier on the kit's trough; what says the tier is the halo behind the
    /// picture and the colour of the word, never a plate drawn in code. The pictures are the
    /// skill-icon pack's, cut by <c>Tools/make_shuffle_art.py</c> and scoped with the lane's
    /// chapter (<c>SiegeMode.ShuffleArt</c>); one that has not arrived draws no picture rather
    /// than a white rectangle (invariant 7b).
    /// </para>
    /// <para>
    /// <b>The refresh key under the cards</b> (MODES.md 59p) swaps the hand for a fresh one for
    /// gems, its price doubling with every refresh the run has bought. It is drawn only while the
    /// deck has something fresh to deal, says its price on its face, and asks the balance only
    /// when tapped: short with a shop to hand stacks the gem shelf over the hand (the hand is
    /// still there when it closes, as the continue panel's is), short with none says how many
    /// more are needed. The new hand pops in where the old one stood, on the same panel.
    /// </para>
    /// <para>
    /// <b>Where things sit is <see cref="Layout.ShuffleHandLayout"/>'s</b> and what it looks
    /// like is <c>Tools/render_shuffle.py</c>'s, which is the only thing that can say whether
    /// three cards of this width still carry a translated name.
    /// </para>
    /// </summary>
    public sealed class ShuffleChoiceOverlay : ModalView
    {
        public IReadOnlyList<ShuffleCard> Hand;
        public ShuffleBuild Held;
        public int Wave;
        public Action<ShuffleCard> OnPick;

        /// <summary>
        /// What the next refresh costs in gems, or nought when none can be sold (the deck has
        /// nothing fresh). A reading rather than a figure, because every refresh doubles it.
        /// Null offers no refresh at all.
        /// </summary>
        public Func<long> RefreshPrice;

        /// <summary>
        /// Pays for a refresh and deals it (<see cref="ShuffleRefresh.TryBuy"/>), answering the
        /// new hand, or null when nothing was bought and nothing changed.
        /// </summary>
        public Func<IReadOnlyList<ShuffleCard>> OnRefresh;

        /// <summary>
        /// The odds of what a refresh would deal now (<see cref="ShuffleOdds.ForRefresh"/>), read
        /// when the "i" is tapped. Null draws no "i".
        /// </summary>
        public Func<ShuffleOdds> RefreshOdds;

        bool _picked;

        /// <summary>True for the one rebuild that lays a refreshed hand: its cards pop in again.</summary>
        bool _redealt;

        /// <summary>The colour a tier is said in: the word, the halo and the toast.</summary>
        public static Color TintOf(ShuffleTier tier)
        {
            switch (tier)
            {
                case ShuffleTier.Rare: return Pal.Azure;
                case ShuffleTier.Epic: return Pal.Foxglove;
                case ShuffleTier.Legendary: return Pal.Gold;
                default: return Pal.Mint;
            }
        }

        /// <summary>The loc key for a tier's one word. Derived from the tier's own name.</summary>
        public static string TierKey(ShuffleTier tier)
            => "ui.shuffle.tier." + tier.ToString().ToLowerInvariant();

        protected override void Build()
        {
            var L = Layout.ShuffleHandLayout.Default;
            int cards = Hand != null ? Hand.Count : 0;

            Scrim = UIKit.Scrim(Content, .78f, null);

            // The Deals sheet's stack: the frame builds the fan, the window, the crown and the
            // banner with the word on it, and fits the whole to a short canvas. **The banner
            // carries the wave**, because a banner carries one word (DEALS, REWARD) and "choose
            // an upgrade" is a sentence in every language - it goes on the line under, where the
            // Deals sheet says what the free allowance is.
            // **As tall as the hand's rows** (`grand_hand` deals five: three over two), and the
            // frame fits the whole block to the canvas it is drawn on (`VictoryFrame.Fit`).
            long price = RefreshPrice != null ? RefreshPrice() : 0L;
            bool refresh = price > 0L && OnRefresh != null;

            var frame = VictoryFrame.Build(Content, L.PanelHeightFor(cards, refresh), Loc.Format("ui.shuffle.wave", Wave).Upper(), L.PanelWidth);
            Backing = frame.Backing;
            Panel = frame.Panel;

            UIKit.Shrinkable(
                UIKit.Titled("Choose", Panel, Loc.Get("ui.shuffle.choose"), 32,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter,
                             new Vector2(L.PanelWidth - 80f, L.NoteHeightLine), new Vector2(.5f, 1f),
                             new Vector2(0f, -L.NoteY), 2f, 2f),
                20);

            for (int i = 0; i < cards; i++)
            {
                L.Place(i, cards, out float x, out float top);
                Card(Hand[i], i, new Vector2(x, -(top + L.CardHeight * .5f)), L);
            }

            if (refresh) RefreshKey(price, cards, L);

            if (Rebuilding)
            {
                frame.Settle();
                return;
            }

            Audio.Hush("click");
            Audio.Sfx("menu", .55f);

            // The entrance, in the Deals sheet's order: the crown first, the window under a
            // crown still settling, then the banner and its word; the cards pop once the window
            // has landed (`CardPopAt`).
            var cue = new Cue(this);
            cue.With(() => { if (frame.Crown) Tween.Pop(frame.Crown.transform, 0f, .5f); });
            cue.Then(.30f, () => Tween.Scale(Panel, 1f, .5f, Ease.OutBack));
            cue.Then(.18f, () => { if (frame.Banner) Tween.Pop(frame.Banner.transform, 0f, .5f); });
            cue.Then(.16f, () => { if (frame.Word) Tween.Pop(frame.Word.transform, 0f, .55f); });
        }

        /// <summary>When the first card pops: after the window has landed, then one beat per card.</summary>
        const float CardPopAt = .55f, CardPopStagger = .09f;

        void Card(ShuffleCard card, int index, Vector2 at, Layout.ShuffleHandLayout L)
        {
            var tint = TintOf(card.Tier);
            var size = new Vector2(L.CardWidth, L.CardHeight);

            var plate = UIKit.Button("Card_" + card.Id, Panel, Art.S("Ui/" + Skins.Card), size,
                                     new Vector2(.5f, 1f), at, () => Pick(card));
            var face = plate.GetComponent<Image>();
            if (face != null) face.type = Image.Type.Sliced;
            plate.ClickSfx = "poke";

            var t = plate.transform;

            // Measured down from the card's own top edge, which is what the layout counts in.
            float top = L.CardHeight * .5f;

            // The picture in the kit's seat, lit from behind in the tier's colour: the halo is
            // the reading at a distance and the word below is the reading up close.
            float seatY = top - L.SeatDown;

            UIKit.Halo(t, tint, L.SeatSize * 1.9f, card.Tier == ShuffleTier.Common ? .18f : .34f,
                       new Vector2(0f, seatY));

            var seat = UIKit.Img("Seat", t, Art.S("Ui/" + Skins.Slot), Color.white,
                                 Vector2.one * L.SeatSize, new Vector2(.5f, .5f), new Vector2(0f, seatY));
            if (seat != null) { seat.type = Image.Type.Sliced; seat.raycastTarget = false; }

            var picture = Art.S(card.Icon);
            var icon = UIKit.Img("Icon", seat != null ? seat.transform : t, picture, Color.white,
                                 Vector2.one * L.IconSize, new Vector2(.5f, .5f), Vector2.zero);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = picture != null;

            // The name, two lines at most, shrunk before it wraps a third time.
            var name = UIKit.Titled("Name", t, Loc.Get(card.NameKey), L.NameSize, Pal.Gold,
                                    TextAnchor.MiddleCenter,
                                    new Vector2(L.CardWidth - L.Inset * 2f, L.NameHeight),
                                    new Vector2(.5f, .5f), new Vector2(0f, top - L.NameDown), 3f, 3f,
                                    wrap: true);
            UIKit.Shrinkable(name, L.NameFloor);
            name.raycastTarget = false;

            // Filled from the card on the table, which is the run's tuned card (MODES.md 59l), so
            // a retuned figure is the figure printed, and a level-stating note prints the level
            // this copy reaches (a second Execution reads 25%). A note with no slot formats to itself.
            int heldNow = Held != null ? Held.Copies(card) : 0;
            var note = UIKit.Titled("Note", t, Loc.Format(card.NoteKey, ShuffleNotes.Args(card, heldNow)), L.NoteSize,
                                    Color.white, TextAnchor.UpperCenter,
                                    new Vector2(L.CardWidth - L.Inset * 2f, L.NoteHeight),
                                    new Vector2(.5f, .5f), new Vector2(0f, top - L.NoteDown), 2f, 0f,
                                    wrap: true);
            UIKit.Shrinkable(note, L.NoteFloor);
            note.raycastTarget = false;

            // **The diamonds** (MODES.md 59n): green when this card goes with a secret the build
            // has started, purple when it brings its own evolution closer. Under the note, so
            // they read as a fact about this card and never as part of its tier.
            Diamonds(t, card, top, L);

            // The tier on the kit's trough, in its own colour - and how many copies the build
            // already holds, because a second copy is a different offer from a first.
            var trough = UIKit.Img("Tier", t, Art.S("Ui/" + Skins.Trough), Color.white,
                                   new Vector2(L.CardWidth - L.Inset * 2f, L.TierHeight),
                                   new Vector2(.5f, .5f), new Vector2(0f, top - L.TierDown));
            if (trough != null) { trough.type = Image.Type.Sliced; trough.raycastTarget = false; }

            int copies = Held != null ? Held.Copies(card) : 0;
            string tier = Loc.Get(TierKey(card.Tier)).Upper();
            if (copies > 0) tier = Loc.Format("ui.shuffle.held", tier, copies + 1).Upper();

            var word = UIKit.Titled("Word", trough != null ? trough.transform : t, tier, L.TierSize, tint,
                                    TextAnchor.MiddleCenter,
                                    new Vector2(L.CardWidth - L.Inset * 2f - 24f, L.TierHeight),
                                    new Vector2(.5f, .5f), Vector2.zero, 3f, 3f);
            UIKit.Shrinkable(word, L.TierFloor);
            word.raycastTarget = false;

            if (card.Tier >= ShuffleTier.Epic) Sheen.Attach((RectTransform)t, 2.8f);

            // A rebuild is the same hand in a new state and does not pop; a refreshed hand is a
            // new hand, and pops at once - the window is already standing.
            if (Rebuilding && !_redealt) return;

            t.localScale = Vector3.zero;
            Tween.Pop(t, 0f, .5f, (_redealt ? 0f : CardPopAt) + index * CardPopStagger);
        }

        /// <summary>
        /// The refresh key: the kit's gem pill (violet, as every gem price in the game is) with
        /// the price on its face and the gem after it. Pops with the cards on the way in.
        /// </summary>
        void RefreshKey(long price, int cards, Layout.ShuffleHandLayout L)
        {
            var key = UIKit.TextButton("Refresh", Panel, "btn_violet",
                                       Loc.Format("ui.shuffle.refresh", Compact.Number(price)).Upper(),
                                       40, new Vector2(L.RefreshWidth, L.RefreshHeight),
                                       new Vector2(.5f, 1f), new Vector2(L.RefreshX, -L.RefreshCentre(cards)),
                                       Refresh, Art.S("Ui/ic_gem"), iconTrails: true);
            UIKit.Shrinkable(key.Label, 24);
            UIKit.FitLabel(key);

            // The odds of what the refresh can deal, beside the key that buys it: a random hand
            // sold for gems says its odds before it is bought (ShuffleOddsOverlay).
            Btn odds = null;
            if (RefreshOdds != null)
                odds = UIKit.IconButton("Odds", Panel, Skins.Aside, "ic_info", Vector2.one * L.OddsSize,
                                        new Vector2(.5f, 1f), new Vector2(L.OddsX, -L.RefreshCentre(cards)),
                                        ShowOdds);

            if (Rebuilding && !_redealt) return;

            float at = (_redealt ? 0f : CardPopAt) + cards * CardPopStagger;
            key.transform.localScale = Vector3.zero;
            Tween.Pop(key.transform, 0f, .5f, at);

            if (odds == null) return;
            odds.transform.localScale = Vector3.zero;
            Tween.Pop(odds.transform, 0f, .5f, at + CardPopStagger);
        }

        /// <summary>
        /// Opens the odds of a refresh over the hand. The hand and the run stand under it, and it
        /// closes back to them; it buys nothing.
        /// </summary>
        void ShowOdds()
        {
            if (_picked || _redealt || RefreshOdds == null) return;

            var odds = RefreshOdds();
            if (odds != null) Flow.Modal<ShuffleOddsOverlay>(v => v.Odds = odds);
        }

        /// <summary>Whether a shop is reachable that could sell gems right now (<c>RunContinueFlow.GemsForSale</c>'s reading).</summary>
        static bool GemsForSale => StoreService.IsAvailable && StoreRules.Catalog.HasGems;

        /// <summary>
        /// The refresh key was tapped. The balance is asked now rather than when the panel was
        /// drawn, because a sync or a purchase moves it while the hand stands; the debit itself is
        /// asked again at its own instant (<see cref="ShuffleRefresh.TryBuy"/>).
        /// </summary>
        void Refresh()
        {
            if (_picked || _redealt || OnRefresh == null || RefreshPrice == null) return;

            long price = RefreshPrice();
            if (price <= 0L) return;

            switch (GemPrice.ChoiceFor(Profile.Gems, price, GemsForSale))
            {
                case GemChoice.BuyGems:
                    // Stacked over the hand rather than replacing it, so the hand is still here
                    // when the shelf closes; the run stays held under both.
                    Flow.Modal<GemShopOverlay>();
                    return;

                case GemChoice.Unavailable:
                    Audio.SfxVaried("back", .5f);
                    Scenery.Toast(Content, Loc.Format("ui.shop.short_gems",
                                                      Compact.Number(price - Profile.Gems)), Pal.Gold, 2.2f);
                    return;
            }

            var hand = OnRefresh();
            if (hand == null || hand.Count == 0)
            {
                // The balance moved between the tap and the debit, or the deck had nothing fresh
                // after all: nothing was charged and the hand on the table stands.
                Audio.SfxVaried("back", .5f);
                return;
            }

            Hand = hand;
            Audio.Sfx("whoosh", .6f);

            _redealt = true;
            try { Rebuild(); }
            finally { _redealt = false; }
        }

        /// <summary>What every evolution's diamond glows in: its mark's own purple.</summary>
        public static readonly Color EvolutionGlow = Pal.Foxglove;

        /// <summary>
        /// What <paramref name="bond"/>'s diamond glows in: the colour its picture was turned to
        /// (<see cref="ShuffleBond.Hue"/>), lifted to full brightness so a glow reads on the
        /// card. A pale secret (the sun, the blades) glows pale.
        /// </summary>
        public static Color GlowOf(ShuffleBond bond)
        {
            if (bond == null || !bond.IsSecret) return EvolutionGlow;
            float s = Mathf.Clamp01(bond.Saturation / 100f) * .85f;
            return Color.HSVToRGB(bond.Hue / 360f, s, 1f);
        }

        readonly List<ShuffleBond> _marks = new List<ShuffleBond>(4);

        /// <summary>
        /// The marks under a card's note, centred in their row: one per secret the card goes
        /// with, each in that secret's own colour, then the evolution's purple; nothing for a
        /// card that brings neither closer (<see cref="ShuffleBonds.Advanced"/>,
        /// <see cref="ShuffleBonds.Evolves"/>).
        /// </summary>
        void Diamonds(Transform t, ShuffleCard card, float top, Layout.ShuffleHandLayout L)
        {
            if (Held == null) return;

            _marks.Clear();
            ShuffleBonds.Advanced(card, Held, _marks);
            bool evolve = ShuffleBonds.Evolves(card, Held);

            int secrets = Mathf.Min(_marks.Count, L.MostDiamonds - (evolve ? 1 : 0));
            int marks = secrets + (evolve ? 1 : 0);
            if (marks == 0) return;

            float pitch = L.DiamondSize + L.DiamondGap;
            float x = -(marks - 1) * pitch * .5f;
            float y = top - L.DiamondDown;

            for (int i = 0; i < secrets; i++, x += pitch)
                Diamond(t, _marks[i].Mark, GlowOf(_marks[i]), new Vector2(x, y), L);

            if (evolve) Diamond(t, ShuffleBond.EvolutionMark, EvolutionGlow, new Vector2(x, y), L);
        }

        /// <summary>
        /// One diamond, breathing in a glow of its own colour. <b>Nothing at all when its picture
        /// has not arrived</b> (invariant 7b): an <c>Image</c> with no sprite is a white square.
        /// </summary>
        static void Diamond(Transform t, string address, Color glow, Vector2 at, Layout.ShuffleHandLayout L)
        {
            var sprite = Art.S("Ui/" + address);
            if (sprite == null) return;

            var halo = UIKit.Halo(t, glow, L.DiamondSize * L.DiamondGlow, .6f, at);
            halo.raycastTarget = false;

            var img = UIKit.Img("Diamond", t, sprite, Color.white, Vector2.one * L.DiamondSize, new Vector2(.5f, .5f), at);
            img.preserveAspect = true;
            img.raycastTarget = false;

            Tween.Breathe(img.transform, .12f, 1.4f);
            Tween.Breathe(halo.transform, .2f, 1.4f);
        }

        void Pick(ShuffleCard card)
        {
            if (_picked || card == null) return;
            _picked = true;

            var pick = OnPick;
            OnPick = null;

            Audio.Sfx("collect", .7f);
            Close(() => pick?.Invoke(card));
        }

        /// <summary>The back key is swallowed: a hand is answered with a card and nothing else.</summary>
        public override bool OnBack() => true;
    }
}
