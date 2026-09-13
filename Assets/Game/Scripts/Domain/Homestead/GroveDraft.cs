using System;

namespace GlimmerGrove.Homestead
{
    /// <summary>Where the thing being placed came from, which decides what cancelling means.</summary>
    public enum GroveDraftSource
    {
        /// <summary>Chosen from the inventory. Nothing has been spent, so cancelling costs nothing.</summary>
        Stock,

        /// <summary>Lifted off a tile. Cancelling has to put it back exactly where it was.</summary>
        Floor,

        /// <summary>
        /// The hall, lifted off its seat. It behaves like a floor draft in every way a player
        /// can see — it drags, it turns, it lights green or red — and differs in exactly two:
        /// it is written through <see cref="HomesteadLayout.MoveHall"/> rather than as a
        /// placement row, and it can never be taken away. There is no such thing as a grove
        /// without a home, so <see cref="GroveDraft.Remove"/> refuses it rather than the bar
        /// hiding a button and hoping (a control that is sometimes absent is a control nobody
        /// learns, and a control that is present and does nothing is worse).
        /// </summary>
        Hall,
    }

    /// <summary>
    /// A placement the player is still deciding about: what they are putting down, where it is
    /// hovering, which way it faces, and whether that is legal.
    ///
    /// <para>
    /// <b>Why this is a type and not a handful of fields on the screen.</b> Placing something was
    /// a state machine smeared across a 1,465-line <c>MonoBehaviour</c> — an <c>_editing</c>
    /// flag, a <c>_dragging</c> flag, a ghost, two mark sets, a cached plan and four loose
    /// coordinates — so "where will this land" was answered in one place, "does it fit" in
    /// another, and "what gets written" in a third. Those three have to agree on every frame of
    /// a drag, and nothing made them. They are one object now, and the view asks it rather than
    /// keeping its own copy.
    /// </para>
    /// <para>
    /// <b>It is Domain because it is a rule, not a drawing.</b> Whether a wall fits on a tile is
    /// the same question whether a finger is dragging it or a test is asserting about it, and
    /// this is the half that can be proved without an Editor.
    /// </para>
    /// <para>
    /// <b>What you see is what you occupy.</b> The footprint here is the one the commit writes
    /// and the one the view lights up — read once, from the catalogue, turned by the facing. A
    /// piece that painted four tiles and held one was the report that started this rework, and
    /// the only durable fix is that there is no second answer to ask.
    /// </para>
    /// </summary>
    public sealed class GroveDraft
    {
        readonly HomesteadCatalog _catalog;

        /// <summary>The piece being placed. Never a dwelling — see <see cref="FromStock"/>.</summary>
        public readonly string PieceId;

        public readonly GroveDraftSource Source;

        /// <summary>Where it was lifted from, or empty when it came from the inventory.</summary>
        public readonly string FromSlot;

        /// <summary>Where it stood when it was lifted, so cancelling can put it back exactly.</summary>
        readonly int _fromCol, _fromRow, _fromFacing;

        /// <summary>The anchor tile it is hovering over: the smallest column and row it covers.</summary>
        public int Col { get; private set; }
        public int Row { get; private set; }

        /// <summary>Which quarter turn it is drawn at, 0..3.</summary>
        public int Facing { get; private set; }

        GroveDraft(HomesteadCatalog catalog, string pieceId, GroveDraftSource source,
                   string fromSlot, int col, int row, int facing)
        {
            _catalog = catalog;
            PieceId = pieceId;
            Source = source;
            FromSlot = fromSlot ?? string.Empty;
            _fromCol = Col = col;
            _fromRow = Row = row;
            _fromFacing = Facing = GroveFootprint.Quarter(facing);
        }

        // ------------------------------------------------------------------ making
        /// <summary>
        /// A piece chosen from the inventory, hovering over a tile.
        ///
        /// Refuses what the picker already refuses, because a picker is not where a rule lives:
        /// a dwelling is drawn from the best home owned rather than placed (invariant 16), and a
        /// piece with no copy left is one the player does not hold.
        /// </summary>
        public static GroveDraft FromStock(HomesteadCatalog catalog, string pieceId, int col, int row)
        {
            if (catalog == null || string.IsNullOrEmpty(pieceId)) return null;

            var piece = catalog.Find(pieceId);
            if (!piece.IsValid || !piece.CanBePlaced) return null;
            if (!HomesteadLedger.CanPlace(piece)) return null;

            return new GroveDraft(catalog, pieceId, GroveDraftSource.Stock, string.Empty, col, row, 0);
        }

