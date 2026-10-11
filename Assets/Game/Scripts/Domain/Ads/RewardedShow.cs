using System;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// One rewarded video on screen, and the single answer it comes to - given <b>once the video
    /// has left the screen</b>, whatever order the network's callbacks arrive in.
    ///
    /// <para>
    /// <b>Two faults bought this class, both reported from a device on 2026-10-11.</b>
    /// </para>
    /// <list type="number">
    /// <item><b>"Rewarded" and "closed" are not ordered.</b> LevelPlay raises both for a video
    /// watched to the end and documents them as asynchronous; with some networks the close comes
    /// first. The provider took whichever arrived first, so a finished video was answered as one
    /// closed early.</item>
    /// <item><b>"Rewarded" arrives while the ad is still up</b> - at the end of the video, with the
    /// end card still showing. Answering then let the caller act behind the ad: the continue panel
    /// closed, the line was raised and the siege resumed under the end card, where a rallied line
    /// with nobody playing falls in six to eight seconds. The player closed the ad onto a second
    /// offer for the run they had just paid for.</item>
    /// </list>
    /// <para>
    /// <b>So the answer is the outcome and the departure together.</b> A reward is remembered and
    /// answered when the video goes - its close, or the game getting focus back after losing it to
    /// the video. A close waits <see cref="CloseGrace"/> for a reward that may follow it, and only
    /// then answers dismissed. A display failure answers at once. And two backstops mean no answer
    /// is ever withheld for good: a reward with no departure is answered after
    /// <see cref="DepartureBackstop"/>, and a departure with no reward after the grace.
    /// </para>
    /// <para>
    /// Pure of any SDK type, so every ordering is proved offline (<c>RewardedShowTests</c>); the
    /// provider only forwards its callbacks here. Thread-safe: the callbacks are not promised a
    /// thread, the flags are written through <see cref="Interlocked"/> (a full fence, so "set mine,
    /// then read yours" cannot be reordered between two threads), and the answer is a
    /// <see cref="TaskCompletionSource{T}"/> set at most once whose continuations run
    /// asynchronously, so no awaiting caller resumes on the SDK's stack.
    /// </para>
    /// </summary>
    public sealed class RewardedShow
    {
        /// <summary>
        /// How long a departure waits for a reward that may follow it. Long enough for a reward
        /// still being delivered as the ad finishes; short enough that a player who skipped is told
        /// so while still looking at the panel.
        /// </summary>
        public static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How long a reward waits for the video to leave before it is answered anyway - the net
        /// under an SDK that never reports the close. Far longer than any end card is looked at, so
        /// it never cuts one short in practice; and on iOS the game is paused under the ad
        /// (<c>LevelPlay.SetPauseGame</c>), so even then nothing resumes behind it.
        /// </summary>
        public static readonly TimeSpan DepartureBackstop = TimeSpan.FromMinutes(2);

        readonly TaskCompletionSource<AdShowResult> _answer =
            new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        readonly TimeSpan _grace, _backstop;

        int _rewarded, _gone, _left, _waitingForDeparture;

        public RewardedShow(AdImpression impression) : this(impression, CloseGrace, DepartureBackstop) { }

        /// <summary>A show with its own timings, for tests that cannot wait seconds or minutes.</summary>
        public RewardedShow(AdImpression impression, TimeSpan grace, TimeSpan backstop)
        {
            Impression = impression;
            _grace = grace < TimeSpan.Zero ? TimeSpan.Zero : grace;
            _backstop = backstop < TimeSpan.Zero ? TimeSpan.Zero : backstop;
        }

        /// <summary>The impression this show is for, carried back on its answer.</summary>
        public AdImpression Impression { get; }

        /// <summary>The one answer. Completes exactly once, never while the video is still up.</summary>
        public Task<AdShowResult> Answer => _answer.Task;

        /// <summary>True once the answer is known.</summary>
        public bool Settled => _answer.Task.IsCompleted;

        /// <summary>
        /// True when a reward arrived after the show had already been answered as dismissed - the
        /// grace was too short for this network. Read by the provider to say so in the log.
        /// </summary>
        public bool RewardedLate { get; private set; }

        /// <summary>
        /// The network says the video was watched to the end. Answered at once if the video has
        /// already gone; otherwise remembered, and answered when it goes.
        /// </summary>
        public Task Rewarded()
        {
            if (Settled)
            {
                RewardedLate = _answer.Task.Result.Outcome == AdOutcome.Dismissed;
                return Task.CompletedTask;
            }

            Interlocked.Exchange(ref _rewarded, 1);

            if (Volatile.Read(ref _gone) == 1)
            {
                Finish();
                return Task.CompletedTask;
            }

            // The end card is still up. Answer when it goes; the backstop only if it never says so.
            return Interlocked.Exchange(ref _waitingForDeparture, 1) == 1
                ? Task.CompletedTask
                : RewardAfterBackstopAsync();
        }

        /// <summary>The video could not be shown. Answers at once; no reward can follow a failure.</summary>
        public void DisplayFailed(string message)
            => _answer.TrySetResult(AdShowResult.Failed(AdOutcome.Error, Impression, message));

        /// <summary>
        /// The video has left the screen. Answers rewarded at once if the reward is in; otherwise
        /// waits the grace for it and answers dismissed. A second departure changes nothing.
        /// </summary>
        public Task Closed()
        {
            if (Settled || Interlocked.Exchange(ref _gone, 1) == 1) return Task.CompletedTask;

            if (Volatile.Read(ref _rewarded) == 1)
            {
                Finish();
                return Task.CompletedTask;
            }

            return DismissAfterGraceAsync();
        }

        /// <summary>The game lost focus while this show was up: the video's own screen took it.</summary>
        public void Left() => Interlocked.Exchange(ref _left, 1);

        /// <summary>
        /// The game has focus again after losing it to this show, so the video is gone - a
        /// departure, exactly as a close is, and the net under a close the SDK never raised.
        ///
        /// <para>
        /// A return with no departure before it is ignored, so focus wobbling while the SDK is
        /// still opening the video cannot answer a show that has not been seen. Where the video
        /// takes no focus from the game (iOS presents it over the same view), this never fires and
        /// the SDK's close is the departure.
        /// </para>
        /// </summary>
        public Task Returned() => Volatile.Read(ref _left) == 1 ? Closed() : Task.CompletedTask;

        void Finish() => _answer.TrySetResult(new AdShowResult(AdOutcome.Rewarded, Impression));

        // Both waits run off the caller's context on purpose: nothing here touches Unity, and the
        // answer's own continuations are what return to the main thread.
        async Task DismissAfterGraceAsync()
        {
            if (_grace > TimeSpan.Zero) await Task.Delay(_grace).ConfigureAwait(false);

            // A reward that landed during the grace has answered already (it saw the departure);
            // this is the skip, or nothing.
            _answer.TrySetResult(AdShowResult.Failed(AdOutcome.Dismissed, Impression));
        }

        async Task RewardAfterBackstopAsync()
        {
            if (_backstop > TimeSpan.Zero) await Task.Delay(_backstop).ConfigureAwait(false);

            Finish();
        }
    }
}
