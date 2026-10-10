using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// The player's own acts that are worth a sync of their own, wired once.
    ///
    /// <para>
    /// Everything else the save moves is owed too, and reaches the server within
    /// <see cref="SyncScheduler.AmbientSeconds"/> or when the player leaves, whichever is
    /// first (<see cref="SaveService.Written"/> and <c>CloudSaveService.Depart</c>, both
    /// below). These are different in two ways. They are <em>deliberate</em> - a
    /// name, a purchase, a piece put down - so the player expects them to survive the next
    /// thing they do, which on a phone is quite often uninstalling the game; and backgrounding
    /// is the least reliable moment there is to start a network call, since the process is
    /// being frozen as it goes out. And two things the server derives are built from the
    /// pushed save rather than from the device - the public card and the name reservation -
    /// so until one of these has been pushed, what a stranger sees is the keeper as they were.
    /// </para>
    /// <para>
    /// <b>Intents, never <c>Changed</c>.</b> Every ledger raises <c>Changed</c> when a save is
    /// loaded, and a save is loaded by every sync that adopts a merge - so a sync asked for on
    /// <c>Changed</c> is a sync every three seconds for the life of the process. The events
    /// here are raised only by the player's own act: <see cref="Wallet.ProfileChanged"/>,
    /// <see cref="CompanionLedger.Bought"/>, <see cref="EndlessLedger.Beaten"/>,
    /// <see cref="PlayerProgress.RecordChanged"/>, <see cref="PlayerProgression.Spent"/> and
    /// <see cref="PlayerProgression.Awarded"/> - and <see cref="SaveService.Written"/>, which a
    /// merge being adopted never raises. A new act that forgets to be listed here degrades
    /// gracefully - the write still reads as owed and goes up within the minute - which is why
    /// this is a list and not a rule every call site has to remember.
    /// </para>
    /// <para>
    /// <b>A finished run is on the list since 2026-09-22</b>, and it used to be the textbook
    /// case for leaving something off it: a star is something the player keeps changing for
    /// another twenty minutes, so it could ride the background sync. What that missed is which
    /// moment the background sync runs at. It runs as the process is being frozen, which on
    /// Android is a push that may never complete, and the next thing a player does with a
    /// cleared glade is quite often pick up their other phone - where the glade then draws as
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
            // (`GrovePublishPolicy.WorthPublishing`) - so without this a keeper could hold out
            // further than anybody alive and never reach the board they did it for. `Beaten`
            // and never `Changed`: the latter fires on every save load, which is this type's
            // whole warning.
            EndlessLedger.Beaten += CloudSaveService.RequestSync;

            // A new Shuffle best is on the card too (`GroveCard.ShuffleWave`), for the same
            // reason and through the same intent-only event.
            GlimmerGrove.Shuffle.ShuffleLedger.Beaten += CloudSaveService.RequestSync;

            // A run recorded. `RecordChanged` is raised by `PlayerProgress.RecordRun` alone -
            // a load raises `Reloaded` instead - so this is the player's act and not a merge
            // arriving, which is the whole distinction this type exists to keep.
            PlayerProgress.RecordChanged += OnRunRecorded;

            // Anything bought with credits or gems, since 2026-09-28. A streak shield bought on
            // one phone was missing on the other: the shield's date lives only in the save, the
            // purchase asked for no sync, and the background push it rode is the one Android may
            // never finish - the finished run's failure above, said of money. The season pass,
            // turrets, keeper levels, challenge deals and utilities had the same gap. Hung on
            // the one spend door rather than on each ledger, so the next purchase is covered too.
            PlayerProgression.Spent += CloudSaveService.RequestSync;

            // And anything collected: a task or season chest, a streak night, a cleared
            // challenge, the Infinite lane's waves. A claim reaches the server only through the
            // sync that submits it, so the other device's balance waits on this.
            PlayerProgression.Awarded += CloudSaveService.RequestSync;

            // The net under everything above, since 2026-09-28. Every persistent change passes
            // through one door, and a merge arriving from the cloud does not - so asking there
            // whether this device now owes the server anything is complete by construction and
            // cannot loop. What it catches is whatever nobody listed: a loadout, a lesson seen,
            // a heart spent, a challenge play. It waits the ambient minute rather than the
            // debounce, because it is not a deliberate act; leaving the app sends it at once
            // (`CloudSaveService.Depart`).
            SaveService.Written += OnWritten;
        }

        static void OnWritten(SaveFileDto file)
        {
            if (CloudSaveService.Owes(file)) CloudSaveService.RequestSyncEventually();
        }

        static void OnBought(AvatarDefinition companion) => CloudSaveService.RequestSync();

        static void OnRunRecorded(LevelRecord record) => CloudSaveService.RequestSync();
    }
}
