using GlimmerGrove.AssetPipeline;
using System;
using System.Collections.Generic;
using GlimmerGrove.App;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The Grovement: a floor of tiles the player owns, buys and builds on.
    ///
    /// <para>
    /// <b>This replaced a ladder of floating islands, and the difference is not decorative.</b>
    /// An island carried hand-authored slots, each with a position, a size and a role, so the
    /// player's decision was which of eleven pre-placed dots got which sticker — every grove
    /// came out with the same composition and different stickers on it. A field of identical
    /// tiles moves the composition to the player: where a thing goes is now as much their
    /// choice as what it is. That is why the slot-kind rule went with the islands (see
    /// <c>HomesteadSlotKind</c>) — it existed to stop a sprinkle of dots looking accidental,
    /// and there are no dots.
    /// </para>
    /// <para>
    /// <b>What it costs and what it does not.</b> The save file gained one field — which
    /// regions of the floor were bought (invariant 15, a union-joined id set) — because land is
    /// paid for now rather than earned from chapters, and that is the one thing here that could
    /// not stay derived. It gained nothing else: a tile is a slot, its id is permanent, and an
    /// untouched tile writes no row, so a three-hundred-tile floor with two things on it costs
    /// two rows exactly as ten islands did. A piece two tiles wide still writes one row — its
    /// footprint is derived from the catalog (<see cref="GroveOccupancy"/>).
    /// </para>
    /// <para>
    /// <b>Two things a field needs that islands did not.</b> Depth has to be computed rather
    /// than authored, because what stands in front of what is now a consequence of where the
    /// player put things — see <c>GroveFootprint.Depth</c>. And the tiles have to be culled,
    /// because a floor is hundreds of them and a phone shows dozens; see
    /// <see cref="GroveFieldView"/>, which is <c>GridView</c>'s bargain in two dimensions.
    /// </para>
    /// </summary>
    public sealed partial class HomesteadScreen : View
    {
        public override string Track => "mus_menu";

        /// <summary>
        /// The one screen in the game that takes two fingers. See <see cref="View.WantsMultiTouch"/>
        /// for why it is declared rather than switched on, and why a board must never inherit it.
        /// </summary>
        public override bool WantsMultiTouch => true;

        public override bool OnBack()
        {
            // The back key ends the ceremony rather than the screen, which is what it means
            // everywhere else in the game: it closes the innermost thing that is open. Leaving
            // instead would cost nothing that is stored — the land is bought either way — but
            // it would answer "let me get on with it" by taking the grove away.
            if (_rise != null) { _rise.Skip(); return true; }

            Flow.Go<HomeScreen>();
            return true;
        }

        /// <summary>
        /// Ground bought a moment ago, which this screen was opened to show arriving. Null on
        /// every ordinary visit.
        ///
        /// <para>
        /// Set by <c>GroveLandOverlay</c> through <c>Flow.Go</c>, which is what makes the
        /// purchase and the ceremony one act rather than two: a player who buys land is taken
        /// back to their grove and shown it happening, instead of walking back to a floor that
        /// is simply bigger than it was. Nothing about it is stored — the purchase is already
        /// recorded and this is only a request to <em>animate</em> it, so a player who kills
        /// the app mid-ceremony finds their land exactly where they left it, undecorated.
        /// </para>
        /// </summary>
        public GroveRegion Arriving { get; set; }

        /// <summary>
        /// How many stars the grove was worth before that purchase, or -1 when nothing is
        /// arriving.
        ///
        /// <para>
        /// Carried rather than derived because it cannot be: land is bought in the shop, so by
        /// the time this screen exists the score has already moved, and the star row's own
        /// rule — celebrate what was not there a moment ago — has no "a moment ago" to compare
        /// against on a screen that has just been built. Without it the reward for the single
        /// most expensive thing in the game would be a number that had quietly changed while
        /// the player was looking somewhere else, which is exactly what drawing the score here
        /// was meant to fix.
        /// </para>
        /// </summary>
        public int ArrivingStars { get; set; } = -1;

        const float HeaderHeight = 214f;

        /// <summary>
        /// The top-left pair — the way out and the way to the boards.
        ///
        /// Declared once because they are only a pair while they agree about size and
        /// baseline: held as literals at their two call sites, a change to one is a change
        /// somebody has to remember to make twice, and the failure is a row that is subtly
        /// not a row.
        /// </summary>
        static readonly Vector2 NavSize = new Vector2(112f, 112f);
        const float NavX = 92f, NavY = -104f, NavGap = 16f;

        RectTransform _viewport;
        GroveFieldView _field;
        Text _summary;

        /// <summary>
        /// The "still fetching" readout, up from the first frame and gone the moment the floor
        /// can be drawn properly. See <see cref="Build"/> for the two waits it covers.
        /// </summary>
        BusyVeil _busy;

        /// <summary>
        /// The art this grove is standing on, held for exactly as long as the screen is.
        ///
        /// Refilled rather than reloaded whenever the floor's contents change — see
        /// <see cref="OnLayoutChanged"/>.
        /// </summary>
        AssetHold _art;

        RectTransform _shop;

        /// <summary>What the grove is worth, in the corner. Owns its own celebration rule.</summary>
        GroveScoreBox _score;

        bool _presented, _teaching, _taught;

        /// <summary>
        /// The arrival ceremony, and the ground it is still holding back.
        ///
        /// <para>
        /// Two fields rather than one because they answer at different times.
        /// <see cref="_pending"/> is set the instant this screen is built, which is what keeps
        /// the new land off the very first paint — the iris opens on the grove as it was, and
        /// nothing has to be un-drawn. <see cref="_rise"/> exists only once there is a floor to
        /// stage it against, and takes the question over from there.
        /// </para>
        /// </summary>
        GroveRegion _pending;
        GroveRise _rise;

        protected override void Build()
        {
            // Read before anything can draw. Everything below asks whether ground is being
            // withheld, and a screen that painted the new land once and took it away again
            // would spend the whole ceremony undoing its own first frame.
            _pending = Arriving;

            // The hub's own sky and nothing else from it. The grove here is the content, so
            // laying the hub's ground and decoration behind it would be two groves in one
            // picture — and this one is supposed to be the player's.
            Scenery.Cover(Content, "home_sky", .05f, .42f);
            Fireflies.Spawn(Content, 16, new Color(1f, .93f, .70f), 6f, 20f);

            BuildField();
            BuildHeader();

            // Says the screen is still fetching, and goes as soon as it is not. Built last so
            // it draws over the field and the header.
            //
            // The two waits it covers are a body read off disk and then the art for whatever
            // that body says is standing on the floor, and the second is much the larger:
            // sixty-eight of the eighty-six pieces are frame folders, so a decorated grove is
            // dozens of addresses. Before this, all of it was drawn as an empty floor — which
            // is also exactly what an *undecorated* grove looks like, so the screen was saying
            // two different things with one picture.
            _busy = BusyVeil.Attach(Safe, Loc.Get("ui.grove.loading"));

            // The catalog is a body, read on entering the feature. Both it and the art load
            // asynchronously and both repaint, because a screen is built in the frame it is
            // asked for and the first paint would otherwise be the only one.
            Warm();

            HomesteadCatalog.Changed += Reload;
            HomesteadLedger.Changed += Repaint;
            // A placement changes what this screen draws *and* what art it needs, so the
            // hold is refilled and the tiles repainted when the new piece lands. That used to
            // be a static claim made by the picker plus a global event this screen listened to;
            // it is now the screen keeping its own hold in step with its own contents.
            HomesteadLayout.Changed += OnLayoutChanged;
            // Buying land adds ground, which is a different set of tiles rather than a different
            // look on the same ones — so it re-measures and refills rather than rebinding.
            GroveLand.Changed += Regrow;
            PlayerProgression.Changed += Repaint;

            // Residents are derived from the keeper ladder, so a run finished in this session
            // can wake a friend while the player is standing here.
            PlayerProgress.Reloaded += Repaint;
            PlayerProgress.RecordChanged += OnRecord;
        }

        void OnDestroy()
        {
            HomesteadCatalog.Changed -= Reload;
            HomesteadLedger.Changed -= Repaint;
            HomesteadLayout.Changed -= OnLayoutChanged;
            GroveLand.Changed -= Regrow;
            PlayerProgression.Changed -= Repaint;
            PlayerProgress.Reloaded -= Repaint;
            PlayerProgress.RecordChanged -= OnRecord;

            // Simply let go. Whether anything is actually freed is the library's question,
            // not this screen's: a shop or a panel that draws the same pieces has already taken
            // its own hold by the time this runs, and an address that does reach nought holds is
            // kept for a few seconds in case the player comes straight back.
            _art?.Dispose();
        }

        /// <summary>
        /// A piece placed, moved or taken away: redraw, and bring the hold in line with what is
        /// standing on the floor now.
        /// </summary>
        void OnLayoutChanged()
        {
            Repaint();
            GroveArtLoader.Fill(_art, GroveArtLoader.Grove(), this, Repaint);
        }

        void OnRecord(LevelRecord record) => Repaint();

        /// <summary>Takes newly bought ground: re-measures the field and refills it, in place.</summary>
        void Regrow()
        {
            if (_field == null) return;

            // A ceremony owns the ground while it is running, and refilling the field under one
            // would throw away the tiles it is in the middle of raising. Unreachable today —
            // land is only sold in the shop, which is a different screen — and one line rather
            // than a comment explaining why it cannot happen.
            if (_rise != null) return;

            // The ground itself changed, so a bar anchored to a tile is anchored to a fact that
            // no longer holds.
            _draft?.Close();

            ShowOwned();
            _field.Rebuild();
            Repaint();
        }

        void Warm() => Run(async token =>
        {
            await HomesteadService.EnsureAsync();
            if (!Living) return;

            Reload();

            // The art set is derived from the catalog, so it can only be asked for once the
            // catalog is in hand — the two waits are genuinely serial, which is why one readout
            // stays up across both and only changes what it says.
            _busy?.Say(Loc.Get("ui.grove.loading_art"));
            _art = GroveArtLoader.Open("grove", GroveArtLoader.Grove(), this, OnArtReady, _busy);
        });

        void OnArtReady()
        {
            if (!Living) return;

            _busy?.Done();
            Repaint();
        }

        // ----------------------------------------------------------------- field
        void BuildField()
        {
            // Laid inside the display's safe area rather than across the whole panel. A field
            // that runs under a camera cutout is a field with tiles the player cannot see and
            // cannot reliably tap, and the corner where a home indicator sits is exactly where
            // a thumb rests to pan. The node re-fits itself (see SafeArea), so a late reading —
            // iOS reports its inset a frame or two after a cold start — moves the field rather
            // than leaving it where a stale number put it. On a display with nothing in the way
            // every inset is zero and this is the layout it always was.
            var stage = SafeArea.Node("Stage", Content);

            _viewport = UIKit.Node("Viewport", stage);
            // No nav bar on this screen. It is the one page in the game that wants the whole
            // display: a floor is panned and zoomed, and a strip of chrome across the bottom is
            // both a slice of grove nobody can see and a row of buttons a dragging thumb keeps
            // catching. The corner arrow is the way out.
            _viewport.offsetMin = new Vector2(0f, 24f);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            _field = GroveFieldView.Attach(_viewport, HomesteadCatalog.Current.Floor,
                                           (col, row) => new GroveTileCell(TakeArrival));
            _field.TileTapped = Tap;
            _field.TileHeld = Hold;
            _field.Hit = Hit;

            // Tapping the sky puts the editing controls away, exactly as tapping a tile does.
            // The two have to agree: the sky is the largest target on this screen and the one a
            // player aims at when they mean "never mind".
            _field.TappedNothing = () => _draft?.Close();

            // The ghost and its buttons hang off the same node the field does, so they are
            // inside the safe area and above the floor. They are placed in world space every
            // frame they move, so being a sibling of the viewport rather than a child of a tile
            // is what keeps them alive while the field pools cells underneath them.
            _draft = new GroveDraftView(stage, _field, () => HomesteadCatalog.Current)
            {
                Settled = Repaint,
                Refused = key => Scenery.Toast(Content, Loc.Get(key)),
            };

            BuildInventoryButton(stage);
            ShowOwned();
        }

        /// <summary>
        /// The way into the inventory, in the corner a thumb rests in.
        ///
        /// <para>
        /// <b>It is a button because a tile is not a menu.</b> Opening the inventory by tapping
        /// the ground meant the one gesture the floor has was answering two questions — "what
        /// goes here" and "what is here" — and it put a modal in front of the player every time
        /// they missed a piece. A tap on the floor now means the thing under it, and the
        /// inventory is somewhere you go.
        /// </para>
        /// <para>
        /// Bottom left rather than bottom right: the right corner is where a right thumb pans
        /// from, and a panning thumb that catches a modal is the fault that took the nav bar off
        /// this screen in the first place.
        /// </para>
        /// </summary>
        void BuildInventoryButton(RectTransform stage)
        {
            UIKit.TextButton("Inventory", stage, Skins.Alternate, Loc.Get("ui.grove.inventory"), 30,
                             new Vector2(228f, 92f), new Vector2(0f, 0f),
                             new Vector2(130f, 78f), OpenInventory);
        }

        /// <summary>
        /// Opens the inventory, and turns whatever is chosen into a draft.
        ///
        /// The panel does not place anything any more: it hands back a piece and closes, and the
        /// ghost appears over the middle of the view. That is what makes the inventory a
        /// catalogue of what the player holds rather than a per-tile picker, and it is why the
        /// panel no longer needs to know which tile it was opened from.
        /// </summary>
        void OpenInventory()
        {
            _draft?.Close();
            Flow.Modal<HomesteadPickerOverlay>(v => v.Chosen = Take);
        }

        /// <summary>
        /// A piece picked up out of the inventory: its full-size art is added to this screen's
        /// hold, and the ghost starts following the finger.
        ///
        /// <para>
        /// <b>The art has to be asked for here rather than when the piece is put down.</b> The
        /// panel it was chosen from draws thumbnails and the floor draws the real thing, so a
        /// ghost drawn before the piece's own art has arrived is invisible — and it is being
        /// dragged, which is the one moment the player is looking straight at it.
        /// </para>
        /// </summary>
        void Take(string id)
        {
            _draft?.Begin(id);

            var piece = HomesteadCatalog.Current.Find(id);
            if (piece.IsValid) GroveArtLoader.Add(_art, GroveArtLoader.Piece(piece), this, Repaint);
        }

        /// <summary>
        /// Takes a new floor: throws every tile away and opens the camera on the hall.
        ///
        /// Called when the catalog is published and once when the body has been read — the two
        /// moments the <em>ground</em> differs. Everything else is a <see cref="Repaint"/>,
        /// which rebinds the tiles that exist without moving the camera, because a player who
        /// places a bench has not asked to be taken anywhere.
        /// </summary>
        void Reload()
        {
            if (_field == null) return;

            var catalog = HomesteadCatalog.Current;
            var floor = catalog.Floor;

            // Called at least twice in the ordinary case — the catalog raises its event and
            // Warm calls this directly — and it throws every tile away when it runs. A ceremony
            // already staged against this same ground must not be rebuilt out from under
            // itself; one staged against ground that has genuinely been republished is a
            // ceremony about a floor that no longer exists, so the land is simply delivered and
            // nothing is lost but the show.
            if (_rise != null)
            {
                if (_rise.Stages(floor)) { Repaint(); return; }
                _rise.Skip();
            }

            _draft?.Close();

            _field.SetFloor(floor);

            // How far the tallest and widest piece in the catalog reaches beyond its tile, so
            // the culling window keeps a tile alive while its picture is on screen.
            GroveTileArt.Reach(catalog, out float up, out float side);
            _field.SetReach(up, side);

            ShowOwned();
            _field.Rebuild();

            // A ceremony frames its own shot, and it has to be allowed to: the whole point of
            // the framing is that it is not where the player would have been left.
            if (!OpenRise(floor))
            {
                // Opened on the hall rather than on the field's origin, which is the corner of
                // a diamond and therefore the emptiest place on the screen. The hall is two
                // tiles deep, so its centre rather than its anchor.
                if (GroveFloor.TryParse(floor.HallTile, out int col, out int row))
                    _field.CentreOn(floor.HallFootprint.CentreCol(col), floor.HallFootprint.CentreRow(row));
                else
                    _field.CentreOn(floor.Cols / 2, floor.Rows / 2);
            }

            Repaint();

            // The body usually arrives before the transition finishes and sometimes after it.
            // Both of these are attempted from both ends rather than from whichever happens to
            // be second: a lesson shown once in a player's life must not be spent on a screen
            // that had nothing on it yet, and neither must a ceremony — see Teach and
            // StartRise.
            StartRise();
            Teach();
        }

        /// <summary>
        /// Tells the field which ground exists and how far it reaches.
        ///
        /// The bounds come from the regions rather than from a sweep of every tile — see
        /// <c>GroveLand.OwnedBounds</c>. Held as one method because the two have to be set
        /// together: a predicate without matching bounds is a field the camera can drag off.
        /// </summary>
        void ShowOwned()
        {
            var floor = HomesteadCatalog.Current.Floor;

            GroveLand.OwnedBounds(floor, out int minCol, out int minRow, out int maxCol, out int maxRow);
            _field.SetVisible(Owned, minCol, minRow, maxCol, maxRow);
        }

        /// <summary>Redraws the tiles that exist, in place, without moving the camera.</summary>
        void Repaint()
        {
            if (_field == null) return;

            // The boxes describe what is drawn, so they are only valid for as long as the
            // drawing is. Cleared here rather than at each writer, because this is the one
            // method every change already comes through.
            _hits.Clear();

            _field.Refresh();
            PaintSummary();
            _score?.Paint();
        }

    }
}
