using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// The seam between the game and whichever consent management platform is installed.
    ///
    /// <para>
    /// Lives in Domain and names no SDK type, which is the same bargain
    /// <see cref="Ads.IAdProvider"/> and <c>ICloudSaveBackend</c> make and it pays for itself
    /// the same way: the ordering, the signals, the settings entry and the tests are all
    /// written and provable with no CMP in the project at all.
    /// </para>
    /// <para>
    /// The interface is deliberately thin, and one thing it deliberately does <b>not</b> do is
    /// hand back a consent string. A CMP writes the IAB TCF string into the platform's own
    /// preference store, where every mediation adapter reads it directly. Carrying a copy
    /// through our code would make us a second source of truth for a value we neither own nor
    /// parse — and the copy would be the one that goes stale.
    /// </para>
    /// </summary>
    public interface IConsentGateway
    {
        /// <summary>
        /// Brings the consent state up to date, prompting if the CMP says a prompt is owed.
        ///
        /// <para>
        /// Called once on the boot path and awaited, which is the whole point of it — see
        /// <see cref="AdPrivacy.ResolveAsync"/> for why the ad SDK must not start before this
        /// completes. It must never throw: a CMP that cannot reach its own servers is an
        /// ordinary Tuesday on a train, and the answer then is the restrictive default rather
        /// than a crash on the splash screen.
        /// </para>
        /// </summary>
        Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default);

        /// <summary>
        /// Whether this player is entitled to a "privacy options" control in Settings.
        ///
        /// <para>
        /// A question rather than a constant, because the answer is per-player: the CMP
        /// decides, from the player's own jurisdiction and from what they were shown. Drawing
        /// the row unconditionally would put a button in front of players it does nothing for;
        /// hiding it from somebody in the EEA who has consented is a compliance failure,
        /// because withdrawing consent has to be as easy as giving it.
        /// </para>
        /// </summary>
        bool CanRevisit { get; }

        /// <summary>
        /// Reopens the consent form so the player can change their mind, and returns what they
        /// decided. Only meaningful when <see cref="CanRevisit"/> is true.
        /// </summary>
        Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default);
    }

    /// <summary>
    /// The gateway used when no CMP is installed.
    ///
    /// <para>
    /// Answers <see cref="AdPrivacySignals.Restricted"/> — no consent, GDPR assumed to apply —
    /// rather than pretending everyone agreed. That is the honest reading and it is also the
    /// safe one: the cost is unpersonalised ads and lower revenue, where the cost of guessing
    /// the other way is personalised ads served to people who never agreed to them.
    /// </para>
    /// <para>
    /// It is <b>not</b> a placeholder to be replaced later. A build with no ad SDK at all — the
    /// Editor, a CI build, a platform without mediation — needs exactly this behaviour for ever,
    /// which is why it reports <see cref="CanRevisit"/> false rather than offering a form it
    /// cannot show.
    /// </para>
    /// </summary>
    public sealed class NullConsentGateway : IConsentGateway
    {
        public Task<AdPrivacySignals> ResolveAsync(CancellationToken cancellation = default)
            => Task.FromResult(AdPrivacySignals.Restricted);

        public bool CanRevisit => false;

        public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
            => Task.FromResult(AdPrivacySignals.Restricted);
    }
}
