using System;
using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// The star ladder a grove's score is read against.
    ///
    /// <para>
    /// <b>Content, not constants, and the reason is the catalog's growth rate.</b> A drop
    /// every two to four weeks adds pieces, land and companions, so the value of a complete
    /// grove rises for the life of the game — which means a threshold that reads as "you have
    /// built nearly everything" today reads as "you have made a start" in a year. Baking the
    /// ladder into the client would make retuning it an app update, and an app update is a
    /// thing a fraction of the player base never takes. It rides the grove body rather than
    /// <c>progression.json</c> because it is a fact about the grove catalog, and because a
    /// score nothing adjudicates has no business on the channel the server reads.
    /// </para>
    /// <para>
    /// <b>Sanitised rather than trusted.</b> Content can arrive from a CDN, so this is
    /// <c>HomesteadMapper</c>'s stance applied to a number: a threshold that is not positive
    /// is dropped, duplicates collapse, the rest are sorted, and anything past
    /// <see cref="MaxStars"/> is cut. The build gate refuses all of that beforehand — see
    /// <c>ContentValidation.ValidateGroveScore</c> — so this is the belt behind the braces,
    /// never the place a mistake is reported.
    /// </para>
    /// </summary>
    public sealed class GroveScoreTable
    {
        /// <summary>
        /// Most stars a ladder may have. A cap rather than a fixed count: the readout draws
        /// one star per rung, so a longer ladder is a legal design and a hundred-star one is
        /// a content mistake that would draw off the side of the screen.
        /// </summary>
        public const int MaxStars = 8;

        /// <summary>
        /// The ladder a body that predates the field falls back to, and the shipped one.
        ///
        /// It exists for the same reason <c>AvatarCatalog</c> keeps a built-in roster: a
        /// grove whose catalog is a version behind must still be able to draw its own score,
        /// and a missing ladder is not a reason to show none.
        /// </summary>
        public static readonly GroveScoreTable Default =
            new GroveScoreTable(new long[] { 10_000, 20_000, 50_000, 100_000, 200_000 });

        readonly long[] _at;

        public GroveScoreTable(IReadOnlyList<long> thresholds)
        {
            var kept = new List<long>(thresholds?.Count ?? 0);

            if (thresholds != null)
                foreach (long at in thresholds)
                    if (at > 0L && !kept.Contains(at)) kept.Add(at);

            kept.Sort();
            if (kept.Count > MaxStars) kept.RemoveRange(MaxStars, kept.Count - MaxStars);

            _at = kept.ToArray();
        }

        /// <summary>How many stars this ladder can award.</summary>
        public int StarCount => _at.Length;

        /// <summary>The score that earns <paramref name="star"/>, counting from 1. Zero if there is no such rung.</summary>
        public long At(int star)
            => star >= 1 && star <= _at.Length ? _at[star - 1] : 0L;

        /// <summary>What the top of the ladder asks for, or 0 for a ladder with no rungs.</summary>
        public long Top => _at.Length == 0 ? 0L : _at[_at.Length - 1];

        /// <summary>How many stars a score has earned.</summary>
        public int StarsFor(long score)
        {
            int stars = 0;
            for (int i = 0; i < _at.Length; i++)
                if (score >= _at[i]) stars = i + 1;

            return stars;
        }

        /// <summary>The rungs, ascending. For a validator and a panel that prints the ladder.</summary>
        public IReadOnlyList<long> Thresholds => _at;
    }

    /// <summary>
    /// What a grove is worth and what that has earned: one reading, taken together.
    ///
    /// Held as a struct so a screen cannot draw a score from one moment against stars from
    /// another — the two are derived from the same pass over the same catalog, which is the
    /// bug the win panel's separately-derived reward row spent a version proving is real.
    /// </summary>
    public readonly struct GroveStanding
    {
        /// <summary>Credits' worth of grove the player holds.</summary>
        public readonly long Score;

        public readonly int Stars;

        /// <summary>How many stars the ladder can award, for a row that draws the empty ones.</summary>
        public readonly int StarCount;

        /// <summary>The score the next star asks for, or 0 at the top of the ladder.</summary>
        public readonly long NextAt;

        /// <summary>The score the star already held asked for; 0 below the first rung.</summary>
        public readonly long HeldAt;

        public GroveStanding(long score, int stars, int starCount, long heldAt, long nextAt)
        {
            Score = score;
            Stars = stars;
            StarCount = starCount;
            HeldAt = heldAt;
            NextAt = nextAt;
        }

        /// <summary>True when there is no further star to win.</summary>
        public bool IsTopped => StarCount > 0 && Stars >= StarCount;

        /// <summary>How far along to the next star, 0 to 1. Full at the top of the ladder.</summary>
        public float Progress
        {
            get
            {
                if (IsTopped || NextAt <= 0L) return 1f;

                long span = NextAt - HeldAt;
                if (span <= 0L) return 1f;

                float t = (float)(Score - HeldAt) / span;
                return t < 0f ? 0f : (t > 1f ? 1f : t);
            }
        }

        /// <summary>Credits still to spend before the next star. Zero at the top.</summary>
        public long ToNext => IsTopped || NextAt <= Score ? 0L : NextAt - Score;
    }

    /// <summary>
    /// What a player's grove is worth, in the credits it would take to assemble it.
    ///
    /// <para>
    /// <b>Derived, and therefore free.</b> It adds nothing to <c>SaveFileDto</c>, has no
    /// merge rule, no floor to keep monotonic and no migration — invariant 14's preferred
    /// shape, for the seventh time. Every input is already stored and already synced:
    /// <c>homesteadOwned</c>, <c>companionsOwned</c> and <c>groveLandOwned</c> are three
    /// union-joined id sets that reach the cloud through <c>FirestoreSaveMapper</c>, so a
    /// score computed on one device is the same score on the next one the moment the sets
    /// arrive. Storing the number instead would be a stored count of exactly the kind
    /// invariant 11b forbids, and it would be forgeable in the one direction that matters —
    /// a leaderboard's.
    /// </para>
    /// <para>
    /// <b>It is a value held, never a value spent, and it cannot go down.</b> Every input is
    /// an entitlement and every entitlement here is irreversible, so the score is monotonic
    /// for free — which is why there is no high-water floor beside it. A piece earned by
    /// playing costs nothing and therefore adds nothing, which is the honest reading of
    /// "what would this grove cost": the ladder measures what a player has put into the
    /// place, and the free bench everybody starts with is not that.
    /// </para>
    /// <para>
    /// <b>Everything on a grove shelf counts, residents included.</b> A resident is a
    /// companion (invariant 16a) and the Grovement's own shop sells them beside the fences,
    /// so a companion bought on that shelf that did not move the grove's score would read as
    /// a defect rather than as a distinction. The rule is therefore the plain one — anything
    /// the player holds that could stand in the grove, plus the ground it stands on — which
    /// is also the rule with nothing in it to special-case as the catalog grows.
    /// </para>
    /// <para>
    /// <b>It counts what is held, never what is placed.</b> Since v20 a priced piece is bought
    /// by the copy, so holding ten fences is worth ten fences — but it is the <em>buying</em>
    /// that moves the number and never the arranging. A score over placements would still be
    /// the wrong one: it would fall when a player took a bench off a tile to try it somewhere
    /// else, which is the single most ordinary thing anybody does on that screen.
    /// </para>
    /// <para>
    /// <b>A bundle is worth what it cost, not ten times what it cost.</b> The catalog sells
    /// fences in tens at one price, so a copy is worth <see cref="HomesteadPiece.UnitCost"/> and
    /// the ten of them come back to the price paid. That is the same reading as before v20 —
    /// market value of what is held — and it is why the star ladder needed no retune: a player
    /// who spends 900 credits scores 900 whether the shop handed them one thing or ten.
    /// </para>
    /// </summary>
    public static class GroveScore
    {
        /// <summary>
        /// What this player's grove is worth against a catalog.
        ///
        /// Reads the ledgers rather than taking them as arguments, exactly as
        /// <c>HomesteadLedger.HeldCount</c> does: every caller is a grove screen asking about
        /// the player in front of it.
        /// </summary>
        public static long Value(HomesteadCatalog catalog) => Value(catalog, LedgerHoldings.Instance);

        /// <summary>
        /// The same reading, against holdings supplied rather than read from the ledgers.
        ///
        /// <para>
        /// <b>This overload exists because the rule is now written twice.</b> Since the public
        /// boards, <c>functions/src/grove.ts</c> recomputes a grove's worth from the save so a
        /// forged one cannot rank — and two implementations of one summation drift, which
        /// here surfaces as a player seeing one number over their own grove and a different
        /// one beside their name on a board. That is invariant 9a's failure mode exactly, so
        /// the summation is pinned by shared vectors and this is the seam that lets the
        /// <em>shipped</em> code be the code under test rather than a copy of it.
        /// </para>
        /// <para>
        /// Note what is deliberately not abstracted: which pieces count and which are free.
        /// Those are facts about the catalog and stay here, because they are the half that
        /// must not differ. Only "does this player hold it" is supplied, because that is the
        /// half the two sides answer with different evidence — the client asks its ledgers,
        /// and the server asks a save document it does not believe.
        /// </para>
        /// </summary>
        public static long Value(HomesteadCatalog catalog, IGroveHoldings held)
        {
            if (catalog == null || held == null) return 0L;

            long total = 0L;

            foreach (var piece in catalog.Pieces)
            {
                if (piece.Cost <= 0) continue;

                // Stock is worth what was paid for it: copies times the price of one, which
                // multiplies back to the bundle price. Everything else is an entitlement and is
                // worth its price once — asked through Holds so a resident is still answered by
                // the companion ledger and a home rung by the set that holds it.
                if (piece.IsStocked)
                {
                    long copies = held.Copies(piece);
                    if (copies > 0L) total += copies * piece.UnitCost;
                }
                else if (held.Holds(piece))
                {
                    total += piece.Cost;
                }
            }

            // Cost, so the two kinds of region that are worth nothing here drop out without a
            // clause. Starter land is free and is never written down (invariant 16e); gem-priced
            // land has no credit price at all, and the score is the *credits'* worth of what is
            // held (16g) against a server ceiling denominated in credits (19a) — so a gem cannot
            // be priced into it in either direction. See GroveRegionDto.gems.
            //
            // Starter land is free and is never written down (invariant 16e), so it adds
            // nothing here without needing to be excluded — its cost is zero.
            foreach (var region in catalog.Floor.Regions)
                if (region.Cost > 0 && held.Owns(region)) total += region.Cost;

            return total;
        }

        /// <summary>The whole reading: the score, the stars it earns and the next rung.</summary>
        public static GroveStanding Of(HomesteadCatalog catalog)
            => Standing(Value(catalog), catalog?.Scores);

        /// <summary>The whole reading, against supplied holdings. See <see cref="Value(HomesteadCatalog, IGroveHoldings)"/>.</summary>
        public static GroveStanding Of(HomesteadCatalog catalog, IGroveHoldings held)
            => Standing(Value(catalog, held), catalog?.Scores);

        /// <summary>The reading for a score already in hand. Split out so it can be tested without a ledger.</summary>
        public static GroveStanding Standing(long score, GroveScoreTable table)
        {
            table = table ?? GroveScoreTable.Default;

            int stars = table.StarsFor(score);

            return new GroveStanding(score, stars, table.StarCount,
                                     table.At(stars), table.At(stars + 1));
        }

        /// <summary>
        /// What a complete grove would be worth: every piece and every region of a catalog.
        ///
        /// Nothing in the game reads this — it is for the build gate, which uses it to say
        /// whether the top of the ladder is sensibly placed. Since v20 it is no longer a
        /// <em>ceiling</em>: a player may buy any number of copies of a stocked piece, so a
        /// ladder above this is reachable rather than impossible. It is still the number worth
        /// checking against, because a top star asking for more than one of everything in the
        /// catalog is asking a player to buy the same fence four hundred times.
        /// </summary>
        public static long MaximumValue(HomesteadCatalog catalog)
        {
            if (catalog == null) return 0L;

            long total = 0L;

            foreach (var piece in catalog.Pieces)
                if (piece.Cost > 0) total += piece.Cost;

            foreach (var region in catalog.Floor.Regions)
                if (region.Cost > 0) total += region.Cost;

            return total;
        }
    }

    /// <summary>
    /// Who holds what, for <see cref="GroveScore"/>.
    ///
    /// Two predicates and nothing else, which is the whole of what the score needs to know
    /// about a player. See <see cref="GroveScore.Value(HomesteadCatalog, IGroveHoldings)"/>
    /// for why this is a seam at all.
    /// </summary>
    public interface IGroveHoldings
    {
        bool Holds(HomesteadPiece piece);

        /// <summary>
        /// How many copies of a stocked piece are held. Asked only of a piece for which
        /// <see cref="HomesteadPiece.IsStocked"/> is true; anything else answers through
        /// <see cref="Holds"/>.
        ///
        /// A separate member rather than folding <see cref="Holds"/> into "copies &gt; 0",
        /// because the two questions have different evidence behind them: a companion is held
        /// by a gate and a purchase, a region by a set, and only the shop's half of the catalog
        /// has a number at all. One method returning 0 or 1 for the entitlements would read as
        /// though a second copy of a home rung were a thing that could exist.
        /// </summary>
        int Copies(HomesteadPiece piece);

        bool Owns(GroveRegion region);
    }

    /// <summary>
    /// The player in front of this device, answered by the live ledgers.
    ///
    /// A singleton rather than an allocation per reading, because the grove screen takes this
    /// reading on every repaint and it holds no state of its own.
    /// </summary>
    public sealed class LedgerHoldings : IGroveHoldings
    {
        public static readonly LedgerHoldings Instance = new LedgerHoldings();

        LedgerHoldings() { }

        public bool Holds(HomesteadPiece piece) => HomesteadLedger.IsHeld(piece);
        public int Copies(HomesteadPiece piece) => HomesteadLedger.Copies(piece);
        public bool Owns(GroveRegion region) => GroveLand.IsOwned(region);
    }
}
