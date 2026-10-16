using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlimmerGrove
{
    /// <summary>Only Shuffle uses these shaders. Both have explicit player-build retention.</summary>
    static class FxGpu
    {
        static readonly int ClockId = Shader.PropertyToID("_ShuffleFxClock");
        internal static readonly int TransformId = Shader.PropertyToID("_FxLocalToCanvas");
        internal static Matrix4x4 LocalToCanvas(UnityEngine.UI.Graphic graphic)
        {
            var canvas = graphic.canvas;
            return canvas != null ? canvas.rootCanvas.transform.worldToLocalMatrix * graphic.transform.localToWorldMatrix
                                  : Matrix4x4.identity;
        }
        public static Vector4 Split(double clock)
        {
            double high = Math.Floor(clock / 1024d) * 1024d;
            return new Vector4((float)high, (float)(clock - high), 0f, 0f);
        }
        public static void SetClock(double clock) => Shader.SetGlobalVector(ClockId, Split(clock));
        public static Shader ParticleShader()
        {
            var shader = Shader.Find("GlimmerGrove/Shuffle Particles");
            return shader != null && shader.isSupported && SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat)
                ? shader : null;
        }
        public static bool Supports(Material source) => source == null || source == Additive.Material;
        public static Material ParticleMaterial(Shader shader, Texture records, bool additive)
        {
            var material = new Material(shader) { name = "Shuffle particle page", hideFlags = HideFlags.DontSave };
            material.SetTexture("_Records", records);
            material.SetFloat("_SrcBlend", (float)(additive ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_Premultiply", additive ? 0f : 1f);
            return material;
        }
        public static void Release(UnityEngine.Object owned)
        {
            if (owned == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(owned);
            else UnityEngine.Object.DestroyImmediate(owned);
        }
    }
}
