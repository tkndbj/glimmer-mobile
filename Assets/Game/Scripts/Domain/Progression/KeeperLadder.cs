using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds a published <c>keeperLevels</c> block is checked against. Mirrored by
    /// <c>seed-config.mjs</c> and <c>content.py</c>, so a block one side accepts the other does too.
    /// </summary>
    public static class KeeperLadderLimits
    {
        /// <summary>The first level anybody can buy - nobody starts below 1.</summary>
        public const int LowestLevel = 2;

        /// <summary>The most the ladder may reach. Well under the curve's own cap.</summary>
        public const int MaxTop = 200;

        /// <summary>The most corners a ladder may have. A shape, not a table.</summary>
        public const int MaxAnchors = 16;

        /// <summary>The most one level may cost, in either currency.</summary>
        public const int MaxPrice = 10_000_000;
    }

    /// <summary>
    /// One corner of the keeper price ladder, as resolved. See <see cref="KeeperLadder"/>.
    /// </summary>
    public readonly struct KeeperAnchor
    {
        public readonly int Level;
        public readonly string Currency;
        public readonly int Price;

        public KeeperAnchor(int level, string currency, int price)
        {
            Level = level;
            Currency = currency;
            Price = price;
        }
    }

    /// <summary>
    /// What buying a keeper level costs - the rule behind invariant 57.
    ///
    /// <para>
    /// <b>A keeper level is derived from XP, and XP is derived from play</b> (invariant 9). A
    /// bought level is the one addend on top of that which is neither: it is an
    /// <em>entitlement</em>, a count of purchases, stored on the wallet document no client can
    /// write and mirrored into the save as a monotonic integer joined by <c>max</c>
    /// (<c>WalletDto.keeperLevelsBought</c>). The effective level a player stands at is the
    /// earned level plus that count, everywhere in the game - every gate, every honorific, the
    /// published card and the public profile - with exactly one exception: <b>the rank ladder
    /// reads the earned level alone</b> (<c>LiveRankSource.KeeperLevel</c>, <c>rungOf</c>),
    /// because a badge is a reading of play and a badge for sale is worth nothing to the people
    /// who earned theirs.
    /// </para>
    /// <para>
    /// <b>The price is a function of the level reached</b>, drawn between a few anchors: on the
    /// straight line between two anchors of one currency, flat at the lower anchor between two
    /// of different currencies. The arithmetic is integer and rounds half up, and it exists three
    /// times - here, in <c>keeper.ts</c>, and in <c>Tools/make_keeper_vectors.py</c>, which writes
    /// the vectors both runtimes are held to. A drift is a purchase refused as underpaid, which
    /// the client drops with its level, so it is loud rather than silent; the vectors are there
    /// so it is never seen at all.
    /// </para>
    /// <para>
    /// <b>Absent sells nothing.</b> Unlike the XP blocks, which fall back to built-in figures so a
    /// stale server agrees with the client, a ladder the server has not been told about must not
    /// be sold on the device: the debit would be refused and the level taken back on the next
    /// sync, which is money shown and taken away. So both sides read absence as "not for sale",
    /// and the screen says so rather than offering a price.
    /// </para>
    /// </summary>
    public sealed class KeeperLadder
    {
        readonly KeeperAnchor[] _anchors;

        KeeperLadder(int top, KeeperAnchor[] anchors)
        {
            Top = top;
            _anchors = anchors ?? Array.Empty<KeeperAnchor>();
        }

        /// <summary>The ladder that sells nothing - the default, and what a broken block resolves to.</summary>
        public static readonly KeeperLadder Empty = new KeeperLadder(0, Array.Empty<KeeperAnchor>());

        /// <summary>The highest level sold. Nought when nothing is.</summary>
        public int Top { get; }

        /// <summary>The corners, lowest level first. Empty when nothing is sold.</summary>
        public IReadOnlyList<KeeperAnchor> Anchors => _anchors;

        /// <summary>Whether any level is for sale at all.</summary>
        public bool Sells => Top >= KeeperLadderLimits.LowestLevel && _anchors.Length > 0;

        /// <summary>The lowest level with a price, or 0 when nothing is sold.</summary>
        public int First => _anchors.Length > 0 ? _anchors[0].Level : 0;

        /// <summary>
        /// What reaching <paramref name="level"/> costs, or false when that level is not sold -
        /// below the first anchor, above <see cref="Top"/>, or on a ladder that sells nothing.
        /// </summary>
        public bool PriceFor(int level, out string currency, out long price)
        {
            currency = null;
            price = 0L;
            if (!Sells || level < First || level > Top) return false;

            // The anchor at or below the level, and the one above it. The last anchor *is* the
            // top, so a level equal to it lands on the last anchor exactly.
            int i = 0;
            while (i + 1 < _anchors.Length && _anchors[i + 1].Level <= level) i++;

            var lower = _anchors[i];
            currency = lower.Currency;

            if (i + 1 >= _anchors.Length || lower.Level == level)
            {
                price = lower.Price;
                return true;
            }

            var upper = _anchors[i + 1];
            if (!string.Equals(lower.Currency, upper.Currency, StringComparison.Ordinal))
            {
                // A hand-over between currencies: the lower band's last figure holds until the
                // next band begins. There is no honest way to interpolate coins into gems.
                price = lower.Price;
                return true;
            }

            price = Between(lower.Price, upper.Price, level - lower.Level, upper.Level - lower.Level);
            return true;
        }

        /// <summary>
        /// The point <paramref name="step"/> of <paramref name="steps"/> along the line from
        /// <paramref name="a"/> to <paramref name="b"/>, rounded half up, in integers.
        ///
        /// Written out once so the three copies can be compared line for line: the numerator is
        /// doubled and the denominator added before one division, which is round-half-up with no
        /// float anywhere in it. <paramref name="b"/> is never below <paramref name="a"/> - the
        /// reader refuses a band whose price falls - so the numerator is never negative and the
        /// division truncates toward the answer rather than away from it.
        /// </summary>
        public static long Between(long a, long b, long step, long steps)
        {
            if (steps <= 0L) return a;
            long num = (b - a) * step;
            return a + (2L * num + steps) / (2L * steps);
        }

        /// <summary>
        /// The level a player stands at: the earned level plus what they bought, never past the
        /// curve's own cap. The one place the two are added, so nothing else has to know a bought
        /// level exists (invariant 57).
        /// </summary>
        public static PlayerLevel Compose(PlayerLevel earned, int bought, int maxLevel)
        {
            if (bought <= 0) return earned;

            long level = (long)earned.Level + bought;
            if (level > maxLevel) level = maxLevel;

            return new PlayerLevel((int)level, earned.TotalXp, earned.XpIntoLevel,
                                   earned.XpForNextLevel, earned.IsMaxLevel);
        }

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>keeperLevels</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the ladder resolves to
        /// <see cref="Empty"/>, because a price ladder with a hole in it is a purchase refused by
        /// the server and taken back - so the whole block is refused rather than one corner
        /// repaired. A content mistake fails a build and never a session.
        /// </summary>
        public static KeeperLadder Resolve(KeeperLadderDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || !dto.IsAuthored) return Empty;             // absent sells nothing

            int top = dto.top;
            if (top < KeeperLadderLimits.LowestLevel || top > KeeperLadderLimits.MaxTop)
            {
                problems.Add($"keeperLevels top is {top}; it must be between " +
                             $"{KeeperLadderLimits.LowestLevel} and {KeeperLadderLimits.MaxTop}");
                return Empty;
            }

            var rows = dto.anchors;
            if (rows == null || rows.Length == 0)
            {
                problems.Add("keeperLevels has a top and no anchors, so no level has a price");
                return Empty;
            }
            if (rows.Length > KeeperLadderLimits.MaxAnchors)
            {
                problems.Add($"keeperLevels lists {rows.Length} anchors; at most " +
                             $"{KeeperLadderLimits.MaxAnchors} are supported");
                return Empty;
            }

            var anchors = new KeeperAnchor[rows.Length];
            bool ok = true;

            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                string where = $"keeperLevels anchor {i}";

                if (row == null) { problems.Add($"{where} is empty"); ok = false; continue; }

                if (row.level < KeeperLadderLimits.LowestLevel || row.level > top)
                {
                    problems.Add($"{where} prices level {row.level}, outside " +
                                 $"{KeeperLadderLimits.LowestLevel}..{top}");
                    ok = false;
                }
                if (i > 0 && rows[i - 1] != null && row.level <= rows[i - 1].level)
                {
                    problems.Add($"{where} prices level {row.level} after level {rows[i - 1].level}; " +
                                 "anchors must climb");
                    ok = false;
                }
                if (row.currency != Currency.Credits && row.currency != Currency.Gems)
                {
                    problems.Add($"{where} is priced in '{row.currency}'; only " +
                                 $"'{Currency.Credits}' and '{Currency.Gems}' are spent");
                    ok = false;
                }
                if (row.price < 1 || row.price > KeeperLadderLimits.MaxPrice)
                {
                    problems.Add($"{where} costs {row.price}; a price is 1..{KeeperLadderLimits.MaxPrice}");
                    ok = false;
                }
                if (i > 0 && rows[i - 1] != null && ok
                    && string.Equals(row.currency, rows[i - 1].currency, StringComparison.Ordinal)
                    && row.price < rows[i - 1].price)
                {
                    // A band that gets cheaper as it climbs is a ladder somebody buys from the top
                    // down; refused rather than sorted, because the author's intent is unclear.
                    problems.Add($"{where} costs {row.price} after {rows[i - 1].price} in the same " +
                                 "currency; a band never gets cheaper as it climbs");
                    ok = false;
                }

                anchors[i] = new KeeperAnchor(row.level, row.currency, row.price);
            }

            if (ok && anchors[anchors.Length - 1].Level != top)
            {
                problems.Add($"keeperLevels top is {top} but the last anchor prices level " +
                             $"{anchors[anchors.Length - 1].Level}; the ladder would have to guess " +
                             "past its own last figure, so the last anchor must be the top");
                ok = false;
            }

            return ok ? new KeeperLadder(top, anchors) : Empty;
        }
    }
}
