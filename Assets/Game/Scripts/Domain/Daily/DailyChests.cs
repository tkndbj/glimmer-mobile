using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Daily
{
    /// <summary>What the panel draws for one chest.</summary>
    public enum ChestState
    {
        /// <summary>Not enough runs yet, or an earlier chest is still waiting.</summary>
        Locked,

        /// <summary>Earned and unopened. This is the one that shines.</summary>
        Ready,

        Opened,
    }

    /// <summary>
    /// The daily chests: how much has been played today, how many chests that has earned,
    /// and what opening one pays.
    ///
    /// <para>
    /// The state is three integers — see <see cref="DailyStateDto"/> — and everything else
    /// is derived. In particular <b>no chest's contents are stored anywhere</b>. They are
    /// recomputed from the player, the day and the chest index whenever they are needed,
    /// which is what makes them identical on the home screen, in the overlay, after a
    /// crash, on a second device, and on the server. A stored prize would be a fourth
    /// copy of the truth and the one a player could edit.
    /// </para>
    /// <para>
    /// Currency awards do not touch the granted baseline. They are queued as identified
    /// entries for the server to confirm — see <see cref="CurrencyLedger.TryAward"/> — so
    /// the reward is spendable immediately and offline without this code ever doing the
    /// one thing the economy's security model forbids a client to do.
    /// </para>
    /// </summary>
    public static class DailyChests
    {
        static int _dayKey;
        static int _runs;
        static int _claimed;

        /// <summary>
        /// Raised when the counters move, including when a day rolls over on a read. The
        /// home panel follows this rather than rebuilding on a timer, because midnight
        /// arrives while a screen is open exactly as often as it arrives while it is not.
        /// </summary>
        public static event Action Changed;

        static DailyChestTable Table => ProgressionRules.Table.Daily;

        /// <summary>
        /// What the drops are seeded from.
        ///
        /// The account id, because that is the identity the server recomputes against —
        /// and, thanks to <see cref="CanOpen"/>, the only one a chest is ever rolled with
        /// when there is a server to disagree with. The device fallback is reached only
        /// when no cloud backend is configured, where nothing is adjudicated and the roll
        /// simply needs to be stable for this installation.
        /// </summary>
        static string PlayerKey
            => CloudState.IsSignedIn ? CloudState.UserId : CloudState.DeviceId;

        /// <summary>
        /// Whether a chest may be opened yet.
        ///
        /// <para>
        /// A chest's contents are seeded from the account id, because that is the one
        /// identifier the server can also compute from. Before the first sign-in there is
        /// no account id, so the client would roll against the device id while the server
        /// re-rolled against the uid — and the player would be shown one reward and given
        /// another. There is no way around that: the client cannot know the server's seed
        /// before it has ever spoken to the server.
        /// </para>
        /// <para>
        /// So the chest waits instead of lying. In practice this is invisible: anonymous
        /// sign-in fires from the splash screen, so an account id exists within seconds of
        /// a first launch that has any connection at all, and it is stored in the save
        /// from then on — every later session opens chests offline quite happily. The only
        /// player who ever sees the wait is one whose <em>very first</em> session has no
        /// network, and for them one connection unlocks it permanently.
        /// </para>
        /// <para>
        /// When no cloud backend is configured at all the gate lifts, because then nothing
        /// is ever adjudicated: the awards are never submitted, so there is no second
        /// opinion for the client's roll to disagree with.
        /// </para>
        /// </summary>
        public static bool CanOpen => CloudState.IsSignedIn || !CloudSaveService.IsAvailable;

        // ------------------------------------------------------------- reading
        public static int Runs { get { Sync(); return _runs; } }

        public static int Claimed { get { Sync(); return _claimed; } }

        public static int ChestCount => Table.ChestCount;

        public static int RunsPerChest => Table.RunsPerChest;

        /// <summary>Runs that earn every chest in the day. What the panel's bar measures.</summary>
        public static int RunsForAll => Table.RunsForAll;

        /// <summary>Runs needed before chest <paramref name="index"/> is earned.</summary>
        public static int RunsFor(int index) => Table.RunsFor(index);

        public static long SecondsUntilReset => DailyRules.SecondsUntilReset(GameClock.NowUnix());

        public static ChestState StateOf(int index)
        {
            Sync();

            if (index < _claimed) return ChestState.Opened;

            // Only the next unopened chest can be ready. The state is a count, so
            // "opened the third but not the first" is not a thing it can represent —
            // and making it representable would buy nothing a player wants.
            return index == _claimed && _runs >= Table.RunsFor(index)
                ? ChestState.Ready
                : ChestState.Locked;
        }

        /// <summary>True when there is a chest waiting to be tapped.</summary>
        public static bool HasReadyChest => StateOf(Claimed) == ChestState.Ready;

        /// <summary>
        /// Runs still to play before the next chest is earned, or 0 when one is already
        /// waiting or the day is finished.
        /// </summary>
        public static int RunsToNextChest
        {
            get
            {
                Sync();
                if (_claimed >= ChestCount) return 0;

                int needed = Table.RunsFor(_claimed) - _runs;
                return needed < 0 ? 0 : needed;
            }
        }

        public static bool DayComplete => Claimed >= ChestCount;

        /// <summary>
        /// What a chest holds, without opening it.
        ///
        /// Safe to call for a locked chest — and it is not called for one. Showing a
        /// player the prize before they have earned it removes the only reason to keep
        /// playing, which is the opposite of what the feature is for. It exists so the
        /// opening overlay and the collect animation read the same list.
        /// </summary>
        public static List<ChestDrop> Preview(int index)
        {
            Sync();
            return Table.Roll(PlayerKey, _dayKey, index);
        }

        /// <summary>The odds behind a chest's variable slot, for the panel's disclosure.</summary>
        public static ChestDefinition Definition(int index) => Table.Chest(index);

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Records a finished run: cleared or lost, any glade, any difficulty.
        ///
        /// <para>
        /// Counted on resolution rather than on entry, so a player cannot farm chests by
        /// opening and abandoning a glade nine times. Losing counts as much as winning —
        /// it costs a heart, which is the same price a win pays, and a daily loop that only
        /// rewards success punishes exactly the players who most need the hearts.
        /// </para>
        /// </summary>
        public static void RecordRun()
        {
            Sync();

            // Bounded so a very long session cannot grow the number without limit; there
            // is nothing above the last chest to earn.
            if (_runs >= Table.RunsForAll) return;

            _runs++;
            SaveService.Save();
            Raise();
        }

        /// <summary>
        /// Opens a chest and applies what it holds.
        ///
        /// <para>
        /// Two independent guards stop a chest paying twice, which is the failure that
        /// matters most here. The claimed counter refuses a second attempt at the same
        /// index; and every currency award carries an id derived from the day and the
        /// index, so even a save file edited to lower the counter collides with an entry
        /// that is already in the ledger, and the server refuses it a third time on top.
        /// </para>
        /// </summary>
        public static bool TryOpen(int index, out List<ChestDrop> drops)
        {
            drops = null;
            Sync();

            if (StateOf(index) != ChestState.Ready) return false;

            // Checked here as well as in the UI. A reward that the server would recompute
            // differently must not be openable through any path, and a guard that lives
            // only in a screen is a guard the next screen forgets. See CanOpen.
            if (!CanOpen) return false;

            var rolled = Table.Roll(PlayerKey, _dayKey, index);

            _claimed = index + 1;
            Apply(rolled, _dayKey, index);

            SaveService.Save();
            Raise();

            Telemetry.Track("daily_chest_opened",
                            "day", _dayKey,
                            "chest", index,
                            "runs", _runs,
                            "drops", Describe(rolled));

            drops = rolled;
            return true;
        }

        /// <summary>
        /// Hands out one chest's contents.
        ///
        /// Currency is queued for the server; hearts and boosts are applied here and now.
        /// The split is not arbitrary — currency is the thing that can be bought with real
        /// money and therefore the thing an attacker forges, while a heart is clamped at
        /// five and a boost expires. Adjudicating the first and trusting the second is
        /// where the security effort actually pays.
        /// </summary>
        static void Apply(List<ChestDrop> drops, int dayKey, int chestIndex)
        {
            long now = GameClock.NowUnix();

            for (int i = 0; i < drops.Count; i++)
            {
                var drop = drops[i];
                if (!drop.IsValid) continue;

                switch (drop.Kind)
                {
                    case ChestDropKind.Credits:
                    case ChestDropKind.Gems:
                        string currency = ChestDropKinds.CurrencyOf(drop.Kind);
                        PlayerProgression.Award(
                            currency, drop.Amount,
                            GrantEntry.DailyChestId(dayKey, chestIndex, currency),
                            GrantEntry.DailyChestReason, now);
                        break;

                    case ChestDropKind.Hearts:
                        Wallet.GrantHearts(drop.Amount);
                        break;

                    case ChestDropKind.HeartBoost:
                        Wallet.GrantHeartBoost(drop.Amount);
                        break;
                }
            }
        }

        static string Describe(List<ChestDrop> drops)
        {
            var parts = new string[drops.Count];
            for (int i = 0; i < drops.Count; i++) parts[i] = drops[i].ToString();
            return string.Join(",", parts);
        }

        // ------------------------------------------------------------ the day
        /// <summary>
        /// Rolls the counters over when the day has moved on.
        ///
        /// Lazy on purpose. A timer that fires at midnight has to survive a backgrounded
        /// app, a suspended process and a device asleep in a drawer, and it has to exist in
        /// every screen that shows a chest. A comparison at the top of every read has none
        /// of those problems and cannot be forgotten by a caller.
        ///
        /// <para>
        /// Unopened chests are lost at the boundary, and that is the design: a chest that
        /// banks is a chest nobody has to come back tomorrow for.
        /// </para>
        /// </summary>
        static void Sync()
        {
            int today = DailyRules.DayKeyFor(GameClock.NowUnix());
            if (today == _dayKey) return;

            // Only ever forward. GameClock already refuses to run backwards, so this
            // guards against a merged save carrying a day from a device whose clock was
            // ahead — resetting back into that day would hand out its chests again.
            if (today < _dayKey) return;

            _dayKey = today;
            _runs = 0;
            _claimed = 0;

            SaveService.MarkDirty();
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            var daily = dto?.daily;

            _dayKey = daily == null || daily.dayKey < 0 ? 0 : daily.dayKey;
            _runs = daily == null || daily.runs < 0 ? 0 : daily.runs;
            _claimed = daily == null || daily.claimed < 0 ? 0 : daily.claimed;

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.daily = new DailyStateDto
            {
                dayKey = _dayKey,
                runs = _runs,
                claimed = _claimed,
            };
        }

        /// <summary>
        /// Joins two devices' days.
        ///
        /// <para>
        /// The later day wins outright — an older day's counters describe a day that is
        /// over, and carrying its runs forward would hand the player a head start on a day
        /// they have not played. Within the same day both counts take the <em>larger</em>
        /// value, which is the opposite of the rule hearts use, and for a specific reason:
        /// runs and claims are not consumable resources that two devices could mint by
        /// refilling each other. They are records of things that happened, the awards
        /// behind them are deduplicated by their own ids, and taking the smaller claim
        /// count would let a player open a chest they had already been paid for.
        /// </para>
        /// </summary>
        internal static DailyStateDto Join(DailyStateDto mine, DailyStateDto other)
        {
            if (mine == null && other == null) return new DailyStateDto();
            if (mine == null) return Copy(other);
            if (other == null) return Copy(mine);

            if (mine.dayKey > other.dayKey) return Copy(mine);
            if (other.dayKey > mine.dayKey) return Copy(other);

            return new DailyStateDto
            {
                dayKey = mine.dayKey,
                runs = Math.Max(mine.runs, other.runs),
                claimed = Math.Max(mine.claimed, other.claimed),
            };
        }

        static DailyStateDto Copy(DailyStateDto d)
            => new DailyStateDto { dayKey = d.dayKey, runs = d.runs, claimed = d.claimed };

        /// <summary>Forgets today. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _dayKey = 0;
            _runs = 0;
            _claimed = 0;
        }
    }
}
