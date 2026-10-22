using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GlimmerGrove.Content;

namespace GlimmerGrove.Offers
{
    /// <summary>
    /// One test a segment makes of a player: a <see cref="OfferMeasure"/> between two inclusive bounds.
    /// An absent bound is open, so <c>{"measure":"purchases","max":0}</c> reads "never paid".
    /// </summary>
    public sealed class OfferRule
    {
        public OfferRule(OfferMeasure measure, long min, long max)
        {
            Measure = measure;
            Min = min;
            Max = max;
        }

        public OfferMeasure Measure { get; }
        public long Min { get; }
        public long Max { get; }

        public bool Holds(OfferProfile profile)
        {
            long value = profile.Get(Measure);
            return value >= Min && value <= Max;
        }
    }

    /// <summary>A named group of players: everyone every rule holds for. No rules is everyone.</summary>
    public sealed class OfferSegment
    {
        public OfferSegment(string id, IReadOnlyList<OfferRule> rules)
        {
            Id = id;
            Rules = rules ?? Array.Empty<OfferRule>();
        }

        public string Id { get; }
        public IReadOnlyList<OfferRule> Rules { get; }

        public bool Matches(OfferProfile profile)
        {
            for (int i = 0; i < Rules.Count; i++)
                if (!Rules[i].Holds(profile)) return false;
            return true;
        }
    }

    /// <summary>
    /// One offer the engine may deal (invariant 64): which store products it sells, which moments it
    /// answers, who it is for, how often it may come back and how long it stands once dealt.
    /// </summary>
    public sealed class OfferTemplate
    {
        public OfferTemplate(string id, OfferLook look, IReadOnlyList<string> products, OfferPick pick,
                             IReadOnlyList<OfferTrigger> moments, IReadOnlyList<string> segments,
                             IReadOnlyList<string> exclude, int cooldownHours, int shows, int buys,
                             int priority, int weight, int lastsMinutes)
        {
            Id = id;
            Look = look;
            Products = products;
            Pick = pick;
            Moments = moments;
            Segments = segments;
            Exclude = exclude ?? Array.Empty<string>();
            CooldownHours = cooldownHours;
            Shows = shows;
            Buys = buys;
            Priority = priority;
            Weight = weight;
            LastsMinutes = lastsMinutes;
        }

        /// <summary>Permanent once published: the device's history and analytics key on it.</summary>
        public string Id { get; }

        public OfferLook Look { get; }

        /// <summary>Store product ids on the offers shelf, cheapest first by convention.</summary>
        public IReadOnlyList<string> Products { get; }

        public OfferPick Pick { get; }
        public IReadOnlyList<OfferTrigger> Moments { get; }

        /// <summary>Segment ids; the offer is for a player in any of them.</summary>
        public IReadOnlyList<string> Segments { get; }

        /// <summary>Segment ids; a player in any of them is never dealt this offer.</summary>
        public IReadOnlyList<string> Exclude { get; }

        /// <summary>Hours after it was last dealt before it may be dealt again.</summary>
        public int CooldownHours { get; }

        /// <summary>How many times it may ever be dealt to an account on a device; nought is no limit.</summary>
        public int Shows { get; }

        /// <summary>How many times it may ever be bought; nought is no limit.</summary>
        public int Buys { get; }

        /// <summary>Higher wins. Equal priorities are drawn by <see cref="Weight"/>.</summary>
        public int Priority { get; }

        public int Weight { get; }

        /// <summary>How long it stands, in the popup and the shop's row, once dealt.</summary>
        public int LastsMinutes { get; }

        public bool Answers(OfferTrigger moment)
        {
            for (int i = 0; i < Moments.Count; i++) if (Moments[i] == moment) return true;
            return false;
        }
    }

