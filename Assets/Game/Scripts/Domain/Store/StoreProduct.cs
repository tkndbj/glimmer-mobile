using System;

namespace GlimmerGrove.Store
{
    /// <summary>
    /// Which shelf of the shop a product sits on.
    ///
    /// <para>
    /// One idea used three times, exactly as <c>GroveShelf</c> is: a shelf is the shop's
    /// tab, the order products are drawn in, and the group a value badge is computed
    /// against. Expressing the division once is what stops the tab row and the "+40%"
    /// badge coming to different conclusions about which products are comparable.
    /// </para>
    /// </summary>
    public enum StoreShelf
    {
        /// <summary>Hard currency. The ladder everything else is priced against.</summary>
        Gems = 0,

        /// <summary>Soft currency, sold directly.</summary>
        Coins,

        /// <summary>Gems and credits together, at a discount on buying the parts.</summary>
        Bundles,

        /// <summary>Hearts and boosts, bought with gems rather than money. Not a product.</summary>
        Supplies,
    }

    /// <summary>
    /// How a store classifies a product. This is the store's own vocabulary, not ours.
    ///
    /// <para>
    /// It has to be authored rather than inferred, because it is the field that decides
    /// whether a purchase can happen twice. A consumable is bought over and over; a
    /// non-consumable is bought once per store account, for ever, and both App Store
    /// Connect and the Play Console enforce that themselves. That is why the one-time
    /// starter bundle is a non-consumable and not a consumable with a flag in our save:
    /// the store refuses the second purchase before any money moves, which no amount of
    /// client state can do, and a restore on a new device costs nothing because the grant
    /// is keyed on a transaction id the server has already seen.
    /// </para>
    /// <para>
    /// A product's kind is therefore <b>permanent</b> in the same way its id is. Changing
    /// one after release means creating a new product, because the store will not let the
    /// old one change what it is.
    /// </para>
    /// </summary>
    public enum StoreProductKind
    {
        Consumable = 0,
        NonConsumable,
    }

    /// <summary>
    /// A mark drawn on a product's card, to make one of them easier to pick than the rest.
    ///
    /// <para>
    /// Authored rather than derived, and that is the exception rather than the rule here —
    /// the "+40% extra" line <em>is</em> derived (see <see cref="StoreProduct.BonusPercent"/>)
    /// because it is arithmetic, and arithmetic that disagreed with the ladder would be a
    /// lie a player can check. Which single card to point at is not arithmetic; it is a
    /// merchandising decision that moves with live conversion data, and it belongs in the
    /// same content file everything else that moves lives in.
    /// </para>
    /// </summary>
    public enum StoreBadge
    {
        None = 0,

        /// <summary>The one most people buy. At most one per shelf; the reader enforces it.</summary>
        Popular,

        /// <summary>The best value on its shelf. At most one per shelf.</summary>
        BestValue,

        /// <summary>Once per account, ever. Drawn differently, and hidden once it is held.</summary>
        Starter,
    }

    /// <summary>
    /// One thing the shop sells for real money, and everything the game knows about it
    /// except its price.
    ///
    /// <para>
    /// <b>The price is deliberately not here, and must never be.</b> A price lives in App
    /// Store Connect and the Play Console, is set per storefront in dozens of currencies,
    /// moves with tax and with exchange rates, and is read back from the store SDK at
    /// runtime as an already-formatted string. Writing one into content would be wrong in
    /// every country but one within a week, and drawing a hardcoded price is a review
    /// rejection on both stores. <see cref="ReferenceUsdCents"/> exists only so the build
    /// gate can prove the ladder gets better as it gets bigger — it is never shown to
    /// anybody.
    /// </para>
    /// <para>
    /// What a product grants is a pair of currency amounts and nothing else. That is the
    /// single most load-bearing decision in this feature. Currency is the one thing the
    /// server owns (invariant 10) and can therefore grant against a validated receipt with
    /// no client involvement at all; hearts and boosts live in the save file and are
    /// applied by the phone. A product that granted both would need the client to apply
    /// half of it after the server had applied the other half, which means a record of
    /// "did I already apply this transaction's hearts" — a new field in the save, merged
    /// across devices, whose failure mode is a player being charged and given nothing. So
    /// hearts and boosts are bought with <b>gems</b> instead (see <see cref="StoreGood"/>),
    /// which is a currency spend the ledger already makes idempotent, and every real-money
    /// product here grants currency only.
    /// </para>
    /// </summary>
    public sealed class StoreProduct
    {
        public StoreProduct(string id, StoreProductKind kind, StoreShelf shelf,
                            long credits, long gems, int referenceUsdCents, StoreBadge badge)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Shelf = shelf;
            Credits = credits < 0 ? 0 : credits;
            Gems = gems < 0 ? 0 : gems;
            ReferenceUsdCents = referenceUsdCents < 0 ? 0 : referenceUsdCents;
            Badge = badge;
        }

