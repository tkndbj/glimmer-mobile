using System.IO;
using System.Linq;
using GlimmerGrove.Content;
using GlimmerGrove.Events;
using GlimmerGrove.Progression;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The shipped season as a contract.
    ///
    /// The shipped cases read <c>manifest.json</c> through <c>JsonUtility</c>, which is a
    /// native call — so they need the Editor and the offline runner reports them as such
    /// (invariant 29e). Everything above them runs anywhere.
    /// </summary>
    public sealed class EventPassTests
    {
        // ------------------------------------------------------------ what shipped
        static ManifestEventDto Shipped()
            => JsonUtility.FromJson<ManifestDto>(File.ReadAllText(
                   Path.Combine(Application.streamingAssetsPath, "Content/manifest.json")))
               .events.Single(x => x.id == "first_watch");

        /// <summary>
        /// Forty rungs and eighty chests, which is the shape the season was commissioned as.
        /// Pinned as a count rather than as a table, because every tier on it is content and
        /// retunable — what may not move without somebody meaning it is the <em>size</em>.
        /// </summary>
        [Test]
        public void TheShippedSeasonHasFortyRungsOnBothTracks()
        {
            var season = Shipped();

            Assert.AreEqual(EventRules.MaxMilestones, season.milestones.Length);
            Assert.IsTrue(season.milestones.All(r => !string.IsNullOrEmpty(r.tier)),
                          "every rung pays on the free track");
            Assert.IsTrue(season.milestones.All(r => !string.IsNullOrEmpty(r.premiumTier)),
                          "and on the paid one, because it sells a pass");
            Assert.Greater(season.passGems, 0, "and the pass has a gem price to sell it at");
        }

        /// <summary>
        /// Goals rise and every tier the ladder names is one the shipped table defines —
        /// the one fault that is invisible in either file on its own, because the ladder is
        /// in the manifest and the tiers are in <c>progression.json</c>.
        /// </summary>
        [Test]
        public void EveryShippedRungRisesAndNamesATierThatExists()
        {
            var season = Shipped();
            var table = ProgressionRules.Table.Tasks;
            int previous = 0;

            foreach (var rung in season.milestones)
            {
                Assert.Greater(rung.goal, previous, "rung goals rise");
                previous = rung.goal;

                Assert.IsNotNull(table.Tier(rung.tier), $"free tier '{rung.tier}' exists");
                Assert.IsNotNull(table.Tier(rung.premiumTier), $"pass tier '{rung.premiumTier}' exists");
            }
        }

        /// <summary>
        /// The paid column has to be worth paying for, rung by rung — a pass that sold the
        /// same chest the free track already gives is a product with nothing behind it. Read
        /// off the ladder's own ranks rather than typed, so a retune keeps this honest.
        /// </summary>
        [Test]
        public void EveryPaidRungOutranksTheFreeOneBesideIt()
        {
            var season = Shipped();
            var table = ProgressionRules.Table.Tasks;

            foreach (var rung in season.milestones)
                Assert.Greater(table.Tier(rung.premiumTier).Rank, table.Tier(rung.tier).Rank,
                               $"the pass rung at {rung.goal} marks must beat the free one");
        }

        /// <summary>
        /// The ladder has to be climbable inside its own window by somebody who claims every
        /// chest they are dealt — a season whose last rungs nobody can reach is a countdown
        /// with an unreachable prize on it. The same arithmetic the Editor validator warns
        /// on, pinned here so it fails a build rather than printing into a log.
        /// </summary>
        [Test]
        public void TheShippedLadderCanBeClimbedInsideItsWindow()
        {
            var season = Shipped();
            var table = ProgressionRules.Table.Tasks;

            float perDay = 0f;

            foreach (var period in Tasks.TaskPeriods.All)
            {
                var slate = table.Slate(period);
                float sum = 0f;
                int live = 0;

                foreach (var task in slate)
                {
                    if (task.Retired) continue;
                    sum += task.Tier.Marks;
                    live++;
                }

                if (live == 0) continue;

                float dealt = Mathf.Min(table.ActivePerPeriod, live) * (sum / live);
                perDay += period == Tasks.TaskPeriod.Weekly ? dealt / 7f : dealt;
            }

            long days = (season.endUnix - season.startUnix) / EventRules.SecondsPerDay;
            int top = season.milestones[season.milestones.Length - 1].goal;

            Assert.Greater(perDay, 0f, "the tier table pays marks at all");
            Assert.GreaterOrEqual(perDay * days, top,
                                  $"{days} days at about {perDay:0.0} marks a day has to reach {top}");
        }
    }
}
