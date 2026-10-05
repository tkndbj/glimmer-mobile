using System;
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
    /// parse - and the copy would be the one that goes stale.
    /// </para>
    /// <para>
    /// <b>Two calls, because there are two moments</b> (invariant 55). <see cref="RefreshAsync"/>
    /// is the splash's: a network round trip that draws nothing, and settles the question on
    /// its own for everybody who owes no form - everyone outside the EEA and the UK, and every
    /// player who has already answered. <see cref="AskAsync"/> is the hub's: the form, for the
    /// player who still owes one, shown only once they have seen the game.
    /// </para>
    /// </summary>
    public interface IConsentGateway
    {
        /// <summary>
        /// Brings the consent state up to date <b>without drawing anything</b>, and reports it.
        ///
        /// <para>
        /// An answer that is not <see cref="AdPrivacySignals.ConsentSettled"/> means a form is
        /// owed, or the CMP could not be reached; either way <see cref="AdPrivacy"/> leaves the
        /// question for the hub. Must never throw: a CMP that cannot reach its own servers is
        /// an ordinary Tuesday on a train.
        /// </para>
        /// </summary>
        Task<AdPrivacySignals> RefreshAsync(CancellationToken cancellation = default);

        /// <summary>
        /// Shows the consent form if one is owed, and reports what the player decided.
        ///
        /// <para>
        /// <paramref name="mayPresent"/> is asked once the form is in hand and immediately
        /// before it is shown. A false answer means the player has moved on - into a run, a
        /// panel - and the form is dropped rather than laid over them: the result is
        /// <see cref="ConsentAsk.Deferred"/> and the question is asked again the next time the
        /// hub is idle. Must never throw.
        /// </para>
        /// </summary>
        Task<ConsentAsk> AskAsync(Func<bool> mayPresent, CancellationToken cancellation = default);

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
    /// What came of asking: the player's answer, or "not now".
    ///
    /// <para>
    /// "Not now" is a third outcome rather than a flavour of failure, and the difference is
    /// what happens next. A failure - the CMP unreachable, no form published - closes the
    /// question for this launch with the restrictive answer, exactly as it always has. A
    /// deferral closes nothing: the player walked away from the hub while the form was
    /// loading, nobody has been asked anything, and the hub asks again when they return.
    /// </para>
    /// </summary>
    public readonly struct ConsentAsk
    {
        /// <summary>What the CMP now says. Meaningless when <see cref="Deferred"/>.</summary>
        public readonly AdPrivacySignals Signals;

        /// <summary>True when the form was dropped unseen because the hub was no longer idle.</summary>
        public readonly bool Deferred;

        ConsentAsk(AdPrivacySignals signals, bool deferred)
        {
            Signals = signals;
            Deferred = deferred;
        }

        /// <summary>The player answered, no form was owed, or asking failed for this launch.</summary>
        public static ConsentAsk Answered(AdPrivacySignals signals) => new ConsentAsk(signals, false);

        /// <summary>Nothing was shown; ask again when the hub is next idle.</summary>
        public static ConsentAsk NotNow => new ConsentAsk(AdPrivacySignals.Restricted, true);
    }

    /// <summary>
    /// The gateway used when no CMP is installed.
    ///
    /// <para>
    /// Answers <see cref="AdPrivacySignals.Restricted"/> - no consent, GDPR assumed to apply -
    /// rather than pretending everyone agreed. That is the honest reading and it is also the
    /// safe one: the cost is unpersonalised ads and lower revenue, where the cost of guessing
    /// the other way is personalised ads served to people who never agreed to them.
    /// </para>
    /// <para>
    /// It is <b>not</b> a placeholder to be replaced later. A build with no ad SDK at all - the
    /// Editor, a CI build, a platform without mediation - needs exactly this behaviour for ever,
    /// which is why it reports <see cref="CanRevisit"/> false rather than offering a form it
    /// cannot show.
    /// </para>
    /// </summary>
    public sealed class NullConsentGateway : IConsentGateway
    {
        public Task<AdPrivacySignals> RefreshAsync(CancellationToken cancellation = default)
            => Task.FromResult(AdPrivacySignals.Restricted);

        public Task<ConsentAsk> AskAsync(Func<bool> mayPresent, CancellationToken cancellation = default)
            => Task.FromResult(ConsentAsk.Answered(AdPrivacySignals.Restricted));

        public bool CanRevisit => false;

        public Task<AdPrivacySignals> RevisitAsync(CancellationToken cancellation = default)
            => Task.FromResult(AdPrivacySignals.Restricted);
    }
}
