using System;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Utilities
{
    /// <summary>Why a purchase or a use was refused, in one word the caller can act on.</summary>
    public enum UtilityRefusal
    {
        /// <summary>Nothing was refused.</summary>
        None = 0,

        /// <summary>No such utility in this build's catalog.</summary>
        Unknown = 1,

        /// <summary>The player is holding none of them.</summary>
        Empty = 2,

        /// <summary>Holding as many as the catalog allows; another would be thrown away.</summary>
        Full = 3,

        /// <summary>Not for sale — a chest hands this one out and gems do not.</summary>
        NotForSale = 4,

        /// <summary>Short of gems.</summary>
        Poor = 5,

        /// <summary>
        /// The keeper gate is not reached. Carried by <c>UtilityItem.MinLevel</c>.
        ///
        /// <b>Appended</b>, for this project's usual reason: these ordinals reach analytics. And
        /// it gates <em>buying</em> only — a utility already in hand is spendable whatever the
        /// gate says, or a retune would confiscate something bought with gems.
        /// </summary>
        Locked = 6,
    }

    /// <summary>
    /// What a player is holding, and the whole of the policy over it: what may be granted, what
    /// may be bought and what may be used.
    ///
    /// <para>
    /// <b>Account-wide and shared by every level of every mode that offers them.</b> That is the
    /// point rather than a convenience: a stock kept per level would be per-level state in the
    /// save, keyed on a level id, merged across devices — and it would make a utility part of a
    /// board's difficulty, which is exactly what invariant 29c forbids a companion's ability from
    /// being. What a player is holding is a fact about the account, so it is one ledger and a
    /// siege is dealt no utilities of its own.
    /// </para>
    /// <para>
    /// <b>The split with <see cref="UtilityStock"/> is the split <c>HomesteadLedger</c> makes with
    /// <c>GroveStock</c>.</b> The stock is two integers per id, mergeable, provable offline and
    /// entirely ignorant of what a utility is; this is every rule that needs the catalog or the
    /// wallet. Neither counts anything the other counts, so the two cannot come to disagree.
    /// </para>
    /// <para>
    /// <b>A grant is not a claim, and that is a deliberate departure from invariant 10a.</b> An
    /// award reaches the player as a claim when it is <em>currency</em>, because currency is what
    /// an attacker wants and what real money buys. A utility is neither: it is bounded by
    /// <see cref="UtilityItem.MaxHeld"/>, it is consumed, it buys no star and no credit
    /// (invariant 39), and nothing about it reaches a leaderboard — so it is applied here and
    /// now, exactly as a chest's hearts and hints are, and the server is told nothing because
    /// there is nothing for it to adjudicate.
    /// </para>
    /// </summary>
    public static class UtilityLedger
    {
        static readonly UtilityStock _stock = new UtilityStock();

        /// <summary>Raised whenever what the player is holding changes.</summary>
        public static event Action Changed;

        /// <summary>
        /// The catalog in force, which is content and may be replaced by a config push.
        ///
        /// Asked through <c>ProgressionRules</c> rather than cached, for the reason every other
        /// table here is: a cached copy is a second answer for a live retune to put out of step
        /// with the first.
        /// </summary>
        public static UtilityCatalog Catalog => ProgressionRules.Table.Utilities;

        // ------------------------------------------------------------- reading
        /// <summary>How many of this utility the player is holding.</summary>
        public static int Held(string id) => _stock.Held(id);

        public static int Held(UtilityItem item) => item == null ? 0 : _stock.Held(item.Id);

        /// <summary>
        /// Room left before a grant of this utility would be thrown away.
        ///
        /// <b>Asked before an offer is made rather than after it is taken</b>, which is
        /// <c>RewardedAds.WouldBenefit</c>'s rule: a ceiling that clamps silently is a reward the
        /// player was shown and did not receive.
        /// </summary>
        public static int RoomFor(UtilityItem item)
        {
            if (item == null) return 0;

            int room = item.MaxHeld - _stock.Held(item.Id);
            return room < 0 ? 0 : room;
        }

        // ------------------------------------------------------------- granting
        /// <summary>
        /// Hands over some, clamped at the published ceiling, and answers how many actually
        /// landed.
        ///
        /// <para>
        /// <b>The ceiling is enforced here and nowhere else</b>, which is what makes it safe to
        /// lower from a config push — <c>RegenLedger.Grant</c>'s rule: a grant is a decision taken
        /// once, so a smaller ceiling refuses new ones without ever reaching back into a save to
        /// take one. An id this build does not know is refused rather than banked, because a row
        /// nothing can spend is a row that would sit in the save for ever.
        /// </para>
        /// </summary>
        public static int Grant(string id, int count)
        {
            var item = Catalog.Find(id);
            if (item == null || count <= 0) return 0;

            int room = RoomFor(item);
            if (room <= 0) return 0;
            if (count > room) count = room;

            int landed = _stock.Earn(item.Id, count);
            if (landed <= 0) return 0;

            SaveService.MarkDirty();
            Raise();
            return landed;
        }

        // ------------------------------------------------------------- buying
        /// <summary>
        /// The most of this one an order could ask for right now: what there is room for, and
        /// what the gems in hand will actually cover.
        ///
        /// <para>
        /// <b>It exists because the ceiling moved.</b> There was deliberately no such thing while
        /// a player could hold nine — the only quantity anything asked about was one, and an
        /// unused bound is a bound nothing keeps honest. At a hundred that stops being true in
        /// both directions: a stepper needs an upper stop, and a shop that sold a hundred one tap
        /// at a time would be a hundred taps.
        /// </para>
        /// <para>
        /// <b>Both stops, not just the ceiling</b> — <c>HomesteadLedger.MaxQuantity</c>'s rule.
        /// A stepper that climbed to the room left would walk a player past what they can pay for
        /// and hand the refusal to the button, which is the panel lying about the one thing it
        /// exists to be exact about. Nought is a legal answer and means "not now": the caller
        /// draws the shortfall rather than a stepper with no stops.
        /// </para>
        /// </summary>
        public static int MaxQuantity(UtilityItem item)
        {
            if (item == null || !item.ForSale) return 0;

            int room = RoomFor(item);
            if (room <= 0) return 0;

            long affordable = PlayerProgression.Gems / item.GemPrice;
            if (affordable <= 0L) return 0;

            return affordable < room ? (int)affordable : room;
        }

        /// <summary>What an order of <paramref name="count"/> costs in gems.</summary>
        public static long Quote(UtilityItem item, int count)
            => item == null || count <= 0 ? 0L : (long)item.GemPrice * count;

        /// <summary>
        /// Why an order would be refused, or <see cref="UtilityRefusal.None"/> if it would not.
        ///
        /// <para>
        /// <b>The ceiling is asked before the price</b>, which is invariant 15a's ordering and for
        /// its reason: a player who is both full and short should be told about the wall money
        /// cannot climb, not offered a shop they would gain nothing from.
        /// </para>
        /// </summary>
        public static UtilityRefusal WhyNotBuy(UtilityItem item, int count)
        {
            if (item == null) return UtilityRefusal.Unknown;
            if (!item.ForSale) return UtilityRefusal.NotForSale;

            // **The gate before the ceiling before the price**, which is invariant 15a's ordering
            // taken one step further: a player who is locked out, full *and* short should be told
            // about the wall that money cannot climb, because it is the only one of the three
            // nothing they do tonight can answer.
            if (!item.ReachedBy(PlayerProgression.Level.Level)) return UtilityRefusal.Locked;

            if (count <= 0 || RoomFor(item) < count) return UtilityRefusal.Full;
            if (!PlayerProgression.CanAfford(Currency.Gems, Quote(item, count))) return UtilityRefusal.Poor;

            return UtilityRefusal.None;
        }

        /// <summary>
        /// Buys some with gems.
        ///
        /// <para>
        /// <b>The gems leave first and the stock rises second, and never the other way round.</b>
        /// <c>PlayerProgression.TrySpend</c> is what refuses an unaffordable order, so a caller
        /// that granted first would have to take something back on a failure — and taking back is
        /// the one thing this project's ledgers are built never to do.
        /// </para>
        /// <para>
        /// The room is re-checked after the debit rather than trusted from
        /// <see cref="WhyNotBuy"/>: nothing here is re-entrant today, but a grant that landed
        /// short would be a charge with something missing, which is the failure invariant 23
        /// names about a continue that does not continue.
        /// </para>
        /// </summary>
        public static bool TryBuy(UtilityItem item, int count, out UtilityRefusal refusal)
        {
            refusal = WhyNotBuy(item, count);
            if (refusal != UtilityRefusal.None) return false;

            long price = Quote(item, count);

            if (!PlayerProgression.TrySpend(Currency.Gems, price, "utility:" + item.Id))
            {
                refusal = UtilityRefusal.Poor;
                return false;
            }

            // Straight to the stock rather than through Grant, because the ceiling was the
            // question WhyNotBuy already answered and re-asking it here could clamp an order
            // that has just been paid for.
            _stock.Earn(item.Id, count);

            SaveService.Save();
            Raise();

            return true;
        }

        // ------------------------------------------------------------- using
        /// <summary>Why a use would be refused, or <see cref="UtilityRefusal.None"/>.</summary>
        public static UtilityRefusal WhyNotUse(UtilityItem item)
        {
            if (item == null) return UtilityRefusal.Unknown;
            return _stock.Held(item.Id) > 0 ? UtilityRefusal.None : UtilityRefusal.Empty;
        }

        /// <summary>
        /// Spends one.
        ///
        /// <para>
        /// <b>Called once per use that actually landed on the board, and nowhere else.</b> A
        /// player charged for a utility whose target refused it is a player who lost something
        /// they paid gems for — which is <c>ProtoView.Took</c>'s rule about a move, applied to the
        /// one resource here that costs real money to replace. The board is asked first and this
        /// is asked second; see <c>SiegeUtility.Apply</c>, which returns what it did before
        /// anything is taken.
        /// </para>
        /// </summary>
        public static bool TryUse(UtilityItem item)
        {
            if (item == null) return false;
            if (_stock.Use(item.Id, 1) <= 0) return false;

            Tasks.TaskLedger.Note(Tasks.TaskGoal.Utilities);

            SaveService.Save();
            Raise();
            return true;
        }

        // ------------------------------------------------------------- events
        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        internal static void LoadFrom(SaveFileDto dto)
        {
            _stock.LoadFrom(dto?.utilityStock);
            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            if (dto == null) return;
            dto.utilityStock = _stock.Write();
        }

        // No Reset(). Erasing an account goes through `SaveService.Adopt(FreshFile())`, which
        // re-runs LoadFrom over an empty file and clears this with everything else — a second
        // door would be a second thing to remember on the one path with no undo (invariant 27).

    }
}
