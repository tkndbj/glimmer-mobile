using System;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Makes a cancellation token mean something against an SDK that has never heard of one.
    ///
    /// <para>
    /// <b>The Firebase Unity SDK takes no <see cref="CancellationToken"/> anywhere.</b>
    /// <c>GetSnapshotAsync</c>, <c>SetAsync</c> and every callable have no overload that accepts
    /// one, so a token threaded carefully down from a screen used to arrive here and simply stop
    /// — which is worse than not having one, because every layer above looked cancellable and
    /// none of it was. That is not a reason to drop the parameter; it is a reason to say exactly
    /// what it buys.
    /// </para>
    /// <para>
    /// <b>Two things, and honestly only two.</b> A call that is <em>already</em> unwanted is
    /// never issued, which is the one that matters on a list somebody is scrolling — a
    /// leaderboard row tapped and left behind costs a document read per row otherwise. And a
    /// caller that has given up stops waiting, so its continuation runs now rather than whenever
    /// the network gets round to answering. What it cannot do is stop work the server has
    /// already been asked for: that read is paid for either way, and pretending otherwise in a
    /// comment is how the next person concludes cancellation is free.
    /// </para>
    /// </summary>
    public static class CloudCancel
    {
        /// <summary>
        /// Waits for <paramref name="work"/>, giving up as soon as the token trips.
        ///
        /// <para>
        /// The task is not abandoned — nothing here can abandon it — it is simply no longer
        /// awaited, so whatever it eventually returns falls on the floor. Any exception it
        /// throws afterwards is observed by the continuation below rather than left to surface
        /// as an unobserved task fault.
        /// </para>
        /// </summary>
        public static async Task<T> OrGiveUp<T>(Task<T> work, CancellationToken cancellation)
        {
            if (work == null) return default;
            if (!cancellation.CanBeCanceled) return await work;

            cancellation.ThrowIfCancellationRequested();

            var abandoned = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellation.Register(() => abandoned.TrySetResult(true)))
            {
                if (await Task.WhenAny(work, abandoned.Task).ConfigureAwait(false) != work)
                {
                    // Nobody is going to read this task's result, so nobody would notice it
                    // faulting either. Observed here so it cannot reach the runtime's
                    // unobserved-exception handler minutes later, under a stack that names
                    // whichever screen happens to be up.
                    Observe(work);
                    throw new OperationCanceledException(cancellation);
                }
            }

            return await work;
        }

        /// <summary>The same for work with no answer.</summary>
        public static async Task OrGiveUp(Task work, CancellationToken cancellation)
        {
            if (work == null) return;
            if (!cancellation.CanBeCanceled) { await work; return; }

            cancellation.ThrowIfCancellationRequested();

            var abandoned = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using (cancellation.Register(() => abandoned.TrySetResult(true)))
            {
                if (await Task.WhenAny(work, abandoned.Task).ConfigureAwait(false) != work)
                {
                    Observe(work);
                    throw new OperationCanceledException(cancellation);
                }
            }

            await work;
        }

        static void Observe(Task work)
            => work.ContinueWith(t => { _ = t.Exception; },
                                 CancellationToken.None,
                                 TaskContinuationOptions.OnlyOnFaulted
                                 | TaskContinuationOptions.ExecuteSynchronously,
                                 TaskScheduler.Default);
    }
}