    /// <summary>
    /// <b>How the offer engine behaves</b> (invariant 64): the pacing every offer shares, the
    /// segments players are sorted into, and the offers each segment may be dealt.
    ///
    /// <para>
    /// <b>Content, published without a build.</b> The live copy is <c>config/offers</c>, written from
    /// <c>firebase/seed/offers.json</c> by <c>seed-offers.mjs</c>, which refuses anything this reader
    /// refuses in its strict form; a device reads it beside play (<see cref="OfferConfig"/>) and keeps
    /// what it had when a document will not read. What a device holds before it has read one is
    /// <see cref="Shipped"/> - the same text, built in, and held equal to revision 1 of the seed file
    /// by <c>OfferTuningTests</c>.
    /// </para>
    /// <para>
    /// <b>The stop is <c>"enabled": false</c></b>, published like any retune: every device stops
    /// dealing at its next read and the shop's row empties as the standing offer runs out.
    /// </para>
    /// </summary>
    public sealed class OfferTuning
    {
        /// <summary>The document shape this build reads. A later shape is published beside it, never over it.</summary>
        public const int Schema = 1;

        /// <summary>The longest document read, so a hostile one cannot take the memory.</summary>
        public const int MostLength = 64 * 1024;

        public const int MostSegments = 32, MostRules = 8, MostOffers = 32, MostProducts = 8;

        OfferTuning(int revision, bool enabled, int holdoutPercent, int minClearedLevels, int perDay,
                    int perSession, int gapMinutes, int quietAfterPurchaseHours, int[] backoffHours,
                    int nearMissPercent, List<OfferSegment> segments, List<OfferTemplate> offers)
        {
            Revision = revision;
            Enabled = enabled;
            HoldoutPercent = holdoutPercent;
            MinClearedLevels = minClearedLevels;
            PerDay = perDay;
            PerSession = perSession;
            GapMinutes = gapMinutes;
            QuietAfterPurchaseHours = quietAfterPurchaseHours;
            BackoffHours = backoffHours;
            NearMissPercent = nearMissPercent;
            Segments = segments;
            Offers = offers;

            _segments = new Dictionary<string, OfferSegment>(StringComparer.Ordinal);
            foreach (var segment in segments) _segments[segment.Id] = segment;
        }

        readonly Dictionary<string, OfferSegment> _segments;

        public int Revision { get; }

        /// <summary>The engine's switch. Off deals nothing; an offer already standing runs out.</summary>
        public bool Enabled { get; }

        /// <summary>
        /// The share of accounts that is never dealt a personal offer, so what the engine earns can be
        /// measured against players it never touched (<see cref="OfferRules.InHoldout"/>).
        /// </summary>
        public int HoldoutPercent { get; }

        /// <summary>Levels a player must have cleared before anything is offered: the first sessions are the game's.</summary>
        public int MinClearedLevels { get; }

        /// <summary>Offers dealt per UTC day, and per session.</summary>
        public int PerDay { get; }
        public int PerSession { get; }

        /// <summary>Minutes after any money popup (a deal or an offer) before an offer may follow it.</summary>
        public int GapMinutes { get; }

        /// <summary>Hours after any purchase before anything more is offered.</summary>
        public int QuietAfterPurchaseHours { get; }

        /// <summary>
        /// The wait after the n-th close in a row, in hours: entry <c>n - 1</c>, and the last entry for
        /// any longer run. Each "not now" makes the next offer wait longer.
        /// </summary>
        public IReadOnlyList<int> BackoffHours { get; }

        /// <summary>How far through a lost run, as a whole percentage, before the loss is a near miss.</summary>
        public int NearMissPercent { get; }

        public IReadOnlyList<OfferSegment> Segments { get; }
        public IReadOnlyList<OfferTemplate> Offers { get; }

        public OfferSegment Segment(string id)
            => id != null && _segments.TryGetValue(id, out var segment) ? segment : null;

        /// <summary>The offer with this id, or null.</summary>
        public OfferTemplate Find(string id)
        {
            if (id == null) return null;
            foreach (var offer in Offers) if (string.Equals(offer.Id, id, StringComparison.Ordinal)) return offer;
            return null;
        }

