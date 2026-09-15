using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The one rule that has to hold on every chapter this mode will ever ship: what a utility is
    /// worth, measured against what it is used on.
    ///
    /// <para>
    /// <b>Why a fixture rather than a number somebody checks.</b> A chapter is made harder by
    /// surging what its raiders carry (invariant 37by), and nothing else in the mode moves with
    /// it — so an authored 440 quietly means less on every chapter after the second, and there is
    /// no reading anywhere that would say so. Every gate stays green: the level parses, par is
    /// derived from the surged health and is correct, the hold simulation never taps a utility at
    /// all, and the shop card prints no number. It is invisible until somebody plays chapter
    /// twenty and says a firepot does nothing.
    /// </para>
    /// <para>
    /// <b>So the invariant is written as arithmetic and pinned here</b>: a utility's damage and a
    /// raider's health go through <em>one</em> multiplier, so their ratio is exact on every
    /// chapter and on every wave of an endless run. These cases are deliberately about the ratio
    /// rather than about any particular figure — a retune of 440 must not move one of them.
    /// </para>
    /// </summary>
    public sealed class SiegeUtilityScaleTests
    {
        const string Gems = "rgby", Wards = "rgby";

        /// <summary>One wave, four colours, nothing about it deciding anything here.</summary>
        static readonly string[] Waves = { "rgby" };

        /// <summary>A settled field with no run in it, so nothing cascades while the clock walks.</summary>
        static string[] Field() => new[]
        {
            "rrgby",
            "bygbr",
            "gbyrg",
        };

        /// <summary>
        /// A board whose raiders carry <paramref name="tough"/> tenths of their ordinary health.
        /// Ten is the plain chapter; thirteen is a chapter carrying three tenths.
        /// </summary>
        static SiegeBoard Board(int tough)
        {
            var rows = Field();

            Assert.IsTrue(ProtoGrid.TryRead(rows, rows[0].Length, rows.Length,
                                            SiegeLayout.Cells, out var grid, out string error),
                          error);

            var layout = new SiegeLayout(grid, Gems, Wards, Waves, null, 0, null, tough, null);
            Assert.IsNull(layout.Fault, layout.Fault);

            return SiegeBoard.Build(layout);
        }

        /// <summary>
        /// The same board with its first wave standing on the hill.
        ///
        /// <b>The clock is walked rather than the raiders placed</b>, because a raider is minted by
        /// the muster and it is the muster that hands it its surge — placing one here would be the
        /// fixture deciding the very thing it is testing.
        /// </summary>
        static SiegeBoard Mustered(int tough)
        {
            var board = Board(tough);

            for (int i = 0; i < 60 * 120 && board.OnTheHill < 1; i++) board.Advance(1f / 60f);

            Assert.Greater(board.OnTheHill, 0, "the wave never reached the hill");
            return board;
        }

        /// <summary>
        /// <b>A baseline small enough that nothing is capped by the health it lands on</b>, which
        /// is what lets these cases read the multiplier rather than the overkill clamp: a storm of
        /// 700 kills a creeper on both boards and absorbs its health rather than its own figure,
        /// so the two would come out equal and the test would pass while proving nothing.
        /// </summary>
        const int Nibble = 10;

        // ------------------------------------------------------------------ the two multipliers
        /// <summary>
        /// <b><c>Hurt</c> and <c>Health</c> are one multiplier, and that identity is the whole
        /// feature.</b> They are written as two members because they say different things at a
        /// call site; the moment they answer differently, a utility's share of a raider stops
        /// being a constant and every argument below it collapses.
        /// </summary>
        [Test]
        public void DamageAndHealthGoThroughOneMultiplier()
        {
            for (int tenths = 10; tenths <= 60; tenths++)
            {
                var surge = new SiegeSurge(tenths, 10);

                for (int amount = 1; amount <= 2_000; amount += 7)
                    Assert.AreEqual(surge.Health(amount), surge.Hurt(amount),
                                    "tenths " + tenths + ", amount " + amount);
            }
        }

        /// <summary>
        /// Never nought once it was anything, for <c>SiegeSurge.Blow</c>'s reason: a utility that
        /// reads as landing and takes nothing is one the player will believe is broken.
        /// </summary>
        [Test]
        public void ADamageFigureIsNeverRoundedAwayToNothing()
        {
            Assert.AreEqual(0, new SiegeSurge(10, 10).Hurt(0));
            Assert.AreEqual(0, new SiegeSurge(10, 10).Hurt(-5));
            Assert.AreEqual(1, new SiegeSurge(10, 10).Hurt(1));
        }

        // ------------------------------------------------------------------ the hill
        /// <summary>
        /// <b>A firepot is worth the same share of a surged raider as of a plain one.</b> Stated
        /// as a cross-multiplication rather than as two figures, so a retune of the health or of
        /// the step cannot make it read as passing for the wrong reason.
        /// </summary>
        [Test]
        public void AFirepotTakesTheSameShareOfASurgedRaiderAsOfAPlainOne()
        {
            int plain = Absorbed(Mustered(10), blast: true);
            int surged = Absorbed(Mustered(13), blast: true);

            Assert.Greater(plain, 0, "the firepot reached nobody, so this case proves nothing");
            Assert.AreEqual(plain * 13, surged * 10,
                            "a firepot has to climb with the hill exactly as the hill climbed");
        }

        /// <summary>The same of a stormcall, which is the other kind measured in hill health.</summary>
        [Test]
        public void AStormcallTakesTheSameShareOfASurgedHillAsOfAPlainOne()
        {
            int plain = Absorbed(Mustered(10), blast: false);
            int surged = Absorbed(Mustered(13), blast: false);

            Assert.Greater(plain, 0, "the storm reached nobody, so this case proves nothing");
            Assert.AreEqual(plain * 13, surged * 10);
        }

        /// <summary>
        /// The hill it is measured against really did climb — the other half of the two cases
        /// above, which would both pass if <em>nothing</em> scaled.
        /// </summary>
        [Test]
        public void ASurgedChaptersRaidersReallyDoCarryMore()
        {
            int plain = Health(Mustered(10));
            int surged = Health(Mustered(13));

            Assert.AreEqual(plain * 13, surged * 10, "the fixture's own premise");
        }

        /// <summary>
        /// <b><c>UtilityKind.Storm</c>'s own promise, kept by the ratio rather than by a number.</b>
        /// Its remarks say it hurts a boss and never fells one — which was a fact about 700 against
        /// 2,050 and would have stopped being true at the chapter that surged past it. Both sides
        /// climb together, so it is now true at every toughness this mode can reach.
        /// </summary>
        [Test]
        public void AStormcallStillCannotFellABossHoweverToughTheChapter()
        {
            for (int tenths = 10; tenths <= 120; tenths++)
            {
                var surge = new SiegeSurge(tenths, 10);

                Assert.Less(surge.Hurt(700), surge.Health(SiegeTuning.BossHealth),
                            "a storm fells the smallest boss at " + tenths + " tenths");
            }
        }

        // ------------------------------------------------------------------ the taxonomy
        /// <summary>
        /// Every kind says what its magnitude is measured in. <b>The guard against the fault this
        /// whole fixture is about</b>: a damaging kind added years from now that nobody thought to
        /// scale would be a number that quietly decays, and the only moment anybody is in a
        /// position to notice is the moment the kind is written.
        /// </summary>
        [Test]
        public void EveryUtilityKindDeclaresWhatItsMagnitudeIsMeasuredIn()
        {
            foreach (UtilityKind kind in System.Enum.GetValues(typeof(UtilityKind)))
            {
                var unit = UtilityUnits.Of(kind);

                if (kind == UtilityKind.None)
                {
                    Assert.AreEqual(UtilityUnit.None, unit);
                    continue;
                }

                Assert.AreNotEqual(UtilityUnit.None, unit,
                                   kind + " has no unit, so nothing knows whether its magnitude "
                                   + "climbs with the board it is used on");
            }
        }

        /// <summary>
        /// Which kinds climb, written down. <b>A list rather than a rule</b>, because the point of
        /// it is to fail when the set changes: adding a kind to it is a decision somebody has to
        /// take on purpose, and taking one out is the decay this fixture exists to catch.
        /// </summary>
        [Test]
        public void OnlyTheKindsMeasuredInHillHealthClimb()
        {
            Assert.IsTrue(UtilityUnits.Climbs(UtilityKind.Blast));
            Assert.IsTrue(UtilityUnits.Climbs(UtilityKind.Storm));

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.Mend),
                           "a ward's stones are a fixed 14 and a blow is never surged, so a "
                           + "mending is worth the same share of the line on every chapter");

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.Surge),
                           "fuel is bounded by the tube it is poured into, so a surged pour would "
                           + "be a pour a ward cannot hold");

            Assert.IsFalse(UtilityUnits.Climbs(UtilityKind.None));
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// What one use of <see cref="Nibble"/> absorbs across the whole hill.
        ///
        /// A blast is aimed at every box in turn rather than at one, so the two kinds reach the
        /// same raiders and the comparison is about the multiplier alone.
        /// </summary>
        static int Absorbed(SiegeBoard board, bool blast)
        {
            if (!blast) return board.Storm(Nibble, null);

            int whole = 0;

            for (int lane = 0; lane < SiegeTuning.Lanes; lane++)
                for (int row = 0; row < SiegeTuning.BlastRows; row++)
                    whole += board.Blast(lane, row, Nibble, null);

            return whole;
        }

        /// <summary>What the whole hill is carrying, which is what a utility is measured against.</summary>
        static int Health(SiegeBoard board)
        {
            int whole = 0;

            for (int i = 0; i < board.Raiders.Count; i++)
                if (board.Raiders[i].Alive) whole += board.Raiders[i].MaxHealth;

            return whole;
        }
    }
}
