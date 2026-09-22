using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The contract between the client, the security rules and the server.
    ///
    /// Everything here guards a failure that is silent in development and total in
    /// production. The Editor never talks to Firestore during a normal session, so a
    /// document the rules refuse, or a field the server cannot find, shows up as
    /// "cloud save quietly does nothing" on real devices and nowhere else.
    /// </summary>
    public sealed class CloudWireTests
    {
        static string RepoPath(params string[] parts)
        {
            var path = new List<string> { RepoRoot() };
            path.AddRange(parts);
            return Path.GetFullPath(Path.Combine(path.ToArray()));
        }

        /// <summary>
        /// The checkout, found without the engine when there is none.
        ///
        /// <para>
        /// <c>Application.dataPath</c> is a native call, and the offline runner has no engine
        /// behind it — so every test here that reads the rules file used to be one that "needs
        /// the Editor", which for a gate whose whole job is to fail before the Editor is opened
        /// is the same as not having it (the lesson of 29e). The runner is started from the
        /// checkout, so walking up from the working directory to the folder holding
        /// <c>firebase/</c> answers the same path; the engine's answer is preferred when it is
        /// there, because a batch-mode test run's working directory is wherever Unity was
        /// launched from.
        /// </para>
        /// </summary>
        static string RepoRoot()
        {
            string root = null;

            try { root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }
            catch (System.Exception) { }

            if (root != null && Directory.Exists(Path.Combine(root, "firebase"))) return root;

            string dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "firebase")))
                dir = Path.GetDirectoryName(dir);

            Assert.IsFalse(string.IsNullOrEmpty(dir), "could not find the checkout from " +
                                                       Directory.GetCurrentDirectory());
            return dir;
        }

        static SaveFileDto Populated()
        {
            var ledger = new CurrencyLedger(Currency.Credits);
            ledger.GrantLocally(900);
            ledger.TrySpend(120, 0, "hint", out _);

            return new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                updatedUnix = 1_700_000_000,
                lastPlayedLevelId = "c01_prism_heart",
                legacyImportDone = true,
                checksum = "abc123",
                settings = new SettingsDto
                {
                    music = StoredFlag.From(false),
                    sfx = StoredFlag.From(true),
                    haptics = StoredFlag.From(true),
                    language = "tr",
                },
                wallet = new WalletDto
                {
                    coins = -1, gems = -1, hearts = 4, displayName = "Fern",
                    hintsProduced = 9, hintsSpent = 7, hintsDueUnix = 1_700_003_600,
                    currencies = new[] { ledger.ToDto() },
                },
                levels = new[]
                {
                    new LevelRecordDto
                    {
                        levelId = "c01_first_light", stars = 3, bestMoves = 12, clears = 5,
                        firstClearedUnix = 1_600_000_000, lastPlayedUnix = 1_700_000_000,
                    },
                    new LevelRecordDto
                    {
                        levelId = "c01_twin_streams", stars = 1, bestMoves = 40, clears = 1,
                        firstClearedUnix = 1_650_000_000, lastPlayedUnix = 1_650_000_000,
                    },
                },
                eventsSeeded = true,
                events = new[]
                {
                    new EventStateDto { id = "first_watch", marks = 7, collectedGoal = 2, premiumGoal = 3, pass = true },
                    new EventStateDto { id = "second_bloom", collectedGoal = 1 },
                },
                streak = new StreakStateDto
                {
                    startDay = 20_310, lastPlayedDay = 20_315, collectedThroughDay = 20_314,

                    // The shield too. A fixture that left it out would prove three quarters of
                    // the wire, and the quarter it left out is the one a player paid gems for
                    // — invariant 12a's rule that a round trip is only as complete as what is
                    // fed into it.
                    shieldFromDay = 20_313,
                },

                // Both periods, each with a count and a claim, because the pair is what the
                // join is: a fixture carrying only counters would prove half the wire and the
                // half it left out is the one that pays a chest twice.
                tasks = new TaskStateDto
                {
                    daily = new TaskPeriodDto
                    {
                        key = 20_315,
                        counts = new[]
                        {
                            new TaskCountDto { goal = "raiders", count = 12 },
                            new TaskCountDto { goal = "runs", count = 2 },
                        },
                        claimed = new[] { "d_play" },
                    },
                    weekly = new TaskPeriodDto
                    {
                        key = 2_902,
                        counts = new[] { new TaskCountDto { goal = "wins", count = 3 } },
                        claimed = new string[0],
                    },
                },
                progression = new ProgressionStateDto { xpHighWater = 4200, levelHighWater = 9 },
                cloud = new CloudStateDto
                {
                    userId = "uid-abc", revision = 17, lastSyncedUnix = 1_699_999_000, deviceId = "dev1",
                },
                tipsSeen = new[] { "duskcap", "taproot" },
                daily = new DailyStateDto { dayKey = 20_315, runs = 4, claimed = 2 },
                ads = new AdStateDto
                {
                    dayKey = 20_315,
                    lastWatchedUnix = 1_699_990_000,
                    watched = new[] { new AdViewCountDto { placement = "coin_bonus", count = 2 } },
                },
                companionsOwned = new[] { "coral", "puff" },

                // A container held and a container refunded, because the two travel by
                // different routes and only one of them is written by this device.
                heartContainersOwned = new[] { "gg_heart_vessel_1", "gg_heart_vessel_2" },
                heartContainersRevoked = new[] { "gg_heart_vessel_1" },

                utilityStock = new[]
                {
                    // Both counters, and one row where they differ, because the pair is what the
                    // join is: a fixture carrying only `earned` would prove half the wire and the
                    // half it left out is the one a second device can lose.
                    new UtilityStockDto { id = "firepot", earned = 4, spent = 1 },
                    new UtilityStockDto { id = "mending", earned = 2, spent = 2 },
                },

                // The line: what was bought, what stands on each colour, and when that was
                // arranged. The stamp is here because it *is* the merge rule for the rows beside
                // it — a fixture carrying the arrangement and not its date would prove the wire
                // and leave out the one field that decides which of two devices' lines survives.
                wardsOwned = new[] { "mortar", "rime" },
                wardLoadout = new[]
                {
                    new WardSlotDto { colour = "r", ward = "mortar" },
                    new WardSlotDto { colour = "b", ward = "rime" },
                },
                wardLoadoutSetUnix = 1_699_001_000,

                // The frame they wear and when: a string and its stamp, the name's shape.
                frameWorn = "dragon",
                frameWornSetUnix = 1_699_002_000,

                endlessBest = new[]
                {
                    new EndlessBestDto { level = "s02_endless", wave = 23 },
                },

                // Two seats of one turret at different stars, which is the shape that would catch
                // a mapper keyed on the turret rather than on the holding.
                wardStars = new[]
                {
                    new WardStarDto { ward = "mortar:b", stars = 5 },
                    new WardStarDto { ward = "mortar:r", stars = 3 },
                },
            };
        }

        /// <summary>
        /// A document written before utilities existed reads back as no rows, which is the same
        /// fact as "granted none" — so the join takes the local side whole and nothing has to
        /// detect the upgrade. The bargain every id-keyed section in this file makes.
        /// </summary>
        [Test]
        public void ADocumentWithNoTasksBlockReadsAsNothingDoneYet()
        {
            var doc = FirestoreSaveMapper.ToDocument(Populated());
            doc.Remove("tasks");

            var restored = FirestoreSaveMapper.FromDocument(doc);

            Assert.IsNotNull(restored.tasks);
            Assert.AreEqual(0, restored.tasks.daily.key, "key zero is what the join treats as knowing nothing");
            Assert.AreEqual(0, restored.tasks.weekly.key);
            Assert.IsEmpty(restored.tasks.daily.counts);
            Assert.IsEmpty(restored.tasks.daily.claimed);
        }

        /// <summary>
        /// The round trip is a fixed point: what the writer drops the reader never invents,
        /// or SaveDelta reads every launch as changed and pushes a write for ever.
        /// </summary>
        [Test]
        public void AnEmptyTaskPeriodSurvivesTheRoundTripUnchanged()
        {
            var original = Populated();
            original.tasks.weekly = new TaskPeriodDto();

            var restored = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(original));

            Assert.AreEqual(0, restored.tasks.weekly.key);
            Assert.IsEmpty(restored.tasks.weekly.counts);
            Assert.IsEmpty(restored.tasks.weekly.claimed);
        }

        [Test]
        public void ADocumentWithNoUtilitiesReadsAsHavingBeenGrantedNone()
        {
            var doc = FirestoreSaveMapper.ToDocument(Populated());
            doc.Remove("utilityStock");

            var restored = FirestoreSaveMapper.FromDocument(doc);

            Assert.IsNotNull(restored.utilityStock);
            Assert.AreEqual(0, restored.utilityStock.Length);
        }

        /// <summary>
        /// A row whose counters are both nought says nothing, so it is never written — and if it
        /// were, the round trip would hand back a row it did not receive and
        /// <c>SaveDelta</c> would read every launch as changed, for ever.
        /// </summary>
        [Test]
        public void AnEmptyUtilityRowNeverReachesTheWire()
        {
            var dto = Populated();
            dto.utilityStock = new[]
            {
                new UtilityStockDto { id = "firepot", earned = 3, spent = 0 },
                new UtilityStockDto { id = "surge", earned = 0, spent = 0 },
            };

            var restored = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(dto));

            Assert.AreEqual(1, restored.utilityStock.Length);
            Assert.AreEqual("firepot", restored.utilityStock[0].id);
        }

        // ------------------------------------------------------- rules agreement
        /// <summary>
        /// The rules pin the document's top-level keys with <c>hasOnly</c>. If the
        /// client ever writes a key that list does not contain, Firestore rejects the
        /// write — every write, for every player, permanently — and the only symptom is
        /// that cloud save stops working. Catching that here costs one test.
        /// </summary>
        [Test]
        public void EveryFieldTheClientWritesIsAllowedByTheSecurityRules()
        {
            string rulesPath = RepoPath("firebase", "firestore.rules");
            Assert.IsTrue(File.Exists(rulesPath), $"security rules not found at {rulesPath}");

            var match = Regex.Match(File.ReadAllText(rulesPath), @"hasOnly\(\s*\[(?<keys>[^\]]*)\]",
                                    RegexOptions.Singleline);
            Assert.IsTrue(match.Success, "could not find the hasOnly key list in firestore.rules");

            var allowed = new HashSet<string>();
            foreach (Match quoted in Regex.Matches(match.Groups["keys"].Value, "'([^']+)'"))
                allowed.Add(quoted.Groups[1].Value);

            Assert.IsNotEmpty(allowed);

            var document = FirestoreSaveMapper.ToDocument(Populated());

            foreach (var key in document.Keys)
                Assert.IsTrue(allowed.Contains(key),
                              $"the client writes '{key}', which firestore.rules would reject — " +
                              "every push would fail with permission-denied");
        }

        /// <summary>
        /// Every list the rules bound is bounded by the client first, and the client's bound
        /// is never the larger.
        ///
        /// <para>
        /// The keys test above proves a field is <em>allowed</em>; this proves it stays allowed
        /// as it grows. The rules bound each list with <c>size() &lt;= N</c>, and because
        /// <c>wellFormed()</c> is one expression over the whole document, a list one entry
        /// over its bound does not lose that entry — it loses every save write for that account,
        /// for ever, with no screen saying so (invariant 12a). That is exactly how the lesson
        /// ledger failed on 2026-09-16: the rules said 64, the client said nothing, and the
        /// longest-played account was the first to cross it. Every bound below is now paired
        /// with the constant the client caps the list at, read out of the rules file so a
        /// change to either side alone fails here.
        /// </para>
        /// <para>
        /// Lists with no constant of their own are bounded by content — the companion roster,
        /// the heart-container products and the ad placements — and their bounds are generous
        /// by an order of magnitude against what ships.
        /// </para>
        /// <para>
        /// <b>The grove's four lists are deliberately absent from this table and still bounded
        /// in the rules.</b> Nothing in this build writes them, so there is no client cap to
        /// pair — but the keys stay allow-listed, because dropping a key a rolled-back client
        /// still writes costs that client every save write (invariant 12a).
        /// </para>
        /// </summary>
        [Test]
        public void EveryListTheClientWritesFitsTheBoundTheRulesEnforce()
        {
            string rules = File.ReadAllText(RepoPath("firebase", "firestore.rules"));

            int Bound(string expression)
            {
                var match = Regex.Match(rules, Regex.Escape(expression) + @"\.size\(\)\s*<=\s*(?<n>\d+)");
                Assert.IsTrue(match.Success, $"could not find a size bound for '{expression}' in firestore.rules");
                return int.Parse(match.Groups["n"].Value);
            }

            var pairs = new (string field, string expression, int clientCap)[]
            {
                ("tipsSeen",      "d.tipsSeen",      TipLedger.MaxIds),
                ("utilityStock",  "d.utilityStock",  Utilities.UtilityStock.MaxIds),
                ("wardsOwned",    "d.wardsOwned",    Wards.WardCatalog.MaxModels * Wards.WardLine.Colours.Length),
                ("wardLoadout",   "d.wardLoadout",   Wards.WardLine.Colours.Length),
                ("endlessBest",   "d.endlessBest",   Progression.EndlessLedger.MaxRows),
                ("wardStars",     "d.wardStars",     Wards.WardStarLedger.MaxRows),
                ("events",        "d.events",        Events.SeasonLedger.MaxSeasons),
                ("levels",        "d.levels",        FirestoreSaveMapper.MaxLevelsPerDocument),
                ("tasks.counts",  "p.counts",        Tasks.TaskLedger.MaxGoals),
                ("tasks.claimed", "p.claimed",       Tasks.TaskLedger.MaxClaimed),
                ("tasks.lifetime","d.tasks.lifetime", Tasks.LifetimeTally.MaxGoals),
                ("frameWorn",     "d.frameWorn",     Frames.FrameLedger.MaxIdLength),
            };

            foreach (var (field, expression, clientCap) in pairs)
            {
                int bound = Bound(expression);
                Assert.LessOrEqual(clientCap, bound,
                                   $"the client may write {clientCap} '{field}' rows and the rules allow {bound} — " +
                                   "every save write for an account at the cap would be refused");
            }

            // The one scalar with the same failure shape: a schema bump past the rules' ceiling
            // would refuse every write from the build that made it.
            var schema = Regex.Match(rules, @"d\.schemaVersion\s*<=\s*(?<n>\d+)");
            Assert.IsTrue(schema.Success, "could not find the schemaVersion ceiling in firestore.rules");
            Assert.LessOrEqual(SaveSchema.Version, int.Parse(schema.Groups["n"].Value),
                               "SaveSchema.Version is past the ceiling firestore.rules allows");
        }

        /// <summary>
        /// The server derives earned currency from these two fields. Renaming either
        /// would not break the client, would not break the rules, and would silently
        /// zero every player's earned balance the moment the server recomputed it.
        /// </summary>
        [Test]
        public void TheLedgerFieldsTheServerReadsAreWrittenUnderTheExpectedNames()
        {
            var document = FirestoreSaveMapper.ToDocument(Populated());

            Assert.IsTrue(document.ContainsKey("levels"), "the server reads 'levels' to derive credits");

            var levels = document["levels"] as Dictionary<string, object>;
            Assert.IsNotNull(levels, "the ledger is a map keyed by level id, not an array");
            Assert.AreEqual(2, levels.Count);

            Assert.IsTrue(levels.ContainsKey("c01_first_light"),
                          "server-side derivation looks a level up by its id as the map key");

            var first = levels["c01_first_light"] as Dictionary<string, object>;
            Assert.IsNotNull(first);
            Assert.IsTrue(first.ContainsKey("stars"), "server-side derivation reads 'stars'");
            Assert.IsFalse(first.ContainsKey("levelId"),
                           "the id is the key; carrying it inside as well invites the two to disagree");
        }

        /// <summary>
        /// A duplicate record is not filtered, it is unrepresentable. That is the whole
        /// reason the ledger is keyed rather than listed.
        /// </summary>
        [Test]
        public void ADuplicateRecordCannotSurviveTheWireFormat()
        {
            var dto = Populated();
            dto.levels = new[]
            {
                new LevelRecordDto { levelId = "c01_first_light", stars = 1, bestMoves = 40 },
                new LevelRecordDto { levelId = "c01_first_light", stars = 3, bestMoves = 12 },
            };

            var levels = FirestoreSaveMapper.ToDocument(dto)["levels"] as Dictionary<string, object>;

            Assert.AreEqual(1, levels.Count, "one glade, one entry, whatever the ledger claimed");
        }

        [Test]
        public void AChangedGladeIsAddressableOnItsOwn()
        {
            Assert.AreEqual("levels.c01_first_light",
                            FirestoreSaveMapper.LevelFieldPath("c01_first_light"),
                            "a partial sync writes this path; changing it would rewrite the whole ledger");
        }

        // ------------------------------------------------------------ round trip
        [Test]
        public void ASaveSurvivesTheJourneyToFirestoreAndBack()
        {
            var original = Populated();
            var restored = FirestoreSaveMapper.FromDocument(FirestoreSaveMapper.ToDocument(original));

            Assert.AreEqual(original.schemaVersion, restored.schemaVersion);
            Assert.AreEqual(original.updatedUnix, restored.updatedUnix);
            Assert.AreEqual(original.lastPlayedLevelId, restored.lastPlayedLevelId);
            Assert.AreEqual(original.legacyImportDone, restored.legacyImportDone);
            Assert.AreEqual(original.checksum, restored.checksum);

            Assert.IsFalse(restored.settings.music.Resolve(true), "a muted game stays muted");
            Assert.AreEqual("tr", restored.settings.language);

            Assert.AreEqual(2, restored.levels.Length);
            Assert.AreEqual("c01_first_light", restored.levels[0].levelId);
            Assert.AreEqual(3, restored.levels[0].stars);
            Assert.AreEqual(12, restored.levels[0].bestMoves);
            Assert.AreEqual(5, restored.levels[0].clears);
            Assert.AreEqual(1_600_000_000, restored.levels[0].firstClearedUnix);

            Assert.AreEqual(4200, restored.progression.xpHighWater);
            Assert.AreEqual(9, restored.progression.levelHighWater);

            Assert.AreEqual("uid-abc", restored.cloud.userId);
            Assert.AreEqual(17, restored.cloud.revision);

            Assert.AreEqual(4, restored.wallet.hearts);
            Assert.AreEqual("Fern", restored.wallet.displayName);

            // The hint ledger, whole. All three or none: the count is derived from the first
            // two, and the deadline moves without the count moving — a device that received
            // only what is on screen would have nothing to join against, which is the fault
            // the heart ledger cost a schema version to fix.
            Assert.AreEqual(9, restored.wallet.hintsProduced);
            Assert.AreEqual(7, restored.wallet.hintsSpent);
            Assert.AreEqual(1_700_003_600, restored.wallet.hintsDueUnix);

            // The streak used to stay on the phone, so a player's flame quietly restarted
            // on their second device. All four dates have to make the round trip or the
            // merge on the other side has nothing to join against — and the shield is the
            // sharpest of them, because a player who paid to be away and then opened the game
            // on their tablet must not find the streak they bought cover for already broken.
            Assert.AreEqual(20_310, restored.streak.startDay);
            Assert.AreEqual(20_315, restored.streak.lastPlayedDay);
            Assert.AreEqual(20_314, restored.streak.collectedThroughDay);
            Assert.AreEqual(20_313, restored.streak.shieldFromDay);

            // The tasks: both periods, counters and claims. A counter that stayed on one
            // phone is a task that reads half done on the other, and a claim that stayed is a
            // chest the other device would pay a second time.
            Assert.AreEqual(20_315, restored.tasks.daily.key);
            Assert.AreEqual(2, restored.tasks.daily.counts.Length);
            Assert.AreEqual("raiders", restored.tasks.daily.counts[0].goal);
            Assert.AreEqual(12, restored.tasks.daily.counts[0].count);
            CollectionAssert.AreEqual(new[] { "d_play" }, restored.tasks.daily.claimed);
            Assert.AreEqual(2_902, restored.tasks.weekly.key);
            Assert.AreEqual(3, restored.tasks.weekly.counts[0].count);
            Assert.IsEmpty(restored.tasks.weekly.claimed);

            // The event floors, which the server pays on: `eventCredits` counts a milestone
            // only once the floor has reached it, so a floor that did not make the trip is a
            // collect the wallet never hears about.
            Assert.IsTrue(restored.eventsSeeded);
            Assert.AreEqual(2, restored.events.Length);
            Assert.AreEqual("first_watch", restored.events[0].id);
            Assert.AreEqual(2, restored.events[0].collectedGoal);

            // The other three fields a row carries, and the pass above all: the client's copy of
            // a gem-bought entitlement (47e). It was off the wire once, so a reinstall drew the
            // pass as not bought over a paid column that read as collected.
            Assert.AreEqual(7, restored.events[0].marks);
            Assert.AreEqual(3, restored.events[0].premiumGoal);
            Assert.IsTrue(restored.events[0].pass, "the season pass flag did not survive the wire");
            Assert.AreEqual("second_bloom", restored.events[1].id);
            Assert.AreEqual(1, restored.events[1].collectedGoal);
            Assert.IsFalse(restored.events[1].pass);

            CollectionAssert.AreEqual(new[] { "coral", "puff" }, restored.companionsOwned);

            CollectionAssert.AreEqual(new[] { "duskcap", "taproot" }, restored.tipsSeen);
        }

        /// <summary>
        /// Every field of the save is carried by the fixture above, so that adding one to
        /// <see cref="SaveFileDto"/> and forgetting the wire fails here instead of in a
        /// player's save.
        ///
        /// <para>
        /// This is the test that was missing. <c>groveLandOwned</c> (a grove field, removed
        /// with the Grovement in v33) shipped in save schema v17,
        /// reached <c>SaveDelta</c> — which is the half everybody remembers, because it decides
        /// what a sync sends — and never reached <see cref="FirestoreSaveMapper"/> or
        /// <c>firestore.rules</c>. Every existing wire test passed, because a field the fixture
        /// does not carry is a field no assertion mentions. The round trip could only ever be
        /// as complete as what is fed into it, so the completeness has to be checked rather
        /// than trusted — exactly the argument invariant 4c makes about the manifest writer,
        /// which lost a live event and thirty prices the same way.
        /// </para>
        /// <para>
        /// It deliberately checks the <em>fixture</em> rather than the mapper. A test that read
        /// the mapper's output and demanded a key per field would have to know which fields are
        /// deliberately absent — the currency ledgers are not uploaded at all — and would grow a
        /// list of exceptions that is itself a thing to forget to update. Making the fixture
        /// complete makes the round trip complete, and the round trip already knows what
        /// "survived" means.
        /// </para>
        /// </summary>
        [Test]
        public void EveryFieldOfTheSaveIsCarriedByTheRoundTripFixture()
        {
            var populated = Populated();
            var missing = new List<string>();

            foreach (var field in typeof(SaveFileDto).GetFields())
            {
                object value = field.GetValue(populated);

                bool unset = value == null
                          || (value is string text && text.Length == 0)
                          || (value is System.Array array && array.Length == 0)
                          || (value is bool flag && !flag)
                          || (value is int i && i == 0)
                          || (value is long l && l == 0L);

                if (unset) missing.Add(field.Name);
            }

            CollectionAssert.IsEmpty(missing,
                "these save fields carry nothing in Populated(), so nothing proves they survive "
                + "the wire: " + string.Join(", ", missing));
        }

        /// <summary>
        /// A document written before rewards were collected by hand reads back as no floors
        /// and an unseeded flag. Both are what the join treats as "knows nothing", so the
        /// local side wins and nothing has to detect the upgrade — the same bargain the
        /// streak block makes one version earlier.
        /// </summary>
        [Test]
        public void ADocumentWithNoEventFloorsReadsAsHavingTakenNothing()
        {
            var doc = FirestoreSaveMapper.ToDocument(Populated());
            doc.Remove("events");
            doc.Remove("eventsSeeded");

            var restored = FirestoreSaveMapper.FromDocument(doc);

            Assert.IsFalse(restored.eventsSeeded);
            Assert.IsNotNull(restored.events);
            Assert.AreEqual(0, restored.events.Length);
        }

        /// <summary>
        /// A document written before the streak travelled reads back as four zeros rather
        /// than throwing — and zero is the value the join treats as "knows nothing", so the
        /// local streak simply wins. Nothing has to detect the upgrade.
        /// </summary>
        [Test]
        public void ADocumentWithNoStreakBlockReadsAsAnEmptyOne()
        {
            var document = FirestoreSaveMapper.ToDocument(Populated());
            document.Remove("streak");

            var restored = FirestoreSaveMapper.FromDocument(document);

            Assert.IsNotNull(restored.streak);
            Assert.AreEqual(0, restored.streak.startDay);
            Assert.AreEqual(0, restored.streak.lastPlayedDay);
            Assert.AreEqual(0, restored.streak.collectedThroughDay);
            Assert.AreEqual(0, restored.streak.shieldFromDay);
        }

        /// <summary>
        /// A document written before the hint pool existed reads back as "no opinion" rather
        /// than as a player who has spent every hint they ever had.
        ///
        /// The distinction is the whole migration: -1 is what <c>SaveMerge</c> answers with a
        /// full pool, and 0 would be a real ledger reading empty. Nothing has to detect the
        /// upgrade, which is why v19 needed no migration code.
        /// </summary>
        [Test]
        public void ADocumentWithNoHintLedgerReadsAsHoldingNoOpinion()
        {
            var document = FirestoreSaveMapper.ToDocument(Populated());
            var wallet = document["wallet"] as Dictionary<string, object>;
            Assert.IsNotNull(wallet);

            wallet.Remove("hintsProduced");
            wallet.Remove("hintsSpent");
            wallet.Remove("hintsDueUnix");

            var restored = FirestoreSaveMapper.FromDocument(document);

            Assert.AreEqual(-1, restored.wallet.hintsProduced);
            Assert.AreEqual(-1, restored.wallet.hintsSpent);
            Assert.AreEqual(0, restored.wallet.hintsDueUnix);
        }

        /// <summary>
        /// Currency is deliberately not sent. The balances that count live in a
        /// document the client cannot write, and shipping a second copy inside the save
        /// would leave two places claiming to know what a player holds.
        /// </summary>
        [Test]
        public void CurrencyLedgersAreNotUploadedWithTheSave()
        {
            var document = FirestoreSaveMapper.ToDocument(Populated());
            var wallet = document["wallet"] as Dictionary<string, object>;

            Assert.IsNotNull(wallet);
            Assert.IsFalse(wallet.ContainsKey("currencies"),
                           "granted and spent are server-owned; the save must not carry them");
            Assert.IsFalse(wallet.ContainsKey("coins"), "the retired v1 mirror does not belong on the wire");
        }

        [Test]
        public void ADocumentFromAnUnknownWriterLoadsInsteadOfThrowing()
        {
            // Numbers as doubles and ints, a missing section, an unexpected extra field:
            // all things a support script or a newer build could plausibly leave behind.
            var document = new Dictionary<string, object>
            {
                { "schemaVersion", 2.0 },
                { "updatedUnix", 1700000000 },
                { "somethingFromTheFuture", "ignored" },
                { "levels", new Dictionary<string, object>
                    {
                        { "a", new Dictionary<string, object> { { "stars", 2.0 } } },
                        { "junk", "not a map" },                                     // dropped
                    }
                },
            };

            var dto = FirestoreSaveMapper.FromDocument(document);

            Assert.AreEqual(2, dto.schemaVersion);
            Assert.AreEqual(1, dto.levels.Length, "unusable entries are dropped, not fatal");
            Assert.AreEqual("a", dto.levels[0].levelId);
            Assert.AreEqual(2, dto.levels[0].stars);
            Assert.IsNotNull(dto.settings, "a missing section reads as defaults");
            Assert.IsNotNull(dto.cloud);
        }

        [Test]
        public void AnEmptyOrNullDocumentIsHandled()
        {
            Assert.IsNull(FirestoreSaveMapper.FromDocument(null));
            Assert.IsNull(FirestoreSaveMapper.ToDocument(null));

            var empty = FirestoreSaveMapper.FromDocument(new Dictionary<string, object>());
            Assert.IsNotNull(empty);
            Assert.AreEqual(0, empty.levels.Length);
        }

        // -------------------------------------------------- server agreement
        /// <summary>
        /// The functions run in one region and Firestore lives in another only if
        /// somebody edits one and forgets the other. Both are constants, so the
        /// mismatch is cheap to catch and expensive to debug — it shows up as every
        /// callable timing out on a device and nothing at all in the Editor.
        /// </summary>
        [Test]
        public void TheClientAndTheFunctionsAgreeOnTheRegion()
        {
            string configPath = RepoPath("firebase", "functions", "src", "config.ts");
            Assert.IsTrue(File.Exists(configPath), $"functions config not found at {configPath}");

            var match = Regex.Match(File.ReadAllText(configPath), @"REGION\s*=\s*""([^""]+)""");
            Assert.IsTrue(match.Success, "could not read REGION from config.ts");

#if GLIMMER_FIREBASE
            Assert.AreEqual(match.Groups[1].Value, FirebaseCloudSaveBackend.FunctionsRegion,
                            "the client would call a region the functions are not deployed to");
#else
            // Without the SDK the adapter compiles out, so the constant cannot be read
            // here. Asserting the file still declares one keeps this test honest rather
            // than silently passing forever.
            Assert.IsNotEmpty(match.Groups[1].Value);
#endif
        }

        /// <summary>The bundle id the server validates receipts against must be ours.</summary>
        [Test]
        public void TheFunctionsValidateReceiptsAgainstThisAppsBundleId()
        {
            string configPath = RepoPath("firebase", "functions", "src", "config.ts");
            var match = Regex.Match(File.ReadAllText(configPath), @"BUNDLE_ID\s*=\s*""([^""]+)""");
            Assert.IsTrue(match.Success, "could not read BUNDLE_ID from config.ts");

            Assert.AreEqual(Application.identifier, match.Groups[1].Value,
                            "a mismatch here rejects every genuine purchase as belonging to another app");
        }
    }
}
