using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The additive material: light that <em>adds</em> to what is under it instead of covering
    /// it.
    ///
    /// <para>
    /// <b>This is the whole difference between a picture of light and light.</b> Every effect
    /// in this game is an <c>Image</c> on a screen-space canvas, and an <c>Image</c> blends by
    /// alpha: a flare drawn over a flare covers it, and a white core inside a gold sheath inside
    /// a soft halo is three cut-outs stacked, of which only the top one is seen where they
    /// overlap. A bought VFX pack is authored against the other blend - its flares, rings and
    /// strands are drawn <c>One One</c>, so where two overlap the result is brighter than either
    /// and six things stacked on a bolt go white. Baked into a reel and alpha-blended, the same
    /// pack arrives flat, which is what the owner reported of the stormcall against the store's
    /// own picture (invariant 37eu). What answers it is not a better bake but the blend: the
    /// pack's own masks (<c>StrikeFx</c>), stacked with this material, at run time.
    /// </para>
    /// <para>
    /// <b>One shader, found by name, and a null when it is not in the build.</b>
    /// <c>Assets/Game/Shaders/UIAdditive.shader</c> is <c>UI/Default</c> with its blend changed,
    /// listed in <c>GraphicsSettings</c>' always-included shaders because nothing references it
    /// and an unreferenced shader is stripped from a player. If it is ever missing, every caller
    /// draws with the ordinary material - dimmer, never absent - and the console says so once.
    /// The material is shared, so everything lit with it batches together.
    /// </para>
    /// <para>
    /// <b>Where it may be used</b>: light. Glows, flares, sparks, beams, bolts, rings - anything
    /// that in the world would be emitting. It is wrong for a body, a plate, a decal or a piece
    /// of chrome, all of which cover what is under them, and it is wrong for anything dark:
    /// adding black is adding nothing, so a shadow drawn with it disappears.
    /// </para>
    /// </summary>
    public static class Additive
    {
        /// <summary>The shader's name, as `Shader.Find` wants it.</summary>
        public const string ShaderName = "GlimmerGrove/UI Additive";

        static Material _material;
        static bool _looked;

        /// <summary>The shared additive material, or null when the shader is not in this build.</summary>
        public static Material Material
        {
            get
            {
                if (_looked) return _material;
                _looked = true;

                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[Additive] {ShaderName} is not in this build - every glow " +
                                     "draws with the ordinary UI material. Check GraphicsSettings' " +
                                     "always-included shaders.");
                    return null;
                }

                _material = new Material(shader)
                {
                    name = "UI Additive",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                return _material;
            }
        }

        /// <summary>Draws <paramref name="graphic"/> additively, and hands it back for chaining.</summary>
        public static T Lit<T>(T graphic) where T : Graphic
        {
            if (graphic == null) return null;

            var material = Material;
            if (material != null) graphic.material = material;
            return graphic;
        }
    }
}
