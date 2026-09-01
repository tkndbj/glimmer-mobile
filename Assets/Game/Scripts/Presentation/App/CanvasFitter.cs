using GlimmerGrove.Layout;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Keeps the canvas scaler's reference width at the one <see cref="CanvasFit"/> says this
    /// display should be drawn at.
    ///
    /// <para>
    /// Added by <c>Boot.BuildCanvas</c>; there is no reason to add one by hand. It exists at all
    /// because the answer is a function of the screen and the screen changes while the app is
    /// running — a tablet is resized in split view, a foldable is opened, and Android reports a
    /// different size for a frame or two after a rotation. A width assigned once in
    /// <c>BuildCanvas</c> would be right on every device that never changes shape and silently
    /// wrong on the ones that do, which is <c>SafeAreaFitter</c>'s argument one layer down.
    /// </para>
    /// <para>
    /// The check is two integer comparisons a frame, and the assignment happens only when the
    /// display really moved — <c>CanvasScaler</c> marks the canvas dirty on every write to
    /// <c>referenceResolution</c>, so writing the same number each frame would rebuild the whole
    /// interface sixty times a second for nothing.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CanvasFitter : MonoBehaviour
    {
        CanvasScaler _scaler;
        int _width, _height;
        bool _ever;

        /// <summary>Hands over the scaler and applies at once, before anything is built on it.</summary>
        internal void Bind(CanvasScaler scaler)
        {
            _scaler = scaler;
            Apply();
        }

        void Awake()
        {
            if (_scaler == null) _scaler = GetComponent<CanvasScaler>();
            Apply();
        }

        void OnEnable() => Apply();

        void Update() => Apply();

        void Apply()
        {
            if (_scaler == null) return;

            int w = Screen.width, h = Screen.height;
            if (_ever && w == _width && h == _height) return;

            _width = w;
            _height = h;
            _ever = true;

            // Only the width is read: the scaler matches on width (`matchWidthOrHeight = 0`), so
            // the reference height is carried through untouched rather than being a second
            // number that could disagree with it.
            _scaler.referenceResolution = new Vector2(CanvasFit.WidthFor(w, h), Boot.RefHeight);
        }
    }
}
