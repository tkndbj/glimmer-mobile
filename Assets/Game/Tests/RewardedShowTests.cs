using System;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using NUnit.Framework;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// One rewarded video's answer, in every order the network's callbacks can arrive.
    ///
    /// <para>
    /// <b>The case this file exists for is
    /// <see cref="AFinishedVideoWhoseCloseArrivesFirstIsStillFinished"/></b>: watched to the end on an
    /// Android device, closed, and answered as closed early because the close beat the reward -
    /// so a lost run stayed lost behind a panel that had just been paid with a video (2026-10-11).
    /// </para>
    /// <para>
    /// The grace is a tenth of a second here rather than the shipped three, so the suite proves the
    /// timing without waiting on it; every wait is bounded, so a regression fails rather than hangs.
    /// </para>
    /// </summary>
    public sealed class RewardedShowTests
    {
        static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(100);
        static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

        static RewardedShow Show() => new RewardedShow(AdImpression.New(AdPlacement.ContinueVideo), Grace);

        static async Task<AdShowResult> Within(RewardedShow show)
        {
            var done = await Task.WhenAny(show.Answer, Task.Delay(Bound));
            Assert.AreSame(show.Answer, done, "the show never answered: a panel would sit on OPENING... for ever");
            return await show.Answer;
        }

        [Test]
        public async Task ARewardThenACloseIsFinished()
        {
            var show = Show();

            show.Rewarded();
            await show.Closed();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Rewarded, result.Outcome);
            Assert.IsTrue(result.Rewarded);
            Assert.AreEqual(show.Impression.TraceId, result.Impression.TraceId, "the answer carries its own impression");
        }

        [Test]
        public async Task AFinishedVideoWhoseCloseArrivesFirstIsStillFinished()
        {
            var show = Show();

            var waiting = show.Closed();
            Assert.IsFalse(show.Settled, "a close alone is not an answer while the reward may follow it");

            show.Rewarded();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Rewarded, result.Outcome);
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

            show.Rewarded();

            Assert.AreEqual(AdOutcome.Dismissed, (await Within(show)).Outcome, "an answer is given once");
            Assert.IsTrue(show.RewardedLate, "and the provider is told the grace was too short");
        }

        [Test]
        public async Task AFailureToShowAnswersAtOnceAndNothingFollowsIt()
        {
            var show = Show();

            show.DisplayFailed("no activity");
            Assert.IsTrue(show.Settled);

            show.Rewarded();
            await show.Closed();

            var result = await Within(show);
            Assert.AreEqual(AdOutcome.Error, result.Outcome);
            Assert.AreEqual("no activity", result.Message);
            Assert.IsFalse(show.RewardedLate, "a reward after a failure is not a late reward");
        }

        [Test]
        public async Task TwoClosesAreOneClose()
        {
            var show = Show();

            var first = show.Closed();
            var second = show.Closed();
            show.Rewarded();

            await Task.WhenAll(first, second);
            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
        }

        // ------------------------------------------------------- the net under a missing close
        /// <summary>
        /// The SDK never raises its close: the game regaining focus after losing it to the video is
        /// the close instead, so the panel is never left on OPENING... with every key dead.
        /// </summary>
        [Test]
        public async Task ComingBackFromTheVideoAnswersWhenTheSdkNeverCloses()
        {
            var show = Show();

            show.Left();
            var waiting = show.Returned();
            Assert.IsFalse(show.Settled, "the reward still has its grace after the return");

            await waiting;
            Assert.AreEqual(AdOutcome.Dismissed, (await Within(show)).Outcome);
        }

        [Test]
        public async Task ARewardAfterComingBackIsStillFinished()
        {
            var show = Show();

            show.Left();
            var waiting = show.Returned();
            show.Rewarded();

            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
            await waiting;
        }

        /// <summary>Focus wobbling before the video has taken the screen answers nothing.</summary>
        [Test]
        public async Task AReturnWithNoDepartureIsIgnored()
        {
            var show = Show();

            await show.Returned();
            await Task.Delay(Grace + Grace);

            Assert.IsFalse(show.Settled);

            show.Rewarded();
            Assert.AreEqual(AdOutcome.Rewarded, (await Within(show)).Outcome);
        }

        [Test]
        public void TheShippedGraceIsLongEnoughToWaitAndShortEnoughToTell()
        {
            Assert.GreaterOrEqual(RewardedShow.CloseGrace, TimeSpan.FromSeconds(1),
                                  "shorter than a reward takes to arrive on a slow device");
            Assert.LessOrEqual(RewardedShow.CloseGrace, TimeSpan.FromSeconds(5),
                               "a player who skipped would stare at OPENING... for too long");
        }
    }
}
