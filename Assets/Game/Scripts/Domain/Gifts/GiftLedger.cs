using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Cloud;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Gifts
{
    /// <summary>What one Claim granted on this device, summed over the gifts it took.</summary>
    public readonly struct GiftClaim
    {
        public readonly long Credits;
        public readonly long Gems;
        public readonly int Gifts;

        public GiftClaim(long credits, long gems, int gifts)
        {
            Credits = credits < 0 ? 0 : credits;
            Gems = gems < 0 ? 0 : gems;
            Gifts = gifts < 0 ? 0 : gifts;
        }

        public bool Any => Credits > 0 || Gems > 0;
    }

    /// <summary>
    /// The admin gifts waiting on this account (invariant 63): what the server last said is owed,
    /// which of it this device has already claimed, and the claim.
    ///
    /// <para>
    /// <b>What is owed is the server's</b> (<c>gifts</c> on every wallet reply), and nothing here
    /// is in the save. Until a reply has said what this account is owed, nothing is waiting
    /// (<see cref="Known"/>): a reinstall or a fresh account cannot invent a gift, and an account
    /// switch forgets the last one's list.
    /// </para>
    /// <para>
    /// <b>A claim is the daily chest's shape</b> (<c>PlayerProgression.Award</c>): each currency the
    /// gift still carries is queued under <see cref="GrantEntry.GiftId"/>, counts toward the balance
    /// at once - so the coins fly into the readout and are spendable on the spot, offline included -
    /// and is resubmitted on every sync until the server confirms it, paying the amount <em>the
    /// wallet holds</em> and never this device's figure. A second phone claiming the same gift
    /// collides with the server's grant record and is confirmed without being paid twice; the
    /// ledger's own union does the same for two queues merging.
    /// </para>
    /// <para>
    /// <b>"Claimed" is derived, never stored.</b> A gift is <em>waiting</em> while some currency it
    /// still carries has no award queued under its id on this device. Once the server confirms the
    /// award it drops the gift from the list on the same reply, so the queue empties and the list
    /// empties together; a reply that still lists a gift this device has queued (a wallet read that
    /// raced the claim) changes nothing. The one way a claimed gift comes back is the server
    /// refusing the award - a forged id, nothing a real gift can do - in which case the ledger
    /// drops the award with the balance it inflated (45d) and whatever the server still lists is
    /// offered again. Nothing can be lost in between.
    /// </para>
    /// </summary>
    public static class GiftLedger
    {
        /// <summary>Raised when what is owed, or what is claimed, changes. Main thread.</summary>
        public static event Action Changed;

        static List<AdminGift> _pending = new List<AdminGift>();

        /// <summary>Whether a wallet reply has said what this account is owed, since the last account change.</summary>
        static bool _known;

        static bool _hooked;

        static GiftLedger() => Hook();

        /// <summary>Installs the account listener. Idempotent (<c>DealLedger.Hook</c>'s reason).</summary>
        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            CloudSaveService.IdentityChanged += OnIdentityChanged;
        }

        // ---------------------------------------------------------------- reading
        /// <summary>A wallet reply has said what this account is owed.</summary>
        public static bool Known => _known;

        /// <summary>Every gift the last reply listed, oldest first, claimed or not.</summary>
        public static IReadOnlyList<AdminGift> Pending => _pending;

        /// <summary>
        /// The gifts to put in front of the player: listed by the server, with something this device
        /// has not yet claimed. Empty until the server has spoken.
        /// </summary>
        public static List<AdminGift> Waiting => _known ? WaitingOf(_pending, IsHeld) : new List<AdminGift>();

        /// <summary>
        /// Which of <paramref name="pending"/> still waits: a gift waits when some currency it carries
        /// is not <paramref name="held"/> - queued or confirmed - on this device. Pure, for the tests.
        /// </summary>
        public static List<AdminGift> WaitingOf(IReadOnlyList<AdminGift> pending, Func<string, string, bool> held)
        {
            var waiting = new List<AdminGift>();
            if (pending == null) return waiting;

            foreach (var gift in pending)
            {
                if (gift == null || !gift.Any) continue;

                bool owed = false;
                foreach (string currency in AdminGifts.Currencies)
                    if (gift.Of(currency) > 0 && (held == null || !held(gift.Id, currency))) { owed = true; break; }

                if (owed) waiting.Add(gift);
            }
            return waiting;
        }

        /// <summary>This device has queued the award for one currency of a gift, and the server has not yet answered it.</summary>
        public static bool IsHeld(string giftId, string currency)
        {
            if (string.IsNullOrEmpty(giftId)) return false;
            var ledger = Wallet.Ledger(currency);
            return ledger != null && ledger.HasGranted(GrantEntry.GiftId(giftId, currency));
        }

        // ---------------------------------------------------------------- claiming
        /// <summary>
        /// Claims every currency of every gift given: each queued under its derived id and counted
        /// at once, then a sync asked for now - the coins are on screen and the server is the only
        /// thing that can make them stay. A currency already queued is skipped (the ledger refuses a
        /// held id), so pressing Claim on two phones, or twice, pays once.
        /// </summary>
        public static GiftClaim Claim(IReadOnlyList<AdminGift> gifts)
        {
            if (gifts == null || gifts.Count == 0) return new GiftClaim(0L, 0L, 0);

            long now = GameClock.NowUnix();
            long credits = 0L, gems = 0L;
            int taken = 0;

            foreach (var gift in gifts)
            {
                if (gift == null || !AdminGifts.IsId(gift.Id)) continue;
                bool any = false;

                if (gift.Credits > 0 && PlayerProgression.Award(Currency.Credits, gift.Credits,
                                                                GrantEntry.GiftId(gift.Id, Currency.Credits),
                                                                GrantEntry.GiftReason, now))
                {
                    credits += gift.Credits;
                    any = true;
                }

                if (gift.Gems > 0 && PlayerProgression.Award(Currency.Gems, gift.Gems,
                                                             GrantEntry.GiftId(gift.Id, Currency.Gems),
                                                             GrantEntry.GiftReason, now))
                {
                    gems += gift.Gems;
                    any = true;
                }

                if (any) taken++;
            }

            var claim = new GiftClaim(credits, gems, taken);
            if (!claim.Any) return claim;

            CloudSaveService.RequestSync();
            Telemetry.Track("gift_claimed", "gifts", taken, "credits", credits, "gems", gems);
            Raise();
            return claim;
        }

        // ---------------------------------------------------------------- the server
        /// <summary>
        /// Folds in what the server says is owed. <paramref name="carried"/> is asked apart from the
        /// list for <c>DealLedger.ApplyServerState</c>'s reason: a deployment that predates gifts
        /// sends nothing, an account owed nothing sends an empty list, and only the second may
        /// clear what this device was last told.
        /// </summary>
        public static void ApplyServerState(bool carried, IReadOnlyList<CloudGiftRow> rows)
        {
            if (!carried) return;

            bool changed = !_known;
            _known = true;

            var incoming = AdminGifts.Read(rows);
            if (!AdminGifts.Same(incoming, _pending))
            {
                _pending = incoming;
                changed = true;
            }

            if (changed) Raise();
        }

        /// <summary>What is owed belongs to the account just left; the next wallet reply says the new one's.</summary>
        internal static void OnIdentityChanged()
        {
            if (!_known && _pending.Count == 0) return;
            _known = false;
            _pending = new List<AdminGift>();
            Raise();
        }

        /// <summary>For tests: forgets everything, as a fresh process would.</summary>
        internal static void ResetForTests()
        {
            _pending = new List<AdminGift>();
            _known = false;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
