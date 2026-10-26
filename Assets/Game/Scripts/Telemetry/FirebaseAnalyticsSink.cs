#if GLIMMER_ANALYTICS
using System;
using System.Collections.Generic;
using System.Text;
using Firebase.Analytics;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Firebase transport. Eligibility, bounded buffering and retry policy live in
    /// <see cref="AnalyticsDelivery"/> and can be tested without a native SDK.
    /// </summary>
    public sealed class FirebaseAnalyticsSink : IAnalyticsSink
    {
        /// <summary>GA4 accepts at most 25 parameters on an event; the rest are dropped.</summary>
        public const int MaxParameters = AnalyticsDelivery.MaxParameters;

        const int MaxNameLength = 40;
        const int MaxStringValueLength = 100;

        readonly List<Parameter> _parameters = new List<Parameter>(MaxParameters);
        readonly AnalyticsDelivery _delivery;

        public FirebaseAnalyticsSink()
        {
            _delivery = new AnalyticsDelivery(SetCollection,
                Send, () => Time.realtimeSinceStartupAsDouble, Diagnose);
        }

        // Resolve the Firebase API only at invocation, after the dependency gate has opened.
        static void SetCollection(bool allowed) => FirebaseAnalytics.SetAnalyticsCollectionEnabled(allowed);

        public void MarkReady() => _delivery.MarkReady();

        /// <summary>
        /// Hands the four consent-mode purposes to the SDK (<c>setConsent</c>), which persists
        /// them across launches. Called only once the dependency check has passed, and before
        /// <see cref="MarkReady"/> lets the first event out. False when the native call threw;
        /// the manifest's denied defaults then still stand, which is the safe side.
        /// </summary>
        public bool ApplyConsent(GlimmerGrove.Privacy.AnalyticsConsent consent)
        {
            try
            {
                FirebaseAnalytics.SetConsent(new Dictionary<ConsentType, Firebase.Analytics.ConsentStatus>
                {
                    { ConsentType.AnalyticsStorage, Status(consent.AnalyticsStorage) },
                    { ConsentType.AdStorage, Status(consent.AdStorage) },
                    { ConsentType.AdUserData, Status(consent.AdUserData) },
                    { ConsentType.AdPersonalization, Status(consent.AdPersonalization) },
                });
                return true;
            }
            catch (Exception)
            {
                // No exception message: native failures can include configuration or device data.
                return false;
            }
        }

        static Firebase.Analytics.ConsentStatus Status(bool granted)
            => granted ? Firebase.Analytics.ConsentStatus.Granted : Firebase.Analytics.ConsentStatus.Denied;
        public void SetCollectionAllowed(bool allowed) => _delivery.SetCollectionAllowed(allowed);
        public void Tick() => _delivery.Tick();

        public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            string name = Sanitise(eventName);
            _delivery.Track(name, properties);
        }

        AnalyticsSendResult Send(string name, IReadOnlyDictionary<string, object> properties)
        {
            Parameter[] parameters;
            try
            {
                _parameters.Clear();
                if (properties != null)
                {
                    foreach (var kv in properties)
                    {
                        if (_parameters.Count >= MaxParameters) break;

                        string key = Sanitise(kv.Key);
                        if (key == null) continue;

                        Parameter parameter = ToParameter(key, kv.Value);
                        if (parameter != null) _parameters.Add(parameter);
                    }
                }
                parameters = _parameters.ToArray();
            }
            catch (Exception)
            {
                // Nothing has reached LogEvent: retrying cannot duplicate an SDK event.
                return AnalyticsSendResult.Retry;
            }

            try
            {
                FirebaseAnalytics.LogEvent(name, parameters);
                return AnalyticsSendResult.Accepted;
            }
            catch (Exception)
            {
                // LogEvent has no acknowledgement/idempotency contract. A native exception may
                // follow acceptance; report the uncertain outcome without replaying revenue.
                return AnalyticsSendResult.Unconfirmed;
            }
        }

        void Diagnose(AnalyticsDeliveryIssue issue, long count)
        {
            string message = $"[Analytics] delivery {issue}; count={count}; pending={_delivery.PendingCount}";
            if (issue == AnalyticsDeliveryIssue.Excluded || issue == AnalyticsDeliveryIssue.CollectionEnabled
                || issue == AnalyticsDeliveryIssue.CollectionDisabled)
                Debug.Log(message);
            else Debug.LogWarning(message);
        }

        /// <summary>
        /// Forces a name or a parameter key into what GA4 will accept: 40 characters or
        /// fewer, letters, digits and underscores only, beginning with a letter, and not
        /// wearing one of Google's own reserved prefixes.
        /// </summary>
        static string Sanitise(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            var sb = new StringBuilder(MaxNameLength);

            // A reserved prefix is rejected outright by GA4, so it is prefixed away rather
            // than stripped: "ga_cost" losing three characters would collide with anything
            // legitimately called "cost".
            if (StartsWithReserved(raw)) sb.Append("x_");

            for (int i = 0; i < raw.Length && sb.Length < MaxNameLength; i++)
            {
                char c = raw[i];
                bool letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                bool digit = c >= '0' && c <= '9';

                if (sb.Length == 0 && !letter)
                {
                    // Must begin with a letter. A leading digit or underscore is carried
                    // rather than discarded, so "3star" and "star" stay different events.
                    sb.Append('x').Append('_');
                    if (digit || c == '_') sb.Append(c);
                    continue;
                }

                sb.Append(letter || digit || c == '_' ? c : '_');
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        static bool StartsWithReserved(string raw)
            => raw.StartsWith("firebase_", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("google_", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("ga_", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Maps a boxed property onto one of GA4's three parameter types.
        ///
        /// <para>
        /// Numbers stay numbers, because a figure delivered as text cannot be averaged or
        /// bucketed in a report and there is no way to change that after the fact - the rows
        /// are already written. Booleans go as 0/1 for the same reason: "true" and "false"
        /// are a two-valued dimension where 0 and 1 are a rate.
        /// </para>
        /// </summary>
        static Parameter ToParameter(string key, object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string s:
                    return new Parameter(key, Truncate(s));
                case bool b:
                    return new Parameter(key, b ? 1L : 0L);
                case int i:
                    return new Parameter(key, (long)i);
                case long l:
                    return new Parameter(key, l);
                case short sh:
                    return new Parameter(key, (long)sh);
                case byte by:
                    return new Parameter(key, (long)by);
                case float f:
                    return new Parameter(key, (double)f);
                case double d:
                    return new Parameter(key, d);
                case decimal m:
                    return new Parameter(key, (double)m);
                case Enum e:
                    return new Parameter(key, Truncate(e.ToString()));
                default:
                    return new Parameter(key, Truncate(value.ToString()));
            }
        }

        static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= MaxStringValueLength
                ? value
                : value.Substring(0, MaxStringValueLength);
        }
    }
}
#endif
