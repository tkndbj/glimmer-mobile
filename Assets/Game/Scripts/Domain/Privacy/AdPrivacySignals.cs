using System;

namespace GlimmerGrove.Privacy
{
    /// <summary>
    /// What a player has been asked, and what they said.
    ///
    /// Three states, and the third is the one that matters: <see cref="Unknown"/> is not a
    /// synonym for <see cref="Denied"/>. A user who has never been asked is a user we may
    /// still ask; one who declined is not. Collapsing the two either re-prompts somebody who
    /// already said no — which is the thing consent law is most pointed about — or treats
    /// silence as agreement, which is worse.
    /// </summary>
    public enum ConsentStatus
    {
        /// <summary>Never asked, or the answer could not be read. Personalisation is off.</summary>
        Unknown = 0,

        /// <summary>The player agreed to personalised advertising.</summary>
        Granted,

        /// <summary>The player declined. Ads still show; they are contextual rather than personal.</summary>
        Denied,
    }

    /// <summary>
    /// Whether the iOS tracking prompt has been answered, and how.
    ///
    /// Mirrors <c>ATTrackingManager.AuthorizationStatus</c> including its numbering, which is
    /// deliberate: the values cross a native boundary as an int and inventing our own ordering
    /// would put a translation table between two things that already agree. Meaningless on
    /// Android, where it is always <see cref="NotSupported"/>.
    /// </summary>
    public enum TrackingStatus
    {
        /// <summary>The prompt has not been answered yet. Apple's <c>notDetermined</c>.</summary>
        NotDetermined = 0,

        /// <summary>Blocked by device policy — parental controls, MDM. Cannot be prompted.</summary>
        Restricted = 1,

        /// <summary>The player said no.</summary>
        Denied = 2,

        /// <summary>The player said yes. The IDFA is readable.</summary>
        Authorized = 3,

        /// <summary>Not an iOS build, or an iOS version that predates the prompt.</summary>
        NotSupported = 99,
    }

