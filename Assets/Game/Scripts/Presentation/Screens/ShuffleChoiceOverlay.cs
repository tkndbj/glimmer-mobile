using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Shuffle;
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

        bool _picked;

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
            var frame = VictoryFrame.Build(Content, L.PanelHeightFor(cards), Loc.Format("ui.shuffle.wave", Wave).Upper(), L.PanelWidth);
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
            // a retuned figure is the figure printed. A note with no slot formats to itself.
            var note = UIKit.Titled("Note", t, Loc.Format(card.NoteKey, ShuffleNotes.Args(card)), L.NoteSize,
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

            if (Rebuilding) return;

            t.localScale = Vector3.zero;
            Tween.Pop(t, 0f, .5f, CardPopAt + index * CardPopStagger);
        }

        /// <summary>What the secret's diamond glows in, and the evolution's: the two marks' own colours.</summary>
        public static readonly Color SecretGlow = Pal.Verdant, EvolutionGlow = Pal.Foxglove;

        /// <summary>
        /// The marks under a card's note, centred in their row: the secret's, the evolution's, or
        /// both, and nothing for a card that brings neither closer (<see cref="ShuffleBonds.Advances"/>,
        /// <see cref="ShuffleBonds.Evolves"/>).
        /// </summary>
        void Diamonds(Transform t, ShuffleCard card, float top, Layout.ShuffleHandLayout L)
        {
            if (Held == null) return;

            bool secret = ShuffleBonds.Advances(card, Held);
            bool evolve = ShuffleBonds.Evolves(card, Held);
            int marks = (secret ? 1 : 0) + (evolve ? 1 : 0);
            if (marks == 0) return;

            float pitch = L.DiamondSize + L.DiamondGap;
            float x = -(marks - 1) * pitch * .5f;
            float y = top - L.DiamondDown;

            if (secret)
            {
                Diamond(t, GlimmerGrove.Content.SiegeMode.SecretMark, SecretGlow, new Vector2(x, y), L.DiamondSize);
                x += pitch;
            }

            if (evolve) Diamond(t, GlimmerGrove.Content.SiegeMode.EvolutionMark, EvolutionGlow, new Vector2(x, y), L.DiamondSize);
        }

        /// <summary>
        /// One diamond, breathing in a glow of its own colour. <b>Nothing at all when its picture
        /// has not arrived</b> (invariant 7b): an <c>Image</c> with no sprite is a white square.
        /// </summary>
        static void Diamond(Transform t, string address, Color glow, Vector2 at, float size)
        {
            var sprite = Art.S("Ui/" + address);
            if (sprite == null) return;

            var halo = UIKit.Halo(t, glow, size * 2.6f, .6f, at);
            halo.raycastTarget = false;

            var img = UIKit.Img("Diamond", t, sprite, Color.white, Vector2.one * size, new Vector2(.5f, .5f), at);
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
