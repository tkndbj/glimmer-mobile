using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Async;
using GlimmerGrove.Persistence;
using GlimmerGrove.Privacy;
using GlimmerGrove.Release;
using GlimmerGrove.Store;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The one place a limited-time deal is put in front of a player outside the shop (invariant
    /// 60c). Nothing else may raise <see cref="DealOverlay"/> on its own.
    ///
    /// <para>
    /// <b>A poll, for <see cref="ConsentMoment"/>'s reason</b>, driven from <c>Boot.Pump</c> after
    /// it: "when the player is standing somewhere calm" is a drawing of the state, not a sequence
    /// of events to keep in step with, and every way back to a calm screen - a panel closed, a run
    /// left, a sheet dismissed - is caught without anybody having to remember to call in.
    /// </para>
    /// <para>
    /// <b>The rules are <see cref="DealPrompt"/>'s</b> (who, which deals, which trigger); this class
    /// only says where the player is and whether it is calm there: the screen current and finished
    /// arriving, nothing over it, no update wall, the app focused, the launch settled and the
    /// consent questions answered - held for <see cref="Beat"/> so the player sees where they have
    /// landed before anything is laid over it.
    /// </para>
    /// <para>
    /// <b>One popup a session</b>, and the deals it carries are marked shown in the save at the
    /// moment it opens, not when it closes: a process killed with the panel up must not raise it
    /// again, and the promise is "once", not "until dismissed". A session is a launch, or a return
    /// after <see cref="NewSessionSeconds"/> in the background.
    /// </para>
    /// </summary>
    public static class DealMoment
    {
        /// <summary>How long a calm screen must stand before a popup is laid over it, in seconds.</summary>
        const float Beat = 1.2f;

        /// <summary>How often the rules are asked while the screen stands calm. They allocate a short list.</summary>
        const float AskEvery = .5f;

        /// <summary>Back after this long in the background is a new session, with a popup of its own.</summary>
        const float NewSessionSeconds = 30f * 60f;

        static bool _hooked, _shownThisSession, _winPending, _shortfallPending;
        static float _calmSince = -1f, _nextAsk;
        static float _pausedAt = -1f;

        /// <summary>
        /// Listens for wins. Called by <c>Boot</c> once; idempotent. <see cref="PlayerProgress.RecordChanged"/>
        /// is raised by a recorded win and nothing else (<c>RunLedger.Win</c>), so it is the one
        /// place a win is heard whichever mode or screen ended it.
        /// </summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            PlayerProgress.RecordChanged += record => { if (record.IsCleared) _winPending = true; };
        }

        /// <summary>
        /// A coin price was just shown that the player cannot pay - a turret, a star, a keeper level.
        /// Answered on the next calm screen that can answer it. Cheap and idempotent: a panel may say
        /// it on every repaint.
        /// </summary>
        public static void NoteShortfall() => _shortfallPending = true;

        /// <summary>The app went to the background.</summary>
        public static void Paused() => _pausedAt = Time.realtimeSinceStartup;

        /// <summary>The app came back. Long enough away is a new session, and a new session may be offered again.</summary>
        public static void Resumed()
        {
            if (_pausedAt >= 0f && Time.realtimeSinceStartup - _pausedAt >= NewSessionSeconds)
            {
                _shownThisSession = false;
                _winPending = false;
                _shortfallPending = false;
            }
            _pausedAt = -1f;
        }

        /// <summary>One frame.</summary>
        public static void Tick()
        {
            if (_shownThisSession) return;

            bool onHub = Flow.Current is HomeScreen;
            bool onStage = onHub || Flow.Current is LoadoutScreen || Flow.Current is KeeperScreen;

            if (!onStage || !Calm())
            {
                _calmSince = -1f;
                return;
            }

            float now = Time.unscaledTime;
            if (_calmSince < 0f) _calmSince = now;
            if (now - _calmSince < Beat || now < _nextAsk) return;
            _nextAsk = now + AskEvery;

            // Keep the list fresh while somebody is standing where a popup could appear; the ledger
            // reads at most once a quarter of an hour whoever asks.
            DealLedger.Refresh();

            long unix = GameClock.NowUnix();
            var unseen = DealPrompt.Unseen(DealLedger.OfferedAllAt(unix), DealSeen.Has);
            var trigger = DealPrompt.Choose(PlayerProgress.ClearedCount, _shownThisSession, _shortfallPending,
                                            _winPending, onHub, onStage, unseen, unix);

            // A pending win or shortfall that found nothing to offer is spent: the next deal is
            // offered at its own moment, not at one that happened before it existed.
            if (trigger == DealTrigger.None)
            {
                if (unseen.Count == 0) { _winPending = false; _shortfallPending = false; }
                return;
            }

            Show(trigger, unseen);
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

        static void Show(DealTrigger trigger, List<ShopDeal> deals)
        {
            // Spent before anything is raised, so no later frame - or a panel that fails to build -
            // can raise a second one this session.
            _shownThisSession = true;
            _winPending = false;
            _shortfallPending = false;
            _calmSince = -1f;

            var ids = new List<string>(deals.Count);
            foreach (var deal in deals) ids.Add(deal.Id);
            DealSeen.Mark(ids);

            Telemetry.Track("deal_offer_shown", "trigger", DealPrompt.Id(trigger), "deals", deals.Count);

            Flow.Modal<DealOverlay>(v =>
            {
                v.Deals = deals;
                v.Trigger = trigger;
            });
        }
    }
}
