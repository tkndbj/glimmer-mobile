using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// One player's grove, as everybody else is allowed to see it.
    ///
    /// <para>
    /// <b>This exists so the save document never has to be readable by a stranger.</b>
    /// <c>players/{uid}</c> holds the level ledger, the streak's dates, the event floors, the
    /// chest counters and the ad allowance; a leaderboard needs a name, a number and where
    /// the benches are. Widening the save's read rule to serve that would publish everything
    /// else with it and make the save's shape a public API that can never change again. So a
    /// card is a separate document, written by the server, holding only what a visitor draws
    /// — which also makes it the natural place to put the one number a player now benefits
    /// from forging. See <c>functions/src/grove.ts</c>.
    /// </para>
    /// <para>
    /// <b>The score on a card is the server's, and that is the whole security story.</b>
    /// <see cref="GroveScore"/> is derived on the client and stores nothing, which was safe
    /// while it was a private readout: invariant 16g says as much, and notes that storing it
    /// would be "forgeable in the one direction that would matter if a leaderboard ever reads
    /// it". This is that leaderboard. The three id sets it derives from are all client-written
    /// (<c>homesteadOwned</c>, <c>groveLandOwned</c>, <c>companionsOwned</c>), and
    /// <c>firestore.rules</c> justifies letting the client write them with the sentence "a
    /// forged entry buys a picture on a screen nobody else sees" — a sentence this feature
    /// makes false. The resolution is invariant 13's: the client's figure is a prediction, the
    /// server recomputes from the save it reads itself, and the bought half is clamped to
    /// currency the server derived. Nothing here is trusted.
    /// </para>
    /// <para>
    /// <b>Nothing in a card is free text except the name.</b> Every piece and every region is
    /// an id from a catalog this build ships, so a grove cannot be arranged into something
    /// offensive — which is why moderation is a name problem here rather than a content
    /// problem, and why <see cref="GroveNames"/> is the only sanitiser in the feature.
    /// </para>
    /// <para>
    /// <b>Ids this build does not know are kept, not dropped.</b> A visitor a content drop
    /// behind will meet pieces and regions that do not exist for them yet.
    /// <see cref="PieceAt"/> resolves through the catalog and returns an invalid piece, which
    /// every drawing path already skips — <c>HomesteadMapper</c>'s stance, and the reason a
    /// visit degrades to a slightly emptier grove rather than to an error.
    /// </para>
    /// </summary>
    public sealed class GroveCard
    {
        /// <summary>The account this grove belongs to. Empty on a card built for preview.</summary>
        public readonly string OwnerId;

        /// <summary>
        /// What to call this keeper, already in its public form.
        ///
        /// The server decides it — see <see cref="GroveNames"/> for why a client's opinion
        /// about its own name stops being trustworthy the moment strangers read it — and it
        /// is never empty on a card that came from the server, because two unnamed keepers
        /// still need rows that differ.
        /// </summary>
        public readonly string Name;

        /// <summary>The companion the keeper is wearing, for the row's portrait.</summary>
        public readonly string AvatarId;

        /// <summary>Keeper level, for the honorific beside the name.</summary>
        public readonly int KeeperLevel;

        /// <summary>Credits' worth of grove held. The server's figure on a published card.</summary>
        public readonly long Score;

        /// <summary>Stars the score earned against the published ladder.</summary>
        public readonly int Stars;

        /// <summary>
        /// The furthest wave this keeper has held out to on the Infinite lane, or nought — what
        /// the endless board is ordered on.
        ///
        /// <para>
        /// <b>The one figure on a card the server cannot recompute, and it is on here rather than
        /// anywhere else precisely because of that.</b> A card is already the document a stranger
        /// reads, already written by the server alone, already moderated and already taken down by
        /// one opt-out; a second public document for one integer would be a second publish path, a
        /// second withdrawal, a second thing to moderate and a second thing to forget. What the
        /// server can do is <em>bound</em> it (<see cref="Progression.EndlessLedger.MaxWave"/>) and
        /// make sure it buys nothing — invariant 13 for a number that is not currency.
        /// </para>
        /// <para>
        /// <b>Nought is absent on the wire.</b> The server omits the field for a keeper who has
        /// never played the lane, which is what keeps the index the endless board is built from to
        /// the players who are actually on it rather than to every card in the game.
        /// </para>
        /// </summary>
        public readonly int BestWave;

        /// <summary>When the server last rebuilt this card, as a Unix timestamp.</summary>
        public readonly long PublishedUnix;

        /// <summary>The dwelling standing on the hall tile, or empty for a grove with none.</summary>
        public readonly string DwellingId;

        /// <summary>
        /// Which tile the keeper has moved their hall onto, and which way they turned it —
        /// empty for a grove whose home still stands where the floor put it.
        ///
        /// <para>
        /// <b>A visitor has to be told this or they draw the house in the wrong place.</b> A
        /// seat used to be content, so every grove in the world had its hall on one tile and a
        /// card never needed to say; it is the player's now (save v26), and a visitor left
        /// guessing would paint a home on ground its owner has since built on — two stands over
        /// the same tiles, which the occupancy index resolves without complaint and which reads
        /// as a bug.
        /// </para>
        /// <para>
        /// <b>Absent is a real answer rather than a missing one</b>, and that is what makes this
        /// safe to ship before the server publishes it: a card with no seat draws the hall where
        /// the floor says, which is exactly what every card said yesterday.
        /// </para>
        /// </summary>
        public readonly string HallSlot;
        public readonly int HallFacing;

        readonly HashSet<string> _land;
        readonly Dictionary<string, Placement> _placed;

        public GroveCard(string ownerId, string name, string avatarId, int keeperLevel,
                         long score, int stars, int bestWave, long publishedUnix,
                         string dwellingId,
                         IEnumerable<string> land,
                         IReadOnlyDictionary<string, Placement> placed,
                         string hallSlot = null, int hallFacing = 0)
        {
            OwnerId = ownerId ?? string.Empty;
            Name = name ?? string.Empty;
            AvatarId = avatarId ?? string.Empty;
            KeeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
            Score = score < 0L ? 0L : score;
            Stars = stars < 0 ? 0 : stars;
            BestWave = bestWave < 0 ? 0
                     : bestWave > Progression.EndlessLedger.MaxWave ? Progression.EndlessLedger.MaxWave
                     : bestWave;
            PublishedUnix = publishedUnix < 0L ? 0L : publishedUnix;
            DwellingId = dwellingId ?? string.Empty;
            HallSlot = hallSlot ?? string.Empty;
            HallFacing = GroveFootprint.Quarter(hallFacing);

            _land = new HashSet<string>(StringComparer.Ordinal);
            if (land != null)
                foreach (string id in land)
                    if (!string.IsNullOrEmpty(id)) _land.Add(id);

            _placed = new Dictionary<string, Placement>(StringComparer.Ordinal);
            if (placed != null)
                foreach (var pair in placed)
                    if (!string.IsNullOrEmpty(pair.Key) && pair.Value.IsOccupied)
                        _placed[pair.Key] = pair.Value;
        }

        /// <summary>
        /// A card with nothing on it. What a visit renders while the fetch is in flight, and
        /// what a failed fetch leaves behind — so no screen has to hold a null.
        /// </summary>
        public static readonly GroveCard Empty =
            new GroveCard(string.Empty, string.Empty, string.Empty, 1, 0L, 0,
                          0, 0L, string.Empty, null, null);

        /// <summary>True once this names an account, which is what a visit needs to draw.</summary>
        public bool IsValid => OwnerId.Length > 0;

        // ------------------------------------------------------------- the floor
        /// <summary>
        /// Whether this keeper owns a region.
        ///
        /// Starter land is free and is never written down (invariant 16e), so it is answered
        /// from the region rather than from the set — which is what lets "absent" and "bought
        /// nothing" stay the same fact on a card exactly as they are in the save.
        /// </summary>
        public bool OwnsLand(GroveRegion region)
            => region != null && region.IsValid && (region.IsStarter || _land.Contains(region.Id));

        /// <summary>Whether the tile at these coordinates is on ground this keeper owns.</summary>
        public bool OwnsLand(GroveFloor floor, int col, int row)
            => floor != null && floor.Contains(col, row) && OwnsLand(floor.RegionOf(col, row));

        /// <summary>Region ids bought, for a mapper writing this back out.</summary>
        public IReadOnlyCollection<string> Land => _land;

        // -------------------------------------------------------- what stands where
        /// <summary>The piece id standing on a tile, or empty.</summary>
        public string PieceIdAt(string slotId)
            => !string.IsNullOrEmpty(slotId) && _placed.TryGetValue(slotId, out var placement)
                ? placement.PieceId
                : string.Empty;

        /// <summary>Which quarter turn the piece on a tile is drawn at.</summary>
        public int FacingAt(string slotId)
            => !string.IsNullOrEmpty(slotId) && _placed.TryGetValue(slotId, out var placement)
            ? placement.Facing
            : 0;

        /// <summary>
        /// The piece standing on a tile, resolved against a catalog. Invalid when the tile is
        /// empty <em>or</em> when this build has never heard of what stands there — the two
        /// are the same to every drawing path, and deliberately so. See the type's remarks.
        /// </summary>
        public HomesteadPiece PieceAt(HomesteadCatalog catalog, string slotId)
            => catalog == null ? default : catalog.Find(PieceIdAt(slotId));

        /// <summary>
        /// Where this card's hall stands, falling back to the floor's own tile.
        ///
        /// The visitor's copy of <see cref="HomesteadLayout.HallSeat"/>, and it falls back the
        /// same way and for the same reason: a seat naming a tile off this build's floor is a
        /// card from a drop this build has not taken, and losing the house over it would be
        /// worse than drawing it where the floor says.
        /// </summary>
        public bool HallSeat(GroveFloor floor, out int col, out int row)
        {
            col = row = 0;
            if (floor == null) return false;

            if (GroveFloor.TryParse(HallSlot, out int c, out int r)
                && floor.Contains(c, r)
                && floor.Contains(c + floor.HallFootprint.Cols - 1, r + floor.HallFootprint.Rows - 1))
            {
                col = c; row = r;
                return true;
            }

            return GroveFloor.TryParse(floor.HallTile, out col, out row);
        }

        /// <summary>The dwelling on the hall, resolved against a catalog.</summary>
        public HomesteadPiece Dwelling(HomesteadCatalog catalog)
            => catalog == null ? default : catalog.Find(DwellingId);

        /// <summary>Every placement, for a mapper writing this back out.</summary>
        public IReadOnlyDictionary<string, Placement> Placements => _placed;

        /// <summary>
        /// Which tile of this grove is covered by what, through the visitor's own catalog.
        ///
        /// <para>
        /// Built the way <c>HomesteadLayout.Occupancy</c> builds the player's, so a visited
        /// grove and the same grove seen by its owner lay out identically — the footprint a
        /// piece covers is a fact about the piece, and a visitor's build supplies it from the
        /// same row of the same file. A card is immutable, so this is built once and kept.
        /// </para>
        /// </summary>
        public GroveOccupancy Occupancy(HomesteadCatalog catalog)
        {
            if (catalog == null) return GroveOccupancy.Empty;
            if (_occupancy != null && ReferenceEquals(_occupancyCatalog, catalog)) return _occupancy;

            var stands = new List<GroveStand>(_placed.Count + 1);

            var hall = HallSeat(catalog.Floor, out int hallCol, out int hallRow)
                ? catalog.Floor.HallStand(DwellingId, hallCol, hallRow, HallFacing)
                : default;
            if (hall.IsValid) stands.Add(hall);

            foreach (var pair in _placed)
            {
                if (!catalog.Floor.Contains(pair.Key)) continue;

                var stand = GroveOccupancy.Of(catalog, pair.Key, pair.Value.PieceId, pair.Value.Facing);
                if (stand.IsValid) stands.Add(stand);
            }

            _occupancy = new GroveOccupancy(stands);
            _occupancyCatalog = catalog;
            return _occupancy;
        }

        GroveOccupancy _occupancy;
        HomesteadCatalog _occupancyCatalog;

        /// <summary>How many tiles have something on them. For the visit screen's caption.</summary>
        public int OccupiedCount => _placed.Count;

        /// <summary>
        /// How many distinct pieces are on show.
        ///
        /// Variety rather than quantity, which is invariant 16's reading of what makes two
        /// groves differ: holding a piece is permission to draw it everywhere, so a count of
        /// placements says how patient somebody was and a count of kinds says what they own.
        /// </summary>
        public int VarietyCount
        {
            get
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pair in _placed) seen.Add(pair.Value.PieceId);
                return seen.Count;
            }
        }

        // ------------------------------------------------------------- this player
        /// <summary>
        /// The card this device would publish for the player in front of it.
        ///
        /// <para>
        /// <b>Built through the same projection a visitor reads, and that is the point.</b>
        /// The alternative — the visit screen drawing from a card and the grove screen
        /// drawing from the live ledgers — is two descriptions of one grove that agree until
        /// somebody edits one, which is how a player ends up visiting their own grove and
        /// finding it different. Everything a card can express is built here, once.
        /// </para>
        /// <para>
        /// The score is this device's own derivation and is a <em>prediction</em>: the server
        /// recomputes it from the save it reads itself and its figure is what the board
        /// shows. They agree for every honest player, which is what makes showing the local
        /// one before a publish returns the right thing to do rather than a lie waiting to be
        /// corrected.
        /// </para>
        /// </summary>
        public static GroveCard OfPlayer(HomesteadCatalog catalog, string ownerId,
                                         string name, string avatarId, int keeperLevel,
                                         long nowUnix)
        {
            // OccupiedSlots, not PlacedIds: the latter answers in piece ids for the art
            // loader, and walking it as slots is how this card came to carry no placements
            // — see HomesteadLayout.OccupiedSlots for what that cost.
            var placed = new Dictionary<string, Placement>(StringComparer.Ordinal);
            foreach (string slotId in HomesteadLayout.OccupiedSlots())
            {
                string pieceId = HomesteadLayout.At(slotId);
                if (string.IsNullOrEmpty(pieceId)) continue;      // emptied on purpose: nothing to show

                placed[slotId] = new Placement(pieceId, 0L, HomesteadLayout.FacingAt(slotId));
            }

            // `HallSlot` rather than a resolved seat: this and `OfSave` have to produce the
            // same fingerprint for the same grove, and only one of them can see the floor's
            // fallback. Absent is the answer for a grove nobody has rearranged, on both sides.
            return Build(catalog, LedgerHoldings.Instance, ownerId, name, avatarId, keeperLevel,
                         EndlessLedger.Best, nowUnix, placed,
                         HomesteadLayout.HallSlot, HomesteadLayout.HallFacing);
        }

        /// <summary>
        /// The card a <em>save file</em> describes — the one a sync has just settled with the
        /// server, which is the save the server will build the real card from.
        ///
        /// <para>
        /// This is what decides whether a publish is owed and what it is asked to prove
        /// (<c>GroveBoard</c>), and it is built from the pushed file rather than from the live
        /// ledgers for one reason: a piece placed while a push is in flight is on the device
        /// and not on the server, and a fingerprint taken from the device would mark it
        /// published when it never was — a card one placement behind, permanently, with no
        /// error anywhere. Reading the same file the server holds makes that impossible
        /// rather than unlikely.
        /// </para>
        /// <para>
        /// It answers the same question <see cref="OfPlayer"/> answers, through the same
        /// builder, over <see cref="SaveHoldings"/> in place of the ledgers; the placement
        /// rows are read the way <c>HomesteadLayout.LoadFrom</c> reads them (the later of two
        /// rows for one slot wins, an emptied slot shows nothing, a retired id is renamed).
        /// Name and companion are the file's own, shown the way the wallet would show them.
        /// </para>
        /// </summary>
        public static GroveCard OfSave(HomesteadCatalog catalog, Persistence.SaveFileDto save,
                                       string ownerId, int keeperLevel, long nowUnix)
        {
            var placed = new Dictionary<string, Placement>(StringComparer.Ordinal);
            var rows = save?.homesteadPlaced;
            if (rows != null)
                foreach (var row in rows)
                {
                    if (row == null || string.IsNullOrEmpty(row.slot)) continue;

                    string pieceId = GroveResidents.Rename(row.piece);
                    if (string.IsNullOrEmpty(pieceId))
                    {
                        placed.Remove(row.slot);       // emptied on purpose, and the later row wins
                        continue;
                    }

                    placed[row.slot] = new Placement(pieceId, 0L, row.facing);
                }

            string stored = save?.wallet?.displayName;
            string name = string.IsNullOrEmpty(stored) ? Persistence.Wallet.DefaultName : stored;

            return Build(catalog, new SaveHoldings(save, keeperLevel), ownerId, name,
                         save?.wallet?.avatarId ?? string.Empty, keeperLevel,
                         EndlessLedger.BestIn(save), nowUnix, placed,
                         save?.groveHall, save?.groveHallFacing ?? 0);
        }

        /// <summary>The one builder both readings go through, so they cannot drift.</summary>
        static GroveCard Build(HomesteadCatalog catalog, IGroveHoldings held, string ownerId,
                               string name, string avatarId, int keeperLevel, int bestWave,
                               long nowUnix,
                               IReadOnlyDictionary<string, Placement> placed,
                               string hallSlot = null, int hallFacing = 0)
        {
            catalog = catalog ?? HomesteadCatalog.Empty;

            var standing = GroveScore.Of(catalog, held);

            var land = new List<string>();
            foreach (var region in catalog.Floor.Regions)
                if (!region.IsStarter && held.Owns(region)) land.Add(region.Id);

            return new GroveCard(ownerId,
                                 GroveNames.Public(name),
                                 avatarId,
                                 keeperLevel,
                                 standing.Score,
                                 standing.Stars,
                                 bestWave,
                                 nowUnix,
                                 HomesteadLedger.BestDwelling(catalog, held).Id,
                                 land,
                                 placed,
                                 hallSlot,
                                 hallFacing);
        }

        /// <summary>
        /// The companion to draw for this card's keeper.
        ///
        /// Resolved rather than looked up, so a card naming a companion this build does not
        /// ship falls back to the starter instead of drawing nothing — <c>AvatarCatalog</c>'s
        /// stance, and the same reason a visited grove keeps its unknown pieces.
        /// </summary>
        public AvatarDefinition Companion() => AvatarCatalog.Resolve(AvatarId);

        /// <summary>
        /// A stable fingerprint of everything a visitor can see.
        ///
        /// <para>
        /// What decides whether a publish is owed. A sync that changed a star rating or a
        /// heart count has not changed this grove, and republishing on every sync would be a
        /// function invocation per player per sync for ever — see
        /// <see cref="GrovePublishPolicy"/>. Ordinal-sorted before hashing because two devices
        /// enumerate a dictionary in whatever order they please, and a fingerprint that
        /// depended on that would call every grove changed every time.
        /// </para>
        /// </summary>
        public string Fingerprint()
        {
            var parts = new List<string>(_land.Count + _placed.Count + 2)
            {
                "s:" + Score.ToString(System.Globalization.CultureInfo.InvariantCulture),

                // The wave is on the card, so a new best is a change a visitor can see and owes
                // a publish exactly as a bench does. Left out, a keeper could hold out ten waves
                // further than anybody alive and never reach the board they did it for, because
                // nothing else about their grove had moved — invariant 19j's fault arriving
                // through a field rather than through a stale read, which is precisely how the
                // hall's seat got here.
                "w:" + BestWave.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "d:" + DwellingId,
                "n:" + Name,
                "a:" + AvatarId,

                // Where the house stands is something a visitor sees, so moving it owes a
                // publish exactly as moving a bench does. Left out, a player could rearrange
                // the largest object in their grove and every board would go on showing the
                // old one for ever — which is the fault invariant 19j is about, arriving
                // through a field rather than through a stale read.
                "h:" + HallSlot + (HallFacing != 0 ? "/" + HallFacing : string.Empty),
            };

            foreach (string id in _land) parts.Add("l:" + id);
            foreach (var pair in _placed)
                parts.Add("p:" + pair.Key + "=" + pair.Value.PieceId
                          + (pair.Value.Facing != 0 ? "/" + pair.Value.Facing : string.Empty));

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
    }
}
