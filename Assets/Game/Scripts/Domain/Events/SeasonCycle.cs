using System;
using System.Collections.Generic;
using System.Globalization;

namespace GlimmerGrove.Events
{
    /// <summary>
    /// A season that runs again, for ever: one authored window and one authored ladder, dealt
    /// back to back on the calendar with nothing stored anywhere.
    ///
    /// <para>
    /// <b>This is invariant 45b's shape, arriving on a season.</b> The task slate rotates by a
    /// pure function of the day — "day <c>k</c> deals slate entries <c>k·n …</c>, on every device
    /// and on the server, with nothing stored" — and a repeating season is the same trick with a
    /// longer period: cycle <c>n</c> runs <c>[start + n·period, start + (n+1)·period)</c>, both
    /// sides compute <c>n</c> from the clock, and no row, cursor or "which season is it" flag
    /// exists on either. **The bill for running seasons for ever is therefore nought** — no
    /// content push a season, no re-seed a season, no deploy a season, and no calendar for
    /// somebody to forget to extend.
    /// </para>
    /// <para>
    /// <b>Why a period rather than a list of dated seasons.</b> A list is a thing that runs out,
    /// silently, at a date somebody picked months earlier: the hub's box simply stops appearing
    /// and nothing anywhere is red. That is the failure this file refuses everywhere else — a
    /// gate that cannot fail (invariant 19e) and a wall nobody is checking (37aw) — and it is
    /// worse here because the symptom is a feature quietly ending rather than a crash.
    /// </para>
    /// <para>
    /// <b>The period is the window, and that is a decision rather than a shortcut.</b> A
    /// separate "repeat every N days" would be a second number that can disagree with the first,
    /// and the disagreement is invisible: a 42-day window repeating every 50 leaves eight days
    /// with no season and every file still reading as authored. Back to back is also what the
    /// owner asked for — when the time finishes it restarts — so the one number that exists is
    /// <see cref="PeriodSeconds"/>, and it <em>is</em> <c>endUnix - startUnix</c>.
    /// </para>
    /// <para>
    /// <b>Everything a new season has to reset resets by itself, because it is all keyed on the
    /// id.</b> Marks live in a per-season row (<see cref="SeasonLedger"/>) so they start at
    /// nought; the grant log is keyed on <c>mark:{id}:{track}:{goal}:{ccy}</c> so every rung pays
    /// again; and the pass entitlement is <c>pass:{id}</c> so it has to be bought again. **No save
    /// schema version, no <c>firestore.rules</c> change and no new claim shape** — which is the
    /// same bargain invariant 20a collects for a mode, arriving on a season.
    /// </para>
    /// <para>
    /// <b>What it costs is the name.</b> A season's name key is derived from its id
    /// (<see cref="GroveEvent.DefaultNameKey"/>), and an id that is derived cannot carry an
    /// authored string — nobody can write <c>ui.event.watch_0037.name</c> into a table that ships
    /// inside the app. So a cycle's name comes from a <b>pool</b> that wraps
    /// (<see cref="NamePoolSize"/>), which is the one place a repeating season is not free. See
    /// <see cref="NameKeyFor"/>.
    /// </para>
    /// </summary>
    public sealed class SeasonCycle
    {
        /// <summary>
        /// How many digits a cycle's number is padded to inside its id.
        ///
        /// <para>
        /// <b>Padded so that ordinal order is chronological order</b>, which several places rely
        /// on and none of them would say so: <see cref="SeasonLedger"/> sorts the save's rows by
        /// id to keep the checksum stable, the eviction that runs at the 64-row ceiling has to
        /// know which season is oldest, and a support query reading a grant log wants the seasons
        /// in the order they happened. Unpadded, <c>watch_10</c> sorts between <c>watch_1</c> and
        /// <c>watch_2</c> and every one of those is quietly wrong.
        /// </para>
        /// <para>
        /// Four digits is 10,000 seasons — over a thousand years at a six-week period, so the
        /// ceiling is a sanity bound rather than a limit anybody meets. It is <b>contract</b>: the
        /// id it builds goes into a save row, a loc lookup and every claim id the season's chests
        /// produce, so widening it later would orphan every id already written (invariant 1).
        /// </para>
        /// </summary>
        public const int IndexDigits = 4;

        /// <summary>The highest cycle an id may name, which falls out of <see cref="IndexDigits"/>.</summary>
        public const int MaxIndex = 9999;

        /// <summary>
        /// How many authored names the pool holds before it wraps.
        ///
        /// <para>
        /// Twelve, which at the shipped six-week period is well over a year before a name comes
        /// round again — and a name coming round again is the honest answer rather than a
        /// shortcoming. The alternatives are worse: an ordinal composed at runtime ("the 37th
        /// Watch") cannot be translated, because ordinals inflect differently in most of the
        /// languages this game ships, and a number in the title is a counter rather than a name.
        /// </para>
        /// <para>
        /// It is <b>contract with the string table</b>: <c>content.py</c> errors when any of the
        /// twelve name keys is missing, because a season whose name does not resolve draws an
        /// empty banner on the hub's largest card — and <c>loc.py</c> cannot see a derived key
        /// at all, which is why the check lives with the content gate and not with the strings.
        /// </para>
        /// </summary>
        public const int NamePoolSize = 12;