        /// <summary>
        /// The piece standing on a tile, lifted off it.
        ///
        /// <para>
        /// It resolves through the whole stand rather than the tile, so touching the far end of
        /// a bridge lifts the bridge. The hall is refused: it is derived from the best home
        /// owned, so it can be neither picked up nor put down (invariant 16).
        /// </para>
        /// <para>
        /// <b>Nothing is written when it is lifted.</b> The piece stays in the save exactly
        /// where it was until <see cref="Commit"/>, so a draft abandoned by backing out of the
        /// screen, a crash, or a sync landing mid-drag leaves the grove untouched. What the
        /// <em>view</em> does is hide the original while the ghost is up, which is a drawing
        /// decision and reverses itself.
        /// </para>
        /// </summary>
        public static GroveDraft FromFloor(HomesteadCatalog catalog, int col, int row)
        {
            if (catalog == null) return null;
            if (!HomesteadLayout.TryStandAt(catalog, col, row, out var stand)) return null;
            if (!stand.IsValid) return null;

            // **The hall is liftable now, and what changed is where its seat lives rather than
            // anything about drafting.** It used to be refused here because the seat was
            // *content* — `GroveFloor.HallTile`, one tile for every grove in the world — so
            // there was nowhere to write a move to. It is player state (save v26), so a home is
            // an ordinary thing to drag; the piece standing on it is still derived from the best
            // home owned, which is the half invariant 16 was really about.
            var source = stand.IsHall ? GroveDraftSource.Hall : GroveDraftSource.Floor;

            return new GroveDraft(catalog, stand.PieceId, source, stand.AnchorId,
                                  stand.AnchorCol, stand.AnchorRow, stand.Facing);
        }

        // ----------------------------------------------------------------- reading
        /// <summary>The piece's own record, or an invalid one if the catalogue has changed under us.</summary>
        public HomesteadPiece Piece => _catalog == null ? default : _catalog.Find(PieceId);

        /// <summary>
        /// The tiles this would occupy, facing as it does — <b>the same value the commit writes
        /// and the view lights</b>. There is deliberately no other way to ask.
        /// </summary>
        public GroveFootprint Footprint
        {
            get
            {
                var piece = Piece;
                return (piece.IsValid ? piece.Footprint : GroveFootprint.Single).Facing(Facing);
            }
        }

        /// <summary>The stand this would become. What the view draws the ghost from.</summary>
        public GroveStand Stand => new GroveStand(Col, Row, PieceId, Facing, Footprint);

        /// <summary>
        /// Whether turning this would change anything — the one thing the TURN key may be built
        /// on, and the same answer <see cref="Turn"/> itself obeys.
        ///
        /// <para>
        /// <b>It is two clauses because a facing is two different facts.</b> A piece with four
        /// facings is four <em>pictures</em>, so turning one is visibly a turn; a piece with a
        /// footprint that is not square covers different <em>tiles</em> at an odd quarter
        /// (<see cref="GroveFootprint.Facing"/>), so turning one is a real move even where the
        /// art never changes. Either alone is enough, and a piece with neither cannot be turned
        /// in any sense a player could see: the roster gives a barrel, a boulder and sixteen
        /// others one facing for exactly that reason, and a resident — a companion, drawn from
        /// one flat portrait or flipbook — is the same case arrived at from the other direction
        /// (<c>GroveResidents.From</c> mints them 1x1 at one facing).
        /// </para>
        /// <para>
        /// <b>Why it is a rule rather than a button the view happens not to draw.</b> Invariant
        /// 16o: what is lit, what will be written and whether the control is live are one
        /// answer, here. A view that hid the key while <c>Turn</c> still turned would leave a
        /// draft able to write a facing of 1 through 3 against a piece that has no second
        /// picture — a row in the save that says something no screen can show, carried by the
        /// merge for ever.
        /// </para>
        /// </summary>
        public bool CanTurn
        {
            get
            {
                var piece = Piece;
                if (!piece.IsValid) return false;

                var drawn = piece.Footprint;
                return piece.Facings > 1 || drawn.Cols != drawn.Rows;
            }
        }

