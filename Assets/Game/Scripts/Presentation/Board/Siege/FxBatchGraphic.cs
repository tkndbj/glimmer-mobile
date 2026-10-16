using UnityEngine;
using UnityEngine.UI;
using Unity.Profiling;

namespace GlimmerGrove
{
    public struct FxQuad
    {
        public Vector4 Uv, InnerUv, Border, Padding, Drawing;
    }

    /// <summary>
    /// One ordered run of compatible quads, reused whole. GPU slots stay immutable until the
    /// page expires; the original CPU fallback uses stable compaction to preserve alpha order.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FxBatchGraphic : MaskableGraphic
    {
        public const int Capacity = 256;
        static readonly ProfilerMarker GeometryMarker = new ProfilerMarker("ShuffleFx.Geometry");

        struct Live
        {
            public FxParticle Particle;
            public FxPose Pose;
            public FxQuad Quad;
            public double Born;
            public bool Expired;
        }

        // Ordered runs can be short when legacy effects intervene. Reserve a small buffer
        // per run, then grow to its high-water mark instead of reserving 256 slots for each.
        Live[] _live = new Live[32];
        Texture _texture;
        Shader _gpuShader;
        Material _alphaMaterial, _addMaterial, _sourceMaterial;
        Texture2D _records;
        Color[] _recordPixels;
        int _used;
        double _nextExpiry = double.PositiveInfinity;
        Vector2 _gpuMin, _gpuMax;
        Matrix4x4 _lastTransform;
        Material _lastRenderingMaterial;
        public int Count { get; private set; }
        public int Used => GpuAnimated ? _used : Count;
        public bool GpuAnimated { get; private set; }
        public int GeometryBuilds { get; private set; }
        public Material SourceMaterial => _sourceMaterial;
        internal float ReferencePixels { get; set; } = 100f;
        public override Texture mainTexture => _texture != null ? _texture : s_WhiteTexture;

        internal void EnableGpu(Shader shader) => _gpuShader = shader;

        internal void Reserve(int count)
        {
            if (_live.Length < count) System.Array.Resize(ref _live, Mathf.Min(Capacity, count));
        }

        public void Bind(Texture texture, Material sharedMaterial)
        {
            _texture = texture;
            _sourceMaterial = sharedMaterial;
            GpuAnimated = _gpuShader != null && FxGpu.Supports(sharedMaterial);
            if (GpuAnimated)
            {
                if (_records == null)
                {
                    _recordPixels = new Color[Capacity * 5];
                    _records = new Texture2D(Capacity, 5, TextureFormat.RGBAFloat, false, true)
                    {
                        name = "Shuffle particle records", filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
                    };
                }
                if (sharedMaterial == null)
                {
                    if (_alphaMaterial == null) _alphaMaterial = FxGpu.ParticleMaterial(_gpuShader, _records, false);
                    material = _alphaMaterial;
                }
                else
                {
                    if (_addMaterial == null) _addMaterial = FxGpu.ParticleMaterial(_gpuShader, _records, true);
                    material = _addMaterial;
                }
            }
            else material = sharedMaterial;
            color = Color.white;
            raycastTarget = false;
            SetMaterialDirty();
        }

        public void Append(FxParticle particle, FxQuad quad, double born)
        {
            int index = Used;
            if (index == _live.Length) System.Array.Resize(ref _live, Mathf.Min(Capacity, _live.Length * 2));
            _live[index] = new Live
            {
                Particle = particle, Quad = quad, Born = born,
                Pose = FxAnimation.Evaluate(particle, 0f),
            };
            Count++;
            if (GpuAnimated)
            {
                _used++;
                _nextExpiry = System.Math.Min(_nextExpiry, born + particle.Delay + particle.Duration);
                var split = FxGpu.Split(born);
                _recordPixels[index] = new Color(particle.At.x, particle.At.y, particle.Size.x, particle.Size.y);
                _recordPixels[Capacity + index] = new Color(particle.Velocity.x, particle.Velocity.y, particle.Spin, particle.Scale);
                _recordPixels[Capacity * 2 + index] = new Color(split.x, split.y, particle.Duration, particle.Delay);
                _recordPixels[Capacity * 3 + index] = particle.Tint;
                _recordPixels[Capacity * 4 + index] = new Color(particle.Angle, particle.Gravity, particle.Phase, (float)particle.Motion);
            }
            SetVerticesDirty();
        }

        public void Sample(double clock)
        {
            if (GpuAnimated)
            {
                SyncTransform();
                // No pose evaluation, vertex dirtying or mesh upload as time passes. Scan only
                // when an expiry is due; leave surviving slots in their original alpha order.
                if (clock < _nextExpiry) return;
                _nextExpiry = double.PositiveInfinity;
                for (int i = 0; i < _used; i++)
                {
                    if (_live[i].Expired) continue;
                    double expires = _live[i].Born + _live[i].Particle.Delay + _live[i].Particle.Duration;
                    if (clock >= expires) { _live[i].Expired = true; Count--; }
                    else _nextExpiry = System.Math.Min(_nextExpiry, expires);
                }
                return;
            }
            int kept = 0;
            for (int i = 0; i < Count; i++)
            {
                var live = _live[i];
                float seconds = (float)(clock - live.Born);
                if (seconds >= live.Particle.Delay + live.Particle.Duration) continue;
                live.Pose = FxAnimation.Evaluate(live.Particle, seconds);
                _live[kept++] = live;
            }
            Count = kept;
            SetVerticesDirty();
        }

        void SyncTransform()
        {
            if (!GpuAnimated) return;
            var transform = FxGpu.LocalToCanvas(this);
            var rendering = materialForRendering;
            if (_lastRenderingMaterial == rendering && _lastTransform.Equals(transform)) return;
            _lastTransform = transform;
            _lastRenderingMaterial = rendering;
            material.SetMatrix(FxGpu.TransformId, transform);
            if (rendering != material) rendering.SetMatrix(FxGpu.TransformId, transform);
        }

        protected override void UpdateMaterial()
        {
            SyncTransform();
            base.UpdateMaterial();
        }

        public override void Cull(Rect clipRect, bool validRect)
        {
            if (!GpuAnimated) { base.Cull(clipRect, validRect); return; }
            // Shader motion can carry a quad into a mask while its static RectTransform is
            // outside. The shader clips the animated vertices; only an invalid mask culls all.
            if (canvasRenderer.cull == !validRect) return;
            canvasRenderer.cull = !validRect;
            onCullStateChanged.Invoke(!validRect);
            OnCullingChanged();
        }

        public void ClearQuads()
        {
            Count = 0;
            _used = 0;
            _nextExpiry = double.PositiveInfinity;
            _texture = null;
            material = null;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            using var sample = GeometryMarker.Auto();
            GeometryBuilds++;
            vh.Clear();
            if (GpuAnimated)
            {
                _records.SetPixels(_recordPixels);
                _records.Apply(false, false);
                for (int i = 0; i < _used; i++)
                {
                    var live = _live[i];
                    Bounds(live.Particle, out _gpuMin, out _gpuMax);
                    live.Pose = new FxPose { Size = live.Particle.Size, Tint = Color.white };
                    if (live.Quad.Border != Vector4.zero) Sliced(vh, live, 0f, 1f, i);
                    else
                    {
                        var r = live.Quad.Drawing;
                        var size = live.Particle.Size;
                        Quad(vh, new Vector2(r.x * size.x, r.y * size.y), new Vector2(r.z * size.x, r.w * size.y), live.Quad.Uv, i);
                    }
                }
                return;
            }
            for (int i = 0; i < Count; i++)
            {
                var live = _live[i];
                var pose = live.Pose;
                if (pose.Tint.a <= 0f || pose.Size.x == 0f || pose.Size.y == 0f) continue;

                float radians = pose.Angle * Mathf.Deg2Rad;
                float sin = Mathf.Sin(radians), cos = Mathf.Cos(radians);
                if (live.Quad.Border != Vector4.zero)
                {
                    Sliced(vh, live, sin, cos);
                    continue;
                }
                var x = new Vector2(cos, sin) * pose.Size.x;
                var y = new Vector2(-sin, cos) * pose.Size.y;
                var uv = live.Quad.Uv;
                var rect = live.Quad.Drawing;
                Color32 tint = pose.Tint;
                int first = vh.currentVertCount;
                vh.AddVert(pose.At + x * rect.x + y * rect.y, tint, new Vector2(uv.x, uv.y));
                vh.AddVert(pose.At + x * rect.x + y * rect.w, tint, new Vector2(uv.x, uv.w));
                vh.AddVert(pose.At + x * rect.z + y * rect.w, tint, new Vector2(uv.z, uv.w));
                vh.AddVert(pose.At + x * rect.z + y * rect.y, tint, new Vector2(uv.z, uv.y));
                vh.AddTriangle(first, first + 1, first + 2);
                vh.AddTriangle(first + 2, first + 3, first);
            }
        }

        void Sliced(VertexHelper vh, Live live, float sin, float cos, int record = -1)
        {
            var size = live.Particle.Size;
            if (size.x <= 0f || size.y <= 0f) return;
            var quad = live.Quad;
            var border = quad.Border;
            float horizontal = border.x + border.z, vertical = border.y + border.w;
            if (horizontal > size.x) { float k = size.x / horizontal; border.x *= k; border.z *= k; }
            if (vertical > size.y) { float k = size.y / vertical; border.y *= k; border.w *= k; }
            var pose = live.Pose;
            var xAxis = new Vector2(cos, sin) * (pose.Size.x / size.x);
            var yAxis = new Vector2(-sin, cos) * (pose.Size.y / size.y);
            Color32 tint = pose.Tint;
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                {
                    float left = Edge(x, size.x, border.x, border.z, quad.Padding.x, quad.Padding.z);
                    float right = Edge(x + 1, size.x, border.x, border.z, quad.Padding.x, quad.Padding.z);
                    float bottom = Edge(y, size.y, border.y, border.w, quad.Padding.y, quad.Padding.w);
                    float top = Edge(y + 1, size.y, border.y, border.w, quad.Padding.y, quad.Padding.w);
                    if (right <= left || top <= bottom) continue;
                    float u0 = UvEdge(x, quad.Uv.x, quad.InnerUv.x, quad.InnerUv.z, quad.Uv.z);
                    float u1 = UvEdge(x + 1, quad.Uv.x, quad.InnerUv.x, quad.InnerUv.z, quad.Uv.z);
                    float v0 = UvEdge(y, quad.Uv.y, quad.InnerUv.y, quad.InnerUv.w, quad.Uv.w);
                    float v1 = UvEdge(y + 1, quad.Uv.y, quad.InnerUv.y, quad.InnerUv.w, quad.Uv.w);
                    if (record >= 0)
                    {
                        Quad(vh, new Vector2(left, bottom), new Vector2(right, top), new Vector4(u0, v0, u1, v1), record);
                        continue;
                    }
                    int first = vh.currentVertCount;
                    vh.AddVert(pose.At + xAxis * left + yAxis * bottom, tint, new Vector2(u0, v0));
                    vh.AddVert(pose.At + xAxis * left + yAxis * top, tint, new Vector2(u0, v1));
                    vh.AddVert(pose.At + xAxis * right + yAxis * top, tint, new Vector2(u1, v1));
                    vh.AddVert(pose.At + xAxis * right + yAxis * bottom, tint, new Vector2(u1, v0));
                    vh.AddTriangle(first, first + 1, first + 2);
                    vh.AddTriangle(first + 2, first + 3, first);
                }
        }

        void Quad(VertexHelper vh, Vector2 min, Vector2 max, Vector4 uv, int record)
        {
            int first = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = Color.white;
            vertex.uv1 = new Vector4(record, min.x, min.y, 0f);
            vertex.position = new Vector3(_gpuMin.x, _gpuMin.y); vertex.uv0 = new Vector4(uv.x, uv.y); vh.AddVert(vertex);
            vertex.uv1 = new Vector4(record, min.x, max.y, 0f);
            vertex.position = new Vector3(_gpuMin.x, _gpuMax.y); vertex.uv0 = new Vector4(uv.x, uv.w); vh.AddVert(vertex);
            vertex.uv1 = new Vector4(record, max.x, max.y, 0f);
            vertex.position = new Vector3(_gpuMax.x, _gpuMax.y); vertex.uv0 = new Vector4(uv.z, uv.w); vh.AddVert(vertex);
            vertex.uv1 = new Vector4(record, max.x, min.y, 0f);
            vertex.position = new Vector3(_gpuMax.x, _gpuMin.y); vertex.uv0 = new Vector4(uv.z, uv.y); vh.AddVert(vertex);
            vh.AddTriangle(first, first + 1, first + 2);
            vh.AddTriangle(first + 2, first + 3, first);
        }

        static void Bounds(FxParticle p, out Vector2 min, out Vector2 max)
        {
            // The CPU mesh must enclose shader motion for native canvas/frustum culling.
            Vector2 end = p.At;
            float scale = 1.6f;
            if (p.Motion == FxMotion.Pop || p.Motion == FxMotion.Wave) scale = Mathf.Max(scale, p.Scale);
            if (p.Motion == FxMotion.Spark || p.Motion == FxMotion.Cinder) end += p.Velocity;
            if (p.Motion == FxMotion.Ember)
            {
                end += p.Velocity * p.Duration - Vector2.up * (.5f * p.Gravity * p.Duration * p.Duration);
                float finalSpeed = (p.Velocity - Vector2.up * (p.Gravity * p.Duration)).magnitude;
                scale = Mathf.Max(scale, 1f + Mathf.Max(p.Velocity.magnitude, finalSpeed) / Mathf.Max(.0001f, p.Scale));
            }
            float radius = p.Size.magnitude * .5f * scale;
            min = Vector2.Min(p.At, end) - Vector2.one * radius;
            max = Vector2.Max(p.At, end) + Vector2.one * radius;
            if (p.Motion == FxMotion.Ember && p.Gravity > 0f)
            {
                float apex = Mathf.Clamp(p.Velocity.y / p.Gravity, 0f, p.Duration);
                max.y = Mathf.Max(max.y, p.At.y + p.Velocity.y * apex - .5f * p.Gravity * apex * apex + radius);
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            FxGpu.Release(_alphaMaterial);
            FxGpu.Release(_addMaterial);
            FxGpu.Release(_records);
            _alphaMaterial = _addMaterial = null;
            _records = null;
        }

        static float Edge(int n, float size, float before, float after, float padBefore, float padAfter)
            => -size * .5f + (n == 0 ? padBefore : n == 1 ? before : n == 2 ? size - after : size - padAfter);

        static float UvEdge(int n, float outerStart, float innerStart, float innerEnd, float outerEnd)
            => n == 0 ? outerStart : n == 1 ? innerStart : n == 2 ? innerEnd : outerEnd;
    }
}
