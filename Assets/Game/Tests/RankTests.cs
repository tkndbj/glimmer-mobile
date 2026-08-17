using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The standing a map node wears, and the one property the whole design rests on:
    /// it only ever climbs.
    ///
    /// <para>
    /// Everything else in a level record is a fact about the player's own play, and a fact
    /// like that is stable — a three-star clear is a three-star clear for ever. A standing
    /// is a fact about a <em>population</em>, and the population moves: <c>publishGroveStats</c>
    /// re-reads five thousand fresh saves a day, and a game that grows from ten thousand
    /// players to a hundred thousand grows a faster field with it. That makes a stored
    /// standing the only number in this save file whose honest value can get <em>worse</em>
    /// through no act of the player's.
    /// </para>
    /// <para>
    /// So the two obvious rules are both wrong, and these tests exist to keep them out.
    /// Recomputing for display means a node quietly reading 66% next month where it read 71%
    /// today, which the player will correctly read as the game having lost their score.
    /// Freezing whatever was current when the record was set is worse: a player who comes
    /// back and beats their own move count against a bigger field is <em>demoted for playing
    /// better</em>. Promotion by <c>max</c> is the only rule with neither failure, and it is
    /// the same rule invariant 11b already forced on hearts, the streak and the event track.
    /// </para>
    /// </summary>
    public sealed class RankTests
    {
        static readonly LevelId Glade = LevelId.Parse("plain_one");

        /// <summary>
        /// Deciles ten through ninety, so a move count is its own percentile and the
        /// arithmetic in a test reads as arithmetic.
        /// </summary>
        static LevelStats Field(int samples = 1000)
            => new LevelStats(samples, new[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 });

        /// <summary>
        /// The same shape, twice as fast: every decile halved. This is what a year of growth
        /// looks like to a player who has not touched the glade.
        /// </summary>
        static LevelStats FasterField(int samples = 1000)
            => new LevelStats(samples, new[] { 5, 10, 15, 20, 25, 30, 35, 40, 45 });

        static LevelRecord Cleared(int stars, int moves, int rank = 0)
            => new LevelRecord(Glade, stars, moves, 1, 100, 100, rank);

        // ------------------------------------------------------------- the ladder
        [Test]
        public void TheBandLadderReadsOffThePercentSlower()
        {
            Assert.AreEqual(RankBand.Top10, RankTier.Of(95), "the cap is the top band");
            Assert.AreEqual(RankBand.Top10, RankTier.Of(90), "the floor is inclusive");
            Assert.AreEqual(RankBand.Top25, RankTier.Of(89));
            Assert.AreEqual(RankBand.Top25, RankTier.Of(75));
            Assert.AreEqual(RankBand.Top50, RankTier.Of(74));
            Assert.AreEqual(RankBand.Top50, RankTier.Of(50));
            Assert.AreEqual(RankBand.None, RankTier.Of(49));
        }

        /// <summary>
        /// Zero is the value <c>JsonUtility</c> writes into a field a v12 file never had, and
        /// -1 is what <see cref="LevelStats.PercentSlower"/> answers when it has nothing to
        /// say. Both have to land on "draw nothing" with no special case anywhere, because
        /// that absence is the whole reason this field needed no migration.
        /// </summary>
        [Test]
        public void AnUnwrittenOrUnknowableStandingIsSimplyAbsent()
        {
            Assert.AreEqual(RankBand.None, RankTier.Of(0));
            Assert.AreEqual(RankBand.None, RankTier.Of(-1));

            Assert.IsFalse(RankTier.IsShown(0));
            Assert.IsFalse(RankTier.IsShown(-1));

            Assert.IsNull(RankTier.KeyOf(RankBand.None),
                          "an unranked glade has no label; asking for one is a caller that forgot to check");
        }

        [Test]
        public void EveryDrawnBandHasAWrittenOutKey()
        {
            // Written out rather than assembled, so the build's loc gate can see them. If one
            // is ever added to the enum without a key, this is where it is caught.
            Assert.AreEqual("ui.rank.top10", RankTier.KeyOf(RankBand.Top10));
            Assert.AreEqual("ui.rank.top25", RankTier.KeyOf(RankBand.Top25));
            Assert.AreEqual("ui.rank.top50", RankTier.KeyOf(RankBand.Top50));
        }

        /// <summary>
        /// The map and the victory panel draw the same comparison from the same floor. If they
        /// drifted apart a player would be congratulated on a run the map then declined to
        /// mark, which reads as one of the two screens being broken.
        /// </summary>
        [Test]
        public void TheMapAndTheVictoryPanelAgreeOnWhatIsWorthSaying()
        {
            var field = Field();

            for (int moves = 1; moves <= 120; moves++)
                Assert.AreEqual(RankTier.IsShown(field.PercentSlower(moves)),
                                field.IsWorthSaying(moves),
                                $"the two disagree at {moves} moves");
        }

        // ---------------------------------------------------- promotion, not replacement
        /// <summary>
        /// The scenario this design exists for.
        ///
        /// <para>
        /// A player clears a glade in twenty moves when the game has ten thousand keepers and
        /// ranks well. A year later, with ten times the players and a much faster field, they
        /// come back and clear it in <em>fifteen</em>. Their honest standing against today's
        /// field is worse than the one they held. Storing today's answer would tell somebody
        /// who just beat their own record that they had gone backwards.
        /// </para>
        /// </summary>
        [Test]
        public void BeatingYourOwnRecordAgainstATougherFieldNeverDemotesYou()
        {
            // Twenty moves against the original field: four fifths of keepers were slower.
            var atLaunch = Cleared(stars: 3, moves: 0).WithRun(3, 20, 200, Field());
            Assert.AreEqual(80, atLaunch.BestRank);
            Assert.AreEqual(RankBand.Top25, RankTier.Of(atLaunch.BestRank));

            // Fifteen moves against a field that has since halved its times: p30, so only 70%
            // are slower. A genuinely better run, and a genuinely worse standing.
            Assert.AreEqual(70, FasterField().PercentSlower(15),
                            "the premise: today's honest answer is lower than the one held");

            var later = atLaunch.WithRun(3, 15, 300, FasterField());

            Assert.AreEqual(15, later.BestMoves, "the move record still improves");
            Assert.AreEqual(80, later.BestRank, "the standing is kept, never replaced");
            Assert.AreEqual(RankBand.Top25, RankTier.Of(later.BestRank));
        }

        /// <summary>
        /// The other half: a standing must not sag while its owner is away. This is what the
        /// naive "recompute for display" version gets wrong, and the reason the number is
        /// stored at all rather than derived on the fly like everything else here.
        /// </summary>
        [Test]
        public void AStandingNeverSagsWhenThePopulationImprovesAndNothingIsPlayed()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 80);

            var swept = held.WithRank(FasterField());

            Assert.AreSame(held, swept, "nothing improved, so nothing is rewritten");
            Assert.AreEqual(80, swept.BestRank);
        }

        [Test]
        public void ABetterFieldPositionIsAdoptedImmediately()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 50);

            var swept = held.WithRank(Field());

            Assert.AreEqual(80, swept.BestRank, "a genuine promotion is taken");
            Assert.AreNotSame(held, swept);
        }

        /// <summary>
        /// The standing is taken over the record after the run is folded in, never over the
        /// run's own move count. A replay that came nowhere near the record would otherwise be
        /// ranked on its own merits — and since a standing only rises, it would achieve nothing
        /// at all, silently, on the one path that exists to capture it.
        /// </summary>
        [Test]
        public void AWorseReplayIsStillRankedOnTheRecordItDidNotBeat()
        {
            var held = Cleared(stars: 3, moves: 20);

            // Sixty moves is a bad run. Ranked as sixty it would read 40% and be discarded;
            // ranked on the surviving record of twenty it reads 80%.
            var after = held.WithRun(3, 60, 300, Field());

            Assert.AreEqual(20, after.BestMoves);
            Assert.AreEqual(80, after.BestRank, "ranked on the record, not on the run");
        }

        // ------------------------------------------------------------- the backfill
        /// <summary>
        /// v13 is the first section of this save file to need no migration code, and this is
        /// why: a standing is derived from a move count that was already on disk, so the first
        /// published table fills in a whole account's history at once. Without it a year-old
        /// player would earn bands only on glades they happened to replay.
        /// </summary>
        [Test]
        public void AV12RecordEarnsItsStandingFromTheMoveCountAlreadyStored()
        {
            var beforeTheFeatureExisted = Cleared(stars: 3, moves: 20, rank: 0);

            var swept = beforeTheFeatureExisted.WithRank(Field());

            Assert.AreEqual(80, swept.BestRank);
            Assert.AreEqual(3, swept.Stars, "the sweep touches nothing else");
            Assert.AreEqual(20, swept.BestMoves);
            Assert.AreEqual(1, swept.Clears);
            Assert.AreEqual(100, swept.LastPlayedUnix);
        }

        [Test]
        public void AGladeNeverClearedEarnsNothingHoweverGoodTheField()
        {
            var untouched = new LevelRecord(Glade, 0, 0, 0, 0, 0);

            Assert.AreSame(untouched, untouched.WithRank(Field()));
            Assert.AreEqual(0, untouched.WithRank(Field()).BestRank);
        }

        [Test]
        public void AFieldTooThinToSpeakFromLeavesTheStandingAlone()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 0);
            var thin = Field(LevelStats.MinimumSamples - 1);

            Assert.AreSame(held, held.WithRank(thin));
            Assert.AreEqual(0, held.WithRank(thin).BestRank, "-1 must not become a standing");
        }

        [Test]
        public void NoFieldAtAllLeavesTheStandingAlone()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 80);

            Assert.AreEqual(80, held.WithRun(3, 18, 300, LevelStats.None).BestRank);
            Assert.AreEqual(80, held.WithRank(LevelStats.None).BestRank);
        }

        /// <summary>
        /// The three-argument fold is what every path that does not know about populations
        /// still calls. It must leave the standing exactly where it was rather than clearing it.
        /// </summary>
        [Test]
        public void TheOverloadWithoutAFieldPreservesWhatWasHeld()
        {
            var held = Cleared(stars: 2, moves: 20, rank: 80);

            var after = held.WithRun(3, 18, 300);

            Assert.AreEqual(3, after.Stars);
            Assert.AreEqual(18, after.BestMoves);
            Assert.AreEqual(80, after.BestRank);
        }

        // ------------------------------------------------------------------- the file
        [Test]
        public void TheStandingSurvivesADtoRoundTrip()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 80);

            Assert.IsTrue(LevelRecord.TryFromDto(held.ToDto(), out var back));
            Assert.AreEqual(80, back.BestRank);
        }

        /// <summary>
        /// A standing buys nothing — a band on a map node, no currency and no advantage — which
        /// is what makes it safe to store client-side at all, by the same test invariant 15
        /// applies to a companion entitlement. It is still clamped to what the producer can
        /// emit, so a hand-edited file cannot invent a tier above the ladder.
        /// </summary>
        [Test]
        public void AForgedStandingIsClampedToWhatThePopulationCouldHaveSaid()
        {
            var forged = new LevelRecordDto
            {
                levelId = Glade.Value, stars = 3, bestMoves = 20, clears = 1,
                bestRank = 4000,
            };

            Assert.IsTrue(LevelRecord.TryFromDto(forged, out var record));
            Assert.AreEqual(LevelStats.MaxRank, record.BestRank);
        }

        [Test]
        public void ANegativeStandingReadsAsUnranked()
        {
            var broken = new LevelRecordDto
            {
                levelId = Glade.Value, stars = 3, bestMoves = 20, clears = 1, bestRank = -7,
            };

            Assert.IsTrue(LevelRecord.TryFromDto(broken, out var record));
            Assert.AreEqual(0, record.BestRank);
            Assert.AreEqual(RankBand.None, RankTier.Of(record.BestRank));
        }

        // ------------------------------------------------------------------ the merge
        static SaveFileDto File(int rank, int moves, long updatedUnix)
            => new SaveFileDto
            {
                schemaVersion = SaveSchema.Version,
                updatedUnix = updatedUnix,
                settings = new SettingsDto(),
                wallet = WalletDto.Unwritten(),
                levels = new[]
                {
                    new LevelRecordDto
                    {
                        levelId = Glade.Value, stars = 3, bestMoves = moves, clears = 1,
                        firstClearedUnix = updatedUnix, lastPlayedUnix = updatedUnix,
                        bestRank = rank,
                    },
                },
                progression = ProgressionStateDto.Unwritten(),
            };

        static LevelRecordDto Only(SaveFileDto dto) => dto.levels[0];

        /// <summary>
        /// Larger wins, in both directions, whichever save is newer. Two devices that ranked
        /// the same move count months apart hold different honest answers, and the larger is
        /// the one that saw the player at their best.
        /// </summary>
        [Test]
        public void TheMergeKeepsTheBetterStandingRegardlessOfWhichSideIsNewer()
        {
            Assert.AreEqual(80, Only(SaveMerge.Join(File(80, 20, 100), File(50, 20, 900))).bestRank);
            Assert.AreEqual(80, Only(SaveMerge.Join(File(50, 20, 900), File(80, 20, 100))).bestRank);
        }

        /// <summary>
        /// A device still on v12 writes zero. Zero is unreachable for a real standing
        /// (<see cref="LevelStats.MinRank"/> is five), so it reads as "this side knows nothing"
        /// and cannot clear a band the other device earned. This is the property invariant 11b
        /// demands before a field may join the merge, and the one hearts originally lacked.
        /// </summary>
        [Test]
        public void AnOlderBuildContributesNothingRatherThanErasingABand()
        {
            var older = File(0, 20, 900);
            var mine = File(80, 20, 100);

            Assert.AreEqual(80, Only(SaveMerge.Join(mine, older)).bestRank);
            Assert.AreEqual(80, Only(SaveMerge.Join(older, mine)).bestRank);
        }

        /// <summary>
        /// It is a join: idempotent and order-independent, so a sync — pull, join, push —
        /// converges rather than the two devices trading answers for ever.
        /// </summary>
        [Test]
        public void TheMergeIsAJoin()
        {
            var a = File(80, 20, 100);
            var b = File(50, 15, 900);

            var ab = SaveMerge.Join(a, b);
            var ba = SaveMerge.Join(b, a);

            Assert.AreEqual(Only(ab).bestRank, Only(ba).bestRank, "order independent");
            Assert.AreEqual(Only(ab).bestRank, Only(SaveMerge.Join(ab, ab)).bestRank, "idempotent");
            Assert.AreEqual(Only(ab).bestRank, Only(SaveMerge.Join(ab, a)).bestRank);
            Assert.AreEqual(80, Only(ab).bestRank);
        }

        /// <summary>
        /// A backfill is the one change here no run produced, so the delta has to notice it or
        /// the first published table would raise bands on the device and upload none of them.
        /// </summary>
        [Test]
        public void ABackfilledStandingIsWorthSyncing()
        {
            var remote = File(0, 20, 100);
            var local = File(80, 20, 100);

            var delta = SaveDelta.Between(remote, local);

            Assert.IsFalse(delta.IsEmpty, "a promoted standing must reach the server");
            Assert.AreEqual(1, delta.ChangedLevelIds.Count);
            Assert.AreEqual(Glade.Value, delta.ChangedLevelIds[0]);

            Assert.IsTrue(SaveDelta.Between(local, local).IsEmpty, "and an unchanged one must not");
        }
    }
}
