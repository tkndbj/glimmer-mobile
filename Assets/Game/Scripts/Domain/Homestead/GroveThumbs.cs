namespace GlimmerGrove.Homestead
{
    /// <summary>
    /// What a piece's pictures are called inside a browse atlas.
    ///
    /// <para>
    /// <b>Two things have to agree about this and they live in different assemblies</b> — the
    /// Editor tool that packs the atlas and the screen that reads sprites out of it — so the
    /// rule is written once, here, in the assembly they both reference. A second copy is a
    /// second answer for a rename to put out of step with the first, and the failure is
    /// invisible: the atlas is generated, so a mismatched name is not a missing file but a
    /// shop cell that quietly stops animating.
    /// </para>
    /// <para>
    /// <b>Frame zero keeps the bare id.</b> That is the whole compatibility decision: every
    /// reader that wants one still picture — <c>GroveBrowseAtlases.Audit</c>, a picker cell, a
    /// buy panel — goes on asking for the id and goes on getting a sensible answer, whether or
    /// not the piece turned out to be animated. Only something that wants the motion asks for
    /// frame one and upwards.
    /// </para>
    /// </summary>
    public static class GroveThumbs
    {
        /// <summary>
        /// Most frames a browse atlas will carry for one piece.
        ///
        /// A ceiling rather than a count: a reader walks upwards until the atlas stops
        /// answering, so nothing has to be told how long a loop is, and a piece authored with
        /// an absurd number of frames cannot quietly quadruple a shelf's atlas.
        /// </summary>
        public const int MaxFrames = 12;

        /// <summary>The name frame <paramref name="index"/> of a piece answers to.</summary>
        public static string Frame(string id, int index)
            => index <= 0 || string.IsNullOrEmpty(id)
                ? id
                : id + "-" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
    }
}
