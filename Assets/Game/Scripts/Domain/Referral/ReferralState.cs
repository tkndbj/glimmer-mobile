using System;
using System.Collections.Generic;

namespace GlimmerGrove.Referral
{
    /// <summary>
    /// The device's copy of what the server said about this account's referrals. Wire shape
    /// for the per-account cache; see <see cref="ReferralState"/>.
    /// </summary>
    [Serializable]
    public sealed class ReferralStateDto
    {
        public int schema;
        public string code;
        public int bound;
        public int finished;
        public string[] paid;
        public bool referred;
        public bool milestoneReached;
        public bool canRedeem;
        public long fetchedUnix;
    }

    /// <summary>
    /// What the server holds about one account's referrals, as last read.
    ///
    /// <para>
    /// <b>Server-owned, device-cached, never in the save.</b> Every number here is written by
    /// <c>referral.ts</c> and only ever read here: how many invitees have bound to this code,
    /// how many have cleared the milestone, which chests have been paid, and — for an account
    /// that typed somebody else's code — whether its own milestone has been reached. Putting
    /// any of it in the save would make it mergeable state with two writers, and a count of
    /// invitees is exactly the count invariant 11b refuses. The cache is a hint for drawing
    /// while the network is out, keyed by account so a switch cannot inherit the other
    /// player's code (8b's shape).
    /// </para>
    /// <para>
    /// <b>A paid chest is a subject string</b> — <c>rung:{friend}:{n}</c> or <c>invitee:{n}</c>
    /// (<see cref="ReferralLanding.Subject"/>) — the same spelling the server keys its grant
    /// on, so the list here and the grant log cannot disagree about what "paid" means.
    /// </para>
    /// </summary>
    public sealed class ReferralState
    {
        public const int Schema = 2;

        public static readonly ReferralState Empty = new ReferralState(
            string.Empty, 0, 0, Array.Empty<string>(), false, false, false, 0L);

        readonly string[] _paid;

        public ReferralState(string code, int bound, int finished, string[] paid,
                             bool referred, bool milestoneReached, bool canRedeem, long fetchedUnix)
        {
            Code = code ?? string.Empty;
            Bound = bound < 0 ? 0 : bound;
            Finished = finished < 0 ? 0 : finished;
            _paid = paid ?? Array.Empty<string>();
            Referred = referred;
            MilestoneReached = milestoneReached;
            CanRedeem = canRedeem;
            FetchedUnix = fetchedUnix < 0 ? 0 : fetchedUnix;
        }

        /// <summary>This account's own code, folded. Empty until the server has minted one.</summary>
        public string Code { get; }

        /// <summary>How many accounts have typed this code.</summary>
        public int Bound { get; }

        /// <summary>How many of them have cleared the milestone.</summary>
        public int Finished { get; }

        /// <summary>The chests the server has paid, by subject.</summary>
        public IReadOnlyList<string> Paid => _paid;

        /// <summary>Whether this account typed somebody's code.</summary>
        public bool Referred { get; }

        /// <summary>Whether the server has seen this account clear the milestone. Only meaningful when <see cref="Referred"/>.</summary>
        public bool MilestoneReached { get; }

        /// <summary>Whether the server would still accept a code from this account.</summary>
        public bool CanRedeem { get; }

        /// <summary>When this was read, in unix seconds. Nought for a state nothing has fetched.</summary>
        public long FetchedUnix { get; }

        /// <summary>Whether the server has ever answered for this account on this device.</summary>
        public bool IsKnown => FetchedUnix > 0;

        public bool HasPaid(string subject)
        {
            for (int i = 0; i < _paid.Length; i++)
                if (string.Equals(_paid[i], subject, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>How many of a payment's chests have been paid: the count of its subjects in the list.</summary>
        public int PaidCount(ReferralClaimKind kind, int goal, int count)
        {
            int paid = 0;
            for (int n = 1; n <= count; n++)
                if (HasPaid(ReferralLanding.Subject(kind, goal, n))) paid++;
            return paid;
        }

        // -------------------------------------------------------------- the cache
        public ReferralStateDto ToDto()
            => new ReferralStateDto
            {
                schema = Schema,
                code = Code,
                bound = Bound,
                finished = Finished,
                paid = (string[])_paid.Clone(),
                referred = Referred,
                milestoneReached = MilestoneReached,
                canRedeem = CanRedeem,
                fetchedUnix = FetchedUnix,
            };

        /// <summary>
        /// Reads a cached copy. A schema this build does not write is read as nothing rather
        /// than as a guess: the cache is only a hint, and the next fetch replaces it whole.
        /// </summary>
        public static ReferralState FromDto(ReferralStateDto dto)
        {
            if (dto == null || dto.schema != Schema) return Empty;

            return new ReferralState(dto.code, dto.bound, dto.finished, dto.paid,
                                     dto.referred, dto.milestoneReached, dto.canRedeem, dto.fetchedUnix);
        }
    }
}
