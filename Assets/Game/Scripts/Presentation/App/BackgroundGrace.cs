using System;
using System.Threading;
using GlimmerGrove.Cloud;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlimmerGrove
{
    /// <summary>
    /// Asks the operating system for time to finish a departure after the app is backgrounded.
    ///
    /// <para>
    /// <b>iOS only, because iOS is the one that needs asking.</b> A departure is issued the moment
    /// the app is paused and finishes on the thread pool in well under a second on any working
    /// connection, but iOS suspends a backgrounded app within seconds and a slow connection can
    /// outlast that. <c>UIApplication beginBackgroundTask</c> is the platform's own answer - up
    /// to about thirty seconds, ended by us the moment the push lands or by the expiry handler if
    /// it never does - bound in <c>GlimmerBackgroundTask.mm</c> the way the share sheet and the
    /// sign-in sheets are. Android keeps a backgrounded process running for several seconds
    /// before freezing it and needs nothing, and the Editor never suspends at all; both answer
    /// null.
    /// </para>
    /// </summary>
    public sealed class BackgroundGrace : IBackgroundGrace
    {
#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern long GlimmerBeginBackgroundTask();

        [DllImport("__Internal")]
        static extern void GlimmerEndBackgroundTask(long task);
#endif

        public IDisposable Begin()
        {
#if UNITY_IOS && !UNITY_EDITOR
            long task = GlimmerBeginBackgroundTask();
            return task == 0 ? null : new Held(task);
#else
            return null;
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// One background task, ended exactly once from whichever thread finishes the
        /// departure. The native side is idempotent too, because its expiry handler may have
        /// ended the task first.
        /// </summary>
        sealed class Held : IDisposable
        {
            long _task;

            public Held(long task) => _task = task;

            public void Dispose()
            {
                long task = Interlocked.Exchange(ref _task, 0);
                if (task != 0) GlimmerEndBackgroundTask(task);
            }
        }
#endif
    }
}
