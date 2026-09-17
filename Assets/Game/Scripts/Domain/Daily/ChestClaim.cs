using System;
using System.Collections.Generic;
using GlimmerGrove.Events;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Daily
{
    /// <summary>
    /// One chest the player may open: which tier it is, and the act of claiming it.
    ///
    /// <para>
    /// <b>Why this exists.</b> The opening ceremony is one panel with three beats, a reel,
    /// a payout cascade and an odds line — seven hundred lines of drawing whose only
    /// dependency on <em>where the chest came from</em> is a tier and a claim. It was bound
    /// to <see cref="TaskDefinition"/> because the tasks page was the only thing that had
    /// one; the season track is now the second, and a second copy of that panel would be a
    /// second place the reel can fall out of step with the lid.
    /// </para>
    /// <para>
    /// A value rather than an interface, because a source is a tier plus a closure and
    /// nothing else — an interface would be two classes and a factory to say the same thing.
    /// The closure answers <c>null</c> when the chest could not be claimed, which is a real
    /// state on both sides: another device may have synced the claim in, or the period may
    /// have rolled over between the tap and the frame that opens the panel.
    /// </para>
    /// </summary>
    public readonly struct ChestClaim
    {
        readonly Func<List<ChestDrop>> _claim;

        ChestClaim(ChestTier tier, Func<List<ChestDrop>> claim)
        {
            Tier = tier;
            _claim = claim;
        }

        /// <summary>What kind of chest this is. Names its picture, its reel and its odds.</summary>
        public ChestTier Tier { get; }

        /// <summary>False for a default-constructed value, which is what an unset field is.</summary>
        public bool IsValid => Tier != null && _claim != null;

        /// <summary>
        /// Claims it. False — with no drops — when it could not be, which the caller shows
        /// by closing rather than by saying anything: nothing was lost, and a panel
        /// explaining a race is worse than no panel.
        /// </summary>
        public bool TryClaim(out List<ChestDrop> drops)
        {
            drops = IsValid ? _claim() : null;
            return drops != null;
        }

        /// <summary>A task's chest, on the tasks page.</summary>
        public static ChestClaim ForTask(TaskDefinition task)
        {
            if (task == null) return default;

            return new ChestClaim(task.Tier,
                () => TaskLedger.TryClaim(task, out var drops) ? drops : null);
        }

        /// <summary>
        /// One night of the streak ladder, when that night pays a chest.
        ///
        /// Invalid for a night that pays a figure — the streak page throws those to the
        /// wallet itself — which is the honest answer rather than a chest panel wrapped
        /// around a number: a ceremony whose reel is a lid opening has nothing to open when
        /// the reward was never in a chest.
        /// </summary>
        public static ChestClaim ForStreakNight(int night)
        {
            var rung = DailyStreak.Ladder.Rung(night);
            if (!rung.IsChest) return default;

            return new ChestClaim(rung.Tier,
                () => DailyStreak.TryCollect(night, out var drops) ? drops : null);
        }

        /// <summary>
        /// A referral chest the server has already agreed to pay.
        ///
        /// The closure is <c>ReferralPayout.Land</c>: the server rolled the chest and moved
        /// the currency before this panel opened, and what is left to do at the start of the
        /// ceremony is bank the rest of the drops and adopt the balances — inside the claim,
        /// so the payout's snapshot of the pills is taken before the grant lands. It answers
        /// null when there is nothing for this device to bank (invariant 51), which the
        /// panel shows by closing.
        /// </summary>
        public static ChestClaim ForReferral(ChestTier tier, Func<List<ChestDrop>> land)
        {
            if (tier == null || land == null) return default;
            return new ChestClaim(tier, land);
        }

        /// <summary>One rung of a season's ladder, on one of its two tracks.</summary>
        public static ChestClaim ForSeason(GroveEvent season, EventMilestone rung, SeasonTrack track)
        {
            var tier = rung.TierOn(track);
            if (season == null || tier == null) return default;

            return new ChestClaim(tier,
                () => SeasonLedger.TryClaim(season, rung, track, out var drops) ? drops : null);
        }
    }
}
