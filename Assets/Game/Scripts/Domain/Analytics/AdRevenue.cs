using System;
using System.Collections.Generic;
using System.Threading;

namespace GlimmerGrove.Analytics
{
    public enum AdRevenueOwner { Unverified, LevelPlay, Automatic }

    /// <summary>A reviewed dashboard mapping, not a guess based on the network's display name.</summary>
    public readonly struct AdRevenueSource
    {
        public readonly string AdUnitId, Network, InstanceId, InstanceName;
        public readonly AdRevenueOwner Owner;

        public AdRevenueSource(string adUnitId, string network, string instanceId, AdRevenueOwner owner,
            string instanceName = null)
        {
            AdUnitId = adUnitId;
            Network = network;
            InstanceId = instanceId;
            InstanceName = instanceName;
            Owner = owner;
        }
    }

    /// <summary>Only revenue/advertising metadata. Never retains the SDK object or its raw JSON.</summary>
    public sealed class AdRevenueImpression
    {
        public readonly string AdUnitId, AuctionId, Network, InstanceId, InstanceName, UnitName, Format, Precision, Placement;
        public readonly double? Revenue;

        public AdRevenueImpression(string adUnitId, string auctionId, string network, string instanceId,
            string unitName, string format, double? revenue, string precision, string placement)
        {
            AdUnitId = adUnitId;
            AuctionId = auctionId;
            Network = network;
            InstanceId = instanceId;
            InstanceName = unitName != null && unitName.Length <= 100 ? unitName : null;
            UnitName = Clip(unitName);
            Format = Clip(format);
            Revenue = revenue;
            Precision = Clip(precision);
            Placement = Clip(placement);
        }

        static string Clip(string value) => value != null && value.Length > 100 ? value.Substring(0, 100) : value;
    }

    public enum AdRevenueIssue
    {
        Excluded, UnverifiedSource, AutomaticSource, InvalidRevenue, InvalidMetadata, Duplicate,
        Overflow, ClearedOnWithdrawal, HandedToAnalytics, UnconfirmedHandoff, CallbackFailure
    }

    /// <summary>
    /// Bounded background-callback inbox; Pump and eligibility changes run on the main thread.
    /// Firebase's existing delivery queue owns retries and native offline persistence after handoff.
    /// No disk state, receipt replay, or second revenue destination is introduced here.
    /// </summary>
    public sealed class AdRevenueTracker
    {
        public const int MaxPending = 64;
        public const int MaxSeen = 512;
        public const int MaxPerTick = 4;
        const int MaxSources = 64;

        readonly object _gate = new object();
        readonly IAnalyticsSink _sink;
        readonly AdRevenueSource[] _sources;
        readonly Action<AdRevenueIssue, long> _diagnose;
        readonly Queue<AdRevenueImpression> _pending = new Queue<AdRevenueImpression>(MaxPending);
        readonly HashSet<(string Auction, string Unit)> _seen = new HashSet<(string, string)>();
        readonly Queue<(string Auction, string Unit)> _seenOrder = new Queue<(string, string)>(MaxSeen);
        readonly long[] _counts = new long[(int)AdRevenueIssue.CallbackFailure + 1];
        readonly long[] _reported = new long[(int)AdRevenueIssue.CallbackFailure + 1];
        bool _allowed, _pumping;
        AdRevenueSource _lastUnverifiedSource;

        public AdRevenueTracker(IAnalyticsSink sink, AdRevenueSource[] sources,
            Action<AdRevenueIssue, long> diagnose = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (sources != null && sources.Length > MaxSources) throw new ArgumentOutOfRangeException(nameof(sources));
            _sources = sources == null ? Array.Empty<AdRevenueSource>() : (AdRevenueSource[])sources.Clone();
            _diagnose = diagnose;
        }

        public int PendingCount { get { lock (_gate) return _pending.Count; } }
        public int SeenCount { get { lock (_gate) return _seen.Count; } }
        public long Count(AdRevenueIssue issue) { lock (_gate) return _counts[(int)issue]; }
        /// <summary>Bounded advertising configuration only, never auction/player/device identifiers.</summary>
        public AdRevenueSource LastUnverifiedSource { get { lock (_gate) return _lastUnverifiedSource; } }

        public void SetCollectionAllowed(bool allowed)
        {
            lock (_gate)
            {
                _allowed = allowed;
                if (!allowed) _lastUnverifiedSource = default;
                if (allowed || _pending.Count == 0) return;
                Add(AdRevenueIssue.ClearedOnWithdrawal, _pending.Count);
                _pending.Clear();
                // Keep the bounded seen cache: a repeated callback cannot resurrect a cleared event.
            }
        }

