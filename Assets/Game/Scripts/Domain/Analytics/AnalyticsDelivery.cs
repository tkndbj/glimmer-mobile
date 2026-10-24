using System;
using System.Collections.Generic;

namespace GlimmerGrove.Analytics
{
    /// <summary>The SDK has no server acknowledgement. An uncertain handoff must not be replayed.</summary>
    public enum AnalyticsSendResult { Accepted, Retry, Unconfirmed }

    /// <summary>Fixed diagnostic categories; never event names, parameters, identifiers or exception messages.</summary>
    public enum AnalyticsDeliveryIssue
    {
        Excluded, Overflow, InvalidPayload, CollectionFailure, SendFailure,
        UnconfirmedHandoff, RetryLimit, ClearedOnWithdrawal, CollectionEnabled, CollectionDisabled, DriverFailure
    }

    /// <summary>
    /// Main-thread delivery policy, independent of Firebase and of the source of collection eligibility.
    /// Only events eligible at capture may wait for the SDK. Withdrawal forgets that queue immediately.
    /// The SDK owns persistence after handoff; this bounded startup/retry queue is process-local.
    /// </summary>
    public sealed class AnalyticsDelivery : IAnalyticsSink
    {
        public const int MaxPending = 64;
        public const int MaxPerTick = 4;
        public const int MaxSendAttempts = 5;
        public const int MaxParameters = 25;
        const int MaxString = 100;

        sealed class Pending
        {
            public readonly string Name;
            public readonly Dictionary<string, object> Properties;
            public int Attempts;

            public Pending(string name, Dictionary<string, object> properties)
            {
                Name = name;
                Properties = properties;
            }
        }

        readonly Action<bool> _setCollection;
        readonly Func<string, IReadOnlyDictionary<string, object>, AnalyticsSendResult> _send;
        readonly Func<double> _now;
        readonly Action<AnalyticsDeliveryIssue, long> _diagnose;
        readonly Queue<Pending> _pending = new Queue<Pending>(MaxPending);
        readonly long[] _counts = new long[(int)AnalyticsDeliveryIssue.DriverFailure + 1];
        bool _ready, _allowed, _pumping;
        bool? _appliedCollection;
        int _collectionFailures;
        double _collectionRetryAt, _sendRetryAt;

        public AnalyticsDelivery(Action<bool> setCollection,
            Func<string, IReadOnlyDictionary<string, object>, AnalyticsSendResult> send,
            Func<double> now, Action<AnalyticsDeliveryIssue, long> diagnose = null)
        {
            _setCollection = setCollection ?? throw new ArgumentNullException(nameof(setCollection));
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _diagnose = diagnose;
        }

        public int PendingCount => _pending.Count;
        public bool? AppliedCollection => _appliedCollection;
        public long Count(AnalyticsDeliveryIssue issue) => _counts[(int)issue];

        public void MarkReady()
        {
            _ready = true;
            Tick();
        }

        /// <summary>
        /// Eligibility is supplied by the product's consent integration, never inferred by this queue.
        /// A later grant cannot make earlier excluded events eligible.
        /// </summary>
        public void SetCollectionAllowed(bool allowed)
        {
            if (_allowed == allowed) return;
            _allowed = allowed;
            _collectionFailures = 0;
            _collectionRetryAt = _sendRetryAt = 0d;
            if (!allowed && _pending.Count > 0)
            {
                int cleared = _pending.Count;
                _pending.Clear();
                Report(AnalyticsDeliveryIssue.ClearedOnWithdrawal, cleared);
            }
            Tick();
        }

        public void Track(string eventName, IReadOnlyDictionary<string, object> properties)
        {
            if (!_allowed)
            {
                Report(AnalyticsDeliveryIssue.Excluded);
                return;
            }
            if (string.IsNullOrEmpty(eventName))
            {
                Report(AnalyticsDeliveryIssue.InvalidPayload);
                return;
            }
            if (_pending.Count >= MaxPending)
            {
                Report(AnalyticsDeliveryIssue.Overflow);
                return;
            }

            try
            {
                _pending.Enqueue(new Pending(Clip(eventName, 40), Snapshot(properties)));
            }
            catch (Exception)
            {
                Report(AnalyticsDeliveryIssue.InvalidPayload);
                return;
            }
        }

