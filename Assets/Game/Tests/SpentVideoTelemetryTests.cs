using System.Collections.Generic;
using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The videos that are spent where they were watched - the turret discount and the video
    /// continue - report into the one event every paid view reports into
    /// (<see cref="RewardedAds.CompletedEvent"/>), so a chart of ad views counts them. They first
    /// shipped reporting elsewhere (the continue) or with a field of their own (the discount),
    /// which left them out of the count without anything looking wrong.
    /// </summary>
    public sealed class SpentVideoTelemetryTests
    {
        sealed class Recorder : IAnalyticsSink
        {
            public readonly List<(string name, Dictionary<string, object> props)> Events =
                new List<(string, Dictionary<string, object>)>();

            public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
                => Events.Add((eventName, new Dictionary<string, object>(properties)));
        }

        Recorder _sink;

        [SetUp]
        public void Attach()
        {
            _sink = new Recorder();
            Telemetry.AddSink(_sink);
        }

        [TearDown]
        public void Detach() => Telemetry.RemoveSink(_sink);

        static AdShowResult Finished(string placement)
            => new AdShowResult(AdOutcome.Rewarded, AdImpression.New(placement));

        [Test]
        public void AFinishedContinueIsCountedWithEveryOtherView()
        {
            RewardedAds.NoteSpent(Finished(AdPlacement.ContinueVideo), ContinueVideo.Reward);

            Assert.AreEqual(1, _sink.Events.Count);
            var (name, props) = _sink.Events[0];

            Assert.AreEqual("rewarded_ad_completed", name, "the event a chart of ad views counts");
            Assert.AreEqual(AdPlacement.ContinueVideo, props["placement"]);
            Assert.AreEqual(ContinueVideo.Reward, props["reward"]);
            Assert.IsTrue(props.ContainsKey("day"));

            // No daily count is kept for these, and a nought would read as "first today" on every row.
            Assert.IsFalse(props.ContainsKey("watched_today"));
        }

        [Test]
        public void ADiscountNamesWhatItBought()
        {
            RewardedAds.NoteSpent(Finished(AdPlacement.TurretDiscount), Wards.WardDiscount.StarReward);

            Assert.AreEqual(1, _sink.Events.Count);
            Assert.AreEqual(AdPlacement.TurretDiscount, _sink.Events[0].props["placement"]);
            Assert.AreEqual("ward_star", _sink.Events[0].props["reward"]);
        }

        [Test]
        public void AVideoThatDidNotFinishIsNotAView()
        {
            var impression = AdImpression.New(AdPlacement.ContinueVideo);

            foreach (var outcome in new[] { AdOutcome.Dismissed, AdOutcome.NoFill, AdOutcome.Unavailable, AdOutcome.Error })
                RewardedAds.NoteSpent(AdShowResult.Failed(outcome, impression), ContinueVideo.Reward);

            Assert.IsEmpty(_sink.Events);
        }

        /// <summary>
        /// Outside the daily caps and the shared cooldown means touching neither: noting a view
        /// raises no change, so no open panel repaints and no other placement starts counting down.
        /// </summary>
        [Test]
        public void NotingASpentViewMovesNoAdState()
        {
            int raised = 0;
            void Count() => raised++;

            long cooldown = RewardedAds.CooldownRemaining();
            RewardedAds.Changed += Count;
            try
            {
                RewardedAds.NoteSpent(Finished(AdPlacement.ContinueVideo), ContinueVideo.Reward);
            }
            finally
            {
                RewardedAds.Changed -= Count;
            }

            Assert.AreEqual(0, raised);
            Assert.AreEqual(cooldown, RewardedAds.CooldownRemaining());
        }
    }
}
