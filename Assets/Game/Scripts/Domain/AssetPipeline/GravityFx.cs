using System.Collections.Generic;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// The gravity well: the four pieces a Gravity Hole is stacked out of on the hill, drawn by
    /// <c>Tools/make_gravity_fx.py</c> into <c>Art/Fx/Gravity/</c>.
    ///
    /// <para>
    /// <b>Named here, in Domain, for <see cref="StrikeFx"/>'s reason</b> (invariant 7b): a scope
    /// has to be able to ask for them before any screen exists. <c>SiegeMode.ArtFor</c> requests
    /// every one, so a siege holds them for as long as it holds its cast, and the view only ever
    /// peeks - a piece the hold has not delivered is skipped rather than drawn.
    /// </para>
    /// <para>
    /// <b>Pieces rather than a reel</b>, because a black hole is things turning over each other
    /// at different speeds: the board spins the disc and the arms itself, so no two frames of a
    /// well are the same picture and nothing loops on a seam.
    /// </para>
    /// <para>
    /// The fixture <c>GravityFxTests</c> holds this list to the tool's and to the siege scope; a
    /// name added here is added in the tool's <c>CUTS</c> in the same change.
    /// </para>
    /// </summary>
    public static class GravityFx
    {
        /// <summary>The accretion disc, face on: spiral bands, gold at the lip to indigo at the rim. Additive.</summary>
        public const string Disc = "disc";

        /// <summary>Thin spiral filaments, the light falling in. Additive.</summary>
        public const string Arms = "arms";

        /// <summary>
        /// The horizon: a near-black ball with a violet limb. <b>The one piece drawn with
        /// ordinary alpha</b>, because it is the one thing that has to take light away.
        /// </summary>
        public const string Core = "core";

        /// <summary>The photon ring round the horizon. Additive.</summary>
        public const string Ring = "ring";

        public static readonly IReadOnlyList<string> All = new[] { Disc, Arms, Core, Ring };
    }
}
