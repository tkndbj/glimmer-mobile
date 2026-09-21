using System;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The chain that carries an endless run to a public board, joint by joint.
    ///
    /// <para>
    /// <b>Every link here fails silently if it breaks</b>, which is why they are pinned
    /// together rather than one per fixture. A run reaches the Endless Watch only if the
    /// ledger records it, the record asks for a sync, the sync's receipt builds a card
    /// carrying the wave, the wave reaches the card's <em>fingerprint</em> so a publish is
    /// judged owed, and the publish gate lets a keeper with nothing else through. Break any
    /// one and nothing throws, nothing logs and no gate goes red — a player simply holds out
    /// further than anybody alive and never appears on the list they did it for. That is
    /// invariant 19j's fault arriving through a field instead of a stale read.
    /// </para>
    /// <para>
    /// The server half of the same rule is <c>bestWave</c> in <c>functions/src/grove.ts</c>,
    /// driven by <c>firebase/functions/test/grove.mjs</c>. The two have to read a save the same
    /// way and bound it the same way, or the number a device draws and the number on the board
    /// disagree for the one account that reaches the ceiling.
    /// </para>
    /// </summary>
    public sealed class EndlessBoardTests
    {
        static readonly LevelId Watch = LevelId.Parse("s02_endlesswatch");
        static readonly LevelId Other = LevelId.Parse("s09_elsewhere");

        [SetUp]
        public void Reset()
        {
            GroveRanks.Clear();

            // No `ResetForTests` of its own: reading an empty file is what clears this ledger
            // in the game too, so the test uses the door the game uses.
            EndlessLedger.LoadFrom(new SaveFileDto());

            CloudSaveService.ForgetSyncRequestForTests();
            SyncTriggers.Attach();
        }

        [TearDown]
        public void Restore()
        {
            GroveRanks.Clear();
            EndlessLedger.LoadFrom(new SaveFileDto());
            CloudSaveService.ForgetSyncRequestForTests();
        }

        static SaveFileDto Saved(params (string level, int wave)[] rows)
        {
            var dto = new SaveFileDto { endlessBest = new EndlessBestDto[rows.Length] };

            for (int i = 0; i < rows.Length; i++)
                dto.endlessBest[i] = new EndlessBestDto { level = rows[i].level, wave = rows[i].wave };

            return dto;
        }

        // ------------------------------------------------------------- the reading
        [Test]
        public void TheLanesBestIsTheBestOfEveryRow()
        {
            EndlessLedger.Record(Watch, 12);
            EndlessLedger.Record(Other, 31);

            // The board is about the lane rather than about one level (invariant 43), so a
            // second Infinite level joining the ladder must not need a new board id or a new
            // field in the save.
            Assert.AreEqual(31, EndlessLedger.Best);
            Assert.AreEqual(12, EndlessLedger.BestFor(Watch));
        }

        [Test]
        public void AKeeperWhoHasNeverPlayedTheLaneReadsAsNought()
        {
            Assert.AreEqual(0, EndlessLedger.Best);
            Assert.AreEqual(0, EndlessLedger.BestIn(null));
            Assert.AreEqual(0, EndlessLedger.BestIn(new SaveFileDto()));

            // Nought is what keeps the field off the card, which is what keeps every card in
            // the game out of the endless board's index. It has to be a plain nought and not
            // merely "absent", because the server writes the field only when it is positive.
            Assert.AreEqual(0, EndlessLedger.BestIn(Saved(("", 400), ("s02_endlesswatch", 0))));
        }

        [Test]
        public void TheSaveIsReadTheWayTheServerReadsIt()
        {
            // Mirrors `bestWave` in functions/src/grove.ts. A publish is judged on the file
            // the server holds, so this is the reading that decides whether one is owed —
            // and if the two sides disagree, the device asks for a publish the server's card
            // will not match, for ever.
            Assert.AreEqual(31, EndlessLedger.BestIn(
                Saved(("s02_endlesswatch", 12), ("s09_elsewhere", 31), ("s10_lower", 4))));

            Assert.AreEqual(3, EndlessLedger.BestIn(Saved(("", 900), ("a", 3))));
            Assert.AreEqual(0, EndlessLedger.BestIn(Saved(("a", -9))));
        }

        [Test]
        public void TheWaveIsBoundedOnBothSidesOfThePublish()
        {
            // Not a clamp on a derivation — there is nothing to derive it from — but the one
            // defence a public number has when the server cannot recompute it. The ceiling
            // must be the same on the card, in the ledger and in `MAX_WAVE`.
            Assert.AreEqual(EndlessLedger.MaxWave, EndlessLedger.BestIn(Saved(("a", 1000000))));

            EndlessLedger.Record(Watch, int.MaxValue);
            Assert.AreEqual(EndlessLedger.MaxWave, EndlessLedger.Best);

            var card = new GroveCard("uid", "Fern", 4, int.MaxValue, 1L);
            Assert.AreEqual(EndlessLedger.MaxWave, card.BestWave);
        }

        // ---------------------------------------------------------------- the chain
        [Test]
        public void ANewBestAsksForASync()
        {
            Assert.IsFalse(CloudSaveService.IsSyncPending);

            Assert.IsTrue(EndlessLedger.Record(Watch, 9));
            Assert.IsTrue(CloudSaveService.IsSyncPending,
                          "a run that beat the record never reaches the server promptly");
        }

        [Test]
        public void ARunThatBeatNothingAsksForNothing()
        {
            EndlessLedger.Record(Watch, 9);
            CloudSaveService.ForgetSyncRequestForTests();

            Assert.IsFalse(EndlessLedger.Record(Watch, 4));
            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        [Test]
        public void ASaveBeingReadNeverAsksForASync()
        {
            // `Beaten` and never `Changed`, which is `SyncTriggers`' whole warning: a sync
            // adopts a merge by loading a save, so a request raised here is a request raised
            // every few seconds for the life of the process.
            EndlessLedger.LoadFrom(Saved(("s02_endlesswatch", 40)));

            Assert.AreEqual(40, EndlessLedger.Best);
            Assert.IsFalse(CloudSaveService.IsSyncPending);
        }

        // ------------------------------------------------------------- the publish
        [Test]
        public void TheWaveIsPartOfWhatAVisitorCanSee()
        {
            var bare = new GroveCard("uid", "Fern", 4, 0, 1L);
            var held = new GroveCard("uid", "Fern", 4, 17, 1L);

            // If the fingerprint does not move, the publish policy believes the card it
            // already sent is current and the new best never leaves the phone.
            Assert.AreNotEqual(bare.Fingerprint(), held.Fingerprint());
        }

        [Test]
        public void AWaveIsTheOnlyThingWorthPublishing()
        {
            var nothing = new GroveCard("uid", "Fern", 4, 0, 1L);
            var wave = new GroveCard("uid", "Fern", 4, 17, 1L);

            // The bar is still a bar — an account that has played nothing is a document, a
            // write and a row in the decile sample for a keeper with nothing to show. What
            // changed on 2026-09-21 is that there is one way over it rather than two: the
            // grove's worth used to be the other, and there is no grove.
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(nothing));
            Assert.IsFalse(GrovePublishPolicy.WorthPublishing(null));
            Assert.IsTrue(GrovePublishPolicy.WorthPublishing(wave));
        }

        // ------------------------------------------------------------ the standing
        /// <summary>
        /// Nine wave counts and a sample big enough to mean them — what a night's job publishes.
        /// </summary>
        static GroveRankPublication Published(int samples, params long[] deciles)
            => new GroveRankPublication(GroveRankTable.None,
                                        new GroveRankTable(samples, deciles),
                                        null, 1L);

        [Test]
        public void AWaveStandsAgainstTheKeepersWhoHaveRunTheLane()
        {
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));

            // Higher is better, exactly as grove worth is — the same table used twice rather
            // than copied or given a flag.
            Assert.AreEqual(GroveRankTable.MinRank, GroveRanks.Waves.TopPercent(400));
            Assert.AreEqual(GroveRankTable.MaxRank, GroveRanks.Waves.TopPercent(1));
            Assert.AreEqual(50, GroveRanks.Waves.TopPercent(10));

            // And the two distributions do not bleed into each other.
            Assert.IsFalse(GroveRanks.Table.IsUsable);
        }

        [Test]
        public void AWaveNobodyCanBeMeasuredAgainstSaysNothing()
        {
            // Never published at all — a first launch, an offline build, a game whose first day
            // it is. This is the state the board ships in and it must not invent a percentile.
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(40));

            // Published, but over too few watchers to mean anything (invariant 19c's own bar).
            GroveRanks.Publish(Published(GroveRankTable.MinimumSamples - 1,
                                         2, 4, 6, 8, 10, 12, 14, 16, 18));
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(40));

            // And a keeper who has never run the lane is not in the population being described.
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));
            Assert.AreEqual(-1, GroveRanks.Waves.TopPercent(0));
        }

        [Test]
        public void TheNameplateSaysTheMostInformativeTrueThingItCan()
        {
            // Never run: the plate names the absence. "Best wave 0" is a bad score where a
            // player who has never run has no score.
            string unplayed = EndlessHub.CaptionFor(0);
            Assert.AreEqual(unplayed, EndlessHub.CaptionFor(-4));

            // Run, but nothing to stand against yet — which is every player on the day this
            // ships, and every player with no backend.
            string label = EndlessHub.CaptionFor(23);
            Assert.AreNotEqual(unplayed, label);

            // Run, and a population exists: the standing replaces the label, because the disc
            // above it is already a large number saying "best wave".
            GroveRanks.Publish(Published(4000, 2, 4, 6, 8, 10, 12, 14, 16, 18));

            string standing = EndlessHub.CaptionFor(23);
            Assert.AreNotEqual(label, standing);
            Assert.AreNotEqual(unplayed, standing);

            // Three states, three different sentences, none of them empty. **Whether the keys
            // resolve is deliberately not asserted here**: this runner loads no localisation, so
            // `Loc.Get` echoes the key back and every one of these would read as a missing
            // string. That question belongs to `Tools/verify/loc.py`, which resolves every
            // key-shaped literal in the source against `loc/en.json` and fails the build on one
            // that is absent (invariant 6) — a fixture asserting it here would be testing the
            // harness.
            foreach (string line in new[] { unplayed, label, standing }) Assert.IsNotEmpty(line);
        }

        [Test]
        public void ACardBuiltFromASaveCarriesThatSavesWave()
        {
            // The card a publish is judged on is built from the file the server holds, never
            // from the live ledger — a run finished while a push was in flight is on the
            // device and not on the server. So a ledger holding more than the save must not
            // leak into the fingerprint, or the device marks a card published that was never
            // built from what it is looking at.
            EndlessLedger.Record(Watch, 99);

            var card = GroveCard.OfSave(Saved(("s02_endlesswatch", 40)), "uid", 4, 1L);

            Assert.AreEqual(40, card.BestWave);
        }
    }
}
