using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Sprites;

namespace GlimmerGrove
{
    /// <summary>Board-owned, exact-size GPU frame arrays. Original sprites remain the fallback.</summary>
    sealed class FxBookLibrary : IDisposable
    {
        internal sealed class Reel
        {
            public Texture2DArray Frames;
            public Texture2D Metadata;
            public int Count;
        }
        sealed class FramesComparer : IEqualityComparer<Sprite[]>
        {
            public bool Equals(Sprite[] a, Sprite[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++) if (!ReferenceEquals(a[i], b[i])) return false;
                return true;
            }
            public int GetHashCode(Sprite[] frames) => frames.Length == 0 ? 0 :
                frames.Length * 397 ^ (!ReferenceEquals(frames[0], null) ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frames[0]) : 0);
        }
        readonly Dictionary<Sprite[], Reel> _reels = new Dictionary<Sprite[], Reel>(new FramesComparer());
        readonly Shader _shader;
        internal Shader Shader => _shader;
        public FxBookLibrary(bool enabled)
        {
            var candidate = enabled ? UnityEngine.Shader.Find("GlimmerGrove/Shuffle Flipbook") : null;
            if (candidate != null && candidate.isSupported && SystemInfo.supports2DArrayTextures &&
                SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)) _shader = candidate;
        }
        internal Reel Resolve(Sprite[] frames)
        {
            if (_reels.TryGetValue(frames, out var found)) return found;
            var reel = Build(frames);
            _reels.Add(frames, reel);
            return reel;
        }
        Reel Build(Sprite[] frames)
        {
            if (_shader == null || frames.Length == 0 || frames[0] == null || frames.Length > SystemInfo.maxTextureArraySlices) return null;
            var source = frames[0].texture;
            var support = SystemInfo.copyTextureSupport;
            if ((support & CopyTextureSupport.DifferentTypes) == 0) return null;
            foreach (var frame in frames)
                if (frame == null || frame.packed || frame.texture.width != source.width ||
                    frame.texture.height != source.height || frame.texture.graphicsFormat != source.graphicsFormat ||
                    frame.rect.size != frames[0].rect.size ||
                    frame.texture.mipmapCount != 1 || frame.texture.filterMode != source.filterMode ||
                    frame.texture.wrapMode != TextureWrapMode.Clamp) return null;

            bool direct = SystemInfo.IsFormatSupported(source.graphicsFormat, GraphicsFormatUsage.Sample);
            if (!direct && (support & CopyTextureSupport.RTToTexture) == 0) return null;
            var format = direct ? source.graphicsFormat : (GraphicsFormatUtility.IsSRGBFormat(source.graphicsFormat)
                ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm);
            var reel = new Reel { Count = frames.Length };
            try
            {
                reel.Frames = new Texture2DArray(source.width, source.height, frames.Length, format, TextureCreationFlags.None)
                {
                    name = "Shuffle projectile frames", filterMode = source.filterMode,
                    anisoLevel = source.anisoLevel,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                // Discard the CPU shadow before GPU copies; Apply after copying would overwrite them.
                reel.Frames.Apply(false, true);
                var metadata = new Color[frames.Length * 2];
                for (int i = 0; i < frames.Length; i++)
                {
                    var frame = frames[i];
                    if (direct) Graphics.CopyTexture(frame.texture, 0, 0, reel.Frames, i, 0);
                    else CopyDecoded(frame.texture, reel.Frames, i, format);
                    var uv = DataUtility.GetOuterUV(frame);
                    metadata[i] = new Color(uv.x, uv.y, uv.z, uv.w);
                    var p = DataUtility.GetPadding(frame);
                    int w = Mathf.RoundToInt(frame.rect.width), h = Mathf.RoundToInt(frame.rect.height);
                    metadata[frames.Length + i] = new Color(p.x/w-.5f, p.y/h-.5f, (w-p.z)/w-.5f, (h-p.w)/h-.5f);
                }
                reel.Metadata = new Texture2D(frames.Length, 2, TextureFormat.RGBAFloat, false, true)
                {
                    name = "Shuffle frame rectangles", filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                };
                reel.Metadata.SetPixels(metadata);
                reel.Metadata.Apply(false, true);
                return reel;
            }
            catch
            {
                // A backend which cannot copy this imported format keeps the original art path.
                FxGpu.Release(reel.Frames);
                FxGpu.Release(reel.Metadata);
                return null;
            }
        }
        static void CopyDecoded(Texture source, Texture2DArray target, int slice, GraphicsFormat format)
        {
            var descriptor = new RenderTextureDescriptor(source.width, source.height)
                { graphicsFormat = format, depthBufferBits = 0, msaaSamples = 1 };
            var temporary = RenderTexture.GetTemporary(descriptor);
            var previous = RenderTexture.active;
            bool srgb = GL.sRGBWrite;
            try
            {
                GL.sRGBWrite = GraphicsFormatUtility.IsSRGBFormat(format);
                Graphics.Blit(source, temporary);
                Graphics.CopyTexture(temporary, 0, 0, target, slice, 0);
            }
            finally
            {
                GL.sRGBWrite = srgb;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(temporary);
            }
        }
        public void Dispose()
        {
            foreach (var reel in _reels.Values)
                if (reel != null) { FxGpu.Release(reel.Frames); FxGpu.Release(reel.Metadata); }
            _reels.Clear();
        }
    }
}