        public void Capture(AdRevenueImpression impression)
        {
            lock (_gate)
            {
                if (impression == null || !Identity(impression.AuctionId) || !Identity(impression.AdUnitId))
                {
                    Add(AdRevenueIssue.InvalidMetadata);
                    return;
                }
                var key = (impression.AuctionId, impression.AdUnitId);
                if (!_seen.Add(key)) { Add(AdRevenueIssue.Duplicate); return; }
                _seenOrder.Enqueue(key);
                if (_seenOrder.Count > MaxSeen) _seen.Remove(_seenOrder.Dequeue());

                // Remember identity even when excluded; a later duplicate is not a new impression.
                if (!_allowed) { Add(AdRevenueIssue.Excluded); return; }
                if (!Identity(impression.Network) || !Identity(impression.InstanceId)
                    || string.IsNullOrWhiteSpace(impression.Format))
                { Add(AdRevenueIssue.InvalidMetadata); return; }

                AdRevenueOwner owner = Owner(impression);
                if (owner != AdRevenueOwner.LevelPlay)
                {
                    if (owner != AdRevenueOwner.Automatic)
                        _lastUnverifiedSource = new AdRevenueSource(impression.AdUnitId, impression.Network,
                            impression.InstanceId, AdRevenueOwner.Unverified, impression.UnitName);
                    Add(owner == AdRevenueOwner.Automatic ? AdRevenueIssue.AutomaticSource : AdRevenueIssue.UnverifiedSource);
                    return;
                }
                if (!impression.Revenue.HasValue || double.IsNaN(impression.Revenue.Value)
                    || double.IsInfinity(impression.Revenue.Value) || impression.Revenue.Value < 0d)
                { Add(AdRevenueIssue.InvalidRevenue); return; }
                if (_pending.Count >= MaxPending) { Add(AdRevenueIssue.Overflow); return; }
                _pending.Enqueue(impression);
            }
        }

        AdRevenueOwner Owner(AdRevenueImpression impression)
        {
            bool matched = false;
            AdRevenueOwner owner = AdRevenueOwner.Unverified;
            foreach (var source in _sources)
            {
                if (!string.Equals(source.AdUnitId, impression.AdUnitId, StringComparison.Ordinal)
                    || !string.Equals(source.Network, impression.Network, StringComparison.Ordinal)) continue;
                // Prefer a reviewed ID. A reviewed exact instance name may identify a dashboard
                // instance when no ID was supplied; empty/wildcard names never authorize a source.
                bool instanceMatches = Identity(source.InstanceId)
                    ? string.Equals(source.InstanceId, impression.InstanceId, StringComparison.Ordinal)
                    : Identity(source.InstanceName) && string.Equals(source.InstanceName, impression.InstanceName, StringComparison.Ordinal);
                if (!instanceMatches) continue;
                // Duplicate/conflicting configuration fails closed, including automatic/manual overlap.
                if (matched) return AdRevenueOwner.Unverified;
                matched = true;
                owner = source.Owner;
            }
            return owner;
        }

        public void CallbackFailed() { lock (_gate) Add(AdRevenueIssue.CallbackFailure); }

        public void Pump()
        {
            if (_pumping) return;
            _pumping = true;
            try
            {
                for (int i = 0; i < MaxPerTick; i++)
                {
                    AdRevenueImpression impression;
                    lock (_gate)
                    {
                        if (!_allowed || _pending.Count == 0) break;
                        impression = _pending.Dequeue();
                    }
                    try
                    {
                        var values = new Dictionary<string, object>
                        {
                            ["ad_platform"] = "LevelPlay",
                            ["ad_source"] = impression.Network,
                            ["ad_unit_name"] = string.IsNullOrWhiteSpace(impression.UnitName) ? impression.AdUnitId : impression.UnitName,
                            ["ad_format"] = impression.Format,
                            ["value"] = impression.Revenue.Value,
                            ["currency"] = "USD"
                        };
                        if (!string.IsNullOrEmpty(impression.Precision)) values["revenue_precision"] = impression.Precision;
                        if (!string.IsNullOrEmpty(impression.Placement)) values["ad_placement"] = impression.Placement;
                        _sink.Track("ad_impression", values);
                        lock (_gate) Add(AdRevenueIssue.HandedToAnalytics);
                    }
                    catch (Exception)
                    {
                        // Track has no acknowledgement contract. Do not risk replaying accepted revenue.
                        lock (_gate) Add(AdRevenueIssue.UnconfirmedHandoff);
                    }
                }
                Report();
            }
            finally { _pumping = false; }
        }

        void Report()
        {
            if (_diagnose == null) return;
            for (int i = 0; i < _counts.Length; i++)
            {
                long count;
                lock (_gate)
                {
                    count = _counts[i];
                    long before = _reported[i];
                    if (count == 0 || (before > 0 && count / 2 < before)) continue;
                    _reported[i] = count;
                }
                try { _diagnose((AdRevenueIssue)i, count); }
                catch (Exception) { /* Diagnostics cannot interrupt a player. */ }
            }
        }

        void Add(AdRevenueIssue issue, long amount = 1)
        {
            int index = (int)issue;
            _counts[index] = _counts[index] > long.MaxValue - amount ? long.MaxValue : _counts[index] + amount;
        }

        static bool Identity(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 100;
    }

    /// <summary>SDK callback entry point. Only Firebase installs a destination; no attribution fan-out.</summary>
    public static class AdRevenue
    {
        static AdRevenueTracker _tracker;
        public static void Install(AdRevenueTracker tracker) => Volatile.Write(ref _tracker, tracker);
        public static void Capture(AdRevenueImpression impression) => Volatile.Read(ref _tracker)?.Capture(impression);
        public static void CallbackFailed() => Volatile.Read(ref _tracker)?.CallbackFailed();
    }
}
