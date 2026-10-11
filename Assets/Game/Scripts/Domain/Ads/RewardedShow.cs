using System;
using System.Threading.Tasks;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// One rewarded video on screen, and the single answer it comes to - whatever order the
    /// network's callbacks arrive in.
    ///
    /// <para>
    /// <b>It exists because "rewarded" and "closed" are not ordered.</b> LevelPlay raises both for a
    /// video watched to the end, and its own documentation says the two are asynchronous and may
    /// arrive either way round; with some networks (AdMob through LevelPlay, which is what Android
    /// runs) the close comes first. The provider used to take whichever arrived first, so a video
    /// watched to the end and closed was answered as one closed early: the continue panel stayed
    /// up over a lost run, the hearts never landed, and the player was told the video had to
    /// finish (2026-10-11, reported from a device). Currency placements hid it, because the server
    /// grants those off its own callback whatever the device decided.
    /// </para>
    /// <para>
    /// <b>So a close is not an answer until the reward has had time to follow it.</b> A reward
    /// settles at once, before or after the close; a display failure settles at once; a close
    /// starts <see cref="CloseGrace"/>, and only when that runs out with no reward is the video
    /// answered as dismissed. A player who really did skip waits that long on OPENING..., which
    /// is the cost of never refusing one who did not.
    /// </para>
    /// <para>
    /// Pure of any SDK type, so every ordering is proved offline (<c>RewardedShowTests</c>); the
    /// provider only forwards its three callbacks here. Thread-safe: the callbacks are not
    /// promised a thread, the answer is a <see cref="TaskCompletionSource{T}"/> set at most once,
    /// and its continuations run asynchronously so no awaiting caller resumes on the SDK's stack.
    /// </para>
    /// </summary>
    public sealed class RewardedShow
    {
        /// <summary>
        /// How long a close waits for a reward that may follow it. Long enough for a reward the
        /// network is still delivering as the activity finishes; short enough that a player who
        /// skipped is told so while still looking at the panel.
        /// </summary>
        public static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(3);

        readonly TaskCompletionSource<AdShowResult> _answer =
            new TaskCompletionSource<AdShowResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        readonly TimeSpan _grace;
        int _closed;
        volatile bool _left;

        public RewardedShow(AdImpression impression) : this(impression, CloseGrace) { }

        /// <summary>A show with its own grace, for tests that cannot wait three seconds.</summary>
        public RewardedShow(AdImpression impression, TimeSpan grace)
        {
            Impression = impression;
            _grace = grace < TimeSpan.Zero ? TimeSpan.Zero : grace;
        }

        /// <summary>The impression this show is for, carried back on its answer.</summary>
        public AdImpression Impression { get; }

        /// <summary>The one answer. Completes exactly once.</summary>
        public Task<AdShowResult> Answer => _answer.Task;

        /// <summary>True once the answer is known.</summary>
        public bool Settled => _answer.Task.IsCompleted;

        /// <summary>
        /// True when a reward arrived after the show had already been answered as dismissed - the
        /// grace was too short for this network. Read by the provider to say so in the log.
        /// </summary>
        public bool RewardedLate { get; private set; }

        /// <summary>The network says the video was watched to the end. Settles at once.</summary>
        public void Rewarded()
        {
            if (!_answer.TrySetResult(new AdShowResult(AdOutcome.Rewarded, Impression)))
                RewardedLate = _answer.Task.Result.Outcome == AdOutcome.Dismissed;
        }

        /// <summary>The video could not be shown. Settles at once; no reward can follow a failure.</summary>
        public void DisplayFailed(string message)
            => _answer.TrySetResult(AdShowResult.Failed(AdOutcome.Error, Impression, message));

        /// <summary>
        /// The video is gone from the screen. Answers dismissed only once the grace has passed with
        /// no reward; a second close changes nothing. Returns the wait, for a caller that wants to
        /// observe it (the provider forgets it through <c>Fire.AndForget</c>).
        /// </summary>
        public Task Closed()
        {
            if (Settled || System.Threading.Interlocked.Exchange(ref _closed, 1) == 1)
                return Task.CompletedTask;

            return DismissAfterGraceAsync();
        }

        /// <summary>
        /// The game lost focus while this show was up: the video's own screen took it.
        /// </summary>
        public void Left() => _left = true;

        /// <summary>
        /// The game has focus again after losing it to this show, so the video is no longer on
        /// screen - <b>the net under a close the SDK never raised</b>. Treated exactly as a close:
        /// the reward still has the whole grace to arrive, and a real close landing meanwhile
        /// changes nothing. Without it a missing callback would leave the panel on OPENING...
        /// with every key dead, a run nobody could finish or lose.
        ///
        /// <para>
        /// A return with no departure before it is ignored, so focus wobbling while the SDK is
        /// still opening the video cannot answer a show that has not been seen. Where the video
        /// takes no focus from the game (iOS presents it over the same activity), this never fires
        /// and the SDK's own close is the answer, as it always is there.
        /// </para>
        /// </summary>
        public Task Returned() => _left ? Closed() : Task.CompletedTask;

        async Task DismissAfterGraceAsync()
        {
            // Off the caller's context on purpose: nothing here touches Unity, and the answer's own
            // continuations are what return to the main thread.
            if (_grace > TimeSpan.Zero) await Task.Delay(_grace).ConfigureAwait(false);

            _answer.TrySetResult(AdShowResult.Failed(AdOutcome.Dismissed, Impression));
        }
    }
}
