using System.Collections.Generic;
using GlimmerGrove.Cloud;

namespace GlimmerGrove.Gifts
{
    /// <summary>
    /// One gift the owner sent this account from the admin page (invariant 63), as the last wallet
    /// reply listed it: what is <em>still unpaid</em> in each currency, and when it was sent.
    ///
    /// <para>
    /// <b>Server-owned, nothing in the save.</b> A gift lives on the wallet document until it is
    /// claimed, is paid by the server under <see cref="Persistence.GrantEntry.GiftId"/> at the
    /// amount the wallet holds, and reaches this device only through a wallet reply - so a
    /// reinstall, a second phone and an account switch all see exactly what the server says is
    /// owed, and nothing else.
    /// </para>
    /// </summary>
    public sealed class AdminGift
    {
        public readonly string Id;
        public readonly long Credits;
        public readonly long Gems;
        public readonly long AtUnix;

        public AdminGift(string id, long credits, long gems, long atUnix)
        {
            Id = id;
            Credits = credits < 0 ? 0 : credits;
            Gems = gems < 0 ? 0 : gems;
            AtUnix = atUnix;
        }

        /// <summary>Something is still owed on it.</summary>
        public bool Any => Credits > 0 || Gems > 0;

        /// <summary>What it still carries in <paramref name="currency"/>; nought for one it does not pay.</summary>
        public long Of(string currency)
            => currency == Persistence.Currency.Credits ? Credits
             : currency == Persistence.Currency.Gems ? Gems
             : 0L;

        public bool SameAs(AdminGift other)
            => other != null && Id == other.Id && Credits == other.Credits && Gems == other.Gems && AtUnix == other.AtUnix;
    }

    /// <summary>
    /// The rules a listed gift must satisfy to be drawn at all. The server's half is
    /// <c>firebase/functions/src/gifts.ts</c>, and the limits here mirror it, so a row the server
    /// would refuse to pay is a row this device never shows.
    /// </summary>
    public static class AdminGifts
    {
        /// <summary><c>GIFT_MAX_CREDITS</c>.</summary>
        public const long MaxCredits = 10_000_000L;

        /// <summary><c>GIFT_MAX_GEMS</c>.</summary>
        public const long MaxGems = 100_000L;

        /// <summary>The two currencies a gift may carry, in the order the panel lays them out.</summary>
        public static readonly string[] Currencies = { Persistence.Currency.Credits, Persistence.Currency.Gems };

        /// <summary>
        /// <c>g</c> + twelve digits + four of <c>[a-z0-9]</c>, the server's <c>GIFT_ID</c>
        /// (<c>ShopDeals.IsId</c>'s shape with the gift's letter). Written out rather than a regex
        /// so the one place it is asked costs no allocation.
        /// </summary>
        public static bool IsId(string id)
        {
            if (id == null || id.Length != 17 || id[0] != 'g') return false;
            for (int i = 1; i <= 12; i++) if (id[i] < '0' || id[i] > '9') return false;
            for (int i = 13; i < 17; i++)
            {
                char c = id[i];
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9')) return false;
            }
            return true;
        }

        /// <summary>
        /// The gifts a reply lists, as the backend handed them over. A malformed row is dropped
        /// rather than failing the reply - the server's rule - and a row with nothing left is not a
        /// gift. The server's order (oldest first) is kept.
        /// </summary>
        public static List<AdminGift> Read(IReadOnlyList<CloudGiftRow> rows)
        {
            var gifts = new List<AdminGift>();
            if (rows == null) return gifts;

            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (row == null || !IsId(row.Id)) continue;
                if (row.Credits < 0 || row.Credits > MaxCredits) continue;
                if (row.Gems < 0 || row.Gems > MaxGems) continue;
                if (row.Credits == 0 && row.Gems == 0) continue;

                // Judged before it is counted as seen: a malformed row must not shadow a sound one
                // under the same id, which is the server's rule as well.
                if (!seen.Add(row.Id)) continue;
                gifts.Add(new AdminGift(row.Id, row.Credits, row.Gems, row.AtUnix));
            }
            return gifts;
        }

        /// <summary>Two lists say the same thing, in the same order.</summary>
        public static bool Same(IReadOnlyList<AdminGift> a, IReadOnlyList<AdminGift> b)
        {
            if (a == null || b == null) return a == b;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!a[i].SameAs(b[i])) return false;
            return true;
        }
    }
}
