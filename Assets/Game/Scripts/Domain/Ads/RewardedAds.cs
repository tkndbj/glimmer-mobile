using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Ads
{
    /// <summary>Why an offer is not available, so the panel can say which kind of no it is.</summary>
    public enum AdOfferState
    {
        /// <summary>Loaded, inside the cap, off cooldown. The button is live.</summary>
        Ready = 0,

        /// <summary>No provider, or the content table does not offer this placement.</summary>
        Unavailable,

        /// <summary>The network has nothing loaded yet. Worth waiting a moment for.</summary>
        NotLoaded,

        /// <summary>Today's allowance for this placement is spent.</summary>
        CapReached,

        /// <summary>Another ad was watched too recently.</summary>
        CoolingDown,

        /// <summary>
        /// The reward would be wasted — hearts at full, and nothing to top up. Offering
        /// anyway would take thirty seconds of someone's life in exchange for nothing.
        /// </summary>
        NothingToGain,

        /// <summary>
        /// A currency reward with no account to pay it into yet. See
        /// <see cref="RewardedAds.CanAdjudicate"/>.
        /// </summary>
        NeedsAccount,
    }

    /// <summary>
    /// Whether an offer can be made, and what to say when it cannot.
    ///
    /// Carries the seconds left on a cooldown so the panel can count down rather than
    /// simply refusing — a disabled button with no explanation is how a player concludes
    /// the feature is broken.
    /// </summary>
    public readonly struct AdOfferStatus
    {
        public readonly AdOfferState State;
        public readonly AdOffer Offer;
        public readonly long SecondsRemaining;

        public AdOfferStatus(AdOfferState state, AdOffer offer, long secondsRemaining = 0)
        {
            State = state;
            Offer = offer;
            SecondsRemaining = secondsRemaining < 0 ? 0 : secondsRemaining;
        }

        public bool CanShow => State == AdOfferState.Ready;
    }

    /// <summary>
    /// The rewarded-ad loop: what may be offered, what a finished view pays, and how the
    /// payment reaches the player.
    ///
    /// <para>
    /// Shaped exactly like <see cref="DailyChests"/>, because it is the same problem seen
    /// from a different angle — a repeatable award, bounded per day, that has to survive a
    /// reset nobody runs, a merge nobody supervises and a clock nobody controls. What
    /// differs is the one thing that matters: a chest's contents can be recomputed by the
    /// server from (account, day, index), and an ad's cannot. Nothing about "this player
    /// watched a video" is derivable. So the split here is sharper than the chest one.
    /// </para>
    /// <para>
    /// <b>Hearts and boosts are applied locally; currency is only ever claimed.</b> A heart
    /// is clamped at five and a boost expires, so the worst a forged one buys is a few
    /// runs somebody would otherwise have waited for — not worth a round trip. Currency is
    /// the thing that can be bought with real money and therefore the thing an attacker
    /// forges, so it goes through <see cref="PlayerProgression.Award"/> under an id derived
    /// from the impression nonce, and is granted by the server only once the ad network's
    /// own machines have confirmed the view in a signed callback. The client's number is a
    /// prediction, exactly as it is for a chest.
    /// </para>
    /// <para>
    /// The counters here are pacing, not money. They cap what may be <em>offered</em>; they
    /// do not decide what anything paid. A save file edited to zero them buys another
    /// offer and no currency at all, because the server is not counting these.
    /// </para>
    /// </summary>
    public static class RewardedAds
    {
        static readonly Dictionary<string, int> _watched = new Dictionary<string, int>(StringComparer.Ordinal);

        static IAdProvider _provider = new NullAdProvider();
        static int _dayKey;
        static long _lastWatchedUnix;

        /// <summary>
        /// Raised when the counters, the cooldown or provider readiness move, so an open
        /// panel repaints. Ad readiness arrives asynchronously and a panel that painted
        /// once shows a dead button for as long as it is open.
        /// </summary>
        public static event Action Changed;

        /// <summary>
        /// Installs the provider. Called by <c>Boot</c>, which picks the real one when an
        /// SDK is compiled in and leaves the null one in place otherwise.
        /// </summary>
        public static void Install(IAdProvider provider)
        {
            if (_provider != null) _provider.ReadinessChanged -= OnReadinessChanged;

            _provider = provider ?? new NullAdProvider();
            _provider.ReadinessChanged += OnReadinessChanged;

            Raise();
        }

        public static IAdProvider Provider => _provider;

        /// <summary>The live table, which content may have retuned since launch.</summary>
        public static AdRewardTable Table => ProgressionRules.Table.Ads;

        static void OnReadinessChanged(string placementId) => Raise();

        // --------------------------------------------------------------- offering
        /// <summary>
        /// Whether this placement can be offered right now, and why not when it cannot.
        ///
        /// <para>
        /// The order of the checks is the order the answers matter in. Content first —
        /// a placement the table does not carry is switched off and nothing else is worth
        /// computing. Then the caps, which are ours and knowable offline. Then readiness,
        /// which is the network's and changes by itself. A panel that reported "no fill"
        /// for a placement that was actually capped would send the player to try again in
        /// five minutes for a thing that resets at midnight.
        /// </para>
        /// </summary>
        public static AdOfferStatus Status(string placementId)
        {
            Sync();

            var offer = Table.Offer(placementId);
            if (!offer.IsValid) return new AdOfferStatus(AdOfferState.Unavailable, offer);

            if (!WouldBenefit(offer)) return new AdOfferStatus(AdOfferState.NothingToGain, offer);

            if (!CanAdjudicate(offer)) return new AdOfferStatus(AdOfferState.NeedsAccount, offer);

            if (WatchedToday(placementId) >= offer.DailyCap)
                return new AdOfferStatus(AdOfferState.CapReached, offer,
                                         DailyRules.SecondsUntilReset(GameClock.NowUnix()));

            long cooling = CooldownRemaining();
            if (cooling > 0) return new AdOfferStatus(AdOfferState.CoolingDown, offer, cooling);

            if (!_provider.IsReady(placementId))
                return new AdOfferStatus(AdOfferState.NotLoaded, offer);

            return new AdOfferStatus(AdOfferState.Ready, offer);
        }

        /// <summary>Shorthand for the common case: may this offer be taken right now?</summary>
        public static bool CanOffer(string placementId) => Status(placementId).CanShow;

        /// <summary>
        /// Whether the offer belongs on screen at all, even if it cannot be taken yet.
        ///
        /// <para>
        /// Distinct from <see cref="CanOffer"/>, and the distinction is the difference
        /// between a feature players understand and one they think is broken. A button that
        /// vanishes during its cooldown teaches nobody anything — the player who watched a
        /// video a minute ago simply finds the option gone and concludes it was a one-off.
        /// Drawn and disabled with "another video in 0:32" on it, the same state teaches the
        /// rule in one glance.
        /// </para>
        /// <para>
        /// Three states still hide it, because each one is a thing that will not resolve by
        /// waiting on this screen: no provider at all, hearts already full, and no account
        /// to pay coins into. Showing a disabled button for those is not information, it is
        /// a dead control.
        /// </para>
        /// </summary>
        public static bool ShouldOffer(string placementId)
        {
            switch (Status(placementId).State)
            {
                case AdOfferState.Ready:
                case AdOfferState.NotLoaded:
                case AdOfferState.CoolingDown:
                case AdOfferState.CapReached:
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether the reward would actually land.
        ///
        /// Only hearts can be wasted — every other kind accumulates. A player at five
        /// hearts who watches a heart ad gets nothing, and there is no honest way to pay
        /// them the difference: converting the overflow to credits would make the payout
        /// depend on how many hearts they happened to hold, which is the exact property
        /// that would stop the server being able to check it.
        /// </summary>
        static bool WouldBenefit(AdOffer offer)
            => offer.Kind != ChestDropKind.Hearts || !Wallet.Hearts.IsFull;

        /// <summary>
        /// Whether a finished view could actually be paid for.
        ///
        /// <para>
        /// The same gate <see cref="DailyChests.CanOpen"/> applies, narrowed to the rewards
        /// that need it. A currency award is granted by the server when the ad network's
        /// verification callback arrives, and that callback identifies the player by the
        /// account id the client handed the SDK before the video started. With no account
        /// there is no id to hand over, so the callback would arrive naming nobody and the
        /// claim would sit unconfirmed forever — the player would watch an ad, see coins,
        /// and lose them on the next sync.
        /// </para>
        /// <para>
        /// Hearts and boosts are deliberately <em>not</em> gated: they are applied by this
        /// client and never adjudicated, so there is nothing for an account to be needed
        /// for. That asymmetry is the whole reason the defeat offer — the one with a real
        /// trigger, and the one a brand-new player meets first — keeps working on a very
        /// first launch with no network.
        /// </para>
        /// <para>
        /// As with chests, the gate lifts when no backend is configured at all, because
        /// then nothing is ever submitted and there is no second opinion to wait for.
        /// </para>
        /// </summary>
        static bool CanAdjudicate(AdOffer offer)
            => !offer.IsCurrency || CloudState.IsSignedIn || !CloudSaveService.IsAvailable;

        /// <summary>Seconds left before another ad may be offered. 0 when none.</summary>
        public static long CooldownRemaining()
        {
            int cooldown = Table.CooldownSeconds;
            if (cooldown <= 0 || _lastWatchedUnix <= 0) return 0;

            long elapsed = GameClock.NowUnix() - _lastWatchedUnix;

            // A clock that jumped backwards leaves this negative, which would read as a
            // cooldown longer than the one configured. GameClock already refuses to run
            // backwards; this is the belt to that pair of braces.
            if (elapsed < 0) return cooldown;

            long remaining = cooldown - elapsed;
            return remaining < 0 ? 0 : remaining;
        }

        public static int WatchedToday(string placementId)
        {
            Sync();
            return _watched.TryGetValue(placementId ?? string.Empty, out int count) ? count : 0;
        }

        /// <summary>Views left today for a placement, for a panel that wants to show it.</summary>
        public static int RemainingToday(string placementId)
        {
            var offer = Table.Offer(placementId);
            if (!offer.IsValid) return 0;

            int left = offer.DailyCap - WatchedToday(placementId);
            return left < 0 ? 0 : left;
        }

        // ---------------------------------------------------------------- paying
        /// <summary>
        /// Records a finished view and hands over what it was worth.
        ///
        /// <para>
        /// Called only with a result the provider reported as <see cref="AdOutcome.Rewarded"/>.
        /// Returns the drop that was applied, so the caller can show it, or an invalid drop
        /// when nothing was paid — which happens when the cap moved underneath the view
        /// (a config push mid-ad) or when the impression does not match the placement.
        /// </para>
        /// <para>
        /// The counter is raised <em>before</em> the reward is applied and is never rolled
        /// back on a failure below. Paying nothing and charging a view is a bug worth
        /// having; paying twice and charging one is money.
        /// </para>
        /// </summary>
        public static ChestDrop Redeem(AdShowResult result)
        {
            if (!result.Rewarded) return ChestDrop.None;

            Sync();

            var impression = result.Impression;
            var offer = Table.Offer(impression.PlacementId);
            if (!offer.IsValid) return ChestDrop.None;

            // The cap is re-checked here rather than trusted from the pre-show call. A
            // published table can change while a thirty-second video is playing, and the
            // view that started legally has to finish under the rules that are live now.
            if (WatchedToday(impression.PlacementId) >= offer.DailyCap) return ChestDrop.None;

            long now = GameClock.NowUnix();

            _watched[impression.PlacementId] = WatchedToday(impression.PlacementId) + 1;
            _lastWatchedUnix = now;

            var drop = offer.AsDrop();
            Apply(drop, impression, now);

            SaveService.Save();
            Raise();

            Telemetry.Track("rewarded_ad_completed",
                            "placement", impression.PlacementId,
                            "reward", drop.ToString(),
                            "watched_today", WatchedToday(impression.PlacementId),
                            "day", _dayKey);

            return drop;
        }

        /// <summary>
        /// Hands over one reward.
        ///
        /// <para>
        /// Hearts and boosts land here and now, exactly as they do for a chest: they are
        /// clamped, they expire, and adjudicating them would cost a round trip to protect
        /// something nobody would bother forging.
        /// </para>
        /// <para>
        /// Currency is <b>not</b> credited — not even as a claim. This is the one place the
        /// ad path diverges from the chest path, and the reason is that the client has
        /// nothing to key a claim on. A chest's award id is derived from facts both sides
        /// can compute; an ad's would have to be derived from the impression, and no
        /// per-impression token survives the trip through LevelPlay 9 to the verification
        /// callback (see <see cref="AdImpression"/>). So the server grants on its own
        /// authority when the network vouches for the view, and all this does is ask for
        /// the answer sooner. Writing a local claim as well would double-count it: the
        /// pending entry and the server's raised baseline would both be in the balance.
        /// </para>
        /// </summary>
        static void Apply(ChestDrop drop, AdImpression impression, long now)
        {
            if (!drop.IsValid) return;

            switch (drop.Kind)
            {
                case ChestDropKind.Credits:
                case ChestDropKind.Gems:
                    // Already granted, or about to be, by the callback the network makes to
                    // our server. Pulling rather than pushing: the sync adopts whatever the
                    // server says the balance is, which is the same path a purchase takes.
                    CloudSaveService.BeginSync();
                    break;

                case ChestDropKind.Hearts:
                    Wallet.GrantHearts(drop.Amount);
                    break;

                case ChestDropKind.HeartBoost:
                    Wallet.GrantHeartBoost(drop.Amount);
                    break;
            }
        }

        // ------------------------------------------------------------ the day
        /// <summary>
        /// Rolls the counters over when the day has moved on.
        ///
        /// Lazy, at the top of every read, for the reason <see cref="DailyChests"/> spells
        /// out: a midnight timer has to survive a backgrounded app, a suspended process and
        /// a device asleep in a drawer, and a comparison cannot be forgotten by a caller.
        ///
        /// <para>
        /// The cooldown deliberately survives the rollover. It is a fact about when the
        /// player last watched something, not about the day, and clearing it at midnight
        /// would hand somebody a free skip once a day for no reason anyone could explain.
        /// </para>
        /// </summary>
        static void Sync()
        {
            int today = DailyRules.DayKeyFor(GameClock.NowUnix());
            if (today == _dayKey) return;

            // Only ever forward, guarding a merged save that carries a day from a device
            // whose clock ran ahead. Resetting back into that day would hand out its
            // allowance a second time.
            if (today < _dayKey) return;

            _dayKey = today;
            _watched.Clear();

            SaveService.MarkDirty();
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _watched.Clear();

            var ads = dto?.ads;

            _dayKey = ads == null || ads.dayKey < 0 ? 0 : ads.dayKey;
            _lastWatchedUnix = ads == null || ads.lastWatchedUnix < 0 ? 0L : ads.lastWatchedUnix;

            if (ads?.watched != null) Absorb(_watched, ads.watched);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            dto.ads = new AdStateDto
            {
                dayKey = _dayKey,
                watched = ToSortedArray(_watched),
                lastWatchedUnix = _lastWatchedUnix,
            };
        }

        /// <summary>
        /// Flattens the counters into a stable order.
        ///
        /// Sorted by placement id, and that is not tidiness. <see cref="SaveChecksum"/>
        /// hashes the serialised file, so an array whose order depended on dictionary
        /// internals would produce a different checksum for identical state — making every
        /// launch look like a change, writing a file that did not need writing and sending
        /// a sync with nothing in it. It is also what lets <c>SaveDelta</c> compare the two
        /// with an ordered walk, the same way the tip list is compared.
        /// </summary>
        static AdViewCountDto[] ToSortedArray(Dictionary<string, int> counts)
        {
            var keys = new List<string>(counts.Keys);
            keys.Sort(StringComparer.Ordinal);

            var result = new AdViewCountDto[keys.Count];
            for (int i = 0; i < keys.Count; i++)
                result[i] = new AdViewCountDto { placement = keys[i], count = counts[keys[i]] };

            return result;
        }

        /// <summary>
        /// Folds a stored array into a map, keeping the larger count for a repeated id.
        ///
        /// A duplicate is a malformed file rather than two allowances, and the larger value
        /// is the conservative reading — the one that cannot be produced by editing the
        /// file to win back a spent view.
        /// </summary>
        static void Absorb(Dictionary<string, int> into, AdViewCountDto[] counts)
        {
            for (int i = 0; i < counts.Length; i++)
            {
                var entry = counts[i];
                if (entry == null || string.IsNullOrEmpty(entry.placement)) continue;

                int count = entry.count < 0 ? 0 : entry.count;

                into[entry.placement] = into.TryGetValue(entry.placement, out int held) && held > count
                    ? held
                    : count;
            }
        }

        /// <summary>
        /// Joins two devices' ad counters.
        ///
        /// <para>
        /// The later day wins outright, exactly as it does for chests — an older day's
        /// counters describe a day that is over. Within a shared day every count takes the
        /// <em>larger</em> value, which is the conservative direction here for a different
        /// reason than it is for chests: an allowance is consumable, and taking the smaller
        /// count would let two devices refill each other's daily ads by taking turns.
        /// </para>
        /// <para>
        /// The cooldown takes the later timestamp for the same reason, and survives a day
        /// change because it is not a fact about the day. Note the asymmetry with
        /// <c>Hearts.JoinBoost</c>, which is generous where this is mean: a boost arrives
        /// deduplicated by its own award id and cannot be minted by merging, whereas an
        /// allowance is exactly the thing a merge could mint.
        /// </para>
        /// </summary>
        internal static AdStateDto Join(AdStateDto mine, AdStateDto other)
        {
            if (mine == null && other == null) return new AdStateDto();
            if (mine == null) return Copy(other);
            if (other == null) return Copy(mine);

            // The cooldown outlives the day, so it is joined before the day is decided.
            long lastWatched = Math.Max(mine.lastWatchedUnix, other.lastWatchedUnix);

            if (mine.dayKey > other.dayKey) return Copy(mine, lastWatched);
            if (other.dayKey > mine.dayKey) return Copy(other, lastWatched);

            var merged = new Dictionary<string, int>(StringComparer.Ordinal);
            if (mine.watched != null) Absorb(merged, mine.watched);
            if (other.watched != null) Absorb(merged, other.watched);

            return new AdStateDto
            {
                dayKey = mine.dayKey,
                watched = ToSortedArray(merged),
                lastWatchedUnix = lastWatched,
            };
        }

        static AdStateDto Copy(AdStateDto state) => Copy(state, state.lastWatchedUnix);

        /// <summary>
        /// A detached, canonical copy: deduplicated, sorted, and holding no reference to
        /// the source arrays.
        ///
        /// Routed through <see cref="Absorb"/> rather than copied element-wise so that the
        /// result of a join is always in canonical form, whichever branch produced it. A
        /// merge that returned one side's array untouched would let a duplicate arriving
        /// from a server payload survive into the file, where it would then be read as the
        /// larger of two counts on some later launch and not on this one.
        /// </summary>
        static AdStateDto Copy(AdStateDto state, long lastWatchedUnix)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            if (state.watched != null) Absorb(counts, state.watched);

            return new AdStateDto
            {
                dayKey = state.dayKey,
                watched = ToSortedArray(counts),
                lastWatchedUnix = lastWatchedUnix,
            };
        }

        /// <summary>Forgets today's allowance. Dev only, and used by the wipe.</summary>
        internal static void Reset()
        {
            _dayKey = 0;
            _lastWatchedUnix = 0L;
            _watched.Clear();
        }
    }
}
