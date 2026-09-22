using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using UnityEngine;

namespace GlimmerGrove.Frames
{
    /// <summary>
    /// One name frame: a painting the game draws round a player's name, and the two places on
    /// it the runtime has to know about — where the name goes and where the eye is.
    ///
    /// <para>
    /// <b>The id is permanent</b>, for invariant 1's reason read across to a cosmetic: the day
    /// a frame is sold, the id is the entitlement (invariant 15), a save row and a store product,
    /// and none of those can be renamed. The painting's address and the frame's name key are
    /// both derived from it (5a's shape), so anything holding the id can draw and name the frame
    /// without a second lookup.
    /// </para>
    /// <para>
    /// <b>The hole and the eye are fractions of the painting</b>, y up, so they survive the
    /// texture being capped on import and mean the same thing at every size the frame is drawn.
    /// The same two figures are in the frame's rig description (<c>Tools/make_frame_rig.py</c>),
    /// where they were measured, and <c>FrameTests</c> holds the two copies together.
    /// </para>
    /// </summary>
    public sealed class FrameDefinition
    {
        /// <summary>The bone the runtime nods, by name. A rig without one stands still.</summary>
        public const string NodBone = "head";

        public readonly string Id;
        public readonly string NameKey;

        /// <summary>The clear box a name is laid out inside, as fractions of the painting, y up.</summary>
        public readonly Rect Hole;

        /// <summary>
        /// The box a card's plate may fill behind the frame, as fractions of the painting, y up:
        /// every edge of it is under paint, so the frame is the card's edge and none of the
        /// plate shows outside it. Larger than <see cref="Hole"/> — it runs under the bars,
        /// the dragon and the ornament.
        /// </summary>
        public readonly Rect Plate;

        /// <summary>Where the eye glow sits, as fractions of the painting, y up.</summary>
        public readonly Vector2 Eye;

        /// <summary>The glow's radius, as a fraction of the painting's <em>height</em>.</summary>
        public readonly float EyeGlowRadius;

        public FrameDefinition(string id, string nameKey, Rect hole, Rect plate, Vector2 eye, float eyeGlowRadius)
        {
            Id = id;
            NameKey = nameKey;
            Hole = hole;
            Plate = plate;
            Eye = eye;
            EyeGlowRadius = eyeGlowRadius;
        }

        /// <summary>The painting's address (invariant 7: built, never listed).</summary>
        public string Address => AssetManifest.Frame(Id);
    }

    /// <summary>
    /// Every frame this build can draw.
    ///
    /// <para>
    /// <b>A table in code, for now, and this is the seam that turns it into content.</b> A
    /// frame is a picture, a name, two rectangles and a price; that is a row of a JSON block
    /// (invariant 4), and the day there is a second one worth selling the block moves into
    /// <c>progression.json</c> beside the turret roster, this class becomes its reader, and no
    /// caller changes — every one of them asks <see cref="All"/> or <see cref="Find"/>. It is a
    /// table rather than a block today because one row is not a catalog, and the owner asked to
    /// look at the first frame before deciding whether there will be a second.
    /// </para>
    /// </summary>
    public static class FrameCatalog
    {
        static readonly FrameDefinition[] Table =
        {
            // Measured off the painting by Tools/make_frame_rig.py: the hole is the clear
            // rectangle between the two gold bars, the eye is the dragon's.
            new FrameDefinition("dragon", "ui.frames.dragon",
                                new Rect(.2947f, .2790f, .6515f, .3812f),
                                new Rect(.1381f, .1989f, .8218f, .5387f),
                                new Vector2(.1671f, .7486f), .05f),
        };

        public static IReadOnlyList<FrameDefinition> All => Table;

        /// <summary>The frame with this id, or null: an unknown id draws nothing (7b).</summary>
        public static FrameDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Table[i].Id == id) return Table[i];
            return null;
        }
    }
}
