using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// What a <em>save file</em> holds, read the way the ledgers read themselves.
    ///
    /// <para>
    /// <see cref="LedgerHoldings"/> answers for the grove standing on this device;
    /// this answers for a grove written down in a <see cref="SaveFileDto"/> — the one a sync
    /// has just settled with the server, which is the grove the public card is built from
    /// (<c>GroveCard.OfSave</c>). The two must agree for the same state, so every clause here
    /// is the ledger's clause, reached through the ledger's own rule rather than restated:
    /// a resident is held by <see cref="CompanionLedger.IsHeld(AvatarDefinition, int, Func{string, bool})"/>
    /// over the save's companion set, a stocked piece by the save's stock rows read through
    /// <see cref="GroveStock.In"/> (which is what knows a v19 file from a v20 one), and land
    /// by the save's region set. What is <em>not</em> in the save — a free piece earned by
    /// clearing a chapter — is asked of <see cref="HomesteadLedger.IsEarned"/> exactly as the
    /// ledger asks it, because play progress is monotonic and merged before this is ever
    /// built, and a free piece is worth nothing to the score in any case (invariant 16g).
    /// </para>
    /// <para>
    /// It is built once per sync and thrown away. It holds no reference to the file.
    /// </para>
    /// </summary>
    public sealed class SaveHoldings : IGroveHoldings
    {
        readonly GroveStock _stock = new GroveStock();
        readonly HashSet<string> _companions;
        readonly HashSet<string> _land;
        readonly int _keeperLevel;

        public SaveHoldings(SaveFileDto save, int keeperLevel)
        {
            _keeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
            _stock.LoadFrom(GroveStock.In(save));
            _companions = Ids(save?.companionsOwned);
            _land = Ids(save?.groveLandOwned);
        }

        public bool Holds(HomesteadPiece piece)
        {
            if (!piece.IsValid) return false;

            if (piece.IsResident)
                return CompanionLedger.IsHeld(GroveResidents.CompanionOf(piece), _keeperLevel,
                                              _companions.Contains);

            return _stock.Any(piece.Id) || HomesteadLedger.IsEarned(piece);
        }

        public int Copies(HomesteadPiece piece)
            => piece.IsStocked ? _stock.Of(piece.Id) : 0;

        public bool Owns(GroveRegion region)
            => region != null && region.IsValid
            && (region.IsStarter || _land.Contains(region.Id));

        static HashSet<string> Ids(string[] raw)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (raw == null) return set;

            foreach (string id in raw)
                if (!string.IsNullOrEmpty(id)) set.Add(id);

            return set;
        }
    }
}
