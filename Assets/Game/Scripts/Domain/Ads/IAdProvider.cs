using System;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// How an attempt to show a rewarded ad ended.
    ///
    /// Every member here is a state the player can reach on a bad train, and each one has
    /// to produce a different sentence on screen. That is why this is not a bool: "it
    /// didn't work" covers a network with nothing to serve, a video the player skipped and
    /// an SDK that never initialised, and those deserve three different answers — one is
    /// "try later", one is "you have to watch it all", one is our fault.
    /// </summary>
    public enum AdOutcome
    {
        /// <summary>The video was watched to the end. The only outcome that pays.</summary>
        Rewarded = 0,

        /// <summary>Closed early. No reward, and not an error — the player chose this.</summary>
        Dismissed,

        /// <summary>
        /// The network had nothing to show. Ordinary and frequent: fill varies by country,
        /// by hour and by how many the player has already seen today.
        /// </summary>
        NoFill,

        /// <summary>The SDK is absent, disabled, or has not finished starting.</summary>
        Unavailable,

        /// <summary>Anything else. <see cref="AdShowResult.Message"/> carries the detail.</summary>
        Error,
    }

    /// <summary>
    /// What one show attempt produced.
    ///
    /// Carries the <see cref="AdImpression"/> back rather than just the outcome, so the
    /// caller does not have to hold it across an await to know which nonce was spent. The
    /// awarding path needs that nonce and nothing else.
    /// </summary>
    public readonly struct AdShowResult
    {
        public readonly AdOutcome Outcome;
        public readonly AdImpression Impression;
        public readonly string Message;

        public AdShowResult(AdOutcome outcome, AdImpression impression, string message = null)
        {
            Outcome = outcome;
            Impression = impression;
            Message = message ?? string.Empty;
        }

        public bool Rewarded => Outcome == AdOutcome.Rewarded && Impression.IsValid;

        public static AdShowResult Failed(AdOutcome outcome, AdImpression impression, string message = null)
            => new AdShowResult(outcome, impression, message);
    }

    /// <summary>
    /// The seam between the game and whichever mediation SDK is installed.
    ///
    /// <para>
    /// Lives in Domain and names no SDK type, which is what lets the whole rewarded-ad
    /// feature — the caps, the content-driven amounts, the award path, the UI, the tests —
    /// be written, run and tested with no ad SDK in the project at all. The same bargain
    /// <c>ICloudSaveBackend</c> makes, and it paid for itself there: the cloud save client
    /// was complete and correct months before the Firebase packages resolved.
    /// </para>
    /// <para>
    /// The interface is deliberately thin. Mediation SDKs want to own initialisation,
    /// preloading, waterfalls, per-network adapters and a dozen callbacks; none of that is
    /// the game's business. The game asks two questions — <em>can I offer this?</em> and
    /// <em>show it and tell me how it went</em> — and everything else belongs behind the
    /// implementation.
    /// </para>
    /// </summary>
    public interface IAdProvider
    {
        /// <summary>
        /// Whether the SDK has started and is willing to be asked for ads at all.
        /// False on a build with no SDK, and while initialisation is still in flight.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Whether a rewarded ad for this placement is loaded and can be shown now.
        ///
        /// <para>
        /// Asked before the offer is drawn, not after it is tapped. An offer that appears
        /// and then fails is worse than one that never appeared, because the player has
        /// already decided they want it — and this is the resource they were short of.
        /// </para>
        /// </summary>
        bool IsReady(string placementId);

        /// <summary>
        /// Raised when readiness changes for any placement, so an open panel can enable
        /// its button the moment fill arrives instead of when the player next taps.
        /// </summary>
        event Action<string> ReadinessChanged;

        /// <summary>
        /// Tells the SDK what it may do with this player's data.
        ///
        /// <para>
        /// <b>Must be called before <see cref="InitializeAsync"/>, and the ordering is the
        /// whole point.</b> A mediation SDK that starts without having been told has already
        /// decided what it may collect and has already run an auction on it; a signal applied
        /// afterwards changes the next request and cannot undo the first. <c>Boot</c> awaits
        /// <c>AdPrivacy.ResolveAsync</c>, applies the result here, and only then initialises.
        /// </para>
        /// <para>
        /// Also called again whenever the player revisits the consent form, which is why it is
        /// separate from initialisation rather than a parameter of it: withdrawing consent has
        /// to reach the SDK without restarting the app.
        /// </para>
        /// </summary>
        void ApplyPrivacy(Privacy.AdPrivacySignals signals);

        /// <summary>Starts the SDK. Safe to call more than once; later calls are no-ops.</summary>
        Task InitializeAsync(CancellationToken cancellation = default);

        /// <summary>
        /// Shows the ad and completes when it is finished, one way or another.
        ///
        /// <para>
        /// The <paramref name="impression"/> is generated by the caller and must be handed
        /// to the SDK as the custom parameter the network echoes into its server-side
        /// verification callback. That is the contract on which the entire grant path
        /// rests: an implementation that drops the nonce produces ads that pay nothing,
        /// because the server will never see a callback it can match to a claim.
        /// </para>
        /// <para>
        /// Never throws for an ordinary failure — a network with no fill is a normal
        /// Tuesday, not an exception. Everything the caller must handle arrives as an
        /// <see cref="AdOutcome"/>.
        /// </para>
        /// </summary>
        Task<AdShowResult> ShowAsync(AdImpression impression, CancellationToken cancellation = default);
    }

    /// <summary>
    /// The provider used when no ad SDK is installed, or when ads are switched off.
    ///
    /// <para>
    /// Reports itself unavailable rather than pretending to be a working provider that
    /// never has fill. The distinction reaches the player: <see cref="AdOutcome.NoFill"/>
    /// says "try again in a bit" and would be a lie here, where the answer is "this build
    /// cannot show ads" and no amount of waiting changes it. A null object that lies about
    /// which kind of nothing it is turns a missing dependency into a support ticket.
    /// </para>
    /// <para>
    /// It is also what keeps the offer buttons off the screen entirely in an SDK-less
    /// build — <see cref="RewardedAds.CanOffer"/> asks <see cref="IsReady"/>, and this
    /// says no — so the feature ships dark rather than broken.
    /// </para>
    /// </summary>
    public sealed class NullAdProvider : IAdProvider
    {
        public bool IsInitialized => false;

        public bool IsReady(string placementId) => false;

        /// <summary>Never raised. Kept so callers need not null-check the subscription.</summary>
        public event Action<string> ReadinessChanged
        {
            add { }
            remove { }
        }

        /// <summary>Nothing to tell. Kept so no caller has to branch on which provider it holds.</summary>
        public void ApplyPrivacy(Privacy.AdPrivacySignals signals) { }

        public Task InitializeAsync(CancellationToken cancellation = default)
            => Task.CompletedTask;

        public Task<AdShowResult> ShowAsync(AdImpression impression, CancellationToken cancellation = default)
            => Task.FromResult(AdShowResult.Failed(AdOutcome.Unavailable, impression,
                                                   "no ad provider is installed in this build"));
    }
}
