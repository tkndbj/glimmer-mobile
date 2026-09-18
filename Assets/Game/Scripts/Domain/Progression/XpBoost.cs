using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds a published <c>xpBoost</c> block is checked against, and the numbers used when
    /// there is none.
    ///
    /// <see cref="EndlessLimits"/>'s job one feature over, and its argument transfers whole:
    /// content may retune what a boost is worth, it may not redefine what the ledger is allowed
    /// to hold. A limit that could itself be published would not be a limit.
    /// </summary>
    public static class XpBoostLimits
    {
        // ------------------------------------------------------- the structural bounds
        /// <summary>
        /// The most bonus XP the ledger may ever hold, whatever a content file says.
        ///
        /// <para>
        /// <b>This is not the bound that matters</b> — that one is proportional and lives in
        /// <see cref="XpBoost.BonusFrom"/> — and saying so here is the point of the constant. A
        /// flat ceiling on a stored number is a weak defence, because it has to be set high
        /// enough for the best honest player and is therefore also high enough for a forger. What
        /// really holds this down is that the bonus is clamped to a <em>fraction of XP the account
        /// can prove</em>, exactly as <c>groveWorth</c> clamps a grove to what the account could
        /// afford (invariant 19a). This is only the overflow guard behind it.
        /// </para>
        /// </summary>
        public const long HardMaxBonusXp = 1000000000L;

        /// <summary>
        /// The most a boost may add, as a percentage, however many are running at once.
        ///
        /// <b>It is also the proportional clamp's factor</b> (<see cref="XpBoost.BonusFrom"/>), so
        /// raising it widens what a forged save can claim as well as what an honest one can earn.
        /// Those are the same number on purpose: a bound derived from the rule cannot drift away
        /// from the rule.
        /// </summary>
        public const int MaxPercent = 1000;

        /// <summary>The longest window a content file may ask for. Thirty days.</summary>
        public const int MaxHours = 24 * 30;

        /// <summary>The longest cooldown a content file may ask for. Seven days.</summary>
        public const int MaxCooldownHours = 24 * 7;

        // ------------------------------------------------------------------ defaults
        /// <summary>Half again, for two hours, once every four. The watched window.</summary>
        public const int DefaultWatchedPercent = 50;
        public const int DefaultWatchedHours = 2;
        public const int DefaultWatchedCooldownHours = 4;

        /// <summary>Double, for a day. The bought window.</summary>
        public const int DefaultBoughtPercent = 100;
        public const int DefaultBoughtHours = 24;

        /// <summary>
        /// The two together, which is what a player running both actually gets.
        ///
        /// <b>The tracks add rather than the larger one winning</b>, and that is a decision with a
        /// reason: if the best won, watching an advert during a bought window would pay nothing,
        /// so the offer would have to be hidden — and an offer that is sometimes a trap is worse
        /// than one that is always worth taking. Adding also composes, which is what a third
        /// source will need.
        /// </summary>
        public const int DefaultMaxPercent = DefaultWatchedPercent + DefaultBoughtPercent;
    }

    /// <summary>
    /// What an XP boost is worth and how long it lasts — content, not code.
    ///
    /// <para>
    /// Published with the curve for <see cref="EndlessRewardTable"/>'s reason and one sharper: a
    /// multiplier on XP is the curve seen from the side, so a client holding a retuned percentage
    /// against an untuned ladder is a keeper level climbing at a speed nobody wrote down.
    /// </para>
    /// </summary>
    public sealed class XpBoostTable
    {
        XpBoostTable(int watchedPercent, int watchedHours, int watchedCooldownHours,
                     int boughtPercent, int boughtHours, int maxPercent)
        {
            WatchedPercent = watchedPercent;
            WatchedHours = watchedHours;
            WatchedCooldownHours = watchedCooldownHours;
            BoughtPercent = boughtPercent;
            BoughtHours = boughtHours;
            MaxPercent = maxPercent;
        }

        /// <summary>What a watched window adds, as a percentage. Nought withdraws it.</summary>
        public int WatchedPercent { get; }

        /// <summary>How long a watched window runs.</summary>
        public int WatchedHours { get; }

        /// <summary>
        /// How long after a watched window <em>starts</em> before another may be taken.
        ///
        /// <b>Measured from the start rather than from the end</b>, which is what lets the whole
        /// cooldown be derived from the deadline already stored (<see cref="XpBoost.WatchedReadyAt"/>)
        /// instead of costing a second field. The streak shield's trick (invariant 48c): there is
        /// one number, so the two facts it carries cannot disagree.
        /// </summary>
        public int WatchedCooldownHours { get; }

        /// <summary>What a bought window adds, as a percentage. Nought withdraws it.</summary>
        public int BoughtPercent { get; }

        /// <summary>How long a bought window runs.</summary>
        public int BoughtHours { get; }

        /// <summary>
        /// The most every running window may add together — the cap on the sum, and the factor
        /// the stored bonus is clamped against. See <see cref="XpBoostLimits.MaxPercent"/>.
        /// </summary>
        public int MaxPercent { get; }

        /// <summary>The numbers that ship inside the build, and the floor under any content mistake.</summary>
        public static readonly XpBoostTable Default = new XpBoostTable(
            XpBoostLimits.DefaultWatchedPercent,
            XpBoostLimits.DefaultWatchedHours,
            XpBoostLimits.DefaultWatchedCooldownHours,
            XpBoostLimits.DefaultBoughtPercent,
            XpBoostLimits.DefaultBoughtHours,
            XpBoostLimits.DefaultMaxPercent);

        /// <summary>Whether anything here can pay at all.</summary>
        public bool Pays => MaxPercent > 0 && (WatchedPercent > 0 || BoughtPercent > 0);

        /// <summary>Whether a watched window is offered.</summary>
        public bool OffersWatched => WatchedPercent > 0 && WatchedHours > 0;

        /// <summary>Whether a bought window is offered.</summary>
        public bool OffersBought => BoughtPercent > 0 && BoughtHours > 0;

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>xpBoost</c> block. Never throws and never returns null: anything
        /// wrong is named in <paramref name="problems"/> and the built-in numbers stand, because a
        /// content mistake must fail a build and never a session.
        ///
        /// <b>Absent falls back to the built-in figures rather than to nothing</b>, for
        /// <see cref="EndlessRewardTable.Resolve"/>'s reason exactly: a server that has not been
        /// seeded with this block would otherwise derive a lower keeper level than the device, and
        /// invariant 19a <em>drops</em> what that level gated rather than clamping it.
        /// </summary>
        public static XpBoostTable Resolve(XpBoostDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            int watchedPercent = Read(dto.watchedPercent, XpBoostLimits.DefaultWatchedPercent,
                                      0, XpBoostLimits.MaxPercent, "xpBoost watchedPercent", problems);
            int watchedHours = Read(dto.watchedHours, XpBoostLimits.DefaultWatchedHours,
                                    0, XpBoostLimits.MaxHours, "xpBoost watchedHours", problems);
            int cooldown = Read(dto.watchedCooldownHours, XpBoostLimits.DefaultWatchedCooldownHours,
                                0, XpBoostLimits.MaxCooldownHours, "xpBoost watchedCooldownHours",
                                problems);

            int boughtPercent = Read(dto.boughtPercent, XpBoostLimits.DefaultBoughtPercent,
                                     0, XpBoostLimits.MaxPercent, "xpBoost boughtPercent", problems);
            int boughtHours = Read(dto.boughtHours, XpBoostLimits.DefaultBoughtHours,
                                   0, XpBoostLimits.MaxHours, "xpBoost boughtHours", problems);

            int maxPercent = Read(dto.maxPercent, XpBoostLimits.DefaultMaxPercent,
                                  0, XpBoostLimits.MaxPercent, "xpBoost maxPercent", problems);

            // A cap under what one window already pays is not a smaller cap, it is a contradiction:
            // the window would be published at a figure the rule refuses to honour, and a player
            // would watch an advert for a number that never arrives. Raised to the larger of the
            // two rather than the block being rejected, because the author's intent is unambiguous.
            int single = watchedPercent > boughtPercent ? watchedPercent : boughtPercent;
            if (maxPercent > 0 && maxPercent < single)
            {
                problems.Add($"xpBoost maxPercent is {maxPercent}, below the {single}% a single " +
                             "window already pays; a window capped under its own figure is one a " +
                             "player is shown and never given, so the cap is raised to it");
                maxPercent = single;
            }

            // A cooldown shorter than the window it meters means a second advert may be watched
            // while the first is still running, so the windows overlap and the "every N hours"
            // the shop prints is not the rate anybody experiences. Reported rather than repaired:
            // overlapping windows are legal (they extend), and an author may want exactly that.
            if (watchedHours > 0 && cooldown > 0 && cooldown < watchedHours)
                problems.Add($"xpBoost watchedCooldownHours is {cooldown}, under the " +
                             $"{watchedHours}h window it meters; windows will overlap and extend, " +
                             "so the effective rate is higher than the cooldown suggests");

            return new XpBoostTable(watchedPercent, watchedHours, cooldown,
                                    boughtPercent, boughtHours, maxPercent);
        }

        /// <summary>
        /// One authored number: unwritten inherits, out of range is clamped and named.
        /// <c>HintRuleTable</c>'s reader, and clamped for its reason — refusing one scalar would
        /// mean discarding the whole block.
        /// </summary>
        static int Read(int authored, int fallback, int min, int max, string name, List<string> problems)
        {
            if (authored < 0) return fallback;                   // -1 is "not written"

            if (authored < min)
            {
                problems.Add($"{name} is {authored}, below the supported minimum {min}; clamped");
                return min;
            }

            if (authored > max)
            {
                problems.Add($"{name} is {authored}, above the supported maximum {max}; clamped");
                return max;
            }

            return authored;
        }
    }

    /// <summary>
    /// The XP boost: two windows, one multiplier, and the single seam every XP payment passes
    /// through.
    ///
    /// <para>
    /// <b>Why a boost on XP cannot simply multiply XP.</b> XP is <em>derived</em> — recomputed
    /// from the star ledger every time it is asked for (invariant 9) — so there is no running
    /// total to scale. Scaling the derived figure while a window is open would make a player's
    /// level <em>fall</em> when it closed, which every floor in this project exists to prevent. So
    /// the bonus has to be worked out at the moment it is earned and remembered, and what is
    /// remembered is one monotonic number joined by <c>max</c>: invariant 11b's storable-count
    /// exception, the same shape as <c>wardStars</c> and <c>endlessBest.waves</c>.
    /// </para>
    /// <para>
    /// <b>And why that is not the thing <c>AdPlacement.WinBonus</c> refused.</b> That comment
    /// rejects multiplying <em>what one run earned</em> in credits, because doing so means storing
    /// which runs were doubled — a forgeable per-level set that pays money. This stores no set and
    /// no per-level anything: it is a single total, it pays <b>XP and never currency</b> (credits
    /// still derive from the star ledger alone), and it is clamped to a fraction of XP the account
    /// can prove. A forged figure moves a keeper level inside an honest range and moves no
    /// balance, which is invariant 13's fourth clause and the same bargain 9d already makes.
    /// </para>
    /// <para>
    /// <b>The seam is <see cref="Bank"/>, and it is the only multiplier in the game.</b> Anything
    /// that pays XP hands its figure to it and is boosted for free — that is what makes a future
    /// source "just work", and it is why no other file may compute a percentage of XP.
    /// </para>
    /// </summary>
    public static class XpBoost
    {
        /// <summary>Raised when a window opens or the banked total moves, so a readout can repaint.</summary>
        public static event Action Changed;

        static XpBoostTable Table => ProgressionRules.Table.XpBoost;

        // ------------------------------------------------------------------ the windows
        /// <summary>When the watched window runs out, or 0. Monotonic; joined by <c>max</c>.</summary>
        public static long WatchedUntilUnix => Wallet.XpBoostWatchedUntilUnix;

        /// <summary>When the bought window runs out, or 0. Monotonic; joined by <c>max</c>.</summary>
        public static long BoughtUntilUnix => Wallet.XpBoostBoughtUntilUnix;

        /// <summary>
        /// What every running window adds together, as a percentage, at a given moment.
        ///
        /// Summed and then capped (<see cref="XpBoostTable.MaxPercent"/>). A percentage rather
        /// than a factor because nothing that decides a payment here may be a float — the
        /// runtimes disagree about them — so the multiply is integer throughout and the divide
        /// happens once, at the end, in <see cref="BonusOn"/>.
        /// </summary>
        public static int PercentAt(long now)
        {
            var table = Table;
            int percent = 0;

            if (WatchedUntilUnix > now) percent += table.WatchedPercent;
            if (BoughtUntilUnix > now) percent += table.BoughtPercent;

            if (percent < 0) percent = 0;
            return percent > table.MaxPercent ? table.MaxPercent : percent;
        }

        /// <summary>What every running window adds together right now.</summary>
        public static int Percent => PercentAt(GameClock.NowUnix());

        /// <summary>Whether anything is running.</summary>
        public static bool Active => Percent > 0;

        /// <summary>
        /// Seconds until every running window has closed, for a countdown. 0 when none is.
        ///
        /// The <em>later</em> of the two deadlines, because that is when the readout should stop —
        /// not when the percentage next changes. A pill that vanished at the first expiry while a
        /// boost was still running is the stale-readout fault invariant 44j is written about.
        /// </summary>
        public static long SecondsLeft
        {
            get
            {
                long now = GameClock.NowUnix();
                long until = WatchedUntilUnix > BoughtUntilUnix ? WatchedUntilUnix : BoughtUntilUnix;

                // Only counts a window this table still pays for; a percentage retuned to nought
                // withdraws the boost, and a countdown over it would be a clock on nothing.
                if (PercentAt(now) <= 0) return 0L;

                long left = until - now;
                return left < 0L ? 0L : left;
            }
        }

        // ------------------------------------------------------------- the watched window
        /// <summary>
        /// When another may be watched.
        ///
        /// <para>
        /// <b>Derived from the one stored deadline rather than costing a field of its own</b>
        /// (invariant 48c's shape): the window ran from <c>until - watchedHours</c>, so the
        /// cooldown is up at <c>until - watchedHours + cooldownHours</c>. One number, so "when
        /// does my boost end" and "when may I watch again" cannot disagree — and nothing but
        /// <see cref="GrantWatched"/> ever writes it, which is what keeps the derivation exact.
        /// </para>
        /// <para>
        /// A gift lands on the <em>bought</em> track for this reason. See <see cref="GrantBought"/>.
        /// </para>
        /// </summary>
        public static long WatchedReadyAt
        {
            get
            {
                long until = WatchedUntilUnix;
                if (until <= 0L) return 0L;                      // never watched: ready now

                var table = Table;
                return until - table.WatchedHours * 3600L + table.WatchedCooldownHours * 3600L;
            }
        }

        /// <summary>Whether another may be watched now.</summary>
        public static bool WatchedReady => WatchedReadyAt <= GameClock.NowUnix();

        /// <summary>Seconds until another may be watched. 0 when one may be taken now.</summary>
        public static long WatchedReadyInSeconds
        {
            get
            {
                long left = WatchedReadyAt - GameClock.NowUnix();
                return left < 0L ? 0L : left;
            }
        }

        /// <summary>
        /// Opens or extends the watched window.
        ///
        /// <b>Extends rather than replaces</b>, which is <c>Wallet.GrantHeartBoost</c>'s rule and
        /// for its reason: a window won while one is running must not take time away from somebody
        /// for doing well twice. The cooldown is enforced by the caller, not here — a grant that
        /// has already been paid for must never be silently dropped, which is the fault invariant
        /// 10d describes in the shape it can take on this side of the wire.
        /// </summary>
        public static void GrantWatched()
        {
            var table = Table;
            if (!table.OffersWatched) return;

            Wallet.GrantXpBoostWatched(table.WatchedHours);
            Raise();
        }

        // -------------------------------------------------------------- the bought window
        /// <summary>
        /// Opens or extends the bought window — the track with no cooldown.
        ///
        /// <b>A gift lands here rather than on the watched track</b>, and that is what keeps
        /// <see cref="WatchedReadyAt"/> honest: the cooldown is derived from the watched deadline,
        /// so anything else writing that deadline would move a cooldown it knows nothing about. A
        /// chest paying an XP boost therefore pays this one, with no cooldown, which is also what
        /// a gift should do.
        /// </summary>
        public static void GrantBought(long hours)
        {
            if (hours <= 0L) return;

            Wallet.GrantXpBoostBought(hours);
            Raise();
        }

        /// <summary>The hours one bought window runs, for a shop card that would rather not guess.</summary>
        public static int BoughtHours => Table.BoughtHours;

        // ------------------------------------------------------------------- the seam
        /// <summary>
        /// What a boost adds to a payment of <paramref name="baseXp"/>, at a given percentage.
        ///
        /// <para>
        /// <b>Integer throughout, and the divide happens once.</b> Nothing that decides a payment
        /// may be a float — .NET, Mono and IL2CPP disagree about them — and a percentage applied
        /// as <c>x * 1.5f</c> would pay a different figure on a phone than on the server. The
        /// hundredths are kept and divided at the end, which is the rule this project keeps for
        /// every factor.
        /// </para>
        /// </summary>
        public static long BonusOn(long baseXp, int percent)
        {
            if (baseXp <= 0L || percent <= 0) return 0L;
            return baseXp * percent / 100L;
        }

        /// <summary>
        /// <b>The seam.</b> Applies whatever boost is running to a payment of XP, banks the bonus
        /// and answers what was added.
        ///
        /// <para>
        /// <b>Every XP payment in the game goes through here</b>, and nothing else may multiply
        /// XP. That is the whole of what makes a future source work without being taught about
        /// boosts: it hands its figure over and the bonus arrives. <c>RunLedger.Win</c> is the
        /// only caller today, and it totals the star delta and the Infinite lane's before calling,
        /// so both are boosted by one rule rather than by two copies of one.
        /// </para>
        /// <para>
        /// Returns the bonus alone rather than the total, so a caller can print "+N" beside the
        /// base figure — a multiplier the player cannot see is one they have no reason to buy.
        /// </para>
        /// </summary>
        public static long Bank(long baseXp)
        {
            long bonus = BonusOn(baseXp, Percent);
            if (bonus <= 0L) return 0L;

            if (!Wallet.RaiseXpBoostEarned(Wallet.XpBoostEarned + bonus)) return 0L;

            Raise();
            return bonus;
        }

        // --------------------------------------------------------------- the derivation
        /// <summary>
        /// The banked bonus, clamped to what the account can prove it earned.
        ///
        /// <para>
        /// <b>This is the real bound, and it is proportional rather than flat.</b> A boost can
        /// only ever have multiplied XP that was actually paid, so a bonus above
        /// <c>provable x maxPercent%</c> is arithmetically impossible however it got into the
        /// file. That clamps a forged figure to a multiple of real progress instead of to some
        /// generous absolute ceiling — the same shape <c>groveWorth</c> uses when it clamps a
        /// grove to what the account could afford (invariant 19a), and a far stronger bound than
        /// <see cref="XpBoostLimits.HardMaxBonusXp"/> behind it.
        /// </para>
        /// <para>
        /// <paramref name="provableXp"/> is the star ledger's XP plus the Infinite lane's — every
        /// source the bonus could have been a percentage <em>of</em>. The server computes the
        /// identical clamp from the same two figures; if the two ever disagree, a published card
        /// silently drops whatever the lower keeper level gated (19a).
        /// </para>
        /// </summary>
        public static long BonusFrom(long provableXp)
        {
            long held = Wallet.XpBoostEarned;
            if (held <= 0L || provableXp <= 0L) return 0L;

            long ceiling = BonusOn(provableXp, Table.MaxPercent);
            if (held > ceiling) held = ceiling;

            return held > XpBoostLimits.HardMaxBonusXp ? XpBoostLimits.HardMaxBonusXp : held;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }
    }
}
