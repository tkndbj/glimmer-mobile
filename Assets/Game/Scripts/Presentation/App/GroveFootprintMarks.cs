using System.Collections.Generic;
using GlimmerGrove.Homestead;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove.App
{
    /// <summary>
    /// A footprint's worth of tile lights: one diamond per tile a piece would occupy, pooled.
    ///
    /// <para>
    /// <b>Why one per tile and not one per piece.</b> A single diamond naming the anchor would
    /// say a four-tile wall was one tile wide — which is exactly the misunderstanding footprints
    /// exist to end, and exactly what a played build reported. Every tile the piece will hold is
    /// lit, so what the player is told matches what the save will take.
    /// </para>
    /// <para>
    /// It takes the footprint it is handed rather than working one out. There is one answer to
    /// "what would this occupy" (<see cref="GroveDraft.Footprint"/>) and a second copy of that
    /// arithmetic here would be a second thing to keep in step.
    /// </para>
    /// </summary>
    public sealed class GroveFootprintMarks
    {
        /// <summary>Ground this would go down on.</summary>
        public static readonly Color Room = Pal.A(Pal.Mint, .58f);

        /// <summary>Ground it would not — the colour the drop is refused in.</summary>
        public static readonly Color NoRoom = new Color(1f, .36f, .30f, .55f);

        /// <summary>Where a lifted piece came from, so the player can see what they are undoing.</summary>
        public static readonly Color Origin = Pal.A(Pal.Sun, .50f);

        readonly RectTransform _parent;
        readonly GroveFieldView _field;
        readonly string _name;
        readonly List<Image> _marks = new List<Image>(4);

        public GroveFootprintMarks(RectTransform parent, GroveFieldView field, string name)
        {
            _parent = parent;
            _field = field;
            _name = name;
        }

        /// <summary>
        /// Lights every tile of a footprint anchored at a tile.
        ///
        /// Placed in world space on every call rather than parented to the field's cells,
        /// because the marks outlive any one tile: the field pools and rebinds cells as the
        /// camera pans, and a light parented to one would be recycled out from under the drag.
        /// </summary>
        public void Light(int anchorCol, int anchorRow, GroveFootprint footprint, Color colour)
        {
            if (_field == null) return;

            int wanted = footprint.TileCount;
            while (_marks.Count < wanted) _marks.Add(Make());

            int i = 0;
            for (int c = 0; c < footprint.Cols; c++)
                for (int r = 0; r < footprint.Rows; r++)
                {
                    var mark = _marks[i++];
                    mark.gameObject.SetActive(true);
                    mark.color = colour;
                    ((RectTransform)mark.transform).sizeDelta =
                        new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight) * _field.Zoom;
                    mark.transform.position = _field.TileWorld(anchorCol + c, anchorRow + r);
                }

            for (; i < _marks.Count; i++) _marks[i].gameObject.SetActive(false);
        }

        public void Hide()
        {
            foreach (var mark in _marks) mark.gameObject.SetActive(false);
        }

        Image Make()
        {
            var mark = UIKit.Img(_name, _parent, Art.IsoTile(128), Room,
                                 new Vector2(GroveFloor.TileWidth, GroveFloor.TileHeight),
                                 new Vector2(.5f, .5f), Vector2.zero);
            mark.raycastTarget = false;
            mark.gameObject.SetActive(false);
            return mark;
        }
    }
}
