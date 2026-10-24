using System;
using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Privacy;
using GlimmerGrove.Progression;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place the helping hand's gift is put in front of a player (invariant 66). Nothing else
    /// may raise <see cref="AssistGiftOverlay"/>.
    ///
    /// <para>
    /// <b>Two doors.</b> The loss that earned it is answered over its own defeat panel
    /// (<see cref="AfterDefeat"/>), while "I keep losing this one" is still the player's thought - and
    /// it goes first there, ahead of any personal offer, so a defeat never carries both. A gift the
    /// player did not stay for (a retry tapped before the panel settled, the back key, a killed
    /// process) waits, owed, for the next calm hub (<see cref="Tick"/>), once a session.
    /// </para>
    /// </summary>
    public static class AssistMoment
    {
        /// <summary>How long the hub must stand idle before the panel is laid over it, in seconds.</summary>
        const float Beat = 1f;

        const float AskEvery = .5f;

        /// <summary>Back after this long in the background is a new session, as the gift panel counts one.</summary>
        const float NewSessionSeconds = 30f * 60f;

        static float _calmSince = -1f, _nextAsk;
        static float _pausedAt = -1f;
        static bool _offeredOnHub;

        public static void Paused() => _pausedAt = Time.realtimeSinceStartup;

        public static void Resumed()
        {
            if (_pausedAt >= 0f && Time.realtimeSinceStartup - _pausedAt >= NewSessionSeconds) _offeredOnHub = false;
            _pausedAt = -1f;
        }

        /// <summary>
        /// The defeat panel has settled and is the top panel. Answers whether the gift was raised over
        /// it - in which case the panel asks for no personal offer this defeat.
        /// </summary>
        public static bool AfterDefeat()
        {
            try
            {
                if (ReleaseGate.IsShut || !AssistLedger.GiftOwed) return false;
                Show("defeat");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

        /// <summary>One frame: a gift still owed, on a calm hub, once a session.</summary>
        public static void Tick()
        {
            if (_offeredOnHub || !(Flow.Current is HomeScreen) || !Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat || now < _nextAsk) return;
            _nextAsk = now + AskEvery;

            if (!AssistLedger.GiftOwed) return;

            _offeredOnHub = true;
            Show("hub");
        }

        /// <summary>Nothing over the hub, nothing about to be, and nothing else asking.</summary>
        static bool Calm()
            => !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused
            && LaunchCalm.IsSettled
            && !AdPrivacy.Owed
            && !AdPrivacy.IsAsking
            && !FirstStepsMoment.Hushed;

        static void Show(string where)
        {
            _calmSince = -1f;
            Telemetry.Track("assist_gift_shown", "where", where);
            Flow.Modal<AssistGiftOverlay>();
        }
    }
}
