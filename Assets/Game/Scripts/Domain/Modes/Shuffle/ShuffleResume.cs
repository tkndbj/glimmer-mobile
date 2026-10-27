using System;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// Where a suspended Shuffle run waits for its player (MODES.md 59s): one
    /// <see cref="ShuffleSnapshot"/> per account and level, on this device.
    ///
    /// <para>
    /// <b>Device-local, never in the save.</b> A run in progress is not something two phones can
    /// agree about (11b: a merge needs a monotonic value, and a run is not one), it reaches no
    /// server, and losing it costs at most the run - which is what closing the app cost before.
    /// So it costs no schema bump, no rules release and no write anybody pays for.
    /// </para>
    /// <para>
    /// <b>Per account</b>, for the reason the lane's daily tally is (<c>EndlessCoins</c>): a run
    /// belongs to whoever played it, so signing in as somebody else never resumes it or banks
    /// its waves into their save.
    /// </para>
    /// <para>
    /// <b>Flushed on every write</b> (<c>DevicePrefs</c>): the snapshot exists to survive the OS
    /// ending the process, which is exactly the ending in which an unflushed preference is lost.
    /// </para>
    /// <para>
    /// <b>Never throws.</b> The hub and the door ask it; an unreadable snapshot is reported,
    /// deleted and read as none, so a bad value can never stop the lane opening.
    /// </para>
    /// </summary>
    public static class ShuffleResume
    {
        public const string Key = "glimmer.shuffle.suspended";

        static EndlessCoins.ITallyStore _store = new PrefsStore();

        /// <summary>Keeps <paramref name="snapshot"/> as the run waiting on its level, replacing any before it.</summary>
        public static void Write(ShuffleSnapshot snapshot)
        {
            if (snapshot == null) return;

            try { _store.Write(Slot(snapshot.Level), snapshot.Encode()); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>The run waiting on <paramref name="level"/> for this account, or null.</summary>
        public static ShuffleSnapshot Read(LevelId level)
        {
            if (!level.IsValid) return null;

            try
            {
                string slot = Slot(level);
                string raw = _store.Read(slot);
                if (string.IsNullOrEmpty(raw)) return null;

                if (ShuffleSnapshot.TryDecode(raw, out var snapshot, out string fault))
                {
                    if (snapshot.Level == level) return snapshot;
                    fault = $"the snapshot is of {snapshot.Level.Value}";
                }

                // Written by a build whose shape was not this one, or damaged. Nothing can resume
                // it, and a refusal kept would be refused again on every visit to the hub.
                UnityEngine.Debug.LogWarning($"[Shuffle] a suspended run could not be read and was dropped: {fault}");
                ShuffleAnalytics.TrackDropped(level, fault);
                _store.Delete(slot);
                return null;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogException(e);
                return null;
            }
        }

        /// <summary>Whether a run is waiting on <paramref name="level"/> for this account.</summary>
        public static bool Holds(LevelId level) => Read(level) != null;

        /// <summary>The wave a waiting run reopens on, or nought when none is waiting.</summary>
        public static int WaveFor(LevelId level) => Read(level)?.Wave ?? 0;

        /// <summary>Lets go of the run waiting on <paramref name="level"/>, if any. Idempotent.</summary>
        public static void Clear(LevelId level)
        {
            if (!level.IsValid) return;

            try { _store.Delete(Slot(level)); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        /// <summary>One slot per level and account.</summary>
        static string Slot(LevelId level)
        {
            string uid = CloudState.UserId ?? string.Empty;
            string slot = Key + ":" + level.Value;
            return uid.Length == 0 ? slot : slot + ":" + uid;
        }

        // ------------------------------------------------------------------ the store
        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => UnityEngine.PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!UnityEngine.PlayerPrefs.HasKey(key)) return;
                UnityEngine.PlayerPrefs.DeleteKey(key);
                UnityEngine.PlayerPrefs.Save();
            }
        }

        /// <summary>Installs a store, or the game's own for <c>null</c>. For fixtures.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store) => _store = store ?? new PrefsStore();
    }
}
