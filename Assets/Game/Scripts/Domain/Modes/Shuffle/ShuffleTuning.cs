using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GlimmerGrove.Content;
using GlimmerGrove.Modes;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The Shuffle ramp's figures for one run (MODES.md 59l). <see cref="Shipped"/> is
    /// <see cref="ShuffleRamp"/>'s constants.
    /// </summary>
    public sealed class ShuffleRampTuning
    {
        /// <summary>
        /// One figure: its key in the document, what ships, and the range a document may move it
        /// in - wide enough to tune, narrow enough that no value in it breaks a board.
        /// </summary>
        public readonly struct Figure
        {
            public readonly string Key;
            public readonly int Shipped, Least, Most;

            public Figure(string key, int shipped, int least, int most)
            {
                Key = key;
                Shipped = shipped;
                Least = least;
                Most = most;
            }
        }

        // In the order the constructor takes them. A figure is added at the end.
        static readonly Figure[] Table =
        {
            new Figure("firstWave", ShuffleRamp.FirstWave, 1, SiegeLayout.MaxRaiders),
            new Figure("mostRaiders", ShuffleRamp.MostRaiders, 1, SiegeLayout.MaxRaiders),
            new Figure("healthStepTwentieths", ShuffleRamp.HealthStepTwentieths, 1, 20),
            new Figure("blowEveryWaves", ShuffleRamp.BlowEveryWaves, 1, 20),
            new Figure("brutesFrom", ShuffleRamp.BrutesFrom, 1, 99),
            new Figure("bulwarksFrom", ShuffleRamp.BulwarksFrom, 1, 99),
            new Figure("bruteShareMost", ShuffleRamp.BruteShareMost, 1, 90),
            new Figure("bulwarkShareMost", ShuffleRamp.BulwarkShareMost, 1, 90),
            new Figure("shareStep", ShuffleRamp.ShareStep, 1, 50),
            new Figure("cursedWave", ShuffleRamp.CursedWave, 1, 999),
            new Figure("voidWave", ShuffleRamp.VoidWave, 1, 999),
            // The deep surge (ShuffleRamp.SurgeFrom). All four per cents at nought switches it off.
            new Figure("surgeFromWave", ShuffleRamp.SurgeFrom, 1, 999),
            new Figure("surgeEveryWaves", ShuffleRamp.SurgeEvery, 1, 100),
            new Figure("surgeHealthPercent", ShuffleRamp.SurgeHealthPercent, 0, 50),
            new Figure("surgeHealthPercentStep", ShuffleRamp.SurgeHealthPercentStep, 0, 20),
            new Figure("surgeBlowPercent", ShuffleRamp.SurgeBlowPercent, 0, 50),
            new Figure("surgeBlowPercentStep", ShuffleRamp.SurgeBlowPercentStep, 0, 20),
            // The pace surge, from the wave after health reaches its ceiling. Both per cents at nought switch it off.
            new Figure("surgePacePercent", ShuffleRamp.SurgePacePercent, 0, 50),
            new Figure("surgePacePercentStep", ShuffleRamp.SurgePacePercentStep, 0, 20),
            new Figure("surgePaceMostHundredths", ShuffleRamp.SurgePaceMostHundredths, 100, ShuffleRamp.MostPaceHundredths),
        };

        public static IReadOnlyList<Figure> Figures => Table;

        public static readonly ShuffleRampTuning Shipped = new ShuffleRampTuning(Written(Table));

        public readonly int FirstWave, MostRaiders, HealthStepTwentieths, BlowEveryWaves, BrutesFrom,
                            BulwarksFrom, BruteShareMost, BulwarkShareMost, ShareStep, CursedWave, VoidWave,
                            SurgeFrom, SurgeEvery, SurgeHealthPercent, SurgeHealthPercentStep,
                            SurgeBlowPercent, SurgeBlowPercentStep,
                            SurgePacePercent, SurgePacePercentStep, SurgePaceMostHundredths;

        /// <summary>
        /// The first wave whose health stands at <see cref="ShuffleRamp.MostHealthTenths"/> on
        /// these figures (<see cref="int.MaxValue"/> for never), where the pace surge takes over.
        /// Derived, never written: worked out once, as the figures are taken, so a wave asks a
        /// field rather than a search.
        /// </summary>
        public readonly int HealthCeilingWave;

        readonly int[] _written;

        ShuffleRampTuning(int[] w)
        {
            _written = (int[])w.Clone();

            int i = 0;
            FirstWave = w[i++];
            MostRaiders = w[i++];
            HealthStepTwentieths = w[i++];
            BlowEveryWaves = w[i++];
            BrutesFrom = w[i++];
            BulwarksFrom = w[i++];
            BruteShareMost = w[i++];
            BulwarkShareMost = w[i++];
            ShareStep = w[i++];
            CursedWave = w[i++];
            VoidWave = w[i++];
            SurgeFrom = w[i++];
            SurgeEvery = w[i++];
            SurgeHealthPercent = w[i++];
            SurgeHealthPercentStep = w[i++];
            SurgeBlowPercent = w[i++];
            SurgeBlowPercentStep = w[i++];
            SurgePacePercent = w[i++];
            SurgePacePercentStep = w[i++];
            SurgePaceMostHundredths = w[i++];

            // Last: it reads the figures above.
            HealthCeilingWave = ShuffleRamp.CeilingWaveOf(this);
        }

        /// <summary>The ramp a document's figures describe, or null with the first fault. All shipped answers <see cref="Shipped"/>.</summary>
        public static ShuffleRampTuning FromWritten(int[] written, out string fault)
        {
            if (!ShuffleTuning.InRange("ramp", Table, written, f => (f.Key, f.Shipped, f.Least, f.Most), out bool shipped, out fault))
                return null;

            // The one figure whose range is another figure.
            if (written[1] < written[0])
            {
                fault = $"ramp.mostRaiders is {written[1]}, fewer than ramp.firstWave ({written[0]}) - a wave would send fewer than the first";
                return null;
            }

            return shipped ? Shipped : new ShuffleRampTuning(written);
        }

        public int[] Written() => (int[])_written.Clone();

        static int[] Written(Figure[] table)
        {
            var w = new int[table.Length];
            for (int i = 0; i < table.Length; i++) w[i] = table[i].Shipped;
            return w;
        }
    }

    /// <summary>
    /// The Shuffle deck's figures for one run (MODES.md 59l): the three tier tables, the hands
    /// they start at and the two promises. <see cref="Shipped"/> is <see cref="ShuffleDeck"/>'s.
    /// </summary>
    public sealed class ShuffleDeckTuning
    {
        public static readonly ShuffleDeckTuning Shipped =
            new ShuffleDeckTuning(ShuffleDeck.Weights, ShuffleDeck.Heated, ShuffleDeck.Wild,
                                  ShuffleDeck.HeatedFrom, ShuffleDeck.WildFrom,
                                  ShuffleDeck.RarePity, ShuffleDeck.EpicPity);

        readonly int[] _opening, _heated, _wild;

        public readonly int HeatedFrom, WildFrom, RarePity, EpicPity;

        ShuffleDeckTuning(int[] opening, int[] heated, int[] wild, int heatedFrom, int wildFrom,
                          int rarePity, int epicPity)
        {
            _opening = opening;
            _heated = heated;
            _wild = wild;
            HeatedFrom = heatedFrom;
            WildFrom = wildFrom;
            RarePity = rarePity;
            EpicPity = epicPity;
        }

        public IReadOnlyList<int> Opening => _opening;
        public IReadOnlyList<int> Heated => _heated;
        public IReadOnlyList<int> Wild => _wild;

        /// <summary>The tier table hand <paramref name="hand"/> (from one) is dealt off - <see cref="ShuffleDeck.WeightsFor"/>'s rule.</summary>
        internal int[] TableFor(int hand) => hand >= WildFrom ? _wild : hand >= HeatedFrom ? _heated : _opening;

        /// <summary>The tier table hand <paramref name="hand"/> is dealt off, for a reader outside the deal.</summary>
        public IReadOnlyList<int> Table(int hand) => TableFor(hand);

        /// <summary>The deck a document describes, or null with the first fault. All shipped answers <see cref="Shipped"/>.</summary>
        public static ShuffleDeckTuning From(int[] opening, int[] heated, int[] wild, int heatedFrom,
                                             int wildFrom, int rarePity, int epicPity, out string fault)
        {
            if (!Weights("deck.opening", opening, out fault) || !Weights("deck.heated", heated, out fault) ||
                !Weights("deck.wild", wild, out fault))
                return null;

            if (heatedFrom < 2 || heatedFrom > 50)
            {
                fault = $"deck.heatedFromHand is {heatedFrom}; it may move from 2 to 50 (ships at {ShuffleDeck.HeatedFrom})";
                return null;
            }

            if (wildFrom <= heatedFrom || wildFrom > 99)
            {
                fault = $"deck.wildFromHand is {wildFrom}; it must come after deck.heatedFromHand ({heatedFrom}) and by 99 (ships at {ShuffleDeck.WildFrom})";
                return null;
            }

            if (rarePity < 2 || rarePity > 20 || epicPity < 2 || epicPity > 20)
            {
                fault = $"deck.rareEveryHands ({rarePity}) and deck.epicEveryHands ({epicPity}) may each move from 2 to 20 " +
                        $"(ship at {ShuffleDeck.RarePity} and {ShuffleDeck.EpicPity})";
                return null;
            }

            var s = Shipped;
            if (Same(opening, s._opening) && Same(heated, s._heated) && Same(wild, s._wild) &&
                heatedFrom == s.HeatedFrom && wildFrom == s.WildFrom && rarePity == s.RarePity && epicPity == s.EpicPity)
                return s;

            return new ShuffleDeckTuning((int[])opening.Clone(), (int[])heated.Clone(), (int[])wild.Clone(),
                                         heatedFrom, wildFrom, rarePity, epicPity);
        }

        /// <summary>A tier table: one weight per tier, none negative, summing to a hundred (a weight is only a probability when the whole is known).</summary>
        static bool Weights(string name, int[] table, out string fault)
        {
            fault = null;
            int tiers = (int)ShuffleTier.Legendary + 1;

            if (table == null || table.Length != tiers)
            {
                fault = $"{name} must list {tiers} weights (common, rare, epic, legendary)";
                return false;
            }

            int sum = 0;
            foreach (int w in table)
            {
                if (w < 0 || w > 100)
                {
                    fault = $"{name} holds {w}; a weight is from 0 to 100";
                    return false;
                }

                sum += w;
            }

            if (sum != 100)
            {
                fault = $"{name} sums to {sum}, not 100";
                return false;
            }

            return true;
        }

        static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }

    /// <summary>
    /// <b>Everything about the Shuffle lane that may be retuned without a build</b> (MODES.md
    /// 59l): the ramp, the deck, every card's figures and whether it is dealt, and the arsenal's
    /// clocks. Immutable; a run holds one for its whole life (<see cref="ShuffleRun.Tuning"/>).
    ///
    /// <para>
    /// <b><see cref="Shipped"/> is the build's own figures and is the code's constants</b> - the
    /// same objects, the same card instances in the same order - so a run on it is the run every
    /// build before this type played, frame for frame. It is what a player gets with no network
    /// ever, and what a bad or withdrawn document falls back to (<c>ShuffleConfig</c>).
    /// </para>
    /// <para>
    /// <b>A document is taken whole or not at all</b> (<see cref="TryRead"/>): every figure must be
    /// stated and in its range, or the document is refused with the first fault named and the
    /// device keeps what it had. What a document cannot do is as much the contract as what it
    /// can: it cannot add a card or an effect (code, 59d), cannot move a figure a card's note
    /// says in words (<see cref="ShuffleNotes"/>), cannot change a hand's size or how often one
    /// is owed (both written into the lane's own sentences in nine languages), and cannot reach
    /// anything that is not this lane's - the chapters and the Infinite lane read none of it.
    /// </para>
    /// <para>
    /// <b>Two readings of one document.</b> <em>Strict</em> is the publisher's
    /// (<c>ShuffleTuningTests.ThePublishedFileIsAccepted</c>, run by <c>seed-shuffle.mjs</c>
    /// before it writes): an unknown key, an unknown or retired card or a card left out is a
    /// fault, because each is a typo on the way to every phone. <em>Lenient</em> is the device's:
    /// it ignores a key or a card it does not know and keeps a card left out at its shipped
    /// figures, so a document written for a newer build - one with a card or a figure this one
    /// lacks - still tunes this build rather than being refused by it. Everything else is the
    /// same check. A change of meaning, not of figures, is a new schema and a new field on the
    /// published document (<c>schema2</c> beside <c>schema1</c>), never a reinterpretation.
    /// </para>
    /// </summary>
    public sealed class ShuffleTuning
    {
        /// <summary>The document shape this build reads. Published under <c>schema1</c>.</summary>
        public const int Schema = 1;

        /// <summary>The longest document read. A whole tuning is a few kilobytes.</summary>
        public const int MostLength = 64 * 1024;

        public static readonly ShuffleTuning Shipped =
            new ShuffleTuning(0, ShuffleRampTuning.Shipped, ShuffleDeckTuning.Shipped,
                              SiegeArsenalTuning.Shipped, ShuffleCards.All, ShuffleRefreshTuning.Shipped);

        readonly ShuffleCard[] _cards;
        readonly Dictionary<string, ShuffleCard> _byId;

        ShuffleTuning(int revision, ShuffleRampTuning ramp, ShuffleDeckTuning deck,
                      SiegeArsenalTuning arsenal, ShuffleCard[] cards, ShuffleRefreshTuning refresh)
        {
            Revision = revision;
            Ramp = ramp;
            Deck = deck;
            Arsenal = arsenal;
            Refresh = refresh ?? ShuffleRefreshTuning.Shipped;
            _cards = cards;

            _byId = new Dictionary<string, ShuffleCard>(StringComparer.Ordinal);
            foreach (var card in cards) _byId[card.Id] = card;
        }

        /// <summary>The published revision these figures came from, or nought for the build's own.</summary>
        public int Revision { get; }

        public ShuffleRampTuning Ramp { get; }

        public ShuffleDeckTuning Deck { get; }

        public SiegeArsenalTuning Arsenal { get; }

        /// <summary>What a refresh of a hand costs (MODES.md 59p).</summary>
        public ShuffleRefreshTuning Refresh { get; }

        /// <summary>Every card a hand may deal, in catalog order: the switched-off ones are not here.</summary>
        public IReadOnlyList<ShuffleCard> Cards => _cards;

        /// <summary>The dealt card with this id, or null for one this tuning does not deal.</summary>
        public ShuffleCard Find(string id)
            => id != null && _byId.TryGetValue(id, out var card) ? card : null;

        // ------------------------------------------------------------------ reading
        /// <summary>
        /// Reads a published document, whole or not at all. Never throws: anything wrong is a
        /// fault naming what and where, and <paramref name="tuning"/> is null.
        /// </summary>
        public static bool TryRead(string json, bool strict, out ShuffleTuning tuning, out string fault)
        {
            tuning = null;

            try
            {
                tuning = Read(json, strict, out fault);
                return tuning != null;
            }
            catch (Exception e)
            {
                // A reader that throws on a document is a reader the document can break, which is
                // the failure this whole type exists to refuse. Nothing above should; this is the
                // floor under it.
                fault = "the document could not be read: " + e.Message;
                tuning = null;
                return false;
            }
        }

        static ShuffleTuning Read(string json, bool strict, out string fault)
        {
            if (string.IsNullOrEmpty(json)) { fault = "the document is empty"; return null; }
            if (json.Length > MostLength) { fault = $"the document is {json.Length} characters, over {MostLength}"; return null; }

            if (!StrictJson.TryParse(json, out object parsed, out fault)) return null;

            if (!(parsed is Dictionary<string, object> root)) { fault = "the document is not an object"; return null; }

            var doc = new Doc(strict);

            if (!doc.Keys(root, "", "schema", "revision", "ramp", "deck", "arsenal", "refresh", "cards")) { fault = doc.Fault; return null; }

            if (!doc.Int(root, "schema", out int schema)) { fault = doc.Fault; return null; }
            if (schema != Schema) { fault = $"the document is schema {schema}; this build reads schema {Schema}"; return null; }

            if (!doc.Int(root, "revision", out int revision)) { fault = doc.Fault; return null; }
            if (revision < 1) { fault = $"revision is {revision}; a published revision is 1 or more (0 is the build's own figures)"; return null; }

            // ------------------------------------------------------------- the ramp
            if (!doc.Object(root, "ramp", out var rampDoc)) { fault = doc.Fault; return null; }

            var rampFigures = ShuffleRampTuning.Figures;
            var rampKeys = new string[rampFigures.Count];
            for (int i = 0; i < rampKeys.Length; i++) rampKeys[i] = rampFigures[i].Key;

            if (!doc.Keys(rampDoc, "ramp.", rampKeys)) { fault = doc.Fault; return null; }

            var rampWritten = new int[rampKeys.Length];
            for (int i = 0; i < rampKeys.Length; i++)
                if (!doc.Int(rampDoc, rampKeys[i], out rampWritten[i], "ramp.")) { fault = doc.Fault; return null; }

            var ramp = ShuffleRampTuning.FromWritten(rampWritten, out fault);
            if (ramp == null) return null;

            // ------------------------------------------------------------- the deck
            if (!doc.Object(root, "deck", out var deckDoc)) { fault = doc.Fault; return null; }

            if (!doc.Keys(deckDoc, "deck.", "opening", "heated", "wild", "heatedFromHand", "wildFromHand",
                          "rareEveryHands", "epicEveryHands")) { fault = doc.Fault; return null; }

            if (!doc.Ints(deckDoc, "opening", out int[] opening, "deck.") ||
                !doc.Ints(deckDoc, "heated", out int[] heated, "deck.") ||
                !doc.Ints(deckDoc, "wild", out int[] wild, "deck.") ||
                !doc.Int(deckDoc, "heatedFromHand", out int heatedFrom, "deck.") ||
                !doc.Int(deckDoc, "wildFromHand", out int wildFrom, "deck.") ||
                !doc.Int(deckDoc, "rareEveryHands", out int rarePity, "deck.") ||
                !doc.Int(deckDoc, "epicEveryHands", out int epicPity, "deck."))
            {
                fault = doc.Fault;
                return null;
            }

            var deck = ShuffleDeckTuning.From(opening, heated, wild, heatedFrom, wildFrom, rarePity, epicPity, out fault);
            if (deck == null) return null;

            // ------------------------------------------------------------- the arsenal
            if (!doc.Object(root, "arsenal", out var arsenalDoc)) { fault = doc.Fault; return null; }

            var arsenalFigures = SiegeArsenalTuning.Figures;
            var arsenalKeys = new string[arsenalFigures.Count];
            for (int i = 0; i < arsenalKeys.Length; i++) arsenalKeys[i] = arsenalFigures[i].Key;

            if (!doc.Keys(arsenalDoc, "arsenal.", arsenalKeys)) { fault = doc.Fault; return null; }

            var arsenalWritten = new int[arsenalKeys.Length];
            for (int i = 0; i < arsenalKeys.Length; i++)
                if (!doc.Int(arsenalDoc, arsenalKeys[i], out arsenalWritten[i], "arsenal.")) { fault = doc.Fault; return null; }

            var arsenal = SiegeArsenalTuning.FromWritten(arsenalWritten, out fault);
            if (arsenal == null) return null;

            // ------------------------------------------------------------- the refresh price
            // Required of the publisher; a device reads a document published before the section
            // existed with the build's own price rather than refusing the whole tuning (59p).
            var refresh = ShuffleRefreshTuning.Shipped;
            if (strict || root.ContainsKey("refresh"))
            {
                if (!doc.Object(root, "refresh", out var refreshDoc)) { fault = doc.Fault; return null; }

                var refreshFigures = ShuffleRefreshTuning.Figures;
                var refreshKeys = new string[refreshFigures.Count];
                for (int i = 0; i < refreshKeys.Length; i++) refreshKeys[i] = refreshFigures[i].Key;

                if (!doc.Keys(refreshDoc, "refresh.", refreshKeys)) { fault = doc.Fault; return null; }

                var refreshWritten = new int[refreshKeys.Length];
                for (int i = 0; i < refreshKeys.Length; i++)
                    if (!doc.Int(refreshDoc, refreshKeys[i], out refreshWritten[i], "refresh.")) { fault = doc.Fault; return null; }

                refresh = ShuffleRefreshTuning.FromWritten(refreshWritten, out fault);
                if (refresh == null) return null;
            }

            // ------------------------------------------------------------- the cards
            if (!root.TryGetValue("cards", out object rawCards) || !(rawCards is List<object> rows))
            {
                fault = "cards is missing or is not a list";
                return null;
            }

            var tuned = new Dictionary<string, ShuffleCard>(StringComparer.Ordinal);
            var off = new HashSet<string>(StringComparer.Ordinal);

            for (int r = 0; r < rows.Count; r++)
            {
                if (!(rows[r] is Dictionary<string, object> row)) { fault = $"cards[{r}] is not an object"; return null; }

                string at = $"cards[{r}].";
                if (!doc.Keys(row, at, "id", "magnitude", "extent", "most", "enabled")) { fault = doc.Fault; return null; }
                if (!doc.String(row, "id", out string id, at)) { fault = doc.Fault; return null; }

                at = $"cards.{id}.";

                if (tuned.ContainsKey(id) || off.Contains(id)) { fault = $"the card '{id}' is listed twice"; return null; }

                var shipped = ShuffleCards.Find(id);

                if (shipped == null)
                {
                    // Retired ids are refused by name (59d, 5f) and an unknown one is a typo - to
                    // the publisher. A device skips both: a newer build's card is not this build's.
                    if (!strict) continue;

                    fault = Array.IndexOf(ShuffleCards.Retired, id) >= 0
                          ? $"the card '{id}' was retired and is never dealt again"
                          : $"no card is called '{id}'";
                    return null;
                }

                if (!doc.Int(row, "magnitude", out int magnitude, at) || !doc.Int(row, "extent", out int extent, at) ||
                    !doc.Int(row, "most", out int most, at) || !doc.Bool(row, "enabled", out bool enabled, at))
                {
                    fault = doc.Fault;
                    return null;
                }

                var card = Tune(shipped, magnitude, extent, most, out fault);
                if (card == null) return null;

                if (enabled) tuned[id] = card;
                else off.Add(id);
            }

            // ------------------------------------------------------------- the catalog
            var cards = new List<ShuffleCard>(ShuffleCards.All.Length);
            bool sameCards = true;

            foreach (var shipped in ShuffleCards.All)
            {
                if (off.Contains(shipped.Id)) { sameCards = false; continue; }

                if (tuned.TryGetValue(shipped.Id, out var card))
                {
                    if (!ReferenceEquals(card, shipped)) sameCards = false;
                    cards.Add(card);
                    continue;
                }

                if (strict)
                {
                    fault = $"the card '{shipped.Id}' is not listed; the published file lists every card, so a card left out is never an accident";
                    return null;
                }

                cards.Add(shipped);
            }

            if (cards.Count < ShuffleDeck.HandSize)
            {
                fault = $"only {cards.Count} cards are switched on; a hand needs {ShuffleDeck.HandSize}";
                return null;
            }

            // Every card held to its most on the run's own arsenal, read back off the board's
            // figures: a fold is arithmetic over several figures (a meteor sooner with every copy),
            // and the one honest check of the result is the result.
            foreach (var card in cards)
            {
                fault = Folded(card, arsenal);
                if (fault != null) return null;
            }

            var catalog = sameCards ? ShuffleCards.All : cards.ToArray();
            return new ShuffleTuning(revision, ramp, deck, arsenal, catalog, refresh);
        }

        /// <summary>
        /// <paramref name="shipped"/> with a document's figures, or null with a fault. A figure the
        /// note says in words must be the shipped one (<see cref="ShuffleNotes"/>); an extent the
        /// card does not have stays nought; a card that only makes sense once stays at one copy;
        /// everything else moves from a quarter to four times what ships (a most to twice), inside
        /// any ceiling of its own. Figures all shipped answer the shipped card itself.
        /// </summary>
        static ShuffleCard Tune(ShuffleCard shipped, int magnitude, int extent, int most, out string fault)
        {
            fault = null;
            string at = "cards." + shipped.Id + ".";
            var worded = ShuffleNotes.Worded(shipped);

            if ((worded & ShuffleWorded.Magnitude) != 0 && magnitude != shipped.Magnitude)
            {
                fault = $"{at}magnitude is {magnitude}, but the card's text says {shipped.Magnitude} in words that change with the number; it is fixed until the text is";
                return null;
            }

            if (!Within(at + "magnitude", magnitude, Math.Max(1, shipped.Magnitude / 4),
                        Math.Min(shipped.Magnitude * 4, MagnitudeCeiling(shipped)), shipped.Magnitude, out fault))
                return null;

            if (shipped.Extent == 0)
            {
                if (extent != 0) { fault = $"{at}extent is {extent}; this card has no second figure, so it is 0"; return null; }
            }
            else
            {
                if ((worded & ShuffleWorded.Extent) != 0 && extent != shipped.Extent)
                {
                    fault = $"{at}extent is {extent}, but the card's text says {shipped.Extent} in words that change with the number; it is fixed until the text is";
                    return null;
                }

                if (!Within(at + "extent", extent, Math.Max(1, shipped.Extent / 4),
                            Math.Min(shipped.Extent * 4, ExtentCeiling(shipped)), shipped.Extent, out fault))
                    return null;
            }

            if (shipped.Most == 1)
            {
                if (most != 1) { fault = $"{at}most is {most}; this card does nothing a second time, so it is 1"; return null; }
            }
            else if (!Within(at + "most", most, 1, Math.Min(shipped.Most * 2, MostCeiling(shipped)), shipped.Most, out fault))
            {
                return null;
            }

            if (magnitude == shipped.Magnitude && extent == shipped.Extent && most == shipped.Most) return shipped;

            return new ShuffleCard(shipped.Id, shipped.Tier, shipped.Effect, magnitude, extent, most, shipped.Picture);
        }

        /// <summary>
        /// A magnitude with a ceiling of its own: a chance or a share is out of a hundred, and a
        /// frost slows by at most nine tenths (<c>SiegeBoosts.SetFrost</c> clamps there, so a note
        /// above it would print more than the board does).
        /// </summary>
        static int MagnitudeCeiling(ShuffleCard card)
        {
            switch (card.Effect)
            {
                case ShuffleEffect.Frost: return 9;
                case ShuffleEffect.Crit:
                case ShuffleEffect.Stun:
                case ShuffleEffect.Repel:
                case ShuffleEffect.Twin:
                case ShuffleEffect.Hex:
                case ShuffleEffect.Execute:
                case ShuffleEffect.Nuke:
                // A pact's cost is a share taken away, and a miser's gain is one too.
                case ShuffleEffect.Miser:
                    return 90;

                // The schools' shares (MODES.md 59r): a rot is a share of a blow taken away, and
                // the board holds every one of these under a ceiling of its own besides
                // (`SiegeSchools.MostRot`, `MostBrittle`).
                case ShuffleEffect.Rot:
                    return 45;
                case ShuffleEffect.Brittle:
                    return 60;
                case ShuffleEffect.Blight:
                    return 100;
                default:
                    return int.MaxValue;
            }
        }

        /// <summary>
        /// An extent with a ceiling of its own: a last stand's health line is a share of a
        /// hundred, and so is every pact's cost (a figure lost past nine tenths is a figure gone).
        /// </summary>
        static int ExtentCeiling(ShuffleCard card)
        {
            switch (card.Effect)
            {
                case ShuffleEffect.Desperate:
                case ShuffleEffect.Frenzy:
                case ShuffleEffect.Titan:
                case ShuffleEffect.Miser:
                case ShuffleEffect.Blood:
                case ShuffleEffect.Reckless:
                    return 90;
                default:
                    return int.MaxValue;
            }
        }

        /// <summary>
        /// A most with a ceiling of its own: a copy past it would do nothing, which is a card
        /// dealt for nothing (5d). There are four turrets to make laser guns, and a blast reaches
        /// the corners at its widest.
        /// </summary>
        static int MostCeiling(ShuffleCard card)
        {
            switch (card.Effect)
            {
                case ShuffleEffect.Laser: return Wards.WardLine.Colours.Length;
                case ShuffleEffect.BlastGems: return 2;
                default: return int.MaxValue;
            }
        }

        /// <summary>
        /// Takes <paramref name="card"/> to its most on a fresh build and reads the board's figures
        /// after each copy, answering the first that no board should run on, or null.
        /// </summary>
        static string Folded(ShuffleCard card, SiegeArsenalTuning arsenal)
        {
            var build = new ShuffleBuild(1u, arsenal);

            for (int n = 1; n <= card.Most; n++)
            {
                if (!build.Take(card)) return $"cards.{card.Id} could not be taken {n} times";

                var b = build.Boosts;
                string at = $"cards.{card.Id} held {n} time{(n == 1 ? "" : "s")}";

                if (b.MeteorEvery != 0f && b.MeteorEvery < 1f) return $"{at} calls a meteor every {b.MeteorEvery:0.##} s; the least is 1 s";
                if (b.VortexEvery != 0f && b.VortexEvery < 1f) return $"{at} opens a vortex every {b.VortexEvery:0.##} s; the least is 1 s";
                if (b.RayEvery != 0f && b.RayEvery < 1f) return $"{at} fires the ray every {b.RayEvery:0.##} s; the least is 1 s";
                if (b.RegenEvery != 0f && b.RegenEvery < .5f) return $"{at} heals every {b.RegenEvery:0.##} s; the least is 0.5 s";
                if (b.ExecutePercent > 90) return $"{at} executes under {b.ExecutePercent}% health; the most is 90%";
                if (b.DesperateBelow > 90) return $"{at} stands its last under {b.DesperateBelow}% health; the most is 90%";
                if (b.NukePercent > 90) return $"{at} nukes {b.NukePercent}% of health; the most is 90%";
                if (b.FocusPercent * b.FocusMost > 1000) return $"{at} focuses a beam of {b.FocusPercent * b.FocusMost}%; the most is 1000%";
                if (b.VenomMost > 30) return $"{at} stacks {b.VenomMost} venom; the most is 30";
                if (b.CombustTenths > 60) return $"{at} combusts at {b.CombustTenths} tenths; the most is 60";
            }

            return null;
        }

        static bool Within(string name, int value, int least, int most, int shipped, out string fault)
        {
            if (value >= least && value <= most) { fault = null; return true; }

            fault = $"{name} is {value}; it may move from {least} to {most} (ships at {shipped})";
            return false;
        }

        /// <summary>
        /// Each figure of a section in its range, and whether every one is the shipped figure.
        /// Shared by the ramp; the arsenal checks its own table the same way.
        /// </summary>
        internal static bool InRange<T>(string section, IReadOnlyList<T> table, int[] written,
                                        Func<T, (string key, int shipped, int least, int most)> of,
                                        out bool shipped, out string fault)
        {
            shipped = true;
            fault = null;

            if (written == null || written.Length != table.Count)
            {
                fault = $"{section} names {(written == null ? 0 : written.Length)} figures, not {table.Count}";
                return false;
            }

            for (int i = 0; i < table.Count; i++)
            {
                var (key, ships, least, most) = of(table[i]);

                if (!Within(section + "." + key, written[i], least, most, ships, out fault)) return false;
                if (written[i] != ships) shipped = false;
            }

            return true;
        }

        // ------------------------------------------------------------------ writing
        /// <summary>
        /// This tuning as a published document, in the shape <see cref="TryRead"/> reads, every
        /// card listed (a switched-off card at its shipped figures). For a fixture's round trip and
        /// for a tool starting a file from the build's own figures.
        /// </summary>
        public string ToJson(int revision)
        {
            var o = new StringBuilder(8 * 1024);
            o.Append("{\n");
            o.Append("  \"schema\": ").Append(Schema).Append(",\n");
            o.Append("  \"revision\": ").Append(revision).Append(",\n");

            o.Append("  \"ramp\": {\n");
            var rampFigures = ShuffleRampTuning.Figures;
            var rampWritten = Ramp.Written();
            for (int i = 0; i < rampFigures.Count; i++)
                o.Append("    \"").Append(rampFigures[i].Key).Append("\": ").Append(rampWritten[i])
                 .Append(i + 1 < rampFigures.Count ? ",\n" : "\n");
            o.Append("  },\n");

            o.Append("  \"deck\": {\n");
            o.Append("    \"opening\": ").Append(List(Deck.Opening)).Append(",\n");
            o.Append("    \"heated\": ").Append(List(Deck.Heated)).Append(",\n");
            o.Append("    \"wild\": ").Append(List(Deck.Wild)).Append(",\n");
            o.Append("    \"heatedFromHand\": ").Append(Deck.HeatedFrom).Append(",\n");
            o.Append("    \"wildFromHand\": ").Append(Deck.WildFrom).Append(",\n");
            o.Append("    \"rareEveryHands\": ").Append(Deck.RarePity).Append(",\n");
            o.Append("    \"epicEveryHands\": ").Append(Deck.EpicPity).Append("\n");
            o.Append("  },\n");

            o.Append("  \"arsenal\": {\n");
            var arsenalFigures = SiegeArsenalTuning.Figures;
            var arsenalWritten = Arsenal.Written();
            for (int i = 0; i < arsenalFigures.Count; i++)
                o.Append("    \"").Append(arsenalFigures[i].Key).Append("\": ").Append(arsenalWritten[i])
                 .Append(i + 1 < arsenalFigures.Count ? ",\n" : "\n");
            o.Append("  },\n");

            o.Append("  \"refresh\": {\n");
            var refreshFigures = ShuffleRefreshTuning.Figures;
            var refreshWritten = Refresh.Written();
            for (int i = 0; i < refreshFigures.Count; i++)
                o.Append("    \"").Append(refreshFigures[i].Key).Append("\": ").Append(refreshWritten[i])
                 .Append(i + 1 < refreshFigures.Count ? ",\n" : "\n");
            o.Append("  },\n");

            o.Append("  \"cards\": [\n");
            var all = ShuffleCards.All;
            for (int i = 0; i < all.Length; i++)
            {
                var card = Find(all[i].Id);
                bool enabled = card != null;
                card = card ?? all[i];

                o.Append("    { \"id\": \"").Append(card.Id)
                 .Append("\", \"magnitude\": ").Append(card.Magnitude.ToString(CultureInfo.InvariantCulture))
                 .Append(", \"extent\": ").Append(card.Extent.ToString(CultureInfo.InvariantCulture))
                 .Append(", \"most\": ").Append(card.Most.ToString(CultureInfo.InvariantCulture))
                 .Append(", \"enabled\": ").Append(enabled ? "true" : "false").Append(" }")
                 .Append(i + 1 < all.Length ? ",\n" : "\n");
            }
            o.Append("  ]\n");
            o.Append("}\n");

            return o.ToString();
        }

        static string List(IReadOnlyList<int> values)
        {
            var o = new StringBuilder("[");
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) o.Append(", ");
                o.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return o.Append(']').ToString();
        }

        // ------------------------------------------------------------------ the reader's hands
        /// <summary>
        /// Field reads that name what is wrong. <b>A key beginning with an underscore is a
        /// comment</b> everywhere (the seed files' <c>_comment</c>), never a figure.
        /// </summary>
        sealed class Doc
        {
            readonly bool _strict;

            public Doc(bool strict) => _strict = strict;

            public string Fault { get; private set; }

            /// <summary>In strict reading, refuses any key not in <paramref name="known"/>.</summary>
            public bool Keys(Dictionary<string, object> obj, string at, params string[] known)
            {
                if (!_strict) return true;

                foreach (var key in obj.Keys)
                {
                    if (key.StartsWith("_", StringComparison.Ordinal)) continue;
                    if (Array.IndexOf(known, key) >= 0) continue;

                    Fault = $"{at}{key} is not a figure this build knows (a typo, or a figure from a newer build)";
                    return false;
                }

                return true;
            }

            public bool Object(Dictionary<string, object> obj, string key, out Dictionary<string, object> found)
            {
                found = null;

                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{key} is missing"; return false; }
                found = raw as Dictionary<string, object>;
                if (found == null) { Fault = $"{key} is not an object"; return false; }

                return true;
            }

            public bool Int(Dictionary<string, object> obj, string key, out int value, string at = "")
            {
                value = 0;

                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                if (!(raw is long whole)) { Fault = $"{at}{key} is not a whole number"; return false; }
                if (whole < int.MinValue || whole > int.MaxValue) { Fault = $"{at}{key} is out of range"; return false; }

                value = (int)whole;
                return true;
            }

            public bool Ints(Dictionary<string, object> obj, string key, out int[] values, string at = "")
            {
                values = null;

                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                if (!(raw is List<object> list)) { Fault = $"{at}{key} is not a list"; return false; }

                values = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    if (!(list[i] is long whole) || whole < int.MinValue || whole > int.MaxValue)
                    {
                        Fault = $"{at}{key}[{i}] is not a whole number";
                        values = null;
                        return false;
                    }

                    values[i] = (int)whole;
                }

                return true;
            }

            public bool String(Dictionary<string, object> obj, string key, out string value, string at = "")
            {
                value = null;

                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                value = raw as string;
                if (string.IsNullOrEmpty(value)) { Fault = $"{at}{key} is not a name"; return false; }

                return true;
            }

            public bool Bool(Dictionary<string, object> obj, string key, out bool value, string at = "")
            {
                value = false;

                if (!obj.TryGetValue(key, out object raw)) { Fault = $"{at}{key} is missing"; return false; }
                if (!(raw is bool b)) { Fault = $"{at}{key} is not true or false"; return false; }

                value = b;
                return true;
            }
        }
    }
}
