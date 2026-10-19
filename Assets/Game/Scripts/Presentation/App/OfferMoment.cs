using GlimmerGrove.Async;
using GlimmerGrove.Offers;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place a personal offer is put in front of a player (invariant 64). Nothing else may
    /// raise <see cref="OfferOverlay"/> on its own except the shop's "for you" band, which only
    /// re-opens the offer already standing.
    ///
    /// <para>
    /// <b>A poll, for <see cref="ConsentMoment"/>'s reason</b>, driven from <c>Boot.Pump</c> after
    /// <see cref="DealMoment"/>: "the player is standing somewhere calm" is a drawing of the state.
    /// The moment it answers was left by play (<see cref="OfferLedger.Pending"/> - a run won or lost,
    /// a coin price that could not be paid, a new session), and the rules decide whether any offer
    /// answers it (<see cref="OfferRules.Choose"/>); this class only says where the player stands and
    /// whether it is calm there, held for <see cref="Beat"/> so the player sees where they have
    /// landed before anything is laid over it.
    /// </para>
    /// <para>
    /// <b>Where.</b> A shortfall is answered on the screen it happened on (the hub, the loadout, the
    /// keeper ladder); every other moment on the hub or the loadout. A calm hub with nothing waiting
    /// is asked once per stand as the plain <see cref="OfferTrigger.Hub"/> moment.
    /// </para>
    /// </summary>
    public static class OfferMoment
    {
        /// <summary>
        /// How long a calm screen must stand, in seconds. Longer than the deals popup's beat, so on the
        /// frame both could speak the deal (once a day, owner-made) goes first and paces this one.
        /// </summary>
        const float Beat = 1.6f;

        const float AskEvery = .5f;

        /// <summary>Back after this long in the background is a new session, as the deals popup counts one.</summary>
        const float NewSessionSeconds = 30f * 60f;

        static float _calmSince = -1f, _nextAsk;
        static float _pausedAt = -1f;
        static bool _askedHub;

        public static void Paused()
        {
            _pausedAt = Time.realtimeSinceStartup;
            OfferLedger.Seen();
            OfferLedger.Save();
        }

        public static void Resumed()
        {
            if (_pausedAt >= 0f && Time.realtimeSinceStartup - _pausedAt >= NewSessionSeconds) OfferLedger.BeginSession();
            _pausedAt = -1f;
        }

        /// <summary>One frame.</summary>
        public static void Tick()
        {
            bool onHub = Flow.Current is HomeScreen;
            bool onLoadout = Flow.Current is LoadoutScreen;
            bool onKeeper = Flow.Current is KeeperScreen;

            if (!(onHub || onLoadout || onKeeper) || !Calm())
            {
                _calmSince = -1f;
                _askedHub = false;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat || now < _nextAsk) return;
            _nextAsk = now + AskEvery;

            OfferLedger.Seen();
            OfferConfig.Refresh();

            var pending = OfferLedger.Pending;

            // A shortfall answers where it happened; anything else waits for the hub or the loadout.
            bool here = pending == OfferTrigger.Shortfall || onHub || onLoadout;
            if (pending != OfferTrigger.None && here)
            {
                var deal = OfferLedger.TryDeal(pending);
                OfferLedger.Spend();
                if (deal.Dealt) { Show(); return; }
            }

            if (onHub && !_askedHub)
            {
                _askedHub = true;
                if (OfferLedger.TryDeal(OfferTrigger.Hub).Dealt) Show();
            }
        }

        /// <summary>Nothing over the screen, nothing about to be, and nothing else asking.</summary>
        static bool Calm()
            => !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused
            && LaunchCalm.IsSettled
            && !AdPrivacy.Owed
            && !AdPrivacy.IsAsking;

        static void Show()
        {
            _calmSince = -1f;
            var standing = OfferLedger.Standing;
            if (standing == null) return;

            Flow.Modal<OfferOverlay>(v =>
            {
                v.Offer = standing;
                v.FromShop = false;
            });
        }
    }
}
