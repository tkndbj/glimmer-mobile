using System;
using GlimmerGrove.Referral;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Keeps the referral state fresh for as long as the screen it is attached to is standing.
    ///
    /// <para>
    /// <b>The one thing on this account that no local event can announce.</b> Everything else a
    /// screen draws moves because something on this device moved it — a level cleared, a chest
    /// opened, a wallet credited — and each of those raises an event a screen can listen to.
    /// How many strangers typed your code, and how many of them cleared the chapter, are facts
    /// about <em>other people's</em> play (invariant 51). Nothing here fires when one changes;
    /// the ledger's own hook (<c>OnSettled</c>) is driven by this account's saves and says
    /// nothing about an invitee's. So a referrer sitting on the invite page watched a board
    /// that could not move, and a friend who had finished was only ever discovered by leaving
    /// the page and coming back.
    /// </para>
    /// <para>
    /// <b>A listener is the answer and the timer is the net.</b> The server bumps a counter in
    /// <c>players/{uid}/private/referral</c> whenever this account's referral state moves, and
    /// <see cref="ReferralLedger.Watch"/> holds a Firestore listener on it, so a friend binding
    /// or finishing reaches the page in the moment it happens rather than within half a minute.
    /// The timer stays behind it at a much longer interval, for the three things a listener
    /// cannot cover: a deployment whose server half does not bump the feed yet, a backend that
    /// cannot watch at all, and the gap between a backgrounding and the listener coming back.
    /// </para>
    /// <para>
    /// <b>Attached rather than written</b>, which is <c>WalletWatch</c>'s rule (invariant 44j)
    /// and for its reason: a listener's lifetime, a cadence, an offline test, a
    /// call-already-out test and a per-frame pump are five things to remember, and a rule five
    /// call sites have to remember is a rule four of them forget. A screen says <em>I am
    /// showing this</em> and nothing else.
    /// </para>
    /// <para>
    /// <b>Nothing outlives the screen.</b> The component dies with its host, and
    /// <see cref="OnDisable"/> releases the ledger's watch — so the listener is stopped by
    /// leaving the page, by the page being destroyed under a navigation, and by the object
    /// being switched off, whichever happens first. Backgrounding is the ledger's
    /// (<c>Paused</c>/<c>Resumed</c>, raised by <c>Boot</c>), because it is a fact about the
    /// app rather than about this page.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ReferralWatch : MonoBehaviour
    {
        IDisposable _watch;
        float _tick;

        /// <summary>
        /// Starts watching on behalf of <paramref name="host"/>. Idempotent, so a screen that
        /// restages itself does not end up holding two.
        /// </summary>
        public static ReferralWatch Attach(Component host)
        {
            if (host == null) return null;

            var watch = host.GetComponent<ReferralWatch>();
            if (watch == null) watch = host.gameObject.AddComponent<ReferralWatch>();

            // The first ask belongs to the page opening, not to the first tick of a timer. The
            // ledger's own freshness window is what stops this costing a call when something
            // else has just asked.
            ReferralLedger.Poke();
            watch._tick = 0f;

            return watch;
        }

        /// <remarks>
        /// <see cref="Attach"/> adds the component, which runs this before it returns — so the
        /// watch is taken here rather than there, and a host that is disabled and enabled again
        /// takes a fresh one rather than holding a stale handle across the gap.
        /// </remarks>
        void OnEnable()
        {
            _tick = 0f;
            _watch = _watch ?? ReferralLedger.Watch();
        }

        void OnDisable() => Release();

        /// <remarks>
        /// Belt and braces, and the same reasoning <c>WalletWatch</c> spells out:
        /// <c>OnDisable</c> runs ahead of <c>OnDestroy</c> on anything that was active, and
        /// <see cref="Release"/> is idempotent, so this only ever matters for a screen destroyed
        /// while its object was already off.
        /// </remarks>
        void OnDestroy() => Release();

        /// <remarks>
        /// A process being killed runs no <c>OnDestroy</c> on many platforms, and a native
        /// listener outliving its managed handle through a teardown is the one shape that
        /// crashes rather than leaks. Cheap insurance on the one callback Unity does promise.
        /// </remarks>
        void OnApplicationQuit() => Release();

        void Release()
        {
            var watch = _watch;
            _watch = null;
            watch?.Dispose();
        }

        void Update()
        {
            // The listener's callback may arrive on any thread, so all it does is set a flag.
            // This is the main thread, and this is where that flag becomes an ask.
            ReferralLedger.Pump();

            _tick += Time.unscaledDeltaTime;
            if (_tick < (ReferralLedger.IsWatching
                             ? ReferralLedger.WatchedPollSeconds
                             : ReferralLedger.PollSeconds)) return;

            // Reset whether or not the ask goes out, so a device held offline asks on the same
            // cadence rather than on every frame after the first window has passed.
            _tick = 0f;

            if (Net.Offline || ReferralLedger.IsBusy) return;
            ReferralLedger.Poke();
        }
    }
}
