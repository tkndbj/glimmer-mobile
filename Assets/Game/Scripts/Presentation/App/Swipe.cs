using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlimmerGrove
{
    /// <summary>
    /// A horizontal swipe over a region, reported once per drag as <c>-1</c> (the finger went
    /// left) or <c>+1</c> (it went right).
    ///
    /// <para>
    /// <b>The direction is the finger's, and the caller decides what it means.</b> A page of
    /// things laid left to right is turned by dragging the current one <em>away</em>, so a
    /// finger moving left is "show me the next"; that inversion belongs to the screen, because
    /// a dial or a slider would read the same gesture the other way round.
    /// </para>
    /// <para>
    /// <b>One report per drag, on crossing the threshold, never on release.</b> Firing on
    /// release makes a slow deliberate drag feel unanswered until the thumb lifts, and firing
    /// on every frame past the threshold turns one gesture into three. The threshold is scaled
    /// by the transform's own scale, as <see cref="CellDrag"/>'s is, so a widened tablet canvas
    /// asks for the same distance under the finger.
    /// </para>
    /// <para>
    /// A vertical drag is ignored rather than reported, so a region that also scrolls can hand
    /// the vertical half to a <c>ScrollRect</c> without the two fighting over one gesture.
    /// </para>
    /// </summary>
    public sealed class Swipe : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<int> Swiped;
        public float Threshold = 70f;

        Vector2 _from;
        bool _fired;

        public void OnBeginDrag(PointerEventData e)
        {
            _from = e.position;
            _fired = false;
        }

        public void OnDrag(PointerEventData e)
        {
            if (_fired) return;

            var delta = e.position - _from;
            float scale = transform.lossyScale.x > .0001f ? transform.lossyScale.x : 1f;
            if (Mathf.Abs(delta.x) < Threshold * scale) return;
            if (Mathf.Abs(delta.y) > Mathf.Abs(delta.x)) return;

            _fired = true;
            Swiped?.Invoke(delta.x > 0f ? 1 : -1);
        }

        public void OnEndDrag(PointerEventData e) { _fired = false; }
    }
}
