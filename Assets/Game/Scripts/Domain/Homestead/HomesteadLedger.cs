using System;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>Why a piece cannot be bought right now, or that it can.</summary>
    public enum HomesteadPurchaseState
    {
        /// <summary>Affordable, unheld and for sale. The only state a buy button is live in.</summary>
        Ready,

        /// <summary>Already held — earned by playing, bought earlier, or free from the start.</summary>
        AlreadyHeld,

        /// <summary>The catalog does not price this one. Every resident is here, always.</summary>
        NotForSale,

        /// <summary>For sale, unheld, and the player is short. Carries the shortfall.</summary>
        TooExpensive,

        /// <summary>
        /// A resident whose keeper gate the player has not reached. Credits cannot answer it.
        ///
        /// Only residents can be in this state — <c>CompanionPurchaseState.LevelLocked</c>
        /// under the grove's own name. Mapping it onto <see cref="NotForSale"/> instead would
        /// tell a player a friend "can only be earned by playing" when it is for sale and they
        /// are four keeper levels away, which is the class of refusal this enum exists to keep
        /// apart.
        /// </summary>
        LevelLocked,

        /// <summary>
        /// A stretch of ground further up the ladder than the player has reached. Money cannot
        /// answer it either — the rung below has to be bought first.
        ///
        /// <para>
        /// Land-only, on <see cref="LevelLocked"/>'s precedent and for its reason: folding it
        /// into <see cref="NotForSale"/> would tell a keeper that ground they can see priced on
        /// the shelf "can only be earned by playing", and folding it into
        /// <see cref="TooExpensive"/> would quote them a price that buys nothing. It is the one
        /// refusal here that a player clears by spending rather than by waiting, so the sentence
        /// has to name the stretch that comes first — see <c>GroveLand.NextForSale</c>.
        /// </para>
        /// </summary>
        EarlierFirst,
    }

    /// <summary>What a piece costs this player right now, and whether they can pay it.</summary>
    public readonly struct HomesteadOffer
    {
        public readonly HomesteadPurchaseState State;

        /// <summary>
        /// The price of the whole order — one bundle's price times <see cref="Quantity"/>.
        ///
        /// <b>Not a unit price.</b> Every caller is about to draw a button that takes this many
        /// credits, so quoting anything else here would make the panel's arithmetic the
        /// panel's problem, in three panels, one of which would get it wrong.
        /// </summary>
        public readonly long Cost;

        /// <summary>
        /// How many bundles this offer is for. One unless a stepper asked for more.
        ///
        /// It rides the offer rather than being passed beside it so that
        /// <see cref="HomesteadOffer.Shortfall"/>, the buy button's caption and the debit can
        /// never come from two different numbers — the bug the win panel's separately-derived
        /// reward row already proved is real.
        /// </summary>
        public readonly int Quantity;

        /// <summary>How many copies land in the player's stock if this offer is taken.</summary>
        public readonly int Copies;

        /// <summary>Credits the player is holding, for a panel that shows the gap.</summary>
        public readonly long Balance;

        /// <summary>The keeper level a resident is gated behind. Zero for everything else.</summary>
        public readonly int RequiredLevel;

        public HomesteadOffer(HomesteadPurchaseState state, long cost, long balance,
                              int requiredLevel = 0, int quantity = 1, int copies = 0)
        {
            RequiredLevel = requiredLevel;
            State = state;
            Cost = cost;
            Balance = balance;
            Quantity = quantity < 1 ? 1 : quantity;
            Copies = copies < 0 ? 0 : copies;
        }

        public bool CanBuy => State == HomesteadPurchaseState.Ready;

        /// <summary>How many credits short, or 0 when the player can pay.</summary>
        public long Shortfall => Cost > Balance ? Cost - Balance : 0L;
    }

    /// <summary>
    /// Which pieces and plots this player holds, and the act of buying one.
    ///
    /// <para>
    /// <b>The rule is requirement OR purchase, and it lives here alone.</b> Every screen
    /// asks <see cref="IsHeld(HomesteadPiece)"/>; nothing else composes the two halves.
    /// That is invariant 15a, learned on companions: <c>AvatarCatalog.ReachedBy</c> had to
    /// be renamed away from <c>IsUnlocked</c> because a call site checking half a rule under
    /// a name promising all of it is precisely how something somebody paid for stays behind
    /// a padlock. Nothing here is called <c>IsUnlocked</c> for the same reason.
    /// </para>
    /// <para>
    /// <b>The earned half is derived; the bought half is stored as a union-joined set.</b>
    /// A requirement is a question about the star ledger, so it recomputes on every device,
    /// survives every merge, cannot be lost, and can be retuned for players who already
    /// have it — invariant 14, and the reason plots and residents cost the save file
    /// nothing at all. A purchase cannot be derived from anything observable, so it is
    /// stored, in the one shape invariant 11b permits and <c>CompanionLedger</c> already
    /// proved: a set of permanent ids that only ever grows.
    /// </para>
    /// <para>
    /// <b>A purchase buys copies; an entitlement buys permission.</b> Buying <c>fence_low</c>
    /// grants <see cref="HomesteadPiece.Bundle"/> fences and each one stands somewhere, so a
    /// grove that wants a dozen buys a dozen. Anything <em>earned</em> — the starter pieces,
    /// the eight glade rewards, every resident, every home rung — is held exactly as it was
    /// before v20: unlimited, derived where it can be, and never counted. The split is the
    /// design rather than a compromise. Stock is the shop's half of the feature and it should
    /// run out; an entitlement is play's half and it should not.
    /// </para>
    /// <para>
    /// <b>What is left is derived, never stored.</b> <see cref="Available(HomesteadPiece)"/> is
    /// copies bought minus copies standing in the grove, and both sides are already in the save
    /// file and already merged. That is what makes a count representable here at all — see
    /// <see cref="GroveStock"/> for the full argument, and note the subtraction is clamped at
    /// zero rather than trusted: two devices can each place the last copy on a different tile,
    /// and the answer to that is a shop that says "none left", never a placement taken down.
    /// </para>
    /// <para>
    /// <b>The forging bound is the same one companions accept.</b> The set is client-written,
    /// so an edited save can award itself a bench. It buys a picture on a screen nobody else
    /// sees: no currency, no progression, no advantage on a board. The money half is defended
    /// where money always is — <see cref="PlayerProgression.TrySpend"/> books an idempotent
    /// debit and <c>submitSpends</c> refuses one the server-derived balance cannot cover.
    /// Nothing here needs adjudicating, which is why nothing adjudicates it.
    /// </para>
    /// </summary>
    public static class HomesteadLedger
    {
        static readonly GroveStock _stock = new GroveStock();

        /// <summary>Prefix on a purchase's spend reason. Read by support, never by code.</summary>
        public const string SpendReason = "grove:";

        /// <summary>
        /// Most bundles one tap of the buy button may order.
        ///
        /// <para>
        /// An <em>economy</em> bound rather than a structural one, which is why it lives here
        /// and not on <see cref="GroveStock"/>: the structural ceiling exists so a hostile file
        /// cannot overflow a sum, and this exists so a stepper cannot spend a player's entire
        /// balance in one held-down press. They are different jobs and a single number serving
        /// both would eventually be moved for one reason and break the other —
        /// <c>HeartLimits.HardCeiling</c> against the published ceiling, one file over.
        /// </para>
        /// <para>
        /// Twenty of a ten-piece bundle is two hundred copies against a 196-tile floor, so it
        /// binds only somebody who has stopped reading the screen.
        /// </para>
        /// </summary>
        public const int MaxPerPurchase = 20;

        /// <summary>Raised when the held set changed, so an open screen can redraw.</summary>
        public static event Action Changed;

        /// <summary>Raised on a completed purchase, for the panel doing the ceremony.</summary>
        public static event Action<HomesteadPiece> Bought;

        /// <summary>
        /// Residents are companions, so a companion bought on the profile changes what this
        /// ledger answers without anything here being told.
        ///
        /// <para>
        /// Hooked once, from the type initialiser, rather than left to each screen. Both
        /// grove screens already subscribe to <see cref="Changed"/>, so re-raising here is what
        /// makes "buy on the profile, see it in the grove" true for every present and future
        /// reader — and this project has twice paid for the alternative, where the rule was a
        /// subscription each new call site had to remember and the third one forgot.
        /// </para>
        /// <para>
        /// The initialiser runs on first touch, which is <see cref="LoadFrom"/> during the save
        /// load — before any screen exists, and therefore before anybody could miss an event.
        /// </para>
        /// </summary>
        static HomesteadLedger()
        {
            CompanionLedger.Changed += Raise;
        }

        /// <summary>
        /// The keeper level the resident half is judged against.
        ///
        /// Read here rather than passed in, unlike <c>CompanionLedger.IsHeld</c>, because every
        /// caller of this file is a grove screen asking about the player in front of it — and
        /// threading a level through <see cref="IsHeld(HomesteadPiece)"/> would put it in the
        /// signature of every question the grove asks, to be got wrong once.
        /// </summary>
        static int KeeperLevel => PlayerProgression.Level.Level;

        // ------------------------------------------------------------- reading
        /// <summary>
        /// Whether the player holds this piece: its requirement met, or bought, or free.
        ///
        /// <b>The whole unlock rule.</b> Nothing else composes it.
        ///
        /// <para>
        /// A resident is <b>delegated whole</b> to <see cref="CompanionLedger"/> rather than
        /// answered here. That is invariant 15a taken literally: the composite rule for a
        /// companion lives in one place, and a grove that re-derived half of it — reading
        /// <c>AvatarCatalog.ReachedBy</c>, or keeping its own copy of the purchased set — is
        /// exactly how somebody who paid for Coral finds her padlocked in the village.
        /// </para>
        /// </summary>
        public static bool IsHeld(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;
            if (piece.IsResident) return CompanionLedger.IsHeld(GroveResidents.CompanionOf(piece), KeeperLevel);

            if (_stock.Any(piece.Id)) return true;
            return IsEarned(piece);
        }

        public static bool IsHeld(string id) => IsHeld(HomesteadCatalog.Current.Find(id));

        /// <summary>
        /// Whether play alone could ever get this piece, whether or not it has yet.
        ///
        /// <para>
        /// Distinct from <see cref="IsEarned"/>, which is the past tense of the same question,
        /// and the distinction is the whole reason this exists: a shop cell marks the pieces
        /// money is not the only way through, and it has to do that <em>before</em> the player
        /// has got there. A resident is the case that made the two diverge — its keeper gate
        /// is now a prerequisite for paying rather than a route of its own, so a priced
        /// companion has no free route at any level.
        /// </para>
        /// </summary>
        public static bool HasFreeRoute(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;
            if (piece.IsResident) return !GroveResidents.CompanionOf(piece).IsForSale;

            return piece.HasRequirement || !piece.IsForSale;
        }

        /// <summary>
        /// Whether play alone has earned this piece.
        ///
        /// <b>Half of the rule.</b> Named for its narrowness on purpose — see the type's
        /// remarks, and invariant 15a for the bug this naming exists to prevent. A panel
        /// that wants to say "you unlocked this by finishing Thorn Hollow" reads this; a
        /// panel deciding whether to draw a padlock reads <see cref="IsHeld(HomesteadPiece)"/>.
        /// </summary>
        public static bool IsEarned(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;

            // A resident's keeper gate is a prerequisite for buying rather than a free route
            // — see CompanionLedger's remarks — so play alone earns a resident only when the
            // roster puts no price on it. Asked through the companion's own definition so the
            // two screens cannot come to disagree about it.
            if (piece.IsResident)
            {
                var companion = GroveResidents.CompanionOf(piece);
                return !companion.IsForSale && AvatarCatalog.ReachedBy(companion, KeeperLevel);
            }

            // Nothing asked of it and no price: the starter furniture a new grove opens with.
            if (!piece.HasRequirement) return !piece.IsForSale;

            if (piece.RequiresLevel.IsValid && HomesteadProgress.IsCleared(piece.RequiresLevel))
                return true;

            return piece.RequiresChapter.IsValid
                && HomesteadProgress.IsChapterFinished(piece.RequiresChapter);
        }

        /// <summary>
        /// Whether this piece was paid for.
        ///
        /// Only for a panel that wants to say so. Never a substitute for
        /// <see cref="IsHeld(HomesteadPiece)"/>: most of the roster is earned and was never
        /// bought, so gating anything on this locks the grove.
        /// </summary>
        /// <summary>
        /// How many <em>distinct</em> pieces were paid for. See <c>CompanionLedger.BoughtCount</c>.
        ///
        /// Distinct rather than a copy count, because every reader of this is counting the shop
        /// grid's cells rather than the grove's tiles.
        /// </summary>
        public static int BoughtCount => _stock.Count;

        public static bool WasBought(HomesteadPiece piece)
            => piece.IsValid
            && (piece.IsResident
                    ? CompanionLedger.WasBought(GroveResidents.CompanionOf(piece))
                    : _stock.Any(piece.Id));

        // ------------------------------------------------------------- stock
        /// <summary>
        /// How many copies of this piece the player has bought over the life of the account.
        ///
        /// The stored half. It only rises, which is what lets it be stored (invariant 11b);
        /// what is left to place is <see cref="Available(HomesteadPiece)"/>.
        /// </summary>
        public static int Copies(HomesteadPiece piece)
            => piece.IsStocked ? _stock.Of(piece.Id) : 0;

        /// <summary>
        /// How many copies of this piece the player may still stand somewhere.
        ///
        /// <para>
        /// <see cref="Unlimited"/> for anything that is not stocked — every resident, every home
        /// rung, and every piece that is free or earned by playing. Those are entitlements and
        /// behave exactly as the whole catalog did before v20.
        /// </para>
        /// <para>
        /// For a stocked piece it is copies bought minus copies standing, <b>clamped at
        /// zero</b>. The clamp is not defensive tidying: two devices editing offline can each
        /// place the last copy on a different tile, and the placement map merges by recency per
        /// slot (invariant 11c), so both survive and the grove briefly holds one more fence than
        /// it bought. Answering "none left" costs the player nothing and resolves the moment
        /// they buy or clear anything; taking a placement down to balance an identity would be
        /// the data loss invariant 11 exists to refuse.
        /// </para>
        /// </summary>
        public static int Available(HomesteadPiece piece)
        {
            if (!piece.IsValid) return 0;
            if (!piece.IsStocked) return IsHeld(piece) ? Unlimited : 0;

            int left = _stock.Of(piece.Id) - HomesteadLayout.CountOf(piece.Id);
            return left < 0 ? 0 : left;
        }

        /// <summary>
        /// What <see cref="Available(HomesteadPiece)"/> answers for a piece that cannot run out.
        ///
        /// A large number rather than a nullable or a second predicate, so every caller can do
        /// plain arithmetic on the answer and the one branch that cares reads
        /// <see cref="HomesteadPiece.IsStocked"/> — which is the honest question anyway.
        /// </summary>
        public const int Unlimited = int.MaxValue;

        /// <summary>True when the player can put another of these in their grove right now.</summary>
        public static bool CanPlace(HomesteadPiece piece)
            => piece.CanBePlaced && IsHeld(piece) && Available(piece) > 0;

        /// <summary>
        /// How many bundles the player could afford and hold, for a stepper's upper stop.
        ///
        /// Bounded by three things and the smallest wins: the balance, <see cref="MaxPerPurchase"/>,
        /// and the room left under <see cref="GroveStock.MaxCopies"/> — the last so a stepper can
        /// never offer an order whose copies would be clamped away after the credits were taken.
        /// </summary>
        public static int MaxQuantity(HomesteadPiece piece)
        {
            if (!piece.IsStocked) return 1;

            long cost = piece.Cost;
            if (cost <= 0L) return 1;

            long affordable = PlayerProgression.Credits / cost;
            if (affordable < 1L) return 1;

            int bundle = piece.Bundle < 1 ? 1 : piece.Bundle;
            int room = (GroveStock.MaxCopies - _stock.Of(piece.Id)) / bundle;

            long capped = affordable < MaxPerPurchase ? affordable : MaxPerPurchase;
            if (room < capped) capped = room;

            return capped < 1L ? 1 : (int)capped;
        }

        // Land moved out of this file entirely when the islands became a floor: it is bought
        // with credits now rather than earned by finishing a chapter, so it is an entitlement
        // rather than something derived. See GroveLand.

        /// <summary>How many pieces the player holds, for the "18 of 46" caption.</summary>
        public static int HeldCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var piece in catalog.Pieces)
                if (IsHeld(piece)) count++;

            return count;
        }

        // ------------------------------------------------------------ the home
        /// <summary>
        /// The best home this player owns, or an invalid piece when the catalog has none.
        ///
        /// <para>
        /// <b>The whole dwelling rule.</b> The hearth draws this; nothing is ever placed on
        /// it. Because the ladder is a set of ids in the same union-joined set as every other
        /// purchase, "best owned" is a maximum over what is held — which makes it idempotent,
        /// order-independent and impossible to lose in a merge, exactly like every other
        /// derived answer in this feature. See <see cref="HomesteadPieceKind.Dwelling"/> for
        /// why it is not a stored level.
        /// </para>
        /// <para>
        /// Ties on tier are broken by catalog order, which is arbitrary and <em>stable</em> —
        /// the property that matters, since two devices must draw the same home.
        /// </para>
        /// </summary>
        public static HomesteadPiece BestDwelling(HomesteadCatalog catalog)
            => BestDwelling(catalog, LedgerHoldings.Instance);

        /// <summary>
        /// The same rule over any holdings — this device's, or a save file's
        /// (<see cref="SaveHoldings"/>), which is what the public card is built from.
        /// </summary>
        public static HomesteadPiece BestDwelling(HomesteadCatalog catalog, IGroveHoldings held)
        {
            var best = default(HomesteadPiece);
            if (catalog == null || held == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsDwelling || !held.Holds(piece)) continue;
                if (!best.IsValid || piece.Tier > best.Tier) best = piece;
            }

            return best;
        }

        /// <summary>
        /// The next rung of the home ladder: the lowest tier above the best one held.
        ///
        /// Invalid when the player is at the top, which is what the panel renders as "this is
        /// the finest home in the grove" rather than as a dead button.
        /// </summary>
        public static HomesteadPiece NextDwelling(HomesteadCatalog catalog)
        {
            if (catalog == null) return default;

            int held = BestDwelling(catalog).Tier;
            var next = default(HomesteadPiece);

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsDwelling || piece.Tier <= held || IsHeld(piece)) continue;
                if (!next.IsValid || piece.Tier < next.Tier) next = piece;
            }

            return next;
        }

        /// <summary>How many rungs the home ladder has, for the pips on the home panel.</summary>
        public static int DwellingCount(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0;

            int count = 0;
            foreach (var piece in catalog.Pieces)
                if (piece.IsDwelling) count++;

            return count;
        }

        /// <summary>
        /// The cheapest piece still for sale that the player does not hold, or an invalid one
        /// when there is nothing left to sell. Drives the shop's "next" prompt.
        /// </summary>
        public static HomesteadPiece CheapestUnheld(HomesteadCatalog catalog)
        {
            var best = default(HomesteadPiece);
            if (catalog == null) return best;

            foreach (var piece in catalog.Pieces)
            {
                if (!piece.IsForSale) continue;
                if (IsHeld(piece)) continue;
                if (!best.IsValid || piece.Cost < best.Cost) best = piece;
            }

            return best;
        }

        /// <summary>
        /// What this piece would cost the player right now, and why it might not be buyable.
        ///
        /// Every refusal is a distinct member because each one renders a different sentence —
        /// the bargain <c>AdOfferState</c> and <c>CompanionOffer</c> both make. A single
        /// "unavailable" would draw the same greyed button for a player 300 credits short and
        /// for a resident that is not for sale at any price, and one of those resolves by
        /// playing for an hour while the other never resolves at all.
        /// </summary>
        public static HomesteadOffer OfferFor(HomesteadPiece piece) => OfferFor(piece, 1);

        /// <summary>
        /// The same question for an order of several bundles.
        ///
        /// <para>
        /// <b>A stocked piece is never <c>AlreadyHeld</c>.</b> That state means "there is
        /// nothing to buy", which stopped being true of the shop's half of the catalog in v20 —
        /// a player with three fences may want three more. It still answers for a resident, a
        /// home rung and anything earned, because those genuinely cannot be bought twice, and
        /// keeping the one state for the one meaning is what stops the shop grid drawing a dead
        /// cell over something it should be selling.
        /// </para>
        /// </summary>
        public static HomesteadOffer OfferFor(HomesteadPiece piece, int quantity)
        {
            long balance = PlayerProgression.Credits;

            if (!piece.IsValid)
                return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L, balance);

            // A resident is priced by the roster and sold by the companion ledger, so the
            // answer is translated rather than recomputed — one price, quoted identically on
            // the profile and in the village.
            if (piece.IsResident) return Translate(CompanionLedger.OfferFor(GroveResidents.CompanionOf(piece),
                                                                           KeeperLevel));

            // Everything that is not stock is bought once, so holding one is the end of it.
            if (!piece.IsStocked)
            {
                if (IsHeld(piece))
                    return new HomesteadOffer(HomesteadPurchaseState.AlreadyHeld, piece.Cost, balance);

                if (!piece.IsForSale)
                    return new HomesteadOffer(HomesteadPurchaseState.NotForSale, 0L, balance);

                var once = balance >= piece.Cost
                    ? HomesteadPurchaseState.Ready
                    : HomesteadPurchaseState.TooExpensive;

                return new HomesteadOffer(once, piece.Cost, balance, 0, 1, 1);
            }

            int wanted = Clamp(quantity, 1, MaxPerPurchase);

            // Room is checked before the price so a player at the structural ceiling is told
            // they are full rather than told they are poor — the ordering CompanionLedger uses
            // for the keeper gate, and for its reason.
            int bundle = piece.Bundle < 1 ? 1 : piece.Bundle;
            int room = (GroveStock.MaxCopies - _stock.Of(piece.Id)) / bundle;
            if (room < 1)
                return new HomesteadOffer(HomesteadPurchaseState.AlreadyHeld, piece.Cost, balance,
                                          0, 1, 0);

            if (wanted > room) wanted = room;

            long cost = (long)piece.Cost * wanted;

            var state = balance >= cost
                ? HomesteadPurchaseState.Ready
                : HomesteadPurchaseState.TooExpensive;

            return new HomesteadOffer(state, cost, balance, 0, wanted, bundle * wanted);
        }

        /// <summary>
        /// A companion's offer as a grove offer. The two enums are the same four states under
        /// two names, which is deliberate — a screen showing a shelf of residents beside a
        /// shelf of fences must render one refusal, not two nearly identical ones.
        /// </summary>
        static HomesteadOffer Translate(CompanionOffer offer)
        {
            HomesteadPurchaseState state;
            switch (offer.State)
            {
                case CompanionPurchaseState.Ready: state = HomesteadPurchaseState.Ready; break;
                case CompanionPurchaseState.AlreadyHeld: state = HomesteadPurchaseState.AlreadyHeld; break;
                case CompanionPurchaseState.TooExpensive: state = HomesteadPurchaseState.TooExpensive; break;
                case CompanionPurchaseState.LevelLocked: state = HomesteadPurchaseState.LevelLocked; break;
                default: state = HomesteadPurchaseState.NotForSale; break;
            }

            return new HomesteadOffer(state, offer.Cost, offer.Balance, offer.RequiredLevel);
        }

        // ------------------------------------------------------------- writing
        /// <summary>
        /// Buys a piece, debiting credits and recording it as held.
        ///
        /// <para>
        /// The debit goes first and the id is only added if it succeeded, which is the
        /// opposite of the tempting order and deliberate — <c>CompanionLedger.TryBuy</c> makes
        /// the argument. A process killed between the two leaves a player who paid and did not
        /// receive, which support can see in the spend log and put right; the other order
        /// leaves a piece nobody paid for, indistinguishable from the forgery this tolerates
        /// and therefore invisible.
        /// </para>
        /// <para>
        /// Re-entrancy is handled by the held check rather than by a flag: a double tap finds
        /// the piece already held on the second pass and returns false without charging.
        /// </para>
        /// </summary>
        public static bool TryBuy(HomesteadPiece piece) => TryBuy(piece, 1);

        /// <summary>
        /// Buys several bundles at once, debiting the whole order and granting all of it.
        ///
        /// <para>
        /// <b>One debit for the order, never a loop of single purchases.</b> A loop would write
        /// a spend entry per bundle — twenty idempotency keys for one decision — and each one
        /// would be separately refusable by the next sync, so a player could be charged for six
        /// fences and receive four with nothing on screen able to explain it. The order is one
        /// spend and one grant, which is also what makes it re-entrant in the same way a single
        /// purchase already was.
        /// </para>
        /// </summary>
        public static bool TryBuy(HomesteadPiece piece, int quantity)
        {
            // A resident is bought as a companion, in one transaction with one spend reason and
            // one entitlement set. Never mirrored into `homesteadOwned`: two records of one
            // purchase is two things a merge can disagree about, and the second one would be
            // the forgeable half of a pair whose other half is already safe.
            if (piece.IsResident) return TryBuyResident(piece);

            var offer = OfferFor(piece, quantity);
            if (!offer.CanBuy) return false;

            if (!PlayerProgression.TrySpend(Currency.Credits, offer.Cost, SpendReason + piece.Id))
                return false;

            // Only a stocked piece has copies; everything else is an entitlement and one row of
            // stock is how this file records that it was paid for.
            _stock.Add(piece.Id, offer.Copies > 0 ? offer.Copies : 1);

            Telemetry.Track("grove_piece_bought", "piece", piece.Id, "cost", offer.Cost,
                            "kind", piece.Kind.ToString(), "copies", offer.Copies);

            // TrySpend already wrote the debit. This write carries the id that debit paid for,
            // and losing it is the failure described above.
            SaveService.Save();
            Raise();

            try { Bought?.Invoke(piece); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        /// <summary>
        /// Buys a resident, which is buying a companion.
        ///
        /// <para>
        /// It deliberately does <b>not</b> go through <c>Profile.TryBuyAvatar</c>, which buys
        /// and then wears. Wearing is a profile preference and housing is a grove arrangement;
        /// somebody who bought a friend to stand by their pond has said nothing about who they
        /// want on their own nameplate, and quietly changing it is the kind of surprise that
        /// makes a player distrust a shop.
        /// </para>
        /// </summary>
        static bool TryBuyResident(HomesteadPiece piece)
        {
            var companion = GroveResidents.CompanionOf(piece);
            if (!CompanionLedger.TryBuy(companion, KeeperLevel)) return false;

            // CompanionLedger saved, and raised its own Changed — which this file re-raises.
            // What is left is the grove's own ceremony hook, so a resident bought in the
            // village celebrates exactly as a fence does.
            try { Bought?.Invoke(piece); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }

            return true;
        }

        static void Raise()
        {
            try { Changed?.Invoke(); }
            catch (Exception e) { UnityEngine.Debug.LogException(e); }
        }

        // --------------------------------------------------- file bridge (internal)
        /// <summary>
        /// Reads the stock, migrating a v19 file on the way.
        ///
        /// <para>
        /// <b>The migration is here rather than in a pass of its own</b> because this is the one
        /// place that can see both halves of it at once: the old id set and the placements those
        /// ids are standing in. Before v20 a purchase was permission rather than a copy, so
        /// there is no honest number to convert — the player bought "a fence" and stood eleven.
        /// Each id therefore becomes the larger of what is actually standing in their grove and
        /// <see cref="GroveStock.LegacyGrant"/>, so nothing they built is left over-placed and
        /// they keep room to rearrange.
        /// </para>
        /// <para>
        /// It runs only when the new section is empty, which is what makes it idempotent: a
        /// migrated file writes stock rows on its next save and never sees this path again, and
        /// a v20 file that genuinely holds nothing has nothing to migrate. The old array is
        /// never written back (see <c>SaveFileDto.homesteadOwned</c>), so this cannot re-run
        /// against its own output and re-grant what a player has since spent.
        /// </para>
        /// </summary>
        internal static void LoadFrom(SaveFileDto dto)
        {
            // GroveStock.In is what knows whether this file predates v20, and it reads the
            // placements out of the same DTO rather than from HomesteadLayout — the save is
            // read section by section and nothing here may depend on the order that happens in.
            _stock.LoadFrom(GroveStock.In(dto));

            Raise();
        }

        internal static void WriteInto(SaveFileDto dto)
        {
            var rows = _stock.Write();

            dto.homesteadStock = rows;

            // The v19 section, derived rather than kept — see GroveStock.Mirror. It is never
            // read back while the stock section has anything in it, so it cannot re-grant what
            // a player has spent, and it is what lets a rolled-back client and a not-yet-
            // redeployed server both keep working.
            dto.homesteadOwned = GroveStock.Mirror(rows);
        }

        /// <summary>
        /// Two devices' purchases, joined. Delegated whole to <see cref="GroveStock.Join"/>,
        /// which is where the argument for a per-id maximum lives.
        ///
        /// It stays on this type because <see cref="SaveMerge"/> reaches for the ledger that
        /// owns each section, and a section that answered from somewhere else would be one more
        /// thing to look up when the merge is the part of this file that has to be obviously
        /// right.
        /// </summary>
        public static HomesteadStockDto[] Join(HomesteadStockDto[] mine, HomesteadStockDto[] other)
            => GroveStock.Join(mine, other);

        static int Clamp(int value, int low, int high)
            => value < low ? low : value > high ? high : value;

        /// <summary>Test seam: forgets every purchase, as a fresh install would.</summary>
        internal static void ResetForTests() => _stock.Clear();

        /// <summary>Test seam: grants copies without a debit. Never call this from the game.</summary>
        internal static void GrantForTests(string id, int copies) => _stock.Add(id, copies);
    }
}
