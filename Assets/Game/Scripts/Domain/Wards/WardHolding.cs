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
    ///
    /// <b>On every rung, with no band exempt.</b> The legendary band was the one exception for
    /// three days — bought outright, because a colourless turret is not bought *for* a colour —
    /// and that is a rule about a purchase riding on a fact about a picture (invariant 42k). What
    /// a player chooses when they put a turret on red is which seat it guards, and that is as
    /// true of one that fires at every colour as of one that fires at one.
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
        /// What separated a turret's id from <em>which copy of it</em> a row was — <b>retired in
        /// place on the wire</b> (invariant 42k), and kept for exactly two jobs.
        ///
        /// <para>
        /// <b>A legendary was bought by the copy for three days and is bought by the seat now.</b>
        /// A colourless turret's row was the bare id, which means every colour, so one payment
        /// stood four Eclipses; the answer was a second row, <c>eclipse#2</c>. It is the wrong
        /// answer — it gave the band a shape no other turret on the shelf has, it hid the second
        /// purchase behind a key on a panel, and it left one star ladder under four turrets,
        /// because a copy is not a seat and only a seat has a ladder. <b>A legendary is now
        /// bought per colour like everything else</b> (<see cref="Row"/>), which is the rule the
        /// other twenty have always obeyed, at the same price for the same four seats.
        /// </para>
        /// <para>
        /// <b>The two jobs.</b> A rolled-back client can still write a copy row into
        /// <c>wardsOwned</c>, which is a union-joined set nothing may prune — so
        /// <see cref="IdOf"/> still reads one as being about its turret, and it covers no seat by
        /// itself, exactly as it never did. And an id carrying this character would still make
        /// every row about it ambiguous, so <see cref="Spellable"/> still refuses one and both
        /// content gates still ask.
        /// </para>
        /// <para>
        /// <b>Nothing writes one.</b> A player who bought copies keeps the bare row beside them,
        /// which means all four seats — so the retirement hands back more than it takes, which is
        /// the only direction a rule about somebody's purchases may ever move.
        /// </para>
        /// </summary>
        public const char CopyMark = '#';

        /// <summary>The row saying this turret is held on this colour.</summary>
        public static string Key(string id, char colour)
            => string.IsNullOrEmpty(id) ? string.Empty : id + Mark + colour;

        /// <summary>
        /// The row this turret is written down under, on this seat — and there is no exception
        /// to it any more.
        ///
        /// <para>
        /// <b>Every turret is bought per seat, the legendary band included</b> (invariant 42k).
        /// The band had its own spelling for three days — a bare row, because a colourless turret
        /// is not bought *for* a colour — and the bare row means every colour, so one payment
        /// stood four Eclipses and the fix was a second row per copy. Both are gone: what a
        /// player is choosing when they put a turret on red is which seat it guards, and that is
        /// true of a turret that fires at every colour exactly as it is of one that fires at one.
        /// Four Eclipses is four purchases because four seats always were.
        /// </para>
        /// <para>
        /// <b>Wearing no colour was three facts and is two.</b> A legendary is cut
        /// once rather than four times (<c>WardModel.ArtFor</c>) and fires at everything on the
        /// hill (<c>SiegeWard.Unbound</c>); what it no longer is, is bought once.
        /// </para>
        /// <para>
        /// <b>The bare row is still read and is now only ever legacy.</b> <see cref="Covers"/>
        /// reads one as every colour — it is what a build from before colours wrote, and what
        /// every legendary bought before this change holds — so nothing is confiscated and
        /// nothing is rewritten. A player who paid for copies keeps all four seats.
        /// </para>
        /// <para>
        /// <b>Every writer goes through this and no writer spells <see cref="Key"/> itself</b>,
        /// which is invariant 15a's lesson about a rule with two halves: a purchase written one
        /// way and a star ledger keyed the other is a turret somebody paid for whose upgrades
        /// belong to a row nothing reads. That is also what makes this change one line — the star
        /// ladder became per seat for the whole shelf because the row did.
        /// </para>
        /// </summary>
        public static string Row(WardModel model, char colour)
            => model == null ? string.Empty : Key(model.Id, colour);

        /// <summary>The same row for a colour index (0..3).</summary>
        public static string Row(WardModel model, int colour)
            => model == null ? string.Empty : Key(model.Id, colour);

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