        /// <summary>
        /// The product id, exactly as configured in both stores.
        ///
        /// <para>
        /// Permanent, in the full sense invariant 1 means: it keys a receipt document that
        /// lives for ever, it is what the server looks up to decide what a purchase was
        /// worth, and neither store lets a product id be reused after it has been deleted.
        /// Never rename one and never repoint one at a different amount — a receipt
        /// redeemed a year from now is looked up against today's table.
        /// </para>
        /// </summary>
        public string Id { get; }

        public StoreProductKind Kind { get; }

        public StoreShelf Shelf { get; }

        /// <summary>Credits granted, by the server, on a validated receipt.</summary>
        public long Credits { get; }

        /// <summary>Gems granted, by the server, on a validated receipt.</summary>
        public long Gems { get; }

        /// <summary>
        /// What this is expected to cost in US cents.
        ///
        /// <para>
        /// Never displayed — see the type's remarks. It exists for two jobs, both of them
        /// offline: <c>Validate Content</c> proves a shelf's value per unit of money rises
        /// with its size, so a ladder cannot ship with a middle rung worse than the one
        /// below it; and <see cref="BonusPercent"/> is computed from it, so the "+40% extra"
        /// on a card is arithmetic over the authored ladder rather than a second number
        /// somebody typed.
        /// </para>
        /// </summary>
        public int ReferenceUsdCents { get; }

        public StoreBadge Badge { get; }

        public bool IsValid => Id.Length > 0 && (Credits > 0 || Gems > 0);

        /// <summary>True when the store will only ever sell this once per account.</summary>
        public bool IsOneTime => Kind == StoreProductKind.NonConsumable;

        /// <summary>
        /// How much more currency per unit of money this gives than the smallest product
        /// on its shelf, as a whole percentage. Zero on the base product itself.
        ///
        /// <para>
        /// Set by <see cref="StoreCatalog"/> once, at load, over the shelf as authored —
        /// which is why it is a property with an internal setter rather than a constructor
        /// argument. A product does not know what else is on its shelf, and a card that
        /// computed this for itself would be computing it against nothing.
        /// </para>
        /// <para>
        /// It compares <em>reference</em> prices, so it is a statement about the ladder and
        /// not about what any particular player is charged. Store price tiers preserve
        /// relative order across storefronts, so the claim holds everywhere; it would stop
        /// holding the moment somebody priced two tiers out of order in one country, which
        /// is a thing to know before doing it.
        /// </para>
        /// </summary>
        public int BonusPercent { get; internal set; }

        /// <summary>
        /// Where this sits on its shelf, 1 for the smallest. Zero until the catalog ranks it.
        ///
        /// <para>
        /// Derived from the reference price rather than authored, so the picture on a card
        /// cannot come to disagree with the ladder — a rung inserted in the middle of a
        /// shelf re-draws everything above it with no art order and no edit anywhere else.
        /// It is what <c>ShopArt</c> composes from: which container a product sits in and
        /// how much is piled on it are a function of this and <see cref="ShelfSize"/>, which
        /// is why a shelf of four and a shelf of six both read as a full ladder.
        /// </para>
        /// </summary>
        public int Tier { get; internal set; }

        /// <summary>How many products share this shelf, so a tier can be read as a fraction.</summary>
        public int ShelfSize { get; internal set; }

        /// <summary>Where this sits on its shelf, 0 at the bottom and 1 at the top.</summary>
        public float TierFraction
            => ShelfSize <= 1 ? 1f : (Tier - 1) / (float)(ShelfSize - 1);

        /// <summary>
        /// Currency units per hundred dollars, as a whole number, used to rank a shelf.
        ///
        /// Integral rather than a float division per comparison, so two products whose
        /// value works out the same cannot swap places on a rounding difference — the
        /// build gate asserts this order, and an assertion that depends on rounding is an
        /// assertion that fails on somebody else's machine. Bundles mix two currencies, so
        /// gems are converted at the rate <see cref="StoreCatalog"/> derives from the two
        /// money ladders rather than at one written down here.
        /// </summary>
        public long ValuePerCent(long creditsPerGem)
        {
            if (ReferenceUsdCents <= 0) return 0;
            long units = Credits + Gems * (creditsPerGem <= 0 ? 1 : creditsPerGem);
            return units * 10000L / ReferenceUsdCents;
        }

