using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Shuffle's transient particles, in reusable mesh pages. Only adjacent compatible runs
    /// merge: batching across an intervening Image would change alpha blending and layering.
    /// Each page stays in the original parent, inheriting its mask, canvas and group alpha.
    /// </summary>
    public sealed class ShuffleFx : MonoBehaviour
    {
        public const int MaxIdlePages = 64;
        static readonly ProfilerMarker SampleMarker = new ProfilerMarker("ShuffleFx.Sample");
        readonly List<FxBatchGraphic> _active = new List<FxBatchGraphic>(16);
        readonly Stack<FxBatchGraphic> _free = new Stack<FxBatchGraphic>(8);
        FxMaskAtlas _atlas;
        RectTransform _storage;
        FxBatchGraphic _tail;
        Shader _particleShader;
        FxBookLibrary _books;
        readonly List<FxFlipbookGraphic> _playing = new List<FxFlipbookGraphic>(32);

        public int ActivePages => _active.Count;
        public int CreatedPages { get; private set; }
        public int IdlePages => _free.Count;
        public int ParticleCount
        {
            get { int count = 0; for (int i = 0; i < _active.Count; i++) count += _active[i].Count; return count; }
        }

        public static ShuffleFx Attach(RectTransform field, bool atlas = true, int warmPages = 8, bool gpu = true)
        {
            // Using the same clock also preserves pause, app-resume and callback emission timing.
            _ = Tween.Inst;
            var fx = field.gameObject.AddComponent<ShuffleFx>();
            fx._atlas = new FxMaskAtlas(atlas);
            fx._particleShader = gpu ? FxGpu.ParticleShader() : null;
            fx._books = new FxBookLibrary(gpu);
            FxGpu.SetClock(Tween.Clock);
            fx._storage = UIKit.Node("FX storage", field);
            fx._storage.gameObject.SetActive(false);
            for (int i = 0; i < Mathf.Clamp(warmPages, 0, MaxIdlePages); i++)
            {
                var page = fx.NewPage();
                page.Reserve(FxBatchGraphic.Capacity);
                fx._free.Push(page);
            }
            return fx;
        }

        public void Prepare(Sprite sprite)
        {
            if (sprite != null) _atlas.Resolve(sprite);
        }

        public void Emit(RectTransform parent, Sprite sprite, Material sharedMaterial, FxParticle particle)
        {
            if (parent == null || sprite == null || particle.Duration <= 0f) return;
            var region = _atlas.Resolve(sprite);
            if (_tail == null || _tail.Used >= FxBatchGraphic.Capacity ||
                _tail.transform.parent != parent || _tail.mainTexture != region.Texture ||
                _tail.SourceMaterial != sharedMaterial ||
                _tail.transform.GetSiblingIndex() != parent.childCount - 1)
            {
                _tail = _free.Count > 0 ? _free.Pop() : NewPage();
                _tail.transform.SetParent(parent, false);
                var parentCanvas = parent.GetComponentInParent<Canvas>();
                _tail.ReferencePixels = parentCanvas != null ? parentCanvas.referencePixelsPerUnit : 100f;
                _tail.rectTransform.anchorMin = Vector2.zero;
                _tail.rectTransform.anchorMax = Vector2.one;
                _tail.rectTransform.offsetMin = _tail.rectTransform.offsetMax = Vector2.zero;
                _tail.Bind(region.Texture, sharedMaterial);
                _tail.gameObject.SetActive(true);
                _tail.transform.SetAsLastSibling();
                _active.Add(_tail);
            }
            var quad = region.Quad;
            float pixelScale = region.UnitsPerPixel * _tail.ReferencePixels;
            quad.Border *= pixelScale;
            quad.Padding *= pixelScale;
            _tail.Append(particle, quad, Tween.Clock);
        }

        FxBatchGraphic NewPage()
        {
            var node = UIKit.Node("FX batch", _storage);
            node.gameObject.SetActive(false);
            var page = node.gameObject.AddComponent<FxBatchGraphic>();
            // A run stays at its original sibling position. Its own non-sorting canvas
            // isolates vertex uploads from the board without moving pops above other art.
            var canvas = node.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = false;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1;
            page.EnableGpu(_particleShader);
            page.raycastTarget = false;
            CreatedPages++;
            return page;
        }

        void LateUpdate()
        {
            Sample(Tween.Clock);
            for (int i = 0; i < _playing.Count; i++)
                if (_playing[i] != null) _playing[i].SyncTransform();
        }

        void Update() => AdvanceBooks(Time.unscaledDeltaTime);

        internal void Play(FxFlipbookGraphic graphic, Sprite[] frames, float fps, bool loop, float anchorOffset)
        {
            graphic.Play(_books, frames, fps, loop, anchorOffset);
            if (graphic.ActiveIndex >= 0) return;
            graphic.ActiveIndex = _playing.Count;
            _playing.Add(graphic);
        }

        internal void Stop(FxFlipbookGraphic graphic)
        {
            int index = graphic.ActiveIndex;
            if (index < 0) return;
            var last = _playing[_playing.Count - 1];
            _playing[index] = last;
            last.ActiveIndex = index;
            _playing.RemoveAt(_playing.Count - 1);
            graphic.ActiveIndex = -1;
            graphic.OnFinished = null;
        }

        public void AdvanceBooks(float rawStep)
        {
            for (int i = 0; i < _playing.Count;)
            {
                var book = _playing[i];
                if (book == null)
                {
                    int last = _playing.Count - 1;
                    _playing[i] = _playing[last];
                    if (_playing[i] != null) _playing[i].ActiveIndex = i;
                    _playing.RemoveAt(last);
                    continue;
                }
                book.Advance(rawStep);
                // A completion can return this book to the pool, swapping the last one here.
                if (i < _playing.Count && _playing[i] == book) i++;
            }
        }

        /// <summary>Also used by the render comparison and stress fixtures without real time.</summary>
        public void Sample(double clock)
        {
            using (SampleMarker.Auto())
            {
                FxGpu.SetClock(clock);
                int kept = 0;
                for (int i = 0; i < _active.Count; i++)
                {
                    var page = _active[i];
                    if (page == null) continue;
                    page.Sample(clock);
                    if (page.Count > 0) { _active[kept++] = page; continue; }
                    if (_tail == page) _tail = null;
                    Recycle(page);
                }
                if (kept < _active.Count) _active.RemoveRange(kept, _active.Count - kept);
            }
        }

        public void ClearParticles()
        {
            foreach (var book in _playing)
                if (book != null) { book.ActiveIndex = -1; book.OnFinished = null; }
            _playing.Clear();
            for (int i = 0; i < _active.Count; i++)
            {
                var page = _active[i];
                if (page == null) continue;
                Recycle(page);
            }
            _active.Clear();
            _tail = null;
        }

        void Recycle(FxBatchGraphic page)
        {
            page.gameObject.SetActive(false);
            page.ClearQuads();
            if (_free.Count >= MaxIdlePages)
            {
                if (Application.isPlaying) Destroy(page.gameObject);
                else DestroyImmediate(page.gameObject);
                return;
            }
            page.transform.SetParent(_storage, false);
            _free.Push(page);
        }

        void OnDestroy()
        {
            _atlas?.Dispose();
            _books?.Dispose();
        }
    }
}
