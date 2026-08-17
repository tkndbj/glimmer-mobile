using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Persistence
{
    /// <summary>
    /// The record that a run is under way, so a run that never reaches an ending still
    /// costs what an ending costs.
    ///
    /// <para>
    /// <b>The problem it solves.</b> A heart is charged when a run is lost. Every way of
    /// ending a run <em>without</em> losing it was therefore free — the restart button, the
    /// back arrow, the pause menu's exits, and killing the app. That barely mattered while
    /// only the move budget could end a run, because running out of turns creeps up on a
    /// player. A countdown does not: it is a visible, reliable cue to tap restart one second
    /// before the loss lands, and a gate anybody can step around on a whim is not a gate.
    /// </para>
    /// <para>
    /// <b>Why a marker on disk rather than a flag in memory.</b> The deliberate exits can be
    /// handled in the screen that owns them, and are. This exists for the ending no screen
    /// ever sees: the process dying. A force-quit, an out-of-memory kill, a flat battery and
    /// a crash are indistinguishable from each other and from each other's intent — no client
    /// can tell them apart, and neither could a server. So the run is written down when it
    /// begins and rubbed out when it resolves; anything still written down at the next launch
    /// was a run that never finished, and is charged then.
    /// </para>
    /// <para>
    /// <b>Local, and deliberately not in the save file.</b> "A run is in flight" is a fact
    /// about <em>this device</em>, not about the account. In <c>SaveFileDto</c> it would be
    /// merged across devices — so a player mid-run on their phone would be charged on their
    /// tablet — and it would break invariant 11b outright, because it goes up and down and
    /// therefore cannot be joined. <see cref="PlayerPrefs"/> is the right home for it, the
    /// same as the legacy import keys.
    /// </para>
    /// <para>
    /// <b>None of it needs the network.</b> The marker is local, the charge is local, and the
    /// charge survives to the cloud because hearts are a ledger of what was produced and what
    /// was spent, merged by <c>max</c> — a spend made in airplane mode only ever raises
    /// <c>spent</c>, so it cannot be undone by a device that was not there when it happened.
    /// Going offline is not a way out of this.
    /// </para>
    /// </summary>
    public static class RunGuard
    {
        /// <summary>
        /// Where the marker lives. Prefixed like the rest of this project's keys and
        /// permanent: renaming it would strand one in-flight run per installed device, each
        /// of which would then never be charged.
        /// </summary>
        const string Key = "glimmer.run.inflight";

        /// <summary>
        /// The glade a previous launch left unfinished, once <see cref="Claim"/> has taken
        /// the heart for it. <see cref="LevelId.None"/> when the last launch ended cleanly.
        ///
        /// Held so the first screen can say so out loud. A resource that quietly decrements
        /// is a resource players feel cheated by later — the same rule the defeat panel's
        /// heart row is built on.
        /// </summary>
        public static LevelId Unfinished { get; private set; }

        /// <summary>True when a heart was actually taken for it, rather than there being none.</summary>
        public static bool UnfinishedWasCharged { get; private set; }

        /// <summary>
        /// Notes that a run has begun, and <b>flushes it to disk immediately</b>.
        ///
        /// The flush is the entire point and is not an optimisation to remove:
        /// <c>PlayerPrefs.SetString</c> writes to memory, and Unity persists that on a clean
        /// quit — which is exactly the exit this type does not care about. Without
        /// <c>PlayerPrefs.Save</c> the marker would be lost by the very crash it exists to
        /// catch.
        /// </summary>
        public static void Begin(LevelId level)
        {
            if (!level.IsValid) return;

            PlayerPrefs.SetString(Key, level.Value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Rubs the marker out. Called wherever a run reaches an ending — won, lost, or
        /// abandoned on purpose — because all three have already been paid for by then.
        ///
        /// Idempotent and cheap enough to call on every such path, which is what it should
        /// be: the failure mode of calling it twice is nothing, and the failure mode of
        /// missing one is a heart taken from a player who did nothing wrong.
        /// </summary>
        public static void Resolve()
        {
            if (!PlayerPrefs.HasKey(Key)) return;

            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Charges for a run left in flight by a previous launch, if there was one. Call once,
        /// after the save has loaded and before anything can start a new run.
        ///
        /// <para>
        /// The marker is cleared whether or not a heart was there to take. A player who was
        /// already at zero owes nothing — <c>TrySpendHeart</c> reports that as "already out"
        /// rather than as a refusal — and leaving the marker behind would charge them for the
        /// same run on some later launch that happened to find them solvent.
        /// </para>
        /// </summary>
        /// <returns>True when a run was outstanding, whether or not it could be charged.</returns>
        public static bool Claim()
        {
            Unfinished = LevelId.None;
            UnfinishedWasCharged = false;

            string raw = PlayerPrefs.GetString(Key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return false;

            Resolve();

            var level = LevelId.Parse(raw);
            if (!level.IsValid) return false;

            Unfinished = level;
            UnfinishedWasCharged = Wallet.TrySpendHeart();
            return true;
        }

        /// <summary>
        /// Forgets the outstanding run once it has been reported to the player, so a
        /// navigation back to the same screen does not say it twice.
        /// </summary>
        public static void NoteReported()
        {
            Unfinished = LevelId.None;
            UnfinishedWasCharged = false;
        }
    }
}
