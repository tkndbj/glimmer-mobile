using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    public sealed partial class SiegeRuleTests
    {
        /// <summary>
        /// **An eased board's raiders are lighter, and nothing it is graded against moves**
        /// (invariant 66). Every raider of every wave keeps <see cref="AssistTable.KeptPercent"/> of
        /// the health the same board deals at full strength, never less than one; par is the
        /// layout's, so it reads the same either way.
        /// </summary>
        [Test]
        public void AnEasedBoardDealsLighterRaidersAgainstTheSamePar()
        {
            var layout = Chapter[5].Built();
            int kept = AssistTable.KeptPercent(7);

            var plain = SiegeBoard.Build(layout, null, SiegeStart.Opening);
            var eased = SiegeBoard.Build(layout, null, SiegeStart.Opening, kept);

            Assert.AreEqual(100, plain.KeptPercent);
            Assert.AreEqual(93, eased.KeptPercent);

            int seen = 0;
            for (int step = 0; step < 4000 && seen < layout.RaiderCount; step++)
            {
                plain.Advance(.05f);
                eased.Advance(.05f);

                int count = System.Math.Min(plain.Raiders.Count, eased.Raiders.Count);
                for (int i = seen; i < count; i++)
                {
                    int full = plain.Raiders[i].MaxHealth;
                    int lighter = eased.Raiders[i].MaxHealth;
                    Assert.AreEqual(plain.Raiders[i].Kind, eased.Raiders[i].Kind, "the two boards dealt different hills");
                    Assert.AreEqual(System.Math.Max(1, full * kept / 100), lighter,
                                    $"raider {i} ({plain.Raiders[i].Kind}) carries {lighter} against {full} at full strength");
                }
                if (count > seen) seen = count;
            }

            Assert.Greater(seen, 0, "no raider was ever mustered, so nothing was compared");
            Assert.AreEqual(SiegeTuning.Par(layout), SiegeTuning.Par(eased.Layout), "par is the layout's");
        }
    }
}
