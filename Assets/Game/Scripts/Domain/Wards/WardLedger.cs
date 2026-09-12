using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Wards
{
    /// <summary>Why a turret cannot be bought right now, or that it can.</summary>
    public enum WardPurchaseState
    {
        /// <summary>Affordable, unheld and for sale. The only state a buy button is live in.</summary>
        Ready,

        /// <summary>Already held — handed over free, or bought earlier.</summary>
        AlreadyHeld,

        /// <summary>The roster puts no price on it and it is not free either. Nothing to offer.</summary>
        NotForSale,

        /// <summary>For sale, unheld, and the player is short. Carries the shortfall.</summary>
        TooExpensive,

        /// <summary>
        /// For sale and unheld, but the keeper gate is not reached.
        ///
        /// <b>Tested before affordability</b>, which is <c>CompanionLedger</c>'s ordering and
        /// invariant 15a's: when both refusals apply, the gate is the one money cannot answer, so
        /// leading with the price would offer somebody a rewarded video for something the video
        /// cannot buy.
        ///
        /// <b>It reaches a gem price too</b>, which is the owner's reversal of what shipped: a
        /// gate used to belong to a credit price alone, so half the shelf could be taken in any
        /// order by anybody holding gems.
        /// </summary>
        LevelLocked,

        /// <summary>
        /// For sale and unheld, and the turret before it on the shelf is not held on this colour.
        ///
        /// <para>
        /// <b>Asked before the level and before the price</b>, which is the coarsest-first reading
        /// of invariant 15a's ordering: a player three rungs down the ladder cannot act on the
        /// keeper level of a rung they have not reached, and the rung in front of them will state
        /// its own gate when they get to it. It is also the only one of the three refusals that
        /// names something to <em>do</em>.
        /// </para>
        /// <para>
        /// Carries the id of the turret that has to come first, in <see cref="WardOffer.Needs"/>.
        /// </para>
        /// </summary>
        Sealed,
    }

    /// <summary>What a turret costs this player right now, and whether they can pay it.</summary>
    public readonly struct WardOffer
    {
        public readonly WardPurchaseState State;

        /// <summary>The price, or 0 when there is none.</summary>
        public readonly long Cost;

        /// <summary>Which currency that price is in: <c>Currency.Credits</c> or <c>Currency.Gems</c>.</summary>
        public readonly string Currency;

        /// <summary>What the player is holding of that currency, for a panel that shows the gap.</summary>
        public readonly long Balance;

        /// <summary>The keeper level this price is gated behind. Nought when ungated.</summary>
        public readonly int RequiredLevel;

        /// <summary>
        /// The turret that has to be bought on this colour first, or empty.
        ///
        /// Only ever set on <see cref="WardPurchaseState.Sealed"/>. An id rather than a name,
        /// because a name is a loc key the caller resolves and this type may not reach for one.
        /// </summary>
        public readonly string Needs;

        public WardOffer(WardPurchaseState state, long cost, string currency, long balance,
                         int requiredLevel = 0, string needs = null)
        {
            State = state;
            Cost = cost;
            Currency = currency;
            Balance = balance;
            RequiredLevel = requiredLevel;
            Needs = needs ?? string.Empty;
        }

        public bool CanBuy => State == WardPurchaseState.Ready;

        public long Shortfall => Cost > Balance ? Cost - Balance : 0L;
    }

    /// <summary>
    /// Which turrets this player owns, and the one rule that says so.
    ///
    /// <para>
    /// <b>A union-joined set of permanent ids, which is invariant 15's shape and for its
    /// reason.</b> Buying is irreversible, so between two devices the player owns whatever either
    /// bought; a count could not be merged (11b) and a per-turret flag could not tell "not bought"
    /// from "written before this turret existed".
    /// </para>
    /// <para>
    /// <b>A starter is never written down</b>, exactly as starter land is not (invariant 16e) and
    /// the starter companion is not (16f): "absent" and "owns nothing but the free one" stay one
    /// fact, so a roster that later prices a turret cannot confiscate one from somebody who was
    /// only ever holding the default.
    /// </para>
    /// <para>
    /// <b>A row is a turret <em>and a colour</em></b> (<see cref="WardHolding"/>), so every read
    /// here takes one and the whole shelf is a ladder climbed four times over. A row with no
    /// colour on it is one an older build wrote and means all four, which is the only reading a
    /// union merge could safely give it.
    /// </para>
    /// <para>
    /// <b>Owning a turret is not money</b>, so a forged entry buys a silhouette and never an
    /// advantage that reaches a public number — every ability is an addition to a bolt and the
    /// grove's score does not read the line. That is what makes a client-held entitlement safe
    /// here where a stored <em>amount</em> would not be (invariant 15's own argument).
    /// </para>
    /// </summary>
    public static class WardLedger
    {
        static readonly HashSet<string> _bought = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Prefix on a purchase's spend reason. Read by support, never by code.</summary>
        public const string SpendReason = "ward:";

        /// <summary>Raised when the held set changed, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>Raised on a completed purchase, for the panel doing the ceremony.</summary>
        public static event Action<WardModel> Bought;

        /// <summary>
        /// The roster in force: whatever <c>progression.json</c> published, or the built-in one.
        ///
        /// <b>Read through the rules rather than cached</b>, which is <c>UtilityLedger</c>'s
        /// shape: a remote push swaps the whole table atomically, so a cached roster would be a
        /// second answer to "what does this turret cost" that a retune could leave stale.
        /// </summary>
        public static WardCatalog Catalog => ProgressionRules.Table.Wards;

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Whether the player holds this turret <em>on this colour</em>: bought it for that seat,
        /// or it is one the roster hands over.
        ///
        /// <para>
        /// <b>The whole unlock rule, and nothing else composes it</b> - invariant 15a's lesson,
        /// where a call site checking half a rule under a name promising all of it is how
        /// something somebody paid for stays behind a padlock. Note what is deliberately absent:
        /// reaching the keeper level of a <em>priced</em> turret grants nothing, and neither does
        /// holding the rung below it. Both are permission to pay.
        /// </para>
        /// <para>
        /// <b>And the colour is not optional.</b> It was, and a turret bought for red stood on
        /// all four seats - which is buying one decision and receiving four. See
        /// <see cref="WardHolding"/>.
        /// </para>
        /// </summary>
        public static bool IsHeld(WardModel model, char colour) => IsHeld(model, colour, Holds);

        /// <summary>The same question about a colour index (0..3).</summary>
        public static bool IsHeld(WardModel model, int colour) => IsHeld(model, Letter(colour));

        /// <summary>
        /// The unlock rule over any purchased set - this ledger's, or one written in a save file.
        /// One body, so the two cannot come to disagree about what "held" means.
        /// </summary>
        public static bool IsHeld(WardModel model, char colour, Func<string, bool> holds)
        {
            if (model == null) return false;
            if (model.IsStarter) return true;
            if (holds == null) return false;

            // Both spellings, because a row written before colours existed means every colour and
            // reading only the exact key would confiscate it (`WardHolding`).
            return holds(WardHolding.Key(model.Id, colour)) || holds(model.Id);
        }

        public static bool IsHeld(string id, char colour) => IsHeld(Catalog.Find(id), colour);

        /// <summary>Whether this exact row is in the purchased set. The predicate, said once.</summary>
        static bool Holds(string row) => _bought.Contains(row);

        /// <summary>The colour letter for an index, clamped the way the line clamps it.</summary>
        static char Letter(int colour)
            => WardLine.Colours[colour < 0 || colour >= WardLine.Colours.Length ? 0 : colour];

        /// <summary>Whether this turret was paid for on this colour. For a panel that says so.</summary>
        public static bool WasBought(WardModel model, char colour)
            => model != null
            && (_bought.Contains(WardHolding.Key(model.Id, colour)) || _bought.Contains(model.Id));

        /// <summary>How many rows were paid for, across every turret and every colour.</summary>
        public static int BoughtCount => _bought.Count;

        /// <summary>How many of the shelf's turrets the player holds on one colour.</summary>
        public static int HeldOn(char colour)
        {
            int count = 0;
            var models = Catalog.Models;

            for (int i = 0; i < models.Count; i++)
                if (IsHeld(models[i], colour)) count++;

            return count;
        }

        /// <summary>
        /// The rung this turret is sealed behind on this colour, or null once it is open.
        ///
        /// <b>One rung rather than the whole prefix</b> - see <see cref="WardCatalog.Before"/>.
        /// </summary>
        public static WardModel SealedBehind(WardModel model, char colour)
        {
            var before = Catalog.Before(model);
            return before != null && !IsHeld(before, colour) ? before : null;
        }

        /// <summary>
        /// What this turret costs the player on this colour right now.
        ///
        /// <para>
        /// <b>Three refusals in coarsest-first order: the rung, then the gate, then the price.</b>
        /// That is invariant 15a's ordering with one more wall in front of it - when several
        /// apply, the one to say is the one furthest from money, because leading with a price
        /// offers somebody a way to spend that could not have worked. A player two rungs down is
        /// told to buy the rung in front of them rather than the keeper level of something they
        /// cannot reach yet, which is also the only one of the three that names an action.
        /// </para>
        /// </summary>
        public static WardOffer OfferFor(WardModel model, char colour, int keeperLevel)
        {
            if (model == null)
                return new WardOffer(WardPurchaseState.NotForSale, 0L, Currency.Credits, 0L);

            var currency = model.ForGems ? Currency.Gems : Currency.Credits;
            long balance = currency == Currency.Gems
                         ? PlayerProgression.Gems : PlayerProgression.Credits;
            long cost = model.ForGems ? model.GemPrice : model.CoinPrice;

            if (IsHeld(model, colour))
                return new WardOffer(WardPurchaseState.AlreadyHeld, cost, currency, balance);

            if (cost <= 0)
                return new WardOffer(WardPurchaseState.NotForSale, 0L, currency, balance);

            var behind = SealedBehind(model, colour);

            if (behind != null)
                return new WardOffer(WardPurchaseState.Sealed, cost, currency, balance,
                                     model.MinLevel, behind.Id);

            if (keeperLevel < model.MinLevel)
                return new WardOffer(WardPurchaseState.LevelLocked, cost, currency, balance,
                                     model.MinLevel);

            var state = balance >= cost ? WardPurchaseState.Ready : WardPurchaseState.TooExpensive;
            return new WardOffer(state, cost, currency, balance, model.MinLevel);
        }

        /// <summary>The same offer for a colour index (0..3).</summary>
        public static WardOffer OfferFor(WardModel model, int colour, int keeperLevel)
            => OfferFor(model, Letter(colour), keeperLevel);

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a turret <em>for one colour</em>, debiting the currency it is priced in and
        /// recording that seat as held.
        ///
        /// <para>
        /// <b>The debit goes first and the row is only added if it succeeded</b>, which is
        /// <c>CompanionLedger.TryBuy</c>'s ordering and its argument: a process killed between the
        /// two leaves a player who paid and did not receive, which the spend log can see and
        /// support can put right, where the other order leaves a turret nobody paid for, which is
        /// indistinguishable from a forgery and therefore invisible.
        /// </para>
        /// <para>
        /// Re-entrancy is handled by the held check rather than by a flag: a double tap finds it
        /// already held on the second pass and returns false without charging.
        /// </para>
        /// </summary>
        public static bool TryBuy(WardModel model, char colour, int keeperLevel)
        {
            var offer = OfferFor(model, colour, keeperLevel);
            if (!offer.CanBuy) return false;

            // The spend reason carries the seat as well as the turret, because support reading a
            // debit has to know which of the four a player paid for.
            string row = WardHolding.Key(model.Id, colour);

            if (!PlayerProgression.TrySpend(offer.Currency, offer.Cost, SpendReason + row))
                return false;

            _bought.Add(row);

            Telemetry.Track("ward_bought", "ward", model.Id, "colour", colour.ToString(),
                            "cost", offer.Cost, "currency", offer.Currency, "level", keeperLevel);

            SaveService.Save();
            Raise();

            try { Bought?.Invoke(model); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        /// <summary>The same purchase for a colour index (0..3).</summary>
        public static bool TryBuy(WardModel model, int colour, int keeperLevel)
            => TryBuy(model, Letter(colour), keeperLevel);

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _bought.Clear();

            var ids = dto?.wardsOwned;
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id)) _bought.Add(id);

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto) => dto.wardsOwned = Sorted(_bought);

        /// <summary>
        /// The union of two devices' purchases.
        ///
        /// <b>No early return for an empty side</b>, which is <c>CompanionLedger.Join</c>'s trap:
        /// handing one array straight back would skip the sort, and <c>SaveDelta</c> walks these
        /// in order — so an unsorted file joined against nothing would read as changed on every
        /// launch and push a write for nothing, for ever.
        /// </summary>
        public static string[] Join(string[] mine, string[] other)
        {
            var union = new SortedSet<string>(StringComparer.Ordinal);

            Absorb(union, mine);
            Absorb(union, other);

            var result = new string[union.Count];
            union.CopyTo(result);
            return result;
        }

        static void Absorb(SortedSet<string> into, string[] ids)
        {
            if (ids == null) return;

            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id)) into.Add(id);
        }

        static string[] Sorted(HashSet<string> ids)
        {
            var sorted = new SortedSet<string>(ids, StringComparer.Ordinal);
            var result = new string[sorted.Count];
            sorted.CopyTo(result);
            return result;
        }
    }
}
