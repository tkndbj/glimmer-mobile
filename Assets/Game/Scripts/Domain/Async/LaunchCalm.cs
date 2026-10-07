using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Async
{
    /// <summary>
    /// The moment a launch has calmed: the hub has drawn, nothing is in front of it, and it
    /// has stood like that for a beat. Anything heavy that the player will not miss for a
    /// second waits here rather than racing the hub's first frame.
    ///
    /// <para>
    /// <b>The fault this answers.</b> The game stuttered on landing on the hub, on every launch,
    /// only with a connection, and only on iOS. The boot path itself was clean: content, art and
    /// the save all load asynchronously on the splash. What landed on the hub's first frames was
    /// the mediation stack - the consent refresh answering, LevelPlay starting, and with it AdMob
    /// (which brings WebKit up) and Unity Ads, and then six rewarded videos being fetched and
    /// pre-rendered into web views at once. Every one of those does its work on the main thread,
    /// and each of them only happens online, which is why aeroplane mode launched smoothly.
    /// </para>
    /// <para>
    /// The cure is <em>when</em>, not <em>whether</em>: the stack costs what it costs, and the
    /// one thing to choose is that it is paid on a frame nobody is watching for a lurch. The
    /// hub's idle beat is that frame. The offer buttons light up when readiness arrives
    /// (<c>RewardedAds.Changed</c>), so a player who taps an offer in the first second sees an
    /// honest "not loaded" rather than nothing - the same state a slow network already produces.
    /// </para>
    /// <para>
    /// Settled by <c>ConsentMoment</c>, which already measures the beat for the consent form, so
    /// the two share one definition of an idle hub. A Domain type because the thing that waits
    /// on it (<c>RewardedAds.StartAsync</c>) is in Domain, and because a test has to be able to
    /// hold it open and prove that mediation waits. Idempotent: once calm, calm for the process.
    /// </para>
    /// </summary>
    public static class LaunchCalm
    {
        static TaskCompletionSource<bool> _calm = Fresh();

        /// <summary>Whether the launch has calmed yet.</summary>
        public static bool IsSettled => _calm.Task.IsCompleted;

        /// <summary>Marks the launch as calm. Harmless to call again.</summary>
        public static void Settle() => _calm.TrySetResult(true);

        /// <summary>Completes when the launch has calmed, at once if it already has.</summary>
        public static Task WhenSettledAsync(CancellationToken cancellation = default)
        {
            if (!cancellation.CanBeCanceled || IsSettled) return _calm.Task;

            var waiting = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellation.Register(() => waiting.TrySetCanceled(cancellation));

            _calm.Task.ContinueWith(_ =>
            {
                registration.Dispose();
                waiting.TrySetResult(true);
            }, TaskContinuationOptions.ExecuteSynchronously);

            return waiting.Task;
        }

        /// <summary>Tests only: a new, uncalmed launch.</summary>
        internal static void Reset() => _calm = Fresh();

        static TaskCompletionSource<bool> Fresh()
            => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
