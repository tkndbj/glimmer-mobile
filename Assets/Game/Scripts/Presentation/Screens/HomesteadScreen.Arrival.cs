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
    /// The Grovement's land-arrival ceremony: staging it, starting it, and handing the ground
    /// back afterwards. Split out because it is the one thing on this screen with a life of its
    /// own — it withholds tiles the player already owns, frames the camera away from where they
    /// left it, and has to survive the catalog being republished underneath it.
    /// </summary>
    public sealed partial class HomesteadScreen
    {
        // -------------------------------------------------------------- arriving
        /// <summary>
        /// Stages the ceremony for ground bought a moment ago, if there is any. True once
        /// something is framing the camera, so the ordinary opening shot stands aside.
        /// </summary>
        bool OpenRise(GroveFloor floor)
        {
            if (_rise != null) return true;
            if (_pending == null) return false;

            // The catalog is a body and may have been republished between the purchase and
            // this screen, so the region is looked up again rather than trusted. A region that
            // is no longer on the floor is simply not celebrated; the land is still owned.
            var region = _pending;
            if (floor.IsEmpty || floor.Region(region.Id) == null)
            {
                _pending = null;
                return false;
            }

            _rise = GroveRise.Play(Content, _field, floor, region,
                                   (col, row) => GroveLand.IsOwned(floor, col, row)
                                              && !region.Holds(col, row),
                                   OnRiseDone);

            return _rise != null;
        }

        /// <summary>
        /// Starts the ceremony once there is both a staged one and a screen the player can
        /// see. Does nothing twice — <c>GroveRise.Begin</c> holds that rule rather than a flag
        /// here, so a second caller cannot get it wrong.
        /// </summary>
        void StartRise()
        {
            if (_presented) _rise?.Begin();
        }

        /// <summary>
        /// Hands the ground back. The withholding stops, the field is re-tested, and whatever
        /// the ceremony was standing in front of happens now: the star the purchase earned,
        /// and the first-visit lessons it was holding up.
        /// </summary>
        void OnRiseDone()
        {
            _rise = null;
            _pending = null;

            if (_field != null)
            {
                ShowOwned();
                _field.Revisit();
            }

            Repaint();
            CelebrateArrival();
            Teach();
        }

        /// <summary>
        /// A star won by the purchase, landed now rather than while the player was in the shop.
        ///
        /// <para>
        /// Measured against the reading taken before the money was spent (see
        /// <see cref="ArrivingStars"/>) rather than against <see cref="_starsShown"/>, which
        /// this screen's first paint has already moved to the new figure — quietly, and
        /// deliberately, because a baseline taken on a blank grove is how a celebration comes
        /// to mean nothing.
        /// </para>
        /// </summary>
        void CelebrateArrival()
        {
            int before = ArrivingStars;
            ArrivingStars = -1;

            _score?.Celebrate(before);
        }

        /// <summary>
        /// Which ground exists. Unowned land is not drawn at all — see
        /// <c>GroveFieldView.SetVisible</c> for why a field of padlocks was the wrong screen.
        ///
        /// <para>
        /// Ground bought a moment ago is owned and still withheld, which is the one place
        /// those two come apart. Kept as an <c>and</c> of two questions rather than folded
        /// into one predicate: what the player owns is <c>GroveLand</c>'s answer and never
        /// this screen's, and a ceremony that could make land look unowned is a ceremony one
        /// bug away from selling it twice.
        /// </para>
        /// </summary>
        bool Owned(int col, int row)
            => GroveLand.IsOwned(HomesteadCatalog.Current.Floor, col, row)
            && !Withheld(col, row);

        bool Withheld(int col, int row)
            => _rise != null
                ? _rise.Hides(col, row)
                : _pending != null && _pending.Holds(col, row);

        /// <summary>
        /// True exactly once per tile of arriving ground, on the bind that first draws it.
        /// The cell's signal to rise into place rather than appear — see <c>GroveRise</c>.
        /// </summary>
        bool TakeArrival(int col, int row) => _rise != null && _rise.TakeArrival(col, row);
    }
}
