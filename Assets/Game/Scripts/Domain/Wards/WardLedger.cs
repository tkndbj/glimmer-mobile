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
        /// For sale in credits and unheld, but the keeper gate is not reached.
        ///
        /// <b>Tested before affordability</b>, which is <c>CompanionLedger</c>'s ordering and
        /// invariant 15a's: when both refusals apply, the gate is the one credits cannot answer,
        /// so leading with the price would offer somebody a rewarded video for something the video
        /// cannot buy.
        /// </summary>
        LevelLocked,
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

        /// <summary>The keeper level a credit price is gated behind. Nought when ungated.</summary>
        public readonly int RequiredLevel;

        public WardOffer(WardPurchaseState state, long cost, string currency, long balance,
                         int requiredLevel = 0)
        {
            State = state;
            Cost = cost;
            Currency = currency;
            Balance = balance;
            RequiredLevel = requiredLevel;
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
        /// Whether the player holds this turret: bought it, or it is one the roster hands over.
        ///
        /// <b>The whole unlock rule, and nothing else composes it</b> — invariant 15a's lesson,
        /// where a call site checking half a rule under a name promising all of it is how
        /// something somebody paid for stays behind a padlock. Note what is deliberately absent:
        /// reaching the keeper level of a <em>priced</em> turret grants nothing. It is permission
        /// to pay.
        /// </summary>
        public static bool IsHeld(WardModel model) => IsHeld(model, _bought.Contains);

        /// <summary>
        /// The unlock rule over any purchased set — this ledger's, or one written in a save file.
        /// One body, so the two cannot come to disagree about what "held" means.
        /// </summary>
        public static bool IsHeld(WardModel model, Func<string, bool> bought)
            => model != null
            && (model.IsStarter || (bought != null && bought(model.Id)));

        public static bool IsHeld(string id) => IsHeld(Catalog.Find(id));

        /// <summary>Whether this turret was paid for. Only for a panel that wants to say so.</summary>
        public static bool WasBought(WardModel model)
            => model != null && _bought.Contains(model.Id);

        /// <summary>How many turrets were paid for.</summary>
        public static int BoughtCount => _bought.Count;

        /// <summary>How many the player holds, for the "6 of 20" caption.</summary>
        public static int HeldCount
        {
            get
            {
                int count = 0;
                var models = Catalog.Models;

                for (int i = 0; i < models.Count; i++)
                    if (IsHeld(models[i])) count++;

                return count;
            }
        }

        /// <summary>
        /// What this turret costs the player right now.
        ///
        /// <b>The gate is asked before the price</b> (invariant 15a's ordering), so a keeper both
        /// a rung short and out of credits is told about the wall money cannot climb.
        /// </summary>
        public static WardOffer OfferFor(WardModel model, int keeperLevel)
        {
            if (model == null)
                return new WardOffer(WardPurchaseState.NotForSale, 0L, Currency.Credits, 0L);

            var currency = model.ForGems ? Currency.Gems : Currency.Credits;
            long balance = currency == Currency.Gems
                         ? PlayerProgression.Gems : PlayerProgression.Credits;
            long cost = model.ForGems ? model.GemPrice : model.CoinPrice;

            if (IsHeld(model))
                return new WardOffer(WardPurchaseState.AlreadyHeld, cost, currency, balance);

            if (cost <= 0)
                return new WardOffer(WardPurchaseState.NotForSale, 0L, currency, balance);

            if (model.ForCoins && keeperLevel < model.MinLevel)
                return new WardOffer(WardPurchaseState.LevelLocked, cost, currency, balance,
                                     model.MinLevel);

            var state = balance >= cost ? WardPurchaseState.Ready : WardPurchaseState.TooExpensive;
            return new WardOffer(state, cost, currency, balance, model.MinLevel);
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a turret, debiting the currency it is priced in and recording it as held.
        ///
        /// <para>
        /// <b>The debit goes first and the id is only added if it succeeded</b>, which is
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
        public static bool TryBuy(WardModel model, int keeperLevel)
        {
            var offer = OfferFor(model, keeperLevel);
            if (!offer.CanBuy) return false;

            if (!PlayerProgression.TrySpend(offer.Currency, offer.Cost, SpendReason + model.Id))
                return false;

            _bought.Add(model.Id);

            Telemetry.Track("ward_bought", "ward", model.Id, "cost", offer.Cost,
                            "currency", offer.Currency, "level", keeperLevel);

            SaveService.Save();
            Raise();

            try { Bought?.Invoke(model); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

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
