using System;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Ads
{
    /// <summary>
    /// Where a spin's number comes from, and whether the wheel may be drawn at all.
    ///
    /// <para>
    /// <b>The index is server-owned, and that is the whole of the design.</b> A slice is a
    /// pure function of (account, day, spin index), so the client and the server agree about
    /// what a spin is worth only for as long as they agree about which spin it is. Two
    /// counters that both increment would drift the first time a verification callback was
    /// delayed past the next win — and drift here is the worst thing this feature could do:
    /// a wheel landing on five hundred while the balance rises by two.
    /// </para>
    /// <para>
    /// So there is one counter, it lives on <c>players/{uid}/private/wallet</c> where no
    /// client may write, it advances only inside the transaction that <em>grants</em> a
    /// win-bonus view, and it rides back to the phone on every wallet reply. That is
    /// <c>containersRevoked</c>'s shape exactly (invariant 18d), for the same reason: the
    /// answer has to be one nobody can forge and one a short or cold reply cannot corrupt.
    /// </para>
    /// <para>
    /// <b>Presence of the field is what says the server understands the wheel.</b> A
    /// deployment that predates it sends nothing, and a client that has heard nothing draws
    /// no wheel — it falls back to the flat offer that deployment does grant. That removes
    /// invariant 12a's deploy-ordering hazard outright rather than writing it down as a
    /// warning: shipping the client first costs a feature nobody sees yet, never a payout
    /// nobody honours.
    /// </para>
    /// <para>
    /// <b>Nothing here is stored.</b> It is a cache of the server's last answer, held for the
    /// session and rebuilt by the next sync. Writing it into the save would be a stored count
    /// in the shape invariant 11b forbids — two devices holding 3 and 5 are equally consistent
    /// with "one spun twice" and "one has not heard yet", and no rule over the pair is right.
    /// Losing it costs the wheel until the next reply, which is seconds.
    /// </para>
    /// </summary>
    public static class WheelStand
    {
        /// <summary>Raised when the stand opens, closes or moves on a spin. Screens repaint on it.</summary>
        public static event Action Changed;

        /// <summary>True once a wallet reply has carried the field, whatever it carried.</summary>
        static bool _heard;

        static int _serverDay = -1;
        static int _serverSpins;

        /// <summary>
        /// What the table says, or <see cref="BonusWheel.None"/> when content has no wheel.
        ///
        /// Read through the ad table rather than held, so a content push swaps it atomically
        /// with the payout it multiplies — the argument <c>ProgressionTable.Ads</c> already
        /// makes about publishing an ad table beside the reward curve.
        /// </summary>
        public static BonusWheel Wheel => RewardedAds.Table.Wheel;

        /// <summary>
        /// Whether the wheel may be shown instead of the flat offer.
        ///
        /// <para>
        /// Three things have to be true and each closes a different way of lying to somebody.
        /// The <b>table</b> has to carry a usable wheel, or there is nothing to draw. There has
        /// to be an <b>account</b> to seed from, because before the first sign-in the client
        /// would roll against a device id while the server re-rolled against the uid —
        /// <c>DailyChests.CanOpen</c>'s refusal, word for word. And the <b>server</b> has to
        /// have said it knows about the wheel, unless there is no backend at all, in which case
        /// nothing is adjudicated and there is nobody to disagree with.
        /// </para>
        /// <para>
        /// A closed stand is not a broken button: the caller falls back to the flat
        /// <c>AdOfferOverlay</c>, which is what the placement paid before there was a wheel and
        /// what a server in that state will still grant.
        /// </para>
        /// </summary>
        public static bool IsOpen
        {
            get
            {
                if (!Wheel.IsUsable) return false;
                if (!RewardSeed.IsReady) return false;

                return !CloudSaveService.IsAvailable || _heard;
            }
        }

        /// <summary>
        /// Which spin of today this would be — the number the slice is seeded from.
        ///
        /// <para>
        /// <b>The larger of the server's figure and this phone's own count, which is a join
        /// rather than a preference.</b> Both count the same thing — win-bonus views that were
        /// paid for today — and each can be the one that knows more. The server's is
        /// authoritative and sees every device; the phone's moves the instant a video pays,
        /// where the server's has to travel back on a sync. Taking the server's alone leaves a
        /// window between collecting a reward and hearing about it in which the wheel would show
        /// the slice that was just paid out, and a player who finishes two glades inside that
        /// window sees the same prize twice and is granted two different ones. A maximum closes
        /// it, is monotonic, and cannot go backwards — invariant 11b's shape, for a number that
        /// is not stored.
        /// </para>
        /// <para>
        /// A server day older than today means it has granted nothing today, which is the same
        /// thing as zero; a day in the future means this device's clock is behind and the
        /// server's tally is the one that governs.
        /// </para>
        /// <para>
        /// The local half is <see cref="RewardedAds.WatchedToday"/> rather than a counter of its
        /// own, and that matters — it advances only in <c>Redeem</c>, so an abandoned spin, a
        /// dismissed video and a no-fill all leave it exactly where it was. The property that
        /// falls out is the one invariant 9c wants: backing out of the panel and spinning again
        /// lands on the same slice.
        /// </para>
        /// </summary>
        public static int NextSpin
        {
            get
            {
                int local = RewardedAds.WatchedToday(AdPlacement.WinBonus);
                if (!_heard) return local;

                int today = DailyRules.DayKeyFor(GameClock.NowUnix());
                int server = _serverDay >= today ? _serverSpins : 0;

                return server > local ? server : local;
            }
        }

        /// <summary>The day the next spin belongs to, which is simply today.</summary>
        public static int DayKey => DailyRules.DayKeyFor(GameClock.NowUnix());

        /// <summary>
        /// Which slice the next spin lands on, or -1 when the stand is shut.
        ///
        /// Asked by the panel when it opens and again by nothing at all — the answer is fixed
        /// for as long as the spin is unpaid, which is exactly why it can be asked before the
        /// video rather than after it.
        /// </summary>
        public static int Landing()
            => IsOpen ? Wheel.Landing(RewardSeed.PlayerKey, DayKey, NextSpin) : -1;

        /// <summary>
        /// Adopts the server's position, from a wallet reply.
        ///
        /// <para>
        /// <paramref name="carried"/> is whether the reply had the field at all, and it is
        /// separate from the numbers on purpose: a brand-new account legitimately answers day
        /// 0, spins 0, which is indistinguishable from a deployment that has never heard of
        /// the wheel unless the presence of the key is reported in its own right. Reading the
        /// zeros as an answer would draw a wheel against a server that would grant the flat
        /// amount — the exact failure the field exists to prevent.
        /// </para>
        /// <para>
        /// Only ever moves forward. The reply is repeated on every currency row and several
        /// syncs can be in flight at once, so an older row must not walk the index back and
        /// hand somebody a slice they have already been paid for.
        /// </para>
        /// </summary>
        public static void ApplyServerState(bool carried, int dayKey, int spins)
        {
            if (!carried) return;
            if (dayKey < 0 || spins < 0) return;

            bool moved = !_heard;
            _heard = true;

            if (dayKey > _serverDay)
            {
                _serverDay = dayKey;
                _serverSpins = spins;
                moved = true;
            }
            else if (dayKey == _serverDay && spins > _serverSpins)
            {
                _serverSpins = spins;
                moved = true;
            }

            if (moved) Raise();
        }

        /// <summary>
        /// Forgets the server's answer, because the account it belonged to is no longer the
        /// one being played.
        ///
        /// <para>
        /// Called on an account switch for <c>AccountGate</c>'s reason (invariant 17): the
        /// index is a fact about one account's day, and carrying it across would seed the
        /// incoming player's first spin from the outgoing player's position. Nothing is lost —
        /// the switch is followed by a sync, and until it lands the stand is simply shut.
        /// </para>
        /// </summary>
        public static void Forget()
        {
            if (!_heard && _serverDay < 0) return;

            _heard = false;
            _serverDay = -1;
            _serverSpins = 0;
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
