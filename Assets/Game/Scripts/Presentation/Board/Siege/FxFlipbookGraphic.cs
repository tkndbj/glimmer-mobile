using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A pooled projectile's original frames and halo, selected in the shader. One board
    /// advances all books, including the original-sprite fallback, without component churn.
    /// </summary>
    public sealed class FxFlipbookGraphic : Image
    {
        static readonly int PhaseId = Shader.PropertyToID("_BookPhase");
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        Sprite[] _frames;
        Material _ownedMaterial;
        Texture _haloTexture;
        Canvas _isolatedCanvas;
        Vector2 _travelBounds;
        float _growthBounds = 1f, _curveBounds, _haloSize, _anchorOffset;
        Matrix4x4 _lastTransform;
        Material _lastRenderingMaterial;
        float _phase, _fps;
        bool _loop, _done, _flying;
        public bool GpuAnimated { get; private set; }
        public int GeometryBuilds { get; private set; }
        public Action OnFinished;
        internal int ActiveIndex = -1;
        public override Texture mainTexture => GpuAnimated ? _haloTexture : base.mainTexture;
        public float Duration => _frames == null ? 0f : _frames.Length / _fps;

        internal void Play(FxBookLibrary library, Sprite[] frames, float fps, bool loop, float anchorOffset)
        {
            _frames = frames;
            _phase = 0f;
            _fps = fps;
            _loop = loop;
            _done = _flying = false;
            _travelBounds = Vector2.zero;
            _growthBounds = 1f;
            _curveBounds = 0f;
            _haloSize = 1f;
            _anchorOffset = anchorOffset;
            OnFinished = null;
            sprite = frames[0];
            var reel = library.Resolve(frames);
            GpuAnimated = reel != null;
            if (GpuAnimated && _isolatedCanvas == null)
            {
                _isolatedCanvas = gameObject.AddComponent<Canvas>();
                _isolatedCanvas.overrideSorting = false;
                _isolatedCanvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1;
            }
            if (_isolatedCanvas != null) _isolatedCanvas.enabled = GpuAnimated;
            if (GpuAnimated)
            {
                if (_ownedMaterial == null)
                    _ownedMaterial = new Material(library.Shader) { name = "Shuffle projectile", hideFlags = HideFlags.DontSave };
                material = _ownedMaterial;
                _haloTexture = Art.Glow(96, 2f).texture;
                _ownedMaterial.SetTexture("_Frames", reel.Frames);
                _ownedMaterial.SetTexture("_FrameRects", reel.Metadata);
                _ownedMaterial.SetFloat("_FrameCount", reel.Count);
                _ownedMaterial.SetFloat(PhaseId, 0f);
                _ownedMaterial.SetFloat("_Flying", 0f);
                _ownedMaterial.SetFloat("_AnchorOffset", anchorOffset);
                _ownedMaterial.SetColor("_Tint", color);
                _ownedMaterial.SetColor("_HaloTint", Color.clear);
                _ownedMaterial.SetFloat("_HaloSize", 1f);
            }
            else material = null;
            SetAllDirty();
        }

        public void Advance(float rawStep)
        {
            // Flipbook.Update used raw unscaled time, unlike Tween's clamped animation clock.
            // Delayed, inactive projectiles must not advance through their hidden frames.
            if (!isActiveAndEnabled || _done || _frames == null || _frames.Length == 0) return;
            _phase += rawStep * _fps;
            int frame = Mathf.FloorToInt(_phase);
            bool finished = !_loop && frame >= _frames.Length;
            if (GpuAnimated)
            {
                float phase = finished ? _frames.Length - 1 : _phase;
                _ownedMaterial.SetFloat(PhaseId, phase);
                var rendering = materialForRendering;
                if (rendering != _ownedMaterial) rendering.SetFloat(PhaseId, phase);
            }
            else sprite = _frames[finished ? _frames.Length - 1 : frame % _frames.Length];
            if (finished) { _done = true; OnFinished?.Invoke(); }
        }

        internal void Halo(Color tint, float size)
        {
            if (!GpuAnimated) return;
            _ownedMaterial.SetColor("_HaloTint", tint);
            _ownedMaterial.SetFloat("_HaloSize", size);
            _haloSize = size;
            SetVerticesDirty();
            SetMaterialDirty();
        }

        internal bool Fly(Vector2 travel, float angle, float scale, float span, float cell, float tall)
        {
            if (!GpuAnimated) return false;
            float radians = -angle * Mathf.Deg2Rad;
            var local = new Vector2(Mathf.Cos(radians)*travel.x - Mathf.Sin(radians)*travel.y,
                                    Mathf.Sin(radians)*travel.x + Mathf.Cos(radians)*travel.y);
            _ownedMaterial.SetVector("_Travel", new Vector4(local.x, local.y, scale, 0f));
            _ownedMaterial.SetVector("_Crop", new Vector4(tall > 0f ? span / tall : 0f, SiegeView.Emerged(0f, cell, tall)));
            _ownedMaterial.SetFloat("_Flying", 1f);
            _ownedMaterial.SetFloat(ProgressId, -1f); // the original creation frame precedes the tween
            _travelBounds = local;
            _growthBounds = Mathf.Max(1f, 1.12f * scale);
            _curveBounds = 0f;
            _flying = true;
            SetVerticesDirty();
            return true;
        }

        internal bool Throw(Vector2 travel, Vector2 side, float angle, float bow, bool wafts)
        {
            if (!Fly(travel, angle, 1f, 0f, 0f, 0f)) return false;
            float radians = -angle * Mathf.Deg2Rad;
            var local = new Vector2(Mathf.Cos(radians)*side.x - Mathf.Sin(radians)*side.y,
                                    Mathf.Sin(radians)*side.x + Mathf.Cos(radians)*side.y);
            _ownedMaterial.SetVector("_Curve", new Vector4(local.x, local.y, bow, wafts ? 1f : 0f));
            _ownedMaterial.SetFloat("_Flying", 2f);
            _growthBounds = 1.25f;
            _curveBounds = side.magnitude * (Mathf.Abs(bow) * 1.4f + (wafts ? .55f : 0f));
            return true;
        }

        internal void Progress(float phase)
        {
            if (!_flying) return;
            _ownedMaterial.SetFloat(ProgressId, phase);
            var rendering = materialForRendering;
            if (rendering != _ownedMaterial) rendering.SetFloat(ProgressId, phase);
        }

        internal void SyncTransform()
        {
            if (!GpuAnimated || _ownedMaterial == null) return;
            var transform = FxGpu.LocalToCanvas(this);
            var rendering = materialForRendering;
            if (_lastRenderingMaterial == rendering && _lastTransform.Equals(transform)) return;
            _lastTransform = transform;
            _lastRenderingMaterial = rendering;
            _ownedMaterial.SetMatrix(FxGpu.TransformId, transform);
            if (rendering != _ownedMaterial) rendering.SetMatrix(FxGpu.TransformId, transform);
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            if (!GpuAnimated) { base.Cull(clipRect, validRect); return; }
            if (canvasRenderer.cull == !validRect) return;
            canvasRenderer.cull = !validRect;
            onCullStateChanged.Invoke(!validRect);
            OnCullingChanged();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            GeometryBuilds++;
            if (!GpuAnimated) { base.OnPopulateMesh(vh); return; }
            vh.Clear();
            // Canvas/frustum culling sees the immutable mesh, before the vertex shader runs.
            // Its hint positions enclose the complete flight, including the halo and bow.
            float radius = (rectTransform.rect.size.magnitude + _haloSize + Mathf.Abs(_anchorOffset) * 2f) * _growthBounds + _curveBounds;
            var min = Vector2.Min(Vector2.zero, _travelBounds) - Vector2.one * radius;
            var max = Vector2.Max(Vector2.zero, _travelBounds) + Vector2.one * radius;
            Quad(vh, 0f, min, max);
            Quad(vh, 1f, min, max);
        }

        static void Quad(VertexHelper vh, float layer, Vector2 min, Vector2 max)
        {
            int start = vh.currentVertCount;
            var v = UIVertex.simpleVert;
            v.color = Color.white;
            v.uv1 = new Vector4(layer, 0f);
            v.position = new Vector3(min.x, min.y); v.uv0 = Vector4.zero; vh.AddVert(v);
            v.position = new Vector3(min.x, max.y); v.uv0 = new Vector4(0f, 1f); vh.AddVert(v);
            v.position = new Vector3(max.x, max.y); v.uv0 = new Vector4(1f, 1f); vh.AddVert(v);
            v.position = new Vector3(max.x, min.y); v.uv0 = new Vector4(1f, 0f); vh.AddVert(v);
            vh.AddTriangle(start, start+1, start+2);
            vh.AddTriangle(start+2, start+3, start);
        }

        protected override void UpdateMaterial()
        {
            if (GpuAnimated && _ownedMaterial != null)
            {
                _ownedMaterial.SetVector("_Size", new Vector4(rectTransform.rect.width, rectTransform.rect.height));
                var rendering = materialForRendering;
                if (rendering != _ownedMaterial)
                {
                    // Unity caches stencil derivatives. Refresh instance data without replacing
                    // the stencil state which belongs to the ancestor Mask.
                    rendering.SetTexture("_Frames", _ownedMaterial.GetTexture("_Frames"));
                    rendering.SetTexture("_FrameRects", _ownedMaterial.GetTexture("_FrameRects"));
                    rendering.SetFloat("_FrameCount", _ownedMaterial.GetFloat("_FrameCount"));
                    rendering.SetFloat(PhaseId, _ownedMaterial.GetFloat(PhaseId));
                    rendering.SetFloat("_Flying", _ownedMaterial.GetFloat("_Flying"));
                    rendering.SetFloat(ProgressId, _ownedMaterial.GetFloat(ProgressId));
                    rendering.SetFloat("_AnchorOffset", _ownedMaterial.GetFloat("_AnchorOffset"));
                    rendering.SetFloat("_HaloSize", _ownedMaterial.GetFloat("_HaloSize"));
                    rendering.SetColor("_Tint", _ownedMaterial.GetColor("_Tint"));
                    rendering.SetColor("_HaloTint", _ownedMaterial.GetColor("_HaloTint"));
                    rendering.SetVector("_Size", _ownedMaterial.GetVector("_Size"));
                    rendering.SetVector("_Travel", _ownedMaterial.GetVector("_Travel"));
                    rendering.SetVector("_Crop", _ownedMaterial.GetVector("_Crop"));
                    rendering.SetVector("_Curve", _ownedMaterial.GetVector("_Curve"));
                }
            }
            SyncTransform();
            base.UpdateMaterial();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            FxGpu.Release(_ownedMaterial);
            _ownedMaterial = null;
        }
    }
}
