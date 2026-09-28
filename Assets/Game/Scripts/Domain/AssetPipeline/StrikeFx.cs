using System.Collections.Generic;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// The strike kit: the nine pieces of light a stormcall's bolt and an overcharge's blast are
    /// stacked out of, cut from the owner's bought lightning pack by
    /// <c>Tools/make_strike_fx.py</c> into <c>Art/Fx/Strike/</c>.
    ///
    /// <para>
    /// <b>Named here, in Domain, because a scope has to be able to ask for them before any
    /// screen exists</b> (invariant 7b): <c>SiegeMode.ArtFor</c> requests every one, so a siege
    /// holds the kit for as long as it holds its cast, and the view only ever <em>peeks</em>.
    /// A piece the hold has not delivered is skipped rather than drawn - a strike with no flare
    /// is a strike, a white square where the flare goes is a bug - which is why the view asks
    /// through a helper that answers null and never through <c>UIKit.Img</c> directly.
    /// </para>
    /// <para>
    /// <b>Every piece is a white coverage mask</b>, so one texture serves four ward colours and
    /// the storm's gold under <c>Image.color</c>, and every one is drawn additively
    /// (<c>Additive</c>, Presentation) - stacking is what makes light out of masks, and it is
    /// the whole difference between these pictures and a baked photograph of them.
    /// </para>
    /// <para>
    /// The fixture <c>SiegeArtTests.EveryStrikePieceIsCutAndScoped</c> holds this list to the
    /// tool's and to the siege scope; a name added here is added in the tool's <c>CUTS</c> in
    /// the same change.
    /// </para>
    /// </summary>
    public static class StrikeFx
    {
        /// <summary>The pack's painted strand: a trunk with forks and tendrils, 1:4 tall.</summary>
        public const string Bolt = "bolt";

        /// <summary>Six soft rays round a bright disc - the flash where a bolt lands.</summary>
        public const string Flare = "flare";

        /// <summary>Four hard rays through a ring - the twinkle an impact throws.</summary>
        public const string Glint = "glint";

        /// <summary>An anamorphic streak - the flash thrown across the ground.</summary>
        public const string Streak = "streak";

        /// <summary>A thin ring - the shockwave, squashed to the hill's perspective.</summary>
        public const string Ring = "ring";

        /// <summary>A radial burst - the ground answering.</summary>
        public const string Wave = "wave";

        /// <summary>The web of cracks a bolt leaves in the ground.</summary>
        public const string Crack = "crack";

        /// <summary>A soft four-point star - sparks and twinkles.</summary>
        public const string Star = "star";

        /// <summary>A spiked splash - the slam under an overcharge.</summary>
        public const string Splat = "splat";

        public static readonly IReadOnlyList<string> All = new[]
        {
            Bolt, Flare, Glint, Streak, Ring, Wave, Crack, Star, Splat,
        };
    }
}
