using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// A claim on a set of addresses. While it is alive they stay loaded; when it is disposed
    /// they are freed — unless somebody else still holds them.
    ///
    /// <para>
    /// <b>This replaces the named scope, and the difference is reference counting.</b> A scope
    /// was a process-global bag keyed by a string: one screen "opened" it and one screen
    /// "closed" it, and because two screens can legitimately want the same art at once, closing
    /// was never safe. The workarounds are worth listing, because all of them go away here — an
    /// address was claimed by the <em>first</em> scope to ask and silently shared by the second
    /// (so the second lost its art when the first closed); a marker interface
    /// <c>IDrawsGroveArt</c> existed so a leaving screen could ask whether the incoming one drew
    /// the same set; and that check read <c>Flow.Current</c>, inferring ownership from whoever
    /// happened to be on screen. Ownership is now <em>held</em> rather than inferred: whoever
    /// wants art takes a hold, and the art lives exactly as long as the last holder.
    /// </para>
    /// <para>
    /// <b>Ordering stops mattering.</b> Unity destroys the outgoing screen at the end of the
    /// frame, so the incoming one has already built and painted by the time the outgoing one
    /// releases. With counts, that is simply an acquire followed by a release and the count
    /// never reaches nought. The old shape got this right only because somebody wrote an essay
    /// about frame ordering into a method comment.
    /// </para>
    /// <para>
    /// <b>Disposing is not always freeing.</b> An address whose last hold goes away is put aside
    /// rather than released, and freed a few seconds later if nothing has asked for it again —
    /// see <see cref="AssetLibrary.GraceSeconds"/>. That is what keeps walking from the grove to
    /// its shop and back from being two full reloads.
    /// </para>
    /// </summary>
    public sealed class AssetHold : IDisposable
    {
        readonly HashSet<string> _held = new HashSet<string>(StringComparer.Ordinal);

        Task _loading;
        bool _disposed;

        internal AssetHold(string name) => Name = name ?? "hold";

        /// <summary>What this hold is for. Diagnostics only — nothing keys on it.</summary>
        public string Name { get; }

        /// <summary>How many addresses this hold is keeping alive.</summary>
        public int Count => _held.Count;

        /// <summary>
        /// True once the last <see cref="LoadAsync"/> has finished.
        ///
        /// <para>
        /// <b>Not "a load has started", which is the distinction that kept costing something.</b>
        /// The old <c>IsScopeLoaded</c> went true the instant a load began, so a second caller
        /// asked it, was told the art was ready, and painted a screenful of blanks. There is no
        /// way to ask that question wrongly here: this tracks the task, not the registration.
        /// </para>
        /// </summary>
        public bool IsLoaded => _loading != null && _loading.IsCompleted;

        /// <summary>
        /// Makes <paramref name="requests"/> exactly what this hold keeps alive: anything new is
        /// acquired and warmed, anything no longer wanted is let go.
        ///
        /// <para>
        /// A replacement rather than a reload, which is what lets a screen call it again
        /// whenever its own contents change — the grove re-runs it when a piece is placed, and
        /// pays for the one new piece rather than tearing down the floor and fetching it back.
        /// </para>
        /// </summary>
        public Task LoadAsync(IReadOnlyList<AssetRequest> requests,
                              IProgress<float> progress = null,
                              CancellationToken cancellation = default)
        {
            if (_disposed) return Task.CompletedTask;

            return _loading = AssetLibrary.FillHoldAsync(this, requests, progress, cancellation, replace: true);
        }

        /// <summary>
        /// Adds to what this hold keeps alive, without letting anything go.
        ///
        /// For a set that only ever grows while a screen is open. <see cref="LoadAsync"/> is the
        /// ordinary way in; this exists for the case where recomputing the whole set would be
        /// more work than naming the one thing that changed.
        /// </summary>
        public Task AddAsync(IReadOnlyList<AssetRequest> requests,
                             CancellationToken cancellation = default)
        {
            if (_disposed) return Task.CompletedTask;

            return _loading = AssetLibrary.FillHoldAsync(this, requests, null, cancellation, replace: false);
        }

        /// <summary>
        /// Takes an address <em>before</em> anything has loaded it, so a later synchronous fetch
        /// lands in something this hold owns rather than in the global set.
        ///
        /// <para>
        /// <b>The one case <see cref="LoadAsync"/> cannot serve.</b> The launch screen draws in
        /// the frame it is built, before the loader it is about to start has run at all, so its
        /// picture is fetched synchronously. Without this it would be treated as global art and
        /// stay resident for the life of the process — a full-screen texture for a screen shown
        /// exactly once.
        /// </para>
        /// </summary>
        public void Claim(string address)
        {
            if (_disposed || string.IsNullOrEmpty(address)) return;

            if (_held.Add(address)) AssetLibrary.Acquire(address, AssetKind.Sprite);
        }

        /// <summary>Lets go of everything. Safe to call twice.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (string address in _held) AssetLibrary.Let(address);
            _held.Clear();
        }

        // ------------------------------------------------------------- internals
        internal bool Take(string address, AssetKind kind)
        {
            if (!_held.Add(address)) return false;

            AssetLibrary.Acquire(address, kind);
            return true;
        }

        internal void Trim(HashSet<string> keep)
        {
            if (_held.Count == 0) return;

            List<string> going = null;

            foreach (string address in _held)
            {
                if (keep.Contains(address)) continue;

                going = going ?? new List<string>();
                going.Add(address);
            }

            if (going == null) return;

            foreach (string address in going)
            {
                _held.Remove(address);
                AssetLibrary.Let(address);
            }
        }
    }
}
