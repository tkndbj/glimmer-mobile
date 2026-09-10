using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The wait between two uses of one utility.
    ///
    /// <para>
    /// Three properties carry it. It is <b>per utility and per run</b>, so nothing about it
    /// reaches the save file — a count of seconds goes both ways and is exactly what invariant
    /// 11b refuses a merge. It is <b>advanced by the caller</b> rather than read off a wall
    /// clock, which is what stops a cooldown being paid off by opening a panel. And it can
    /// <b>only ever refuse a use</b>, which is why invariant 39 needs no arithmetic here: every
    /// run playable with cooldowns was playable without them, so no charge against the graded
    /// count can move.
    /// </para>
    /// <para>
    /// The readouts are pinned as hard as the rule, because a cooldown a player cannot read is
    /// a cell that refuses a tap for no visible reason.
    /// </para>
    /// </summary>
    public sealed class UtilityCooldownTests
    {
        static UtilityItem Cooling(int seconds, string id = "test",
                                   UtilityKind kind = UtilityKind.Blast, int order = 1)
            => new UtilityItem(id, kind, 10, 1, 10, order, 0, seconds);

        // =================================================================== the rule
        [Test]
        public void AFreshCooldownIsReadyAndSaysNothing()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(10);

            Assert.IsTrue(cool.Ready(item));
            Assert.IsFalse(cool.Any);
            Assert.AreEqual(0f, cool.Fraction(item));
            Assert.AreEqual(0, cool.Seconds(item));
        }

        [Test]
        public void SpendingOneStartsItsOwnClockAndNobodyElses()
        {
            var cool = new UtilityCooldown();
            var one = Cooling(10);
            var two = Cooling(15, "other", UtilityKind.Mend, 2);

            cool.Spend(one);

            Assert.IsFalse(cool.Ready(one));
            Assert.IsTrue(cool.Ready(two), "a cooldown is per utility, never per bar");
        }

        [Test]
        public void ItIsReadyAgainOnceItsSecondsHaveGoneBy()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(10);

            cool.Spend(item);

            Assert.IsFalse(cool.Advance(4f), "nothing has ended yet");
            Assert.IsFalse(cool.Advance(5.9f));
            Assert.IsTrue(cool.Advance(1f), "the frame it ends on is the frame that repaints");

            Assert.IsTrue(cool.Ready(item));
            Assert.IsFalse(cool.Any);
        }

        /// <summary>
        /// <b>A utility authoring no cooldown is never held back</b>, which is what makes an
        /// absent field mean "as the bar behaved before this existed" — the shape every optional
        /// number here takes, because <c>JsonUtility</c> writes a nought into a field an older
        /// file never had.
        /// </summary>
        [Test]
        public void AUtilityWithNoCooldownIsNeverHeldBack()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(0);

            cool.Spend(item);

            Assert.IsTrue(cool.Ready(item));
            Assert.IsFalse(cool.Any, "an item with no cooldown must not occupy a row");
        }

        /// <summary>Using one again restarts its wait rather than stacking a second onto it.</summary>
        [Test]
        public void UsingOneAgainRestartsTheWaitRatherThanAddingToIt()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(10);

            cool.Spend(item);
            cool.Advance(6f);
            cool.Spend(item);

            Assert.AreEqual(10f, cool.Left(item), .0001f);
            Assert.AreEqual(1f, cool.Fraction(item), .0001f);
        }

        [Test]
        public void ARestartForgetsEveryCooldown()
        {
            var cool = new UtilityCooldown();

            cool.Spend(Cooling(30));
            cool.Clear();

            Assert.IsFalse(cool.Any);
            Assert.IsTrue(cool.Ready(Cooling(30)));
        }

        /// <summary>
        /// Several at once, each on its own clock, ending in the order their seconds run out
        /// rather than in the order they were spent. The list is compacted in place every frame,
        /// so this is the case a backwards walk exists for.
        /// </summary>
        [Test]
        public void SeveralCooldownsRunSideBySideAndEndOnTheirOwnSchedules()
        {
            var cool = new UtilityCooldown();

            var quick = Cooling(10, "quick");
            var slow = Cooling(30, "slow", UtilityKind.Storm, 2);
            var middling = Cooling(20, "middling", UtilityKind.Surge, 3);

            cool.Spend(slow);
            cool.Spend(quick);
            cool.Spend(middling);

            cool.Advance(10f);
            Assert.IsTrue(cool.Ready(quick));
            Assert.IsFalse(cool.Ready(middling));
            Assert.IsFalse(cool.Ready(slow));

            cool.Advance(10f);
            Assert.IsTrue(cool.Ready(middling));
            Assert.IsFalse(cool.Ready(slow));

            cool.Advance(10f);
            Assert.IsFalse(cool.Any);
        }

        // =================================================================== the readouts
        /// <summary>
        /// <b>The countdown rounds up.</b> Down would print a nought over a cell that still
        /// refuses a tap, which is a readout disagreeing with the rule it is a readout of.
        /// </summary>
        [Test]
        public void TheCountdownNeverSaysNoughtWhileItIsStillCooling()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(10);

            cool.Spend(item);
            cool.Advance(9.6f);

            Assert.IsFalse(cool.Ready(item));
            Assert.AreEqual(1, cool.Seconds(item));
        }

        /// <summary>
        /// The sweep is drawn from this, so it has to run the whole way down and stop at both
        /// ends: a wedge starting at nine tenths or stopping at a tenth would read as a cooldown
        /// that skipped its first or its last moment.
        /// </summary>
        [Test]
        public void TheSweepRunsFromOneToNought()
        {
            var cool = new UtilityCooldown();
            var item = Cooling(20);

            cool.Spend(item);
            Assert.AreEqual(1f, cool.Fraction(item), .0001f);

            cool.Advance(10f);
            Assert.AreEqual(.5f, cool.Fraction(item), .0001f);

            cool.Advance(10f);
            Assert.AreEqual(0f, cool.Fraction(item));
        }

        // =================================================================== authoring it
        static UtilitiesDto Block(int cooldown)
            => new UtilitiesDto
            {
                items = new[]
                {
                    new UtilityDto
                    {
                        id = "firepot", kind = UtilityKinds.Blast, magnitude = 40,
                        gemPrice = 12, maxHeld = 100, order = 1, cooldownSeconds = cooldown,
                    },
                },
            };

        [Test]
        public void AnAuthoredCooldownReachesTheCatalog()
        {
            var problems = new List<string>();
            var built = UtilityCatalog.Resolve(Block(25), problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(25, built.Find("firepot").CooldownSeconds);
            Assert.IsTrue(built.Find("firepot").Cools);
        }

        [Test]
        public void AnAbsentCooldownMeansNone()
        {
            var problems = new List<string>();
            var built = UtilityCatalog.Resolve(Block(0), problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(0, built.Find("firepot").CooldownSeconds);
            Assert.IsFalse(built.Find("firepot").Cools);
        }

        /// <summary>
        /// <b>Refused rather than clamped</b>, because the only way to write a number this far
        /// out is to have written it in another unit — ten seconds typed as ten thousand
        /// milliseconds is an item usable once a raid, which plays as a broken bar rather than
        /// as a retune somebody meant. A clamp would hide exactly the mistake worth failing a
        /// build over.
        /// </summary>
        [Test]
        public void ACooldownWrittenInTheWrongUnitIsRefused()
        {
            var problems = new List<string>();
            var built = UtilityCatalog.Resolve(Block(10000), problems);

            Assert.IsNotEmpty(problems);
            Assert.AreSame(UtilityCatalog.Default, built,
                           "a bad catalog must cost a retune, never a session");
        }

        [Test]
        public void ANegativeCooldownIsRefused()
        {
            var problems = new List<string>();

            Assert.AreSame(UtilityCatalog.Default, UtilityCatalog.Resolve(Block(-1), problems));
            Assert.IsNotEmpty(problems);
        }

        // =================================================================== what ships
        /// <summary>
        /// The bar in force really does cool, and its items disagree about how long — which is
        /// the whole point of the feature. A stormcall that came back as fast as a firepot would
        /// simply be the strongest tap on the bar, and holding a hundred of anything would make
        /// every wave the same answer given as fast as a thumb moves.
        /// </summary>
        [Test]
        public void TheShippedBarCoolsAndItsItemsDisagreeAboutHowLong()
        {
            var catalog = ProgressionRules.Table.Utilities;
            var seen = new HashSet<int>();

            foreach (var item in catalog.Items)
            {
                Assert.IsTrue(item.Cools,
                              $"utility '{item.Id}' has no cooldown, so holding a hundred of "
                              + "them is a hundred taps in four seconds");

                Assert.LessOrEqual(item.CooldownSeconds, UtilityCooldown.MaxSeconds);
                seen.Add(item.CooldownSeconds);
            }

            Assert.Greater(seen.Count, 1,
                           "every utility cools for the same time, so the cooldown decides "
                           + "nothing between them");
        }
    }
}
