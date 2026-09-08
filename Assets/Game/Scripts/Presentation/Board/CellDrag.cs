using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GlimmerGrove
{
    /// <summary>
    /// A drag on a cell, reported once as a direction the moment it has gone far enough.
    ///
    /// <para>
    /// Reports through the event system's own drag messages so the tap on the same cell is
    /// still a tap: a press that never travels past <see cref="Threshold"/> is left alone.
    /// </para>
    /// <para>
    /// <b>A file of its own because it belongs to no mode.</b> It was written inside
    /// <c>BudView</c> and used by Prismvale and Thornwatch as well, so deleting Budburst took
    /// the drag handler out from under two live modes and the compiler was the only thing that
    /// said so. A mode is meant to be removable for the price of its own files (invariant 20a),
    /// and shared machinery living inside one of them is exactly what stops that being true.
    /// </para>
    /// </summary>
    public sealed class CellDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Action<Vector2Int> Dragged;
        public Action Began, Ended;
        public float Threshold = 40f;

        Vector2 _from;
        bool _fired;

        public void OnBeginDrag(PointerEventData e)
        {
            _from = e.position;
            _fired = false;
            Began?.Invoke();
        }

        public void OnDrag(PointerEventData e)
        {
            if (_fired) return;

            var delta = e.position - _from;
            float scale = transform.lossyScale.x > .0001f ? transform.lossyScale.x : 1f;
            if (delta.magnitude < Threshold * scale) return;

            _fired = true;
            var dir = Mathf.Abs(delta.x) > Mathf.Abs(delta.y)
                    ? new Vector2Int(delta.x > 0f ? 1 : -1, 0)
                    : new Vector2Int(0, delta.y > 0f ? 1 : -1);
            Dragged?.Invoke(dir);
        }

        public void OnEndDrag(PointerEventData e)
        {
            _fired = false;
            Ended?.Invoke();
        }
    }
}
