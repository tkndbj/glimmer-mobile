using System;
using UnityEngine;
using GlimmerGrove.Cloud;
using GlimmerGrove.Daily;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What the Infinite lane pays in <em>credits</em>: a rate per wave, bounded by a ceiling
    /// per day.
    ///
    /// <para>
    /// <b>A claim rather than a derivation, and that is the one thing separating this from the
    /// XP beside it.</b> Endless XP is a pure function of a monotonic lifetime tally
    /// (<see cref="EndlessRewardTable.XpFor"/>, invariant 9d) and needs no claim, no counter and
    /// no merge rule, which is why invariant 14 prefers that shape. Credits cannot copy it. A
    /// lifetime tally times a credit rate is a number a forged save mints once and keeps — at
    /// the XP ceiling that is three million credits, which is the colour shelf and the legendary
    /// band together. Invariant 19l says it plainly: the day the endless board pays anything,
    /// the old argument stops being defensible.
    /// </para>
    /// <para>
    /// <b>So the payment falls to invariant 13's fourth clause — bound it so tightly that
    /// forging buys nothing.</b> The bound is a day: however many waves a save claims, the lane
    /// pays at most <see cref="EndlessRewardTable.DailyCreditCap"/> between one midnight and the
    /// next, so a cheater is paid exactly what somebody who played a full evening is paid. That
    /// is the whole of the security, and it is why the cap is a figure about <em>money</em>
    /// rather than about waves: a cap on waves is minted again by every replayed run.
    /// </para>
    /// <para>
    /// <b>The ceiling that counts is the server's.</b> This class keeps a device-local tally so
    /// the hub can stop offering money the server would refuse and so a player is never shown a
    /// payment they do not receive — it is a hint in exactly the sense invariant 8b means one,
    /// and it is deliberately <em>not</em> in the save. A per-day figure resets, and anything a
    /// merge touches has to be monotonic (invariant 11b), so a daily counter on the wire would
    /// be the one shape that cannot be joined. Keeping it off the wire also costs no schema
    /// version, no <c>hasOnly</c> line and no rules release (invariant 12a).
    /// </para>
    /// <para>
    /// <b>Wiping this tally buys a cheater nothing</b>, which is what makes a device-local bound
    /// acceptable here: the real ceiling is held against the wallet document the server owns, so
    /// a reinstalled hint simply produces claims the server declines to pay.
    /// </para>
    /// </summary>
    public static class EndlessCoins
    {
        /// <summary>
        /// Where the day's tally lives, per account.
        ///
        /// <b>Per account rather than per device</b>, because a switch is finished locally
        /// (invariant 17a) and a shared ceiling would let one account spend another's day.
        /// </summary>
        const string Key = "glimmer.endless.coins";

        static EndlessRewardTable Table => ProgressionRules.Table.Endless;

        /// <summary>The credits this account has already been paid by the lane today.</summary>
        public static int PaidToday
        {
            get
            {
                Read(out int day, out int paid);
                return day == DailyRules.DayKeyFor(GameClock.NowUnix()) ? paid : 0;
            }
        }

        /// <summary>What the lane could still pay this account today. Nought once it is spent.</summary>
        public static int RemainingToday
        {
            get
            {
                if (!Table.PaysCredits) return 0;

                int left = Table.DailyCreditCap - PaidToday;
                return left > 0 ? left : 0;
            }
        }

        /// <summary>What one run's waves would be worth right now, the day's ceiling applied.</summary>
        public static int Worth(int waves) => Table.CreditsFor(waves, PaidToday);

        /// <summary>
        /// Pays a finished run and answers what it was worth.
        ///
        /// <para>
        /// <b>The id is derived from the day and from what the day had already paid</b>, which is
        /// invariant 10a's rule and is doing three jobs here. Two devices that bank the same run
        /// produce the same string and are paid once. A resubmission confirms rather than paying
        /// again. And the running total is <em>in</em> the id, so the server can bound a claim by
        /// reading it rather than by trusting a total sent beside it.
        /// </para>
        /// <para>
        /// <b>The tally moves before the claim and never after.</b> A claim is banked into the
        /// save and flushed by <c>PlayerProgression.Award</c>; if that call throws or the process
        /// dies between the two, a tally already advanced pays the player too little once, and a
        /// tally advanced afterwards pays them the same money twice under a fresh id. Too little
        /// once is the direction to be wrong in.
        /// </para>
        /// </summary>
        public static int Bank(int waves)
        {
            if (waves <= 0 || !Table.PaysCredits) return 0;

            long now = GameClock.NowUnix();
            int today = DailyRules.DayKeyFor(now);

            Read(out int day, out int paid);
            if (day != today) paid = 0;

            int amount = Table.CreditsFor(waves, paid);
            if (amount <= 0) return 0;

            Write(today, paid + amount);

            string id = GrantEntry.EndlessWavesId(today, paid, Currency.Credits);
            PlayerProgression.Award(Currency.Credits, amount, id, GrantEntry.EndlessWavesReason, now);

            return amount;
        }

        /// <summary>
        /// Folds in what the server says this account has already been paid today.
        ///
        /// <para>
        /// <b>This is what makes the ceiling cross-device, and without it the lane had a way to
        /// show money and then take it back.</b> The bound was always enforced on the server, but
        /// each device counted the day for itself — so a second phone, or a reinstall, would
        /// offer credits the ceiling had already paid out, and a claim refused for that reason
        /// is dropped by the client <em>together with the balance it inflated</em> (invariant
        /// 45d). The player saw six hundred credits arrive and vanish, having done nothing wrong.
        /// </para>
        /// <para>
        /// <b>The same shape the bonus wheel uses</b> (<c>WheelStand.ApplyServerState</c>), for
        /// the same reason: a per-day figure every device has to agree about lives on the wallet
        /// document no client can write and rides back on the reply.
        /// </para>
        /// <para>
        /// <b>It only ever moves the tally up.</b> A reply is a snapshot taken before this
        /// device's latest run was banked, so taking the server's figure whole would forget a
        /// claim already in flight and offer its money a second time. Later day wins outright;
        /// the same day takes the larger of the two.
        /// </para>
        /// <para>
        /// <b>Carried is asked separately from the number</b>, because a fresh account honestly
        /// answers nought and a deployment that predates the field also sends nothing — and only
        /// one of those two is something to believe.
        /// </para>
        /// </summary>
        public static void ApplyServerState(bool carried, int dayKey, int paid)
        {
            if (!carried || dayKey < 0 || paid < 0) return;

            Read(out int day, out int mine);

            if (dayKey > day) { Write(dayKey, paid); return; }
            if (dayKey == day && paid > mine) Write(dayKey, paid);
        }

        /// <summary>
        /// Forgets the tally, because the account it belonged to is no longer the one being
        /// played.
        ///
        /// Called on an account switch for <c>WheelStand.Forget</c>'s reason (invariant 17): the
        /// day is a fact about one account, and carrying it across would spend the incoming
        /// player's ceiling out of the outgoing player's evening. Nothing is lost - the switch is
        /// followed by a sync, and the server's figure arrives with it.
        /// </summary>
        public static void Forget() => PlayerPrefs.DeleteKey(Slot());

        // ------------------------------------------------------------------ the store
        /// <summary>
        /// One key per account, because the tally is about an account rather than about a phone.
        /// </summary>
        static string Slot()
        {
            string uid = CloudState.UserId ?? string.Empty;
            return uid.Length == 0 ? Key : Key + ":" + uid;
        }

        /// <summary>
        /// <c>{dayKey}:{paid}</c>, and an unreadable value reads as a fresh day.
        ///
        /// <b>Never throws and never fails closed.</b> A hint that cannot be parsed must not stop
        /// the lane paying — the server is the ceiling, so the worst a lost tally does is let a
        /// device ask for money it will not be given, which the wallet then declines.
        /// </summary>
        static void Read(out int day, out int paid)
        {
            day = 0;
            paid = 0;

            string raw = PlayerPrefs.GetString(Slot(), string.Empty);
            if (string.IsNullOrEmpty(raw)) return;

            int split = raw.IndexOf(':');
            if (split <= 0 || split >= raw.Length - 1) return;

            if (!int.TryParse(raw.Substring(0, split), out day)) { day = 0; return; }
            if (!int.TryParse(raw.Substring(split + 1), out paid)) { day = 0; paid = 0; }

            if (paid < 0) paid = 0;
        }

        static void Write(int day, int paid)
            => PlayerPrefs.SetString(Slot(), day + ":" + paid);
    }
}
