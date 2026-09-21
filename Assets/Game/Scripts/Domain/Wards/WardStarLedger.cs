using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// How far each turret a player owns has been upgraded, and what buying the next star costs.
    ///
    /// <para>
    /// <b>Keyed on the holding rather than on the turret</b> — <c>{id}:{colour}</c>, the same
    /// string <c>wardsOwned</c> uses (<see cref="WardHolding"/>). A turret is bought per colour
    /// because which colour a trick is worth having on is the whole of what makes the shelf a
    /// choice (invariant 42c), and the shelf is drawn per seat — so the card a player is looking
    /// at is already "this turret, on this seat", and the stars on it are that seat's.
    /// </para>
    /// <para>
    /// <b>Every turret on the shelf, with no band exempt — which is what a seat being the unit
    /// of purchase buys.</b> A legendary was bought outright for three days (invariant 42k), so
    /// its row carried no colour and its four seats shared one ladder: upgrading the Eclipse on
    /// red upgraded the Eclipse on blue, which is not what a player who paid four times for four
    /// turrets is owed. It is bought per seat now, so <c>WardHolding.Row</c> hands this the
    /// keyed row for the whole roster and each seat climbs on its own. <b>Nothing here changed to
    /// make that true</b>: the writing and the reading were already on the same side of one rule,
    /// which is the whole reason invariant 15a asks for that shape.
    ///
    /// <b>The bare row is still read, and is now only ever legacy.</b> A file from before colours
    /// existed holds one, and so does a legendary bought under the old rule — so
    /// <see cref="StarsOf(string, char)"/> falls back to it, and a five-star Eclipse keeps five
    /// stars on all four seats rather than being handed back at one.
    /// </para>
    /// <para>
    /// <b>A star count only ever rises, which is the whole reason it is storable.</b> Invariant 11b
    /// refuses a stored count outright: two devices showing 3 and 0 are equally consistent with
    /// "one spent three" and "one has not heard yet". An upgrade cannot be undone, so the join is
    /// a per-key <c>max</c> and the two devices are unambiguous — the same shape as
    /// <c>endlessBest</c> (14a's floor) and <c>homesteadStock</c>. <b>Before adding anything else
    /// here, check it only ever rises.</b>
    /// </para>
    /// <para>
    /// <b>Absent means one star, not nought.</b> A turret bought before this shipped, a row a
    /// merge dropped and a file from an older build all read the same way — so nothing needs a
    /// migration and nothing can stand at nought stars. The floor lives in
    /// <c>WardStars.Sane</c>, which every read goes through.
    /// </para>
    /// <para>
    /// <b>Nothing here is adjudicated, for <c>WardLedger</c>'s reason.</b> A forged star buys an
    /// addition to a bolt and a little health; it can never reach a public number, because a
    /// grove's worth is derived from what is held in the grove (19a) and the line is not part of
    /// it. The money half is defended where money always is — <c>submitSpends</c> refuses a debit
    /// the server-derived balance cannot cover.
    /// </para>
    /// </summary>
    public static class WardStarLedger
    {
        /// <summary>
        /// The most rows carried, and <b>the tightest bound in this feature now</b>: a shelf of
        /// thirty turrets times four seats is a hundred and twenty, eight short of this.
        ///
        /// <para>
        /// <b>It was thirty-eight short, and what spent the headroom was the legendary band
        /// becoming per-seat</b> (invariant 42k): ten turrets that carried one row each now carry
        /// four. Nothing about that is unsafe today, and the failure if it ever stops being safe
        /// is the quiet kind — <see cref="Write"/> truncates, so the rows past the bound are
        /// upgrades a player paid for that stop being written down. <b>So it is a fixture rather
        /// than a comment</b>: <c>WardLoadoutTests.TheStarLedgerHoldsTheWholeShelf</c> fails the
        /// day a roster outgrows this, which is the day to raise both this and the rules' bound
        /// together (12b, in that order).
        /// </para>
        /// <para>
        /// The rules cap the array at the same figure, which is what makes this the client's own
        /// cap rather than a suggestion, and bounds what a hand-edited file can push through the
        /// merge.
        /// </para>
        /// </summary>
        public const int MaxRows = 128;

        static readonly Dictionary<string, int> Stars =
            new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Raised when a star is bought, so a shelf and a panel repaint together.</summary>
        public static event Action Changed;

        /// <summary>What the save holds, in the order it is written.</summary>
        public static IEnumerable<KeyValuePair<string, int>> Rows => Stars;

        /// <summary>Takes the ledger from a save file. Absent rows are one star.</summary>
        public static void LoadFrom(WardStarDto[] rows)
        {
            Stars.Clear();

            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.ward)) continue;
                    if (row.stars <= WardStars.Least) continue;   // one star is the absent state

                    int stars = WardStars.Sane(row.stars);

                    if (Stars.TryGetValue(row.ward, out int held) && held >= stars) continue;

                    Stars[row.ward] = stars;
                }

            Changed?.Invoke();
        }

        /// <summary>
        /// How far this turret has been taken on this colour.
        ///
        /// <b>Never nought and never past the top</b>, whatever the file said: every read goes
        /// through <c>WardStars.Sane</c>, so a turret is always standing somewhere on the ladder.
        /// </summary>
        public static int StarsOf(string id, char colour)
        {
            if (string.IsNullOrEmpty(id)) return WardStars.Least;

            // The seat's own row first, then the bare one — a file written before colours
            // existed, or a legendary bought while the band was bought outright (invariant 42k,
            // `WardHolding.CopyMark`). Taking the seat's first
            // means a per-colour row always wins where both somehow exist, which is the reading
            // that can never confiscate an upgrade somebody bought for one seat.
            if (Stars.TryGetValue(WardHolding.Key(id, colour), out int stars))
                return WardStars.Sane(stars);

            return Stars.TryGetValue(id, out int bare) ? WardStars.Sane(bare) : WardStars.Least;
        }

        /// <summary>See <see cref="StarsOf(string, char)"/>.</summary>
        public static int StarsOf(WardModel model, char colour)
            => model == null ? WardStars.Least : StarsOf(model.Id, colour);

        /// <summary>The turret as it stands on this colour: which model, and how far up.</summary>
        public static WardBuild BuildOf(WardModel model, char colour)
            => new WardBuild(model, StarsOf(model, colour));

        /// <summary>
        /// Records a star bought. Never lowers one, and never past the top.
        ///
        /// <para>
        /// <b>The ledger does not take the money.</b> Who may pay, and whether they can, is the
        /// caller's question and is asked through <c>PlayerProgression.TrySpend</c> — a ledger
        /// that debited would be a second place a purchase happens, and two of those is two
        /// chances to charge for one thing (invariant 23's argument about the continue).
        /// </para>
        /// <para>
        /// <b>It takes the model rather than its id, because <see cref="WardHolding.Row"/> is
        /// where a holding's spelling is decided</b> — it answered differently for the legendary
        /// band while that band was bought outright, and the day it does so again this writer
        /// follows it without being told. Every writer takes this overload; the id one is kept
        /// for a caller that has nothing else, and writes the per-colour row.
        /// </para>
        /// </summary>
        public static bool Raise(WardModel model, char colour, int stars)
            => model != null && Raise(WardHolding.Row(model, colour), stars);

        /// <summary>See <see cref="Raise(WardModel, char, int)"/>.</summary>
        public static bool Raise(string id, char colour, int stars)
            => Raise(WardHolding.Key(id, colour), stars);

        static bool Raise(string key, int stars)
        {
            if (string.IsNullOrEmpty(key)) return false;

            stars = WardStars.Sane(stars);

            if (Stars.TryGetValue(key, out int held) && held >= stars) return false;
            if (stars <= WardStars.Least) return false;

            Stars[key] = stars;
            Changed?.Invoke();
            return true;
        }

        /// <summary>The rows to write, sorted so an unchanged ledger pushes nothing.</summary>
        public static WardStarDto[] ToRows() => Write(Stars);

        /// <summary>
        /// Joins two files' ladders by taking the further of each.
        ///
        /// <b>A per-key maximum, which is the only join a count may have</b> — see the summary.
        /// Idempotent and order-independent by construction, which is what invariant 11 asks of
        /// anything a sync touches.
        /// </summary>
        public static WardStarDto[] Join(WardStarDto[] mine, WardStarDto[] other)
        {
            var best = new Dictionary<string, int>(StringComparer.Ordinal);

            Absorb(best, mine);
            Absorb(best, other);

            return Write(best);
        }

        static void Absorb(Dictionary<string, int> into, WardStarDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.ward)) continue;
                if (row.stars <= WardStars.Least) continue;

                int stars = WardStars.Sane(row.stars);

                if (into.TryGetValue(row.ward, out int held) && held >= stars) continue;

                into[row.ward] = stars;
            }
        }

        static WardStarDto[] Write(Dictionary<string, int> from)
        {
            var keys = new List<string>(from.Keys);
            keys.Sort(StringComparer.Ordinal);

            int take = keys.Count > MaxRows ? MaxRows : keys.Count;
            var rows = new WardStarDto[take];

            for (int i = 0; i < take; i++)
                rows[i] = new WardStarDto { ward = keys[i], stars = from[keys[i]] };

            return rows;
        }
    }
}
