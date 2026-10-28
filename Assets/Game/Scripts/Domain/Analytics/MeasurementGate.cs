using System;
using System.Collections.Generic;
using GlimmerGrove.Privacy;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// When the two measurement SDKs may open, and on whose answer (invariant 55b).
    ///
    /// <para>
    /// <b>Why it exists.</b> Android 1.1.4 opened Firebase collection the moment its dependency
    /// check passed, with every consent purpose still at the manifest's denied default. Firebase
    /// logs <c>first_open</c> and <c>session_start</c> at exactly that moment, so they went out as
    /// cookieless pings GA never reports; the splash's UMP answer granted consent a few seconds
    /// later, and <c>first_open</c> is never sent twice. GA showed 0 first opens for the build.
    /// So collection now waits for the answer: <b>UMP answers, consent is applied, then collection
    /// opens</b>, and the first event carries what the player is actually owed.
    /// </para>
    /// <para>
    /// <b>Three ways to open, and none of them waits on the game.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>Answered</b> - <c>AdPrivacy</c> committed an answer (outside the EEA, or a player who
    /// answered before). Both SDKs open on it.</item>
    /// <item><b>Form owed</b> - the splash's refresh finished and the consent form is still owed (a new
    /// EEA player). Analytics opens at once, every purpose denied, which is exactly what the player
    /// is owed until the form. Attribution keeps waiting for the form (the owner, 2026-10-28): an
    /// AppsFlyer install reported anonymised can never be attributed, so starting it before the form
    /// would cost every EEA player who then consents.</item>
    /// <item><b>Timed out</b> - UMP was asked and has said nothing for <see cref="AnswerSeconds"/>,
    /// or nobody asked it within <see cref="UnaskedSeconds"/> of install (a build or launch with no
    /// consent platform). Both open under the restrictive answer, the region unknown: Analytics
    /// denied, attribution anonymised as a GDPR player. A late answer is applied when it comes.</item>
    /// </list>
    /// <para>
    /// The clock runs from the moment UMP is asked, not from install: the splash asks only after
    /// its assets load, and a slow phone spending four seconds on that would otherwise time out a
    /// question it had not yet put. Main thread only; polled by <c>AnalyticsSetup.Tick</c>.
    /// </para>
    /// </summary>
    public sealed class MeasurementGate
    {
        /// <summary>How long an asked UMP has to answer before both SDKs open without it.</summary>
        public const double AnswerSeconds = 3d;

        /// <summary>How long after install a UMP nobody has asked stops holding measurement back.</summary>
        public const double UnaskedSeconds = 20d;

        readonly Func<double> _now;
        readonly double _installedAt;
        double _askedAt = double.NaN;
        bool _prepared, _answered, _timedOut;
        AdPrivacySignals _signals = AdPrivacySignals.Restricted;

        public MeasurementGate(Func<double> now)
        {
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _installedAt = Read();
        }

        /// <summary>An answer has been committed (<c>AdPrivacy.Changed</c>).</summary>
        public bool IsAnswered => _answered;

        /// <summary>The latest committed answer, or the restrictive one before any.</summary>
        public AdPrivacySignals Signals => _signals;

        /// <summary>
        /// The splash's question has been put (<paramref name="asked"/>) or answered without settling
        /// (<paramref name="prepared"/>: the form is owed). Polled; idempotent.
        /// </summary>
        public void Observe(bool asked, bool prepared)
        {
            if (asked && double.IsNaN(_askedAt)) _askedAt = Read();
            if (prepared) _prepared = true;
        }

        /// <summary>An answer was committed. Carried from the event, never read back off <c>AdPrivacy</c>.</summary>
        public void Answer(AdPrivacySignals signals)
        {
            _signals = signals;
            _answered = true;
        }

        /// <summary>UMP said nothing in time. Latched: a timed-out launch never closes again.</summary>
        public bool IsTimedOut
        {
            get
            {
                if (_timedOut || _answered || _prepared) return _timedOut;

                double now = Read();
                _timedOut = double.IsNaN(_askedAt)
                    ? now - _installedAt >= UnaskedSeconds
                    : now - _askedAt >= AnswerSeconds;
                return _timedOut;
            }
        }

        /// <summary>Firebase may turn collection on: consent for the first event is known.</summary>
        public bool AnalyticsMayOpen => _answered || _prepared || IsTimedOut;

        /// <summary>
        /// Whether AppsFlyer may start now, and under what. An answer starts it as the player gave
        /// it; a time-out with no word from UMP starts it anonymised; a form still owed waits.
        /// </summary>
        public bool AttributionMayStart(out AttributionConsent consent)
        {
            if (_answered)
            {
                consent = AttributionConsent.For(_signals);
                return true;
            }

            consent = AttributionConsent.For(AdPrivacySignals.Restricted);
            return !_prepared && IsTimedOut;
        }

        double Read()
        {
            double now = _now();
            return double.IsNaN(now) || double.IsInfinity(now) ? 0d : now;
        }
    }

    /// <summary>
    /// The Firebase order (invariant 55b): consent is handed to the SDK before collection is turned
    /// on, so <c>first_open</c> and <c>session_start</c> carry it. The SDK calls are delegates, so the
    /// order is held by <c>MeasurementGateTests</c> without a native SDK.
    /// </summary>
    public sealed class AnalyticsOpening
    {
        readonly Func<AnalyticsConsent, bool> _applyConsent;
        readonly Action _open;
        AnalyticsConsent _consent;
        AnalyticsConsent? _applied;
        bool _sdkReady, _opened;

        /// <param name="initial">What the SDK is told if nothing better is known by the time it opens.</param>
        /// <param name="applyConsent">Hands the four purposes to the SDK; false when the native call failed.</param>
        /// <param name="open">Opens delivery, which turns native collection on (<see cref="AnalyticsDelivery.MarkReady"/>).</param>
        public AnalyticsOpening(AnalyticsConsent initial, Func<AnalyticsConsent, bool> applyConsent, Action open)
        {
            _consent = initial;
            _applyConsent = applyConsent ?? throw new ArgumentNullException(nameof(applyConsent));
            _open = open ?? throw new ArgumentNullException(nameof(open));
        }

        public bool IsOpen => _opened;

        /// <summary>The last consent the SDK accepted; null before any.</summary>
        public AnalyticsConsent? Applied => _applied;

        /// <summary>A new answer. Pushed at once if the SDK can be called, before or after opening.</summary>
        public void Consent(AnalyticsConsent consent)
        {
            _consent = consent;
            Push();
        }

        /// <summary>The native SDK has passed its dependency check and may be called.</summary>
        public void SdkReady()
        {
            _sdkReady = true;
            Push();
        }

        /// <summary>
        /// Opens once the SDK is ready and the gate allows it, consent pushed first. A push the native
        /// side refused still opens: the manifest's denied defaults then stand, which is the safe side.
        /// </summary>
        public bool TryOpen(bool mayOpen)
        {
            if (_opened || !_sdkReady || !mayOpen) return false;

            Push();
            _opened = true;
            _open();
            return true;
        }

        void Push()
        {
            if (!_sdkReady) return;
            if (_applied.HasValue && _applied.Value.Equals(_consent)) return;
            if (_applyConsent(_consent)) _applied = _consent;
        }
    }

    /// <summary>
    /// What AppsFlyer is told: whether GDPR applies and whether the player allowed personalisation.
    /// Not allowed means anonymised as well as consent data saying so (<c>AnalyticsSetup.AppsFlyer</c>).
    /// </summary>
    public readonly struct AttributionConsent : IEquatable<AttributionConsent>
    {
        public readonly bool GdprApplies;
        public readonly bool Allowed;

        public AttributionConsent(bool gdprApplies, bool allowed)
        {
            GdprApplies = gdprApplies;
            Allowed = allowed;
        }

        /// <summary>One consent answer for the whole project, so the conservative mapping is the only honest one.</summary>
        public static AttributionConsent For(AdPrivacySignals signals)
            => new AttributionConsent(signals.GdprApplies, signals.AllowsPersonalisation);

        public bool Anonymised => !Allowed;

        public bool Equals(AttributionConsent other) => GdprApplies == other.GdprApplies && Allowed == other.Allowed;
        public override bool Equals(object obj) => obj is AttributionConsent other && Equals(other);
        public override int GetHashCode() => (GdprApplies ? 2 : 0) | (Allowed ? 1 : 0);
        public override string ToString() => $"gdpr={GdprApplies} allowed={Allowed}";
    }

    /// <summary>The attribution SDK, as <see cref="AttributionStart"/> drives it.</summary>
    public interface IAttributionSdk
    {
        /// <summary>Consent data and anonymisation. Called before <see cref="Start"/>, and again on every change.</summary>
        void ApplyConsent(AttributionConsent consent);

        void Start();

        void Send(string name, IReadOnlyDictionary<string, string> values);
    }

    /// <summary>
    /// Starts attribution once, consent first, and holds the events raised before it.
    ///
    /// <para>
    /// <b>A small queue, because the tutorial now comes first.</b> Its lessons
    /// (<c>af_achievement_unlocked</c>) can be finished before UMP answers on a slow network, and an
    /// EEA player's whole tutorial is played before the form. Up to <see cref="MaxHeld"/> are kept,
    /// oldest first (the earliest funnel steps are the ones worth most); later ones are dropped and
    /// counted. The queue is process-local: a player who quits before the start loses it, exactly as
    /// the install itself is not reported for them.
    /// </para>
    /// <para>
    /// <b>Nothing held leaves under a weaker consent than the player gave.</b> The flush runs only
    /// after <see cref="IAttributionSdk.ApplyConsent"/> and <see cref="IAttributionSdk.Start"/>, under
    /// the same consent every live event carries; a player who has not allowed personalisation sends
    /// them anonymised, as they send everything. A consent change after the start is applied before
    /// the next event.
    /// </para>
    /// </summary>
    public sealed class AttributionStart
    {
        public const int MaxHeld = 32;

        readonly IAttributionSdk _sdk;
        readonly Queue<KeyValuePair<string, Dictionary<string, string>>> _held =
            new Queue<KeyValuePair<string, Dictionary<string, string>>>(MaxHeld);
        AttributionConsent? _applied;

        public AttributionStart(IAttributionSdk sdk) => _sdk = sdk ?? throw new ArgumentNullException(nameof(sdk));

        public bool Started { get; private set; }
        public int Held => _held.Count;
        public long Dropped { get; private set; }

        /// <summary>The consent the SDK was last told; null before the start.</summary>
        public AttributionConsent? Applied => _applied;

        /// <summary>Sends now once started; holds a copy until then.</summary>
        public void Send(string name, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (Started)
            {
                _sdk.Send(name, values);
                return;
            }

            if (_held.Count >= MaxHeld)
            {
                Dropped++;
                return;
            }

            var copy = new Dictionary<string, string>(values?.Count ?? 0);
            if (values != null) foreach (var pair in values) copy[pair.Key] = pair.Value;
            _held.Enqueue(new KeyValuePair<string, Dictionary<string, string>>(name, copy));
        }

        /// <summary>
        /// Applies <paramref name="consent"/>, starts the SDK the first time, then flushes what was
        /// held. Later calls re-apply a changed consent only. Exceptions from the SDK propagate to the
        /// caller, which logs them; a start that threw is not marked started.
        /// </summary>
        public void Start(AttributionConsent consent)
        {
            if (!_applied.HasValue || !_applied.Value.Equals(consent))
            {
                _sdk.ApplyConsent(consent);
                _applied = consent;
            }

            if (Started) return;

            _sdk.Start();
            Started = true;

            while (_held.Count > 0)
            {
                var next = _held.Dequeue();
                _sdk.Send(next.Key, next.Value);
            }
        }
    }
}