        /// <summary>The backoff after <paramref name="dismissals"/> closes in a row, in seconds; nought for none.</summary>
        public long BackoffSeconds(int dismissals)
        {
            if (dismissals <= 0 || BackoffHours.Count == 0) return 0L;
            int at = Math.Min(dismissals, BackoffHours.Count) - 1;
            return BackoffHours[at] * 3600L;
        }

        // ------------------------------------------------------------------ the build's own
        static OfferTuning _shipped;

        /// <summary>
        /// The tuning a device holds before it has read a published one: <see cref="ShippedJson"/>,
        /// read by the strict reader. Never null - <c>OfferTuningTests</c> holds that the text reads.
        /// </summary>
        public static OfferTuning Shipped
        {
            get
            {
                if (_shipped != null) return _shipped;
                if (!TryRead(ShippedJson, true, out var tuning, out string fault))
                    throw new InvalidOperationException("the built-in offer tuning does not read: " + fault);
                return _shipped = tuning;
            }
        }

        /// <summary>
        /// The current revision of <c>firebase/seed/offers.json</c>, built in, so a device that has
        /// not read the published one deals by the same rules. Kept equal to the seed file at this
        /// revision by <c>OfferTuningTests.TheSeedFileAtTheBuiltInRevisionIsTheBuiltInTuning</c>;
        /// change both together. Revision 2 opens offers after the first chapter (ten levels).
        /// </summary>
        public const string ShippedJson = @"{
  ""schema"": 1,
  ""revision"": 2,
  ""enabled"": true,
  ""holdoutPercent"": 10,
  ""minClearedLevels"": 10,
  ""perDay"": 2,
  ""perSession"": 1,
  ""gapMinutes"": 90,
  ""quietAfterPurchaseHours"": 48,
  ""backoffHours"": [6, 24, 72],
  ""nearMissPercent"": 75,
  ""segments"": [
    { ""id"": ""everyone"", ""rules"": [] },
    { ""id"": ""new_player"", ""rules"": [ { ""measure"": ""days_playing"", ""max"": 2 }, { ""measure"": ""purchases"", ""max"": 0 } ] },
    { ""id"": ""never_paid"", ""rules"": [ { ""measure"": ""purchases"", ""max"": 0 } ] },
    { ""id"": ""engaged_free"", ""rules"": [ { ""measure"": ""purchases"", ""max"": 0 }, { ""measure"": ""days_playing"", ""min"": 3 }, { ""measure"": ""active_days_7"", ""min"": 4 } ] },
    { ""id"": ""struggling"", ""rules"": [ { ""measure"": ""runs"", ""min"": 5 }, { ""measure"": ""loss_percent"", ""min"": 50 } ] },
    { ""id"": ""stuck"", ""rules"": [ { ""measure"": ""loss_streak"", ""min"": 3 } ] },
    { ""id"": ""cruising"", ""rules"": [ { ""measure"": ""runs"", ""min"": 5 }, { ""measure"": ""loss_percent"", ""max"": 25 } ] },
    { ""id"": ""ad_watcher"", ""rules"": [ { ""measure"": ""purchases"", ""max"": 0 }, { ""measure"": ""ad_views_7"", ""min"": 10 } ] },
    { ""id"": ""payer"", ""rules"": [ { ""measure"": ""purchases"", ""min"": 1 } ] },
    { ""id"": ""payer_entry"", ""rules"": [ { ""measure"": ""purchases"", ""min"": 1 }, { ""measure"": ""top_cents"", ""max"": 399 } ] },
    { ""id"": ""payer_mid"", ""rules"": [ { ""measure"": ""top_cents"", ""min"": 400, ""max"": 799 } ] },
    { ""id"": ""payer_high"", ""rules"": [ { ""measure"": ""top_cents"", ""min"": 800 } ] },
    { ""id"": ""payer_big"", ""rules"": [ { ""measure"": ""top_cents"", ""min"": 800 }, { ""measure"": ""spend_cents"", ""min"": 5000 } ] },
    { ""id"": ""payer_whale"", ""rules"": [ { ""measure"": ""top_cents"", ""min"": 3999 } ] },
    { ""id"": ""lapsed_payer"", ""rules"": [ { ""measure"": ""purchases"", ""min"": 1 }, { ""measure"": ""days_since_purchase"", ""min"": 21 } ] },
    { ""id"": ""returning"", ""rules"": [ { ""measure"": ""days_away"", ""min"": 3 } ] },
    { ""id"": ""tired"", ""rules"": [ { ""measure"": ""dismissals"", ""min"": 3 } ] }
  ],
  ""offers"": [
    { ""id"": ""starter"", ""look"": ""starter"", ""products"": [""gg_offer_1""], ""pick"": ""first"",
      ""moments"": [""session"", ""win"", ""hub""], ""segments"": [""new_player""], ""exclude"": [],
      ""cooldownHours"": 0, ""shows"": 1, ""buys"": 1, ""priority"": 100, ""weight"": 100, ""lastsMinutes"": 1440 },
    { ""id"": ""first_buy"", ""look"": ""value"", ""products"": [""gg_offer_1""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""engaged_free""], ""exclude"": [""new_player""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 1, ""priority"": 60, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""near_miss"", ""look"": ""rescue"", ""products"": [""gg_offer_2""], ""pick"": ""first"",
      ""moments"": [""near_miss""], ""segments"": [""struggling"", ""stuck""], ""exclude"": [],
      ""cooldownHours"": 48, ""shows"": 0, ""buys"": 0, ""priority"": 80, ""weight"": 100, ""lastsMinutes"": 360 },
    { ""id"": ""stuck_help"", ""look"": ""rescue"", ""products"": [""gg_offer_2""], ""pick"": ""first"",
      ""moments"": [""loss""], ""segments"": [""stuck""], ""exclude"": [],
      ""cooldownHours"": 72, ""shows"": 0, ""buys"": 0, ""priority"": 70, ""weight"": 100, ""lastsMinutes"": 360 },
    { ""id"": ""coin_gap_free"", ""look"": ""value"", ""products"": [""gg_offer_2"", ""gg_offer_3"", ""gg_offer_4""], ""pick"": ""gap"",
      ""moments"": [""shortfall""], ""segments"": [""never_paid""], ""exclude"": [],
      ""cooldownHours"": 24, ""shows"": 0, ""buys"": 0, ""priority"": 90, ""weight"": 100, ""lastsMinutes"": 240 },
    { ""id"": ""coin_gap_payer"", ""look"": ""value"", ""products"": [""gg_offer_2"", ""gg_offer_3"", ""gg_offer_4"", ""gg_offer_5"", ""gg_offer_6"", ""gg_offer_7""], ""pick"": ""gap"",
      ""moments"": [""shortfall""], ""segments"": [""payer""], ""exclude"": [],
      ""cooldownHours"": 24, ""shows"": 0, ""buys"": 0, ""priority"": 90, ""weight"": 100, ""lastsMinutes"": 240 },
    { ""id"": ""comeback"", ""look"": ""comeback"", ""products"": [""gg_offer_2""], ""pick"": ""first"",
      ""moments"": [""session""], ""segments"": [""returning""], ""exclude"": [""payer""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 85, ""weight"": 100, ""lastsMinutes"": 1440 },
    { ""id"": ""comeback_payer"", ""look"": ""comeback"", ""products"": [""gg_offer_3""], ""pick"": ""first"",
      ""moments"": [""session""], ""segments"": [""lapsed_payer""], ""exclude"": [],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 86, ""weight"": 100, ""lastsMinutes"": 1440 },
    { ""id"": ""step_down"", ""look"": ""value"", ""products"": [""gg_offer_2""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""tired""], ""exclude"": [""never_paid""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 58, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""step_entry"", ""look"": ""boost"", ""products"": [""gg_offer_3""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""payer_entry""], ""exclude"": [""tired""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 50, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""step_mid"", ""look"": ""boost"", ""products"": [""gg_offer_4""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""payer_mid""], ""exclude"": [""tired""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 51, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""step_high"", ""look"": ""boost"", ""products"": [""gg_offer_5""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""payer_high""], ""exclude"": [""tired""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 52, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""step_big"", ""look"": ""special"", ""products"": [""gg_offer_6""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""payer_big""], ""exclude"": [""tired""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 53, ""weight"": 100, ""lastsMinutes"": 720 },
    { ""id"": ""step_whale"", ""look"": ""special"", ""products"": [""gg_offer_7""], ""pick"": ""first"",
      ""moments"": [""win"", ""chapter"", ""session""], ""segments"": [""payer_whale""], ""exclude"": [""tired""],
      ""cooldownHours"": 168, ""shows"": 0, ""buys"": 0, ""priority"": 54, ""weight"": 100, ""lastsMinutes"": 720 }
  ]
}";

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// Reads a document, whole or not at all. Never throws. <paramref name="strict"/> also refuses
        /// any key this build does not know (the seeder's reading); a device reads leniently, so a
        /// later build may add a figure beside these without older phones refusing the document.
        /// </summary>
        public static bool TryRead(string json, bool strict, out OfferTuning tuning, out string fault)
        {
            tuning = null;
            try
            {
                tuning = Read(json, strict, out fault);
                return tuning != null;
            }
            catch (Exception e)
            {
                fault = "the document could not be read: " + e.Message;
                tuning = null;
                return false;
            }
        }

        static OfferTuning Read(string json, bool strict, out string fault)
        {
            if (string.IsNullOrEmpty(json)) { fault = "the document is empty"; return null; }
            if (json.Length > MostLength) { fault = $"the document is {json.Length} characters, over {MostLength}"; return null; }
            if (!StrictJson.TryParse(json, out object parsed, out fault)) return null;
            if (!(parsed is Dictionary<string, object> root)) { fault = "the document is not an object"; return null; }

            var doc = new Doc(strict);

            if (!doc.Keys(root, "", "schema", "revision", "enabled", "holdoutPercent", "minClearedLevels", "perDay",
                          "perSession", "gapMinutes", "quietAfterPurchaseHours", "backoffHours", "nearMissPercent",
                          "segments", "offers"))
            { fault = doc.Fault; return null; }

            if (!doc.Int(root, "schema", 1, int.MaxValue, out int schema)) { fault = doc.Fault; return null; }
            if (schema != Schema) { fault = $"the document is schema {schema}; this build reads schema {Schema}"; return null; }

            if (!doc.Int(root, "revision", 1, int.MaxValue, out int revision) ||
                !doc.Bool(root, "enabled", out bool enabled) ||
                !doc.Int(root, "holdoutPercent", 0, 50, out int holdout) ||
                !doc.Int(root, "minClearedLevels", 0, 1000, out int minCleared) ||
                !doc.Int(root, "perDay", 1, 10, out int perDay) ||
                !doc.Int(root, "perSession", 1, 5, out int perSession) ||
                !doc.Int(root, "gapMinutes", 0, 10080, out int gap) ||
                !doc.Int(root, "quietAfterPurchaseHours", 0, 720, out int quiet) ||
                !doc.Int(root, "nearMissPercent", 1, 100, out int nearMiss))
            { fault = doc.Fault; return null; }

            if (perSession > perDay) { fault = $"perSession {perSession} is over perDay {perDay}"; return null; }

            if (!doc.Ints(root, "backoffHours", 1, 8, 0, 720, out int[] backoff)) { fault = doc.Fault; return null; }
            for (int i = 1; i < backoff.Length; i++)
                if (backoff[i] < backoff[i - 1]) { fault = $"backoffHours[{i}] is shorter than the wait before it; a longer run of closes never waits less"; return null; }

            // ------------------------------------------------------------- segments
            if (!doc.List(root, "segments", 1, MostSegments, out var segmentRows)) { fault = doc.Fault; return null; }

            var segments = new List<OfferSegment>(segmentRows.Count);
            var segmentIds = new HashSet<string>(StringComparer.Ordinal);

            for (int s = 0; s < segmentRows.Count; s++)
            {
                string at = $"segments[{s}].";
                if (!(segmentRows[s] is Dictionary<string, object> row)) { fault = $"segments[{s}] is not an object"; return null; }
                if (!doc.Keys(row, at, "id", "rules")) { fault = doc.Fault; return null; }
                if (!doc.Id(row, "id", at, out string id)) { fault = doc.Fault; return null; }
                if (!segmentIds.Add(id)) { fault = $"{at}id '{id}' is listed twice"; return null; }
                if (!doc.List(row, "rules", 0, MostRules, out var ruleRows, at)) { fault = doc.Fault; return null; }

                var rules = new List<OfferRule>(ruleRows.Count);
                for (int r = 0; r < ruleRows.Count; r++)
                {
                    string ruleAt = $"{at}rules[{r}].";
                    if (!(ruleRows[r] is Dictionary<string, object> ruleRow)) { fault = $"{at}rules[{r}] is not an object"; return null; }
                    if (!doc.Keys(ruleRow, ruleAt, "measure", "min", "max")) { fault = doc.Fault; return null; }
                    if (!doc.Name(ruleRow, "measure", ruleAt, out string measureId)) { fault = doc.Fault; return null; }

                    var measure = OfferMeasures.Parse(measureId);
                    if (measure == OfferMeasure.None) { fault = $"{ruleAt}measure '{measureId}' is not a measure this build knows"; return null; }

                    bool hasMin = ruleRow.ContainsKey("min"), hasMax = ruleRow.ContainsKey("max");
                    if (!hasMin && !hasMax) { fault = $"{ruleAt} has neither min nor max, so it asks nothing"; return null; }

                    long min = long.MinValue, max = long.MaxValue;
                    if (hasMin && !doc.Long(ruleRow, "min", ruleAt, out min)) { fault = doc.Fault; return null; }
                    if (hasMax && !doc.Long(ruleRow, "max", ruleAt, out max)) { fault = doc.Fault; return null; }
                    if (min > max) { fault = $"{ruleAt}min {min} is over max {max}, so nobody is in it"; return null; }

                    rules.Add(new OfferRule(measure, min, max));
                }

                segments.Add(new OfferSegment(id, rules));
            }

            // ------------------------------------------------------------- offers
            if (!doc.List(root, "offers", 0, MostOffers, out var offerRows)) { fault = doc.Fault; return null; }

            var offers = new List<OfferTemplate>(offerRows.Count);
            var offerIds = new HashSet<string>(StringComparer.Ordinal);

            for (int o = 0; o < offerRows.Count; o++)
            {
                string at = $"offers[{o}].";
                if (!(offerRows[o] is Dictionary<string, object> row)) { fault = $"offers[{o}] is not an object"; return null; }
                if (!doc.Keys(row, at, "id", "look", "products", "pick", "moments", "segments", "exclude", "cooldownHours",
                              "shows", "buys", "priority", "weight", "lastsMinutes"))
                { fault = doc.Fault; return null; }

                if (!doc.Id(row, "id", at, out string id)) { fault = doc.Fault; return null; }
                if (!offerIds.Add(id)) { fault = $"{at}id '{id}' is listed twice"; return null; }

                if (!doc.Name(row, "look", at, out string lookId)) { fault = doc.Fault; return null; }
                var look = OfferLooks.Parse(lookId);
                if (look == OfferLook.None) { fault = $"{at}look '{lookId}' is not a look this build carries words for"; return null; }

                if (!doc.Names(row, "products", 1, MostProducts, at, out var products)) { fault = doc.Fault; return null; }
                foreach (string product in products)
                    if (!IsId(product, 64)) { fault = $"{at}products names '{product}', which is not a store product id"; return null; }

                if (!doc.Name(row, "pick", at, out string pickId)) { fault = doc.Fault; return null; }
                OfferPick pick;
                if (pickId == "first") pick = OfferPick.First;
                else if (pickId == "gap") pick = OfferPick.Gap;
                else { fault = $"{at}pick '{pickId}' is neither 'first' nor 'gap'"; return null; }

                if (!doc.Names(row, "moments", 1, OfferTriggers.All.Length, at, out var momentIds)) { fault = doc.Fault; return null; }
                var moments = new List<OfferTrigger>(momentIds.Count);
                foreach (string momentId in momentIds)
                {
                    var moment = OfferTriggers.Parse(momentId);
                    if (moment == OfferTrigger.None) { fault = $"{at}moments names '{momentId}', which is not a moment this build knows"; return null; }
                    if (moments.Contains(moment)) { fault = $"{at}moments names '{momentId}' twice"; return null; }
                    moments.Add(moment);
                }

                if (pick == OfferPick.Gap && (moments.Count != 1 || moments[0] != OfferTrigger.Shortfall))
                { fault = $"{at}pick 'gap' sizes an offer to a coin shortfall, so its only moment must be 'shortfall'"; return null; }

                if (!doc.Names(row, "segments", 1, MostSegments, at, out var who)) { fault = doc.Fault; return null; }
                if (!doc.Names(row, "exclude", 0, MostSegments, at, out var exclude)) { fault = doc.Fault; return null; }
                foreach (string segment in who)
                    if (!segmentIds.Contains(segment)) { fault = $"{at}segments names '{segment}', which no segment is"; return null; }
                foreach (string segment in exclude)
                    if (!segmentIds.Contains(segment)) { fault = $"{at}exclude names '{segment}', which no segment is"; return null; }

                if (!doc.Int(row, "cooldownHours", 0, 8760, out int cooldown, at) ||
                    !doc.Int(row, "shows", 0, 1000, out int shows, at) ||
                    !doc.Int(row, "buys", 0, 1000, out int buys, at) ||
                    !doc.Int(row, "priority", 0, 1000, out int priority, at) ||
                    !doc.Int(row, "weight", 1, 1000, out int weight, at) ||
                    !doc.Int(row, "lastsMinutes", 5, 10080, out int lasts, at))
                { fault = doc.Fault; return null; }

                offers.Add(new OfferTemplate(id, look, products, pick, moments, who, exclude, cooldown, shows, buys,
                                             priority, weight, lasts));
            }

            fault = null;
            return new OfferTuning(revision, enabled, holdout, minCleared, perDay, perSession, gap, quiet, backoff,
                                   nearMiss, segments, offers);
        }

        /// <summary>The id rule every permanent name here follows: lower case letters, digits and underscores.</summary>
        internal static bool IsId(string id, int most)
        {
            if (string.IsNullOrEmpty(id) || id.Length > most) return false;
            foreach (char c in id)
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            return true;
        }

        /// <summary>
        /// Every figure in one canonical line, so two tunings can be compared for meaning rather than
        /// for spacing (<c>OfferTuningTests</c>).
        /// </summary>
        public string Describe()
        {
            var o = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            o.Append("r").Append(Revision.ToString(inv)).Append(Enabled ? " on" : " off")
             .Append(" h").Append(HoldoutPercent.ToString(inv)).Append(" c").Append(MinClearedLevels.ToString(inv))
             .Append(" d").Append(PerDay.ToString(inv)).Append(" s").Append(PerSession.ToString(inv))
             .Append(" g").Append(GapMinutes.ToString(inv)).Append(" q").Append(QuietAfterPurchaseHours.ToString(inv))
             .Append(" n").Append(NearMissPercent.ToString(inv)).Append(" b[").Append(string.Join(",", BackoffHours)).Append(']');

            foreach (var segment in Segments)
            {
                o.Append(" |").Append(segment.Id).Append(':');
                foreach (var rule in segment.Rules)
                    o.Append(OfferMeasures.Id(rule.Measure)).Append('[').Append(rule.Min.ToString(inv)).Append(',')
                     .Append(rule.Max.ToString(inv)).Append(']');
            }

            foreach (var offer in Offers)
            {
                o.Append(" #").Append(offer.Id).Append(' ').Append(OfferLooks.Id(offer.Look)).Append(' ')
                 .Append(string.Join(",", offer.Products)).Append(offer.Pick == OfferPick.Gap ? " gap " : " first ");
                foreach (var moment in offer.Moments) o.Append(OfferTriggers.Id(moment)).Append(',');
                o.Append(" in ").Append(string.Join(",", offer.Segments)).Append(" not ").Append(string.Join(",", offer.Exclude))
                 .Append(' ').Append(offer.CooldownHours.ToString(inv)).Append('/').Append(offer.Shows.ToString(inv))
                 .Append('/').Append(offer.Buys.ToString(inv)).Append('/').Append(offer.Priority.ToString(inv))
                 .Append('/').Append(offer.Weight.ToString(inv)).Append('/').Append(offer.LastsMinutes.ToString(inv));
            }

            return o.ToString();
        }

        // ------------------------------------------------------------------ the reader's hands
        /// <summary>Field reads that name what is wrong. A key beginning with an underscore is a comment.</summary>
        sealed class Doc
        {
            readonly bool _strict;
            public Doc(bool strict) => _strict = strict;
            public string Fault { get; private set; }

            public bool Keys(Dictionary<string, object> obj, string at, params string[] known)
            {
                if (!_strict) return true;
                foreach (var key in obj.Keys)
                {
                    if (key.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (Array.IndexOf(known, key) >= 0) continue;
                    Fault = $"{at}{key} is not a figure this build knows (a typo, or a figure from a newer build)";
                    return false;
                }
                return true;
            }

            public bool Long(Dictionary<string, object> obj, string key, string at, out long value)
            {
                value = 0;
                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                if (!(raw is long whole)) { Fault = $"{at}{key} is not a whole number"; return false; }
                value = whole;
                return true;
            }

            public bool Int(Dictionary<string, object> obj, string key, int min, int max, out int value, string at = "")
            {
                value = 0;
                if (!Long(obj, key, at, out long whole)) return false;
                if (whole < min || whole > max) { Fault = $"{at}{key} is {whole}, outside {min}..{max}"; return false; }
                value = (int)whole;
                return true;
            }

            public bool Ints(Dictionary<string, object> obj, string key, int fewest, int most, int min, int max, out int[] values)
            {
                values = null;
                if (!List(obj, key, fewest, most, out var list)) return false;
                values = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is long whole) || whole < min || whole > max)
                    {
                        Fault = $"{key}[{i}] is not a whole number in {min}..{max}";
                        values = null;
                        return false;
                    }
                    values[i] = (int)whole;
                }
                return true;
            }

            public bool Bool(Dictionary<string, object> obj, string key, out bool value)
            {
                value = false;
                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{key} is missing"; return false; }
                if (!(raw is bool b)) { Fault = $"{key} is not true or false"; return false; }
                value = b;
                return true;
            }

            public bool List(Dictionary<string, object> obj, string key, int fewest, int most, out List<object> list, string at = "")
            {
                list = null;
                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                list = raw as List<object>;
                if (list == null) { Fault = $"{at}{key} is not a list"; return false; }
                if (list.Count < fewest || list.Count > most) { Fault = $"{at}{key} lists {list.Count}, outside {fewest}..{most}"; return false; }
                return true;
            }

            public bool Name(Dictionary<string, object> obj, string key, string at, out string value)
            {
                value = null;
                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                value = raw as string;
                if (string.IsNullOrEmpty(value)) { Fault = $"{at}{key} is not a name"; return false; }
                return true;
            }

            public bool Id(Dictionary<string, object> obj, string key, string at, out string value)
            {
                if (!Name(obj, key, at, out value)) return false;
                if (IsId(value, 32)) return true;
                Fault = $"{at}{key} '{value}' is not an id (lower case letters, digits and underscores, at most 32)";
                return false;
            }

            public bool Names(Dictionary<string, object> obj, string key, int fewest, int most, string at, out List<string> names)
            {
                names = null;
                if (!List(obj, key, fewest, most, out var list, at)) return false;
                names = new List<string>(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is string s) || s.Length == 0) { Fault = $"{at}{key}[{i}] is not a name"; names = null; return false; }
                    if (names.Contains(s)) { Fault = $"{at}{key} names '{s}' twice"; names = null; return false; }
                    names.Add(s);
                }
                return true;
            }
        }
    }
}
