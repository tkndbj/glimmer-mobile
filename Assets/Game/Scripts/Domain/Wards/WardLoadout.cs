using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// Which turret the player has put on each colour, and the one place that choice is stored.
    ///
    /// <para>
    /// <b>An instruction, not an achievement, so it is merged by recency and not by value</b> —
    /// invariant 16's split, where a purchase is an entitlement joined by union
    /// (<see cref="WardLedger"/>) and an arrangement is an instruction stamped with its own date.
    /// It is the one part of this feature a merge can lose something from, which is exactly why
    /// invariant 11c's two hard-won rules are followed to the letter here: the stamp is the
    /// <em>choice's</em> own (never the file's <c>updatedUnix</c>, which
    /// <c>SaveService.Snapshot</c> sets to now and which therefore made the local side newer in
    /// every comparison it ever took part in), and a player who has never chosen writes
    /// <b>nothing at all</b> rather than storing the default — or a device with no opinion would
    /// be indistinguishable from one that had made a choice.
    /// </para>
    /// <para>
    /// <b>One stamp for the whole line rather than one per colour.</b> A loadout is one
    /// arrangement made in one sitting on one screen, so the thing a player would be surprised to
    /// lose is the arrangement — and per-colour stamps would let two devices interleave into a
    /// line neither of them ever chose, which is worse than losing the older of two lines.
    /// </para>
    /// <para>
    /// <b>Set once and used everywhere.</b> Nothing about a level, a chapter or a mode reaches
    /// this: the line is a fact about the account, so a player arranges it once and walks into any
    /// rung with it. That is the same argument invariant 39a makes for holding utility stock
    /// account-wide — a loadout kept per level would make the turrets part of a board's
    /// difficulty, which is what invariant 29c refuses a companion's ability.
    /// </para>
    /// </summary>
    public static class WardLoadout
    {
        static readonly Dictionary<char, string> _chosen = new Dictionary<char, string>(4);
        static long _setUnix;

        /// <summary>Raised when the line changed, so an open screen or a live board can redraw.</summary>
        public static event Action Changed;

        /// <summary>When the player last arranged the line, or nought if they never have.</summary>
        public static long SetUnix => _setUnix;

        /// <summary>Whether the player has ever arranged the line at all.</summary>
        public static bool Chosen => _chosen.Count > 0;

        // ------------------------------------------------------------- reading
        /// <summary>The turret id chosen for this colour, or empty for one never chosen.</summary>
        public static string IdFor(char colour)
            => _chosen.TryGetValue(colour, out string id) ? id : string.Empty;

        /// <summary>
        /// The line as the board should play it: every gap, every unknown id and every turret the
        /// player no longer holds filled in with the roster's starter.
        ///
        /// <b>Resolved on every ask rather than cached</b>, because the three things it depends on
        /// — the roster, what is owned and what was chosen — all change while the game is running,
        /// and a cache is a fourth thing that can disagree with them. It is four dictionary
        /// lookups.
        /// </summary>
        public static WardLine Line
        {
            get
            {
                var catalog = WardLedger.Catalog;
                var slots = new List<WardSlot>(_chosen.Count);

                foreach (var pair in _chosen) slots.Add(new WardSlot(pair.Key, pair.Value));

                return WardLine.Resolve(catalog, slots, WardLedger.IsHeld);
            }
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Stands a turret on a colour.
        ///
        /// <para>
        /// <b>Refuses a turret the player does not hold</b>, rather than storing it and letting
        /// <see cref="WardLine.Resolve"/> quietly fall back. The two would look identical on this
        /// device and differ on the next one: a stored id the player never owned is an id that
        /// would come true the day a drop made that turret free.
        /// </para>
        /// <para>
        /// Answers false when nothing changed, so a screen redrawing itself does not stamp a
        /// choice nobody made — which would push the line over a real arrangement made on another
        /// device.
        /// </para>
        /// </summary>
        public static bool Choose(char colour, string wardId)
        {
            if (WardLine.Colours.IndexOf(colour) < 0) return false;

            var model = WardLedger.Catalog.Find(wardId);
            if (model == null || !WardLedger.IsHeld(model)) return false;

            if (_chosen.TryGetValue(colour, out string held)
                && string.Equals(held, model.Id, StringComparison.Ordinal))
                return false;

            _chosen[colour] = model.Id;
            _setUnix = SaveSchema.NowUnix();

            Telemetry.Track("ward_chosen", "colour", colour.ToString(), "ward", model.Id);

            SaveService.Save();
            Raise();
            return true;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _chosen.Clear();
            _setUnix = dto?.wardLoadoutSetUnix ?? 0L;

            var rows = dto?.wardLoadout;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.colour)
                        || string.IsNullOrEmpty(row.ward)) continue;

                    char colour = row.colour[0];
                    if (WardLine.Colours.IndexOf(colour) < 0) continue;

                    _chosen[colour] = row.ward;
                }

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.wardLoadout = Rows(_chosen);
            dto.wardLoadoutSetUnix = _setUnix;
        }

        /// <summary>
        /// The rows, in colour order.
        ///
        /// <b>Ordered rather than however the dictionary walks</b>, because <c>SaveDelta</c>
        /// compares these to decide whether anything has to be pushed: an unstable order would
        /// read as changed on every launch and push a write for nothing, for ever — which is the
        /// trap <c>CompanionLedger.Join</c>'s own note is about.
        /// </summary>
        static WardSlotDto[] Rows(Dictionary<char, string> chosen)
        {
            var rows = new List<WardSlotDto>(chosen.Count);

            for (int i = 0; i < WardLine.Colours.Length; i++)
            {
                char colour = WardLine.Colours[i];
                if (!chosen.TryGetValue(colour, out string ward) || string.IsNullOrEmpty(ward))
                    continue;

                rows.Add(new WardSlotDto { colour = colour.ToString(), ward = ward });
            }

            return rows.ToArray();
        }

        /// <summary>
        /// Picks between two devices' arrangements and keeps the date of the one it picked.
        ///
        /// <para>
        /// <b>Still a join</b>, in <c>SaveMerge.Chosen</c>'s sense: it is a maximum over a total
        /// order — a real arrangement beats an absent one, then the later stamp wins, then a
        /// stable ordinal comparison of the canonical text settles a tie — so it is idempotent and
        /// gives the same answer whichever device runs it, which is what a merge promises. The
        /// empty test comes first and outranks the stamps because empty is never something a
        /// player asked for: <see cref="Choose"/> cannot store it, so it only ever means "this
        /// device has no opinion", and an opinion beats none however old it is.
        /// </para>
        /// </summary>
        public static (WardSlotDto[] Rows, long At) Join(WardSlotDto[] mine, long mineAt,
                                                         WardSlotDto[] other, long otherAt)
        {
            bool haveMine = mine != null && mine.Length > 0;
            bool haveOther = other != null && other.Length > 0;

            if (!haveMine && !haveOther) return (Array.Empty<WardSlotDto>(), 0L);
            if (!haveOther) return (Canonical(mine), mineAt < 0L ? 0L : mineAt);
            if (!haveMine) return (Canonical(other), otherAt < 0L ? 0L : otherAt);

            if (mineAt != otherAt)
                return mineAt > otherAt ? (Canonical(mine), mineAt) : (Canonical(other), otherAt);

            return string.CompareOrdinal(Text(mine), Text(other)) >= 0
                 ? (Canonical(mine), mineAt) : (Canonical(other), otherAt);
        }

        /// <summary>One arrangement's rows in colour order, with anything unreadable dropped.</summary>
        static WardSlotDto[] Canonical(WardSlotDto[] rows)
        {
            var chosen = new Dictionary<char, string>(4);

            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.colour)
                        || string.IsNullOrEmpty(row.ward)) continue;

                    char colour = row.colour[0];
                    if (WardLine.Colours.IndexOf(colour) < 0) continue;

                    chosen[colour] = row.ward;
                }

            return Rows(chosen);
        }

        /// <summary>The canonical text of an arrangement, for a stable tie-break.</summary>
        static string Text(WardSlotDto[] rows)
        {
            var canonical = Canonical(rows);
            var text = new System.Text.StringBuilder();

            for (int i = 0; i < canonical.Length; i++)
                text.Append(canonical[i].colour).Append(':').Append(canonical[i].ward).Append('|');

            return text.ToString();
        }
    }
}
