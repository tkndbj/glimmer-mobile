using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace GlimmerGrove.Async
{
    /// <summary>
    /// Starts work that nothing is going to await, and makes sure it cannot fail in silence.
    ///
    /// <para>
    /// <b>This exists to get rid of <c>async void</c>.</b> An <c>async void</c> method has no
    /// task to observe, so an exception that escapes it does not surface anywhere a player or a
    /// log will show — it is swallowed by the synchronisation context. Every one of them in this
    /// project was therefore wrapped in a hand-written <c>try/catch</c> that logs, and the rule
    /// "remember to wrap it" is the shape this codebase has paid for repeatedly: the two that
    /// forgot were a scope that never loaded and a grant that never landed, both completely
    /// quiet. An <c>async Task</c> handed to this cannot forget, because the catch is here.
    /// </para>
    /// <para>
    /// <b>Cancellation is not a failure.</b> A screen closing mid-load, a visit superseded by
    /// the next one, a sync abandoned because the account changed — all three are the ordinary
    /// end of a piece of work, and logging them would teach everyone to ignore the log. So
    /// <see cref="OperationCanceledException"/> is dropped and everything else is reported with
    /// the name of whatever started it.
    /// </para>
    /// <para>
    /// <b>For code with no lifetime of its own.</b> Anything living on a screen should use
    /// <c>View.Run</c>, which is this plus a token that trips when the screen goes.
    /// </para>
    /// </summary>
    public static class Fire
    {
        /// <summary>
        /// Runs <paramref name="work"/> and forgets about it, reporting anything that goes wrong
        /// against <paramref name="from"/>.
        /// </summary>
        public static void AndForget(Func<Task> work, string from)
        {
            if (work == null) return;

            _ = GuardAsync(work, from);
        }

        /// <summary>
        /// The same for a task that has already been started — the shape a caller ends up with
        /// when the work needs arguments it captured itself.
        /// </summary>
        public static void AndForget(Task started, string from)
        {
            if (started == null) return;

            _ = GuardAsync(() => started, from);
        }

        static async Task GuardAsync(Func<Task> work, string from)
        {
            try
            {
                var task = work();
                if (task != null) await task;
            }
            catch (OperationCanceledException)
            {
                // The ordinary end of abandoned work. See the type's remarks.
            }
            catch (Exception e)
            {
                Debug.LogError($"[{from ?? "async"}] {e.GetType().Name}: {e.Message}");
                Debug.LogException(e);
            }
        }
    }
}