        /// <summary>Where a lifted piece was standing, and nought for one out of the inventory.</summary>
        public int FromCol => _fromCol;

        /// <summary>Where a lifted piece was standing, and nought for one out of the inventory.</summary>
        public int FromRow => _fromRow;

        /// <summary>
        /// The tiles a lifted piece was covering before it was picked up, <b>facing the way it
        /// was standing</b> rather than the way it is being held.
        ///
        /// <para>
        /// It exists because the view got this wrong by asking the piece instead: a footprint
        /// read straight off the catalogue is the piece as it was <em>drawn</em>, and an odd
        /// quarter swaps its axes (<see cref="GroveFootprint.Facing"/>). So a fence that had
        /// been turned lit its origin across the grain — three tiles the wrong way — and the
        /// mark meant to say <em>this is what you are leaving</em> pointed at tiles the piece
        /// had never been on. Asking the draft is the same argument <see cref="Footprint"/>
        /// makes for the other end of the move: there is one answer, and it is here.
        /// </para>
        /// </summary>
        public GroveFootprint FromFootprint
        {
            get
            {
                var piece = Piece;
                return (piece.IsValid ? piece.Footprint : GroveFootprint.Single).Facing(_fromFacing);
            }
        }

        /// <summary>
        /// Whether it would go down where it is hovering.
        ///
        /// <para>
        /// A draft lifted off the floor ignores <em>itself</em>, or a piece could never be put
        /// back where it came from and turning one in place would always be refused.
        /// </para>
        /// </summary>
        public bool Fits => _catalog != null
                         && HomesteadLayout.Occupancy(_catalog)
                                .Fits(_catalog.Floor, Footprint, Col, Row, Ground, Ignoring);

        /// <summary>
        /// Which ground this draft may stand on, and the hall is the one thing that asks a
        /// different question.
        ///
        /// <para>
        /// <see cref="GroveLand.IsBuildable"/> is owned ground <em>the hall is not standing
        /// on</em>, which is exactly right for everything else and self-defeating for the hall:
        /// it would refuse every seat overlapping where the house already is, so a home could be
        /// moved a long way and never a short one, and turning it on the spot would be refused
        /// outright. Ownership is the real rule, and the hall's own tiles are freed the same way
        /// a moving piece's are — by <see cref="Ignoring"/>.
        /// </para>
        /// </summary>
        Func<int, int, bool> Ground
            => Source == GroveDraftSource.Hall
                ? (Func<int, int, bool>)((c, r) => GroveLand.IsOwned(_catalog.Floor, c, r))
                : (c, r) => GroveLand.IsBuildable(_catalog.Floor, c, r);

        /// <summary>Whether it is standing exactly where it started, facing the way it did.</summary>
        public bool Unmoved => Source != GroveDraftSource.Stock
                            && Col == _fromCol && Row == _fromRow && Facing == _fromFacing;

        /// <summary>The stand a move may overlap: its own former self, or nothing.</summary>
        long Ignoring => Source == GroveDraftSource.Stock
            ? GroveOccupancy.NoKey
            : GroveOccupancy.Key(_fromCol, _fromRow);

        // ------------------------------------------------------------------ moving
        /// <summary>
        /// Puts the ghost under a tile, centred on it and kept on the floor.
        ///
        /// <para>
        /// <b>Centred rather than anchored.</b> The anchor is the back corner, so hanging a
        /// four-tile wall off the touched tile would draw it entirely to one side of the finger.
        /// The player is looking at the ghost, so the ghost goes where they are pointing.
        /// </para>
        /// <para>
        /// <b>And it never shifts itself to make something fit.</b> The blind placement path
        /// searches outward for a free anchor (<c>GroveOccupancy.TryFit</c>), which is right when
        /// nothing is drawn and wrong the moment something is: a ghost that jumped aside as the
        /// finger crossed an occupied tile would be the control arguing with the player. It
        /// hovers where it was put and says it does not fit; <see cref="Fits"/> is what the view
        /// paints red and what <see cref="Commit"/> refuses on.
        /// </para>
        /// <para>
        /// Clamping is the one exception, and it is not a choice — a footprint hanging off the
        /// edge of the world has no tiles to occupy at all.
        /// </para>
        /// </summary>
        public bool MoveTo(int touchCol, int touchRow)
        {
            if (_catalog == null) return false;

            var footprint = Footprint;
            var floor = _catalog.Floor;

            int col = touchCol - (footprint.Cols - 1) / 2;
            int row = touchRow - (footprint.Rows - 1) / 2;

            col = Clamp(col, 0, floor.Cols - footprint.Cols);
            row = Clamp(row, 0, floor.Rows - footprint.Rows);

            if (col == Col && row == Row) return false;

            Col = col;
            Row = row;
            return true;
        }

