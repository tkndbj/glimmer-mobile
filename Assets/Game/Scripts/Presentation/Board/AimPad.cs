using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A transparent surface that reports where a finger is, in its own local space, while the
    /// finger is down.
    ///
    /// <para>
    /// <b>Its own file for <c>CellDrag</c>'s reason</b> (invariant 38): shared input machinery
    /// living inside one mode's view is machinery that leaves with that mode, and this is the
    /// second thing here a second mode would want unchanged. <c>CellDrag</c> answers "which way
    /// did the finger go from this cell", which is a swap; this answers "where is the finger",
    /// which is aiming — different questions, and a pad that tried to be both would be a pad
    /// neither could rely on.
    /// </para>
    /// <para>
    /// <b>It fires on release rather than on touch, and that is what makes aiming safe.</b> A
    /// utility is a consumable that may have been bought with gems, so a target chosen by the
    /// frame a finger happens to land on is one a player cannot correct — where a drag-then-lift
    /// lets them move it, see the marker, and take it off the board entirely by lifting outside.
    /// It is the same bargain <c>ProtoView</c>'s modes strike with a swap: a move is free to
    /// discover and only expensive to keep (invariant 22b).
    /// </para>
    /// </summary>
    public sealed class AimPad : MonoBehaviour,
                                 IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        /// <summary>Where the finger is, in this rect's local space. Raised on touch and on drag.</summary>
        public Action<Vector2> Moved { get; set; }

        /// <summary>Where the finger left, in this rect's local space.</summary>
        public Action<Vector2> Released { get; set; }

        /// <summary>Raised when a press ends without a usable point — the player pulling out.</summary>
        public Action Cancelled { get; set; }

        RectTransform _rt;
        bool _down;

        void Awake()
        {
            _rt = (RectTransform)transform;

            // A pad with no graphic takes no raycasts at all, so it needs one — fully
            // transparent, which Unity still hit-tests. An `Image` with a null sprite would be a
            // white rectangle over the board (invariant 7b), so it is given the one-pixel sprite
            // every other invisible target here uses.
            if (GetComponent<Graphic>() == null)
            {
                var img = gameObject.AddComponent<Image>();
                img.sprite = Art.Pixel;
                img.color = new Color(0f, 0f, 0f, 0f);
            }

            GetComponent<Graphic>().raycastTarget = true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            _down = true;
            Report(e, Moved);
        }

        public void OnDrag(PointerEventData e)
        {
            if (!_down) return;
            Report(e, Moved);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (!_down) return;
            _down = false;

            if (!Report(e, Released)) Cancelled?.Invoke();
        }

        /// <summary>
        /// Turns a pointer event into a local point and hands it on.
        ///
        /// False when the point cannot be worked out — a pointer with no camera on a screen-space
        /// canvas, or an event arriving after the pad has been detached — and the caller reads
        /// that as "no target", which is the safe answer: nothing is spent.
        /// </summary>
        bool Report(PointerEventData e, Action<Vector2> to)
        {
            if (to == null || _rt == null) return false;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _rt, e.position, e.pressEventCamera, out var local)) return false;

            to(local);
            return true;
        }
    }
}
