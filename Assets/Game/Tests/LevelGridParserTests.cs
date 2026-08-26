using GlimmerGrove.Content;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The parser turns authored text into a board. Everything downstream trusts it,
    /// so it has to reject bad input loudly rather than produce a subtly wrong grid.
    /// </summary>
    public sealed class LevelGridParserTests
    {
        static LevelLayout Layout(params string[] rows)
            => new LevelLayout(rows[0].Split(' ').Length, rows.Length, rows);

        [Test]
        public void ParsesHeadsArmsColourRotationAndLock()
        {
            var result = LevelGridParser.Parse(Layout("*N#W/2! @S#R/0"));
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));

            var source = result.Cells[0];
            Assert.AreEqual(Kind.Source, source.kind);
            Assert.AreEqual(Puzzle.N, source.solved);
            Assert.AreEqual(Energy.All, source.colour);
            Assert.AreEqual(2, source.rot);
            Assert.IsTrue(source.locked);

            var lamp = result.Cells[1];
            Assert.AreEqual(Kind.Lamp, lamp.kind);
            Assert.AreEqual(Energy.R, lamp.colour);
            Assert.IsFalse(lamp.locked);
        }

        [Test]
        public void EmptyCellsAreEmpty()
        {
            var result = LevelGridParser.Parse(Layout(". -EW/0"));
            Assert.IsTrue(result.Ok);
            Assert.AreEqual(Kind.Empty, result.Cells[0].kind);
            Assert.AreEqual(Kind.Pipe, result.Cells[1].kind);
        }

        [Test]
        public void LampsWantingAnyColourParseAsAny()
        {
            var result = LevelGridParser.Parse(Layout("@W#A/0 -EW/0"));
            Assert.IsTrue(result.Ok);
            Assert.AreEqual(Energy.Any, result.Cells[0].colour);
        }

        [Test]
        public void ATaprootRuneParsesToItsLetterOrdinal()
        {
            var result = LevelGridParser.Parse(Layout("-EW/0&A -EW/0&C"));
            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));

            Assert.AreEqual(1, result.Cells[0].link);
            Assert.AreEqual(3, result.Cells[1].link);
        }

        [TestCase("%EW/0", TestName = "unknown head")]
        [TestCase("-/0", TestName = "no arms")]
        [TestCase("-EW#Z/0", TestName = "unknown colour")]
        [TestCase("-EW/9", TestName = "rotation out of range")]
        [TestCase("-EW/0zz", TestName = "trailing junk")]
        [TestCase("*EW/0", TestName = "colourless heart-crystal")]
        // 'x' was the duskcap, which is gone. A retired head has to be refused rather than
        // ignored: a chapter file carrying one is content written for a build that no longer
        // exists, and reading it as anything at all would put a tile on the board that no
        // rule here knows what to do with.
        [TestCase("xEW/0", TestName = "retired duskcap head")]
        [TestCase("-EW/0&a", TestName = "lower-case root rune")]
        [TestCase("-EW/0&", TestName = "root rune missing")]
        [TestCase("@W#A/0&A", TestName = "critter on a taproot")]
        [TestCase("-EW/0!&A", TestName = "rooted and bound")]
        [TestCase("-EW/0~2&A", TestName = "brittle and bound")]
        public void MalformedTokensAreRejected(string token)
        {
            var result = LevelGridParser.Parse(Layout(token + " -EW/0"));
            Assert.IsFalse(result.Ok, "expected '" + token + "' to be rejected");
        }

        [Test]
        public void RowWidthMismatchIsReported()
        {
            var layout = new LevelLayout(3, 1, new[] { "-EW/0 -EW/0" });
            var result = LevelGridParser.Parse(layout);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("expected 3", string.Join("; ", result.Errors));
        }

        [Test]
        public void RowCountMismatchIsReported()
        {
            var layout = new LevelLayout(1, 3, new[] { "-EW/0" });
            var result = LevelGridParser.Parse(layout);

            Assert.IsFalse(result.Ok);
            StringAssert.Contains("3 rows", string.Join("; ", result.Errors));
        }

        [Test]
        public void RaggedWhitespaceIsTolerated()
        {
            var layout = new LevelLayout(2, 1, new[] { "  -EW/0    -EW/0  " });
            var result = LevelGridParser.Parse(layout);

            Assert.IsTrue(result.Ok, string.Join("; ", result.Errors));
        }
    }
}
