using System.Collections.Generic;
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

                { "checksum", dto.checksum ?? string.Empty },

                { "settings", new Dictionary<string, object>
                    {
                        { "music", (long)(dto.settings?.music.state ?? 0) },
                        { "sfx", (long)(dto.settings?.sfx.state ?? 0) },
                        { "haptics", (long)(dto.settings?.haptics.state ?? 0) },
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
                        { "displayName", dto.wallet?.displayName ?? string.Empty },
                        { "avatarId", dto.wallet?.avatarId ?? string.Empty },
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
                dto.wallet.displayName = Str(wallet, "displayName");
                dto.wallet.avatarId = Str(wallet, "avatarId");
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
