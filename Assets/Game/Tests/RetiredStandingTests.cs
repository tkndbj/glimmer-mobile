using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The population standing (<c>bestRank</c>) after its badge and its producer were removed
    /// on 2026-10-07: retired in place, the way <c>bestMillis</c> was (invariant 11f).
    ///
    /// <para>
    /// Nothing produces a new value and nothing draws one. What is left is a promise to every
    /// save that already holds one, and to every shipped build that still writes one: the value
    /// is carried through a run, a reload, a merge and a sync exactly as it arrived. A build that
    /// dropped or zeroed it would disagree with the cloud's copy on every sync, which is the loop
    /// invariant 11f exists to prevent.
    /// </para>
    /// </summary>
    public sealed class RetiredStandingTests
    {
        static readonly LevelId Glade = LevelId.Parse("plain_one");

        static LevelRecord Cleared(int stars, int moves, int rank)
            => new LevelRecord(Glade, stars, moves, 1, 100, 100, rank);

        // ---------------------------------------------------------------- the record
        [Test]
        public void FoldingARunInDoesNotDisturbAStoredStanding()
        {
            var held = Cleared(stars: 2, moves: 20, rank: 80);

            var after = held.WithRun(3, 18, 300);

            Assert.AreEqual(3, after.Stars);
            Assert.AreEqual(18, after.BestMoves);
            Assert.AreEqual(80, after.BestRank, "carried, never recomputed");
        }

        [Test]
        public void AClimbingFoldDoesNotDisturbAStoredStandingEither()
        {
            var held = Cleared(stars: 2, moves: 20, rank: 80);

            Assert.AreEqual(80, held.WithRun(3, 25, 300, true).BestRank);
        }

        [Test]
        public void ARecordWithoutAStandingNeverGainsOne()
        {
            var fresh = LevelRecord.Empty(Glade).WithRun(3, 5, 100);

            Assert.AreEqual(0, fresh.BestRank, "nothing produces a standing any more");
        }

        // ------------------------------------------------------------------ the file
        [Test]
        public void TheStandingSurvivesADtoRoundTrip()
        {
            var held = Cleared(stars: 3, moves: 20, rank: 80);

            Assert.IsTrue(LevelRecord.TryFromDto(held.ToDto(), out var back));
            Assert.AreEqual(80, back.BestRank);
        }

        /// <summary>
        /// Clamped on read to what the retired producer could emit, so a file reads the same
        /// as it did while the badge existed.
        /// </summary>
        [Test]
        public void AForgedStandingIsStillClampedToWhatTheProducerCouldHaveSaid()
        {
            var forged = new LevelRecordDto
            {
                levelId = Glade.Value, stars = 3, bestMoves = 20, clears = 1, bestRank = 4000,
            };

            Assert.IsTrue(LevelRecord.TryFromDto(forged, out var record));
            Assert.AreEqual(95, record.BestRank);
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
        }

        // ----------------------------------------------------------------- the merge
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

        [Test]
        public void TheMergeStillKeepsTheLargerStandingWhicheverSideIsNewer()
        {
            Assert.AreEqual(80, Only(SaveMerge.Join(File(80, 20, 100), File(50, 20, 900))).bestRank);
            Assert.AreEqual(80, Only(SaveMerge.Join(File(50, 20, 900), File(80, 20, 100))).bestRank);
        }

        /// <summary>
        /// Every build from now on writes whatever it read, and a save that never held a
        /// standing writes zero. Zero must keep contributing nothing, or a new device would
        /// erase what an older one recorded.
        /// </summary>
        [Test]
        public void ASideWithoutAStandingContributesNothingRatherThanErasingOne()
        {
            var none = File(0, 20, 900);
            var held = File(80, 20, 100);

            Assert.AreEqual(80, Only(SaveMerge.Join(held, none)).bestRank);
            Assert.AreEqual(80, Only(SaveMerge.Join(none, held)).bestRank);
        }

        [Test]
        public void TheMergeIsStillAJoin()
        {
            var a = File(80, 20, 100);
            var b = File(50, 15, 900);

            var ab = SaveMerge.Join(a, b);
            var ba = SaveMerge.Join(b, a);

            Assert.AreEqual(Only(ab).bestRank, Only(ba).bestRank, "order independent");
            Assert.AreEqual(Only(ab).bestRank, Only(SaveMerge.Join(ab, ab)).bestRank, "idempotent");
            Assert.AreEqual(80, Only(ab).bestRank);
        }

        /// <summary>
        /// The delta still sees the field, so a value adopted from one side reaches the other
        /// rather than the two disagreeing for ever - and an unchanged one owes nothing.
        /// </summary>
        [Test]
        public void TheDeltaStillSeesTheField()
        {
            var remote = File(0, 20, 100);
            var local = File(80, 20, 100);

            Assert.IsFalse(SaveDelta.Between(remote, local).IsEmpty);
            Assert.IsTrue(SaveDelta.Between(local, local).IsEmpty);
        }
    }
}