        /// <summary>The stem every cycle id is built from — the manifest's own <c>id</c>.</summary>
        public readonly string BaseId;

        /// <summary>When cycle nought opened. Absolute Unix seconds.</summary>
        public readonly long StartUnix;

        /// <summary>
        /// How long one cycle runs, in seconds — and therefore also the gap between two
        /// openings, because the seasons run back to back. Always positive on a valid cycle.
        /// </summary>
        public readonly long PeriodSeconds;

        public readonly IReadOnlyList<EventMilestone> Milestones;
        public readonly string Icon;
        public readonly int PassGems;

        public SeasonCycle(string baseId, long startUnix, long periodSeconds,
                           IReadOnlyList<EventMilestone> milestones,
                           string icon = null, int passGems = 0)
        {
            BaseId = baseId ?? string.Empty;
            StartUnix = startUnix;
            PeriodSeconds = periodSeconds;
            Milestones = milestones ?? Array.Empty<EventMilestone>();
            Icon = icon ?? string.Empty;
            PassGems = passGems < 0 ? 0 : passGems > EventRules.MaxPassGems ? EventRules.MaxPassGems : passGems;
        }

        public bool IsValid => !string.IsNullOrEmpty(BaseId)
                            && PeriodSeconds > 0
                            && Milestones.Count > 0;

        // ------------------------------------------------------------------ the calendar
        /// <summary>
        /// Which cycle is running at <paramref name="nowUnix"/>, or -1 before the first opens.
        ///
        /// <para>
        /// <b>Integer division on the elapsed seconds, and it is floored toward zero on purpose
        /// by refusing the negative case outright</b> rather than letting C# truncate it. A clock
        /// reading before <see cref="StartUnix"/> is a real state — a build shipped ahead of the
        /// season opening, or a device whose clock is wrong — and <c>-1 / period</c> answering
        /// <c>0</c> would put such a device inside cycle nought weeks early. The answer is "no
        /// cycle", which every caller already handles, because a season that has not started is
        /// the state the game shipped in.
        /// </para>
        /// </summary>
        public int IndexAt(long nowUnix)
        {
            if (!IsValid || nowUnix < StartUnix) return -1;

            long elapsed = nowUnix - StartUnix;
            long index = elapsed / PeriodSeconds;

            return index > MaxIndex ? -1 : (int)index;
        }

        /// <summary>When cycle <paramref name="index"/> opens. Undefined for a negative index.</summary>
        public long StartOf(int index) => StartUnix + (long)index * PeriodSeconds;

        /// <summary>When cycle <paramref name="index"/> closes — the instant the next one opens.</summary>
        public long EndOf(int index) => StartOf(index) + PeriodSeconds;

        /// <summary>
        /// Whether cycle <paramref name="index"/> has opened by <paramref name="nowUnix"/>.
        ///
        /// <b>The whole of what bounds a forged claim</b>, and the server asks exactly this. A
        /// cycle that has not opened may not be claimed against, so the most any save can ever
        /// extract is one ladder per elapsed period — which is precisely what an honest player
        /// who finishes every season gets, and is invariant 47c's bound restated for a calendar
        /// that no longer ends.
        /// </summary>
        public bool HasOpenedBy(int index, long nowUnix)
            => IsValid && index >= 0 && index <= MaxIndex && nowUnix >= StartOf(index);

        // ------------------------------------------------------------------ ids
        /// <summary>
        /// The id cycle <paramref name="index"/> wears: the stem, an underscore, and the number
        /// padded to <see cref="IndexDigits"/>.
        ///
        /// <b>Invariant-culture formatting, always.</b> A save row, a loc lookup and a claim id
        /// are built from this, and a device whose culture renders digits in another script —
        /// Arabic-Indic, Devanagari — would otherwise write an id no other device can read and
        /// no server can parse. Nothing in this game formats an id with the ambient culture; this
        /// is the one place it would have been easy to.
        /// </summary>
        public string IdFor(int index)
            => index < 0 || index > MaxIndex
             ? string.Empty
             : BaseId + "_" + index.ToString("D" + IndexDigits.ToString(CultureInfo.InvariantCulture),
                                             CultureInfo.InvariantCulture);

