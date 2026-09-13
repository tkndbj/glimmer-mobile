using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Progression;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// The teaching funnel is the only instrument that says where the first ten minutes
    /// lose people, and every one of its faults is silent: a wrong parameter is a column of
    /// plausible numbers, and a missing event is a gap nobody can tell from a player who
    /// never got there. None of it can be checked after the fact either — analytics not
    /// collected in the first months is simply gone.
    ///
    /// <para>
    /// So what is pinned here is the shape rather than the wiring: that both halves are
    /// raised, that the parameters a report groups by are present and spelled the way the
    /// sinks read them, and that reading time is floored rather than rounded — because a tip
    /// dismissed in 900ms has to report nought, which is the reading the whole event exists
    /// to produce.
    /// </para>
    /// </summary>
    public sealed class LessonAnalyticsTests
    {
        /// <summary>Keeps what it was handed, so an assertion can read the event back.</summary>
        sealed class Spy : IAnalyticsSink
        {
            public readonly List<(string Name, Dictionary<string, object> Values)> Events
                = new List<(string, Dictionary<string, object>)>();

            public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
            {
                // Copied rather than kept: Telemetry reuses one scratch dictionary, so holding
                // the reference would leave every recorded event wearing the last one's values.
                var copy = new Dictionary<string, object>();
                foreach (var kv in properties) copy[kv.Key] = kv.Value;

                Events.Add((eventName, copy));
            }

            public Dictionary<string, object> Only(string name)
            {
                var found = Events.FindAll(e => e.Name == name);
                Assert.AreEqual(1, found.Count, $"expected exactly one '{name}'");
                return found[0].Values;
            }
        }

        Spy _spy;

        [SetUp]
        public void Attach()
        {
            Telemetry.Clear();
            _spy = new Spy();
            Telemetry.AddSink(_spy);
        }

        [TearDown]
        public void Detach() => Telemetry.Clear();

        static Mechanic Some => Mechanic.SiegeFuel;

        [Test]
        public void ShownCarriesWhatAReportGroupsBy()
        {
            LessonAnalytics.TrackShown(Some, "SiegeScreen", repeat: false, pointed: true);

            var values = _spy.Only(LessonAnalytics.Shown);
            Assert.AreEqual(Some.Id, values["mechanic"]);
            Assert.AreEqual("SiegeScreen", values["screen"]);
            Assert.AreEqual(false, values["repeat"]);
            Assert.AreEqual(true, values["pointed"]);
        }

        [Test]
        public void FinishedCarriesHowAndHowLong()
        {
            LessonAnalytics.TrackFinished(Some, "SiegeScreen", LessonAnalytics.ByButton,
                                          4.9f, repeat: false);

            var values = _spy.Only(LessonAnalytics.Finished);
            Assert.AreEqual(LessonAnalytics.ByButton, values["how"]);
            Assert.AreEqual(4, values["seconds"], "reading time is floored, not rounded");
        }

        /// <summary>
        /// The measurement the event exists for. A tip dismissed inside a second was not read,
        /// and rounding would report it as one second of reading — the same figure as a tip
        /// somebody actually looked at.
        /// </summary>
        [Test]
        public void ATipDismissedInsideASecondReadsAsNought()
        {
            LessonAnalytics.TrackFinished(Some, "MapScreen", LessonAnalytics.ByBack,
                                          .9f, repeat: false);

            Assert.AreEqual(0, _spy.Only(LessonAnalytics.Finished)["seconds"]);
        }

        /// <summary>
        /// Two clocks read a frame apart can differ the wrong way. A negative reading would
        /// be silently averaged into every figure in the report.
        /// </summary>
        [Test]
        public void ANegativeDurationIsClamped()
        {
            LessonAnalytics.TrackFinished(Some, null, LessonAnalytics.ByButton,
                                          -0.2f, repeat: false);

            Assert.AreEqual(0, _spy.Only(LessonAnalytics.Finished)["seconds"]);
        }

        /// <summary>
        /// An absent screen is a value rather than a missing parameter, because a report
        /// cannot group by something that is not there — the rows simply vanish.
        /// </summary>
        [Test]
        public void AnAbsentScreenIsStillAValue()
        {
            LessonAnalytics.TrackShown(Some, null, repeat: false, pointed: false);

            Assert.AreEqual("none", _spy.Only(LessonAnalytics.Shown)["screen"]);
        }

        /// <summary>
        /// An exit nobody named is counted as the unexplained one. Defaulting to a completion
        /// would quietly turn every unhandled teardown into a player who read the tip.
        /// </summary>
        [Test]
        public void AnUnnamedExitIsNotACompletion()
        {
            LessonAnalytics.TrackFinished(Some, "MapScreen", null, 3f, repeat: false);

            Assert.AreEqual(LessonAnalytics.ByNavigation,
                            _spy.Only(LessonAnalytics.Finished)["how"]);
        }

        /// <summary>
        /// A lesson with no id is a bug upstream, and an event carrying an empty mechanic
        /// would be a row in every report that cannot be traced back to anything.
        /// </summary>
        [Test]
        public void AnInvalidMechanicRaisesNothing()
        {
            LessonAnalytics.TrackShown(default, "MapScreen", false, false);
            LessonAnalytics.TrackFinished(default, "MapScreen", LessonAnalytics.ByButton, 1f, false);

            Assert.AreEqual(0, _spy.Events.Count);
        }

        /// <summary>
        /// The two halves are separate events on purpose: a ratio needs both counted
        /// independently, and a shown with no finished is the one exit a panel cannot report
        /// for itself — the process dying while a modal is up.
        /// </summary>
        [Test]
        public void TheTwoHalvesAreCountedSeparately()
        {
            LessonAnalytics.TrackShown(Some, "SiegeScreen", false, true);
            LessonAnalytics.TrackFinished(Some, "SiegeScreen", LessonAnalytics.ByButton, 6f, false);

            Assert.AreEqual(2, _spy.Events.Count);
            Assert.AreEqual(LessonAnalytics.Shown, _spy.Events[0].Name);
            Assert.AreEqual(LessonAnalytics.Finished, _spy.Events[1].Name);
        }
    }
}
