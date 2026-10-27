using GlimmerGrove.Async;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using GlimmerGrove.Social;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// When the store's rating box is asked for (invariant 68): after a win the player is proud of, on the
    /// first calm screen that is not a run.
    ///
    /// <para>
    /// <b>Noted at the victory panel's exit, in the slot nothing else took</b> (<c>WinOverlay.Leave</c>: a
    /// first step, else the chapter's account ask, else this), so it never follows another moment from the
    /// same run. It is only noted there; the box is asked for once the screen the player landed on has
    /// stood calm for <see cref="Beat"/> - never over a run (a store sheet over a live board costs a fight),
    /// the tutorial, a challenge, a modal or the update wall. A player who taps NEXT carries the moment
    /// through that run to the next calm screen. Backgrounding the app forgets it.
    /// </para>
    /// <para>
    /// <b>Re-judged when it fires</b> (<see cref="RatingLedger.Judge"/>), spent the instant before the
    /// request (<see cref="RatingLedger.NoteAsked"/>), and then the hub's own popups are hushed until the
    /// next run (<see cref="FirstStepsMoment.Hush"/>) so nothing is laid under the store's sheet. Off a
    /// phone nothing is spent.
    /// </para>
    /// </summary>
    public static class RatingMoment
    {
        /// <summary>How long the landing screen must stand calm before the box is asked for, in seconds.</summary>
        const float Beat = .6f;

        static int _stars;
        static float _calmSince = -1f;

        /// <summary>
        /// A ladder win left the victory panel with nothing else raised. Notes it when it qualifies; asks
        /// for nothing yet.
        /// </summary>
        public static void NoteWin(int stars)
        {
            if (RatingLedger.Judge(stars) != RatingVerdict.Ask) return;
            _stars = Mathf.Max(_stars, stars);
            _calmSince = -1f;
        }

        /// <summary>Every frame, from <c>Boot</c>.</summary>
        public static void Tick()
        {
            if (_stars <= 0) return;

            if (!Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat) return;

            int stars = _stars;
            Forget();

            // The table, the history and the money clock may all have moved since the win.
            if (RatingLedger.Judge(stars) != RatingVerdict.Ask) return;

            if (!StoreReview.IsSupported)
            {
                Debug.Log("[Rating] the store's rating box would be asked for now (no store here; nothing spent)");
                return;
            }

            RatingLedger.NoteAsked();
            FirstStepsMoment.Hush();
            StoreReview.Request();
        }

        /// <summary>The app went to the background: whatever was noted is no longer the moment.</summary>
        public static void Paused() => Forget();

        static void Forget()
        {
            _stars = 0;
            _calmSince = -1f;
        }

        /// <summary><c>ReminderMoment</c>'s calm: a settled screen that is not a run, with nothing over it.</summary>
        static bool Calm()
            => Flow.Current != null
            && !(Flow.Current is RunScreen)
            && !(Flow.Current is ChallengeScreen)
            && !(Flow.Current is TutorialScreen)
            && !(Flow.Current is SplashScreen)
            && !Flow.Busy
            && !Flow.Covered
            && !ReleaseGate.IsShut
            && Application.isFocused
            && LaunchCalm.IsSettled
            && !AdPrivacy.Owed
            && !AdPrivacy.IsAsking
            && !FirstStepsMoment.Hushed;
    }
}
