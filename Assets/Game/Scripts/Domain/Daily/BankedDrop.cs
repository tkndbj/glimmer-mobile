using GlimmerGrove.Persistence;
using GlimmerGrove.Utilities;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// How a reward that is <em>banked</em> reaches the player — everything a chest or an ad can
    /// pay that is not currency and does not die with the run that earned it.
    ///
    /// <para>
    /// <b>It exists because there were two copies of this switch and they had already drifted.</b>
    /// <c>DailyChests.Apply</c> and <c>RewardedAds.Apply</c> each handled the kinds their own
    /// feature could pay, and the second one grew hints while the first did not — so a chest that
    /// rolled a hint would have shown the player a card and granted nothing, silently, on a table
    /// that is content and could hand one out from a config push tomorrow. That is invariant 5b
    /// exactly: two copies of one rule, each correct until a case appeared that only one of them
    /// had been written for.
    /// </para>
    /// <para>
    /// <b>What is deliberately not here.</b> Currency is the caller's, because the two callers do
    /// genuinely different things with it — a chest queues a claim keyed on the chest it came from
    /// (invariant 10a), while an ad asks the server for the answer it is already computing
    /// (invariant 10d) — and folding those together would hide the one asymmetry in the reward
    /// system that matters. A transient kind is the caller's for the opposite reason: it belongs
    /// to a live board, which no Domain static can see.
    /// </para>
    /// </summary>
    public static class BankedDrop
    {
        /// <summary>
        /// Applies a drop that is banked rather than adjudicated, and answers whether it did.
        ///
        /// <para>
        /// False means "not mine" — currency, transient, or nothing at all — so a caller can hand
        /// everything to this first and then deal only with what is left. It is <em>not</em> a
        /// report of whether the player benefited: a heart granted at the ceiling is applied and
        /// evaporates, which is the chest's documented bargain, and whether an offer should have
        /// been made at all is <c>RewardedAds.WouldBenefit</c>'s question, asked earlier.
        /// </para>
        /// <para>
        /// The switch is exhaustive over the banked kinds and lists the others explicitly rather
        /// than falling through a <c>default</c>, so adding a kind and forgetting it here is a
        /// case a reviewer can see missing — which is the property the old <c>RewardedAds</c>
        /// switch had and the chest's did not.
        /// </para>
        /// </summary>
        public static bool Apply(ChestDrop drop)
        {
            if (!drop.IsValid) return false;

            switch (drop.Kind)
            {
                case ChestDropKind.Hearts:
                    Wallet.GrantHearts(drop.Amount);
                    return true;

                case ChestDropKind.HeartBoost:
                    Wallet.GrantHeartBoost(drop.Amount);
                    return true;

                case ChestDropKind.Hints:
                    // Refused rather than clamped at a full pool — the hint ceiling equals its
                    // refill cap — which is why an offer that could pay one asks first.
                    Wallet.GrantHints(drop.Amount);
                    return true;

                case ChestDropKind.Utility:
                    // Applied here and now rather than claimed, exactly as hearts and hints are.
                    // A utility is not currency (invariant 13), and what makes that safe rather
                    // than merely cheap is that it cannot improve a grade (invariant 39) — so
                    // there is nothing for the server to recompute and nothing to forge.
                    UtilityLedger.Grant(drop.Item, drop.Amount);
                    return true;

                case ChestDropKind.Credits:
                case ChestDropKind.Gems:
                    // The caller's: a chest queues a claim keyed on the chest, an ad waits for
                    // the network's signed callback. See the type's remarks.
                    return false;

                case ChestDropKind.RunTime:
                    // Retired, and the caller's in any case: it belongs to a live board, which
                    // nothing in Domain can see.
                    return false;

                default:
                    return false;
            }
        }
    }
}
