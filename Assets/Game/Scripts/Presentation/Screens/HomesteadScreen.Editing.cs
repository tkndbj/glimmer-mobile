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
    /// What a tap on the floor means, and what is under it. The draft itself lives in
    /// <see cref="GroveDraftView"/> and <c>GroveDraft</c>; this is the screen's half — hit-testing
    /// a tile against what is drawn on it, and turning a gesture into a lift.
    /// </summary>
    public sealed partial class HomesteadScreen
    {
        // ------------------------------------------------------------ what is drawn
        /// <summary>The stand covering a tile — anchored on it or reaching over it — or an invalid one.</summary>
        static bool StandAt(int col, int row, out GroveStand stand)
            => HomesteadLayout.TryStandAt(HomesteadCatalog.Current, col, row, out stand);

        readonly Dictionary<long, GroveHit> _hits = new Dictionary<long, GroveHit>();

        /// <summary>
        /// The box and mask of the art drawn from a tile, in field space — what
        /// <see cref="GrovePick"/> tests a tap against. Answers for an anchor only; a tile a
        /// footprint reaches over draws nothing of its own.
        ///
        /// <para>
        /// Cached per tile because this is asked for every live tile on every tap <em>and</em>
        /// on every frame of a move drag. Sixty tiles a frame under a moving thumb is exactly
        /// the continuous garbage the field's depth comparer is held as a field to avoid. The
        /// cache is cleared by <see cref="Repaint"/>, which is the one door every change to
        /// the picture comes through.
        /// </para>
        /// </summary>
        GroveHit Hit(int col, int row)
        {
            long key = GroveOccupancy.Key(col, row);
            if (_hits.TryGetValue(key, out var cached)) return cached;

            var catalog = HomesteadCatalog.Current;
            var hit = new GroveHit(col, row, 0f, 0f, 0f, 0f);

            if (HomesteadLayout.Occupancy(catalog).TryAnchored(col, row, out var stand))
                hit = GroveTileArt.Hit(GroveTileArt.PieceOf(catalog, stand), stand);

            _hits[key] = hit;
            return hit;
        }

        // ------------------------------------------------------------- placing
        //
        // Everything about putting something down lives in GroveDraftView and GroveDraft, which
        // is the whole of this rework. What used to be here was a state machine in loose fields
        // — an _editing flag, a _dragging flag, a ghost, two mark sets, a cached plan and four
        // coordinates — so "what is lit", "what will be written" and "is the button live" were
        // three answers kept in step by hand. They are one object now and this screen composes
        // it rather than being it.

        GroveDraftView _draft;

        /// <summary>
        /// Says that what was asked for does not fit. Kept public because the draft view raises
        /// its refusals as loc keys rather than drawing them: a toast belongs to the screen that
        /// owns the safe area, not to a control floating over a floor.
        /// </summary>
        public void SayNoRoom() => Scenery.Toast(Content, Loc.Get("ui.grove.no_room"));


        // ------------------------------------------------------------------- tap
        void Tap(int col, int row)
        {
            // A tap while something is being placed puts it away and does nothing else. One
            // tap to dismiss is what every panel here does, and answering the dismissing tap by
            // lifting something else as well would be two responses to one gesture.
            if (_draft != null && _draft.Active) { _draft.Close(); return; }

            // No land branch: ground the player does not own is not drawn, so there is nothing
            // here to tap. Expanding is done in the shop, where the other things they buy are.

            if (!StandAt(col, row, out var stand)) return;      // bare ground; the button is the way in

            // A home goes to the home panel in every state — the question at a house is
            // almost always "where am I on the ladder" rather than "shall I move this", and a
            // tap is the gesture that asks it. Moving one is a **hold** (see `Hold`), which is
            // the split every other tile makes too: a tap asks about a thing, a press picks it
            // up. Reaching the ladder would otherwise cost a trip through a menu on the one
            // object a player looks at most.
            if (stand.IsHall) { Flow.Modal<HomesteadHomeOverlay>(); return; }

            // Whatever covers the tile is what was tapped, so touching the far end of a bridge
            // lifts the bridge. The draft resolves that itself, from the stand.
            _draft?.Lift(col, row);
        }

        /// <summary>
        /// A finger resting on a tile lifts whatever is standing there, the hall included.
        ///
        /// <para>
        /// <b>It is the same verb on every tile rather than a gesture the hall alone answers.</b>
        /// A control that works in one place and nowhere else is one nobody finds twice, and the
        /// hold is already how the rest of this floor behaves — a piece a tap lifts is a piece a
        /// press lifts, so there is nothing new to learn and the hall is simply no longer the
        /// exception. What it buys is the home: a tap there opens the ladder (see
        /// <see cref="Tap"/>), so without this there would be no gesture left that could move a
        /// house.
        /// </para>
        /// <para>
        /// The hold cancels the tap that press would have produced (`GroveFieldView.Hold`), so
        /// a player who holds the hall gets the draft and never the panel behind it.
        /// </para>
        /// </summary>
        void Hold(int col, int row)
        {
            if (_draft != null && _draft.Active) return;   // already holding something
            _draft?.Lift(col, row);
        }
    }
}
