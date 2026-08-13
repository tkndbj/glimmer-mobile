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
            var path = new List<string> { Application.dataPath, ".." };
            path.AddRange(parts);
            return Path.GetFullPath(Path.Combine(path.ToArray()));
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
                progression = new ProgressionStateDto { xpHighWater = 4200, levelHighWater = 9 },
                cloud = new CloudStateDto
                {
                    userId = "uid-abc", revision = 17, lastSyncedUnix = 1_699_999_000, deviceId = "dev1",
                },
            };
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
