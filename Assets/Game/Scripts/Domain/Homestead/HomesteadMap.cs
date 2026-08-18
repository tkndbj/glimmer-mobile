using System;
using System.Collections.Generic;

namespace GlimmerGrove.Homestead
{
    /// <summary>Where one plot is drawn, in canvas pixels.</summary>
    public readonly struct PlotPlacement
    {
        public readonly HomesteadPlot Plot;

        /// <summary>Centre of the island, measured from the canvas's left edge and its top.</summary>
        public readonly float CentreX, CentreY;

        public readonly float Width, Height;

        public PlotPlacement(HomesteadPlot plot, float centreX, float centreY, float width, float height)
        {
            Plot = plot;
            CentreX = centreX;
            CentreY = centreY;
            Width = width;
            Height = height;
        }

        public float Left => CentreX - Width * .5f;
        public float Right => CentreX + Width * .5f;
        public float Top => CentreY - Height * .5f;
        public float Bottom => CentreY + Height * .5f;

        public bool Overlaps(PlotPlacement other)
            => Left < other.Right && other.Left < Right
            && Top < other.Bottom && other.Top < Bottom;
    }

    /// <summary>Every plot's rectangle, and how tall the grove came out.</summary>
    public sealed class HomesteadLayoutMap
    {
        readonly PlotPlacement[] _placements;

        public HomesteadLayoutMap(PlotPlacement[] placements, float canvasHeight)
        {
            _placements = placements ?? Array.Empty<PlotPlacement>();
            CanvasHeight = canvasHeight;
        }

        public IReadOnlyList<PlotPlacement> Placements => _placements;

        /// <summary>How tall the grove canvas has to be to hold every plot.</summary>
        public float CanvasHeight { get; }

        /// <summary>Every pair that overlaps. Empty by construction — see <see cref="HomesteadMap"/>.</summary>
        public List<string> Collisions()
        {
            var found = new List<string>();

            for (int i = 0; i < _placements.Length; i++)
                for (int k = i + 1; k < _placements.Length; k++)
                    if (_placements[i].Overlaps(_placements[k]))
                        found.Add($"{_placements[i].Plot.Id} and {_placements[k].Plot.Id}");

            return found;
        }
    }

    /// <summary>
    /// Where the islands sit. The grove's <c>ChapterMap</c>, and it exists for invariant 8a's
    /// reason: geometry belongs in Domain where a validator and a test can reach it, not
    /// beside the screen that happens to draw it.
    ///
    /// <para>
    /// <b>A plot's vertical position is derived, never authored.</b> That is the whole point
    /// of this type and it is a correction: the first version had an author write a <c>y</c>
    /// fraction per plot against a fixed 3400px canvas, and every consecutive pair of islands
    /// overlapped — the ten shipped plots total 4,632px of art before a single gap, so the
    /// canvas was never tall enough and no amount of re-tuning fractions would have fixed it.
    /// The starter plot ended up <em>below</em> the scrollable area entirely, which is what
    /// "I cannot scroll down" turned out to mean: the one island the player owned could not be
    /// reached.
    /// </para>
    /// <para>
    /// The trouble with authoring <c>y</c> is that the number it has to agree with — how tall
    /// the island draws — is a property of a PNG the author cannot see from the JSON. So it is
    /// computed instead: plots stack bottom to top in catalog order with a guaranteed gap, and
    /// the canvas height falls out of the sum. Overlap is then impossible by construction
    /// rather than merely checked, a re-cut sprite cannot break the layout, and a drop that
    /// adds a plot needs no numbers re-tuned anywhere.
    /// </para>
    /// <para>
    /// <b>What stays authored is <c>x</c></b>, because that is a composition choice nothing can
    /// derive — it is what makes the grove wind up the screen with satellites off to the sides
    /// instead of being a plain column.
    /// </para>
    /// </summary>
    public static class HomesteadMap
    {
        /// <summary>
        /// Clear sky between two consecutive islands, in canvas pixels.
        ///
        /// Generous on purpose: the art has soft grass fringes and each island carries a seat
        /// shadow beneath it, so touching rectangles already read as overlapping. It is also
        /// what a locked plot's caption sits in.
        ///
        /// <b>And it is what a tall piece grows into.</b> A rectangle here bounds the
        /// <em>island</em>, not what stands on it: an oak in a canopy slot reaches roughly a
        /// sixth of an island above the grass, which is why the gap was raised with the islands
        /// rather than left where it was. A canopy slot's scale is the other half of that
        /// bargain — see the templates in the catalog.
        /// </summary>
        public const float Gap = 190f;

        /// <summary>Sky above the top island and below the bottom one.</summary>
        public const float EdgePadding = 140f;

        /// <summary>
        /// The height an island draws at, given how wide it is drawn and the shape of its art.
        ///
        /// <paramref name="aspect"/> is the sprite's height over its width. Taking it as a
        /// parameter is what keeps this in Domain: the screen reads it off a loaded
        /// <c>Sprite</c>, the Editor validator off the asset database, and a test can state it
        /// outright — none of which Domain is allowed to know about.
        /// </summary>
        public static float HeightOf(HomesteadPlot plot, float canvasWidth, float aspect)
        {
            float width = canvasWidth * (plot?.Width ?? .5f);
            return width * (aspect > 0f ? aspect : 1f);
        }

        /// <summary>
        /// Places every plot in the catalog, bottom to top in catalog order.
        ///
        /// <paramref name="aspectOf"/> answers the shape of a plot's art; anything it cannot
        /// answer for falls back to a square, which keeps a catalog whose art has not imported
        /// yet laid out sanely rather than collapsed on top of itself.
        /// </summary>
        public static HomesteadLayoutMap Build(HomesteadCatalog catalog, float canvasWidth,
                                               Func<HomesteadPlot, float> aspectOf)
        {
            if (catalog == null || catalog.PlotCount == 0)
                return new HomesteadLayoutMap(Array.Empty<PlotPlacement>(), EdgePadding * 2f);

            var plots = catalog.Plots;
            var heights = new float[plots.Count];
            float total = EdgePadding * 2f;

            for (int i = 0; i < plots.Count; i++)
            {
                float aspect = aspectOf != null ? aspectOf(plots[i]) : 1f;
                heights[i] = HeightOf(plots[i], canvasWidth, aspect);
                total += heights[i];
                if (i > 0) total += Gap;
            }

            var placements = new PlotPlacement[plots.Count];

            // Walk up from the bottom, because catalog order is the order a player earns them
            // and the grove should grow upward as they do — the first plot is the one under
            // their thumb when the screen opens.
            float fromBottom = EdgePadding;

            for (int i = 0; i < plots.Count; i++)
            {
                float centreFromBottom = fromBottom + heights[i] * .5f;

                placements[i] = new PlotPlacement(
                    plots[i],
                    canvasWidth * plots[i].X,
                    total - centreFromBottom,          // measured down from the canvas top
                    canvasWidth * plots[i].Width,
                    heights[i]);

                fromBottom += heights[i] + Gap;
            }

            return new HomesteadLayoutMap(placements, total);
        }
    }
}
