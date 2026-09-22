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
    /// so until one of these has been pushed, what a stranger sees is the keeper as they were.
    /// </para>
    /// <para>
    /// <b>Intents, never <c>Changed</c>.</b> Every ledger raises <c>Changed</c> when a save is
    /// loaded, and a save is loaded by every sync that adopts a merge — so a sync asked for on
    /// <c>Changed</c> is a sync every three seconds for the life of the process. The events
    /// here are raised only by the player's own act: <see cref="Wallet.ProfileChanged"/>,
    /// <see cref="CompanionLedger.Bought"/>, <see cref="EndlessLedger.Beaten"/> and
    /// <see cref="PlayerProgress.RecordChanged"/>. A new act that forgets to be listed here
    /// degrades gracefully — the change still goes up when the app is backgrounded — which is
    /// why this is a list and not a rule every call site has to remember.
    /// </para>
    /// <para>
    /// <b>A finished run is on the list since 2026-09-22</b>, and it used to be the textbook
    /// case for leaving something off it: a star is something the player keeps changing for
    /// another twenty minutes, so it could ride the background sync. What that missed is which
    /// moment the background sync runs at. It runs as the process is being frozen, which on
    /// Android is a push that may never complete, and the next thing a player does with a
    /// cleared glade is quite often pick up their other phone — where the glade then draws as
    /// unplayed, because the server was never told. A run is minutes long and the request is
    /// debounced, so the cost is one read and one small write per run while the phone is still
    /// awake, which is the cheapest moment there is to send anything.
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
            CompanionLedger.Bought += OnBought;

            // A new endless best is on the public card (`GroveCard.BestWave`), and since the
            // Grovement went it is the *only* thing that puts a keeper on a board
            // (`GrovePublishPolicy.WorthPublishing`) — so without this a keeper could hold out
            // further than anybody alive and never reach the board they did it for. `Beaten`
            // and never `Changed`: the latter fires on every save load, which is this type's
            // whole warning.
            EndlessLedger.Beaten += CloudSaveService.RequestSync;

            // A run recorded. `RecordChanged` is raised by `PlayerProgress.RecordRun` alone —
            // a load raises `Reloaded` instead — so this is the player's act and not a merge
            // arriving, which is the whole distinction this type exists to keep.
            PlayerProgress.RecordChanged += OnRunRecorded;
        }

        static void OnBought(AvatarDefinition companion) => CloudSaveService.RequestSync();

        static void OnRunRecorded(LevelRecord record) => CloudSaveService.RequestSync();
    }
}
