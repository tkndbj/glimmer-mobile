using System;
using System.Globalization;
using System.Text;

namespace GlimmerGrove.Social
{
    /// <summary>
    /// What a keeper's name may look like once strangers can read it.
    ///
    /// <para>
    /// <b>Storage and publication are different problems, and this is the second one.</b>
    /// <c>RenameOverlay.Clean</c> asks only what a text field owes a database — bounded,
    /// trimmed, no control characters — which is the correct bar for a string nobody but
    /// its owner ever sees. A leaderboard changes what that string is: it is now shown to
    /// people the player has never met, in a row beside their own, in a list the game
    /// itself publishes. So it acquires a second rule, and this is it.
    /// </para>
    /// <para>
    /// <b>The bidirectional controls are why this is not merely a length check.</b> U+202E
    /// and its family re-order the text that <em>follows</em> them, so a name carrying one
    /// does not misdraw itself — it misdraws the rest of the row, and on some layouts the
    /// rows under it. That is a defect a length cap and a word list both sail straight past,
    /// it is trivially discovered, and it is the single most likely way a public list of
    /// user-supplied strings gets broken. Zero-width characters are here for the quieter
    /// version of the same trick: a name that looks identical to somebody else's, and a name
    /// that measures as fifteen characters and draws as none.
    /// </para>
    /// <para>
    /// <b>This is a mirror, not the authority.</b> The server sanitises again on publish and
    /// its answer is what appears on the board — a client's opinion about its own name is
    /// exactly the kind of thing that stops being trustworthy once strangers read it. This
    /// copy exists so the rename panel can show the player what will actually be published
    /// rather than letting them discover it on the board, which is the bargain the client's
    /// reward arithmetic already makes with <c>ProgressionLedger</c>. Word filtering
    /// deliberately lives only on the server: a list shipped in a client is a list that can
    /// be read out of the client.
    /// </para>
    /// <para>
    /// It holds no Unity types and no statics so it can be run offline against the shared
    /// vectors, which is what keeps it and <c>functions/src/grove.ts</c> honest — invariant
    /// 9a's discipline, applied to the one string in this game a stranger will read.
    /// </para>
    /// </summary>
    public static class GroveNames
    {
        /// <summary>
        /// The longest public name. Matches <c>RenameOverlay.MaxLength</c>, and has to: a
        /// player allowed to store sixteen characters and shown twelve on the board would
        /// read the difference as their name being wrong rather than as a rule.
        /// </summary>
        public const int MaxLength = 16;

        /// <summary>
        /// The fewest visible characters a published name may have.
        ///
        /// Two rather than one, because a single character is not a name anybody can refer
        /// to and a board of one-character rows is what happens when it is allowed to be.
        /// A name refused here is not rejected — the player keeps it, and their own screens
        /// keep drawing it — it simply is not what the board shows. See
        /// <see cref="IsPublishable"/>.
        /// </summary>
        public const int MinLength = 2;

        // The invisible characters .NET does not classify as Control or Format, spelled as
        // escapes rather than pasted, because a source file is the worst possible place to
        // keep a character nobody can see.
        const char ZeroWidthSpace = '\u200B';
        const char ZeroWidthNonJoiner = '\u200C';
        const char ZeroWidthJoiner = '\u200D';
        const char WordJoiner = '\u2060';
        const char ZeroWidthNoBreakSpace = '\uFEFF';
        const char SoftHyphen = '\u00AD';

        /// <summary>
        /// Everything a published name may not contain, tested per UTF-16 code unit.
        ///
        /// <list type="bullet">
        /// <item>Control and format characters, which is where every bidirectional override
        /// and embedding lives — see the type's remarks for why that one matters most.</item>
        /// <item>The zero-width family, which .NET classifies as ordinary letters or
        /// punctuation but which draw as nothing.</item>
        /// <item>Surrogates, dropped rather than paired: a name made of emoji is a name no
        /// support ticket can be written about, and half a pair in a database is worse than
        /// no character at all.</item>
        /// </list>
        /// </summary>
        public static bool IsForbidden(char c)
        {
            if (char.IsControl(c) || char.IsSurrogate(c)) return true;

            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Format:
                case UnicodeCategory.PrivateUse:
                case UnicodeCategory.OtherNotAssigned:
                case UnicodeCategory.LineSeparator:
                case UnicodeCategory.ParagraphSeparator:
                    return true;
            }

