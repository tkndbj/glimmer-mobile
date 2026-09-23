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
        public long feedRev;
    }

    /// <summary>
    /// Where one row of the referrer's board stands: nobody has taken that seat yet, a friend
    /// has typed the code and is still playing the milestone, or a friend has finished it.
    ///
    /// <para>
    /// The server keeps two counts and no list (invariant 51): <see cref="ReferralState.Bound"/>
    /// friends typed the code and <see cref="ReferralState.Finished"/> of them cleared the
    /// chapter. The payment is flat, so a row is a <em>seat</em> rather than a person — the
    /// finished friends fill the seats from the top, the ones still playing sit under them,
    /// and which real person is in which seat is a question nothing here needs answered.
    /// </para>
    /// </summary>
    public enum ReferralFriendStatus
    {
        /// <summary>No friend has taken this seat.</summary>
        Open,

        /// <summary>A friend typed the code and has not yet finished the milestone.</summary>
        Playing,

        /// <summary>A friend finished the milestone; the row pays.</summary>
        Finished,
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
        /// <summary>Bumped from 2 on 2026-09-22, when <see cref="FeedRev"/> joined the cache.</summary>
        public const int Schema = 3;

        /// <summary>
        /// The <see cref="FeedRev"/> of a state that was read without a listener saying where
        /// the feed stood. Never equal to anything the feed can say, so a state stamped with it
        /// is always asked about again the first time a listener does speak.
        /// </summary>
        public const long UnknownFeed = -1L;

        public static readonly ReferralState Empty = new ReferralState(
            string.Empty, 0, 0, Array.Empty<string>(), false, false, false, 0L);

        readonly string[] _paid;

        public ReferralState(string code, int bound, int finished, string[] paid,
                             bool referred, bool milestoneReached, bool canRedeem, long fetchedUnix,
                             long feedRev = UnknownFeed)
        {
            Code = code ?? string.Empty;
            Bound = bound < 0 ? 0 : bound;
            Finished = finished < 0 ? 0 : finished;
            _paid = paid ?? Array.Empty<string>();
            Referred = referred;
            MilestoneReached = milestoneReached;
            CanRedeem = canRedeem;
            FetchedUnix = fetchedUnix < 0 ? 0 : fetchedUnix;
            FeedRev = feedRev < 0 ? UnknownFeed : feedRev;
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

        /// <summary>
        /// Where the account's feed counter stood when this answer was asked for, as the
        /// listener last reported it — or <see cref="UnknownFeed"/> when no listener had
        /// spoken by then.
        ///
        /// <para>
        /// <b>This is what makes a screen open cost nothing.</b> The server bumps
        /// <c>players/{uid}/private/referral</c> inside every transaction that moves this
        /// account's referral state, and the device listens to that document. A listener's
        /// first delivery is the document as it stands, and it says nothing about whether
        /// anything moved — unless the device remembers which value its cached answer was
        /// read under. With the stamp, a delivery that matches it is proof the cached answer
        /// is still the server's answer, and no call is made. Taken at the moment the ask
        /// went out rather than when the reply landed, so a bump in between can only make
        /// the device ask once more, never miss one.
        /// </para>
        /// <para>
        /// A fact about the cache rather than about the account, which is why
        /// <see cref="Matches"/> ignores it: two answers that say the same thing about the
        /// account are the same answer whatever the feed said at the time.
        /// </para>
        /// </summary>
        public long FeedRev { get; }

        /// <summary>The same answer, stamped with the feed it was read under.</summary>
        public ReferralState WithFeed(long feedRev)
            => new ReferralState(Code, Bound, Finished, _paid, Referred, MilestoneReached, CanRedeem,
                                 FetchedUnix, feedRev);

        public bool HasPaid(string subject)
        {
            for (int i = 0; i < _paid.Length; i++)
                if (string.Equals(_paid[i], subject, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Whether this says the same thing about the account as <paramref name="other"/>.
        ///
        /// <para>
        /// <b><see cref="FetchedUnix"/> is deliberately not compared, and neither is
        /// <see cref="FeedRev"/>.</b> The first moves on every read, so a comparison including
        /// it would answer "different" every single time and be worth nothing — which is the
        /// whole reason this method exists. A server read that brings back what the device
        /// already had must be able to say so, or every visit to the invite page raises a
        /// change nobody made and the page redraws itself underneath the player. The second is
        /// a fact about when the cache was read, not about the account, and a page draws none
        /// of it.
        /// </para>
        /// <para>
        /// <b>The paid list is compared as a set, both ways.</b> The server answers an array
        /// and nothing promises its order (<c>referral.ts</c> filters rather than sorts), so
        /// comparing position by position would report a reordering as a change. Both
        /// directions rather than one plus equal lengths, because a duplicate on one side
        /// would otherwise read as a match. The list is bounded by
        /// <c>maxBound * count + count</c> server-side — about a hundred entries at the
        /// shipped ceiling — so the quadratic walk is cheap and allocates nothing.
        /// </para>
        /// </summary>
        public bool Matches(ReferralState other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;

            if (!string.Equals(Code, other.Code, StringComparison.Ordinal)) return false;
            if (Bound != other.Bound || Finished != other.Finished) return false;
            if (Referred != other.Referred || MilestoneReached != other.MilestoneReached) return false;
            if (CanRedeem != other.CanRedeem) return false;
            if (_paid.Length != other._paid.Length) return false;

            for (int i = 0; i < _paid.Length; i++)
                if (!other.HasPaid(_paid[i])) return false;
            for (int i = 0; i < other._paid.Length; i++)
                if (!HasPaid(other._paid[i])) return false;

            return true;
        }

        /// <summary>How many of a payment's chests have been paid: the count of its subjects in the list.</summary>
        public int PaidCount(ReferralClaimKind kind, int goal, int count)
        {
            int paid = 0;
            for (int n = 1; n <= count; n++)
                if (HasPaid(ReferralLanding.Subject(kind, goal, n))) paid++;
            return paid;
        }

        /// <summary>
        /// Where the <paramref name="friend"/>-th seat of the referrer's board stands, counting
        /// from one. See <see cref="ReferralFriendStatus"/>.
        ///
        /// <para>
        /// <see cref="Finished"/> is read as no more than <see cref="Bound"/>, because a server
        /// answer in which more friends finished than ever joined is one this device cannot
        /// make sense of and should draw as the smaller claim rather than as a seat that is
        /// finished and empty at once.
        /// </para>
        /// </summary>
        public ReferralFriendStatus FriendStatus(int friend)
        {
            if (friend < 1) return ReferralFriendStatus.Open;
            int finished = Finished < Bound ? Finished : Bound;
            if (friend <= finished) return ReferralFriendStatus.Finished;
            if (friend <= Bound) return ReferralFriendStatus.Playing;
            return ReferralFriendStatus.Open;
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
                feedRev = FeedRev,
            };

        /// <summary>
        /// Reads a cached copy. A schema this build does not write is read as nothing rather
        /// than as a guess: the cache is only a hint, and the next fetch replaces it whole.
        /// </summary>
        public static ReferralState FromDto(ReferralStateDto dto)
        {
            if (dto == null || dto.schema != Schema) return Empty;

            return new ReferralState(dto.code, dto.bound, dto.finished, dto.paid,
                                     dto.referred, dto.milestoneReached, dto.canRedeem, dto.fetchedUnix,
                                     dto.feedRev);
        }
    }
}
