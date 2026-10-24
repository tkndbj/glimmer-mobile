using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Utilities;

namespace GlimmerGrove.Progression
{
    /// <summary>One utility the helping hand's gift holds.</summary>
    public readonly struct AssistItem
    {
        public readonly string Id;
        public readonly int Count;

        public AssistItem(string id, int count)
        {
            Id = id;
            Count = count;
        }
    }

    /// <summary>
    /// The helping hand (invariant 66): a player stuck on one level of the ladder is handed
    /// utilities after a loss, and the level is quietly eased after two.
    ///
    /// <para>
    /// <b>Content decides how many losses, how much and how often; code decides what a loss is and
    /// where the easing lands.</b> Every number here is the <c>assist</c> block of
    /// <c>progression.json</c>; the rule that only a <em>genuine</em> loss counts
    /// (<see cref="IsGenuine"/>), that the first chapter of the ladder gives the gift sooner, and that
    /// an eased run is graded as if it were not (<see cref="FullStrength"/>) are this type's.
    /// </para>
    /// <para>
    /// <b>Absent is the feature off</b>, and so is a block with a fault in it: a gift that cannot be
    /// granted must not be shown, and an easing nobody measured must not be applied.
    /// </para>
    /// </summary>
    public sealed class AssistTable
    {
        /// <summary>The most losses any threshold may ask for.</summary>
        public const int MaxLosses = 10;

        /// <summary>The largest easing a block may author, in per cent of raider health.</summary>
        public const int MaxEasePercent = 30;

        /// <summary>The most gifts or easings a block may allow in one day.</summary>
        public const int MaxPerDay = 5;

        /// <summary>The most utilities one gift may hold, by kind, and of each kind.</summary>
        public const int MaxItems = 4, MaxCount = 5;

        public static readonly AssistTable Empty = new AssistTable();

        readonly AssistItem[] _gift;

        AssistTable()
        {
            _gift = Array.Empty<AssistItem>();
        }

        AssistTable(AssistDto dto, AssistItem[] gift)
        {
            IsOn = true;
            OpeningLosses = dto.openingLosses;
            GiftLosses = dto.giftLosses;
            GiftsPerDay = dto.giftsPerDay;
            EaseLosses = dto.easeLosses;
            EasePercent = dto.easePercent;
            EasesPerDay = dto.easesPerDay;
            MinFelledPercent = dto.minFelledPercent;
            MinMatches = dto.minMatches;
            _gift = gift;
        }

        /// <summary>Whether the file carried a block that resolved. False is the feature off.</summary>
        public bool IsOn { get; }

        public int OpeningLosses { get; }
        public int GiftLosses { get; }
        public int GiftsPerDay { get; }
        public int EaseLosses { get; }
        public int EasePercent { get; }
        public int EasesPerDay { get; }
        public int MinFelledPercent { get; }
        public int MinMatches { get; }

        public IReadOnlyList<AssistItem> Gift => _gift;

        /// <summary>Whether a gift can be handed over at all.</summary>
        public bool Gives => IsOn && GiftsPerDay > 0 && _gift.Length > 0;

        /// <summary>Whether a level can be eased at all.</summary>
        public bool Eases => IsOn && EasesPerDay > 0 && EasePercent > 0;

        /// <summary>The losses in a row on one level that earn the gift there.</summary>
        public int LossesForGift(bool opening) => opening ? OpeningLosses : GiftLosses;

        /// <summary>
        /// Whether a lost run counts: it brought down a share of what the level sends and made a
        /// number of matches. A run left to fall on its own does neither, so walking away from a
        /// board to collect a gift is not a way of earning one.
        /// </summary>
        public bool IsGenuine(int felled, int raiders, int matches)
        {
            if (raiders <= 0 || felled < 0 || matches < MinMatches) return false;
            return (long)felled * 100 >= (long)raiders * MinFelledPercent;
        }

        /// <summary>
        /// The share of raider health an eased board keeps, in per cent; 100 when nothing is eased.
        /// </summary>
        public static int KeptPercent(int easePercent)
            => easePercent <= 0 ? 100 : 100 - Math.Min(easePercent, MaxEasePercent);

        /// <summary>
        /// What a run on an eased board is graded on: the matches it would have taken at full
        /// strength. Raiders that carry less health fall to proportionally fewer matches, so the run
        /// is scaled back up and the star lines mean the same thing on every board. Rounded up,
        /// toward the stricter grade.
        /// </summary>
        public static int FullStrength(int matches, int easePercent)
        {
            int kept = KeptPercent(easePercent);
            if (matches <= 0 || kept >= 100) return matches;
            return (int)(((long)matches * 100 + kept - 1) / kept);
        }

