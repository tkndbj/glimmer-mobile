using GlimmerGrove.Persistence;

namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// Which generation of the grove catalogue a save's grove belongs to, and the one shape in
    /// which a monotonically merged set can be <em>cleared</em>.
    ///
    /// <para>
    /// <b>Why clearing needs a mechanism at all.</b> Everything the grove stores is joined so
    /// that nothing is ever lost: purchases and land are unions, placements take the later
    /// stamp. That is invariant 11's whole promise and it is what makes a sync safe without a
    /// prompt. It also means <em>deleting a grove is not expressible</em>. Clear the four
    /// fields locally and push, and the next pull joins the server's copy straight back in;
    /// wipe the server and the first device that has not synced pushes it back up. Neither half
    /// can win, because a monotonic join has no way to say "this is gone" — only "I have not
    /// heard of it".
    /// </para>
    /// <para>
    /// <b>An epoch says it.</b> It is one integer that only ever rises, so it merges by
    /// <c>max</c> like every other mergeable number here (invariant 11b), and the rule is that
    /// the grove belonging to the <em>lower</em> epoch is not joined — it is discarded. Two
    /// devices at different epochs converge on the higher one's grove; two devices at the same
    /// epoch join exactly as before; and a device that has been offline since before the reset
    /// cannot reintroduce what the reset removed, however long it stays away. It needs no
    /// server function and no sweep to be correct, which is the property a one-off wipe could
    /// never have.
    /// </para>
    /// <para>
    /// <b>What it is not for.</b> This is not a way to take things away from players, and it
    /// must never be used as one. A grove is worth credits a player earned, and those credits
    /// reach a public leaderboard (invariant 19a) — so raising this refunds nothing and
    /// silently lowers somebody's standing. It exists because the catalogue it indexes was
    /// *replaced*: every id in the old grove now names nothing, so those groves were already
    /// holes standing on ground nobody could account for.
    /// </para>
    /// </summary>
    public static class GroveEpoch
    {
        /// <summary>
        /// The generation this build's catalogue is.
        ///
        /// <para>
        /// <b>1 — the village.</b> On 2026-09-11 the whole grove catalogue was replaced: the
        /// isometric sheets it had been cut from went, and a village rendered from KayKit
        /// models took their place under new ids (the old ones are spent for ever — see
        /// <c>Tools/grove_retired.txt</c>). Not one piece id survived, so every stored grove
        /// named pieces the catalogue no longer knows: a floor of tiles that read as occupied
        /// and drew nothing. Epoch 0 is every save written before that.
        /// </para>
        /// <para>
        /// Raising this again means the catalogue has been replaced again, whole. It is not a
        /// version number for content drops — a drop adds ids and takes none away, which the
        /// ordinary join already handles.
        /// </para>
        /// </summary>
        public const int Current = 1;

        /// <summary>What epoch a save belongs to. An absent field reads as 0, which is correct:
        /// every save written before this existed is the generation before this one.</summary>
        public static int Of(SaveFileDto dto) => dto == null || dto.groveEpoch < 0 ? 0 : dto.groveEpoch;

        /// <summary>Whether a save's grove is this build's, and so may be read and joined.</summary>
        public static bool IsCurrent(SaveFileDto dto) => Of(dto) >= Current;

        /// <summary>
        /// Which of two saves' groves survives a merge: the newer epoch's, whole.
        ///
        /// Returns 0 when they share an epoch and the caller should join them as usual, -1 when
        /// the first side's grove wins outright and 1 when the second's does.
        /// </summary>
        public static int Compare(SaveFileDto mine, SaveFileDto other)
        {
            int a = Of(mine), b = Of(other);
            return a == b ? 0 : (a > b ? -1 : 1);
        }
    }
}
