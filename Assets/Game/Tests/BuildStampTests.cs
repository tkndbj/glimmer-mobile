using GlimmerGrove.Persistence;
using GlimmerGrove.Release;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The builds an account has run (save v47, <see cref="BuildStamp"/>): one parser for the figure,
    /// and two joins that only ever move one way, with nought meaning "none recorded".
    /// </summary>
    public sealed class BuildStampTests
    {
        [Test]
        public void TheFigureIsTheReleaseGatesOwnParse()
        {
            Assert.IsTrue(AppVersion.TryParse("1.1.3", out int build));
            Assert.AreEqual(10103, build, "the store version 1.1.3 is recorded as 10103");
            Assert.IsTrue(BuildStamp.IsBuild(build));

            Assert.IsTrue(AppVersion.TryParse("99.99.99", out int top));
            Assert.AreEqual(BuildStamp.MaxBuild, top, "the bound is the largest version the parser can answer below 100.0.0");
        }

        [Test]
        public void TheFirstBuildKeepsTheLowestRecordedOne()
        {
            Assert.AreEqual(10103, BuildStamp.JoinFirst(10103, 10204));
            Assert.AreEqual(10103, BuildStamp.JoinFirst(10204, 10103));
            Assert.AreEqual(10204, BuildStamp.JoinFirst(0, 10204), "nought is absent, never the lowest");
            Assert.AreEqual(10204, BuildStamp.JoinFirst(10204, 0));
            Assert.AreEqual(0, BuildStamp.JoinFirst(0, 0));
            Assert.AreEqual(10204, BuildStamp.JoinFirst(-1, 10204), "an impossible figure is absent too");
        }

        [Test]
        public void TheLastBuildKeepsTheHighest()
        {
            Assert.AreEqual(10204, BuildStamp.JoinLast(10103, 10204));
            Assert.AreEqual(10204, BuildStamp.JoinLast(10204, 10103));
            Assert.AreEqual(10103, BuildStamp.JoinLast(0, 10103));
            Assert.AreEqual(0, BuildStamp.JoinLast(0, 0));
            Assert.AreEqual(10103, BuildStamp.JoinLast(10103, BuildStamp.MaxBuild + 1),
                            "a figure the rules would refuse is never carried");
        }

        [Test]
        public void BothJoinsAreOrderFreeAndIdempotent()
        {
            int[] figures = { 0, 10000, 10103, 10204, 20000, BuildStamp.MaxBuild };
            foreach (int a in figures)
                foreach (int b in figures)
                {
                    Assert.AreEqual(BuildStamp.JoinFirst(a, b), BuildStamp.JoinFirst(b, a));
                    Assert.AreEqual(BuildStamp.JoinLast(a, b), BuildStamp.JoinLast(b, a));
                    Assert.AreEqual(BuildStamp.JoinFirst(a, b), BuildStamp.JoinFirst(BuildStamp.JoinFirst(a, b), b));
                    Assert.AreEqual(BuildStamp.JoinLast(a, b), BuildStamp.JoinLast(BuildStamp.JoinLast(a, b), b));
                }
        }

        [Test]
        public void AMergeKeepsTheOldestAndTheNewestOfTwoDevices()
        {
            var phone = new SaveFileDto { schemaVersion = SaveSchema.Version, firstBuild = 10103, lastBuild = 10103 };
            var tablet = new SaveFileDto { schemaVersion = SaveSchema.Version, firstBuild = 10204, lastBuild = 10300 };

            var merged = SaveMerge.Join(phone, tablet);

            Assert.AreEqual(10103, merged.firstBuild);
            Assert.AreEqual(10300, merged.lastBuild);
            Assert.IsTrue(SaveDelta.Between(tablet, merged).ScalarsChanged, "the server's copy learns the phone's older first build");
            Assert.IsFalse(SaveDelta.Between(merged, merged).ScalarsChanged, "an agreed file owes nothing");
        }
    }
}
