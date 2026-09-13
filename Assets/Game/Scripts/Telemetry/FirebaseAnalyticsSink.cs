#if GLIMMER_ANALYTICS
using System;
using System.Collections.Generic;
using System.Text;
using Firebase.Analytics;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// Delivers <see cref="Telemetry"/> events to Google Analytics for Firebase.
    ///
    /// <para>
    /// The seam this plugs into has existed since the first level and has never had a sink
    /// behind it in a shipped build, so every event this project raises has been going
    /// nowhere on a device. Nothing about that was visible: <see cref="Telemetry.Track"/>
    /// returns immediately when no sink is attached, and the Editor has always had
    /// <see cref="DebugAnalyticsSink"/>, so the console showed events on the one machine
    /// where they were worth nothing.
    /// </para>
    /// <para>
    /// GA4 refuses names it does not like rather than mangling them, and it refuses them
    /// <em>server-side</em> — the call succeeds, the device logs nothing, and the event is
    /// simply absent from a report weeks later. So every name and key is forced into the
    /// legal shape here rather than trusted to call sites: a rule enforced at one boundary
    /// cannot be forgotten by the ninetieth caller. The costs of the two mistakes are not
    /// symmetrical — a squashed name is a row with an odd label, a rejected one is data that
    /// was never collected and cannot be backfilled.
    /// </para>
    /// <para>
    /// Events raised before Firebase has resolved its native dependencies are held rather
    /// than dropped, because the earliest ones are the most valuable: first launch, the
    /// tutorial, and the first level are exactly the window a retention test is bought to
    /// measure, and they all happen while the SDK is still starting. The queue is bounded,
    /// since a device where Firebase never becomes ready must not accumulate a session's
    /// worth of events it will never send.
    /// </para>
    /// </summary>
    public sealed class FirebaseAnalyticsSink : IAnalyticsSink
    {
        /// <summary>GA4 accepts at most 25 parameters on an event; the rest are dropped.</summary>
        public const int MaxParameters = 25;

        const int MaxNameLength = 40;
        const int MaxStringValueLength = 100;

        /// <summary>
        /// How many events are held while the SDK starts. Generous enough to cover a launch
        /// and the opening of a run, small enough that a device where Firebase never resolves
        /// is holding kilobytes rather than a session.
        /// </summary>
        const int MaxQueued = 64;

        readonly List<Parameter> _parameters = new List<Parameter>(MaxParameters);
        readonly Queue<Pending> _queued = new Queue<Pending>(MaxQueued);

        bool _ready;
        int _dropped;

        readonly struct Pending
        {
            public readonly string Name;
            public readonly Dictionary<string, object> Properties;

            public Pending(string name, Dictionary<string, object> properties)
            {
                Name = name;
                Properties = properties;
            }
        }

        /// <summary>
        /// Called once the native SDK has resolved. Flushes whatever was held, in order.
        /// </summary>
        public void MarkReady()
        {
            if (_ready) return;
            _ready = true;

            while (_queued.Count > 0)
            {
                var pending = _queued.Dequeue();
                Send(pending.Name, pending.Properties);
            }

            if (_dropped > 0)
            {
                Debug.LogWarning($"[Analytics] {_dropped} event(s) were dropped before Firebase was ready");
                _dropped = 0;
            }
        }

        public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            string name = Sanitise(eventName);
            if (name == null) return;

            if (!_ready)
            {
                if (_queued.Count >= MaxQueued) { _dropped++; return; }

                // Telemetry reuses one scratch dictionary for every event, so holding a
                // reference to it would hand the flush whatever the *last* event happened to
                // carry. Copy, or every queued event arrives wearing the same properties.
                var copy = new Dictionary<string, object>(properties?.Count ?? 0);
                if (properties != null)
                    foreach (var kv in properties) copy[kv.Key] = kv.Value;

                _queued.Enqueue(new Pending(name, copy));
                return;
            }

            Send(name, properties);
        }

        void Send(string name, IReadOnlyDictionary<string, object> properties)
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

            FirebaseAnalytics.LogEvent(name, _parameters.ToArray());
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
        /// bucketed in a report and there is no way to change that after the fact — the rows
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
