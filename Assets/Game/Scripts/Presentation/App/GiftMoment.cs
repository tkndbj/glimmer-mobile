using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Gifts;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place a gift from the owner is put in front of a player (invariant 63). Nothing
    /// else may raise <see cref="GiftOverlay"/> on its own.
    ///
    /// <para>
    /// <b>A poll, for <see cref="ConsentMoment"/>'s reason</b>, driven from <c>Boot.Pump</c> between
    /// it and <see cref="DealMoment"/>: "the player is standing on an idle hub" is a drawing of the
    /// state, and every way back to one - the splash handing over, a run left, a panel closed, a
    /// sync landing while they stand there - is caught without anybody having to call in. The hub
    /// only: a gift is money, and money lands on the screen whose readouts it flies into.
    /// </para>
    /// <para>
    /// <b>The rules are <see cref="GiftPrompt"/>'s</b>: every waiting gift, whenever one of them
    /// has not been shown this session. Shown is marked the moment the panel is raised, so a
    /// process killed with it up does not raise it again this session - and the gift waits on the
    /// server, so the next session raises it anyway. A session is a launch, or a return after
    /// <see cref="NewSessionSeconds"/> in the background, as <see cref="DealMoment"/> counts one.
    /// </para>
    /// </summary>
    public static class GiftMoment
    {
        /// <summary>How long the hub must stand idle before the panel is laid over it, in seconds.</summary>
        const float Beat = 1f;

        /// <summary>How often the ledger is asked while the hub stands idle. It allocates a short list.</summary>
        const float AskEvery = .5f;

        /// <summary>Back after this long in the background is a new session, and a put-away gift is offered again.</summary>
        const float NewSessionSeconds = 30f * 60f;

        static readonly HashSet<string> _shown = new HashSet<string>(StringComparer.Ordinal);
        static float _calmSince = -1f, _nextAsk;
        static float _pausedAt = -1f;

        /// <summary>The app went to the background.</summary>
        public static void Paused() => _pausedAt = Time.realtimeSinceStartup;

        /// <summary>The app came back. Long enough away is a new session.</summary>
        public static void Resumed()
        {
            if (_pausedAt >= 0f && Time.realtimeSinceStartup - _pausedAt >= NewSessionSeconds) _shown.Clear();
            _pausedAt = -1f;
        }

        /// <summary>One frame.</summary>
        public static void Tick()
        {
            if (!(Flow.Current is HomeScreen) || !Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat || now < _nextAsk) return;
            _nextAsk = now + AskEvery;

            var show = GiftPrompt.Choose(GiftLedger.Waiting, _shown.Contains);
            if (show.Count == 0) return;

            Show(show);
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

        static void Show(List<AdminGift> gifts)
        {
            // Marked before anything is raised, so no later frame - or a panel that fails to build -
            // can raise a second one for the same gifts this session.
            foreach (var gift in gifts) _shown.Add(gift.Id);
            _calmSince = -1f;

            long credits = 0L, gems = 0L;
            foreach (var gift in gifts) { credits += gift.Credits; gems += gift.Gems; }
            Telemetry.Track("gift_shown", "gifts", gifts.Count, "credits", credits, "gems", gems);

            Flow.Modal<GiftOverlay>(v => v.Gifts = gifts);
        }
    }
}
