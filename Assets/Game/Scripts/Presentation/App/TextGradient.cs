using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A ramp of colours written across a graphic's own mesh, left to right.
    ///
    /// <para>
    /// <b>uGUI has no gradient, and the two ways round that are both worse.</b> A `Text` carries
    /// one colour, so a graded caption is either a picture — which is a string baked into art,
    /// and this game's captions are loc keys (invariant 6) — or the same word drawn several
    /// times in several colours through a mask, which is one more thing per letter on a canvas
    /// that has already been billed once for exactly that (`UI effects cost the canvas`). A mesh
    /// modifier is neither: uGUI is already building these vertices, and this paints them on the
    /// way past. No extra object, no extra draw call, no extra rebuild.
    /// </para>
    /// <para>
    /// <b>The ramp runs on the mesh's own extent rather than on the rect</b>, so it grades the
    /// <em>word</em> and not the box it is centred in — a caption in a box twice its width would
    /// otherwise show only the middle of the ramp, and every one of these labels is centred in a
    /// box sized for the longest string its band can say.
    /// </para>
    /// <para>
    /// <b>It multiplies rather than replaces</b>, which is what keeps the rest of the kit
    /// working: the graphic's own colour still tints it and its alpha still fades it, so a
    /// graded caption can be popped, tweened or faded by anything that does not know this
    /// component is here.
    /// </para>
    /// <para>
    /// <b>Added before <c>Outline</c>, deliberately.</b> uGUI runs mesh modifiers in the order
    /// their components were added, and <c>Outline</c> copies the vertices it is handed and
    /// paints the copies its own effect colour — so a gradient added first grades the letters
    /// and leaves the bleed alone, where one added last would grade the bleed too and lose the
    /// second colour a neon is made of.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TextGradient : BaseMeshEffect
    {
        /// <summary>The ramp, evenly spaced across the mesh. One stop is a flat tint.</summary>
        public Color[] Stops = { Color.white, Color.white };

        /// <summary>Sets the ramp and redraws.</summary>
        public void Paint(params Color[] stops)
        {
            if (stops == null || stops.Length == 0) return;
            Stops = stops;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh == null || vh.currentVertCount == 0) return;
            if (Stops == null || Stops.Length == 0) return;

            var v = new UIVertex();

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.x < min) min = v.position.x;
                if (v.position.x > max) max = v.position.x;
            }

            // A one-character caption, or a mesh with no width at all, would divide by nothing.
            float span = max - min;
            if (span < .01f) span = .01f;

            for (int i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                v.color = Sample((v.position.x - min) / span) * (Color)v.color;
                vh.SetUIVertex(v, i);
            }
        }

        Color Sample(float t)
        {
            if (Stops.Length == 1) return Stops[0];

            t = Mathf.Clamp01(t) * (Stops.Length - 1);
            int i = Mathf.Min(Mathf.FloorToInt(t), Stops.Length - 2);
            return Color.Lerp(Stops[i], Stops[i + 1], t - i);
        }
    }
}
