using GlimmerGrove.Persistence;

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// What a finished sync hands to anything that builds on the server's copy of the save.
    ///
    /// <para>
    /// <b>The one fact worth carrying is which save the server now holds.</b> Two things in
    /// the game derive from the document under <c>players/{uid}</c> rather than from the
    /// device — the public card and the name reservation — and both went wrong the same way:
    /// asked for the moment the device changed, they were built by the server from the save
    /// it had, which was the one pushed <em>last</em> time. A card one session behind its
    /// grove for the life of the account, with nothing on any screen to say so. So the
    /// request now waits for the sync, and the sync says what it settled.
    /// </para>
    /// <para>
    /// <b><see cref="ServerRevision"/> is the proof.</b> Every save carries a revision that
    /// only rises (<c>CloudState.Revision</c>, enforced by the security rules), so a
    /// server-side rebuild can report which revision it read and the client can check the
    /// answer against what it pushed — see <c>GrovePublication.Proves</c>. It is the revision
    /// of the <em>document</em> rather than of <see cref="Save"/>: when nothing needed
    /// pushing the two differ by the increment a merge always adds, and the document's is
    /// the one the server will report.
    /// </para>
    /// </summary>
    public readonly struct SyncReceipt
    {
        /// <summary>The save both sides now hold. Never null on a receipt a sync raised.</summary>
        public readonly SaveFileDto Save;

        /// <summary>
        /// The revision the server's document carries after this sync, or 0 when it could
        /// not be known — in which case nothing can be proved against it and nothing is.
        /// </summary>
        public readonly long ServerRevision;

        /// <summary>Whether this sync wrote anything, or found both sides already agreeing.</summary>
        public readonly bool Pushed;

        public SyncReceipt(SaveFileDto save, long serverRevision, bool pushed)
        {
            Save = save;
            ServerRevision = serverRevision < 0L ? 0L : serverRevision;
            Pushed = pushed;
        }

        public bool IsValid => Save != null;
    }
}
