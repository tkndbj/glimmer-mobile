using System;
using System.Collections.Generic;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// One keeper, as everybody else is allowed to see them.
    ///
    /// <para>
    /// <b>This exists so the save document never has to be readable by a stranger.</b>
    /// <c>players/{uid}</c> holds the level ledger, the streak's dates, the event floors, the
    /// chest counters and the ad allowance; a leaderboard needs a name, a badge and a number.
    /// Widening the save's read rule to serve that would publish everything else with it and
    /// make the save's shape a public API that can never change again. So a card is a separate
    /// document, written by the server, holding only what a visitor draws — which also makes it
    /// the natural place to put the one number a player now benefits from forging. See
    /// <c>functions/src/grove.ts</c>.
    /// </para>
    /// <para>
    /// <b>The Grovement was removed on 2026-09-21 and this card is what is left of it.</b> The
    /// village a player used to build is gone — with it went the worth, the land, the
    /// placements, the hall's seat and the dwelling, which were the only reason this type ever
    /// referenced <c>Homestead</c>. What stays is the half that was never about the grove: who
    /// this keeper is (name, badge, keeper level), how far they have taken the Infinite lane,
    /// and the turret line they carry. The name on the type is kept deliberately — a card is
    /// still <c>groves/{uid}</c> on the wire, the collection is named in <c>firestore.rules</c>
    /// and in every deployed function, and those spellings are permanent (invariant 19o).
    /// </para>
    /// <para>
    /// <b>Nothing in a card is free text except the name</b>, so <see cref="GroveNames"/> is the
    /// only sanitiser in the feature — there is no string here for anybody to write anything in.
    /// With the arrangement gone, the "walls tile, so somebody writes whatever they like on the
    /// ground" case goes with it: <see cref="ReportSubject.Name"/> is the live judgement and
    /// <see cref="ReportSubject.Grove"/> stays held, because a subject names a collection and a
    /// wire spelling cannot move.
    /// </para>
    /// </summary>
    public sealed class GroveCard
    {
        /// <summary>The account this card belongs to. Empty on a card built for preview.</summary>
        public readonly string OwnerId;

        /// <summary>
        /// What to call this keeper, already in its public form.
        ///
        /// The server decides it — see <see cref="GroveNames"/> for why a client's opinion
        /// about its own name stops being trustworthy the moment strangers read it — and it
        /// is never empty on a card that came from the server, because two unnamed keepers
        /// still need rows that differ.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The rank this keeper holds, as a rung id — the badge a board row and a public
        /// profile draw. Empty for a keeper below the first rung, which is an ordinary state.
        ///
        /// <para>
        /// <b>Derived on both sides and trusted on neither.</b> This is the client's own
        /// reading, taken off the same save file the server will read, and it is here for one
        /// reason only: <see cref="Fingerprint"/>. A rank reached by felling raiders moves
        /// nothing else a visitor can see, so a card that did not carry it would never be
        /// republished and the board would show the old badge for ever — invariant 19j's fault
        /// arriving through a field, which is exactly how the endless wave got onto that list.
        /// What the board actually shows is the server's own answer: <c>rungOf</c> in
        /// <c>functions/src/grove.ts</c> recomputes it from the save it reads itself, because a
        /// number that goes public becomes adjudicated (invariant 19a).
        /// </para>
        /// <para>
        /// <b>A rung id is not a spent id.</b> Nothing in a save holds one, so a retired rung
        /// costs a picture and a string rather than a permanent name — but an unknown one
        /// arriving from a newer server has to draw as <em>nothing</em> rather than as a
        /// rectangle (invariant 7b), which is <c>RankArt.Badge</c>'s job.
        /// </para>
        /// </summary>
        public readonly string RungId;

        /// <summary>Keeper level, for the honorific beside the name.</summary>
        public readonly int KeeperLevel;

        /// <summary>
        /// The furthest wave this keeper has held out to on the Infinite lane, or nought — what
        /// the endless board is ordered on.
        ///
        /// <para>
        /// <b>The one figure on a card the server cannot recompute, and it is on here rather than
        /// anywhere else precisely because of that.</b> A card is already the document a stranger
        /// reads, already written by the server alone, already moderated and already taken down by
        /// one opt-out; a second public document for one integer would be a second publish path, a
        /// second withdrawal, a second thing to moderate and a second thing to forget. What the
        /// server can do is <em>bound</em> it (<see cref="Progression.EndlessLedger.MaxWave"/>) and
        /// make sure it buys nothing — invariant 13 for a number that is not currency.
        /// </para>
        /// <para>
        /// <b>Nought is absent on the wire.</b> The server omits the field for a keeper who has
        /// never played the lane, which is what keeps the index the endless board is built from to
        /// the players who are actually on it rather than to every card in the game.
        /// </para>
        /// <para>
        /// <b>Since the Grovement went, this is the whole of what puts a keeper on a board.</b>
        /// See <see cref="GrovePublishPolicy.WorthPublishing"/>: the worth test it used to share
        /// the decision with cannot be met by anybody any more, so the lane is the only route.
        /// </para>
        /// </summary>
        public readonly int BestWave;

        /// <summary>When the server last rebuilt this card, as a Unix timestamp.</summary>
        public readonly long PublishedUnix;

        readonly List<Wards.WardSlot> _line;
        readonly int[] _rungs;

        public GroveCard(string ownerId, string name, int keeperLevel,
                         int bestWave, long publishedUnix,
                         IReadOnlyList<Wards.WardSlot> line = null,
                         IReadOnlyList<int> rungs = null,
                         string rungId = null)
        {
            OwnerId = ownerId ?? string.Empty;
            Name = name ?? string.Empty;
            RungId = rungId ?? string.Empty;
            KeeperLevel = keeperLevel < 1 ? 1 : keeperLevel;
            BestWave = bestWave < 0 ? 0
                     : bestWave > Progression.EndlessLedger.MaxWave ? Progression.EndlessLedger.MaxWave
                     : bestWave;
            PublishedUnix = publishedUnix < 0L ? 0L : publishedUnix;

            // The seats, in colour order, with a rung beside each. Two lists rather than a pair
            // type because `WardLine.Resolve` takes exactly this shape — the line a visitor draws
            // goes through the same resolver the player's own board does, so a turret this build
            // has never heard of falls back to the starter rather than drawing nothing.
            _line = new List<Wards.WardSlot>(Wards.WardLine.Colours.Length);
            var ladder = new int[Wards.WardLine.Colours.Length];
            for (int i = 0; i < ladder.Length; i++) ladder[i] = Wards.WardStars.Least;

            if (line != null)
                for (int i = 0; i < line.Count; i++)
                {
                    var slot = line[i];
                    int at = Wards.WardLine.Colours.IndexOf(slot.Colour);
                    if (at < 0 || !slot.IsValid) continue;

                    _line.Add(slot);
                    ladder[at] = Wards.WardStars.Sane(rungs != null && i < rungs.Count
                                                      ? rungs[i]
                                                      : Wards.WardStars.Least);
                }

            _rungs = ladder;
        }

        /// <summary>
        /// A card with nothing on it. What a profile renders while the fetch is in flight, and
        /// what a failed fetch leaves behind — so no screen has to hold a null.
        /// </summary>
        public static readonly GroveCard Empty =
            new GroveCard(string.Empty, string.Empty, 1, 0, 0L);

        /// <summary>True once this names an account, which is what a profile needs to draw.</summary>
        public bool IsValid => OwnerId.Length > 0;

        /// <summary>
        /// The seats this keeper has arranged, in colour order. Fewer than four when some of the
        /// line is the roster's starter — see <see cref="Line"/>.
        /// </summary>
        public IReadOnlyList<Wards.WardSlot> Seats => _line;

        /// <summary>
        /// The turret line this keeper carries into a siege, resolved against a catalog.
        ///
        /// <para>
        /// <b>Through <see cref="Wards.WardLine.Resolve"/>, which is the path a board already
        /// takes.</b> A seat the card does not name, a turret this build has never heard of and
        /// one that was retired between drops all land on the roster's starter — the same
        /// fallback the player's own game applies, so a visited line and the line its owner sees
        /// cannot differ for any reason but the catalog.
        /// </para>
        /// <para>
        /// <b>Ownership is not re-asked and must not be</b>, which inverts
        /// <c>WardLoadout.Line</c>'s clause on purpose: that one asks what the player in front of
        /// the device holds *now*, and the only holdings a visitor knows about are their own. The
        /// server has already refused every seat it could not vouch for (<c>publishedLine</c>), so
        /// asking again here would draw four starters for everybody.
        /// </para>
        /// </summary>
        public Wards.WardLine Line(Wards.WardCatalog catalog)
            => Wards.WardLine.Resolve(catalog, _line, null, (model, colour) => StarsOn(colour));

        /// <summary>How far this keeper has taken the turret on a colour. One where they have not.</summary>
        public int StarsOn(char colour)
        {
            int at = Wards.WardLine.Colours.IndexOf(colour);
            return at < 0 ? Wards.WardStars.Least : _rungs[at];
        }

        /// <summary>Whether the keeper has arranged anything at all, as opposed to standing four starters.</summary>
        public bool HasLine => _line.Count > 0;

        // ------------------------------------------------------------- this player
        /// <summary>
        /// The card this device would publish for the player in front of it.
        ///
        /// <para>
        /// <b>Built through the same projection a visitor reads, and that is the point.</b>
        /// The alternative — the profile drawing from a card and the game drawing from the live
        /// ledgers — is two descriptions of one keeper that agree until somebody changes one.
        /// Everything a card can express is built here, once.
        /// </para>
        /// </summary>
        public static GroveCard OfPlayer(string ownerId, string name, int keeperLevel,
                                         long nowUnix)
        {
            Loadout(Wards.WardLoadout.IdFor, Wards.WardStarLedger.StarsOf,
                    out var line, out var rungs);

            // The live ladder for the live card: this is the keeper as they stand on the device,
            // so the rank is read the way the map's own badge reads it. `OfSave` takes the same
            // rung off the file instead, and the two answer identically for a settled save —
            // which is what stops a publish being asked for on every sync.
            return Build(ownerId, name, keeperLevel, EndlessLedger.Best, nowUnix,
                         line, rungs, Ranks.RankLedger.Held?.Id);
        }

        /// <summary>
        /// The card a <em>save file</em> describes — the one a sync has just settled with the
        /// server, which is the save the server will build the real card from.
        ///
        /// <para>
        /// This is what decides whether a publish is owed and what it is asked to prove
        /// (<c>GroveBoard</c>), and it is built from the pushed file rather than from the live
        /// ledgers for one reason: a wave banked while a push is in flight is on the device
        /// and not on the server, and a fingerprint taken from the device would mark it
        /// published when it never was — a card one run behind, permanently, with no error
        /// anywhere. Reading the same file the server holds makes that impossible rather than
        /// unlikely.
        /// </para>
        /// <para>
        /// It answers the same question <see cref="OfPlayer"/> answers, through the same
        /// builder, over the file in place of the ledgers. The name is the file's own, shown the
        /// way the wallet would show it.
        /// </para>
        /// </summary>
        public static GroveCard OfSave(Persistence.SaveFileDto save,
                                       string ownerId, int keeperLevel, long nowUnix)
        {
            string stored = save?.wallet?.displayName;
            string name = string.IsNullOrEmpty(stored) ? Persistence.Wallet.DefaultName : stored;

            // The file's own rows, read the way `WardLoadout.LoadFrom` and
            // `WardStarLedger.LoadFrom` read them — the later row for a colour wins, and a
            // holding with no row stands at the first star.
            var chosen = new Dictionary<char, string>(Wards.WardLine.Colours.Length);
            if (save?.wardLoadout != null)
                foreach (var row in save.wardLoadout)
                {
                    if (row == null || string.IsNullOrEmpty(row.colour)
                        || string.IsNullOrEmpty(row.ward)) continue;

                    char colour = row.colour[0];
                    if (Wards.WardLine.Colours.IndexOf(colour) < 0) continue;

                    chosen[colour] = row.ward;
                }

            var ladder = new Dictionary<string, int>(StringComparer.Ordinal);
            if (save?.wardStars != null)
                foreach (var row in save.wardStars)
                {
                    if (row == null || string.IsNullOrEmpty(row.ward)) continue;
                    ladder[row.ward] = row.stars;
                }

            Loadout(colour => chosen.TryGetValue(colour, out string id) ? id : string.Empty,
                    (ward, colour) => ladder.TryGetValue(
                        Wards.WardHolding.Key(ward, colour), out int at)
                        ? at : Wards.WardStars.Least,
                    out var line, out var rungs);

            // The rung the *file* says, never the ledger's — this card is what decides whether a
            // publish is owed, and a rank read off the device would mark a card published that
            // the server has not seen the play behind. It is also the exact reading the server
            // will take, which is what keeps the fingerprint honest rather than merely stable.
            var rung = Ranks.RankLedger.Ladder.Held(
                new Ranks.SaveRankSource(save, Content.GameContent.Index, keeperLevel));

            return Build(ownerId, name, keeperLevel, EndlessLedger.BestIn(save), nowUnix,
                         line, rungs, rung?.Id);
        }

        /// <summary>The one builder both readings go through, so they cannot drift.</summary>
        static GroveCard Build(string ownerId, string name, int keeperLevel, int bestWave,
                               long nowUnix,
                               IReadOnlyList<Wards.WardSlot> line, IReadOnlyList<int> rungs,
                               string rungId)
            => new GroveCard(ownerId,
                             GroveNames.Public(name),
                             keeperLevel,
                             bestWave,
                             nowUnix,
                             line,
                             rungs,
                             rungId);

        /// <summary>
        /// The seats a stored loadout names, and how far each has been taken.
        ///
        /// <para>
        /// <b>The rows the player actually chose, never the resolved four.</b>
        /// <c>WardLoadout.Line</c> fills every gap with the roster's starter, which is right for
        /// a board and wrong for a card: a seat nobody chose would then be published as a
        /// deliberate choice, and — worse — the fingerprint would change the day the roster's
        /// starter did, calling every keeper in the game changed at once.
        /// </para>
        /// <para>
        /// In colour order, because the fingerprint is ordinal-sorted and a card is compared to
        /// another card: the save writes rows in colour order too (<c>WardLoadout.Rows</c>), and
        /// two orders for one line is a publish asked for on every launch.
        /// </para>
        /// </summary>
        static void Loadout(Func<char, string> chosen, Func<string, char, int> stars,
                            out List<Wards.WardSlot> line, out List<int> rungs)
        {
            line = new List<Wards.WardSlot>(Wards.WardLine.Colours.Length);
            rungs = new List<int>(Wards.WardLine.Colours.Length);

            for (int i = 0; i < Wards.WardLine.Colours.Length; i++)
            {
                char colour = Wards.WardLine.Colours[i];

                string ward = chosen(colour);
                if (string.IsNullOrEmpty(ward)) continue;

                line.Add(new Wards.WardSlot(colour, ward));
                rungs.Add(Wards.WardStars.Sane(stars(ward, colour)));
            }
        }

        /// <summary>
        /// A stable fingerprint of everything a visitor can see.
        ///
        /// <para>
        /// What decides whether a publish is owed. A sync that changed a star rating or a
        /// heart count has not changed this keeper, and republishing on every sync would be a
        /// function invocation per player per sync for ever — see
        /// <see cref="GrovePublishPolicy"/>. Ordinal-sorted before hashing because two devices
        /// enumerate in whatever order they please, and a fingerprint that depended on that
        /// would call every card changed every time.
        /// </para>
        /// <para>
        /// <b>Its shape changed when the Grovement went</b>, which costs every account that
        /// already holds a card exactly one republish on the first launch of that build — the
        /// stored fingerprint cannot match, so a publish is asked for once and then never again
        /// until something real moves. That is the intended price: the alternative is keeping
        /// dead parts in the hash so that cards which no longer describe anything go on looking
        /// unchanged.
        /// </para>
        /// </summary>
        public string Fingerprint()
        {
            var parts = new List<string>(_line.Count + 3)
            {
                // The wave is on the card, so a new best is a change a visitor can see and owes
                // a publish. Left out, a keeper could hold out ten waves further than anybody
                // alive and never reach the board they did it for, because nothing else about
                // them had moved — invariant 19j's fault arriving through a field rather than
                // through a stale read.
                "w:" + BestWave.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "n:" + Name,

                // The badge is on every board row, so reaching a rung is a change a visitor
                // sees and owes a publish. It has to be here rather than left to ride on
                // something else moving, because a rung can be reached by a reading nothing
                // else on this card mentions — two and a half thousand raiders felled moves no
                // wave and no name, and the keeper would wear the old badge on every board for
                // ever. Same fault as the endless wave, arriving through a second field.
                "r:" + RungId,
            };

            foreach (var seat in _line)
                parts.Add("t:" + seat.Colour + "=" + seat.Ward + "/" + StarsOn(seat.Colour));

            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
    }
}
