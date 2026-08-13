using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace GlimmerGrove.Analytics
{
    /// <summary>Somewhere events can be delivered: a vendor SDK, a log, a test double.</summary>
    public interface IAnalyticsSink
    {
        void Track(string eventName, IReadOnlyDictionary<string, object> properties);
    }

    /// <summary>
    /// Fans game events out to whatever sinks are attached.
    ///
    /// No vendor SDK is wired up yet and that is fine — the point of having this now
    /// is that the call sites exist from the first level. Difficulty tuning for a
    /// global audience is only possible with per-level completion and quit data, and
    /// that is the one thing that cannot be backfilled: data not collected in the
    /// first months is simply gone. Attaching a real sink later is one line here.
    ///
    /// Never throws. An analytics failure must not be able to break a game session.
    /// </summary>
    public static class Telemetry
    {
        static readonly List<IAnalyticsSink> _sinks = new List<IAnalyticsSink>();
        static readonly Dictionary<string, object> _scratch = new Dictionary<string, object>();

        public static void AddSink(IAnalyticsSink sink)
        {
            if (sink != null && !_sinks.Contains(sink)) _sinks.Add(sink);
        }

        public static void RemoveSink(IAnalyticsSink sink) => _sinks.Remove(sink);

        public static void Clear() => _sinks.Clear();

        public static bool HasSinks => _sinks.Count > 0;

        /// <summary>
        /// Sends an event. Properties are supplied as alternating key/value pairs,
        /// which keeps call sites to one readable line without allocating a dictionary
        /// at every one of them.
        /// </summary>
        public static void Track(string eventName, params object[] keyValuePairs)
        {
            if (_sinks.Count == 0) return;
            if (string.IsNullOrEmpty(eventName)) return;

            _scratch.Clear();
            for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
            {
                if (keyValuePairs[i] is string key) _scratch[key] = keyValuePairs[i + 1];
            }

            for (int i = 0; i < _sinks.Count; i++)
            {
                try
                {
                    _sinks[i].Track(eventName, _scratch);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Telemetry] sink {_sinks[i].GetType().Name} threw: {e.Message}");
                }
            }
        }
    }

    /// <summary>Prints events to the console. Handy while building, off in a release.</summary>
    public sealed class DebugAnalyticsSink : IAnalyticsSink
    {
        public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            var sb = new StringBuilder("[Analytics] ").Append(eventName);
            foreach (var kv in properties) sb.Append("  ").Append(kv.Key).Append('=').Append(kv.Value);
            Debug.Log(sb.ToString());
        }
    }
}