        /// <summary>At most four sends per pump; retries back off from one second to one minute.</summary>
        public void Tick()
        {
            if (!_ready || _pumping) return;
            if (_appliedCollection == _allowed && (!_allowed || _pending.Count == 0)) return;
            _pumping = true;
            try
            {
                double now;
                try { now = _now(); }
                catch (Exception) { Report(AnalyticsDeliveryIssue.DriverFailure); return; }
                if (double.IsNaN(now) || double.IsInfinity(now))
                {
                    Report(AnalyticsDeliveryIssue.DriverFailure);
                    return;
                }
                if (_appliedCollection != _allowed)
                {
                    if (now < _collectionRetryAt) return;
                    try
                    {
                        bool requested = _allowed;
                        _setCollection(requested);
                        _appliedCollection = requested;
                        _collectionFailures = 0;
                        Report(requested ? AnalyticsDeliveryIssue.CollectionEnabled : AnalyticsDeliveryIssue.CollectionDisabled);
                    }
                    catch (Exception)
                    {
                        // A throwing setter may have changed native state. Never trust the old cache.
                        _appliedCollection = null;
                        _collectionFailures = Math.Min(_collectionFailures + 1, 7);
                        _collectionRetryAt = now + RetryDelay(_collectionFailures);
                        Report(AnalyticsDeliveryIssue.CollectionFailure);
                        return;
                    }
                }

                if (!_allowed || _appliedCollection != true || now < _sendRetryAt) return;
                for (int i = 0; i < MaxPerTick && _allowed && _pending.Count > 0; i++)
                {
                    var pending = _pending.Peek();
                    AnalyticsSendResult result;
                    try { result = _send(pending.Name, pending.Properties); }
                    catch (Exception) { result = AnalyticsSendResult.Unconfirmed; }

                    // A callback can withdraw eligibility and clear the queue during a send.
                    if (_pending.Count == 0 || !ReferenceEquals(_pending.Peek(), pending)) return;

                    if (result == AnalyticsSendResult.Retry)
                    {
                        Report(AnalyticsDeliveryIssue.SendFailure);
                        if (++pending.Attempts < MaxSendAttempts)
                        {
                            _sendRetryAt = now + RetryDelay(pending.Attempts);
                            return;
                        }
                        _pending.Dequeue();
                        Report(AnalyticsDeliveryIssue.RetryLimit);
                    }
                    else
                    {
                        _pending.Dequeue();
                        if (result != AnalyticsSendResult.Accepted)
                            Report(AnalyticsDeliveryIssue.UnconfirmedHandoff);
                    }
                    _sendRetryAt = 0d;
                }
            }
            finally { _pumping = false; }
        }

        static double RetryDelay(int attempts) => Math.Min(60d, 1 << Math.Min(attempts - 1, 6));

        static Dictionary<string, object> Snapshot(IReadOnlyDictionary<string, object> properties)
        {
            var copy = new Dictionary<string, object>(Math.Min(properties?.Count ?? 0, MaxParameters));
            if (properties == null) return copy;
            foreach (var pair in properties)
            {
                if (copy.Count >= MaxParameters) break;
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null) continue;
                object value = pair.Value;
                // Retain scalars only, never a mutable game object or an unbounded string.
                if (!(value is bool || value is byte || value is short || value is int || value is long
                    || value is float || value is double || value is decimal || value is Enum))
                    value = Clip(value.ToString(), MaxString);
                copy[Clip(pair.Key, 40)] = value;
            }
            return copy;
        }

        static string Clip(string value, int max) => value != null && value.Length > max ? value.Substring(0, max) : value;

        void Report(AnalyticsDeliveryIssue issue, long amount = 1)
        {
            int index = (int)issue;
            long before = _counts[index];
            long count = _counts[index] = before > long.MaxValue - amount ? long.MaxValue : before + amount;
            // First occurrence and powers of two: bounded logging even under a permanent failure.
            if (before != 0 && (count & (count - 1)) != 0) return;
            try { _diagnose?.Invoke(issue, count); }
            catch (Exception) { /* Diagnostics must never interrupt a player or the queue. */ }
        }
    }
}
