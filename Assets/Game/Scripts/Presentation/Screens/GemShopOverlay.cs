using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The gem shelf, brought to wherever the player is standing.
    ///
    /// <para>
    /// <b>It exists because one screen in the game cannot be left.</b> Everywhere else a short
    /// gem balance opens the shop, which is right: that is the moment somebody has decided
    /// they want something, and it is the best moment in the game to show them how to get it.
    /// Over a run it is the worst — the board behind the panel is frozen at its fail state
    /// with its heart uncharged, and navigating away forfeits it. So a player who tapped "get
    /// gems" to <em>save</em> their run would lose it on the way to paying for it, which is
    /// the shape of mistake that gets refunds asked for.
    /// </para>
    /// <para>
    /// <b>Two panels raise it, and it belongs to neither.</b> <see cref="ContinueOverlay"/>
    /// sells the run where it stands; <c>DefeatOverlay</c> sells the heart that pays for a
    /// fresh one. Both need the same shelf brought to the same frozen screen, and the second
    /// caller is what turned a private detail of the first into a thing: nothing here knows
    /// what the gems are <em>for</em>, which is exactly what makes a third caller free. What a
    /// caller supplies is one callback — <see cref="Bought"/>, raised only when gems have
    /// actually been granted — and what it gets back is a panel that steps out from under the
    /// receipt so its own offer is standing again with the price now affordable.
    /// </para>
    /// <para>
    /// <b>It is a top-up, not a second shop.</b> No tabs, no supplies, no restore line, no
    /// bundles shelf — one list of everything that grants gems, ordered by size, and a way
    /// back. Those omissions are the design: this panel exists to answer one question that was
    /// asked one screen ago, and every control that answers a different one is a way to lose
    /// the thread. <c>ShopScreen</c> remains the place to browse.
    /// </para>
    /// <para>
    /// <b>It closes itself when the gems land.</b> A purchase is reported by
    /// <c>StoreService.Granted</c> — after the server has granted, never when the payment
    /// sheet closes — and <c>Boot</c> raises the receipt panel over the top wherever the
    /// player happens to be. This one steps out from under it, so dismissing the receipt
    /// leaves the offer that started all of this standing with its price now affordable. That
    /// is the whole flow: tap, pay, receipt, one tap to continue.
    /// </para>
    /// <para>
    /// <b>The list virtualises</b> (<see cref="GridView"/>) for the reason every list here
    /// does — the shop catalog is content and grows every drop, and a panel that built a
    /// subtree per product would cost the catalog's size on a screen showing four of them.
    /// </para>
    /// </summary>
    public sealed class GemShopOverlay : ModalView
    {
        /// <summary>
        /// Raised when gems have actually been granted, so whoever opened this can repaint.
        ///
        /// Not raised on a dismissal: a caller that wants to know the panel went away has
        /// <c>OnDestroy</c>, and every caller so far only cares about the balance, which it can
        /// read for itself.
        /// </summary>
        public Action Bought;

        // ------------------------------------------------------------------ geometry
        const float PanelW = 900f;
        const float ContentW = 720f;
        const float HeadRoom = 150f;
        const float NoteH = 92f;
        const float ListH = 780f;
        const float BackH = 104f;
        const float FootRoom = 40f;

        const float CellW = 400f, CellH = 384f;

        /// <summary>Corner radius of a card's plate, in the pixels Art.Round is cut at.</summary>
        const int CellRadius = 26;
        const int Columns = 2;

        readonly List<StoreProduct> _products = new List<StoreProduct>();

        GridView _grid;
        Text _note;

        protected override void Build()
        {
            float y = HeadRoom;
            float noteY = y;                y += NoteH + 6f;
            float listY = y;                y += ListH + 14f;
            float backY = y + BackH * .5f;  y += BackH + FootRoom;

            // Dismissable by the scrim, unlike the offer underneath it. Backing out of a shop
            // costs nothing and returns the player to the panel they came from, which is
            // exactly what a stray tap should do here.
            MakePanel(new Vector2(PanelW, y), Loc.Get("ui.gems.title"));

            _note = UIKit.Shrinkable(
                UIKit.Titled("Note", Panel, string.Empty, 28, new Color(.36f, .25f, .18f),
                             TextAnchor.UpperCenter, new Vector2(ContentW, NoteH),
                             new Vector2(.5f, 1f), new Vector2(0f, -noteY),
                             outline: 0f, shadow: 0f, wrap: true), 18);

            BuildList(listY);

            // Skins.Alternate rather than Skins.Resting: grey means "not a control right
            // now", and the way out of a shop is very much a control. Same correction the
            // cancel keys took when they stopped reading as broken.
            UIKit.TextButton("Back", Panel, Skins.Alternate, Loc.Get("ui.common.back"), 32,
                             new Vector2(420f, BackH), new Vector2(.5f, 1f),
                             new Vector2(0f, -backY), () => Close());

            StoreService.Granted += OnGranted;
            StoreService.Changed += Repaint;
            StoreService.Failed += OnFailed;

            // The store may not have connected yet — the splash starts it, and a player can
            // reach a fail state before it answers. Asking again costs nothing when it has.
            StoreService.BeginConnect();

            Reload();
        }

        void OnDestroy()
        {
            StoreService.Granted -= OnGranted;
            StoreService.Changed -= Repaint;
            StoreService.Failed -= OnFailed;
        }

        void BuildList(float top)
        {
            var viewport = UIKit.Node("Viewport", Panel);
            viewport.anchorMin = new Vector2(.5f, 1f);
            viewport.anchorMax = new Vector2(.5f, 1f);
            viewport.pivot = new Vector2(.5f, 1f);
            viewport.sizeDelta = new Vector2(PanelW - 60f, ListH);
            viewport.anchoredPosition = new Vector2(0f, -top);

            _grid = GridView.Attach(viewport, Columns, CellW, CellH,
                                    parent => new GemCell(this, parent),
                                    padTop: 8f, padBottom: 16f);
        }

        // ------------------------------------------------------------------ the list
        /// <summary>
        /// Everything on the catalog that grants gems, cheapest first.
        ///
        /// <para>
        /// <b>Every product that grants gems, not the gem shelf.</b> A bundle is a perfectly
        /// good answer to "I need gems", and this list has to agree exactly with
        /// <c>StoreCatalog.HasGems</c> — which is what decided the offer one panel back would
        /// have a way to be met. A shelf filter here and a grant test there is two answers to
        /// one question, and the day they disagree is the day somebody is shown a "get gems"
        /// button that opens an empty panel.
        /// </para>
        /// <para>
        /// Cheapest first, by tier rather than by the file's order, for <c>ShopScreen</c>'s
        /// reason: the container a card's picture is drawn from is derived from the price, so
        /// an authored order that disagreed would stand a gold chest above a pouch.
        /// </para>
        /// <para>
        /// A product the store has never heard of is left out of the list entirely rather than
        /// added and hidden, or the grid draws a hole that still answers taps.
        /// </para>
        /// </summary>
        void Gather()
        {
            _products.Clear();

            foreach (var product in StoreRules.Catalog.Products)
            {
                if (product == null || product.Gems <= 0L) continue;
                if (StoreService.OfferFor(product).State == StoreOfferState.Missing) continue;
                _products.Add(product);
            }

            _products.Sort((a, b) => a.Tier.CompareTo(b.Tier));
        }

        /// <summary>The first paint: a new list, and the one time the rows are allowed an entrance.</summary>
        void Reload()
        {
            Gather();
            PaintNote();
            if (_grid != null) _grid.Show(_products.Count);
        }

        /// <summary>
        /// Redraws what is on screen: same cells, same place, no entrance.
        ///
        /// The house rule <c>GridView</c> exists to make cheap — <c>Show</c> animates and
        /// <c>Refresh</c> does not, and anything raised by an event is a redraw. Prices
        /// arriving from the store is exactly such an event, and it lands a second or two
        /// after this panel opens.
        /// </summary>
        void Repaint()
        {
            if (!this) return;

            // A store that has just answered turns Missing products into real ones, so the
            // *list* can change and not only the cells. Either way the rows get no entrance:
            // Show(animate: false) is the re-measuring form of Refresh, which is the
            // distinction ShopScreen draws and the reason the shelf stopped flickering every
            // time a price arrived.
            int before = _products.Count;
            Gather();
            PaintNote();

            if (_grid == null) return;

            if (_products.Count != before) _grid.Show(_products.Count, animate: false);
            else _grid.Refresh();
        }

        /// <summary>
        /// The one line this panel can say about itself, and each state is a different
        /// sentence.
        ///
        /// A shop that cannot reach the store and one that is still loading look identical
        /// from an empty list, and only one of them is worth waiting for — <c>ShopScreen</c>'s
        /// summary line, cut to the states a run's fail state can actually meet.
        /// </summary>
        void PaintNote()
        {
            if (!_note) return;

            if (_products.Count > 0)
            {
                _note.text = Loc.Format("ui.gems.note", Compact.Number(Profile.Gems));
                _note.color = new Color(.36f, .25f, .18f);
                return;
            }

            switch (StoreService.Status)
            {
                case StoreStatus.Connecting:
                    _note.text = Loc.Get("ui.shop.connecting");
                    _note.color = new Color(.20f, .36f, .44f);
                    break;

                case StoreStatus.Offline:
                    _note.text = Loc.Get("ui.shop.offline");
                    _note.color = new Color(.52f, .34f, .16f);
                    break;

                default:
                    _note.text = Loc.Get("ui.shop.unavailable");
                    _note.color = new Color(.52f, .34f, .16f);
                    break;
            }
        }

        // ------------------------------------------------------------------ outcomes
        /// <summary>
        /// The gems landed. Steps out from under the receipt and tells whoever opened this.
        ///
        /// <para>
        /// <b>The order is the point.</b> <c>Boot</c> raises the receipt panel from this same
        /// event, and modals stack in the order they are added — so the receipt is already
        /// above this one by the time this runs. Closing quietly leaves it drawn over the
        /// panel that raised this one, and dismissing it lands the player back exactly where
        /// they were, one tap from carrying on.
        /// </para>
        /// <para>
        /// Quiet, because the receipt's own chime is already playing: a backing-out whoosh
        /// underneath a celebration is one sound too many, which is the case
        /// <c>ModalView.Close</c>'s quiet flag was written for.
        /// </para>
        /// </summary>
        void OnGranted(StoreGrant grant)
        {
            if (!grant.IsValid || grant.Gems <= 0L) { Repaint(); return; }

            var bought = Bought;
            Bought = null;

            Close(() =>
            {
                try { bought?.Invoke(); }
                catch (Exception e) { Debug.LogException(e); }
            }, quiet: true);
        }

        /// <summary>
        /// A purchase attempt ended without a transaction.
        ///
        /// A toast rather than a panel, and cancelling says nothing at all — <c>ShopScreen</c>'s
        /// judgement, and it matters more here: this panel is already the second modal on a
        /// frozen run, and a third one apologising for a dismissed payment sheet is how a
        /// player loses track of what they were doing.
        /// </summary>
        void OnFailed(string productId, StoreFailure failure, string message)
        {
            Repaint();

            if (failure == StoreFailure.Cancelled) return;

            var (key, tint) = StoreWording.Failure(failure);
            Scenery.Toast(Content, Loc.Get(key), tint, 2.6f);
        }

        void Tap(StoreProduct product)
        {
            StoreTap.Buy(this, product);
            Repaint();
        }

        /// <summary>
        /// The hardware key closes this panel, and it is <b>not</b> optional furniture.
        ///
        /// <para>
        /// <c>View.OnBack</c> returns false by default and <c>Flow.HandleBack</c> walks the
        /// modal stack downwards until something says it dealt with the key — so a panel that
        /// does not answer hands the press to whatever is <em>underneath</em> it. Under this one
        /// is the continue offer, whose back key declines and ends the run. Without this line a
        /// player who pressed back to leave the gem list would lose the glade they opened it to
        /// save, and the panel they were looking at would still be on screen.
        /// </para>
        /// </summary>
        public override bool OnBack()
        {
            Close();
            return true;
        }

        // ------------------------------------------------------------------ one card
        /// <summary>
        /// One gem pack, drawn as a top-up rather than as a shelf card.
        ///
        /// <para>
        /// The same <see cref="ProductCard"/> the browse screen draws, at a smaller size and
        /// undecorated. That is the whole difference and it is a <em>parameter</em> rather than
        /// a second implementation: the seat, the rays, the ribbon and the badge all answer
        /// "which of these should you be looking at", which is a question for somebody choosing
        /// between shelves. A player who opened this list needs a specific number of gems and
        /// has already chosen, so every one of them would be noise.
        /// </para>
        /// <para>
        /// What is left here is the two things a card cannot know: which row it is, and what a
        /// tap does. On this screen a tap must never navigate.
        /// </para>
        /// </summary>
        sealed class GemCell : IGridCell
        {
            readonly GemShopOverlay _panel;
            readonly ProductCard _card;

            StoreProduct _product;

            public RectTransform Root => _card.Root;

            public GemCell(GemShopOverlay panel, RectTransform parent)
            {
                _panel = panel;
                _card = new ProductCard(parent,
                                        new ProductCard.Look(CellW, CellH, CellRadius,
                                                             decorated: false),
                                        () => _panel.Tap(_product));
            }

            public void Bind(int index)
            {
                _product = index >= 0 && index < _panel._products.Count
                    ? _panel._products[index] : null;

                if (_product == null) { _card.Hide(); return; }

                // Never featured: nothing on this list is being pointed at. See the remarks.
                _card.Draw(_product, StoreService.OfferFor(_product), featured: false);
            }
        }
    }
}
