using System;

namespace GlimmerGrove.Wards
{
    /// <summary>
    /// One row of <c>wardsOwned</c>: a turret <em>and the colour it was bought for</em>, and the
    /// one place that pair is spelled.
    ///
    /// <para>
    /// <b>A turret is owned per colour, which is the whole of what this type exists for.</b> A
    /// line holds four turrets and a colour is what a level's hill decides, so buying a rend
    /// turret and having it appear on all four seats is buying one decision and receiving four —
    /// and it collapses the only thing that made the shelf a choice rather than a ladder
    /// (invariant 26h's test, asked of a purchase). Red and green are separate ownings, separate
    /// prices and separate ladders.
    /// </para>
    /// <para>
    /// <b>A bare id — one with no colour on it — means every colour, and that is a reading rather
    /// than a migration.</b> It is what a build that owned turrets outright wrote, and under a
    /// per-colour rule the honest interpretation of "they own this turret" is "on all four". It is
    /// also the only interpretation that is safe under a union merge (invariant 11b): reading it
    /// as one colour, or as none, would confiscate something somebody paid for the first time an
    /// old file met a new build. Nothing rewrites such a row — it is carried through untouched, so
    /// a device on either build reads the same set of holdings out of the same file.
    /// </para>
    /// <para>
    /// <b>The set stays a union-joined set of permanent strings</b>, which is invariant 15's shape
    /// and the reason this cost the save file no schema version: what changed is what a string
    /// means, not what shape the field is, and both spellings mean something a merge can only ever
    /// add to.
    /// </para>
    /// </summary>
    public static class WardHolding
    {
        /// <summary>
        /// What separates a turret's id from the colour it was bought for.
        ///
        /// <b>A character an id may never contain</b>, which both content gates enforce: an id
        /// carrying one would make a holding ambiguous, and the ambiguity would resolve in
        /// whichever direction happened to be tried first.
        /// </summary>
        public const char Mark = ':';

        /// <summary>
        /// What separates a turret's id from <em>which copy of it</em> this row is.
        ///
        /// <para>
        /// <b>A second copy is a second row, which is the whole of how a legendary is counted.</b>
        /// A colourless turret stands on any seat (<see cref="WardModel.Legendary"/>), so one
        /// purchase used to fill all four — and four Eclipses on a line was one payment. Owning
        /// is per <em>copy</em> now: the first is the bare id, the second is <c>eclipse#2</c>, and
        /// a line may stand as many as have been bought.
        /// </para>
        /// <para>
        /// <b>It cost the save nothing, exactly as the bare row did (42h).</b> A copy row is
        /// another permanent string in a union-joined set, so two devices that each bought a
        /// second copy offline land on <em>one</em> second copy — which is the per-id <c>max</c>
        /// invariant 16h gives priced decor, arrived at by the set union rather than by a count.
        /// There is no schema version, no <c>hasOnly</c> release and no migration, and the rules'
        /// bound does not move: a legendary now occupies at most four rows, which is what every
        /// per-colour turret has always occupied.
        /// </para>
        /// <para>
        /// <b>A character an id may never contain</b>, held by both content gates beside
        /// <see cref="Mark"/> and for the same reason — an id carrying one would make every row
        /// about it ambiguous, and a save is not the place to find that out.
        /// </para>
        /// </summary>
        public const char CopyMark = '#';

        /// <summary>The row saying this turret is held on this colour.</summary>
        public static string Key(string id, char colour)
            => string.IsNullOrEmpty(id) ? string.Empty : id + Mark + colour;

