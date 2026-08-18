using System;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// The permanent ids of the game's rewarded ad placements.
    ///
    /// <para>
    /// Ids, not an enum, and never renamed or reused — the same rule a
    /// <see cref="Content.LevelId"/> and a <see cref="Persistence.Currency"/> follow, for
    /// the same reason. A placement id is written into every award id the server
    /// adjudicates, into the daily cap counters in the save file, into the mediation
    /// dashboard's own configuration and into analytics. Five places, three of them
    /// outside this repository. An enum's numbering is an implementation detail and has no
    /// business reaching any of them.
    /// </para>
    /// <para>
    /// What a placement <em>pays</em> is not here. That is content — see
    /// <see cref="AdRewardTable"/> — because a reward amount is the single most retuned
    /// number in an ad-supported game and must never need a store review. This type is
    /// only the list of doors; the table says what is behind each one.
    /// </para>
    /// </summary>
    public static class AdPlacement
    {
        /// <summary>
        /// Offered when a defeat leaves the player with no hearts. The one placement with
        /// a natural trigger, and the one that carries the loop.
        /// </summary>
        public const string HeartRefill = "heart_refill";

        /// <summary>Offered from the coin pill on the home screen. Player-initiated, always.</summary>
        public const string CoinBonus = "coin_bonus";

        /// <summary>
        /// Offered when the glade's clock runs out, and it buys more of that clock.
        ///
        /// <para>
        /// The highest-intent moment in the game: the player has already invested the whole
        /// run, the loss is one frame away, and what is on offer is the only thing that
        /// undoes it. It is also the only placement that pays no currency at all — see
        /// <see cref="Daily.ChestDropKind.RunTime"/> — which is why it needs no account, no
        /// claim and no server opinion, and why the shared cooldown does not apply to it.
        /// </para>
        /// <para>
        /// Deliberately repeatable within one run. Bounded by the placement's own daily cap
        /// and nothing else, because the thing repetition costs is not the game's: a
        /// continued run's elapsed time keeps climbing against thresholds derived from par,
        /// so the second extension has usually already cost the player their third star and
        /// the fourth has cost them their second. The clock grades the run whether or not it
        /// ends it.
        /// </para>
        /// </summary>
        public const string RunContinue = "run_continue";

        /// <summary>
        /// Offered on the victory panel, for credits on top of what the glade paid.
        ///
        /// <para>
        /// A flat, server-granted amount rather than a literal doubling of what this run
        /// earned, and that is a constraint rather than a preference. Earned credits are
        /// <em>derived</em> from the star ledger (invariant 9), so there is no accumulated
        /// figure to multiply; doubling one run would mean storing which runs had been
        /// doubled, which is a forgeable per-level set that <em>pays</em> — invariant 15
        /// sends that straight back to 13, and the honest answer there is that a signed
        /// callback naming a placement is the only ad evidence this game can get. So the
        /// amount is content, the server grants its own figure exactly as it does for
        /// <see cref="CoinBonus"/>, and the panel prints the real number instead of a
        /// multiplier it cannot honour.
        /// </para>
        /// </summary>
        public const string WinBonus = "win_bonus";

        public static readonly string[] All = { HeartRefill, CoinBonus, RunContinue, WinBonus };

        /// <summary>
        /// Whether an id names a placement this build knows.
        ///
        /// Asked before anything is counted against a cap or submitted as an award, so a
        /// content file written against a newer build cannot make this one bank counters
        /// for a placement it will never show.
        /// </summary>
        public static bool IsKnown(string placementId)
        {
            if (string.IsNullOrEmpty(placementId)) return false;

            for (int i = 0; i < All.Length; i++)
                if (string.Equals(All[i], placementId, StringComparison.Ordinal)) return true;

            return false;
        }
    }

    /// <summary>
    /// One attempt to show a rewarded ad.
    ///
    /// <para>
    /// A local handle and nothing more. It carries which placement was asked for and a
    /// throwaway id used to correlate the show call with its result in logs — it is
    /// <b>not</b> a claim, not a key, and the server never sees it.
    /// </para>
    /// <para>
    /// That is worth stating plainly because the obvious design is the opposite one, and
    /// it was tried here first. The tempting shape is a client-generated nonce handed to
    /// the SDK as a custom parameter, echoed back by the network's verification callback,
    /// and used as a derived award id exactly like a daily chest's — which would let an ad
    /// reward ride the existing <see cref="Persistence.CurrencyLedger.TryAward"/> path and
    /// be spendable the instant the video ends.
    /// </para>
    /// <para>
    /// It does not survive contact with the SDK. LevelPlay 9 removed the legacy
    /// <c>setRewardedVideoServerParams</c> that used to carry arbitrary per-impression
    /// data; what remains is <c>LevelPlaySegment</c>, which is documented as user
    /// segmentation and says nothing about reaching the server-to-server callback. Building
    /// the one security-critical link in the economy on an undocumented hope is precisely
    /// the thing this project refuses to do — and the failure would be silent, in the worst
    /// possible way: ads that play, players who are told they earned coins, and a server
    /// that never grants them.
    /// </para>
    /// <para>
    /// So the design uses only what the callback contract actually guarantees:
    /// <c>[USER_ID]</c>, which we set to the account id when the SDK starts, and
    /// <c>[EVENT_ID]</c>, which the network generates per view. Between them the server
    /// knows who watched what, and it grants on its own authority — see
    /// <see cref="RewardedAds"/> and <c>firebase/functions/src/ads.ts</c>. The cost is one
    /// sync round trip before coins appear, which is affordable because watching an ad
    /// already required a network connection.
    /// </para>
    /// </summary>
    public readonly struct AdImpression
    {
        public readonly string PlacementId;

        /// <summary>Correlates a show call with its result in a log. Never leaves the device.</summary>
        public readonly string TraceId;

        public AdImpression(string placementId, string traceId)
        {
            PlacementId = placementId ?? string.Empty;
            TraceId = traceId ?? string.Empty;
        }

        public bool IsValid => !string.IsNullOrEmpty(PlacementId);

        public static AdImpression New(string placementId)
            => new AdImpression(placementId, Guid.NewGuid().ToString("N"));

        /// <summary>What a rewarded-ad grant records as its cause, on both sides.</summary>
        public const string Reason = "rewarded_ad";
    }
}
