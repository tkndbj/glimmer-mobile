using System;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// One rewarded video's answer, in every order the network's callbacks can arrive - and never
    /// before the video has left the screen.
    ///
    /// <para>
    /// Two device reports, both 2026-10-11, each pinned by name:
    /// <see cref="AFinishedVideoWhoseCloseArrivesFirstIsStillFinished"/> (the close beat the reward,
    /// and a watched video was answered as skipped) and
    /// <see cref="ARewardIsNotAnsweredWhileTheAdIsStillOnScreen"/> (the reward arrived under the end
    /// card, the siege resumed behind the ad, and the rallied line fell before the player was back).
    /// </para>
    /// <para>
    /// The timings are a tenth and a quarter of a second here rather than the shipped three seconds
    /// and two minutes, so the suite proves them without waiting on them; every wait is bounded, so
    /// a regression fails rather than hangs.
    /// </para>
    /// </summary>
    public sealed class RewardedShowTests
    {
        static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(100);
        static readonly TimeSpan Backstop = TimeSpan.FromMilliseconds(250);
        static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

        static RewardedShow Show()
            => new RewardedShow(AdImpression.New(AdPlacement.ContinueVideo), Grace, Backstop);

        static async Task<AdShowResult> Within(RewardedShow show)
        {
            var done = await Task.WhenAny(show.Answer, Task.Delay(Bound));
            Assert.AreSame(show.Answer, done, "the show never answered: a panel would sit on OPENING... for ever");
            return await show.Answer;
        }

        // ---------------------------------------------------------------- the order of events
        [Test]
        public async Task ARewardIsNotAnsweredWhileTheAdIsStillOnScreen()
        {
            var show = Show();

            var waiting = show.Rewarded();
            Assert.IsFalse(show.Settled,
                           "answered under the end card: the caller would resume the run behind the ad");

            await show.Closed();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Rewarded, result.Outcome);
            Assert.AreEqual(show.Impression.TraceId, result.Impression.TraceId, "the answer carries its own impression");

            await waiting;
            Assert.AreEqual(AdOutcome.Rewarded, (await show.Answer).Outcome, "the backstop running out changes nothing");
        }

        [Test]
        public async Task AFinishedVideoWhoseCloseArrivesFirstIsStillFinished()
        {
            var show = Show();

            var waiting = show.Closed();
            Assert.IsFalse(show.Settled, "a close alone is not an answer while the reward may follow it");

            await show.Rewarded();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Rewarded, result.Outcome);
            Assert.IsTrue(show.Settled, "the video had already gone, so the reward answers at once");
            Assert.IsFalse(show.RewardedLate);

            await waiting;
            Assert.AreEqual(AdOutcome.Rewarded, (await show.Answer).Outcome, "the grace running out changes nothing");
        }

        [Test]
        public async Task ACloseWithNoRewardIsASkipOnceTheGraceRunsOut()
        {
            var show = Show();

            var waiting = show.Closed();
            Assert.IsFalse(show.Settled);

            await waiting;

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Dismissed, result.Outcome);
            Assert.IsFalse(result.Rewarded);
        }

        [Test]
        public async Task ARewardAfterTheGraceIsReportedLateAndChangesNothing()
        {
            var show = Show();
            await show.Closed();

            await show.Rewarded();

            Assert.AreEqual(AdOutcome.Dismissed, (await Within(show)).Outcome, "an answer is given once");
            Assert.IsTrue(show.RewardedLate, "and the provider is told the grace was too short");
        }

        [Test]
        public async Task AFailureToShowAnswersAtOnceAndNothingFollowsIt()
        {
            var show = Show();

            show.DisplayFailed("no activity");
            Assert.IsTrue(show.Settled);

            await show.Rewarded();
            await show.Closed();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Error, result.Outcome);
            Assert.AreEqual("no activity", result.Message);
            Assert.IsFalse(show.RewardedLate, "a reward after a failure is not a late reward");
        }

        [Test]
        public async Task TwoClosesAndTwoRewardsAreOneOfEach()
        {
            var show = Show();

            var first = show.Rewarded();
            var second = show.Rewarded();
            Assert.IsFalse(show.Settled);

            await Task.WhenAll(show.Closed(), show.Closed());
            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);

            await Task.WhenAll(first, second);
        }

        // ---------------------------------------------------------- the nets under a lost callback
        /// <summary>
        /// The SDK never raises its close: the game regaining focus after losing it to the video is
        /// the departure instead, so the panel is never left on OPENING... with every key dead.
        /// </summary>
        [Test]
        public async Task ComingBackFromTheVideoIsADeparture()
        {
            var show = Show();

            show.Left();
            var waiting = show.Rewarded();
            Assert.IsFalse(show.Settled, "still on the video");

            var back = show.Returned();

            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
            await Task.WhenAll(waiting, back);
        }

        [Test]
        public async Task ComingBackWithNoRewardIsASkipOnceTheGraceRunsOut()
        {
            var show = Show();

            show.Left();
            var waiting = show.Returned();
            Assert.IsFalse(show.Settled, "the reward still has its grace after the return");

            await waiting;
            Assert.AreEqual(AdOutcome.Dismissed, (await Within(show)).Outcome);
        }

        /// <summary>Focus wobbling before the video has taken the screen answers nothing.</summary>
        [Test]
        public async Task AReturnWithNoDepartureIsIgnored()
        {
            var show = Show();

            await show.Returned();
            await Task.Delay(Grace + Grace);

            Assert.IsFalse(show.Settled);

            show.Left();
            var back = show.Returned();
            var waiting = show.Rewarded();
            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
            await Task.WhenAll(back, waiting);
        }

        /// <summary>
        /// A reward whose ad never reports leaving is answered all the same once the backstop runs
        /// out, so a missing close can delay a continue but never withhold it.
        /// </summary>
        [Test]
        public async Task ARewardWithNoDepartureIsAnsweredWhenTheBackstopRunsOut()
        {
            var show = Show();

            var waiting = show.Rewarded();
            Assert.IsFalse(show.Settled);

            await waiting;
            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
        }

        [Test]
        public void TheShippedTimingsAreLongEnoughToWaitAndShortEnoughToTell()
        {
            Assert.GreaterOrEqual(RewardedShow.CloseGrace, TimeSpan.FromSeconds(1),
                                  "shorter than a reward takes to arrive on a slow device");
            Assert.LessOrEqual(RewardedShow.CloseGrace, TimeSpan.FromSeconds(5),
                               "a player who skipped would stare at OPENING... for too long");

            Assert.GreaterOrEqual(RewardedShow.DepartureBackstop, TimeSpan.FromSeconds(60),
                                  "would cut short an end card somebody is still reading");
            Assert.LessOrEqual(RewardedShow.DepartureBackstop, TimeSpan.FromMinutes(5),
                               "a lost close would hold a paid continue for too long");
        }
    }
}
