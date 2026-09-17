using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Tasks;

namespace GlimmerGrove.Referral
{
    /// <summary>What one side is paid for one finished invitee: a tier, this many times.</summary>
    public readonly struct ReferralPayment
    {
        public readonly ChestTier Tier;
        public readonly int Count;

        public ReferralPayment(ChestTier tier, int count)
        {
            Tier = tier;
            Count = count < 1 ? 1 : count > ReferralTable.MaxCount ? ReferralTable.MaxCount : count;
        }

        public bool IsValid => Tier != null;

        public override string ToString() => Count + "x " + (Tier?.Id ?? "?");
    }

    /// <summary>
    /// Refer-a-friend, as content: the milestone an invitee has to reach, what the referrer
    /// is paid per finished invitee, and what the invitee is paid on finishing.
    ///
    /// <para>
    /// <b>Every number here is adjudicated on the server, and this copy only draws.</b> The
    /// same block is published as <c>config/progression.referral</c> by <c>seed-config.mjs</c>,
    /// and the server judges a claim against <em>its</em> copy. That is what lets the referral
    /// pay currency without a claim id in the save — the whole state is server-owned
    /// (invariant 51).
    /// </para>
    /// <para>
    /// <b>It is flat, by the owner's decision on 2026-09-17</b>: every finished invitee pays
    /// the referrer the same chests, up to the cap of bound invitees, and the invitee is paid
    /// the same on finishing. A payment names a tier (invariant 45) and a count, so one
    /// published disclosure covers every chest that pays it. <b>A referral chest grows no
    /// season</b>, also the owner's decision: nothing here calls <c>SeasonLedger.NoteChest</c>.
    /// </para>
    /// <para>
    /// <b>The milestone is named, not derived.</b> It is the first chapter of the live mode
    /// today, and a content push can move it. <c>content.py</c> proves it is shipped, enabled
    /// and reachable with no stars and no keeper level.
    /// </para>
    /// </summary>
    public sealed class ReferralTable
    {
        ReferralTable(ChapterId milestone, int maxBound, string shareLink,
                      ReferralPayment invitee, ReferralPayment perInvitee)
        {
            Milestone = milestone;
            MaxBound = maxBound;
            ShareLink = shareLink ?? string.Empty;
            Invitee = invitee;
            PerInvitee = perInvitee;
        }

        /// <summary>The chapter an invitee has to clear before either side is paid.</summary>
        public ChapterId Milestone { get; }

        /// <summary>
        /// How many invitees may ever bind to one code, and therefore the most the referrer
        /// can be paid for. Bound, not finished: a ceiling that only moved on the invitee's
        /// play would be a list with no ceiling, and the list is what the server stores.
        /// </summary>
        public int MaxBound { get; }

        /// <summary>
        /// The link the share sheet carries beside the code. Content rather than derived,
        /// because the iOS store link cannot be derived (invariant 49b) and the Android one
        /// does not resolve until the listing is public.
        /// </summary>
        public string ShareLink { get; }

        /// <summary>What the invitee opens on clearing the milestone.</summary>
        public ReferralPayment Invitee { get; }

        /// <summary>What the referrer opens for each invitee who clears it.</summary>
        public ReferralPayment PerInvitee { get; }

        /// <summary>Whether there is anything to offer at all. A table that offers nothing draws no entry.</summary>
        public bool Offers => MaxBound > 0 && Invitee.IsValid && PerInvitee.IsValid && Milestone.IsValid;

        /// <summary>The payment for one side, by claim kind.</summary>
        public ReferralPayment PaymentFor(ReferralClaimKind kind)
            => kind == ReferralClaimKind.Invitee ? Invitee : PerInvitee;

        // ------------------------------------------------------------- limits
        /// <summary>The most chests one payment may be. More is a typo, not a design.</summary>
        public const int MaxCount = 4;

        /// <summary>A cap above this is a document the server would have to page.</summary>
        public const int MaxBoundCeiling = 500;

        public const int MaxLinkLength = 200;

        // ------------------------------------------------------------ built in
        /// <summary>
        /// The table that ships inside the build, for a malformed file. Matches the shipped
        /// content on purpose: a fallback that offered a different payout from the one the
        /// server was seeded with would be a screen promising chests the server refuses.
        /// </summary>
        public static readonly ReferralTable Default = BuildDefault();

        static ReferralTable BuildDefault()
        {
            var royal = TaskTable.Default.Tier("royal");
            return new ReferralTable(ChapterId.Parse("s01_thornwatch"), 50, string.Empty,
                                     new ReferralPayment(royal, 2), new ReferralPayment(royal, 2));
        }

        /// <summary>A table that offers nothing, for a file that withdraws the feature.</summary>
        public static readonly ReferralTable None =
            new ReferralTable(ChapterId.None, 0, string.Empty, default, default);

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Reads the block. Absent is the built-in table; a block that authors a cap of nought
        /// withdraws the feature; anything malformed falls back to the built-in table with a
        /// problem, so a typo costs live tuning and never the screen.
        /// </summary>
        public static ReferralTable Resolve(ReferralDto dto, Func<string, ChestTier> tier,
                                            List<string> problems)
        {
            problems = problems ?? new List<string>();
            if (dto == null || !dto.IsAuthored) return Default;

            if (dto.withdrawn) return None;                // authored: withdrawn on purpose

            if (!ChapterId.TryParse(dto.milestoneChapter, out var milestone, out string idError))
            {
                problems.Add($"referral milestoneChapter '{dto.milestoneChapter}' is rejected: {idError}; " +
                             "using the built-in table");
                return Default;
            }

            if (!TryReadPayment(dto.invitee, "invitee", tier, problems, out var invitee)) return Default;
            if (!TryReadPayment(dto.perInvitee, "perInvitee", tier, problems, out var perInvitee)) return Default;

            int maxBound = dto.maxBound;
            if (maxBound <= 0)
            {
                problems.Add("referral block authors no maxBound; using the built-in table");
                return Default;
            }

            if (maxBound > MaxBoundCeiling)
            {
                problems.Add($"referral maxBound is {maxBound}, above the supported {MaxBoundCeiling}; clamped");
                maxBound = MaxBoundCeiling;
            }

            string link = dto.shareLink ?? string.Empty;
            if (link.Length > MaxLinkLength || (link.Length > 0 && !link.StartsWith("https://", StringComparison.Ordinal)))
            {
                problems.Add($"referral shareLink '{link}' is not an https link under {MaxLinkLength} " +
                             "characters; the share sheet carries the code alone");
                link = string.Empty;
            }

            return new ReferralTable(milestone, maxBound, link, invitee, perInvitee);
        }

        static bool TryReadPayment(ReferralPaymentDto dto, string role, Func<string, ChestTier> tier,
                                   List<string> problems, out ReferralPayment payment)
        {
            payment = default;

            var chest = tier?.Invoke(dto?.tier);
            if (chest == null)
            {
                problems.Add($"referral {role} tier '{dto?.tier}' is not a chest tier the tasks block " +
                             "defines; using the built-in table");
                return false;
            }

            int count = dto.count <= 0 ? 1 : dto.count;
            if (count > MaxCount)
            {
                problems.Add($"referral {role} pays {count} chests, above the supported {MaxCount}; " +
                             "using the built-in table");
                return false;
            }

            payment = new ReferralPayment(chest, count);
            return true;
        }
    }
}
