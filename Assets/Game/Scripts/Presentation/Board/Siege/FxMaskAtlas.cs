using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace GlimmerGrove
{
    /// <summary>
    /// Exact GPU copies of small uncompressed masks. Imported/compressed art keeps its own
    /// texture. No readback, recompression, downsampling or change to the global art cache.
    /// </summary>
    sealed class FxMaskAtlas
    {
        public struct Region
        {
            public Texture Texture;
            public FxQuad Quad;
            public float UnitsPerPixel;
        }
        const int Side = 256, Border = 1;
        readonly Dictionary<Sprite, Region> _regions = new Dictionary<Sprite, Region>();
        readonly List<Texture2D> _textures = new List<Texture2D>();
        Texture2D _current;
        int _x, _y, _row;
        readonly bool _enabled;

        public FxMaskAtlas(bool enabled) => _enabled = enabled &&
            (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

        public Region Resolve(Sprite sprite)
        {
            if (_regions.TryGetValue(sprite, out var region)) return region;
            var source = sprite.texture;
            var padding = DataUtility.GetPadding(sprite);
            int spriteW = Mathf.RoundToInt(sprite.rect.width), spriteH = Mathf.RoundToInt(sprite.rect.height);
            region = new Region
            {
                Texture = source, UnitsPerPixel = 1f / sprite.pixelsPerUnit,
                Quad = new FxQuad
                {
                    Uv = DataUtility.GetOuterUV(sprite), InnerUv = DataUtility.GetInnerUV(sprite),
                    Border = sprite.border, Padding = padding,
                    Drawing = new Vector4(padding.x / spriteW - .5f, padding.y / spriteH - .5f,
                                          (spriteW - padding.z) / spriteW - .5f, (spriteH - padding.w) / spriteH - .5f),
                },
            };
            int w = source.width, h = source.height;
            if (_enabled && source.format == TextureFormat.RGBA32 && source.mipmapCount == 1 &&
                source.filterMode == FilterMode.Bilinear && source.wrapMode == TextureWrapMode.Clamp &&
                !sprite.packed && w + Border * 2 <= Side && h + Border * 2 <= Side)
            {
                if (_current == null) NewTexture();
                // CopyTexture requires matching GPU formats, including the sRGB flag.
                if (source.graphicsFormat == _current.graphicsFormat)
                {
                    if (_x + w + Border * 2 > Side) { _x = 0; _y += _row; _row = 0; }
                    if (_y + h + Border * 2 > Side) NewTexture();
                    int x = _x + Border, y = _y + Border;
                    Graphics.CopyTexture(source, 0, 0, 0, 0, w, h, _current, 0, 0, x, y);
                    // Extrude the clamp edge so bilinear samples are identical at every UV.
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int sw = dx == 0 ? w : 1, sh = dy == 0 ? h : 1;
                            int sx = dx > 0 ? w - 1 : 0, sy = dy > 0 ? h - 1 : 0;
                            int tx = dx < 0 ? x - 1 : dx > 0 ? x + w : x;
                            int ty = dy < 0 ? y - 1 : dy > 0 ? y + h : y;
                            Graphics.CopyTexture(source, 0, 0, sx, sy, sw, sh, _current, 0, 0, tx, ty);
                        }
                    region.Texture = _current;
                    region.Quad.Uv = MapUv(region.Quad.Uv, x, y, w, h);
                    region.Quad.InnerUv = MapUv(region.Quad.InnerUv, x, y, w, h);
                    _x += w + Border * 2;
                    _row = Mathf.Max(_row, h + Border * 2);
                }
            }
            _regions.Add(sprite, region);
            return region;
        }

        static Vector4 MapUv(Vector4 uv, int x, int y, int w, int h)
            => new Vector4((x + uv.x * w) / Side, (y + uv.y * h) / Side,
                           (x + uv.z * w) / Side, (y + uv.w * h) / Side);

        void NewTexture()
        {
            _current = new Texture2D(Side, Side, TextureFormat.RGBA32, false)
            {
                name = "Shuffle FX masks", filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            _current.Apply(false, true);
            _textures.Add(_current);
            _x = _y = _row = 0;
        }

        public void Dispose()
        {
            foreach (var texture in _textures)
            {
                if (Application.isPlaying) Object.Destroy(texture);
                else Object.DestroyImmediate(texture);
            }
            _textures.Clear();
            _regions.Clear();
            _current = null;
        }
    }
}