        /// <summary>
        /// The cycle number an id names, or -1 when the id is not one of this cycle's.
        ///
        /// <para>
        /// Deliberately strict: the stem must match exactly, the tail must be exactly
        /// <see cref="IndexDigits"/> digits, and the id must round-trip through
        /// <see cref="IdFor"/>. A looser parse would accept <c>watch_7</c> and <c>watch_00007</c>
        /// as the same season as <c>watch_0007</c>, which is three save rows and three sets of
        /// grant-log keys for one season — the sort of thing that reads as a reset to the player
        /// who hits it and as nothing at all to everybody else.
        /// </para>
        /// </summary>
        public int IndexOf(string id)
        {
            if (!IsValid || string.IsNullOrEmpty(id)) return -1;

            int stem = BaseId.Length;
            if (id.Length != stem + 1 + IndexDigits) return -1;
            if (string.CompareOrdinal(id, 0, BaseId, 0, stem) != 0) return -1;
            if (id[stem] != '_') return -1;

            int value = 0;
            for (int i = stem + 1; i < id.Length; i++)
            {
                char c = id[i];
                if (c < '0' || c > '9') return -1;
                value = value * 10 + (c - '0');
            }

            return value;
        }

        /// <summary>Whether <paramref name="id"/> is one of this cycle's, whenever it falls.</summary>
        public bool Owns(string id) => IndexOf(id) >= 0;

        // ------------------------------------------------------------------ names
        /// <summary>
        /// The loc key cycle <paramref name="index"/> takes its name from.
        ///
        /// <para>
        /// <b>A pool that wraps, because a derived id cannot carry an authored string.</b>
        /// Everything else about a season is derived and costs nothing; a name has to be written
        /// by somebody and translated, and translations ship inside the app (invariant 50a), so
        /// there is no arrangement in which an endless series of seasons has an endless series of
        /// authored names. Twelve is over a year at the shipped period.
        /// </para>
        /// <para>
        /// The pool is indexed by the cycle rather than by anything stored, so two devices on the
        /// same season always draw the same name, and a device that has been offline across a
        /// rollover draws the right one the moment it reads the clock.
        /// </para>
        /// </summary>
        public static string NameKeyFor(int index) => "ui.season." + PoolSlot(index) + ".name";

        /// <summary>
        /// The blurb every cycle shares.
        ///
        /// <b>One string rather than one per pool slot, because a blurb says what a watch *is*
        /// and that does not vary by which watch it is.</b> Twelve near-identical sentences would
        /// be twelve things to translate into every language this game ships, to say the same
        /// thing twelve times — and the first one to be edited without the others would be a
        /// season that quietly explains itself differently from its neighbours.
        /// </summary>
        public const string BlurbKey = "ui.season.blurb";

        /// <summary>
        /// Which of the twelve a cycle takes, as a string.
        ///
        /// Euclidean rather than C#'s <c>%</c>, which keeps the sign of the dividend: a negative
        /// index can only arrive from a caller that ignored <see cref="IndexAt"/>'s -1, and
        /// <c>ui.season.-5.name</c> is a key that resolves to nothing and draws an empty banner
        /// rather than saying anything.
        /// </summary>
        static string PoolSlot(int index)
        {
            int slot = index % NamePoolSize;
            if (slot < 0) slot += NamePoolSize;
            return slot.ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ materialising
        /// <summary>
        /// Cycle <paramref name="index"/> as an ordinary <see cref="GroveEvent"/>, or null.
        ///
        /// <para>
        /// <b>This is the whole of what keeps the recurrence cheap.</b> Everything downstream —
        /// the ledger, the two screens, the notification plan, the progress reader, the claim
        /// builder — takes a <c>GroveEvent</c> and has never heard of a cycle, so a repeating
        /// season is a *source* of seasons rather than a second kind of season with its own path
        /// through the game. The alternative, a flag on <c>GroveEvent</c> that every reader
        /// checks, is the shape this file refuses in <c>ModeValidator</c>'s note: a branch an
        /// entry can be missing from.
        /// </para>
        /// </summary>
        public GroveEvent EventFor(int index)
        {
            if (!IsValid || index < 0 || index > MaxIndex) return null;

            return new GroveEvent(IdFor(index), StartOf(index), EndOf(index), Milestones,
                                  Icon, PassGems, NameKeyFor(index), BlurbKey);
        }

        /// <summary>The cycle running at <paramref name="nowUnix"/>, or null before the first.</summary>
        public GroveEvent LiveAt(long nowUnix) => EventFor(IndexAt(nowUnix));

        /// <summary>
        /// Cycle <paramref name="id"/>, whenever it falls, or null when the id is not one of
        /// this cycle's.
        ///
        /// <b>No clock is consulted, deliberately.</b> This is what reaches a season the player
        /// still holds an unopened chest from, and one of those is by definition in the past; the
        /// *clock* question — may this be claimed against yet — is <see cref="HasOpenedBy"/>, and
        /// it is the server that has to ask it.
        /// </summary>
        public GroveEvent EventById(string id) => EventFor(IndexOf(id));
    }
}
