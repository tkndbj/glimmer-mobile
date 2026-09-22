using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Frames;
using UnityEngine;
using UnityEngine.U2D;
using UnityEngine.U2D.Animation;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A name frame, drawn on the canvas and alive.
    ///
    /// <para>
    /// <b>Why this is a <see cref="MaskableGraphic"/> and not a <c>SpriteRenderer</c>.</b> The
    /// whole UI is one <c>ScreenSpaceOverlay</c> canvas and the only camera in the game draws
    /// nothing (<c>Boot.EnsureCamera</c>, <c>cullingMask = 0</c>), so a sprite in the world is
    /// never seen — the bench in <c>VfxDemoScreen</c> pays for its own camera and render texture
    /// to get round exactly that, which is the wrong price for a thing that sits on a scrolling
    /// row under a <c>RectMask2D</c>. So the package's runtime does what it is for and nothing
    /// else: a <see cref="SpriteSkin"/> on a hidden stage deforms the sprite's rigged mesh
    /// against the bones every frame, this graphic reads the deformed vertices back
    /// (<see cref="SpriteSkin.GetDeformedVertexPositionData"/>) and pushes them into the
    /// canvas mesh with the sprite's own UVs. Masking, clipping, sibling order and the canvas
    /// scaler all work because it is an ordinary graphic; the animation is the package's
    /// because it is an ordinary skin.
    /// </para>
    /// <para>
    /// <b>The rig comes with the sprite</b> — bones, bind poses and weights written into the
    /// texture's import by <c>FrameRigImport</c> from the rig <c>make_frame_rig.py</c> derives —
    /// so this class builds the bone hierarchy off <c>Sprite.GetBones()</c> and knows nothing
    /// about any particular frame. What it animates is <see cref="FrameIdle"/>: the nod turns
    /// the bone named <see cref="FrameDefinition.NodBone"/>, the eyes and the sheen are two
    /// floats on the material, and a frame whose rig has no such bone simply does not nod.
    /// </para>
    /// <para>
    /// <b>The sprite is asked for, never loaded, here</b> (7b). Whoever builds this holds the
    /// frame's scope and calls <see cref="Show"/> again when it lands; until then the graphic is
    /// disabled and draws nothing, which is the one state an <c>Image</c> cannot be in.
    /// </para>
    /// </summary>
    // **`RequireComponent(CanvasRenderer)`, as `Image` declares it, because `Graphic` does not.**
    // `Graphic.canvasRenderer` adds one lazily only when `GetComponent` answers a true null,
    // and in the Editor a missing component answers a *fake* null — so a graphic added to a
    // bare node drew nothing at all, with every other probe green (skin valid, shader bound,
    // mesh populated). Found on the first play-mode look, 2026-09-21.
    [RequireComponent(typeof(CanvasRenderer))]
    [DefaultExecutionOrder(Order)]
    public sealed class NameFrame : MaskableGraphic
    {
        /// <summary>
        /// After the package's <c>DeformationManagerUpdater</c> (<c>UpdateOrder.spriteSkinUpdateOrder</c>,
        /// 10), so a frame's <c>LateUpdate</c> reads this frame's deformation and not last frame's.
        /// </summary>
        public const int Order = 40;

        public const string ShaderName = "Gemfire/UI Name Frame";

        static readonly int ShineId = Shader.PropertyToID("_Shine");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int EyeId = Shader.PropertyToID("_EyeUV");
        static readonly int GlowRadiusId = Shader.PropertyToID("_GlowRadius");
        static readonly int AspectId = Shader.PropertyToID("_Aspect");

        FrameDefinition _frame;
        Sprite _sprite;
        GameObject _stage;
        SpriteSkin _skin;
        Transform _nod;
        Quaternion _nodBind;
        Vector2[] _bind, _uv;
        ushort[] _tris;
        Vector3[] _live;
        bool _deformed;
        FrameIdle _idle;
        Material _mat;

        /// <summary>Stand one in a box, the way <c>UIKit.Img</c> stands an image.</summary>
        public static NameFrame Build(string name, Transform parent, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            var rt = UIKit.Box(name, parent, size, anchor, pos);
            var frame = rt.gameObject.AddComponent<NameFrame>();
            frame.raycastTarget = false;
            frame.color = Color.white;
            return frame;
        }

        /// <summary>The frame being shown, whether or not its painting has arrived.</summary>
        public FrameDefinition Frame => _frame;

        /// <summary>True once the painting is here and the frame is drawing.</summary>
        public bool Drawn => _sprite != null && enabled;

        /// <summary>
        /// Show this frame, or none. Asked again when a scope lands — a call that changes
        /// nothing costs nothing, so a repaint may call it freely.
        /// </summary>
        public void Show(FrameDefinition frame)
        {
            var sprite = frame == null ? null : AssetLibrary.Peek<Sprite>(frame.Address);
            if (frame == _frame && sprite == _sprite) return;

            Teardown();
            _frame = frame;
            _sprite = sprite;

            if (sprite == null)
            {
                enabled = false;
                return;
            }

            BuildRig();
            enabled = true;
            SetMaterialDirty();
            SetVerticesDirty();
        }

        public void Clear() => Show(null);

        public override Texture mainTexture => _sprite != null ? _sprite.texture : s_WhiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMaterial();
        }

        protected override void OnDestroy()
        {
            Teardown();
            if (_mat != null)
            {
                if (Application.isPlaying) Destroy(_mat); else DestroyImmediate(_mat);
                _mat = null;
            }
            base.OnDestroy();
        }

        void EnsureMaterial()
        {
            if (_mat != null) return;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                // Said once, plainly: the frame still draws through the default UI material,
                // without its sheen and its eyes. The shader is in the always-included list.
                Debug.LogError("NameFrame: shader '" + ShaderName + "' is not in the build.");
                return;
            }

            _mat = new Material(shader) { name = "~NameFrame", hideFlags = HideFlags.HideAndDontSave };
            material = _mat;
        }

        // ------------------------------------------------------------------ the stage
        void BuildRig()
        {
            _bind = _sprite.vertices;
            _uv = _sprite.uv;
            _tris = _sprite.triangles;
            _live = new Vector3[_bind.Length];
            _deformed = false;
            _nod = null;

            _stage = new GameObject("Rig") { hideFlags = HideFlags.DontSave };
            _stage.transform.SetParent(transform, false);

            // The renderer is what the skin deforms and has to stay enabled for that
            // (`SpriteSkin` skips a disabled one); nothing draws it, because no camera in the
            // game draws anything.
            var renderer = _stage.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;

            _skin = _stage.AddComponent<SpriteSkin>();
            _skin.alwaysUpdate = true;
            _skin.forceCpuDeformation = true;

            var bones = _sprite.GetBones();
            if (bones != null && bones.Length > 0)
            {
                var transforms = new Transform[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    var go = new GameObject(bone.name);
                    var parent = bone.parentId < 0 || bone.parentId >= i
                        ? _stage.transform
                        : transforms[bone.parentId];
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = bone.position;
                    go.transform.localRotation = bone.rotation;
                    transforms[i] = go.transform;

                    if (bone.name == FrameDefinition.NodBone)
                    {
                        _nod = go.transform;
                        _nodBind = bone.rotation;
                    }
                }

                _skin.SetRootBone(transforms[0]);
                _skin.SetBoneTransforms(transforms);
            }

            // Seeded off the instance so two frames on one screen never nod together.
            _idle = new FrameIdle(unchecked((uint)GetHashCode() * 2654435761u));
        }

        void Teardown()
        {
            if (_stage != null)
            {
                if (Application.isPlaying) Destroy(_stage); else DestroyImmediate(_stage);
            }
            _stage = null;
            _skin = null;
            _nod = null;
            _idle = null;
            _bind = null;
            _uv = null;
            _tris = null;
            _live = null;
            _deformed = false;
        }

        // ------------------------------------------------------------------ the beat
        void Update()
        {
            if (_idle == null || _frame == null) return;

            // Unscaled: the ads plugin's Editor consent stub parks `timeScale` at nought and
            // leaves it there (CLAUDE.md), and a frame that stops breathing during a modal
            // reads as broken.
            _idle.Advance(Time.unscaledDeltaTime);

            if (_nod != null)
                _nod.localRotation = _nodBind * Quaternion.Euler(0f, 0f, _idle.HeadDegrees);

            // On the material being rendered, which under a stencil `Mask` is a copy of ours:
            // a float set on the base would never reach the screen.
            var m = materialForRendering;
            if (m == null || !m.HasProperty(ShineId)) return;

            m.SetFloat(ShineId, _idle.ShinePosition);
            m.SetFloat(GlowId, _idle.EyeGlow);
            m.SetVector(EyeId, new Vector4(_frame.Eye.x, _frame.Eye.y, 0f, 0f));
            m.SetFloat(GlowRadiusId, _frame.EyeGlowRadius);
            m.SetFloat(AspectId, _sprite != null ? _sprite.rect.width / _sprite.rect.height : 1f);
        }

        void LateUpdate()
        {
            if (_skin == null || _live == null || !_skin.isActiveAndEnabled) return;
            if (!_skin.HasCurrentDeformedVertices()) return;

            int i = 0;
            foreach (var v in _skin.GetDeformedVertexPositionData())
            {
                if (i >= _live.Length) break;
                _live[i++] = v;
            }

            _deformed = i == _live.Length;
            SetVerticesDirty();
        }

        // ------------------------------------------------------------------ the mesh
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_sprite == null || _bind == null || _uv == null || _tris == null) return;

            // The sprite's own frame is pixels of the imported texture around its pivot; the
            // graphic's is its rect. A vertex is mapped through the painting's fractions so the
            // frame keeps its shape whatever box it is stood in, and a nod may leave the box.
            var box = GetPixelAdjustedRect();
            var rect = _sprite.rect;
            var pivot = _sprite.pivot;
            float ppu = _sprite.pixelsPerUnit;
            Color32 tint = color;

            for (int i = 0; i < _bind.Length; i++)
            {
                Vector3 v = _deformed ? _live[i] : (Vector3)_bind[i];
                float nx = (v.x * ppu + pivot.x) / rect.width;
                float ny = (v.y * ppu + pivot.y) / rect.height;
                vh.AddVert(new Vector3(box.xMin + nx * box.width, box.yMin + ny * box.height, 0f),
                           tint, _uv[i]);
            }

            for (int t = 0; t + 2 < _tris.Length; t += 3)
                vh.AddTriangle(_tris[t], _tris[t + 1], _tris[t + 2]);
        }

        /// <summary>
        /// Where a frame's hole lands inside a box of this size — the rectangle a name is laid
        /// out in, centred on the box, in the box's own units.
        /// </summary>
        public static Rect HoleIn(FrameDefinition frame, Vector2 size) => RectIn(frame.Hole, size);

        /// <summary>Where a frame's plate box lands inside a box of this size (<see cref="FrameDefinition.Plate"/>).</summary>
        public static Rect PlateIn(FrameDefinition frame, Vector2 size) => RectIn(frame.Plate, size);

        /// <summary>A painting-fraction rectangle inside a box of this size, centred on the box.</summary>
        public static Rect RectIn(Rect fraction, Vector2 size)
        {
            return new Rect((fraction.x - .5f) * size.x, (fraction.y - .5f) * size.y,
                            fraction.width * size.x, fraction.height * size.y);
        }
    }
}
