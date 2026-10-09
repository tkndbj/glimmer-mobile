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
            var frame = VictoryFrame.Build(Content, L.PanelHeight, Loc.Format("ui.shuffle.wave", Wave).Upper(), L.PanelWidth);
            Backing = frame.Backing;
            Panel = frame.Panel;

            UIKit.Shrinkable(
                UIKit.Titled("Choose", Panel, Loc.Get("ui.shuffle.choose"), 32,
                             new Color(1f, .96f, .88f, .82f), TextAnchor.MiddleCenter,
                             new Vector2(L.PanelWidth - 80f, L.NoteHeightLine), new Vector2(.5f, 1f),
                             new Vector2(0f, -L.NoteY), 2f, 2f),
                20);

            float pitch = L.CardWidth + L.CardGap;
            float left = -(cards - 1) * pitch * .5f;

            for (int i = 0; i < cards; i++)
                Card(Hand[i], i, new Vector2(left + i * pitch, -L.CardsCentre), L);

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
            var name = UIKit.Titled("Name", t, Loc.Get(card.NameKey), L.NameSize, Pal.Cream,
                                    TextAnchor.MiddleCenter,
                                    new Vector2(L.CardWidth - L.Inset * 2f, L.NameHeight),
                                    new Vector2(.5f, .5f), new Vector2(0f, top - L.NameDown), 3f, 3f,
                                    wrap: true);
            UIKit.Shrinkable(name, L.NameFloor);
            name.raycastTarget = false;

            var note = UIKit.Titled("Note", t, Loc.Get(card.NoteKey), L.NoteSize,
                                    Pal.A(Pal.Cream, .86f), TextAnchor.UpperCenter,
                                    new Vector2(L.CardWidth - L.Inset * 2f, L.NoteHeight),
                                    new Vector2(.5f, .5f), new Vector2(0f, top - L.NoteDown), 2f, 0f,
                                    wrap: true);
            UIKit.Shrinkable(note, L.NoteFloor);
            note.raycastTarget = false;

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
