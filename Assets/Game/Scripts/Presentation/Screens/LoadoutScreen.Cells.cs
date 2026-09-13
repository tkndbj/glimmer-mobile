using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the loadout's two shelves draw, and the one transaction each of them can do.
    ///
    /// <para>
    /// <b>The offer is asked of the ledger and never worked out here</b> — <c>WardLedger.OfferFor</c>
    /// asks the keeper gate before the price (invariant 15a's ordering), so a player both a rung
    /// short and out of credits is told about the wall money cannot climb rather than being sold a
    /// video that could not have bought it.
    /// </para>
    /// </summary>
    public sealed partial class LoadoutScreen
    {
        /// <summary>Repaints the line and the shelf without rebuilding either.</summary>
        void Paint()
        {
            if (this == null || _grid == null) return;

            PaintLine();
            PaintShelf();
        }

        void PaintLine()
        {
            var line = WardLoadout.Line;

            // **The line grows over a chapter, and a seat the player has not reached is shut.**
            // Thornwatch's opening rungs stand three wards, so the fourth is a seat nothing has
            // ever stood in - see `WardSeats`, and note that the board still stands whatever the
            // *level* says: this is about what may be arranged, never about what fights.
            _cleared = WardSeats.ClearedIn(GameContent.Index, GameMode.Siege,
                                           PlayerProgress.IsCleared);

            // A shut seat must never be the one being filled: a player who cleared a chapter,
            // arranged the fourth seat and then had their save replaced by an older one would
            // otherwise be left on a grid they cannot buy from.
            if (!WardSeats.IsOpen(_slot, _cleared)) _slot = 0;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var model = line.At(slot.Colour);
                bool open = WardSeats.IsOpen(slot.Colour, _cleared);
                bool picked = open && _shelf == Shelf.Wards && _slot == slot.Colour;

                var accent = Pal.EnergyColour(1 << slot.Colour);

                slot.Seat.sprite = Art.S("Ui/" + (picked ? Skins.PlateOrange : Skins.PlateBlue));
                slot.Edge.enabled = picked;
                slot.Glow.color = Pal.A(accent, picked ? .52f : .34f);
                // **The turret in the colour it stands on, which is the picture of the line.**
                // These four boxes are what a player takes into a run, and they used to draw the
                // same uncoloured thumbnail the catalogue does with the colour carried by the glow
                // behind at a third of an alpha - so the one screen that is supposed to show the
                // line never showed it, and the four turrets met on the hill were four objects the
                // player had never seen. The glow stays and stops being the only thing saying it.
                slot.Icon.sprite = AssetLibrary.Sprite(AssetManifest.WardArt(model, slot.Colour));
                // **One word, because the caption is as wide as a seat and no wider.** It read
                // "LOCKED - 3 LEVELS" and overflowed its box, which a Unity `Text` does not clip
                // (37n) - so the count printed over the seats either side of it. How many levels
                // are owed is said at full length on the panel a tap opens
                // (`ui.loadout.seat_shut`), which is where a player is asking the question; what
                // this line owes is *that it is shut*, and that fits.
                slot.Name.text = open ? Loc.Get(model.NameKey) : Loc.Get("ui.loadout.seat_at");

                // Dimmed rather than hidden, for the reason the padlock is drawn over the seat:
                // the turret standing there is real and is fighting for them.
                slot.Icon.color = open ? Color.white : new Color(1f, 1f, 1f, .34f);
                slot.Lock.enabled = !open;

                if (!open) slot.Glow.color = Pal.A(accent, .14f);
            }
        }

        void PaintShelf()
        {
            _shelfIcons.Clear();

            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.transform.SetParent(null, false);
                Destroy(old);
            }

            if (_shelf == Shelf.Wards) PaintWards();
            else PaintItems();

            // Spent on the way in, so every repaint after it is a redraw rather than an arrival.
            _shown = true;
        }

        // ----------------------------------------------------------------- turrets
        void PaintWards()
        {
            var models = WardLedger.Catalog.Models;
            int level = PlayerProgression.Level.Level;
            var line = WardLoadout.Line;

            const float gapX = CellGapX, gapY = CellGapY;
            float span = Columns * CellW + (Columns - 1) * gapX;
            float left = -span * .5f + CellW * .5f;

            // **Laid out by a cursor rather than by index arithmetic**, because a band breaks the
            // "cell i sits at row i/4" rule the moment it exists: a tier starts a fresh row under
            // a header of its own, so where the next cell goes depends on what came before it
            // rather than on how many. Written as `i % Columns` with a header spliced in, the two
            // would disagree the first time a band held a number that does not divide by four -
            // which is every band on this shelf.
            float y = -gapY;
            int tier = 0, column = 0;

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                int band = WardTier.Of(model);

                if (band != tier)
                {
                    if (tier != 0)
                    {
                        if (column != 0) y -= CellH + gapY;      // close the part-full row
                        y -= TierGap;
                    }

                    TierBadge(band, span, y - TierH * .5f);
                    y -= TierH;

                    tier = band;
                    column = 0;
                }

                // **Per seat**, which is the whole of the per-colour rule reaching the shelf: the
                // same turret is held on red and for sale on blue, so this grid is a different
                // grid on every slot and tapping a seat repaints it (`Choose`).
                var offer = WardLedger.OfferFor(model, _slot, level);

                float x = left + column * (CellW + gapX);
                bool standing = line.At(_slot) == model;

                WardCell(model, offer, standing, new Vector2(x, y - CellH * .5f), i);

                if (++column < Columns) continue;

                column = 0;
                y -= CellH + gapY;
            }

            if (column != 0) y -= CellH + gapY;

            _grid.sizeDelta = new Vector2(0f, -y + gapY);
        }

        /// <summary>How tall a band's header is, and the air above it.</summary>
        const float TierH = 64f, TierGap = 26f;

        /// <summary>
        /// The header over one band: its name, with a rule running out to either side.
        ///
        /// <para>
        /// <b>A label between two rules rather than a plate</b>, because this is punctuation and
        /// not a control. A filled bar the width of the grid would read as another row of the
        /// shelf — one a player could try to tap — where a caption on a line reads as a heading
        /// the way it does in every list they have ever scrolled.
        /// </para>
        /// <para>
        /// It carries no state and nothing keys on it: a band is <c>WardTier</c>, which is a label
        /// on the order the shelf already had. What opens a rung is still the rung below it.
        /// </para>
        /// </summary>
        void TierBadge(int tier, float span, float midY)
        {
            var row = UIKit.Box("Tier" + tier, _grid, new Vector2(span, TierH),
                                new Vector2(.5f, 1f), new Vector2(0f, midY));

            var name = UIKit.Label("Name", row, Loc.Get(WardTier.NameKey(tier)), 30,
                                   Pal.A(Pal.Cream, .92f), TextAnchor.MiddleCenter,
                                   new Vector2(240f, TierH), new Vector2(.5f, .5f), Vector2.zero,
                                   FontStyle.Bold);
            name.raycastTarget = false;

            // The rules stop short of the caption on both sides, so the line never runs under the
            // letters however wide the grid is drawn.
            float reach = (span - 280f) * .5f;

            for (int side = -1; side <= 1; side += 2)
            {
                var rule = UIKit.Img("Rule", row, Art.Round(2), Pal.A(Pal.Cream, .22f),
                                     new Vector2(reach, 3f), new Vector2(.5f, .5f),
                                     new Vector2(side * (140f + reach * .5f), 0f));
                rule.raycastTarget = false;
            }
        }

        void WardCell(WardModel model, WardOffer offer, bool standing, Vector2 at, int index)
        {
            bool held = offer.State == WardPurchaseState.AlreadyHeld;

            var cell = UIKit.Box("Ward_" + model.Id, _grid, new Vector2(CellW, CellH),
                                 new Vector2(.5f, 1f), at);

            var hit = cell.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var plate = UIKit.Img("Plate", cell,
                                  Art.S("Ui/" + (standing ? Skins.PlateOrange : Skins.PlateBlue)),
                                  Color.white);
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);

            if (standing)
            {
                var edge = UIKit.Img("Edge", cell, Art.RoundOutline(CellRadius, 6f), Skins.PlateEdge);
                UIKit.StretchTo((RectTransform)edge.transform, -2, -2, -2, -2);
            }

            // The picture. **The real turret, worn in the colour of the seat being filled** —
            // so tapping a different slot turns the whole shelf that colour, which is the mode's
            // central rule shown in one gesture rather than explained: a turret has no colour of
            // its own, and the colour is the seat it is put in. It answers the question somebody
            // browsing is actually asking, which is not "what is this turret" but "what will this
            // look like on red".
            //
            // **Not the invariant-16c relaxation it looks like.** That rule browses thumbnails
            // because a grove cell draws at ~170 points against art cut at 512; a turret is cut at
            // 192x240, *smaller* than the 176-square thumbnail it replaces, so the true picture
            // and the cheap one cost the same. All eighty are in the scope, so a slot tap repaints
            // rather than loading (`AssetManifest.WardShelfAssets`).
            // **Placed by its centre, and it is worth saying why the old numbers looked
            // plausible.** `UIKit.Box` pivots at the middle whatever it is anchored to, so a
            // 179-tall picture anchored to the cell's top edge at -22 had 68 units of itself
            // above the cell — the turret was drawn high, clipped by nothing (uGUI does not
            // clip), and the gap it left below read as a picture that had slipped up. Every
            // number under here is now `margin + half the box`.
            // Full colour whichever it is: the veil below does the dimming, and a tint on the
            // picture only ever *darkens* it — which on a shelf whose whole job is telling twenty
            // silhouettes apart is the one thing not to do (the grove shop's own lesson).
            var icon = UIKit.Img("Turret", cell, AssetLibrary.Sprite(AssetManifest.WardArt(model, _slot)),
                                 Color.white, new Vector2(IconBox, IconBox),
                                 new Vector2(.5f, 1f), new Vector2(0f, -(IconTop + IconBox * .5f)));
            icon.preserveAspect = true;

            // Held so the scope arriving can dress this cell rather than rebuild it (`Dress`).
            _shelfIcons[model.Id] = icon;

            if (!held)
            {
                // **The veil, and where it sits in the order is the whole of it.** uGUI paints in
                // sibling order, so this goes after the picture and before the padlock — and the
                // name, the note and the footer are all built after it, so the price stays at
                // full strength. It covers the plate rather than the picture alone, which is what
                // says the *cell* is not yours rather than that its turret is faded.
                var veil = UIKit.Img("Veil", cell, Art.Round(CellRadius),
                                     new Color(0f, 0f, 0f, .48f));
                UIKit.StretchTo((RectTransform)veil.transform, 0, 0, 0, 0);
                veil.raycastTarget = false;

                // Over the turret rather than beside it, and drawn at white — the same lock on
                // the same decision the profile and the companion shelf make. `held` rather
                // than a narrower state on purpose: what the padlock says is "this one is not
                // yours", which is true of a turret you could buy this second as much as of one
                // behind a keeper level. Which of the two it is, the footer says in words.
                //
                // Built after the picture, so it draws over it: uGUI paints in sibling order.
                var padlock = UIKit.Img("Lock", cell, Art.S("Ui/ic_padlock"), Color.white,
                                        new Vector2(IconBox * .62f, IconBox * .62f),
                                        new Vector2(.5f, 1f), new Vector2(0f, -(IconTop + IconBox * .5f)));
                padlock.preserveAspect = true;
                padlock.raycastTarget = false;
            }

            // The name and the price, and nothing else - see `CellH`.
            var name = UIKit.Titled("Name", cell, Loc.Get(model.NameKey), 26, Pal.Cream,
                                    TextAnchor.MiddleCenter, new Vector2(CellW - 24f, 36f),
                                    new Vector2(.5f, 1f), new Vector2(0f, -NameY),
                                    0f, 2f);
            UIKit.Shrinkable(name, 16);

            // **Only on a turret this seat actually holds.** A ladder over a card that is still
            // for sale would read as a promise about what buying it gives you; what a player owns
            // is what has a place on the ladder at all, and an unheld card's own strip already
            // says what it costs.
            if (held)
                WardStarRow.Build(cell, new Vector2(0f, -StarsY),
                                  WardStarLedger.StarsOf(model, WardLine.Colours[_slot]));

            Footer(cell, model, offer, standing);

            cell.gameObject.AddComponent<Btn>().Setup(() => Tap(model));

            // **Only on the way in.** A repaint - art arriving, a purchase landing, a slot being
            // tapped - must not replay the entrance, or the screen reads as reloading itself
            // (invariant 16d).
            if (!_shown) Tween.Pop(cell, Mathf.Min(index, 9) * .022f, .3f);
        }

        /// <summary>
        /// The one line at the foot of a cell: what it costs, or what it asks for.
        ///
        /// <b>A turret already held draws no strip at all.</b> It used to say STAND or ON THE
        /// LINE, which is a caption on twenty cells telling nineteen of them something the plate
        /// already says: the one standing on this colour wears the lit plate and the gold rim, and
        /// the rest are simply not it.
        /// </summary>
        void Footer(RectTransform cell, WardModel model, WardOffer offer, bool standing)
        {
            string text;
            Color ink;
            bool coin = false, gems = false;

            switch (offer.State)
            {
                case WardPurchaseState.AlreadyHeld:
                    return;

                case WardPurchaseState.Sealed:
                    // **One word, and the same word the panel behind this cell uses.** It read
                    // "After Lighthouse" — the rung named, on the argument that it is the only
                    // one of the three refusals that names something to do. What that produced
                    // on a shelf is a row of cells each naming a different turret, so the line a
                    // player scans reads as twenty unrelated conditions rather than as one state
                    // repeated; and the name is the *other* turret's, which is the one thing on
                    // the cell not about the turret it is drawn on.
                    //
                    // What is lost is which turret unlocks it, and that is one tap away and
                    // fuller: `ui.loadout.sealed_note` says "Buy {0} on this colour first" on the
                    // panel, where there is room for a sentence. Same key as that panel's own
                    // button, so the shelf and the thing it opens cannot come to disagree about
                    // what this state is called.
                    text = Loc.Get("ui.loadout.sealed");
                    ink = Pal.A(Pal.Cream, .55f);
                    break;

                case WardPurchaseState.LevelLocked:
                    text = Loc.Format("ui.loadout.level", offer.RequiredLevel);
                    ink = Pal.A(Pal.Cream, .55f);
                    break;

                case WardPurchaseState.NotForSale:
                    text = Loc.Get("ui.loadout.locked");
                    ink = Pal.A(Pal.Cream, .45f);
                    break;

                default:
                    // **`coin` is "there is a price here", `gems` is "and it is in gems".** It
                    // was only ever set from `gems`, so a turret priced in credits drew its
                    // number with no glyph beside it — half the roster, and the half whose
                    // currency a player cannot guess from the number.
                    text = offer.Cost.ToString("N0");
                    ink = offer.State == WardPurchaseState.Ready
                        ? Pal.Cream : Pal.A(Pal.Cream, .55f);
                    coin = true;
                    gems = offer.Currency == Currency.Gems;
                    break;
            }

            // Bigger than it was, which is what the description's height paid for: the price is
            // the number this shelf is about and it was set at the size of a caption.
            var box = UIKit.Box("Foot", cell, new Vector2(CellW - 20f, 56f),
                                new Vector2(.5f, 0f), new Vector2(0f, FootY));

            var seat = UIKit.Img("Seat", box, Art.Round(18), new Color(0f, 0f, 0f, .30f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            float shift = coin ? 18f : 0f;

            var label = UIKit.Titled("T", box, text, 32, ink, TextAnchor.MiddleCenter,
                                     new Vector2(CellW - 66f, 48f), new Vector2(.5f, .5f),
                                     new Vector2(shift, 0f), 0f, 2f);

            // **Down to 14 rather than 20, because this strip now sometimes holds a sentence.** A
            // price is four characters and a sealed rung is "After Lighthouse"; a `UIKit.Label`
            // that overflows is not clipped by anything (invariant 37n), so the floor is what
            // stops it being drawn over the plate's own edge.
            UIKit.Shrinkable(label, 14);

            if (!coin) return;

            // The gem's own icon, or the hub's spinning coin — which is a reel rather than a
            // sprite, so it is attached rather than named (`ShopScreen.BalancePill`'s idiom: the
            // pile on a card is made of this coin, so a price and a purse read as one currency).
            var glyph = UIKit.Img("Coin", box, gems ? Art.S("Ui/ic_gem") : null, Color.white,
                                  new Vector2(34f, 34f), new Vector2(.5f, .5f),
                                  new Vector2(-label.preferredWidth * .5f - 19f, 0f));
            glyph.preserveAspect = true;

            if (!gems) Flipbook.Attach(glyph, "Ui/Coin", 11f);
        }

        /// <summary>
        /// One tap on a turret, held or not: show it.
        ///
        /// <para>
        /// <b>Standing used to be the plain tap and is now one tap deeper, which is a real cost
        /// paid for a real reason.</b> A held turret went straight onto the line and an unheld one
        /// opened a price — so the only turrets a player could ever *see* firing were the ones they
        /// had already bought, and the decision this shelf exists to ask them to make was being
        /// made from a thumbnail. <c>WardPreviewOverlay</c> answers both, and standing is still one
        /// tap from inside it.
        /// </para>
        /// <para>
        /// <b>The seat's colour travels with it</b>, because the panel is raised by tapping a cell
        /// that is already wearing one: a grey turret one tap deeper is the very inconsistency the
        /// shelf stopped having.
        /// </para>
        /// </summary>
        void Tap(WardModel model)
        {
            Flow.Modal<WardPreviewOverlay>(v =>
            {
                v.Model = model;
                v.Colour = _slot;
                v.Changed = Paint;
            });
        }

        // ----------------------------------------------------------------- items
        void PaintItems()
        {
            var items = UtilityLedger.Catalog.Items;
            int level = PlayerProgression.Level.Level;

            const float gapX = CellGapX, gapY = CellGapY;
            float span = Columns * CellW + (Columns - 1) * gapX;
            float left = -span * .5f + CellW * .5f;

            int rows = (items.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * (CellH + gapY) + gapY);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                float x = left + (i % Columns) * (CellW + gapX);
                float y = -gapY - (i / Columns) * (CellH + gapY) - CellH * .5f;

                ItemCell(item, level, new Vector2(x, y), i);
            }
        }

        void ItemCell(UtilityItem item, int level, Vector2 at, int index)
        {
            bool open = item.ReachedBy(level);
            int held = UtilityLedger.Held(item.Id);

            var cell = UIKit.Box("Item_" + item.Id, _grid, new Vector2(CellW, CellH),
                                 new Vector2(.5f, 1f), at);

            var hit = cell.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var plate = UIKit.Img("Plate", cell, Art.S("Ui/" + Skins.PlateBlue), Color.white);
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);

            // The ward cell's numbers, for the ward cell's reason - the two grids share a cell
            // size, so anything that is not the same here is a difference nobody chose. They are
            // constants on the screen now rather than a pair of matching literals, which is what
            // let four columns be one edit instead of two that could disagree.
            //
            // A name and a price and nothing else, which is the turret shelf's rule arrived at
            // here too: what a utility *does* is a sentence, and it is drawn in full on the panel
            // this cell opens - see `CellH`.
            var icon = UIKit.Img("Icon", cell, Art.S(item.Art),
                                 open ? Color.white : new Color(.62f, .66f, .72f, 1f),
                                 new Vector2(IconBox, IconBox),
                                 new Vector2(.5f, 1f), new Vector2(0f, -(IconTop + IconBox * .5f)));
            icon.preserveAspect = true;

            var name = UIKit.Titled("Name", cell, Loc.Get(item.NameKey), 25,
                                    open ? Pal.Cream : Pal.A(Pal.Cream, .82f),
                                    TextAnchor.MiddleCenter, new Vector2(CellW - 24f, 34f),
                                    new Vector2(.5f, 1f), new Vector2(0f, -NameY),
                                    0f, 2f);
            UIKit.Shrinkable(name, 16);

            // How many are in hand, top-right, where the bar's own badge is — so the two readouts
            // of one number are in the same corner of the same shape (invariant 39d's rule about
            // where a count goes).
            if (held > 0)
            {
                var badge = UIKit.Img("Badge", cell, Art.Round(999),
                                      new Color(.10f, .16f, .12f, .96f),
                                      new Vector2(56f, 40f), new Vector2(1f, 1f),
                                      new Vector2(-12f, -12f));

                var count = UIKit.Titled("N", badge.transform, held.ToString(), 23, Pal.Cream,
                                         TextAnchor.MiddleCenter, new Vector2(52f, 36f),
                                         new Vector2(.5f, .5f), Vector2.zero, 0f, 2f);
                UIKit.Shrinkable(count, 14);
            }

            string text = !open ? Loc.Format("ui.loadout.level", item.MinLevel)
                        : item.ForSale ? item.GemPrice.ToString("N0")
                        : Loc.Get("ui.loadout.chest_only");

            var box = UIKit.Box("Foot", cell, new Vector2(CellW - 24f, 42f),
                                new Vector2(.5f, 0f), new Vector2(0f, FootY));

            var seat = UIKit.Img("Seat", box, Art.Round(16), new Color(0f, 0f, 0f, .30f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            bool priced = open && item.ForSale;

            var label = UIKit.Titled("T", box, text, 23,
                                     open ? Pal.Cream : Pal.A(Pal.Cream, .55f),
                                     TextAnchor.MiddleCenter, new Vector2(CellW - 62f, 36f),
                                     new Vector2(.5f, .5f), new Vector2(priced ? 14f : 0f, 0f),
                                     0f, 2f);
            UIKit.Shrinkable(label, 14);

            if (priced)
                UIKit.Img("Gem", box, Art.S("Ui/ic_gem"), Color.white, new Vector2(26f, 26f),
                          new Vector2(.5f, .5f), new Vector2(-label.preferredWidth * .5f - 15f, 0f));

            cell.gameObject.AddComponent<Btn>().Setup(() =>
            {
                if (!open || !item.ForSale) { Audio.Sfx("blocked", .4f); return; }
                Flow.Modal<UtilityBuyOverlay>(v => { v.Item = item; v.Bought = Paint; });
            });

            Tween.Pop(cell, Mathf.Min(index, 9) * .022f, .3f);
        }
    }
}
