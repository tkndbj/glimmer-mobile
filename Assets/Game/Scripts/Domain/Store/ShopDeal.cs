using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// One limited-time shop deal (invariant 60): a number of coins sold for a number of gems,
    /// on sale from <see cref="StartUnix"/> until <see cref="EndUnix"/>, once per account.
    ///
    /// <para>
    /// <b>Made on the admin page, never authored in a content file.</b> A deal is a decision the
    /// owner takes on a Tuesday, not a build; it is published in one public document
    /// (<c>config/deals</c>) that the server prices every debit off and the game reads once in a
    /// while. So nothing here is a loc key or an address: a deal draws itself from its two
    /// amounts, the clock and the art the shop already ships.
    /// </para>
    /// </summary>
    public sealed class ShopDeal
    {
        public readonly string Id;
        public readonly long Credits;
        public readonly long Gems;
        public readonly long StartUnix;
        public readonly long EndUnix;

        public ShopDeal(string id, long credits, long gems, long startUnix, long endUnix)
        {
            Id = id;
            Credits = credits;
            Gems = gems;
            StartUnix = startUnix;
            EndUnix = endUnix;
        }

        /// <summary>On sale: the window has opened and not closed.</summary>
        public bool IsLive(long nowUnix) => nowUnix >= StartUnix && nowUnix < EndUnix;

        /// <summary>Seconds until it closes, never negative.</summary>
        public long SecondsLeft(long nowUnix) => EndUnix > nowUnix ? EndUnix - nowUnix : 0L;

        public bool SameAs(ShopDeal other)
            => other != null && Id == other.Id && Credits == other.Credits && Gems == other.Gems
            && StartUnix == other.StartUnix && EndUnix == other.EndUnix;
    }

    /// <summary>
    /// Reads the published document, and the rules a deal must satisfy to be read at all. The
    /// server's half is <c>firebase/functions/src/deals.ts</c>, and the limits here mirror it, so
    /// a row the server would refuse to price is a row this device never offers.
    /// </summary>
    public static class ShopDeals
    {
        /// <summary>The document's shape version (<c>DEALS_SCHEMA</c>). Any other lists nothing.</summary>
        public const int Schema = 1;

        /// <summary><c>DEAL_MAX_CREDITS</c>.</summary>
        public const long MaxCredits = 10_000_000L;

        /// <summary><c>DEAL_MAX_GEMS</c>.</summary>
        public const long MaxGems = 100_000L;

        /// <summary>
        /// Whether a string is a deal id the server could have minted: <c>d</c>, twelve digits
        /// (the UTC minute it opened) and four of <c>a-z0-9</c>. Written out rather than a
        /// pattern so the one place it is asked costs no allocation.
        /// </summary>
        public static bool IsId(string id)
        {
            if (id == null || id.Length != 17 || id[0] != 'd') return false;
            for (int i = 1; i <= 12; i++) if (id[i] < '0' || id[i] > '9') return false;
            for (int i = 13; i < 17; i++)
            {
                char c = id[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
            }
            return true;
        }

        /// <summary>
        /// The deals a document lists, as the Firestore SDK hands it over. A malformed row is
        /// dropped rather than failing the document - the server's rule - and a document of
        /// another schema lists nothing.
        /// </summary>
        public static List<ShopDeal> Read(IDictionary<string, object> document)
        {
            var deals = new List<ShopDeal>();
            if (document == null) return deals;
            if (!document.TryGetValue("schema", out object schema) || Long(schema) != Schema) return deals;
            if (!document.TryGetValue("deals", out object raw) || !(raw is IEnumerable<object> rows)) return deals;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!(row is IDictionary<string, object> map)) continue;
                var deal = ReadRow(map);
                if (deal == null || !seen.Add(deal.Id)) continue;
                deals.Add(deal);
            }
            return deals;
        }

        /// <summary>One row, or null when any field is wrong. A deal is all or nothing.</summary>
        public static ShopDeal ReadRow(IDictionary<string, object> row)
        {
            if (row == null) return null;

            string id = row.TryGetValue("id", out object rawId) ? rawId as string : null;
            if (!IsId(id)) return null;

            long credits = Field(row, "credits"), gems = Field(row, "gems");
            long start = Field(row, "startUnix"), end = Field(row, "endUnix");

            if (credits <= 0 || credits > MaxCredits) return null;
            if (gems <= 0 || gems > MaxGems) return null;
            if (start <= 0 || end <= start) return null;

            return new ShopDeal(id, credits, gems, start, end);
        }

        static long Field(IDictionary<string, object> row, string key)
            => row.TryGetValue(key, out object value) ? Long(value) : 0L;

        /// <summary>
        /// A whole number, however the SDK typed it. A fraction is refused as nought rather than
        /// truncated: the server refuses it too, and a deal priced at 899.5 gems is malformed.
        /// </summary>
        static long Long(object value)
        {
            switch (value)
            {
                case long l: return l;
                case int i: return i;
                case double d: return d == Math.Floor(d) && d < long.MaxValue && d > long.MinValue ? (long)d : 0L;
                default: return 0L;
            }
        }
    }

    /// <summary>
    /// The one read the deals need: the public document, once in a while, never listened to
    /// (invariant 60's fetch rule, and the owner's: a listener bills every open client on every
    /// write). Implemented by the Firebase backend; a backend without it sells no deals.
    /// </summary>
    public interface IDealBackend
    {
        /// <summary>
        /// The published deals. An absent document succeeds with none; only a failure to reach the
        /// database fails, so the ledger can tell "nothing on sale" from "did not ask".
        /// </summary>
        Task<(CloudResult result, List<ShopDeal> deals)> ReadDealsAsync(CancellationToken cancellation = default);
    }
}