            return c == ZeroWidthSpace || c == ZeroWidthNonJoiner || c == ZeroWidthJoiner
                || c == WordJoiner || c == ZeroWidthNoBreakSpace || c == SoftHyphen;
        }

        /// <summary>
        /// The public form of a stored name: forbidden characters dropped, every run of
        /// whitespace collapsed to one ordinary space, trimmed, and cut to
        /// <see cref="MaxLength"/>.
        ///
        /// <para>
        /// Empty in, empty out — and empty is a real answer rather than a failure. A player
        /// who has never renamed stores nothing (invariant 11c), so "no name" has to survive
        /// this rather than becoming a default here: the default is decided by whoever draws,
        /// and on the board it is decided by the server, which is the only party that can
        /// give two unnamed keepers rows that differ.
        /// </para>
        /// <para>
        /// The cut is applied <em>after</em> collapsing rather than before, so padding cannot
        /// be used to push real characters past the limit, and the result is trimmed again
        /// because cutting mid-word can leave a trailing space.
        /// </para>
        /// </summary>
        public static string Public(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;

            var builder = new StringBuilder(stored.Length < MaxLength ? stored.Length : MaxLength);
            bool pendingSpace = false;

            foreach (char c in stored)
            {
                // Whitespace is asked about first, and the order is the rule rather than a
                // detail. A tab is a control character *and* a word break; dropping it as
                // the former turns "Fern<tab>Willow" into one word, which is a different
                // name from the one the player typed. Anything that separates words
                // separates them; only what draws as nothing is deleted.
                if (char.IsWhiteSpace(c))
                {
                    // Held rather than written, so a run collapses and a leading run
                    // disappears without a second pass over the string.
                    if (builder.Length > 0) pendingSpace = true;
                    continue;
                }

                if (IsForbidden(c)) continue;

                if (pendingSpace)
                {
                    if (builder.Length >= MaxLength) break;
                    builder.Append(' ');
                    pendingSpace = false;
                }

                if (builder.Length >= MaxLength) break;
                builder.Append(c);
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// The longest a collision key may be.
        ///
        /// Larger than <see cref="MaxLength"/> and that is not slack: compatibility
        /// normalisation <em>expands</em>. U+3390 is one character and folds to four, so a
        /// sixteen-character name can legitimately produce a longer key. The cap exists only
        /// so a document id can never be unbounded, and it is far below Firestore's own
        /// 1,500-byte limit.
        /// </summary>
        public const int MaxKeyLength = 64;

        /// <summary>
        /// The characters this runtime's Unicode tables disagree with the server's about,
        /// mapped by hand so that neither side has to be right about them.
        ///
        /// <para>
        /// <b>Measured in the Editor rather than assumed.</b> Unity's Mono expands the
        /// fullwidth forms, the squared units, the roman numerals and the fractions exactly as
        /// Node does, and misses two things: the <b>Latin ligature block</b> (U+FB00–FB06),
        /// which its compatibility tables do not decompose at all, and <b>U+1E9E</b>, whose
        /// lowercase it leaves alone where Node gives ß. Both are ordinary characters a Mac or
        /// a German keyboard produces, so both are worth closing rather than documenting.
        /// </para>
        /// <para>
        /// <b>Applied before normalisation, so it is idempotent either way.</b> A future Unity
        /// whose tables do decompose these sees them already decomposed and does nothing; the
        /// server, whose tables already do, likewise. That is what keeps this from becoming a
        /// thing to remember to delete.
        /// </para>
        /// <para>
        /// <b>Two whole scripts, because they are living ones.</b> Unicode gained lowercase
        /// Cherokee in 8.0 and Georgian Mtavruli in 11.0, both after Mono's tables were fixed,
        /// so a Georgian name typed in capitals — which is how Georgian capitals are written —
        /// folded to something the server had never heard of. Both are contiguous ranges with a
        /// constant offset, so two living scripts cost four lines rather than a table.
        /// </para>
        /// <para>
        /// <b>Where this stops, and why that is not a loose end.</b> Beyond these, Mono's
        /// normalisation tables differ from ICU's across a long tail of compatibility
        /// characters — enclosed alphanumerics, CJK compatibility ideographs, some presentation
        /// forms, and about sixty scattered Latin and Cyrillic letters added to Unicode after
        /// Mono froze. Measured, not guessed: 27 of the 256 blocks of the BMP disagree
        /// somewhere. Closing all of it would mean shipping normalisation tables in the client,
        /// which is a great deal of weight to make a <em>hint</em> exact, so it stays open
        /// deliberately. The consequence is the one this whole split is built to tolerate: a
        /// name in that tail gets a wrong hint under the field, corrected by the claim a moment
        /// later. It can never produce a duplicate, because a reservation is decided by the
        /// server's fold and only ever by the server's fold. What the vectors pin is the set
        /// that agrees — every script a keyboard actually produces — and a regression inside
        /// that set fails a build.
        /// </para>
        /// </summary>
        static string Agree(string text)
        {
            // The overwhelmingly common case: nothing to do, and no allocation to do it with.
            bool touched = false;
            foreach (char c in text)
            {
                if ((c >= '\uFB00' && c <= '\uFB06') || c == '\u0130' || c == '\u1E9E'
                    || (c >= '\u13A0' && c <= '\u13F5') || (c >= '\u1C90' && c <= '\u1CBF'))
                {
                    touched = true;
                    break;
                }
            }

            if (!touched) return text;

            var builder = new StringBuilder(text.Length + 4);

            foreach (char c in text)
            {
                switch (c)
                {
                    case '\uFB00': builder.Append("ff"); break;
                    case '\uFB01': builder.Append("fi"); break;
                    case '\uFB02': builder.Append("fl"); break;
                    case '\uFB03': builder.Append("ffi"); break;
                    case '\uFB04': builder.Append("ffl"); break;
                    case '\uFB05': builder.Append("st"); break;
                    case '\uFB06': builder.Append("st"); break;

                    // The two case mappings, done here so the lowering below cannot disagree.
                    case '\u0130': builder.Append('i'); break;
                    case '\u1E9E': builder.Append('\u00DF'); break;

                    default:
                        // Cherokee (Unicode 8.0) and Georgian Mtavruli (Unicode 11.0): contiguous
                        // ranges with a constant offset, which is why two living scripts cost
                        // four lines. U+1CBB and U+1CBC are unassigned and fall through.
                        if (c >= '\u13A0' && c <= '\u13EF') builder.Append((char)(c + 0x97D0));
                        else if (c >= '\u13F0' && c <= '\u13F5') builder.Append((char)(c + 0x0008));
                        else if (c >= '\u1C90' && c <= '\u1CBA') builder.Append((char)(c - 0x0BC0));
                        else if (c >= '\u1CBD' && c <= '\u1CBF') builder.Append((char)(c - 0x0BC0));
                        else builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// The collision key for a stored name: what two names have to share before they
        /// count as the same name.
        ///
        /// <para>
        /// <b>This is a document id, and that is the whole design.</b> Uniqueness is held by
        /// a Firestore document keyed on this string — <c>names/{key}</c> — so it is enforced
        /// by the database's own primary key rather than by a query. A create against an id
        /// that exists fails, at any concurrency, with no index and no scan: the cost of
        /// asking whether a name is taken is one document read at ten players and at ten
        /// million. The obvious alternative, <c>where("name","==",x)</c>, is both racy (two
        /// clients read "free" and both write) and an index over a collection that grows for
        /// the life of the game. See <c>functions/src/names.ts</c>.
        /// </para>
        /// <para>
        /// <b>Duplicates get in through normalisation, not through concurrency.</b>
        /// <c>Fern</c>, <c>fern</c>, <c>FERN</c>, <c>F e r n</c> and <c>Ｆｅｒｎ</c> are five
        /// documents and one name, so the fold is compatibility normalisation, then a
        /// case fold, then everything that is not a letter or a digit dropped.
        /// </para>
        /// <para>
        /// <b>Letters and digits of <em>any</em> script.</b> Folding to ASCII would be
        /// shorter and would make every name written in Cyrillic, Greek, Arabic or kana fold
        /// to nothing — which in a game that ships globally means those players silently stop
        /// having reservable names at all. That is why this asks
        /// <see cref="char.IsLetterOrDigit(char)"/> rather than matching a range.
        /// </para>
        /// <para>
        /// <b>The fold may only ever be loosened.</b> Adding confusable folding later (0/O,
        /// Cyrillic а for Latin a) would make two names already held collapse onto one key,
        /// which needs a repair job rather than a deploy; removing a rule only ever frees keys
        /// up. Confusables are deliberately out today, because folding digits into letters
        /// refuses <c>Fern1</c> to somebody because <c>Ferni</c> exists, and that is a worse
        /// bargain than the impersonation it prevents until impersonation is actually observed.
        /// </para>
        /// <para>
        /// <b>A divergence between this and the server's copy is a display bug, never a
        /// correctness one.</b> The client folds so it can read the right document for the
        /// "is this taken" hint; the claim is adjudicated by a transaction on the server,
        /// which folds with its own copy. So a platform whose normalisation tables differ can
        /// at worst show an optimistic hint and then be refused — never take a name twice.
        /// The vectors in <c>firebase/shared/grove-vectors.json</c> pin the cases that matter
        /// (invariant 9a); the <c>try</c> below is the same reasoning for a runtime that has
        /// no normalisation tables at all.
        /// </para>
        /// </summary>
        public static string Key(string stored)
        {
            string visible = Public(stored);
            if (visible.Length == 0) return string.Empty;

            string folded;
            try
            {
                folded = Agree(visible).Normalize(NormalizationForm.FormKC);
            }
            catch (ArgumentException)
            {
                // Invalid sequences for normalisation. Public() has already dropped the
                // surrogates and the format characters, so reaching here means a runtime
                // disagreeing about something exotic; folding what we have is a better
                // answer than refusing the name.
                folded = Agree(visible);
            }
            catch (NotSupportedException)
            {
                folded = Agree(visible);
            }

            // The two runtimes' case tables disagree in exactly two places, and both were found
            // by the shared vectors rather than by reading either implementation. .NET's
            // invariant lowercase is Unicode's *simple* one-to-one mapping; JavaScript's
            // `toLowerCase` is the *full* mapping, which includes the unconditional and the
            // context-sensitive entries of SpecialCasing.
            //
            //   U+0130 (İ)  the one character whose lowercase is longer than itself. JavaScript
            //               expands it to `i` + U+0307; .NET leaves it untouched. Closed in
            //               `Agree` above, before normalisation, with U+1E9E.
            //
            //   U+03C2 (ς)  final sigma, and the one that has to be closed *after* lowering
            //               because lowering is what produces it. JavaScript applies Unicode's
            //               Final_Sigma condition, so a Greek name ending in Σ lowers to ς there
            //               and to σ here. Folding them together is the right answer anyway:
            //               they are one letter, and a player would not accept that moving a
            //               name's last letter makes it a different name.
            //
            // Everything else in SpecialCasing is conditional on a Turkish or Lithuanian locale,
            // which neither side's locale-independent fold applies.
            folded = folded.ToLowerInvariant().Replace('\u03C2', '\u03C3');

            var builder = new StringBuilder(folded.Length < MaxKeyLength ? folded.Length : MaxKeyLength);

            foreach (char c in folded)
            {
                if (!char.IsLetterOrDigit(c)) continue;
                if (builder.Length >= MaxKeyLength) break;

                builder.Append(c);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Whether this stored name is fit to appear on a board beside a stranger's.
        ///
        /// <para>
        /// The length is measured on the <em>public</em> form, which is the only measurement
        /// that means anything: sixteen zero-width joiners is an empty name wearing a length.
        /// </para>
        /// <para>
        /// <b>The key is measured too, and that is what keeps two systems from disagreeing.</b>
        /// A name of punctuation — <c>!!</c>, <c>···</c> — has two visible characters and folds
        /// to nothing, so it could be published and could never be reserved, and two keepers
        /// would appear on one board under one name with nothing able to tell them apart.
        /// Requiring both makes "publishable" and "reservable" the same predicate, which is
        /// the property every caller here quietly assumes.
        /// </para>
        /// </summary>
        public static bool IsPublishable(string stored)
            => Public(stored).Length >= MinLength && Key(stored).Length >= MinLength;
    }
}
