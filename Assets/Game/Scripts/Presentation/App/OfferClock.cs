using System;
using GlimmerGrove.Offers;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The shop band's clock for a personal offer (invariant 64): the time left to the second, red in
    /// the last hour, and a call to the band's owner the moment it runs out - which no event
    /// announces. <see cref="DealClock"/>'s job for a <see cref="StandingOffer"/>, sharing its
    /// <see cref="DealClock.Timer"/> and <see cref="DealClock.UrgentSeconds"/> so the two clocks read
    /// alike side by side.
    /// </summary>
    public sealed class OfferClock : MonoBehaviour
    {
        Text _timer;
        StandingOffer _offer;
        Action _gone;
        float _next;
        bool _spent;

        static readonly Color Urgent = new Color(1f, .36f, .28f);

        public static OfferClock Attach(GameObject band, Text timer, StandingOffer offer, Action gone)
        {
            var clock = band.AddComponent<OfferClock>();
            clock._timer = timer;
            clock._offer = offer;
            clock._gone = gone;
            return clock;
        }

        void Update()
        {
            if (_spent || _offer == null || !_timer) return;

            float now = Time.unscaledTime;
            if (now < _next) return;
            _next = now + .25f;

            long left = _offer.SecondsLeft(GameClock.NowUnix());
            _timer.text = DealClock.Timer(left);
            _timer.color = left < DealClock.UrgentSeconds ? Urgent : Color.white;

            if (left > 0) return;

            _spent = true;
            try { _gone?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
