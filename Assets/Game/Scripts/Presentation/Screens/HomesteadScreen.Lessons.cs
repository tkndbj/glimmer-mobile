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
    /// The two things a first visit has to be told, and the rule that neither is spent on a
    /// screen that has nothing on it yet.
    /// </summary>
    public sealed partial class HomesteadScreen
    {
        // ------------------------------------------------------------------ tips
        public override void OnPresented()
        {
            _presented = true;
            StartRise();
            Teach();
        }

        /// <summary>
        /// The two things a first visit has to be told: what this place is, and where the
        /// things it is built from come from.
        ///
        /// <para>
        /// <b>They are ordinary lessons, on the ordinary ledger.</b> A grove tip is a
        /// <c>Mechanic</c> like a crossing is — a permanent id, strings derived from it, and
        /// <c>TipLedger</c> recording that this player has met it. That is what makes them
        /// shown once in a lifetime rather than once per install: the ledger is a union-joined
        /// set in the save file, so a second device does not re-teach what the first one
        /// taught, and it cost no new field to say so. They are deliberately not in
        /// <c>Mechanic.TeachingOrder</c>, which is the board scan's queue — nothing about a
        /// glade implies the player has opened the Grovement.
        /// </para>
        /// <para>
        /// <b>Nothing is taught over an empty screen.</b> The catalog is a body read on
        /// entering the feature, so on a cold start it can land after the transition has
        /// finished — and a welcome tip spent while the grove behind it is still blank is
        /// spent for good. So this is attempted from both <see cref="OnPresented"/> and
        /// <see cref="Reload"/>, does nothing until there is a grove to point at, and does
        /// nothing twice.
        /// </para>
        /// </summary>
        void Teach()
        {
            if (_taught || _teaching || !_presented) return;
            if (!HomesteadCatalog.IsLoaded || HomesteadCatalog.Current.Floor.IsEmpty) return;

            // Ground arriving takes the screen, and a lesson raised over it would be a modal
            // in front of the thing the player just paid to watch. The ceremony calls this
            // itself when it hands the screen back, so nothing is lost by waiting — and a
            // first visit cannot be one of these anyway, since land costs credits.
            if (_pending != null || _rise != null) return;

            var queue = new List<ScreenLesson>(2);

            // The welcome has nothing to ring: it is about the whole screen, and a hole cut
            // around one tile would say it is about that tile.
            ScreenLessons.OfferScreen(queue, Mechanic.Grove);
            ScreenLessons.Offer(queue, Mechanic.GroveShop, _shop);

            _taught = true;
            if (queue.Count == 0) return;

            _teaching = true;

            // A beat after the iris, so the first thing the player sees is their own grove
            // and the second is somebody explaining it. Chained by `ScreenLessons`, which is
            // the same sequence the map and the loadout shelf get - a second copy of "one
            // panel at a time, with a beat between them" is a second thing that can disagree.
            //
            // The editing controls are put away before each one, because the shop lesson cuts
            // a hole around the shop button and a bar floating over the field inside that hole
            // would be lit by a lesson that is not about it.
            Tween.After(.45f, () => ScreenLessons.Show(this, queue,
                                                       finished: () => _teaching = false,
                                                       beforeEach: () => _draft?.Close()), this);
        }
    }
}
