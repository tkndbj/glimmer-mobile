using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Converts a save between the local file shape and the Firestore document shape.
    ///
    /// Written out by hand rather than serialised as one opaque JSON string, and the
    /// reason is worth stating because the shortcut is tempting. If the ledger were a
    /// string, the security rules could not check anything about it, and — far more
    /// importantly — the server could not re-derive earned currency from it. That
    /// derivation is the thing that stops a forged save minting money. A blob would
    /// have quietly given that up in exchange for thirty fewer lines.
    ///
    /// Keys are part of the wire contract. Renaming one is a breaking change for every
    /// device that has already synced, exactly like renaming a level id.
    /// </summary>
    public static class FirestoreSaveMapper
    {
        public const int MaxLevelsPerDocument = 5000;

        /// <summary>The field a single glade's record lives under, for a partial write.</summary>
        public static string LevelFieldPath(string levelId) => "levels." + levelId;

        // ------------------------------------------------------------- to cloud
        /// <summary>
        /// One glade's record. Keyed by level id in the parent map rather than carrying
        /// the id inside itself, which is what makes a duplicate structurally impossible.
        /// </summary>
        public static Dictionary<string, object> LevelValue(LevelRecordDto record)
            => new Dictionary<string, object>
            {
                { "stars", (long)record.stars },
                { "bestMoves", (long)record.bestMoves },
                { "clears", (long)record.clears },
                { "firstClearedUnix", record.firstClearedUnix },
                { "lastPlayedUnix", record.lastPlayedUnix },

                // The standing travels because it is the only field here the device cannot
                // rebuild on its own: it needs the population that was published when the
                // record was set, and that table is gone by the time a reinstall happens.
                // The server neither reads nor adjudicates it — a band buys nothing, and the
                // rules validate the document's top-level keys rather than a glade's, so this
                // needed no rules change. See LevelRecord.BestRank.
                { "bestRank", (long)record.bestRank },

                // Travels for the same reason the standing does: a fastest clear cannot be
                // rebuilt from anything else on the device, so one that never left the phone
                // is a record lost on reinstall. The server neither reads nor adjudicates it.
                { "bestMillis", (long)record.bestMillis },
            };

        /// <summary>
        /// The whole document. Used for the first write, when nothing exists to diff
        /// against; afterwards a sync sends <see cref="HeaderFields"/> plus only the
        /// glades that actually changed.
        /// </summary>
        public static Dictionary<string, object> ToDocument(SaveFileDto dto)
        {
            if (dto == null) return null;

            var document = HeaderFields(dto);
            document["levels"] = LevelMap(dto);
            return document;
        }

        /// <summary>
        /// The ledger as a map, not an array. Three things follow, and all of them
        /// matter: a level id cannot appear twice, the server can key its lookups
        /// directly, and a sync can write <c>levels.c01_first_light</c> on its own
        /// instead of re-uploading the whole ledger because one glade gained a star.
        /// </summary>
        public static Dictionary<string, object> LevelMap(SaveFileDto dto)
        {
            var levels = new Dictionary<string, object>();
            if (dto?.levels == null) return levels;

            foreach (var record in dto.levels)
            {
                if (record == null || string.IsNullOrEmpty(record.levelId)) continue;
                if (levels.Count >= MaxLevelsPerDocument && !levels.ContainsKey(record.levelId)) break;

                levels[record.levelId] = LevelValue(record);
            }

            return levels;
        }

        /// <summary>Everything except the ledger. Small, and sent whenever anything is.</summary>
        public static Dictionary<string, object> HeaderFields(SaveFileDto dto)
        {
            if (dto == null) return null;

            return new Dictionary<string, object>
            {
                { "schemaVersion", (long)dto.schemaVersion },
                { "updatedUnix", dto.updatedUnix },
                { "lastPlayedLevelId", dto.lastPlayedLevelId ?? string.Empty },
                { "legacyImportDone", dto.legacyImportDone },
                { "tipsSeen", new List<object>(dto.tipsSeen ?? new string[0]) },

                // The companions the player bought. This has to travel, and for a stronger
                // reason than the tip set: it is the only thing in the save that cannot be
                // re-derived from anything else, so a set that never left the phone is a
                // companion somebody paid real progress for and loses on reinstall. The
                // server neither reads nor adjudicates it — a purchase is a cosmetic, and the
                // money half was already defended by submitSpends refusing a debit the
                // balance cannot cover. See CompanionLedger.
                { "companionsOwned", new List<object>(dto.companionsOwned ?? new string[0]) },

                // The heart containers, and this is the entry with the sharpest reason of the
                // three: a container is a real-money purchase, so a set that never left the
                // phone is a payment the player made and cannot see on their other device.
                // The revocations travel beside them, or a refund honoured on one device
                // would be undone by the next sync from another. Both are forgeable and both
                // are accounted for — a forged container buys faster hearts and no currency,
                // no progression and nothing that reaches a board, and the refund path is
                // owned by the server, which writes the revocations this field only caches.
                // See HeartContainerLedger and invariant 12a for why a field has to reach all
                // four places before it is really on the wire.
                { "heartContainersOwned", new List<object>(dto.heartContainersOwned ?? new string[0]) },
                { "heartContainersRevoked", new List<object>(dto.heartContainersRevoked ?? new string[0]) },

                // The grove. Its purchases travel for exactly the companions' reason; its
                // arrangement travels because it is the one thing in this file a player would
                // notice missing on a second device, and an evening spent laying out a grove
                // that never left the phone is the worst kind of loss — invisible until a
                // reinstall. Neither is adjudicated: a piece is a cosmetic, and the money half
                // is already defended by submitSpends. See HomesteadLedger.
                { "homesteadStock", Stock(dto.homesteadStock) },

                // The v19 mirror, derived by HomesteadLedger and carried so a rolled-back
                // client and a not-yet-redeployed groveWorth both keep working.
                { "homesteadOwned", new List<object>(dto.homesteadOwned ?? new string[0]) },
                { "homesteadPlaced", Placements(dto.homesteadPlaced) },

                // Land, and it is the reason the two lines above are not enough. It arrived a
                // schema version after them and reached SaveFileDto and SaveDelta but not this
                // map, so a floor bought with credits stayed on the phone that bought it — and
                // nothing showed it, because a device that never replaces its local save never
                // reads back what it failed to write. Switching accounts is what replaces it,
                // and the grove came back as the free starter square with everything standing
                // outside it invisible: the placements had survived, the ground under them had
                // not. Adding a field to the save is not done until it is on this map, in the
                // reader below, and in firestore.rules — where an unlisted key does not fail
                // the field, it fails the whole write.
                { "groveLandOwned", new List<object>(dto.groveLandOwned ?? new string[0]) },

                { "checksum", dto.checksum ?? string.Empty },

                { "settings", new Dictionary<string, object>
                    {
                        { "music", (long)(dto.settings?.music.state ?? 0) },
                        { "sfx", (long)(dto.settings?.sfx.state ?? 0) },
                        { "haptics", (long)(dto.settings?.haptics.state ?? 0) },
                        { "board", (long)(dto.settings?.board.state ?? 0) },
                        { "language", dto.settings?.language ?? string.Empty },
                    }
                },

                // Currency is deliberately absent. The balances that matter live in
                // players/{uid}/private/wallet, which the client cannot write, and
                // sending a second copy here would leave two documents claiming to
                // know what a player is holding. Only the parts nothing is spending
                // travel with the save.
                { "wallet", new Dictionary<string, object>
                    {
                        // The heart ledger. It has to travel, and it has to travel whole:
                        // a device that received only the count would have nothing to join
                        // against and would be back to guessing which side is stale, which
                        // is what used to delete a player's refills on every sync.
                        { "heartsProduced", dto.wallet?.heartsProduced ?? -1L },
                        { "heartsSpent", dto.wallet?.heartsSpent ?? -1L },
                        { "heartsDueUnix", dto.wallet?.heartsDueUnix ?? 0L },

                        // The derived mirror, for a client still on a pre-v8 build.
                        { "hearts", (long)(dto.wallet?.hearts ?? -1) },
                        { "heartsNextRefillUnix", dto.wallet?.heartsNextRefillUnix ?? 0L },

                        { "heartBoostUntilUnix", dto.wallet?.heartBoostUntilUnix ?? 0L },

                        // The hint ledger, whole, for the heart ledger's reason. -1 rather
                        // than 0 for the two counters, so a document written before hints
                        // existed is recognisable as holding no opinion rather than as a
                        // player who has spent everything.
                        { "hintsProduced", dto.wallet?.hintsProduced ?? -1L },
                        { "hintsSpent", dto.wallet?.hintsSpent ?? -1L },
                        { "hintsDueUnix", dto.wallet?.hintsDueUnix ?? 0L },

                        // The two preferences, each with the moment it was chosen. The
                        // stamps are not decoration: they are the whole of how the merge
                        // decides between two devices, and a name that travelled without
                        // one would be dated by whichever device asked last — which is the
                        // bug schema v15 exists to end. See SaveMerge.Chosen.
                        { "displayName", dto.wallet?.displayName ?? string.Empty },
                        { "displayNameSetUnix", dto.wallet?.displayNameSetUnix ?? 0L },
                        { "avatarId", dto.wallet?.avatarId ?? string.Empty },
                        { "avatarSetUnix", dto.wallet?.avatarSetUnix ?? 0L },
                    }
                },

                // Today's chest counters. Three integers, and they have to travel: a
                // second device that did not know a chest had been opened would draw it
                // as waiting, and the player would tap a chest that pays nothing.
                { "daily", new Dictionary<string, object>
                    {
                        { "dayKey", (long)(dto.daily?.dayKey ?? 0) },
                        { "runs", (long)(dto.daily?.runs ?? 0) },
                        { "claimed", (long)(dto.daily?.claimed ?? 0) },
                    }
                },

                // Today's ad allowance. It has to travel for the same reason the chest
                // counters do, and for a sharper one: the cap is the only thing between a
                // second device and a second set of ads, so a count that stays on one
                // phone is a cap that does not exist. Its absence here also made every
                // single sync a write — SaveDelta compares this section, and a field that
                // never comes back always differs from the one about to be sent.
                { "ads", new Dictionary<string, object>
                    {
                        { "dayKey", (long)(dto.ads?.dayKey ?? 0) },
                        { "lastWatchedUnix", dto.ads?.lastWatchedUnix ?? 0L },
                        { "watched", AdCounts(dto.ads) },
                    }
                },

                // The streak's three dates, which until now never left the phone — so a
                // player's streak silently restarted on their second device, and
                // DailyStreak.Join had nothing to join against. Every field is monotonic,
                // which is what makes sending them safe: the merge is three maxes and the
                // larger value is always the one that knows more.
                //
                // The server reads them too, but only to log a disagreement — see
                // `saveSupports` in functions/src/streak.ts. Nothing it pays depends on
                // them, which is deliberate: they are client-written, and a payment rule
                // resting on a forgeable number is not a rule.
                { "streak", new Dictionary<string, object>
                    {
                        { "startDay", (long)(dto.streak?.startDay ?? 0) },
                        { "lastPlayedDay", (long)(dto.streak?.lastPlayedDay ?? 0) },
                        { "collectedThroughDay", (long)(dto.streak?.collectedThroughDay ?? 0) },
                    }
                },

                // How much of each event's track the player has taken. Unlike the streak
                // dates above, the server *pays* on these: `eventCredits` counts a milestone
                // only once its floor has reached it, so a floor that stayed on the phone
                // would be a collect the wallet never heard about. Safe to send for the
                // usual reason — it is clamped there to the glades the star ledger actually
                // supports, so an edited one takes early what play had already earned and
                // nothing more.
                { "eventsSeeded", dto.eventsSeeded },
                { "events", EventFloors(dto.events) },

                { "progression", new Dictionary<string, object>
                    {
                        { "xpHighWater", dto.progression?.xpHighWater ?? -1L },
                        { "levelHighWater", (long)(dto.progression?.levelHighWater ?? -1) },
                    }
                },

                { "cloud", new Dictionary<string, object>
                    {
                        { "userId", dto.cloud?.userId ?? string.Empty },
                        { "revision", dto.cloud?.revision ?? 0L },
                        { "lastSyncedUnix", dto.cloud?.lastSyncedUnix ?? 0L },
                        { "deviceId", dto.cloud?.deviceId ?? string.Empty },
                    }
                },
            };
        }

        /// <summary>
        /// Per-placement view counts, as a list of small maps.
        ///
        /// A list rather than a map keyed by placement id, because a placement id is
        /// content and Firestore field names are not: an id with a dot or a slash in it
        /// would silently become a nested path. The order is the one
        /// <c>RewardedAds.WriteInto</c> already sorts into, so the comparison on the way
        /// back is an ordered walk.
        /// </summary>
        static List<object> AdCounts(AdStateDto ads)
        {
            var list = new List<object>();
            if (ads?.watched == null) return list;

            foreach (var entry in ads.watched)
            {
                if (entry == null || string.IsNullOrEmpty(entry.placement)) continue;

                list.Add(new Dictionary<string, object>
                {
                    { "placement", entry.placement },
                    { "count", (long)entry.count },
                });
            }

            return list;
        }

        /// <summary>
        /// Each event's collected floor, as a list of small maps.
        ///
        /// A list rather than a map keyed by event id, for the reason <see cref="AdCounts"/>
        /// gives: an event id is content and a Firestore field name is not, so an id
        /// carrying a dot would silently become a nested path. Already sorted by
        /// <c>EventCollection.WriteInto</c>, so the walk back is ordered.
        /// </summary>
        static List<object> EventFloors(EventStateDto[] events)
        {
            var list = new List<object>();
            if (events == null) return list;

            foreach (var entry in events)
            {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;

                list.Add(new Dictionary<string, object>
                {
                    { "id", entry.id },
                    { "collectedGoal", (long)entry.collectedGoal },
                });
            }

            return list;
        }

        /// <summary>
        /// The grove's arrangement, as a list of small maps.
        ///
        /// A list rather than a map keyed by slot id, for <see cref="EventFloors"/>'s reason:
        /// a slot id is content and a Firestore field name is not, so an id carrying a dot
        /// would silently become a nested path. Already sorted by
        /// <c>HomesteadLayout.WriteInto</c>, so the walk back is ordered.
        /// </summary>
        /// <summary>
        /// The stock rows, as a list of maps.
        ///
        /// A list rather than a map keyed by piece id, for <see cref="Placements"/>'s reason: a
        /// piece id is content and a Firestore field name is not, so an id carrying a dot would
        /// silently become a nested path. Already sorted by <c>GroveStock.Write</c>, so the walk
        /// back is ordered and <see cref="SaveDelta"/> can compare it row by row.
        /// </summary>
        static List<object> Stock(HomesteadStockDto[] rows)
        {
            var list = new List<object>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.id) || row.copies <= 0) continue;

                list.Add(new Dictionary<string, object>
                {
                    { "id", row.id },
                    { "copies", (long)row.copies },
                });
            }

            return list;
        }

        static List<object> Placements(HomesteadPlacementDto[] rows)
        {
            var list = new List<object>();
            if (rows == null) return list;

            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrEmpty(row.slot)) continue;

                list.Add(new Dictionary<string, object>
                {
                    { "slot", row.slot },
                    { "piece", row.piece ?? string.Empty },
                    { "setUnix", row.setUnix },

                    // Part of the arrangement, so it travels with it. A piece that comes back
                    // facing the other way is the same loss as one that comes back missing,
                    // only quieter.
                    { "flipped", row.flipped },
                });
            }

            return list;
        }

        // ----------------------------------------------------------- from cloud
        /// <summary>
        /// Reads a document back. Treats every field as missing until proven otherwise,
        /// because a document may have been written by a newer build, an older one, or
        /// a support tool, and none of those are reasons to throw on a background
        /// thread during a sync.
        /// </summary>
        public static SaveFileDto FromDocument(IDictionary<string, object> doc)
        {
            if (doc == null) return null;

            var dto = new SaveFileDto
            {
                schemaVersion = (int)Long(doc, "schemaVersion", SaveSchema.Version),
                updatedUnix = Long(doc, "updatedUnix", 0),
                lastPlayedLevelId = Str(doc, "lastPlayedLevelId"),
                legacyImportDone = Bool(doc, "legacyImportDone"),
                tipsSeen = StrList(doc, "tipsSeen"),
                companionsOwned = StrList(doc, "companionsOwned"),
                heartContainersOwned = StrList(doc, "heartContainersOwned"),
                heartContainersRevoked = StrList(doc, "heartContainersRevoked"),
                // Read as well as written, and it is the v19 field. A document written by a
                // device that has not updated carries the old id set and no stock at all, and
                // GroveStock.In is what turns one into the other — so leaving this out would
                // mean a sync from an older phone arrived as a grove with nothing bought.
                homesteadOwned = StrList(doc, "homesteadOwned"),
                groveLandOwned = StrList(doc, "groveLandOwned"),
                checksum = Str(doc, "checksum"),
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                daily = new DailyStateDto(),
                ads = new AdStateDto(),
                streak = new StreakStateDto(),
                progression = ProgressionStateDto.Unwritten(),
                cloud = new CloudStateDto(),
            };

            if (Map(doc, "settings") is IDictionary<string, object> settings)
            {
                dto.settings.music = new StoredFlag { state = (int)Long(settings, "music", 0) };
                dto.settings.sfx = new StoredFlag { state = (int)Long(settings, "sfx", 0) };
                dto.settings.haptics = new StoredFlag { state = (int)Long(settings, "haptics", 0) };
                dto.settings.board = new StoredFlag { state = (int)Long(settings, "board", 0) };
                dto.settings.language = Str(settings, "language");
            }

            if (Map(doc, "wallet") is IDictionary<string, object> wallet)
            {
                dto.wallet.hearts = (int)Long(wallet, "hearts", -1);
                dto.wallet.heartsNextRefillUnix = Long(wallet, "heartsNextRefillUnix", 0);

                // -1 when the document was last written by a pre-v8 build, which is what
                // tells SaveMerge to read the mirror above instead. Defaulting these to 0
                // would claim a real ledger of an empty-handed player.
                dto.wallet.heartsProduced = Long(wallet, "heartsProduced", -1);
                dto.wallet.heartsSpent = Long(wallet, "heartsSpent", -1);
                dto.wallet.heartsDueUnix = Long(wallet, "heartsDueUnix", 0);
                dto.wallet.heartBoostUntilUnix = Long(wallet, "heartBoostUntilUnix", 0);

                // -1 when the document predates the hint pool, which SaveMerge reads as "no
                // opinion" and answers with a full pool. Defaulting to 0 would claim a real
                // ledger belonging to a player who had spent every hint they ever had.
                dto.wallet.hintsProduced = Long(wallet, "hintsProduced", -1);
                dto.wallet.hintsSpent = Long(wallet, "hintsSpent", -1);
                dto.wallet.hintsDueUnix = Long(wallet, "hintsDueUnix", 0);
                dto.wallet.displayName = Str(wallet, "displayName");
                dto.wallet.avatarId = Str(wallet, "avatarId");

                // Absent on a document last written before v15, which reads back as zero —
                // "chosen, but nobody recorded when". That is exactly what the merge treats
                // as the oldest possible choice, so a stamped rename from any updated device
                // wins immediately and nothing has to detect the upgrade.
                dto.wallet.displayNameSetUnix = Long(wallet, "displayNameSetUnix", 0);
                dto.wallet.avatarSetUnix = Long(wallet, "avatarSetUnix", 0);
            }

            if (Map(doc, "daily") is IDictionary<string, object> daily)
            {
                dto.daily.dayKey = (int)Long(daily, "dayKey", 0);
                dto.daily.runs = (int)Long(daily, "runs", 0);
                dto.daily.claimed = (int)Long(daily, "claimed", 0);
            }

            if (Map(doc, "ads") is IDictionary<string, object> ads)
            {
                dto.ads.dayKey = (int)Long(ads, "dayKey", 0);
                dto.ads.lastWatchedUnix = Long(ads, "lastWatchedUnix", 0);
                dto.ads.watched = ReadAdCounts(ads);
            }

            // Absent on a document last written by a build that predates currency rungs,
            // which reads back as three zeros — and zero is what the join treats as "knows
            // nothing", so the local streak simply wins. Nothing has to detect the upgrade.
            if (Map(doc, "streak") is IDictionary<string, object> streak)
            {
                dto.streak.startDay = (int)Long(streak, "startDay", 0);
                dto.streak.lastPlayedDay = (int)Long(streak, "lastPlayedDay", 0);
                dto.streak.collectedThroughDay = (int)Long(streak, "collectedThroughDay", 0);
            }

            // Absent on a document written before rungs were collected by hand, which reads
            // back as no floors and an unseeded flag — and both are what the join treats as
            // "knows nothing", so the local side wins. Nothing has to detect the upgrade.
            dto.eventsSeeded = Bool(doc, "eventsSeeded");
            dto.events = ReadEventFloors(doc);

            // Absent on a document written before the grove existed, which reads back as no
            // rows — and no rows is "this device has no opinion about any slot", so the join
            // takes the local side whole. Nothing has to detect the upgrade.
            dto.homesteadStock = ReadStock(doc);
            dto.homesteadPlaced = ReadPlacements(doc);

            if (Map(doc, "progression") is IDictionary<string, object> progression)
            {
                dto.progression.xpHighWater = Long(progression, "xpHighWater", -1);
                dto.progression.levelHighWater = (int)Long(progression, "levelHighWater", -1);
            }

            if (Map(doc, "cloud") is IDictionary<string, object> cloud)
            {
                dto.cloud.userId = Str(cloud, "userId");
                dto.cloud.revision = Long(cloud, "revision", 0);
                dto.cloud.lastSyncedUnix = Long(cloud, "lastSyncedUnix", 0);
                dto.cloud.deviceId = Str(cloud, "deviceId");
            }

            dto.levels = ReadLevels(doc);
            return dto;
        }

        /// <summary>
        /// Per-placement view counts, tolerating anything that is not one — the same
        /// rule <see cref="StrList"/> follows, for the same reason: this runs on a
        /// background thread during a sync, where an exception costs the whole save.
        /// </summary>
        static AdViewCountDto[] ReadAdCounts(IDictionary<string, object> ads)
        {
            if (!ads.TryGetValue("watched", out object raw) || !(raw is IEnumerable<object> items))
                return new AdViewCountDto[0];

            var counts = new List<AdViewCountDto>();

            foreach (var item in items)
            {
                if (!(item is IDictionary<string, object> entry)) continue;

                string placement = Str(entry, "placement");
                if (string.IsNullOrEmpty(placement)) continue;

                counts.Add(new AdViewCountDto { placement = placement, count = (int)Long(entry, "count", 0) });
            }

            return counts.ToArray();
        }

        /// <summary>
        /// Each event's collected floor, tolerating anything that is not one — the same
        /// rule <see cref="ReadAdCounts"/> follows and for the same reason.
        /// </summary>
        static EventStateDto[] ReadEventFloors(IDictionary<string, object> doc)
        {
            if (!doc.TryGetValue("events", out object raw) || !(raw is IEnumerable<object> items))
                return new EventStateDto[0];

            var floors = new List<EventStateDto>();

            foreach (var item in items)
            {
                if (!(item is IDictionary<string, object> entry)) continue;

                string id = Str(entry, "id");
                if (string.IsNullOrEmpty(id)) continue;

                floors.Add(new EventStateDto { id = id, collectedGoal = (int)Long(entry, "collectedGoal", 0) });
            }

            return floors.ToArray();
        }

        /// <summary>
        /// The grove's arrangement, tolerating anything that is not one — the rule
        /// <see cref="ReadEventFloors"/> follows and for the same reason.
        /// </summary>
        /// <summary>
        /// The stock rows out of a cloud document, dropping anything malformed.
        ///
        /// A row with no id or a count that is not positive is skipped rather than repaired:
        /// <c>GroveStock</c> would drop it a moment later anyway, and letting it through would
        /// make the round trip write back a row it did not receive, which is exactly the
        /// difference <see cref="SaveDelta"/> would then read as a change on every launch.
        /// </summary>
        static HomesteadStockDto[] ReadStock(IDictionary<string, object> doc)
        {
            if (!doc.TryGetValue("homesteadStock", out object raw) || !(raw is IEnumerable<object> items))
                return new HomesteadStockDto[0];

            var rows = new List<HomesteadStockDto>();

            foreach (object item in items)
            {
                if (!(item is IDictionary<string, object> map)) continue;

                string id = Str(map, "id");
                if (string.IsNullOrEmpty(id)) continue;

                long copies = Long(map, "copies", 0L);
                if (copies <= 0L) continue;

                rows.Add(new HomesteadStockDto
                {
                    id = id,
                    copies = copies > GroveStock.MaxCopies ? GroveStock.MaxCopies : (int)copies,
                });
            }

            return rows.ToArray();
        }

        static HomesteadPlacementDto[] ReadPlacements(IDictionary<string, object> doc)
        {
            if (!doc.TryGetValue("homesteadPlaced", out object raw) || !(raw is IEnumerable<object> items))
                return new HomesteadPlacementDto[0];

            var rows = new List<HomesteadPlacementDto>();

            foreach (var item in items)
            {
                if (!(item is IDictionary<string, object> entry)) continue;

                string slot = Str(entry, "slot");
                if (string.IsNullOrEmpty(slot)) continue;

                rows.Add(new HomesteadPlacementDto
                {
                    slot = slot,
                    piece = Str(entry, "piece"),
                    setUnix = Long(entry, "setUnix", 0),

                    // Absent on a document written before pieces could be flipped, which reads
                    // back as false — the value a piece that was never flipped already holds.
                    flipped = Bool(entry, "flipped"),
                });
            }

            return rows.ToArray();
        }

        static LevelRecordDto[] ReadLevels(IDictionary<string, object> doc)
        {
            if (!doc.TryGetValue("levels", out object raw) || !(raw is IDictionary<string, object> map))
                return new LevelRecordDto[0];

            var records = new List<LevelRecordDto>();

            foreach (var pair in map)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                if (!(pair.Value is IDictionary<string, object> entry)) continue;

                records.Add(new LevelRecordDto
                {
                    levelId = pair.Key,
                    stars = (int)Long(entry, "stars", 0),
                    bestMoves = (int)Long(entry, "bestMoves", 0),
                    clears = (int)Long(entry, "clears", 0),
                    firstClearedUnix = Long(entry, "firstClearedUnix", 0),
                    lastPlayedUnix = Long(entry, "lastPlayedUnix", 0),

                    // Absent on every document written before v13, and zero is exactly the
                    // right answer there: no real standing can be zero, so the merge treats
                    // it as "this side knows nothing" and keeps the other device's band.
                    bestRank = (int)Long(entry, "bestRank", 0),

                    // Absent on every document written before v14, and zero is the right
                    // answer there too: it means "never timed", not "instant", so the merge
                    // keeps whichever device actually has a time.
                    bestMillis = (int)Long(entry, "bestMillis", 0),
                });
            }

            return records.ToArray();
        }

        // ------------------------------------------------------------- readers
        /// <summary>
        /// Firestore hands numbers back as long, but a document touched by the console
        /// or a support script can hold an int or a double for the same field. Reading
        /// through one converter means a hand-edited document loads rather than
        /// throwing halfway through a sync.
        /// </summary>
        static long Long(IDictionary<string, object> map, string key, long fallback)
        {
            if (map == null || !map.TryGetValue(key, out object value) || value == null) return fallback;

            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return (long)d;
                case float f: return (long)f;
                case string s: return long.TryParse(s, out long parsed) ? parsed : fallback;
                default: return fallback;
            }
        }

        static string Str(IDictionary<string, object> map, string key)
            => map != null && map.TryGetValue(key, out object value) && value is string s ? s : string.Empty;

        static bool Bool(IDictionary<string, object> map, string key)
            => map != null && map.TryGetValue(key, out object value) && value is bool b && b;

        /// <summary>
        /// A list of strings, tolerating anything that is not one.
        ///
        /// Firestore hands arrays back as <c>List&lt;object&gt;</c>, and a document may
        /// have been written by a newer build or a support tool. Entries that are not
        /// strings are dropped rather than thrown over — this runs on a background
        /// thread during a sync, where an exception costs the whole save.
        /// </summary>
        static string[] StrList(IDictionary<string, object> map, string key)
        {
            if (map == null || !map.TryGetValue(key, out object value)) return new string[0];
            if (!(value is IEnumerable<object> items)) return new string[0];

            var list = new List<string>();
            foreach (var item in items)
                if (item is string s && !string.IsNullOrEmpty(s)) list.Add(s);

            return list.ToArray();
        }

        static object Map(IDictionary<string, object> map, string key)
            => map != null && map.TryGetValue(key, out object value) ? value : null;
    }
}