    /// <summary>
    /// Everything the ad SDK needs to know about what it may do with this player's data.
    ///
    /// <para>
    /// <b>A value, decided once and handed down.</b> The alternative — every layer asking a
    /// consent SDK what it thinks, whenever it wants to know — is how an app ends up
    /// initialising mediation before the answer has arrived, which is the single failure that
    /// makes the whole exercise pointless: an SDK started without consent has already decided
    /// what it may collect, and telling it afterwards does not undo the first auction. See
    /// <see cref="AdPrivacy"/> for the ordering that prevents it.
    /// </para>
    /// <para>
    /// It carries no IAB TCF string, and that is not an omission. The consent string is
    /// written by the CMP into the platform's own preference store, where every mediation
    /// adapter reads it directly; passing a copy through our code would be a second source of
    /// truth for a value we do not own, do not parse and cannot correct. What travels here is
    /// only what a mediation SDK has to be <em>told</em> in an API call.
    /// </para>
    /// </summary>
    public readonly struct AdPrivacySignals : IEquatable<AdPrivacySignals>
    {
        /// <summary>
        /// Whether the player is somewhere GDPR applies — the EEA, the UK.
        ///
        /// Answered by the CMP from the device's own geography rather than guessed from a
        /// locale, because a German phone in a Turkish airport is still a German user and a
        /// system language is not a jurisdiction.
        /// </summary>
        public readonly bool GdprApplies;

        /// <summary>What the player said, or <see cref="ConsentStatus.Unknown"/> if never asked.</summary>
        public readonly ConsentStatus Gdpr;

        /// <summary>
        /// Whether the player has exercised a US "do not sell my personal information" right.
        ///
        /// Separate from <see cref="Gdpr"/> because the laws are: CCPA is opt-<em>out</em> and
        /// GDPR is opt-<em>in</em>, so a single "consented" flag would have to mean the
        /// opposite thing in each and would eventually be read the wrong way round.
        /// </summary>
        public readonly bool DoNotSell;

        /// <summary>
        /// Whether this install is treated as child-directed under COPPA.
        ///
        /// A constant today — see <see cref="AdPrivacy.ChildDirected"/> — but carried here
        /// rather than read at the point of use, so the day it stops being a constant there is
        /// one place that changes.
        /// </summary>
        public readonly bool ChildDirected;

        /// <summary>The iOS tracking prompt's answer. See <see cref="TrackingStatus"/>.</summary>
        public readonly TrackingStatus Tracking;

        public AdPrivacySignals(bool gdprApplies, ConsentStatus gdpr, bool doNotSell,
                                bool childDirected, TrackingStatus tracking)
        {
            GdprApplies = gdprApplies;
            Gdpr = gdpr;
            DoNotSell = doNotSell;
            ChildDirected = childDirected;
            Tracking = tracking;
        }

        /// <summary>
        /// What is assumed before anything has been resolved, and what a build with no CMP
        /// stays on for ever.
        ///
        /// <para>
        /// Deliberately the <em>restrictive</em> answer rather than the permissive one: no
        /// consent, and GDPR assumed to apply. That costs revenue on every non-EU player until
        /// a CMP is installed, which is exactly the right way round — the failure mode of
        /// guessing "consented" is serving personalised ads to somebody who never agreed, and
        /// no amount of revenue makes that a good trade.
        /// </para>
        /// </summary>
        public static AdPrivacySignals Restricted =>
            new AdPrivacySignals(true, ConsentStatus.Unknown, true, false, TrackingStatus.NotDetermined);

        /// <summary>
        /// Whether personalised advertising is permitted.
        ///
        /// <para>
        /// The one derived answer in the type, and it is derived rather than stored so that
        /// the three inputs cannot come to disagree with it. Outside GDPR territory the
        /// absence of a prompt is not a refusal, so consent is not required; inside it,
        /// nothing but an explicit <see cref="ConsentStatus.Granted"/> will do. A US opt-out
        /// overrides both, and a child-directed install overrides everything.
        /// </para>
        /// <para>
        /// Note what this does <b>not</b> gate: whether an ad is shown at all. A player who
        /// declines still sees contextual ads and still earns the reward — an offer that
        /// quietly stopped working after somebody exercised a legal right would be a dark
        /// pattern with a legal department attached.
        /// </para>
        /// </summary>
        public bool AllowsPersonalisation
        {
            get
            {
                if (ChildDirected || DoNotSell) return false;
                if (!GdprApplies) return true;
                return Gdpr == ConsentStatus.Granted;
            }
        }

        /// <summary>
        /// Whether the device advertising id may be used — the iOS half, which the OS
        /// enforces regardless of what any SDK is told.
        ///
        /// <see cref="TrackingStatus.NotSupported"/> is permissive because it means Android or
        /// an iOS old enough to predate the prompt, where the id is governed by the platform's
        /// own settings rather than by us.
        /// </summary>
        public bool AllowsDeviceId
            => Tracking == TrackingStatus.Authorized || Tracking == TrackingStatus.NotSupported;

        public bool Equals(AdPrivacySignals other)
            => GdprApplies == other.GdprApplies && Gdpr == other.Gdpr
            && DoNotSell == other.DoNotSell && ChildDirected == other.ChildDirected
            && Tracking == other.Tracking;

        public override bool Equals(object obj) => obj is AdPrivacySignals other && Equals(other);

        public override int GetHashCode()
            => (GdprApplies, Gdpr, DoNotSell, ChildDirected, Tracking).GetHashCode();

        public static bool operator ==(AdPrivacySignals a, AdPrivacySignals b) => a.Equals(b);
        public static bool operator !=(AdPrivacySignals a, AdPrivacySignals b) => !a.Equals(b);

        public override string ToString()
            => $"gdpr {(GdprApplies ? "applies" : "n/a")}/{Gdpr}, doNotSell {DoNotSell}, " +
               $"child {ChildDirected}, att {Tracking} → " +
               $"personalised {AllowsPersonalisation}, deviceId {AllowsDeviceId}";
    }
}