        /// <summary>
        /// The loc key for this product's name, derived from its id and not overridable —
        /// invariant 5a, for its reason. Anything holding a product id can name it without
        /// reading the catalog, which is what lets a purchase confirmation say what was
        /// bought after the shop screen is long gone.
        /// </summary>
        public string NameKey => "store.product." + Id;

        public override string ToString() => $"{Id} ({Credits} credits, {Gems} gems)";
    }

    /// <summary>
    /// Something bought with gems rather than with money: hearts, and faster hearts.
    ///
    /// <para>
    /// The reason these are not store products is set out in <see cref="StoreProduct"/>,
    /// and it is worth restating from the other side. A gem spend is a
    /// <c>CurrencyLedger.TrySpend</c>, which is already offline-safe, already carries an
    /// idempotency key, and is already refused by the server when the derived balance
    /// cannot cover it. Applying hearts in the same call is exactly what buying a companion
    /// does and needs no new machinery whatever. Selling hearts for money instead would
    /// mean a permanent store product per heart bundle — priced in every storefront,
    /// undeletable, and re-priced by hand every time the gate is retuned — in exchange for
    /// nothing a player can tell apart.
    /// </para>
    /// </summary>
    public sealed class StoreGood
    {
        public StoreGood(string id, StoreGoodKind kind, int amount, long gems)
        {
            Id = id ?? string.Empty;
            Kind = kind;
            Amount = amount < 0 ? 0 : amount;
            Gems = gems < 0 ? 0 : gems;
        }

        /// <summary>Permanent. It is written into a debit's reason, which support reads.</summary>
        public string Id { get; }

        public StoreGoodKind Kind { get; }

        /// <summary>Hearts, or hours of boost. The unit is the kind's own.</summary>
        public int Amount { get; }

        public long Gems { get; }

        public bool IsValid => Id.Length > 0 && Kind != StoreGoodKind.None && Amount > 0 && Gems > 0;

        /// <summary>Derived from the id, for <see cref="StoreProduct.NameKey"/>'s reason.</summary>
        public string NameKey => "store.good." + Id;

        /// <summary>
        /// What a debit for this records as its cause. Free text on the wire, and read by
        /// nothing — the server does not adjudicate what a gem was spent on, only that the
        /// balance covered it — so it exists for a human looking at a support case.
        /// </summary>
        public string SpendReason => "store_good:" + Id;

        public override string ToString() => $"{Id} ({Amount}, {Gems} gems)";
    }

    /// <summary>
    /// What a gem-priced good hands over.
    ///
    /// <para>
    /// Deliberately its own enum rather than a reuse of <c>ChestDropKind</c>, even though
    /// two members would line up. A chest drop is a thing the server re-rolls and can pay
    /// in currency; this is a thing gems buy, and currency is explicitly not on the list —
    /// a good that paid credits would need the server to mint them, which is the whole
    /// reason products grant currency and goods do not. Sharing the enum would make that
    /// distinction impossible to see and easy to break.
    /// </para>
    /// </summary>
    public enum StoreGoodKind
    {
        None = 0,

        /// <summary>Hearts, granted at once and clamped by <c>HeartRules.Ceiling</c>.</summary>
        Hearts,

        /// <summary>Hours of faster refill, extending any boost already running.</summary>
        HeartBoost,
    }

    /// <summary>The permanent ids a content file uses for good kinds. See <c>ChestDropKinds</c>.</summary>
    public static class StoreGoodKinds
    {
        public const string Hearts = "hearts";
        public const string HeartBoost = "heart_boost";

        public static StoreGoodKind Parse(string id)
        {
            if (string.Equals(id, Hearts, StringComparison.Ordinal)) return StoreGoodKind.Hearts;
            if (string.Equals(id, HeartBoost, StringComparison.Ordinal)) return StoreGoodKind.HeartBoost;
            return StoreGoodKind.None;
        }

        public static string Id(StoreGoodKind kind)
        {
            switch (kind)
            {
                case StoreGoodKind.Hearts: return Hearts;
                case StoreGoodKind.HeartBoost: return HeartBoost;
                default: return string.Empty;
            }
        }
    }
}