        /// <summary>
        /// The row saying this is the <paramref name="copy"/>th of this turret the player owns,
        /// counting from one.
        ///
        /// <b>The first copy is the bare id and carries no number</b>, which is what makes this
        /// additive: every legendary bought before copies existed is copy one, already written,
        /// already read by <see cref="Covers"/>, and nothing rewrites it.
        /// </summary>
        public static string Copy(string id, int copy)
            => string.IsNullOrEmpty(id) ? string.Empty
             : copy <= 1 ? id
             : id + CopyMark + copy.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// The row this turret is written down under, on this seat — and the one place the
        /// per-colour rule has an exception.
        ///
        /// <para>
        /// <b>A legendary is written bare</b> (<see cref="WardModel.Legendary"/>): it wears no
        /// colour, so what it is bought for is not a seat. That needs no new spelling and no
        /// schema version, because a bare id has always meant <em>every colour</em> here — it is
        /// what a build that owned turrets outright wrote, and it is the only reading a union
        /// merge could safely give one. <see cref="Covers"/> already honours it, so a legendary is
        /// held on all four seats by the rule that was written for a file from 2026.
        /// </para>
        /// <para>
        /// <b>Held on every seat is not the same as standing on every seat, and that is the
        /// distinction copies buy.</b> This row says the player owns one; how many they own is
        /// <see cref="Copy"/>, and how many may stand at once is <c>WardLedger.Copies</c>. Four
        /// Eclipses on a line is four purchases.
        /// </para>
        /// <para>
        /// <b>Every writer goes through this and no writer spells <see cref="Key"/> itself</b>,
        /// which is invariant 15a's lesson about a rule with two halves: a purchase written one
        /// way and a star ledger keyed the other is a turret somebody paid for whose upgrades
        /// belong to a row nothing reads.
        /// </para>
        /// </summary>
        public static string Row(WardModel model, char colour)
            => model == null ? string.Empty
             : model.Colourless ? model.Id
             : Key(model.Id, colour);

        /// <summary>The same row for a colour index (0..3).</summary>
        public static string Row(WardModel model, int colour)
            => model == null ? string.Empty
             : model.Colourless ? model.Id
             : Key(model.Id, colour);

        /// <summary>The row saying this turret is held on this colour index (0..3).</summary>
        public static string Key(string id, int colour)
            => Key(id, WardLine.Colours[colour < 0 || colour >= WardLine.Colours.Length
                                        ? 0 : colour]);

        /// <summary>
        /// Reads a row into the turret it names and the colour it was bought for.
        ///
        /// <b>Answers false for a bare id</b> rather than guessing a colour, because a bare id
        /// means <em>all</em> of them and there is no single answer to hand back. A caller asking
        /// "is this row about red" wants that distinction; one asking "which turret is this about"
        /// wants <see cref="IdOf"/>.
        /// </summary>
        public static bool TryRead(string row, out string id, out char colour)
        {
            id = string.Empty;
            colour = '\0';

            if (string.IsNullOrEmpty(row)) return false;

            int at = row.IndexOf(Mark);

            // The colour is exactly one character and the id is what is left, so a row with the
            // mark in any other position is not a holding this build wrote and is left alone.
            if (at <= 0 || at != row.Length - 2) return false;

            colour = row[row.Length - 1];
            if (WardLine.Colours.IndexOf(colour) < 0) return false;

            id = row.Substring(0, at);
            return true;
        }

        /// <summary>
        /// Whether this row covers this turret on this colour.
        ///
        /// <b>The bare-id clause is here rather than at a call site</b>, which is invariant 15a's
        /// rule about a rule with two halves: a reader that checked only the exact key would
        /// silently stop honouring every holding written before colours existed.
        /// </summary>
        public static bool Covers(string row, string id, char colour)
        {
            if (string.IsNullOrEmpty(row) || string.IsNullOrEmpty(id)) return false;

            if (string.Equals(row, id, StringComparison.Ordinal)) return true;

            return string.Equals(row, Key(id, colour), StringComparison.Ordinal);
        }

        /// <summary>
        /// The turret a row is about, whichever way it is spelled.
        ///
        /// For a reader counting what has been paid for, never for one deciding whether a slot may
        /// be filled — that question is <see cref="Covers"/>, and it has a colour in it.
        /// </summary>
        public static string IdOf(string row)
        {
            if (string.IsNullOrEmpty(row)) return string.Empty;

            if (TryRead(row, out string id, out _)) return id;

            // A copy row is about the same turret as the bare one — see <see cref="CopyMark"/>.
            int at = row.IndexOf(CopyMark);
            return at > 0 ? row.Substring(0, at) : row;
        }

        /// <summary>
        /// Whether an id may be written into a holding at all.
        ///
        /// <b>Asked by the content gates rather than at run time</b>: an id carrying the mark
        /// would make every row about it ambiguous, and a save is not the place to discover that.
        ///
        /// <b>Both marks, because there are two spellings now</b> (<see cref="CopyMark"/>) — and
        /// a gate that learned one of them would let the other through on the day it mattered.
        /// </summary>
        public static bool Spellable(string id)
            => !string.IsNullOrEmpty(id)
            && id.IndexOf(Mark) < 0
            && id.IndexOf(CopyMark) < 0;
    }
}
