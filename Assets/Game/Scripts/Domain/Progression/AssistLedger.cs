using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Utilities;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The helping hand on this device (invariant 66): which account, where its log is kept, and the
    /// three moments that move it - a run ending, a board being dealt, a gift being taken. Every rule
    /// is <see cref="AssistHistory"/>'s; every number is <see cref="AssistTable"/>'s.
    ///
    /// <para>
    /// <b>The ladder only</b>: a level of the live mode's main track (<see cref="Applies"/>). The
    /// Infinite and Shuffle lanes are graded on how far a run gets, so a loss there is the run, and
    /// the daily challenges never reach <see cref="RunLedger"/> at all.
    /// </para>
    /// <para>
    /// <b>The easing is never said.</b> Nothing a player can see reads <see cref="EasePercentFor"/>;
    /// the board deals its raiders a little lighter and the run is graded as if it had not
    /// (<see cref="AssistTable.FullStrength"/>), so a star means the same on every board.
    /// </para>
    /// </summary>
    public static class AssistLedger
    {
        /// <summary>The device-local key the log is kept under, one per account.</summary>
        public const string LogKeyPrefix = "glimmer.assist.log.";

        /// <summary>The log's name while no account is signed in yet (a first launch offline).</summary>
        const string NoAccount = "device";

        static string _account;
        static AssistHistory _history;
        static EndlessCoins.ITallyStore _store = new PrefsStore();

        /// <summary>Raised when a gift becomes owed or is taken.</summary>
        public static event Action Changed;

        public static AssistTable Table => ProgressionRules.Table.Assist;

        // ------------------------------------------------------------------ the account
        static string Account
        {
            get
            {
                string uid = CloudState.UserId;
                return string.IsNullOrEmpty(uid) ? NoAccount : uid;
            }
        }

        /// <summary>This account's log, loaded on first ask and again after a switch.</summary>
        static AssistHistory History
        {
            get
            {
                string account = Account;
                if (_history != null && account == _account) return _history;

                _account = account;
                _history = Load(account);
                return _history;
            }
        }

        static AssistHistory Load(string account)
        {
            AssistLogDto log = null;
            try
            {
                string text = _store.Read(LogKeyPrefix + account);
                if (!string.IsNullOrEmpty(text)) log = JsonUtility.FromJson<AssistLogDto>(text);
            }
            catch (Exception e)
            {
                // A pacing hint that will not read costs one gift's pacing, never a session.
                Debug.LogWarning("[Assist] the device's log did not read and starts afresh: " + e.Message);
            }
            return new AssistHistory(log);
        }

        static void Save()
        {
            if (_history == null || string.IsNullOrEmpty(_account)) return;
            try { _store.Write(LogKeyPrefix + _account, JsonUtility.ToJson(_history.Log)); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ which levels
        /// <summary>Whether a level is one the helping hand watches: the live mode's ladder.</summary>
        public static bool Applies(LevelId level)
        {
            if (!level.IsValid || !GameContent.IsLoaded) return false;

            var index = GameContent.Index;
            if (!index.Contains(level) || index.ModeOf(level) != GameMode.Siege) return false;
            return index.TrackOf(level).Equals(GameTrack.Main);
        }

        /// <summary>Whether a level is in the ladder's first chapter, where the gift comes sooner.</summary>
        static bool InOpening(LevelId level)
        {
            var chapters = GameContent.Index.ChaptersIn(GameMode.Siege, GameTrack.Main);
            return chapters.Count > 0 && chapters[0].Id.Equals(GameContent.ChapterOf(level));
        }

        // ------------------------------------------------------------------ what play says
        /// <summary>
        /// A run on a level ended. Called by <see cref="RunLedger"/> for every mode; anything off the
        /// ladder is ignored here. <paramref name="felled"/> of <paramref name="raiders"/> is how far
        /// the run got, and <paramref name="matches"/> what it spent - together they decide whether a
        /// loss was a genuine one.
        /// </summary>
        public static void NoteRun(LevelId level, bool won, int felled, int raiders, int matches)
        {
            try
            {
                var table = Table;
                if (!table.IsOn || !Applies(level)) return;

                var history = History;

                if (won)
                {
                    bool eased = history.IsEased(level.Value);
                    if (history.LossesOn(level.Value) == 0 && !eased) return;

                    history.NoteWin(level.Value);
                    Save();
                    if (eased) Telemetry.Track("assist_eased_clear", "level", level.Value);
                    return;
                }

                if (!table.IsGenuine(felled, raiders, matches)) return;

                long now = GameClock.NowUnix();
                bool owed = history.NoteLoss(level.Value, InOpening(level), table,
                                             DailyRules.DayKeyFor(now), now);
                Save();

                if (!owed) return;

                Telemetry.Track("assist_gift_owed", "level", level.Value,
                                "losses", history.LossesOn(level.Value));
                Raise();
            }
            catch (Exception e)
            {
                // A helping hand that throws must never cost the player the run it was told about.
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// How much the raiders of the run about to be dealt on this level give up, in per cent of
        /// their health; nought for none. Asked once per board a screen deals. A level newly eased
        /// spends one of today's easings.
        /// </summary>
        public static int EasePercentFor(LevelId level)
        {
            try
            {
                var table = Table;
                if (!table.IsOn || !Applies(level)) return 0;

                var history = History;
                int percent = history.EaseFor(level.Value, table, DailyRules.DayKeyFor(GameClock.NowUnix()),
                                              out bool newlyEased);
                if (newlyEased)
                {
                    Save();
                    Telemetry.Track("assist_eased", "level", level.Value, "percent", percent,
                                    "losses", history.LossesOn(level.Value));
                }
                return percent;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return 0;
            }
        }

        // ------------------------------------------------------------------ the gift
        /// <summary>Whether a gift is waiting to be taken.</summary>
        public static bool GiftOwed
        {
            get
            {
                try { return Table.Gives && History.GiftOwed; }
                catch (Exception e) { Debug.LogException(e); return false; }
            }
        }

        /// <summary>What the owed gift holds, or an empty list when none is owed.</summary>
        public static IReadOnlyList<AssistItem> OwedGift
            => GiftOwed ? Table.Gift : Array.Empty<AssistItem>();

        /// <summary>
        /// Takes the owed gift: every utility it holds lands in the stock, the save is written, and
        /// only then is the gift forgotten. Answers what actually landed (a utility the player holds
        /// as many of as the catalog allows lands nothing). A process killed between the two writes
        /// hands the gift over a second time rather than not at all - utilities are bounded and buy
        /// no grade (invariant 39), so that is the direction to err in.
        /// </summary>
        public static List<AssistItem> ClaimGift()
        {
            var landed = new List<AssistItem>();
            if (!GiftOwed) return landed;

            var history = History;
            foreach (var item in Table.Gift)
            {
                int got = UtilityLedger.Grant(item.Id, item.Count);
                if (got > 0) landed.Add(new AssistItem(item.Id, got));
            }

            SaveService.Save();

            string level = history.TakeGift();
            Save();

            int count = 0;
            foreach (var item in landed) count += item.Count;
            Telemetry.Track("assist_gift_claimed", "level", level, "items", count);

            Raise();
            return landed;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ the store
        sealed class PrefsStore : EndlessCoins.ITallyStore
        {
            public string Read(string key) => PlayerPrefs.GetString(key, string.Empty);
            public void Write(string key, string value) => DevicePrefs.WriteString(key, value);

            public void Delete(string key)
            {
                if (!PlayerPrefs.HasKey(key)) return;
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Installs a store and forgets everything held; the game's own for null. For fixtures.</summary>
        internal static void UseStore(EndlessCoins.ITallyStore store)
        {
            _store = store ?? new PrefsStore();
            _history = null;
            _account = null;
        }
    }
}