        /// <summary>
        /// Reads the optional <c>assist</c> block against the utility catalog. Never throws and never
        /// returns null: anything wrong is named in <paramref name="problems"/> and the table
        /// resolves to <see cref="Empty"/>.
        /// </summary>
        public static AssistTable Resolve(AssistDto dto, UtilityCatalog utilities, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;

            utilities ??= UtilityCatalog.Default;
            bool ok = true;

            void Range(string field, int value, int least, int most)
            {
                if (value >= least && value <= most) return;
                problems.Add($"assist.{field} is {value}; it must be {least} to {most}");
                ok = false;
            }

            Range("openingLosses", dto.openingLosses, 1, MaxLosses);
            Range("giftLosses", dto.giftLosses, 1, MaxLosses);
            Range("giftsPerDay", dto.giftsPerDay, 0, MaxPerDay);
            Range("easeLosses", dto.easeLosses, 1, MaxLosses);
            Range("easePercent", dto.easePercent, 1, MaxEasePercent);
            Range("easesPerDay", dto.easesPerDay, 0, MaxPerDay);
            Range("minFelledPercent", dto.minFelledPercent, 0, 100);
            Range("minMatches", dto.minMatches, 0, 1000);

            var rows = dto.gift ?? Array.Empty<AssistItemDto>();
            if (rows.Length == 0 && dto.giftsPerDay > 0)
            {
                problems.Add("assist.gift is empty while assist.giftsPerDay allows a gift; a gift has to hold something");
                ok = false;
            }
            if (rows.Length > MaxItems)
            {
                problems.Add($"assist.gift lists {rows.Length} utilities; at most {MaxItems} are supported");
                ok = false;
            }

            var gift = new List<AssistItem>(rows.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"assist.gift row {i}";

                if (row == null || string.IsNullOrEmpty(row.id))
                {
                    problems.Add($"{where} names no utility");
                    ok = false;
                    continue;
                }
                if (utilities.Find(row.id) == null)
                {
                    problems.Add($"{where} names utility '{row.id}', which the catalog does not hold");
                    ok = false;
                }
                if (!seen.Add(row.id))
                {
                    problems.Add($"{where} names '{row.id}' a second time; one row a utility");
                    ok = false;
                }
                if (row.count < 1 || row.count > MaxCount)
                {
                    problems.Add($"{where} hands over {row.count} '{row.id}'; it must be 1 to {MaxCount}");
                    ok = false;
                }

                gift.Add(new AssistItem(row.id, row.count));
            }

            return ok ? new AssistTable(dto, gift.ToArray()) : Empty;
        }
    }

    /// <summary>
    /// What this device remembers about the helping hand, for one account: the losses in a row per
    /// level, which levels are eased, what today has already handed over, and a gift still owed.
    ///
    /// <para>
    /// <b>Device-local and never in the save</b>, for <c>OfferLogDto</c>'s reason: it is a pacing
    /// hint that pays no currency, so a second device starting from nothing costs at most one more
    /// gift of utilities, and nothing here needs a schema bump, a rules release or a merge.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class AssistLogDto
    {
        public List<AssistLevelDto> levels = new List<AssistLevelDto>();

        /// <summary>The UTC day <see cref="giftsToday"/> counts, and how many gifts it handed over.</summary>
        public int giftDay, giftsToday;

        /// <summary>The UTC day <see cref="easesToday"/> counts, and how many levels it newly eased.</summary>
        public int easeDay, easesToday;

        /// <summary>The level a gift is owed for, or empty. Cleared only when the gift is granted.</summary>
        public string owed = string.Empty;
    }

    /// <summary>One level a player has lost on since last clearing it.</summary>
    [Serializable]
    public sealed class AssistLevelDto
    {
        public string id = string.Empty;

        /// <summary>Genuine losses since the level was last cleared.</summary>
        public int losses;

        /// <summary>The UTC day the level was eased, or nought while it is not.</summary>
        public int easedDay;

        /// <summary>When the level last lost, for evicting the oldest row first.</summary>
        public long lastUnix;
    }

    /// <summary>
    /// Every rule of the helping hand over one account's log, as pure functions of the log, the
    /// table and the day. <see cref="AssistLedger"/> only says which account and when.
    /// </summary>
    public sealed class AssistHistory
    {
        /// <summary>The most levels remembered at once; the oldest row nothing eases goes first.</summary>
        public const int MaxLevels = 32;

        readonly AssistLogDto _log;

