using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Every field of the save is wired to the cloud in all three places, or says why not.
    ///
    /// <para>
    /// <b>The one way a synced game still loses data is a field somebody forgot.</b> A field
    /// the Firestore mapper does not carry never reaches the server; one <see cref="SaveDelta"/>
    /// does not compare is never pushed on its own (the sync reads "nothing changed"); and one
    /// <see cref="SaveMerge.Join"/> does not copy is <em>erased</em> by the next sync on every
    /// device, which is the worst of the three because it destroys what the player already had.
    /// Each has happened here once: <c>groveLandOwned</c> reached the delta and never the mapper
    /// (v17), a season row compared one field of four (47o), and every fixture passed each time
    /// because a hand-written fixture only checks the fields its author remembered.
    /// </para>
    /// <para>
    /// <b>So nothing here is written by hand.</b> <see cref="Full"/> builds a save by reflection
    /// in which every field at every depth - every row of every list included - holds a value
    /// that is not its default, and each rule is asked of every one of those fields by path. A
    /// field added to <see cref="SaveFileDto"/> or to anything under it is therefore checked the
    /// day it is written, and fails until it is wired or it is named in one of the lists below
    /// <b>with its reason</b>. Those lists are the whole of what is deliberately not synced, and
    /// a new line in one is a decision a reviewer can see.
    /// </para>
    /// <para>
    /// Offline, pure over the data: no store, no backend, no Unity.
    /// </para>
    /// </summary>
    public sealed class SaveWiringTests
    {
        // ============================================================ the exemptions
        // A path is a field name, dotted into sections, with [] for "every row of this list".
        // An entry covers the field it names and everything under it.

        /// <summary>Fields the Firestore mapper deliberately does not carry.</summary>
        static readonly Dictionary<string, string> NotOnTheWire = new Dictionary<string, string>
        {
            ["wallet.currencies"] = "the currency ledgers are server-owned and travel only as submissions (CloudWireTests.CurrencyLedgersAreNotUploadedWithTheSave)",
            ["wallet.coins"] = "the retired v1 balance mirror; read back as -1, 'not carried' (CurrencyLedgersAreNotUploadedWithTheSave)",
            ["wallet.gems"] = "the retired v1 balance mirror, for the same reason",
        };

        /// <summary>Fields <see cref="SaveDelta"/> deliberately does not compare.</summary>
        static readonly Dictionary<string, string> NotADifference = new Dictionary<string, string>
        {
            ["updatedUnix"] = "moves on every local write; comparing it would make every sync a write (SaveDelta.ScalarsDiffer)",
            ["checksum"] = "moves on every local write, for the same reason",
            ["cloud.revision"] = "moves on every snapshot, for the same reason",
            ["cloud.lastSyncedUnix"] = "this device's own record of its last sync; it moves on every sync and means nothing to another device",
            ["cloud.deviceId"] = "names the handset that wrote the file, not anything the player did",
            ["wallet.hearts"] = "a count derived from the heart ledger, which is compared instead (heartsProduced / heartsSpent / heartsDueUnix)",
            ["wallet.heartsNextRefillUnix"] = "the pre-v8 deadline beside that count, derived from the ledger for the same reason",
            ["wallet.currencies"] = "not on the wire; a waiting wallet entry is owed through CloudSaveService.Owes (CloudDepartureTests)",
            ["wallet.coins"] = "not on the wire",
            ["wallet.gems"] = "not on the wire",
        };

        /// <summary>Fields a merge deliberately does not keep from a side that knows them.</summary>
        static readonly Dictionary<string, string> NotKeptByAMerge = new Dictionary<string, string>
        {
            ["cloud.revision"] = "a join is max + 1 by design, so the merged file is newer than either side (optimistic concurrency)",
            ["checksum"] = "stamped by SaveStore on every write, so a merged file is checksummed as itself rather than as either side",
        };

        static bool Exempt(Dictionary<string, string> list, string path)
            => list.Keys.Any(key => path == key || path.StartsWith(key + ".", StringComparison.Ordinal)
                                                || path.StartsWith(key + "[]", StringComparison.Ordinal));

        // =============================================================== the rules
        /// <summary>
        /// Every field survives the journey to Firestore and back. The rules half of the same
        /// wire - that every key the mapper writes is allowed by <c>hasOnly</c> - is
        /// <c>CloudWireTests.EveryFieldTheClientWritesIsAllowedByTheSecurityRules</c>.
        /// </summary>
        [Test]
        public void EveryFieldSurvivesTheWire()
        {
            var full = Full();
            var back = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(full));

            var lost = Leaves(full)
                .Where(path => !Exempt(NotOnTheWire, path))
                .Where(path => !SameAt(full, back, path))
                .ToList();

            Assert.IsEmpty(lost, "these save fields do not survive FirestoreSaveMapper - wire them (and "
                                 + "firestore.rules' hasOnly, 12a) or add them to NotOnTheWire with a reason:\n  "
                                 + string.Join("\n  ", lost.Select(p => p + "  sent " + Show(ValueAt(full, p))
                                                                      + ", read back " + Show(ValueAt(back, p)))));
        }

        /// <summary>
        /// Changing any one field is something the sync sees. A field the delta ignores is
        /// pushed only when something else happens to change beside it.
        /// </summary>
        [Test]
        public void EveryFieldIsADifferenceTheSyncSees()
        {
            var full = Full();

            var unseen = Leaves(full)
                .Where(path => !Exempt(NotADifference, path))
                .Where(path => SaveDelta.Between(full, Changed(full, path)).IsEmpty)
                .ToList();

            Assert.IsEmpty(unseen, "SaveDelta reads a change to these fields as nothing to send - compare them "
                                   + "in SaveDelta or add them to NotADifference with a reason:\n  "
                                   + string.Join("\n  ", unseen));
        }

        /// <summary>
        /// A merge never erases what one side knows, asked three ways, because each reaches a
        /// different part of <see cref="SaveMerge.Join"/>.
        ///
        /// <para>
        /// Against a file with no sections at all (a fresh install, an account with no
        /// document), most section joins take the "the other side knows nothing" shortcut and
        /// hand back a copy - so that case alone never runs the field-by-field join, and a join
        /// that forgot the shield's date passed it (found by mutation when this test was
        /// written). Against the <em>same</em> file on both devices, and against a file whose
        /// sections exist and hold nothing, every field goes through the real join: the first
        /// is the merge two synced devices do on every foreground, the second a device that
        /// has played but not this. A field the join forgets comes back as its default there.
        /// </para>
        /// </summary>
        [Test]
        public void NoFieldIsErasedByAMerge()
        {
            var full = Full();
            var empty = new SaveFileDto();
            var hollow = (SaveFileDto)Hollow(typeof(SaveFileDto));

            var erased = new List<string>();
            foreach (var (label, merged) in new[]
            {
                ("against no sections, known here", SaveMerge.Join(full, empty)),
                ("against no sections, known there", SaveMerge.Join(empty, full)),
                ("the same file on both devices", SaveMerge.Join(full, (SaveFileDto)Clone(full))),
                ("against empty sections, known here", SaveMerge.Join(full, hollow)),
                ("against empty sections, known there", SaveMerge.Join(hollow, full)),
            })
            {
                erased.AddRange(Leaves(full)
                    .Where(path => !Exempt(NotKeptByAMerge, path))
                    .Where(path => !SameAt(full, merged, path))
                    .Select(path => path + "  (" + label + ": had " + Show(ValueAt(full, path))
                                    + ", merged " + Show(ValueAt(merged, path)) + ")"));
            }

            Assert.IsEmpty(erased, "SaveMerge.Join loses these fields - join them or add them to "
                                   + "NotKeptByAMerge with a reason:\n  " + string.Join("\n  ", erased));
        }

        /// <summary>
        /// What a sync agrees with the server is what the device then holds.
        ///
        /// <para>
        /// <b>The loop this holds shut.</b> A sync records the merged file as agreed
        /// (<c>CloudSaveService.Agree</c>) and adopts it; every later write asks whether the
        /// device's file differs from the agreed one (<c>Owes</c>) and, if so, syncs within a
        /// minute. A ledger that <em>changed</em> the file as it loaded it - capped a list the
        /// merge did not, dropped a row it does not recognise, re-derived a field - would leave
        /// the device permanently different from what it agreed, so every write would owe a sync,
        /// every sync would pull the server's copy back and "learn" the dropped rows again
        /// (<c>CloudSaveService.Learned</c>), and every idle player would cost a read and a
        /// callable a minute for ever while every screen listening redrew itself. So adopting a
        /// merged file and snapshotting it straight back must change nothing a push carries.
        /// </para>
        /// </summary>
        [Test]
        public void TheDeviceHoldsWhatASyncAgreed()
        {
            SaveService.Unload();
            EndlessCoins.UseStore(new EndlessCoins.MemoryStore());
            SaveService.LoadWith(new AccountSwitchTests.MemoryStore(), new AccountSwitchTests.MemoryArchive());

            try
            {
                var full = Full();
                var merged = SaveMerge.Join(full, (SaveFileDto)Clone(full));

                // The refill clocks are due in the future, as a live file's are at the moment of
                // a sync. Left in 2023 the ledgers would catch up on load and produce a heart -
                // which is a real event rather than drift, and is correctly owed to the server
                // once; a test asking "does adopting change anything" must not be answered by
                // the clock.
                long soon = SaveSchema.NowUnix() + 3600;
                merged.wallet.heartsDueUnix = soon;
                merged.wallet.hintsDueUnix = soon;

                SaveService.Adopt(merged);
                var held = SaveService.Snapshot();

                var drift = Leaves(merged)
                    .Where(path => !Exempt(NotADifference, path))
                    .Where(path => !SameAt(merged, held, path))
                    .Select(path => path + "  (agreed " + Show(ValueAt(merged, path))
                                    + ", held " + Show(ValueAt(held, path)) + ")")
                    .ToList();

                Assert.IsTrue(SaveDelta.Between(merged, held).IsEmpty,
                              "adopting the merged file changed what a push carries, so every write "
                              + "after a sync owes another one:\n  " + string.Join("\n  ", drift));
            }
            finally
            {
                SaveService.Unload();
                EndlessCoins.UseStore(null);
            }
        }

        /// <summary>
        /// The exemptions name real fields. A list entry left behind by a rename is an exemption
        /// for nothing - and a field that later takes the name would be exempt on arrival.
        /// </summary>
        [Test]
        public void EveryExemptionNamesAFieldThatExists()
        {
            var paths = new HashSet<string>(Leaves(Full()));

            var stale = NotOnTheWire.Keys.Concat(NotADifference.Keys).Concat(NotKeptByAMerge.Keys)
                                    .Where(key => !paths.Any(path => path == key
                                        || path.StartsWith(key + ".", StringComparison.Ordinal)
                                        || path.StartsWith(key + "[]", StringComparison.Ordinal)))
                                    .Distinct()
                                    .ToList();

            Assert.IsEmpty(stale, "these exemptions name no field of the save: " + string.Join(", ", stale));
        }

        /// <summary>
        /// The generator is the test's foundation, so it is held too: every field it reaches holds
        /// a value that is not its default. A field left at its default would read as "survived"
        /// through a mapper that dropped it.
        /// </summary>
        [Test]
        public void TheFullSaveLeavesNoFieldAtItsDefault()
        {
            var full = Full();
            var blank = Leaves(full).Where(path => IsDefault(ValueAt(full, path))).ToList();

            Assert.IsEmpty(blank, "Full() left these at their default: " + string.Join(", ", blank));
            Assert.Greater(Leaves(full).Count(), 60, "the walker reached suspiciously few fields");
        }

        // ============================================================ the full save
        /// <summary>
        /// A save with every field set to a value that is distinct, not a default, and valid for
        /// what reads it. Most fields take a counter; the ones whose readers normalise a value
        /// (a flag that is one of two states, a schema version, a list the writer sorts) take a
        /// value those readers accept, so a normalisation is never mistaken for a loss.
        /// </summary>
        static SaveFileDto Full()
        {
            var dto = (SaveFileDto)Fill(typeof(SaveFileDto), "");

            dto.schemaVersion = SaveSchema.Version;
            dto.legacyImportDone = true;
            return dto;
        }

        static int _counter;

        /// <summary>
        /// A save whose every section exists and holds nothing: each class built, each list
        /// empty, each field at its default. What a device that has played, but never touched a
        /// given feature, carries for it.
        /// </summary>
        static object Hollow(Type type)
        {
            if (IsLeaf(type)) return type == typeof(string) ? string.Empty : Activator.CreateInstance(type);
            if (type.IsArray) return Array.CreateInstance(type.GetElementType(), 0);

            var instance = Activator.CreateInstance(type);
            foreach (var field in Fields(type)) field.SetValue(instance, Hollow(field.FieldType));
            return instance;
        }

        static object Fill(Type type, string path)
        {
            if (type == typeof(int)) return Valid(path) ?? (object)(100 + ++_counter);
            if (type == typeof(long)) return Valid(path) ?? (object)(1_700_000_000L + ++_counter);
            if (type == typeof(bool)) return true;
            if (type == typeof(string)) return Valid(path) ?? ("v" + ++_counter);

            if (type == typeof(StoredFlag)) return StoredFlag.From(false);   // the non-default state

            if (type.IsArray)
            {
                var element = type.GetElementType();
                var array = Array.CreateInstance(element, 1);
                array.SetValue(Fill(element, path + "[]"), 0);
                return array;
            }

            var instance = Activator.CreateInstance(type);
            foreach (var field in Fields(type))
                field.SetValue(instance, Fill(field.FieldType, Join(path, field.Name)));
            return instance;
        }

        /// <summary>
        /// Values some reader constrains. Grown only when a rule above fails on a normalisation
        /// rather than on a loss - never to make a real gap go away.
        /// </summary>
        static object Valid(string path)
        {
            switch (path)
            {
                case "schemaVersion": return SaveSchema.Version;

                // A ledger the reader rebuilds so that nothing is spent before it was produced
                // (RegenLedger.Of), and the derived count and deadline beside it.
                case "wallet.heartsProduced": return 12L;
                case "wallet.heartsSpent": return 9L;
                case "wallet.heartsDueUnix": return 1_700_000_500L;
                case "wallet.hearts": return 3;
                case "wallet.heartsNextRefillUnix": return 1_700_000_500L;
                case "wallet.hintsProduced": return 9L;
                case "wallet.hintsSpent": return 7L;
                case "wallet.hintsDueUnix": return 1_700_000_600L;

                // The milestone list is canonical: it holds only levels above the floor beside
                // it (KeeperMilestoneSet.Normal), so the row has to stand above the floor.
                case "wallet.keeperMilestonesClaimed": return 12;
                case "wallet.keeperMilestonesTaken[]": return 20;

                // Ids a reader parses and drops when unknown - by design, a malformed row is
                // judged once, at read.
                case "wardLoadout[].colour": return "r";
                case "tasks.daily.counts[].goal": return "raiders";
                case "tasks.weekly.counts[].goal": return "raiders";
                case "tasks.lifetime[].goal": return "raiders";

                // A waiting debit dated before the server's confirmed cut-off is already in the
                // baseline and is pruned by a merge (CurrencyLedger.PruneConfirmedPending), so a
                // real ledger's cut-off is always older than what it still holds.
                case "wallet.currencies[].confirmedThroughUnix": return 1_600_000_000L;

                // A ceiling (five stars) and an order (never more wins than attempts).
                case "wardStars[].stars": return 3;
                case "challenges.today[].attempts": return 2;
                case "challenges.today[].wins": return 1;

                // A level record's reader clamps both to what a run can produce (three stars,
                // a percentile band), and no writer produces anything outside them.
                case "levels[].stars": return 3;
                case "levels[].bestRank": return 40;

                // A streak's reader pulls a collected night back to the last day played
                // (DailyStreak.RepairCollected); every real writer keeps it there already.
                case "streak.startDay": return 20_000;
                case "streak.lastPlayedDay": return 20_010;
                case "streak.collectedThroughDay": return 20_005;
                case "streak.shieldFromDay": return 20_008;

                // The nights and rungs taken out of order are canonical lists (FloorSet.Normal):
                // each entry above the floor beside it. A streak night is a night number inside the
                // run above (start 20,000, floor 20,005: nights 7 to 11), and the anchor a night the
                // run could have reached (StreakTaken.Canonical).
                case "streak.collectedRun": return 20_000;
                case "streak.collectedNights[]": return 8;
                case "streak.collectedAnchorNight": return 9;
                case "streak.collectedAnchorDay": return 20_008;
                case "events[].collectedGoal": return 10;
                case "events[].taken[]": return 20;
                case "events[].premiumGoal": return 10;
                case "events[].premiumTaken[]": return 30;

                default: return null;
            }
        }

        // ================================================================ reflection
        static IEnumerable<FieldInfo> Fields(Type type)
            => type.GetFields(BindingFlags.Public | BindingFlags.Instance);

        static bool IsLeaf(Type type)
            => type.IsPrimitive || type == typeof(string) || type == typeof(StoredFlag);

        static string Join(string path, string name) => path.Length == 0 ? name : path + "." + name;

        /// <summary>Every leaf path of <paramref name="root"/>, rows of a list as <c>[]</c>.</summary>
        static IEnumerable<string> Leaves(object root)
        {
            var found = new List<string>();
            Walk(root.GetType(), "", found);
            return found;
        }

        static void Walk(Type type, string path, List<string> found)
        {
            if (IsLeaf(type)) { found.Add(path); return; }

            if (type.IsArray) { Walk(type.GetElementType(), path + "[]", found); return; }

            foreach (var field in Fields(type)) Walk(field.FieldType, Join(path, field.Name), found);
        }

        /// <summary>
        /// The value at <paramref name="path"/>, reading the first row of each list on the way -
        /// the generator writes exactly one - or null when any step is missing.
        /// </summary>
        static object ValueAt(object root, string path)
        {
            object current = root;
            foreach (var step in path.Split('.'))
            {
                if (current == null) return null;

                bool row = step.EndsWith("[]", StringComparison.Ordinal);
                string name = row ? step.Substring(0, step.Length - 2) : step;

                var field = current.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (field == null) return null;
                current = field.GetValue(current);

                if (row)
                {
                    var array = current as Array;
                    current = array == null || array.Length == 0 ? null : array.GetValue(0);
                }
            }
            return current;
        }

        static bool SameAt(object a, object b, string path) => Equals(ValueAt(a, path), ValueAt(b, path));

        /// <summary>A deep copy with the one field at <paramref name="path"/> moved off its value.</summary>
        static SaveFileDto Changed(SaveFileDto source, string path)
        {
            var copy = (SaveFileDto)Clone(source);

            var steps = path.Split('.');
            object owner = copy;
            for (int i = 0; i < steps.Length; i++)
            {
                bool row = steps[i].EndsWith("[]", StringComparison.Ordinal);
                string name = row ? steps[i].Substring(0, steps[i].Length - 2) : steps[i];
                var field = owner.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                bool last = i == steps.Length - 1;

                if (row)
                {
                    var array = (Array)field.GetValue(owner);
                    if (last)
                    {
                        array.SetValue(Moved(array.GetValue(0)), 0);
                        return copy;
                    }
                    owner = array.GetValue(0);
                    continue;
                }

                if (last)
                {
                    object moved = Moved(field.GetValue(owner));
                    if (owner.GetType().IsValueType)
                        throw new InvalidOperationException("a struct in the middle of a path: " + path);
                    field.SetValue(owner, moved);
                    return copy;
                }

                // A struct leaf (StoredFlag) is handled as a whole above; nothing else here is a
                // struct, so the owner is always a reference and the write lands in the copy.
                owner = field.GetValue(owner);
            }

            return copy;
        }

        static object Moved(object value)
        {
            switch (value)
            {
                case int i: return i + 1;
                case long l: return l + 1;
                case bool b: return !b;
                case string s: return s + "~";
                case StoredFlag f: return StoredFlag.From(!f.Resolve(true));
                default: throw new InvalidOperationException("no way to move a " + value?.GetType().Name);
            }
        }

        static object Clone(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (IsLeaf(type)) return value;

            if (type.IsArray)
            {
                var source = (Array)value;
                var copy = Array.CreateInstance(type.GetElementType(), source.Length);
                for (int i = 0; i < source.Length; i++) copy.SetValue(Clone(source.GetValue(i)), i);
                return copy;
            }

            var instance = Activator.CreateInstance(type);
            foreach (var field in Fields(type)) field.SetValue(instance, Clone(field.GetValue(value)));
            return instance;
        }

        static bool IsDefault(object value)
            => value == null
            || (value is int i && i == 0)
            || (value is long l && l == 0L)
            || (value is bool b && !b)
            || (value is string s && s.Length == 0)
            || (value is StoredFlag f && f.state == StoredFlag.Unset);

        static string Show(object value)
            => value == null ? "(missing)"
             : value is StoredFlag f ? "flag " + f.state
             : value.ToString();
    }
}
