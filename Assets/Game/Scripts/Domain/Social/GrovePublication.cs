namespace GlimmerGrove.Social
{
    /// <summary>
    /// What the server answers when asked to rebuild this account's public card.
    ///
    /// <para>
    /// <b>The card, and which save it was built from.</b> The server derives the card from
    /// the document under <c>players/{uid}</c> and reports that document's revision back;
    /// the client asked after a sync it knows the revision of, so the two can be compared.
    /// A publish that answers with an older revision than the one asked for has built a card
    /// from a save this device had already replaced — which is the shape of the bug this
    /// exists to make impossible, and which no other reading can see: the call succeeds, the
    /// card is well-formed, and the board shows last week's grove.
    /// </para>
    /// <para>
    /// <b>Absence is not staleness.</b> A deployment that predates the field reports no
    /// revision, and treating that as stale would have every client retrying against a server
    /// that cannot ever satisfy them (invariant 13a). So the check only runs when both sides
    /// have a number — invariant 25's rule that presence of a field says a deployment
    /// understands it.
    /// </para>
    /// </summary>
    public readonly struct GrovePublication
    {
        /// <summary>What the server wrote. <see cref="GroveCard.Empty"/> after a withdrawal.</summary>
        public readonly GroveCard Card;

        /// <summary>
        /// The revision of the save the card was built from, or -1 when the server did not say.
        /// </summary>
        public readonly long SaveRevision;

        public GrovePublication(GroveCard card, long saveRevision)
        {
            Card = card ?? GroveCard.Empty;
            SaveRevision = saveRevision < 0L ? -1L : saveRevision;
        }

        /// <summary>A reply from a server that does not report what it built from.</summary>
        public static readonly GrovePublication Unproven = new GrovePublication(GroveCard.Empty, -1L);

        /// <summary>Whether the server said which save it read.</summary>
        public bool ReportsRevision => SaveRevision >= 0L;

        /// <summary>
        /// Whether this card was built from the save at <paramref name="revision"/> or a
        /// later one.
        ///
        /// True when nothing can be checked — the server did not report, or the client did
        /// not know what it had pushed — because an unprovable publish is the situation the
        /// game was in before the field existed, and it must not become a refusal.
        /// </summary>
        public bool Proves(long revision)
            => !ReportsRevision || revision <= 0L || SaveRevision >= revision;
    }
}