        public AssistHistory(AssistLogDto log)
        {
            _log = log ?? new AssistLogDto();
            _log.levels ??= new List<AssistLevelDto>();
            _log.levels.RemoveAll(row => row == null || string.IsNullOrEmpty(row.id));
            _log.owed ??= string.Empty;
        }

        public AssistLogDto Log => _log;

        /// <summary>The level a gift is owed for, or empty.</summary>
        public string Owed => _log.owed;

        public bool GiftOwed => _log.owed.Length > 0;

        /// <summary>Genuine losses on this level since it was last cleared.</summary>
        public int LossesOn(string level) => Find(level)?.losses ?? 0;

        /// <summary>Whether this level is eased now.</summary>
        public bool IsEased(string level) => (Find(level)?.easedDay ?? 0) > 0;

        /// <summary>
        /// A genuine loss on a level of the ladder. Answers whether it earned the gift, which is then
        /// owed until <see cref="TakeGift"/> - at most <see cref="AssistTable.GiftsPerDay"/> a day,
        /// and never a second while one is still owed.
        /// </summary>
        public bool NoteLoss(string level, bool opening, AssistTable table, int today, long nowUnix)
        {
            if (string.IsNullOrEmpty(level) || table == null || !table.IsOn) return false;

            var row = Find(level) ?? Add(level);
            row.losses = Math.Min(row.losses + 1, 1000);
            row.lastUnix = Math.Max(row.lastUnix, nowUnix);

            if (!table.Gives || GiftOwed) return false;
            if (row.losses < table.LossesForGift(opening)) return false;
            int given = GiftsOn(today);
            if (given >= table.GiftsPerDay) return false;

            // Counted before the day is moved: read after it, the count would be yesterday's.
            _log.giftsToday = given + 1;
            _log.giftDay = today;
            _log.owed = level;
            return true;
        }

        /// <summary>A level was cleared: its losses and its easing are forgotten.</summary>
        public void NoteWin(string level)
        {
            if (string.IsNullOrEmpty(level)) return;
            _log.levels.RemoveAll(row => string.Equals(row.id, level, StringComparison.Ordinal));
        }

        /// <summary>
        /// How much this level's raiders give up on the run about to be dealt, in per cent; nought
        /// for none. A level becomes eased the first time it is asked about with enough losses
        /// behind it and today's allowance not spent, and stays eased until it is cleared - so an
        /// easing carried over from an earlier day is never counted against today.
        /// </summary>
        public int EaseFor(string level, AssistTable table, int today, out bool newlyEased)
        {
            newlyEased = false;
            if (string.IsNullOrEmpty(level) || table == null || !table.IsOn || table.EasePercent <= 0) return 0;

            var row = Find(level);
            if (row == null) return 0;
            if (row.easedDay > 0) return table.EasePercent;

            if (!table.Eases || row.losses < table.EaseLosses) return 0;
            int eased = EasesOn(today);
            if (eased >= table.EasesPerDay) return 0;

            // Counted before the day is moved, for `NoteLoss`'s reason.
            _log.easesToday = eased + 1;
            _log.easeDay = today;
            row.easedDay = today;
            newlyEased = true;
            return table.EasePercent;
        }

        /// <summary>The owed gift was granted. Answers the level it was owed for, or empty.</summary>
        public string TakeGift()
        {
            string level = _log.owed;
            _log.owed = string.Empty;
            return level;
        }

        int GiftsOn(int today) => _log.giftDay == today ? Math.Max(0, _log.giftsToday) : 0;

        int EasesOn(int today) => _log.easeDay == today ? Math.Max(0, _log.easesToday) : 0;

        AssistLevelDto Find(string level)
        {
            if (string.IsNullOrEmpty(level)) return null;
            foreach (var row in _log.levels)
                if (string.Equals(row.id, level, StringComparison.Ordinal)) return row;
            return null;
        }

        AssistLevelDto Add(string level)
        {
            while (_log.levels.Count >= MaxLevels) Evict();
            var row = new AssistLevelDto { id = level };
            _log.levels.Add(row);
            return row;
        }

        /// <summary>The oldest row that is not eased goes first; only a log full of eased rows gives up its oldest.</summary>
        void Evict()
        {
            int pick = -1;
            for (int pass = 0; pass < 2 && pick < 0; pass++)
                for (int i = 0; i < _log.levels.Count; i++)
                {
                    var row = _log.levels[i];
                    if (pass == 0 && row.easedDay > 0) continue;
                    if (pick < 0 || row.lastUnix < _log.levels[pick].lastUnix) pick = i;
                }

            if (pick >= 0) _log.levels.RemoveAt(pick);
            else _log.levels.Clear();
        }
    }
}
