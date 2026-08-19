using System;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// The bounds a published catalog is checked against, and the numbers used when there
    /// is none.
    ///
    /// <para>
    /// Same job <c>HeartLimits</c> does for the gate and <c>AdRules</c> does for the ad
    /// table: content is allowed to retune the shop, it is not allowed to redefine what
    /// the shop is. Everything here is a compile-time constant precisely because it is
    /// what a published file is checked <em>against</em> — a limit that could itself be
    /// published would not be a limit.
    /// </para>
    /// </summary>
    public static class StoreLimits
    {
        /// <summary>
        /// Most products a catalog may list.
        ///
        /// <para>
        /// Generous against the thirteen that ship, and bounded at all for a reason that is
        /// not memory: every product id here is fetched from the store at launch, and both
        /// stores rate-limit and slow down that call as the list grows. A catalog that grew
        /// without anybody noticing would show up as a shop that takes four seconds to
        /// open, on the screen where a slow open costs money.
        /// </para>
        /// </summary>
        public const int MaxProducts = 64;

        /// <summary>Most gem-priced goods a catalog may list.</summary>
        public const int MaxGoods = 32;

        /// <summary>
        /// The most currency one product may grant.
        ///
        /// <para>
        /// A sanity bound rather than a design one, and it is the server that enforces the
        /// figure that matters — this side only decides what a card is allowed to promise.
        /// It exists because a misplaced zero in a content push is the cheapest possible way
        /// to hand out ten times what a product costs, and unlike every other number in this
        /// file that mistake cannot be taken back: the currency has been granted against a
        /// real receipt and clawing it back is a support case with a proof of purchase
        /// attached.
        /// </para>
        /// </summary>
        public const long MaxGrant = 5_000_000L;

        /// <summary>Most hearts one good may hand over. Above the published ceiling on purpose.</summary>
        public const int MaxGoodHearts = 200;

        /// <summary>Most hours of boost one good may hand over. See <c>HeartLimits.MaxBoostHoursLimit</c>.</summary>
        public const int MaxGoodBoostHours = 168;

        /// <summary>Most gems one good may cost.</summary>
        public const long MaxGoodPrice = 100_000L;

        /// <summary>Cheapest a product may claim to be, in US cents. Below both stores' floors.</summary>
        public const int MinReferenceCents = 49;

        /// <summary>Dearest a product may claim to be, in US cents.</summary>
        public const int MaxReferenceCents = 100_000;
    }

    /// <summary>
    /// What the shop sells: products bought with money, and goods bought with gems.
    ///
    /// <para>
    /// <b>One authored list, two consumers, and that is the whole point.</b> This table is
    /// read from <c>progression.json</c> by the client to draw the shop, and the seed
    /// script derives <c>config/products</c> on the server from the same block — so the
    /// amount a card promises and the amount a receipt is honoured for cannot disagree
    /// unless somebody forgets to re-seed, which is the one failure the seeder exists to
    /// make loud. This is invariant 9a's lesson applied to money: the rule exists in two
    /// places because it has to run in two places, so it is <em>generated</em> into the
    /// second rather than typed there.
    /// </para>
    /// <para>
    /// Like every other optional block in the progression file this is deliberately not a
    /// schema bump — see <c>HeartRuleTable</c>. A client that predates it reads a file with
    /// a store block and ignores it; a client that has it reads a file written before the
    /// block existed and falls back to the built-in ladder.
    /// </para>
    /// <para>
    /// <b>Nothing here is safe to lower after release in the way the heart block is.</b>
    /// The heart gate's numbers describe a rule; these describe what somebody has already
    /// paid for. Lowering a product's grant does not confiscate anything — the receipt is
    /// keyed to a transaction and honoured once — but it does mean two players who paid the
    /// same price got different amounts, and the second one will notice. Retune by adding a
    /// product, never by re-pointing one.
    /// </para>
    /// </summary>
    public sealed class StoreCatalog
    {
        readonly StoreProduct[] _products;
        readonly StoreGood[] _goods;
        readonly Dictionary<string, StoreProduct> _byId;
        readonly Dictionary<string, StoreGood> _goodsById;

        StoreCatalog(StoreProduct[] products, StoreGood[] goods)
        {
            _products = products ?? Array.Empty<StoreProduct>();
            _goods = goods ?? Array.Empty<StoreGood>();

            _byId = new Dictionary<string, StoreProduct>(_products.Length, StringComparer.Ordinal);
            foreach (var product in _products) _byId[product.Id] = product;

            _goodsById = new Dictionary<string, StoreGood>(_goods.Length, StringComparer.Ordinal);
            foreach (var good in _goods) _goodsById[good.Id] = good;

            CreditsPerGem = DeriveCreditsPerGem(_products);
            RankShelves(_products, CreditsPerGem);
        }

        public IReadOnlyList<StoreProduct> Products => _products;

        public IReadOnlyList<StoreGood> Goods => _goods;

        /// <summary>True when there is anything at all to sell.</summary>
        public bool HasAnything => _products.Length > 0 || _goods.Length > 0;

        /// <summary>
        /// How many credits one gem is worth, derived from the two money ladders' entry
        /// rungs rather than written down.
        ///
        /// <para>
        /// It exists for one job — putting a bundle's gems and credits on one scale so a
        /// shelf can be ranked and its bonus badges computed — and it is derived so that
        /// retuning either ladder moves it automatically. A typed constant would be a third
        /// opinion about the exchange rate sitting between two ladders that already imply
        /// one, and it would go stale the first time either moved.
        /// </para>
        /// <para>
        /// Note carefully what this is <b>not</b>: it is not an exchange the player can
        /// make. Gems do not buy credits anywhere in this game, deliberately — see
        /// <see cref="StoreGood"/> — so nothing about this number reaches an economy, and
        /// it can be wrong by a fifth without any player being able to tell.
        /// </para>
        /// </summary>
        public long CreditsPerGem { get; }

        public StoreProduct Find(string productId)
            => !string.IsNullOrEmpty(productId) && _byId.TryGetValue(productId, out var p) ? p : null;

        public StoreGood FindGood(string goodId)
            => !string.IsNullOrEmpty(goodId) && _goodsById.TryGetValue(goodId, out var g) ? g : null;

        /// <summary>Products on one shelf, in the order the file authored them.</summary>
        public List<StoreProduct> Shelf(StoreShelf shelf)
        {
            var list = new List<StoreProduct>();
            foreach (var product in _products) if (product.Shelf == shelf) list.Add(product);
            return list;
        }

        /// <summary>
        /// Every product id, for the one call that has to name them all: the store fetch at
        /// launch. Both stores answer with metadata only for ids they were asked about, so
        /// a product missing from this list is a product with no price and no card.
        /// </summary>
        public List<string> AllProductIds()
        {
            var ids = new List<string>(_products.Length);
            foreach (var product in _products) ids.Add(product.Id);
            return ids;
        }

        // ------------------------------------------------------------------ defaults
        /// <summary>
        /// The ladder that ships inside the build, and the floor under any content mistake.
        ///
        /// <para>
        /// A real catalog rather than an empty one, for the reason every other table here
        /// ships a real default: a content read that fails must cost live tuning and never
        /// a feature. It is kept byte-for-byte in step with <c>progression.json</c> by
        /// <c>StoreTests</c>, which reads the shipped file and compares — otherwise this
        /// would be a second opinion about what the shop sells, and the failure mode is a
        /// card promising an amount the server will not grant.
        /// </para>
        /// </summary>
        public static readonly StoreCatalog Default = new StoreCatalog(
            new[]
            {
                // --- gems: the ladder every other price is measured against ------------
                new StoreProduct("gg_gems_1", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 100, 99, StoreBadge.None),
                new StoreProduct("gg_gems_2", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 340, 299, StoreBadge.None),
                new StoreProduct("gg_gems_3", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 750, 599, StoreBadge.Popular),
                new StoreProduct("gg_gems_4", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 1700, 1299, StoreBadge.None),
                new StoreProduct("gg_gems_5", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 3900, 2499, StoreBadge.None),
                new StoreProduct("gg_gems_6", StoreProductKind.Consumable, StoreShelf.Gems,
                                 0, 8500, 4999, StoreBadge.BestValue),

                // --- coins -------------------------------------------------------------
                new StoreProduct("gg_coins_1", StoreProductKind.Consumable, StoreShelf.Coins,
                                 2500, 0, 199, StoreBadge.None),
                new StoreProduct("gg_coins_2", StoreProductKind.Consumable, StoreShelf.Coins,
                                 9000, 0, 599, StoreBadge.Popular),
                new StoreProduct("gg_coins_3", StoreProductKind.Consumable, StoreShelf.Coins,
                                 26000, 0, 1499, StoreBadge.None),
                new StoreProduct("gg_coins_4", StoreProductKind.Consumable, StoreShelf.Coins,
                                 75000, 0, 3999, StoreBadge.BestValue),

                // --- bundles -----------------------------------------------------------
                new StoreProduct("gg_bundle_starter", StoreProductKind.NonConsumable, StoreShelf.Bundles,
                                 7500, 500, 299, StoreBadge.Starter),
                new StoreProduct("gg_bundle_keeper", StoreProductKind.Consumable, StoreShelf.Bundles,
                                 12000, 700, 999, StoreBadge.None),
                new StoreProduct("gg_bundle_grove", StoreProductKind.Consumable, StoreShelf.Bundles,
                                 42000, 2200, 2999, StoreBadge.BestValue),
            },
            new[]
            {
                new StoreGood("hearts_five", StoreGoodKind.Hearts, 5, 50),
                new StoreGood("hearts_fifteen", StoreGoodKind.Hearts, 15, 125),
                new StoreGood("hearts_forty", StoreGoodKind.Hearts, 40, 280),
                new StoreGood("boost_day", StoreGoodKind.HeartBoost, 24, 30),
                new StoreGood("boost_three_day", StoreGoodKind.HeartBoost, 72, 75),
            });

        /// <summary>A catalog with nothing in it, which is what a deployment with no shop has.</summary>
        public static readonly StoreCatalog Empty =
            new StoreCatalog(Array.Empty<StoreProduct>(), Array.Empty<StoreGood>());

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>store</c> block. Never throws and never returns null.
        ///
        /// <para>
        /// Unlike the heart block, a bad entry here is <b>dropped</b> rather than clamped,
        /// and the distinction is the difference between tuning and money. Clamping a
        /// refill period to something sensible is a guess in the author's direction;
        /// clamping a grant would mean selling a product for an amount nobody authored,
        /// against a server that would honour a different one. A product that does not read
        /// cleanly is therefore not sold at all, which is visible in one glance at the shop
        /// and impossible to mistake for working.
        /// </para>
        /// </summary>
        public static StoreCatalog Resolve(StoreDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                       // absent is not an error

            // <para>
            // <b>An absent block and an empty one are the same thing, and they have to be.</b>
            // <c>JsonUtility</c> instantiates a nested serialisable field whether or not the
            // file carried it, so <c>dto</c> is non-null for a progression file that has never
            // heard of a shop — which is every file this project shipped before today, and the
            // reward-vector fixture, which deliberately carries only the blocks it is about.
            // Reporting that as an authoring mistake made two vector tests red for a shop
            // nobody had authored. So the complaint below is gated on something actually
            // having been written down.
            // </para>
            bool authored = (dto.products != null && dto.products.Length > 0) ||
                            (dto.goods != null && dto.goods.Length > 0);

            var products = new List<StoreProduct>();
            var goods = new List<StoreGood>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var badged = new Dictionary<StoreShelf, StoreBadge>();

            if (dto.products != null)
            {
                foreach (var entry in dto.products)
                {
                    if (products.Count >= StoreLimits.MaxProducts)
                    {
                        problems.Add($"store lists more than {StoreLimits.MaxProducts} products; " +
                                     "the rest are dropped, because every id is fetched from the " +
                                     "store at launch and that call slows as the list grows");
                        break;
                    }

                    var product = ReadProduct(entry, seen, badged, problems);
                    if (product != null) products.Add(product);
                }
            }

            if (dto.goods != null)
            {
                foreach (var entry in dto.goods)
                {
                    if (goods.Count >= StoreLimits.MaxGoods)
                    {
                        problems.Add($"store lists more than {StoreLimits.MaxGoods} goods; the rest " +
                                     "are dropped");
                        break;
                    }

                    var good = ReadGood(entry, seen, problems);
                    if (good != null) goods.Add(good);
                }
            }

            // A block that authored entries and produced none of them is a mistake worth
            // failing a build over: every one of them was dropped by name above, and a live
            // build would open on an empty shop with nothing on screen to say why. A block
            // that authored nothing is simply a file from before the shop existed.
            if (products.Count == 0 && goods.Count == 0)
            {
                if (authored)
                    problems.Add("store block has no usable products or goods; the built-in ladder " +
                                 "stands. Remove the entries rather than leaving unreadable ones.");

                return Default;
            }

            return new StoreCatalog(products.ToArray(), goods.ToArray());
        }

        static StoreProduct ReadProduct(StoreProductDto dto, HashSet<string> seen,
                                        Dictionary<StoreShelf, StoreBadge> badged, List<string> problems)
        {
            if (dto == null) return null;

            string id = dto.id ?? string.Empty;
            if (!IsUsableId(id))
            {
                problems.Add($"store product id '{dto.id}' is unusable; ids are lower case letters, " +
                             "digits and underscores, because a receipt is looked up by this string " +
                             "for the life of the account");
                return null;
            }

            if (!seen.Add(id))
            {
                problems.Add($"store lists '{id}' twice; the second is dropped");
                return null;
            }

            if (!TryReadShelf(dto.shelf, out var shelf))
            {
                problems.Add($"store product '{id}' names unknown shelf '{dto.shelf}'");
                return null;
            }

            if (shelf == StoreShelf.Supplies)
            {
                problems.Add($"store product '{id}' sits on the supplies shelf, which is for goods " +
                             "bought with gems; a real-money product cannot go there");
                return null;
            }

            bool oneTime = string.Equals(dto.kind, "nonconsumable", StringComparison.OrdinalIgnoreCase);
            if (!oneTime && !string.Equals(dto.kind, "consumable", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"store product '{id}' has kind '{dto.kind}'; it must be 'consumable' or " +
                             "'nonconsumable', and the two are not interchangeable — the store itself " +
                             "enforces that a nonconsumable is sold once per account");
                return null;
            }

            long credits = dto.credits < 0 ? 0 : dto.credits;
            long gems = dto.gems < 0 ? 0 : dto.gems;

            if (credits == 0 && gems == 0)
            {
                problems.Add($"store product '{id}' grants nothing");
                return null;
            }

            if (credits > StoreLimits.MaxGrant || gems > StoreLimits.MaxGrant)
            {
                problems.Add($"store product '{id}' grants more than the supported " +
                             $"{StoreLimits.MaxGrant}; dropped rather than clamped, because a card " +
                             "promising a figure the server will not honour is worse than no card");
                return null;
            }

            if (dto.referenceUsdCents < StoreLimits.MinReferenceCents ||
                dto.referenceUsdCents > StoreLimits.MaxReferenceCents)
            {
                problems.Add($"store product '{id}' has referenceUsdCents {dto.referenceUsdCents}, " +
                             $"outside {StoreLimits.MinReferenceCents}..{StoreLimits.MaxReferenceCents}. " +
                             "It is never shown to a player, but the value ladder is proved against it");
                return null;
            }

            var badge = ReadBadge(dto.badge, id, problems);

            // At most one Popular and one BestValue per shelf. Two of either is not a
            // content error the player can see as an error — it reads as a shop that cannot
            // make up its mind, which is exactly what a badge exists to prevent.
            if (badge == StoreBadge.Popular || badge == StoreBadge.BestValue)
            {
                if (badged.TryGetValue(shelf, out var already) && already == badge)
                {
                    problems.Add($"store shelf '{shelf}' already carries a '{badge}' badge; " +
                                 $"'{id}' keeps the product and loses the badge");
                    badge = StoreBadge.None;
                }
                else badged[shelf] = badge;
            }

            if (badge == StoreBadge.Starter && !oneTime)
            {
                problems.Add($"store product '{id}' is badged as a starter but is a consumable; " +
                             "a starter offer that can be bought twice is not one, so the badge " +
                             "is dropped rather than the product");
                badge = StoreBadge.None;
            }

            return new StoreProduct(id, oneTime ? StoreProductKind.NonConsumable : StoreProductKind.Consumable,
                                    shelf, credits, gems, dto.referenceUsdCents, badge);
        }

        static StoreGood ReadGood(StoreGoodDto dto, HashSet<string> seen, List<string> problems)
        {
            if (dto == null) return null;

            string id = dto.id ?? string.Empty;
            if (!IsUsableId(id))
            {
                problems.Add($"store good id '{dto.id}' is unusable; ids are lower case letters, " +
                             "digits and underscores");
                return null;
            }

            if (!seen.Add(id))
            {
                problems.Add($"store lists '{id}' twice; the second is dropped");
                return null;
            }

            var kind = StoreGoodKinds.Parse(dto.kind);
            if (kind == StoreGoodKind.None)
            {
                problems.Add($"store good '{id}' names unknown kind '{dto.kind}'. Only " +
                             $"'{StoreGoodKinds.Hearts}' and '{StoreGoodKinds.HeartBoost}' can be bought " +
                             "with gems — currency cannot, because only the server may grant it");
                return null;
            }

            int max = kind == StoreGoodKind.Hearts ? StoreLimits.MaxGoodHearts : StoreLimits.MaxGoodBoostHours;
            if (dto.amount < 1 || dto.amount > max)
            {
                problems.Add($"store good '{id}' hands over {dto.amount}, outside 1..{max}");
                return null;
            }

            if (dto.gems < 1 || dto.gems > StoreLimits.MaxGoodPrice)
            {
                problems.Add($"store good '{id}' costs {dto.gems} gems, outside 1..{StoreLimits.MaxGoodPrice}");
                return null;
            }

            return new StoreGood(id, kind, dto.amount, dto.gems);
        }

        static StoreBadge ReadBadge(string badge, string id, List<string> problems)
        {
            if (string.IsNullOrEmpty(badge)) return StoreBadge.None;
            if (string.Equals(badge, "popular", StringComparison.OrdinalIgnoreCase)) return StoreBadge.Popular;
            if (string.Equals(badge, "best_value", StringComparison.OrdinalIgnoreCase)) return StoreBadge.BestValue;
            if (string.Equals(badge, "starter", StringComparison.OrdinalIgnoreCase)) return StoreBadge.Starter;

            problems.Add($"store product '{id}' has unknown badge '{badge}'; drawn without one");
            return StoreBadge.None;
        }

        static bool TryReadShelf(string shelf, out StoreShelf value)
        {
            value = StoreShelf.Gems;
            if (string.IsNullOrEmpty(shelf)) return false;
            if (string.Equals(shelf, "gems", StringComparison.OrdinalIgnoreCase)) { value = StoreShelf.Gems; return true; }
            if (string.Equals(shelf, "coins", StringComparison.OrdinalIgnoreCase)) { value = StoreShelf.Coins; return true; }
            if (string.Equals(shelf, "bundles", StringComparison.OrdinalIgnoreCase)) { value = StoreShelf.Bundles; return true; }
            if (string.Equals(shelf, "supplies", StringComparison.OrdinalIgnoreCase)) { value = StoreShelf.Supplies; return true; }
            return false;
        }

        /// <summary>
        /// The same rule ids everywhere else in this project follow. Deliberately strict:
        /// both stores accept a wider alphabet than this, and the narrowest set that works
        /// on both is the one that cannot surprise anybody later.
        /// </summary>
        static bool IsUsableId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;

            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ ranking
        /// <summary>
        /// Credits per gem, from the cheapest product on each money shelf.
        ///
        /// Falls back to a flat 1 when either shelf is absent, which makes a bundle's value
        /// meaningless rather than wrong — and nothing but a badge depends on it.
        /// </summary>
        static long DeriveCreditsPerGem(StoreProduct[] products)
        {
            StoreProduct gemBase = null, coinBase = null;

            foreach (var product in products)
            {
                if (product.Shelf == StoreShelf.Gems && product.Gems > 0 &&
                    (gemBase == null || product.ReferenceUsdCents < gemBase.ReferenceUsdCents))
                    gemBase = product;

                if (product.Shelf == StoreShelf.Coins && product.Credits > 0 &&
                    (coinBase == null || product.ReferenceUsdCents < coinBase.ReferenceUsdCents))
                    coinBase = product;
            }

            if (gemBase == null || coinBase == null) return 1L;

            // (credits per cent) / (gems per cent), integral, floored at 1.
            long numerator = coinBase.Credits * gemBase.ReferenceUsdCents;
            long denominator = (long)gemBase.Gems * coinBase.ReferenceUsdCents;
            if (denominator <= 0) return 1L;

            long rate = numerator / denominator;
            return rate < 1 ? 1L : rate;
        }

        /// <summary>
        /// Fills in every product's <see cref="StoreProduct.BonusPercent"/>, measured
        /// against the cheapest <em>repeatable</em> product on its own shelf.
        ///
        /// <para>
        /// Cheapest by reference price rather than first in the file, so re-ordering the
        /// authored list for display reasons cannot silently change what every card claims.
        /// </para>
        /// <para>
        /// <b>One-time products are never the baseline.</b> A starter offer is deliberately
        /// worth several times the ladder — that is what it is for, and it is safe because
        /// the store will not sell it twice — so measuring the ladder against it would make
        /// every ordinary rung read as no bonus at all. They are still measured against the
        /// ladder themselves, which is exactly the number worth printing on one.
        /// </para>
        /// </summary>
        static void RankShelves(StoreProduct[] products, long creditsPerGem)
        {
            foreach (StoreShelf shelf in Enum.GetValues(typeof(StoreShelf)))
            {
                var onShelf = new List<StoreProduct>();
                foreach (var product in products)
                    if (product.Shelf == shelf) onShelf.Add(product);

                if (onShelf.Count == 0) continue;

                // The picture on a card is a function of where it sits, so the order is
                // taken from the reference price rather than from the file — a rung inserted
                // in the middle re-draws everything above it and nothing has to be re-authored.
                onShelf.Sort((a, b) => a.ReferenceUsdCents.CompareTo(b.ReferenceUsdCents));

                for (int i = 0; i < onShelf.Count; i++)
                {
                    onShelf[i].Tier = i + 1;
                    onShelf[i].ShelfSize = onShelf.Count;
                }

                StoreProduct baseline = null;
                foreach (var product in onShelf)
                {
                    if (product.IsOneTime) continue;
                    if (baseline == null || product.ReferenceUsdCents < baseline.ReferenceUsdCents)
                        baseline = product;
                }

                if (baseline == null) continue;

                long baseValue = baseline.ValuePerCent(creditsPerGem);
                if (baseValue <= 0) continue;

                foreach (var product in onShelf)
                {
                    long value = product.ValuePerCent(creditsPerGem);
                    long percent = (value - baseValue) * 100L / baseValue;

                    product.BonusPercent = percent <= 0 ? 0 : (int)percent;
                }
            }
        }
    }

    /// <summary>
    /// The live catalog, read synchronously from anywhere.
    ///
    /// A facade over <see cref="ProgressionRules"/> shaped exactly like <c>HeartRules</c>
    /// and <c>RewardedAds.Table</c>, for their reason: the alternative is an install step,
    /// and a step someone has to remember is a step that gets forgotten.
    /// </summary>
    public static class StoreRules
    {
        public static StoreCatalog Catalog => ProgressionRules.Table.Store;

        public static StoreProduct Find(string productId) => Catalog.Find(productId);

        public static StoreGood FindGood(string goodId) => Catalog.FindGood(goodId);
    }
}
