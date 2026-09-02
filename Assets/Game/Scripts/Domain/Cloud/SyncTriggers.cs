using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// The player's own acts that are worth a sync of their own, wired once.
    ///
    /// <para>
    /// Everything else in the save reaches the server on the next background sync and nobody
    /// notices the delay. These are different in two ways. They are <em>deliberate</em> — a
    /// name, a purchase, a piece put down — so the player expects them to survive the next
    /// thing they do, which on a phone is quite often uninstalling the game; and backgrounding
    /// is the least reliable moment there is to start a network call, since the process is
    /// being frozen as it goes out. And two things the server derives are built from the
    /// pushed save rather than from the device — the public card and the name reservation —
    /// so until one of these has been pushed, what a stranger sees is the grove as it was.
    /// </para>
    /// <para>
    /// <b>Intents, never <c>Changed</c>.</b> Every ledger raises <c>Changed</c> when a save is
    /// loaded, and a save is loaded by every sync that adopts a merge — so a sync asked for on
    /// <c>Changed</c> is a sync every three seconds for the life of the process. The events
    /// here are raised only by the player's own act: <see cref="HomesteadLayout.Edited"/>,
    /// the three <c>Bought</c>s and <see cref="Wallet.ProfileChanged"/>. A new act that
    /// forgets to be listed here degrades gracefully — the change still goes up when the app
    /// is backgrounded — which is why this is a list and not a rule every call site has to
    /// remember.
    /// </para>
    /// <para>
    /// Cheap however often it fires: the request is debounced and coalesced by
    /// <see cref="SyncScheduler"/>, and an unchanged save sends nothing (invariant 11a).
    /// </para>
    /// </summary>
    public static class SyncTriggers
    {
        static bool _attached;

        /// <summary>Subscribes once. Safe to call again.</summary>
        public static void Attach()
        {
            if (_attached) return;
            _attached = true;

            Wallet.ProfileChanged += CloudSaveService.RequestSync;
            HomesteadLayout.Edited += CloudSaveService.RequestSync;
            HomesteadLedger.Bought += OnBought;
            GroveLand.Bought += OnBought;
            CompanionLedger.Bought += OnBought;
        }

        static void OnBought(HomesteadPiece piece) => CloudSaveService.RequestSync();
        static void OnBought(GroveRegion region) => CloudSaveService.RequestSync();
        static void OnBought(AvatarDefinition companion) => CloudSaveService.RequestSync();
    }
}
