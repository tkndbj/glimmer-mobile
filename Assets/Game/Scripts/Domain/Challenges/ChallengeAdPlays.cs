using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Challenges
{
    /// <summary>
    /// How many extra challenge plays the <c>challenge_play</c> advert has earned this account
    /// today, as the server last said.
    ///
    /// <para>
    /// <b>The count is the server's and this is a copy of it.</b> A won play is a coin claim, and
    /// the server bounds every coin claim by the plays it knows the day allowed (56i). A play the
    /// device granted for itself the moment a video closed would be a play the server had not yet
    /// heard of - and if the network's callback never came, its win would be refused once the day
    /// closed and the coins drawn for it taken back (45d). So the signed callback raises the count
    /// on the wallet document no client can write (<c>challengeAds</c>, <c>challenges.ts</c>),
    /// capped at the published allowance, the wallet reply carries it back, and this holds the
    /// answer. The price is a few seconds between the video closing and the play arriving, which
    /// <c>AdGrantWatch</c> keeps short (invariant 10d: an advert is granted by the network's
    /// callback, never claimed by the client).
    /// </para>
    /// <para>
    /// <b>A per-day figure, so it is not in the save</b> - <see cref="EndlessCoins"/>' argument
    /// word for word: a count that resets at midnight cannot be joined by a merge (11b), and
    /// keeping it off the wire costs no schema version, no <c>hasOnly</c> line and no rules
    /// release. It is kept per account in the same device store the lane's tally uses, so an
    /// offline relaunch still knows what the day was given and a switch reads the other
    /// account's slot rather than inheriting this one's (invariant 17).
    /// </para>
    /// <para>
    /// <b>A forged copy buys nothing.</b> It offers plays; a play pays nothing until it is won,
    /// and the coins for a win past the day's own allowance are priced against the server's
    /// count, never this one.
    /// </para>
    /// </summary>
    public static class ChallengeAdPlays
    {
        const string Key = "glimmer.challenge.adplays";

        /// <summary>What <c>CloudSaveService.AwaitAdGrant</c> is asked to wait on for a play.</summary>
        public const string WatchKey = AdPlacement.ChallengePlay;

        /// <summary>Raised when the day's count moved, so a page drawing plays repaints.</summary>
        public static event Action Changed;

        /// <summary>Extra plays the adverts have earned today, every genre sharing them.</summary>
        public static int GrantedToday
        {
            get
            {
                Read(out int day, out int granted);
                return day == ChallengeCalendar.Today() ? granted : 0;
            }
        }

        /// <summary>
        /// Folds in the server's figure: the day it is counting and what it has granted in it.
        ///
        /// <para>
        /// <b>Upward only within a day, and a later day outright</b>, for
        /// <see cref="EndlessCoins.ApplyServerState"/>'s reason: a reply is a snapshot, and one
        /// taken before the last callback landed must not take back a play already shown. The
        /// server's count never falls within a day, so a smaller figure is always an older one.
        /// </para>
        /// <para>
        /// <b>Carried is asked separately from the number</b>, because a fresh day honestly
        /// answers nought and a deployment that predates the field sends nothing.
        /// </para>
        /// </summary>
        public static void ApplyServerState(bool carried, int dayKey, int granted)
        {
            if (!carried || dayKey < 0 || granted < 0) return;
            if (granted > AdRules.MaxDailyCap) granted = AdRules.MaxDailyCap;

            Read(out int day, out int mine);

            if (dayKey > day || (dayKey == day && granted > mine))
            {
                Write(dayKey, granted);
                Raise();
            }
        }

        /// <summary>
        /// Grants plays with nobody to ask, for a build with no cloud backend at all: then no
        /// callback can ever be counted and there is no server to disagree with, which is the
        /// gate <c>RewardedAds.CanAdjudicate</c> lifts for the same case. Never called when a
        /// backend exists.
        /// </summary>
        internal static void GrantLocally(int plays)
        {
            if (plays <= 0) return;

            int today = ChallengeCalendar.Today();
            Read(out int day, out int granted);
            if (day != today) granted = 0;

            int next = granted + plays;
            if (next > AdRules.MaxDailyCap) next = AdRules.MaxDailyCap;

            Write(today, next);
            Raise();
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ the store
        /// <summary>One slot per account, for the reason the lane's tally has one.</summary>
        static string Slot()
        {
            string uid = CloudState.UserId ?? string.Empty;
            return uid.Length == 0 ? Key : Key + ":" + uid;
        }

        /// <summary><c>{dayKey}:{granted}</c>; anything unreadable reads as nothing granted.</summary>
        static void Read(out int day, out int granted)
        {
            day = 0;
            granted = 0;

            string raw = EndlessCoins.Store.Read(Slot());
            if (string.IsNullOrEmpty(raw)) return;

            int split = raw.IndexOf(':');
            if (split <= 0 || split >= raw.Length - 1) return;

            if (!int.TryParse(raw.Substring(0, split), out day)) { day = 0; return; }
            if (!int.TryParse(raw.Substring(split + 1), out granted)) { day = 0; granted = 0; }

            if (granted < 0) granted = 0;
        }

        static void Write(int day, int granted)
            => EndlessCoins.Store.Write(Slot(), day + ":" + granted);

        /// <summary>Test seam: forgets this account's day.</summary>
        internal static void ResetForTests() => EndlessCoins.Store.Delete(Slot());
    }
}
