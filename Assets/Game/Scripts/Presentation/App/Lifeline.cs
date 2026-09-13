using System;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Async;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// A cancellation token that trips when the object it is attached to is destroyed.
    ///
    /// <para>
    /// <b>A component rather than a base-class hook, and that is the whole design.</b> The
    /// obvious place for this is <c>OnDestroy</c> on <see cref="View"/> — and it cannot go
    /// there. Unity's messages are found by name on the most derived type, so a screen that
    /// declares its own <c>void OnDestroy()</c> <em>hides</em> the base's and the base's never
    /// runs; forty-odd screens here declare one. Making it <c>virtual</c> would work only for
    /// every screen somebody remembered to convert, and the ones nobody converted would fail
    /// silently — a token that never trips, which looks exactly like a token that was never
    /// needed. Unity dispatches <c>OnDestroy</c> to <em>each component</em> independently, so a
    /// component cannot be hidden, forgotten or overridden away.
    /// </para>
    /// <para>
    /// One per <c>GameObject</c>, fetched or added on demand. Anything sharing an object shares
    /// its life, which is exactly what "attached to the same object" means.
    /// </para>
    /// </summary>
    public sealed class Lifeline : MonoBehaviour
    {
        CancellationTokenSource _source;

        /// <summary>
        /// Cancelled the moment the host is destroyed. Read it before an <c>await</c> and hand
        /// it to whatever is being awaited.
        /// </summary>
        public CancellationToken Token
            => _source != null ? _source.Token : (_source = new CancellationTokenSource()).Token;

        /// <summary>True while the host is still there and has not been destroyed.</summary>
        public bool Living => this != null && (_source == null || !_source.IsCancellationRequested);

        /// <summary>
        /// The one on <paramref name="host"/>'s object, adding it if there is none.
        ///
        /// Answers null for a host that has already been destroyed, so a caller that has awaited
        /// something cannot resurrect one — <c>AddComponent</c> on a dead object throws, and it
        /// would be reviving the very lifetime this exists to end.
        /// </summary>
        public static Lifeline Of(MonoBehaviour host)
        {
            if (host == null) return null;

            var found = host.GetComponent<Lifeline>();
            return found != null ? found : host.gameObject.AddComponent<Lifeline>();
        }

        /// <summary>
        /// Starts work bound to this object's life: it is handed a token that trips on destroy,
        /// and anything it throws is reported rather than swallowed.
        /// </summary>
        public void Run(Func<CancellationToken, Task> work, string from)
        {
            if (work == null) return;

            var token = Token;
            Fire.AndForget(() => work(token), from);
        }

        void OnDestroy()
        {
            if (_source == null) return;

            _source.Cancel();
            _source.Dispose();
            _source = null;
        }
    }
}
