using System;
using System.Threading.Tasks;
using GlimmerGrove.Ads;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// What one rewarded video came to: the prize, the flight that measured it, and - where
    /// nothing was paid - why.
    ///
    /// <para>
    /// <see cref="Flight"/> is opened <em>before</em> the reward is redeemed and the redeem runs
    /// inside it, so it is never null, including on the paths that paid nothing. That costs a few
    /// reads on a refusal, which is the cheapest thing on any screen this appears on, and it is
    /// what makes the amount honest: deriving it afterwards from the offer is wrong in exactly the
    /// case that matters, since a heart reward landing at the ceiling grants nothing and owing it
    /// would rewind a pill below where it ever stood.
    /// </para>
    /// </summary>
    public readonly struct VideoPayment
    {
        /// <summary>What the video paid, or an invalid drop.</summary>
        public readonly ChestDrop Drop;

        /// <summary>The flight the reward was redeemed inside, holding what it put on each pill. See <see cref="RewardFlight"/>.</summary>
        public readonly RewardFlight Flight;

        /// <summary>How the show attempt ended, in the SDK's terms.</summary>
        public readonly AdOutcome Outcome;

        /// <summary>
        /// True when the attempt failed rather than being refused - the SDK answered
        /// <see cref="AdOutcome.Error"/> or threw (<see cref="RewardedVideo.Show"/> folds a throw
        /// into that outcome). Either is "that did not work", never "try later".
        /// </summary>
        public readonly bool Faulted;

        public VideoPayment(ChestDrop drop, RewardFlight flight, AdOutcome outcome, bool faulted)
        {
            Drop = drop;
            Flight = flight;
            Outcome = outcome;
            Faulted = faulted;
        }

        /// <summary>True when there is something to hand over.</summary>
        public bool Paid => Drop.IsValid;
    }

    /// <summary>
    /// Showing a rewarded video and finding out what it paid - once, for every caller.
    ///
    /// <para>
    /// <b>It was written three times before it was written here.</b> <c>AdOfferOverlay</c>, the
    /// bonus wheel and the defeat panel's heart refill each need the same five steps in the same
    /// order - mint an impression, show it, open the flight, redeem inside it, read the refusal -
    /// and the order is the substance rather than the steps. The impression is minted before the
    /// SDK is asked for anything because the nonce inside it has to reach the network as a custom
    /// parameter (<see cref="AdImpression"/>); the flight is opened before the redeem because the
    /// redeem is what moves the pills, and the flight has to measure it. A copy per panel is three places for one of those two
    /// orderings to be got wrong, and neither failure is visible in a compile, a validator or a
    /// screenshot - the first pays nobody and the second rewinds a readout to a figure it never
    /// held. Invariant 9a at the smallest scale it appears at, which is the argument
    /// <c>AdOfferButton</c> was lifted out on.
    /// </para>
    /// <para>
    /// <b>It returns rather than calling back, and it deliberately knows nothing about panels.</b>
    /// Every caller here is a <c>MonoBehaviour</c> that may not survive the video - a player who
    /// backgrounds the app during one can come back to a different screen entirely - so the
    /// liveness check belongs to whoever is holding a reference, right after the await, where
    /// C#'s own <c>if (this == null)</c> reads as what it is. A callback taken by this method
    /// would put that check somewhere the caller cannot see it.
    /// </para>
    /// <para>
    /// The reward is banked either way: <see cref="RewardedAds.Redeem"/> touches no UI, so a
    /// caller that has been destroyed by the time this returns has still been paid - which is
    /// why the redeem happens here rather than in the caller's own continuation.
    /// </para>
    /// </summary>
    public static class RewardedVideo
    {
        /// <summary>
        /// Shows <paramref name="placementId"/>'s video and hands back what it came to.
        ///
        /// <para>
        /// Never throws. An ad SDK that faults must not leave a panel stuck on "opening" with a
        /// dead button, and every caller of this is exactly one <c>await</c> away from a screen
        /// in that state - so the fault is reported as a <see cref="VideoPayment"/> like any
        /// other refusal, logged on the way past so it is still in the console.
        /// </para>
        /// </summary>
        public static async Task<VideoPayment> Watch(string placementId)
        {
            var result = await Show(placementId);

            // Opened before the redeem and the redeem run inside it, always. See the class remarks.
            // On a fault it is still opened, so a caller never has to test it for null: it has
            // banked nothing, so playing it would be a no-op and adding an invalid drop to it is
            // refused.
            var flight = RewardFlight.Begin();
            var drop = flight.Bank(() => RewardedAds.Redeem(result));

            // What the device banked (hearts, a boost) the flight has measured; a currency prize is
            // paid by the server a few seconds from now (invariant 10d), so it is owed as it arrives.
            flight.Expect(drop);

            bool faulted = result.Outcome == AdOutcome.Error;
            return new VideoPayment(drop, flight, result.Outcome, faulted);
        }

        /// <summary>
        /// Shows <paramref name="placementId"/>'s video and answers how it ended, <b>redeeming
        /// nothing</b>.
        ///
        /// <para>
        /// For the placements that are spent where they were watched rather than paid from the ad
        /// table - the turret discount and the video continue - whose reward is a purchase or a
        /// run the caller applies itself (<see cref="RewardedAds.NoteSpent"/> records it). One
        /// copy of the impression and the fault rule for every caller, <see cref="Watch"/>
        /// included: it was written a second and a third time beside those two features, which
        /// is the drift this class was lifted out to end.
        /// </para>
        /// <para>
        /// Never throws: an SDK fault is answered as <see cref="AdOutcome.Error"/> and logged on
        /// the way past, so no panel is left on OPENING with every key dead.
        /// </para>
        /// </summary>
        public static async Task<AdShowResult> Show(string placementId)
        {
            var impression = AdImpression.New(placementId);

            try
            {
                return await RewardedAds.Provider.ShowAsync(impression);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return AdShowResult.Failed(AdOutcome.Error, impression, e.Message);
            }
        }

        /// <summary>
        /// The sentence to put in front of a player when nothing was paid.
        ///
        /// One place, because <see cref="VideoPayment.Faulted"/> and
        /// <see cref="AdOutcome.Error"/> are two spellings of the same news and a caller that
        /// checked only one of them would say "unavailable" where it meant "that did not work".
        /// </summary>
        public static string Refusal(VideoPayment payment)
            => payment.Faulted
                 ? Loc.Get("ui.ads.failed")
                 : AdOfferButton.Refusal(payment.Outcome);
    }
}