        /// <summary>
        /// Turns it a quarter, and shifts it back onto the floor if the turn hung it off an edge.
        ///
        /// <para>
        /// It turns even when the result does not fit, which is the opposite of what the old
        /// in-place turn did. A draft is not committed, so an illegal facing costs nothing and
        /// says something: the player sees the footprint go red and turns again. Refusing would
        /// make the button dead for reasons they cannot see.
        /// </para>
        /// </summary>
        public void Turn()
        {
            // Nothing to turn is not the same as a turn that does not fit: the second is the
            // case the remarks above are about and still happens, while this one is a piece
            // with one picture on a square footprint, where a quarter is invisible on the
            // screen and a fact in the save. See CanTurn.
            if (!CanTurn) return;

            Facing = GroveFootprint.Quarter(Facing + 1);

            var footprint = Footprint;
            var floor = _catalog?.Floor;
            if (floor == null) return;

            Col = Clamp(Col, 0, floor.Cols - footprint.Cols);
            Row = Clamp(Row, 0, floor.Rows - footprint.Rows);
        }

        // ---------------------------------------------------------------- finishing
        /// <summary>
        /// Writes it down, or says why it cannot be.
        ///
        /// <para>
        /// The only path from a draft to the save file, so the footprint that was shown is the
        /// footprint that is taken. A draft that has not moved is <c>Unchanged</c> rather than a
        /// write, because an untouched slot writing a row is how a merge comes to have an
        /// opinion nobody formed (invariant 16).
        /// </para>
        /// </summary>
        public GrovePlaceResult Commit()
        {
            if (_catalog == null) return GrovePlaceResult.Refused;
            if (!Fits) return GrovePlaceResult.NoRoom;
            if (Unmoved) return GrovePlaceResult.Unchanged;

            switch (Source)
            {
                // The hall has a seat of its own rather than a placement row, because what is
                // standing on it is still derived from the best home owned: writing it as a
                // placement would be a second record of which house the player has, and two
                // records of one fact are two things a merge can disagree about (invariant 16).
                case GroveDraftSource.Hall:
                    return HomesteadLayout.MoveHall(_catalog, Col, Row, Facing);

                case GroveDraftSource.Floor:
                    return HomesteadLayout.Rest(_catalog, FromSlot, Col, Row, Facing);

                default:
                    return HomesteadLayout.Rest(_catalog, string.Empty, Col, Row, Facing, PieceId);
            }
        }

        /// <summary>
        /// Takes the piece off the floor and back into stock. Only a draft lifted from a tile
        /// has anything to take away; one from the inventory was never put down.
        /// </summary>
        public GrovePlaceResult Remove()
        {
            // A home cannot be taken away — there is no grove without one, and the piece
            // standing here is derived from what the player owns rather than from a row that
            // could be cleared. Refused rather than absent: see `GroveDraftSource.Hall`.
            if (_catalog == null || Source != GroveDraftSource.Floor) return GrovePlaceResult.Refused;
            return HomesteadLayout.Clear(FromSlot) ? GrovePlaceResult.Placed : GrovePlaceResult.Unchanged;
        }

        /// <summary>
        /// Abandons it. Nothing was written when it was lifted, so there is nothing to undo —
        /// this only puts the draft back where it started so a view can animate it home.
        /// </summary>
        public void Cancel()
        {
            Col = _fromCol;
            Row = _fromRow;
            Facing = _fromFacing;
        }

        static int Clamp(int value, int low, int high)
            => high < low ? low : value < low ? low : value > high ? high : value;
    }
}
