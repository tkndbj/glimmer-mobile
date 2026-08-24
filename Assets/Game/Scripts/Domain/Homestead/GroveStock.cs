using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// How many copies of each priced grove piece a player has bought.
    ///
    /// <para>
    /// <b>This is a stored count, and v16 spent a paragraph explaining why one could never
    /// live in this file.</b> That paragraph was right about the shape it described and wrong
    /// that no shape existed, and the distinction is the whole of this type. A count of copies
    /// <em>remaining</em> is unmergeable for hearts' reason (invariant 11b): two devices
    /// showing 3 and 1 are equally consistent with "one bought two more" and "one has not
    /// heard about a purchase", so every rule over the pair is wrong somewhere. A count of
    /// copies <b>ever bought</b> only rises, so the join is a per-id <c>max</c> and the larger
    /// value is always the one that knows more. What is left to place is then <em>derived</em>
    /// — bought minus placed — from two things the save file already holds.
    /// </para>
    /// <para>
    /// Hearts reached that shape after shipping the broken one and hints inherited it;
    /// <see cref="RegenLedger"/> is where those two share their arithmetic. This does not use
    /// it, deliberately: a regen ledger is three counters and a clock answering "how many are
    /// there now", where this is one counter per id answering "how many were bought", with the
    /// other half of the subtraction living in the placement map rather than here. Forcing one
    /// type over both would mean a clock this has no use for and a per-id map that one has no
    /// use for. What they share is the <em>idea</em>, and the idea is written down in invariant
    /// 11b rather than in a base class.
    /// </para>
    /// <para>
    /// <b>It holds no policy.</b> Which pieces are stocked, what a purchase grants and whether
    /// a player may place one are all <see cref="HomesteadLedger"/>'s, because those need the
    /// catalog and this needs nothing. That is what lets every rule about merging, migrating
    /// and bounding this section be proved offline against plain integers.
    /// </para>
    /// </summary>
    public sealed class GroveStock
    {
        /// <summary>
        /// Most copies of one piece this will ever record, and a permanent <c>const</c> rather
        /// than anything a content push can move.
        ///
        /// <para>
        /// <c>HeartLimits.HardCeiling</c>'s argument exactly. The clamp is <em>structural</em>
        /// — it exists so a corrupt or hostile file cannot put a number here that overflows the
        /// grove's worth or the wire's size guard — and a structural clamp may never be a
        /// published number, because lowering a published one would cut a counter the merge
        /// proof requires to be monotonic, leaving two devices restoring and re-clamping each
        /// other for ever. How many a player may buy in one go is an economy question in a
        /// different place; see <see cref="HomesteadLedger.MaxPerPurchase"/>.
        /// </para>
        /// <para>
        /// The floor is 196 tiles, so this is generous by more than an order of magnitude and
        /// still small enough that the grove's worth cannot leave <see cref="long"/>.
        /// </para>
        /// </summary>
        public const int MaxCopies = 9_999;

        /// <summary>
        /// Most distinct ids this will record, which is what bounds the wire.
        ///
        /// It matches the <c>homesteadStock</c> size guard in <c>firestore.rules</c>, and it
        /// has to: a save the client is willing to write and the rules refuse loses the whole
        /// document write rather than the extra rows (invariant 12a).
        /// </summary>
        public const int MaxIds = 512;

        /// <summary>
        /// Copies granted per id when a v19 file is migrated, on top of whatever is already
        /// standing in the grove. See <see cref="Migrate"/>.
        ///
        /// <para>
        /// <b>One, and being generous here was tried and is wrong.</b> Before v20 a purchase
        /// was permission to draw a piece anywhere, so a migrating file carries no honest
        /// number to convert — the player bought "a fence" and stood eleven of them. The
        /// tempting answer is a bundle's worth, and it cannot be had: this runs while the save
        /// is being read and again inside <see cref="SaveMerge"/>, and the grove catalog is
        /// loaded on <em>entering the Grovement</em>, so neither caller knows what a bundle is.
        /// A fixed grant instead would hand ten copies of a singly-sold 4,000-credit oak to
        /// anybody who owned one — a grove worth 40,000 where 4,000 was paid, on the number
        /// that reaches a public leaderboard (invariant 19a).
        /// </para>
        /// <para>
        /// So a migrated purchase is worth what it cost, and what a player <em>built</em> is
        /// kept whole by the placement count rather than by this. The one overstatement left is
        /// somebody who stood eleven fences from a ten-fence bundle, which is bounded by the
        /// grove they actually made and is the right way round: it comes from refusing to take
        /// a placement down.
        /// </para>
        /// </summary>
        public const int LegacyGrant = 1;

        readonly Dictionary<string, int> _copies = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>How many copies of this id were bought. Zero for anything never bought.</summary>
        public int Of(string id)
            => !string.IsNullOrEmpty(id) && _copies.TryGetValue(id, out int n) ? n : 0;

        /// <summary>True when at least one copy was bought — the "was this ever paid for" question.</summary>
        public bool Any(string id) => Of(id) > 0;

        /// <summary>How many distinct pieces have been bought.</summary>
        public int Count => _copies.Count;

        /// <summary>Every id with copies. Order is unspecified; <see cref="Write"/> sorts.</summary>
        public IReadOnlyCollection<string> Ids => _copies.Keys;

        /// <summary>
        /// Records copies bought, and answers how many were actually added.
        ///
        /// <para>
        /// It clamps at <see cref="MaxCopies"/> rather than refusing, because by the time
        /// anything here runs the caller has already taken the player's credits — refusing
        /// would be a charge with nothing delivered. <see cref="HomesteadLedger"/> is what stops
        /// a purchase reaching the ceiling in the first place, where it can still say no.
        /// </para>
        /// </summary>
        public int Add(string id, int copies)
        {
            if (string.IsNullOrEmpty(id) || copies <= 0) return 0;
            if (!_copies.ContainsKey(id) && _copies.Count >= MaxIds) return 0;

            int had = Of(id);
            int now = Clamp((long)had + copies);
            _copies[id] = now;

            return now - had;
        }

        /// <summary>Forgets everything, as a fresh install would.</summary>
        public void Clear() => _copies.Clear();

        // --------------------------------------------------------- file bridge
        /// <summary>
        /// Reads the rows of a save file, ignoring anything malformed rather than throwing.
        ///
        /// A duplicated id takes the larger of the two, which is the rule <see cref="Join"/>
        /// uses — so a file that should have been impossible (invariant 11a) is read the way
        /// two devices holding it would have been merged, rather than by whichever row happened
        /// to come last.
        /// </summary>
        public void LoadFrom(HomesteadStockDto[] rows)
        {
            _copies.Clear();
            Absorb(rows);
        }

        /// <summary>
        /// The rows to write, sorted by id.
        ///
        /// Not tidiness: <see cref="SaveChecksum"/> hashes the serialised file and
        /// <see cref="SaveDelta"/> walks these in order, so dictionary order would make an
        /// unchanged save look changed on every launch and push a write for nothing, for ever.
        /// </summary>
        public HomesteadStockDto[] Write()
        {
            if (_copies.Count == 0) return Array.Empty<HomesteadStockDto>();

            var ids = new List<string>(_copies.Keys);
            ids.Sort(StringComparer.Ordinal);

            var rows = new HomesteadStockDto[ids.Count];
            for (int i = 0; i < ids.Count; i++)
                rows[i] = new HomesteadStockDto { id = ids[i], copies = _copies[ids[i]] };

            return rows;
        }

        /// <summary>
        /// The two devices' purchases, joined.
        ///
        /// <para>
        /// A per-id maximum, which is the whole reason this section is allowed to hold a number
        /// at all: copies bought only ever rise, so the larger side is always the one that has
        /// heard about more purchases, and the join is idempotent and order-independent without
        /// trying (invariant 11).
        /// </para>
        /// <para>
        /// No early return for an empty side, deliberately — the trap
        /// <c>HomesteadLedger.Join</c> and <c>CompanionLedger.Join</c> both document. Handing
        /// one array straight back would skip the sort, so an unsorted file joined against
        /// nothing would come out still unsorted and <see cref="SaveDelta"/> would read every
        /// launch as changed.
        /// </para>
        /// <para>
        /// Unknown ids are kept, exactly as <c>tipsSeen</c> and <c>companionsOwned</c> keep
        /// theirs: copies bought on a newer build must not be confiscated by a trip through an
        /// older one.
        /// </para>
        /// </summary>
        public static HomesteadStockDto[] Join(HomesteadStockDto[] mine, HomesteadStockDto[] other)
        {
            var stock = new GroveStock();
            stock.Absorb(mine);
            stock.Absorb(other);

            return stock.Write();
        }

        /// <summary>
        /// Folds rows into what is already held, keeping the larger count for each id.
        ///
        /// The one place a row is judged, so <see cref="LoadFrom"/> and <see cref="Join"/>
        /// cannot come to disagree about what a malformed row means — invariant 5b's rule in a
        /// file with no reason to have two copies of it.
        /// </summary>
        void Absorb(HomesteadStockDto[] rows)
        {
            if (rows == null) return;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id) || row.copies <= 0) continue;
                if (!_copies.ContainsKey(row.id) && _copies.Count >= MaxIds) continue;

                int now = Clamp(row.copies);
                if (now > Of(row.id)) _copies[row.id] = now;
            }
        }

        /// <summary>
        /// The ids with at least one copy, sorted — the v19 <c>homesteadOwned</c> set, derived.
        ///
        /// <para>
        /// <b>Written as well as read, and it buys two things.</b> A client rolled back to a
        /// build before v20 finds the section it understands and keeps its pieces, which is the
        /// bargain the v4 heart mirror already makes. And the <em>deployed</em>
        /// <c>groveWorth</c> reads this field, so a client shipped before the functions are
        /// redeployed still scores its grove on the boards instead of dropping to nothing —
        /// the ordering hazard invariant 12a warns about, removed rather than documented.
        /// </para>
        /// <para>
        /// Derived on every write and never authoritative. <see cref="In"/> prefers the stock
        /// section whenever it holds anything, so a file carrying both is read as the v20 file
        /// it is and the mirror can never re-grant what a player has since spent.
        /// </para>
        /// </summary>
        public static string[] Mirror(HomesteadStockDto[] rows)
        {
            if (rows == null || rows.Length == 0) return Array.Empty<string>();

            var ids = new List<string>(rows.Length);
            foreach (var row in rows)
                if (row != null && !string.IsNullOrEmpty(row.id) && row.copies > 0) ids.Add(row.id);

            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        /// <summary>
        /// A v19 file's <c>homesteadOwned</c> set as stock rows.
        ///
        /// <para>
        /// <b>Pure, and static, because two callers need it at two different moments.</b>
        /// <c>HomesteadLedger.LoadFrom</c> runs it when a save is read off disk, and
        /// <see cref="SaveMerge"/> runs it on each side before joining — because a cloud
        /// document written by a v19 client carries the old set and no stock at all, and a join
        /// that only looked at the new section would quietly drop every purchase that device
        /// had made. Writing the conversion twice is invariant 5b's mistake in the one place
        /// where getting it wrong costs somebody the shop.
        /// </para>
        /// <para>
        /// Before v20 a purchase was permission to draw a piece anywhere rather than a copy of
        /// it, so there is no honest number to convert: the player bought "a fence" and stood
        /// eleven of them. Each id becomes the larger of what is standing in their grove and
        /// <see cref="LegacyGrant"/> — nothing they built is left over-placed, and they keep
        /// room to rearrange.
        /// </para>
        /// <para>
        /// Residents are never in that set (they are companions, invariant 16a) and neither are
        /// the free or earned pieces, so nothing here needs the catalog and the whole conversion
        /// can be proved offline against two arrays.
        /// </para>
        /// </summary>
        public static HomesteadStockDto[] Migrate(string[] owned, HomesteadPlacementDto[] placed)
        {
            if (owned == null || owned.Length == 0) return Array.Empty<HomesteadStockDto>();

            var standing = new Dictionary<string, int>(StringComparer.Ordinal);
            if (placed != null)
                foreach (var row in placed)
                {
                    if (row == null || string.IsNullOrEmpty(row.piece)) continue;

                    standing.TryGetValue(row.piece, out int had);
                    standing[row.piece] = had + 1;
                }

            var stock = new GroveStock();
            foreach (var id in owned)
            {
                if (string.IsNullOrEmpty(id)) continue;

                standing.TryGetValue(id, out int copies);
                stock.Add(id, copies > LegacyGrant ? copies : LegacyGrant);
            }

            return stock.Write();
        }

        /// <summary>
        /// The stock a save file is holding, whichever schema wrote it.
        ///
        /// The one door every reader of this section goes through, so "is this a v19 file"
        /// is asked in one place rather than at each call site — the shape
        /// <c>Wallet.ReadChosenName</c> already uses for the same class of question.
        /// </summary>
        public static HomesteadStockDto[] In(SaveFileDto dto)
        {
            if (dto == null) return Array.Empty<HomesteadStockDto>();
            if (dto.homesteadStock != null && dto.homesteadStock.Length > 0) return dto.homesteadStock;

            return Migrate(dto.homesteadOwned, dto.homesteadPlaced);
        }

        static int Clamp(long copies)
            => copies < 0L ? 0 : copies > MaxCopies ? MaxCopies : (int)copies;
    }
}
