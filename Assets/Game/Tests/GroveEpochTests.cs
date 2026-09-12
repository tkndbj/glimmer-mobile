using GlimmerGrove.Homestead;
using GlimmerGrove.Persistence;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The one rule in the save file that lets something be taken away.
    ///
    /// <para>
    /// <b>Why it needs a fixture of its own.</b> Everything the grove stores is joined so that
    /// nothing is ever lost — purchases and land by union, placements by the later stamp — and
    /// that is invariant 11's promise. It also means <em>clearing a grove is not expressible</em>:
    /// clear the fields and push, and the next pull joins the server's copy back; wipe the
    /// server, and the first device that has not synced pushes it back up. Neither half can win,
    /// because a monotonic join has no way to say "this is gone", only "I have not heard of it".
    /// </para>
    /// <para>
    /// So the cases below are about <em>convergence</em> rather than about any one device: two
    /// devices at different epochs have to end up holding the same grove whichever order they
    /// merge in, and whichever of them was asked. Anything less and the two push at each other
    /// for ever, which is the exact failure the facing's tie-break was written against.
    /// </para>
    /// </summary>
    public sealed class GroveEpochTests
    {
        static SaveFileDto Save(int epoch, params string[] land)
            => new SaveFileDto
            {
                groveEpoch = epoch,
                groveLandOwned = land,
                homesteadPlaced = new[]
                {
                    new HomesteadPlacementDto
                    {
                        slot = GroveFloor.TileId(2, 2),
                        piece = "piece_" + epoch,
                        setUnix = 1000 + epoch,
                    },
                },
                homesteadStock = new[]
                {
                    new HomesteadStockDto { id = "piece_" + epoch, copies = 3 },
                },
            };

        static string PieceAt(SaveFileDto dto, int col, int row)
        {
            foreach (var placement in dto.homesteadPlaced ?? new HomesteadPlacementDto[0])
                if (placement.slot == GroveFloor.TileId(col, row))
                    return placement.piece;
            return null;
        }

        // ================================================================= reading
        [Test]
        public void ASaveWithNoEpochBelongsToTheGenerationBeforeThisOne()
        {
            // `JsonUtility` writes 0 into a field an older file never had, and 0 is exactly what
            // such a file means: it was written before the catalogue was replaced.
            Assert.AreEqual(0, GroveEpoch.Of(new SaveFileDto()));
            Assert.IsFalse(GroveEpoch.IsCurrent(new SaveFileDto()));
            Assert.IsTrue(GroveEpoch.IsCurrent(Save(GroveEpoch.Current)));
        }

        [Test]
        public void AnEpochAheadOfThisBuildIsStillCurrent()
        {
            // A device a drop ahead has a grove this build cannot fully name, and the honest
            // reading is to keep it rather than to discard it: the ids it holds are content
            // from the future, which the catalogue already carries through untouched.
            Assert.IsTrue(GroveEpoch.IsCurrent(Save(GroveEpoch.Current + 1)));
        }

        // ================================================================ merging
        [Test]
        public void TheNewerEpochsGroveReplacesTheOlderOneRatherThanAbsorbingIt()
        {
            var old = Save(0, "east_meadow");
            var now = Save(GroveEpoch.Current);

            var joined = SaveMerge.Join(now, old);

            Assert.AreEqual(GroveEpoch.Current, joined.groveEpoch);
            Assert.AreEqual("piece_" + GroveEpoch.Current, PieceAt(joined, 2, 2));
            Assert.AreEqual(0, joined.groveLandOwned.Length,
                            "land is a union everywhere else, and this is the one thing that clears it");
        }

        [Test]
        public void ItConvergesWhicheverWayRoundTheDevicesMeet()
        {
            var old = Save(0, "east_meadow");
            var now = Save(GroveEpoch.Current);

            var oneWay = SaveMerge.Join(now, old);
            var otherWay = SaveMerge.Join(old, now);

            Assert.AreEqual(oneWay.groveEpoch, otherWay.groveEpoch);
            Assert.AreEqual(PieceAt(oneWay, 2, 2), PieceAt(otherWay, 2, 2));
            Assert.AreEqual(oneWay.groveLandOwned.Length, otherWay.groveLandOwned.Length);
        }

        [Test]
        public void AStaleDeviceCannotPushAnOldGroveBackHoweverLongItStaysAway()
        {
            // The failure the epoch exists to make impossible. Without it, the device that has
            // been offline since before the reset wins every union it takes part in.
            var stale = Save(0, "east_meadow", "west_hollow");

            var settled = SaveMerge.Join(Save(GroveEpoch.Current), stale);
            var again = SaveMerge.Join(settled, stale);
            var andAgain = SaveMerge.Join(stale, again);

            Assert.AreEqual(0, andAgain.groveLandOwned.Length);
            Assert.AreEqual("piece_" + GroveEpoch.Current, PieceAt(andAgain, 2, 2));
        }

        [Test]
        public void TwoDevicesOfTheSameGenerationJoinExactlyAsBefore()
        {
            // The epoch must be invisible in the ordinary case, which is every case but one.
            var mine = Save(GroveEpoch.Current, "east_meadow");
            var theirs = Save(GroveEpoch.Current, "west_hollow");

            var joined = SaveMerge.Join(mine, theirs);

            Assert.AreEqual(2, joined.groveLandOwned.Length, "still a union");
        }

        // ================================================================ loading
        [Test]
        public void AGroveFromTheGenerationBeforeThisOneIsNotReadAtAll()
        {
            HomesteadLayout.ResetForTests();
            GroveSave.LoadFrom(Save(0, "east_meadow"));

            Assert.AreEqual(string.Empty, HomesteadLayout.At(GroveFloor.TileId(2, 2)),
                            "an id from the old catalogue names nothing, so the tile is bare");
        }

        [Test]
        public void WhatIsWrittenBackIsStampedWithThisBuildsEpoch()
        {
            // Without the stamp a device that loaded an old grove would read empty, write 0, and
            // take the whole of it back from the cloud on its very next pull.
            HomesteadLayout.ResetForTests();
            GroveSave.LoadFrom(Save(0));

            var written = new SaveFileDto();
            GroveSave.WriteInto(written);

            Assert.AreEqual(GroveEpoch.Current, written.groveEpoch);
        }

        [Test]
        public void AGroveOfThisGenerationIsReadNormally()
        {
            HomesteadLayout.ResetForTests();
            GroveSave.LoadFrom(Save(GroveEpoch.Current));

            Assert.AreEqual("piece_" + GroveEpoch.Current,
                            HomesteadLayout.At(GroveFloor.TileId(2, 2)));
        }

        // ================================================================== delta
        [Test]
        public void ADeviceThatHasOnlyClearedAnOldGroveStillHasSomethingToPush()
        {
            // The grove's three sections agree — both are empty — and the epoch is the only
            // thing that differs. Until it is pushed, the server's copy still claims the older
            // generation and wins the join back, so "nothing changed" would be a loop.
            var remote = Save(0);
            remote.homesteadPlaced = new HomesteadPlacementDto[0];
            remote.homesteadStock = new HomesteadStockDto[0];
            remote.groveLandOwned = new string[0];

            var merged = SaveMerge.Join(remote, remote);
            merged.groveEpoch = GroveEpoch.Current;

            Assert.IsFalse(SaveDelta.Between(remote, merged).IsEmpty,
                           "the epoch alone is a real change, and until it is pushed the "
                           + "server's older copy wins the join back");
        }
    }
}
