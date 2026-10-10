using System;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The shop tab's deal alert on the bottom bar (invariant 60): a purple starburst with a
    /// white <c>!!!</c> on the tab's top-right corner while a deal is on sale that this account
    /// has not bought (<see cref="DealLedger.Offered"/>).
    ///
    /// <para>
    /// <b>Watched, never drawn</b> (44j's rule about readouts, said of a badge): it subscribes to
    /// <see cref="DealLedger.Changed"/> and repaints, and it re-asks once a second because a
    /// deal ends on the clock and nothing announces the clock. It is also where the deals are
    /// asked for (<see cref="DealLedger.Refresh"/>): every screen with the bar builds one, and the
    /// ledger's own cadence turns that into at most one read a quarter of an hour.
    /// </para>
    /// </summary>
    public sealed class DealAlert : MonoBehaviour
    {
        WaitingBadge _badge;
        float _next;
        bool _shown;

        /// <summary>Hangs the alert on <paramref name="plate"/>'s top-right corner.</summary>
        public static DealAlert Attach(RectTransform plate)
        {
            var alert = plate.gameObject.AddComponent<DealAlert>();
            alert._badge = WaitingBadge.Alert(plate, new Vector2(1f, 1f), new Vector2(-16f, -12f),
                                              WaitingBadge.DealPurple, 1f);
            return alert;
        }

        void OnEnable()
        {
            DealLedger.Changed += Paint;
            DealLedger.Refresh();
            Paint();
        }

        void OnDisable() => DealLedger.Changed -= Paint;

        void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            Paint();
        }

        void Paint()
        {
            if (!this || _badge == null) return;

            bool show = DealLedger.Offered != null;
            if (show == _shown) return;
            _shown = show;

            _badge.Paint(show ? WaitingBadge.AlertMarks : string.Empty);
        }
    }

    /// <summary>
    /// The shop's deal band's clock (invariant 60): the timer to the second, red and pulsing on
    /// every tick in the last hour, the burst behind the coffer turning, and the word to the shop
    /// when the deal it shows is no longer the one on offer (ended, bought or replaced) so the
    /// band can be taken away.
    /// </summary>
    public sealed class DealClock : MonoBehaviour
    {
        /// <summary>Under this, the timer is red and every second lands with a pulse.</summary>
        public const long UrgentSeconds = 3600;

        /// <summary>How fast the burst turns, in degrees a second. Slow enough to be light, not motion.</summary>
        const float RaysPerSecond = 14f;

        /// <summary>How big a tick's pulse starts, and how long it takes to settle.</summary>
        const float Pulse = .14f, PulseTime = .25f;

        static readonly Color Calm = Color.white;
        static readonly Color Urgent = new Color(1f, .36f, .28f);

        Text _timer;
        RectTransform _rays;
        string _dealId;
        Action _gone;
        long _shown = -1;
        float _tickAt, _nextCheck;
        bool _left;

        /// <summary>
        /// How far to lift a line of digits so the <em>digits</em>, not the line, sit on the middle of
        /// their box, at <paramref name="size"/>. Measured off <c>GameFont.ttf</c>: Unity centres a line
        /// on its glyph-bound line box (956 up and 161 down per 1000 units, the font's
        /// <c>ascentCalculationMode</c>), while a digit stands from the baseline to 707 - so a number
        /// drawn "middle" sits .044 of its size low. Three units at the deal cards' sizes.
        /// </summary>
        public static float DigitLift(int size) => Mathf.Round(size * .044f);

        /// <summary>
        /// A wait to the second: <c>1d 04:12:33</c>, then <c>04:12:33</c>. Seconds always, because
        /// a clock that visibly moves is the point of the band.
        /// </summary>
        public static string Timer(long seconds)
        {
            if (seconds < 0) seconds = 0;
            long days = seconds / 86400, hours = seconds % 86400 / 3600, minutes = seconds % 3600 / 60, secs = seconds % 60;
            string clock = hours.ToString("00") + ":" + minutes.ToString("00") + ":" + secs.ToString("00");
            return days > 0 ? days + "d " + clock : clock;
        }

        public static DealClock Attach(GameObject band, Text timer, RectTransform rays, ShopDeal deal, Action gone)
        {
            var clock = band.AddComponent<DealClock>();
            clock._timer = timer;
            clock._rays = rays;
            clock._dealId = deal.Id;
            clock._gone = gone;
            return clock;
        }

        void Update()
        {
            if (_left) return;
            float now = Time.unscaledTime;

            if (_rays) _rays.localRotation = Quaternion.Euler(0f, 0f, -now * RaysPerSecond);

            if (now >= _nextCheck)
            {
                _nextCheck = now + .2f;

                var deal = DealLedger.Offered;
                if (deal == null || deal.Id != _dealId)
                {
                    // Once: the shop destroys this band in answer, and a second call from the same
                    // frame would ask it to take away a band it has already replaced.
                    _left = true;
                    _gone?.Invoke();
                    return;
                }

                long left = deal.SecondsLeft(GameClock.NowUnix());
                if (left != _shown && _timer)
                {
                    _shown = left;
                    _timer.text = Timer(left);
                    _timer.color = left < UrgentSeconds ? Urgent : Calm;
                    _tickAt = now;
                }
            }

            // The last hour: each second lands with a pulse that settles before the next.
            if (_timer)
            {
                float k = _shown >= 0 && _shown < UrgentSeconds ? Mathf.Clamp01(1f - (now - _tickAt) / PulseTime) : 0f;
                _timer.rectTransform.localScale = Vector3.one * (1f + Pulse * k * k);
            }
        }
    }
}
