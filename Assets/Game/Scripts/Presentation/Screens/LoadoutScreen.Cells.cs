using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
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
            PaintSummary();
        }

        void PaintSummary()
        {
            if (_summary == null) return;

            _summary.text = _shelf == Shelf.Wards
                ? Loc.Format("ui.loadout.held", WardLedger.HeldCount,
                             WardLedger.Catalog.Count)
                : Loc.Get("ui.loadout.items_note");
        }

        void PaintLine()
        {
            var line = WardLoadout.Line;

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var model = line.At(slot.Colour);
                bool picked = _shelf == Shelf.Wards && _slot == slot.Colour;

                var accent = Pal.EnergyColour(1 << slot.Colour);

                slot.Seat.color = picked ? Pal.A(accent, .26f) : Pal.A(accent, .10f);
                slot.Edge.color = picked ? Pal.A(accent, .95f) : Pal.A(Color.white, .14f);
                slot.Icon.sprite = AssetLibrary.Sprite(AssetManifest.WardThumb(model.Id));
                slot.Name.text = Loc.Get(model.NameKey);
            }
        }

        void PaintShelf()
        {
            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.transform.SetParent(null, false);
                Destroy(old);
            }

            if (_shelf == Shelf.Wards) PaintWards();
            else PaintItems();
        }

        // ----------------------------------------------------------------- turrets
        void PaintWards()
        {
            var models = WardLedger.Catalog.Models;
            int level = PlayerProgression.Level.Level;
            var line = WardLoadout.Line;

            float gapX = 24f, gapY = 24f;
            float span = Columns * CellW + (Columns - 1) * gapX;
            float left = -span * .5f + CellW * .5f;

            int rows = (models.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * (CellH + gapY) + gapY);

            for (int i = 0; i < models.Count; i++)
            {
                var model = models[i];
                var offer = WardLedger.OfferFor(model, level);

                float x = left + (i % Columns) * (CellW + gapX);
                float y = -gapY - (i / Columns) * (CellH + gapY) - CellH * .5f;

                bool standing = line.At(_slot) == model;

                WardCell(model, offer, standing, new Vector2(x, y), i);
            }
        }

        void WardCell(WardModel model, WardOffer offer, bool standing, Vector2 at, int index)
        {
            bool held = offer.State == WardPurchaseState.AlreadyHeld;
            var accent = Pal.EnergyColour(1 << _slot);

            var cell = UIKit.Box("Ward_" + model.Id, _grid, new Vector2(CellW, CellH),
                                 new Vector2(.5f, 1f), at);

            var hit = cell.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var plate = UIKit.Img("Plate", cell, Art.Round(CellRadius),
                                  standing ? Pal.A(accent, .20f)
                                           : new Color(.06f, .12f, .16f, .86f));
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);

            var edge = UIKit.Img("Edge", cell, Art.RoundOutline(CellRadius, 3f),
                                 standing ? Pal.A(accent, .90f) : new Color(1f, 1f, 1f, .12f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            // The picture. **A thumbnail out of the shared UI set, never the turret itself** —
            // browsing twenty models in four colours would be eighty textures for a grid whose
            // cells draw at 150 points (invariant 16c).
            var icon = UIKit.Img("Turret", cell, AssetLibrary.Sprite(AssetManifest.WardThumb(model.Id)),
                                 held ? Color.white : new Color(.55f, .58f, .62f, .92f),
                                 new Vector2(CellW * .56f, CellW * .56f),
                                 new Vector2(.5f, 1f), new Vector2(0f, -22f));
            icon.preserveAspect = true;

            var name = UIKit.Titled("Name", cell, Loc.Get(model.NameKey), 28,
                                    held ? Pal.Cream : Pal.A(Pal.Cream, .70f),
                                    TextAnchor.MiddleCenter, new Vector2(CellW - 28f, 38f),
                                    new Vector2(.5f, 1f), new Vector2(0f, -CellW * .58f - 34f),
                                    0f, 2f);
            UIKit.Shrinkable(name, 18);

            // What it does, in one line. The only place the game says it, and it is here rather
            // than on a panel because this is where somebody is deciding.
            var note = UIKit.Label("Note", cell, Loc.Get(model.NoteKey), 21,
                                   Pal.A(Pal.Cream, .58f), TextAnchor.UpperCenter,
                                   new Vector2(CellW - 34f, 60f), new Vector2(.5f, 1f),
                                   new Vector2(0f, -CellW * .58f - 74f));
            UIKit.Shrinkable(note, 15);

            Footer(cell, model, offer, standing);

            cell.gameObject.AddComponent<Btn>().Setup(() => Tap(model, offer));

            Tween.Pop(cell, Mathf.Min(index, 9) * .022f, .3f);
        }

        /// <summary>
        /// The one line at the foot of a cell: what it costs, what it asks for, or that it is
        /// standing on the line.
        /// </summary>
        void Footer(RectTransform cell, WardModel model, WardOffer offer, bool standing)
        {
            string text;
            Color ink;
            bool coin = false, gems = false;

            switch (offer.State)
            {
                case WardPurchaseState.AlreadyHeld:
                    text = standing ? Loc.Get("ui.loadout.standing") : Loc.Get("ui.loadout.stand");
                    ink = standing ? Pal.Sun : Pal.A(Pal.Cream, .82f);
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
                    text = offer.Cost.ToString("N0");
                    ink = offer.State == WardPurchaseState.Ready
                        ? Pal.Cream : Pal.A(Pal.Cream, .55f);
                    gems = offer.Currency == Currency.Gems;
                    break;
            }

            var box = UIKit.Box("Foot", cell, new Vector2(CellW - 36f, 46f),
                                new Vector2(.5f, 0f), new Vector2(0f, 30f));

            var seat = UIKit.Img("Seat", box, Art.Round(16), new Color(0f, 0f, 0f, .30f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            coin = coin || gems;
            float shift = coin ? 16f : 0f;

            var label = UIKit.Titled("T", box, text, 26, ink, TextAnchor.MiddleCenter,
                                     new Vector2(CellW - 80f, 40f), new Vector2(.5f, .5f),
                                     new Vector2(shift, 0f), 0f, 2f);
            UIKit.Shrinkable(label, 16);

            if (!coin) return;

            // The gem's own icon, or the hub's spinning coin — which is a reel rather than a
            // sprite, so it is attached rather than named (`ShopScreen.BalancePill`'s idiom: the
            // pile on a card is made of this coin, so a price and a purse read as one currency).
            var glyph = UIKit.Img("Coin", box, gems ? Art.S("Ui/ic_gem") : null, Color.white,
                                  new Vector2(30f, 30f), new Vector2(.5f, .5f),
                                  new Vector2(-label.preferredWidth * .5f - 18f, 0f));
            glyph.preserveAspect = true;

            if (!gems) Flipbook.Attach(glyph, "Ui/Coin", 11f);
        }

        /// <summary>
        /// One tap on a turret: stand it, or offer it.
        ///
        /// <b>Standing is the common case and gets the plain tap</b>, because a player who owns
        /// six turrets is arranging far more often than buying — and a confirmation on an action
        /// that is one tap to undo is a confirmation that teaches people to tap through them
        /// (which is the argument the three real confirmations in this game are held to).
        /// </summary>
        void Tap(WardModel model, WardOffer offer)
        {
            if (offer.State == WardPurchaseState.AlreadyHeld)
            {
                char colour = WardLine.Colours[_slot];

                if (WardLoadout.Choose(colour, model.Id)) Audio.Sfx("unlock", .5f);
                else Audio.Sfx("blocked", .4f);

                Paint();
                return;
            }

            Flow.Modal<WardBuyOverlay>(v => { v.Model = model; v.Bought = Paint; });
        }

        // ----------------------------------------------------------------- items
        void PaintItems()
        {
            var items = UtilityLedger.Catalog.Items;
            int level = PlayerProgression.Level.Level;

            float gapX = 24f, gapY = 24f;
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

            var plate = UIKit.Img("Plate", cell, Art.Round(CellRadius),
                                  new Color(.06f, .12f, .16f, .86f));
            UIKit.StretchTo((RectTransform)plate.transform, 0, 0, 0, 0);

            var edge = UIKit.Img("Edge", cell, Art.RoundOutline(CellRadius, 3f),
                                 new Color(1f, 1f, 1f, .12f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var icon = UIKit.Img("Icon", cell, Art.S(item.Art),
                                 open ? Color.white : new Color(.55f, .58f, .62f, .92f),
                                 new Vector2(CellW * .5f, CellW * .5f),
                                 new Vector2(.5f, 1f), new Vector2(0f, -26f));
            icon.preserveAspect = true;

            var name = UIKit.Titled("Name", cell, Loc.Get(item.NameKey), 28,
                                    open ? Pal.Cream : Pal.A(Pal.Cream, .70f),
                                    TextAnchor.MiddleCenter, new Vector2(CellW - 28f, 38f),
                                    new Vector2(.5f, 1f), new Vector2(0f, -CellW * .54f - 36f),
                                    0f, 2f);
            UIKit.Shrinkable(name, 18);

            var note = UIKit.Label("Note", cell, Loc.Get(item.NoteKey), 21,
                                   Pal.A(Pal.Cream, .58f), TextAnchor.UpperCenter,
                                   new Vector2(CellW - 34f, 58f), new Vector2(.5f, 1f),
                                   new Vector2(0f, -CellW * .54f - 76f));
            UIKit.Shrinkable(note, 15);

            // How many are in hand, top-right, where the bar's own badge is — so the two readouts
            // of one number are in the same corner of the same shape (invariant 39d's rule about
            // where a count goes).
            if (held > 0)
            {
                var badge = UIKit.Img("Badge", cell, Art.Round(999),
                                      new Color(.10f, .16f, .12f, .96f),
                                      new Vector2(64f, 44f), new Vector2(1f, 1f),
                                      new Vector2(-16f, -16f));

                var count = UIKit.Titled("N", badge.transform, held.ToString(), 26, Pal.Cream,
                                         TextAnchor.MiddleCenter, new Vector2(60f, 40f),
                                         new Vector2(.5f, .5f), Vector2.zero, 0f, 2f);
                UIKit.Shrinkable(count, 16);
            }

            string text = !open ? Loc.Format("ui.loadout.level", item.MinLevel)
                        : item.ForSale ? item.GemPrice.ToString("N0")
                        : Loc.Get("ui.loadout.chest_only");

            var box = UIKit.Box("Foot", cell, new Vector2(CellW - 36f, 46f),
                                new Vector2(.5f, 0f), new Vector2(0f, 30f));

            var seat = UIKit.Img("Seat", box, Art.Round(16), new Color(0f, 0f, 0f, .30f));
            UIKit.StretchTo((RectTransform)seat.transform, 0, 0, 0, 0);

            bool priced = open && item.ForSale;

            var label = UIKit.Titled("T", box, text, 26,
                                     open ? Pal.Cream : Pal.A(Pal.Cream, .55f),
                                     TextAnchor.MiddleCenter, new Vector2(CellW - 80f, 40f),
                                     new Vector2(.5f, .5f), new Vector2(priced ? 16f : 0f, 0f),
                                     0f, 2f);
            UIKit.Shrinkable(label, 16);

            if (priced)
                UIKit.Img("Gem", box, Art.S("Ui/ic_gem"), Color.white, new Vector2(30f, 30f),
                          new Vector2(.5f, .5f), new Vector2(-label.preferredWidth * .5f - 18f, 0f));

            cell.gameObject.AddComponent<Btn>().Setup(() =>
            {
                if (!open || !item.ForSale) { Audio.Sfx("blocked", .4f); return; }
                Flow.Modal<UtilityBuyOverlay>(v => { v.Item = item; v.Bought = Paint; });
            });

            Tween.Pop(cell, Mathf.Min(index, 9) * .022f, .3f);
        }
    }
}
