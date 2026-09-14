using System;
using System.Collections.Generic;
using GlimmerGrove.Homestead;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove.App
{
    /// <summary>
    /// The half of placing something that the player can see: a ghost they drag, the tiles it
    /// would take lit under it, and the two or three buttons that finish the decision.
    ///
    /// <para>
    /// <b>It owns no state about the placement.</b> Where the piece is, which way it faces and
    /// whether it fits all live on <see cref="GroveDraft"/>; this asks on every frame it
    /// repaints. That split is the point of the rework — the same three facts used to be kept
    /// in a screen as loose fields beside the drawing, so "what is lit", "what will be written"
    /// and "is Confirm enabled" were three answers that had to be marched in step by hand.
    /// </para>
    /// <para>
    /// <b>Dragging never commits.</b> A drop is just where the ghost ended up; the write happens
    /// on Confirm and nowhere else. That makes a fumbled drag cost nothing, lets the player turn
    /// a piece as many times as they like before deciding, and means backing out of the screen
    /// mid-drag leaves the grove exactly as it was.
    /// </para>
    /// <para>
    /// <b>The ghost is the drag handle, and that is what keeps the floor still.</b> Unity routes
    /// a drag to the first ancestor of the pressed object that handles one, so a ghost that takes
    /// the drag is a ghost the field never sees — and the camera does not pan out from under the
    /// thing being moved. A bare drag on the floor goes on meaning "pan", which is what it has to
    /// mean on a screen this size.
    /// </para>
    /// </summary>
    public sealed class GroveDraftView
    {
        /// <summary>What a piece is drawn at while it is in the air.</summary>
        const float GhostAlpha = .82f;

        /// <summary>How far above the ghost's own tile the buttons float, before zoom.</summary>
        const float BarLift = 210f;

        const float BarHeight = 104f;

        /// <summary>How wide a key is when its caption does not need more.</summary>
        const float ButtonWidth = 132f;
        const float ButtonGap = 10f;
        const int ButtonFont = 28;

        /// <summary>
        /// The air a caption is given beyond what it measures, on top of the inset the kit
        /// already leaves. Two short words (TAKE AWAY) overflowed a key sized for one.
        /// </summary>
        const float ButtonAir = 16f;

        readonly RectTransform _parent;
        readonly GroveFieldView _field;
        readonly Func<HomesteadCatalog> _catalog;

        readonly GroveFootprintMarks _target;
        readonly GroveFootprintMarks _origin;

        Image _ghost;
        RectTransform _bar;
        Button _confirm;

        GroveDraft _draft;

        /// <summary>Raised when a draft is finished or abandoned, so the screen can repaint.</summary>
        public Action Settled;

        /// <summary>Raised with a refusal the player should be told about out loud.</summary>
        public Action<string> Refused;

        /// <summary>Whether something is being placed right now.</summary>
        public bool Active => _draft != null;

        /// <summary>The tile a live draft is hovering over, for a caller that needs to hide it.</summary>
        public GroveStand Standing => _draft?.Stand ?? default;

        /// <summary>The slot a lifted piece came from, so the screen can stop drawing it there.</summary>
        public string LiftedFrom => _draft != null && _draft.Source != GroveDraftSource.Stock
            ? _draft.FromSlot
            : string.Empty;

        public GroveDraftView(RectTransform parent, GroveFieldView field, Func<HomesteadCatalog> catalog)
        {
            _parent = parent;
            _field = field;
            _catalog = catalog;

            _origin = new GroveFootprintMarks(parent, field, "DraftOrigin");
            _target = new GroveFootprintMarks(parent, field, "DraftTarget");
        }

        // ------------------------------------------------------------------ opening
        /// <summary>Starts placing something chosen from the inventory, over the middle of the view.</summary>
        public void Begin(string pieceId)
        {
            var catalog = _catalog();
            if (catalog == null) return;

            // Over the tile the camera is looking at rather than over a corner: the player has
            // just come back from a panel, and a ghost that appeared off-screen would read as
            // the inventory having done nothing.
            _field.CentreTile(out int col, out int row);
            Open(GroveDraft.FromStock(catalog, pieceId, col, row));
        }

        /// <summary>Lifts whatever stands on a tile. Bare ground and the hall lift nothing.</summary>
        public void Lift(int col, int row) => Open(GroveDraft.FromFloor(_catalog(), col, row));

        void Open(GroveDraft draft)
        {
            if (draft == null) return;

            _draft = draft;
            EnsureParts();
            BuildBar();          // which buttons there are depends on where the draft came from
            Paint();

            _ghost.gameObject.SetActive(true);
            _bar.gameObject.SetActive(true);
            Tween.Pop(_bar, .6f, .22f);
            // **The sound of holding something, and it belongs here rather than in the two
            // callers.** `Begin` takes a piece out of the inventory and `Lift` takes one off a
            // tile, and what the player has done in both cases is the same: they are now
            // carrying a piece and the floor is showing them where it would go. Two call sites
            // would be two answers to one question. It was a `tick` - the driest and most
            // repeated thing in the set, the wheel-peg sound - which said nothing about
            // picking anything up. `stow` is its answer.
            Audio.SfxVaried("lift", .6f);

            Settled?.Invoke();      // the screen hides the original while the ghost is up
        }

        /// <summary>
        /// Abandons a draft without writing anything.
        ///
        /// Nothing was written when it was lifted (<see cref="GroveDraft"/>), so this is a
        /// drawing change only — which is why tapping the sky can do it, and why backing out of
        /// the screen is safe at any point in a drag.
        /// </summary>
        public void Close()
        {
            _draft?.Cancel();
            Dismiss();
        }

        /// <summary>
        /// Takes the ghost and its buttons off the screen, whether or not a draft is open.
        ///
        /// Separate from <see cref="Close"/> because a committed draft must <em>not</em> be
        /// cancelled on the way out — cancelling moves it back to where it was lifted from,
        /// which after a successful commit would be a lie the next repaint reads. Written as one
        /// method rather than repeated, because the first cut guarded on `_draft != null` and a
        /// commit cleared it first, so the ghost stayed on screen over the piece it had just
        /// become.
        /// </summary>
        void Dismiss()
        {
            _draft = null;

            if (_ghost) _ghost.gameObject.SetActive(false);
            if (_bar) _bar.gameObject.SetActive(false);
            _target.Hide();
            _origin.Hide();

            Settled?.Invoke();
        }

        // ------------------------------------------------------------------ dragging
        /// <summary>
        /// Moves the ghost to the tile under the finger, for as long as the finger is down.
        ///
        /// <para>
        /// It asks <see cref="GroveFieldView.GroundTileAt"/> rather than <c>TryTileAt</c>, and
        /// the difference is the whole of what two reported drag faults were: the tap's
        /// question prefers whatever is <em>drawn</em> over the point and refuses a point over
        /// nothing, so dragging across a piece snapped the ghost in front of it and dragging
        /// off the owned ground stopped the ghost dead. A finger that is already holding
        /// something is pointing at <em>floor</em>, so the floor is what it is asked about.
        /// </para>
        /// </summary>
        void OnDrag(PointerEventData e)
        {
            if (_draft == null) return;
            if (!_field.GroundTileAt(e.position, e.pressEventCamera, out int col, out int row)) return;
            if (!_draft.MoveTo(col, row)) return;

            Paint();
        }

        // ------------------------------------------------------------------ deciding
        void OnTurn()
        {
            if (_draft == null || !_draft.CanTurn) return;

            _draft.Turn();
            Paint();
            Audio.SfxVaried("tick", .45f);
        }

        void OnConfirm()
        {
            if (_draft == null) return;

            var result = _draft.Commit();

            // NoRoom should be unreachable — the button is not interactable when the footprint
            // is red — so it is said out loud rather than swallowed. A control that silently
            // does nothing is the one thing worse than a control that refuses.
            if (result == GrovePlaceResult.NoRoom)
            {
                Refused?.Invoke("ui.grove.no_room");
                return;
            }

            // **`lift` again rather than a `pop`, and the repeat is the point.** Sucker 02 is the
            // piece and the tile meeting — the same suction whether it is coming up off one or
            // going down onto one — so picking up and putting down are one gesture bookended by
            // one sound, and `stow` is left meaning the one thing that is not that: a piece
            // leaving the grove. The `pop` it replaces is the credits-token clip that eight other
            // things play, and it was the wrong news here for exactly `arrive`'s reason.
            //
            // **Louder than the pickup at .6, and that is not the same-clip-at-two-volumes fault
            // this file fixed a few lines up.** That one was two *adjacent* actions — opening a
            // draft and removing one — where the only difference was a couple of dB. These two
            // are seconds apart with a drag between them, and one is a ghost appearing while the
            // other is a piece landing on the floor; a landing is the bigger event, so it is the
            // louder reading of the same sound.
            if (result == GrovePlaceResult.Placed) Audio.SfxVaried("lift", .85f);

            Dismiss();          // committed, so there is nothing to cancel back to
        }

        void OnRemove()
        {
            if (_draft == null || _draft.Source != GroveDraftSource.Floor) return;

            _draft.Remove();
            Dismiss();

            // `lift`'s pair, from the same material. What it replaced was a `tick` at .7 against
            // the `tick` at .5 that opened the draft - the same clip a shade louder, which is not
            // a distinction anybody can hear, so taking a piece back sounded like picking it up.
            Audio.SfxVaried("stow", .6f);
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Redraws the ghost and its marks from the draft, if one is open.
        ///
        /// <para>
        /// For the screen to call from its own repaint, so the ghost hears about everything a
        /// tile hears about — above all its own art landing. A piece taken from the inventory
        /// was chosen off a thumbnail atlas and its full-size sprite is fetched only then
        /// (<c>HomesteadScreen.Take</c>), so the paint that opens the draft usually runs
        /// before the sprite exists and <see cref="HomesteadArt.Paint"/> hides the image.
        /// Nothing repainted it until the first drag, so the player saw a lit footprint with
        /// no piece in it and read the inventory as having done nothing.
        /// </para>
        /// </summary>
        public void Repaint()
        {
            if (_draft == null) return;
            Paint();
        }

        /// <summary>
        /// Redraws everything from the draft. Cheap enough to call on every frame of a drag: it
        /// moves four transforms and asks one predicate.
        /// </summary>
        void Paint()
        {
            if (_draft == null) return;

            var catalog = _catalog();
            var piece = _draft.Piece;
            var stand = _draft.Stand;
            bool fits = _draft.Fits;

            // The ghost, laid out exactly as the real thing would be — same size, same lift,
            // same facing — so what is dragged is what lands.
            var size = HomesteadArt.SizeOnFloor(piece, GroveTileArt.PieceScale) * _field.Zoom;
            var rt = (RectTransform)_ghost.transform;
            rt.sizeDelta = size;

            HomesteadArt.Paint(_ghost, piece, _draft.Facing);
            _ghost.color = new Color(_ghost.color.r, _ghost.color.g, _ghost.color.b,
                                     _ghost.color.a * GhostAlpha);

            var offset = GroveTileArt.Offset(piece, stand, size / Mathf.Max(_field.Zoom, .0001f));
            _ghost.transform.position = _field.TileWorld(stand.AnchorCol, stand.AnchorRow)
                                      + (Vector3)(offset * _field.Zoom);

            _target.Light(_draft.Col, _draft.Row, _draft.Footprint,
                          fits ? GroveFootprintMarks.Room : GroveFootprintMarks.NoRoom);

            // Where it came from, so a move shows what it is leaving. Hidden once the piece is
            // back on its own tiles, or the two lights would sit on top of each other.
            //
            // Every part of that is the draft's answer rather than one worked out here: the
            // footprint has to be the one it was *standing* at, because an odd quarter swaps its
            // axes and a turned piece lit its origin across the grain — see
            // GroveDraft.FromFootprint, which is the fault that bought the property.
            if (_draft.Source == GroveDraftSource.Floor && !_draft.Unmoved)
                _origin.Light(_draft.FromCol, _draft.FromRow, _draft.FromFootprint,
                              GroveFootprintMarks.Origin);
            else
                _origin.Hide();

            PlaceBar(fits);
        }

        /// <summary>
        /// Floats the buttons above the ghost and says whether Confirm is available.
        ///
        /// Above rather than over: all three act on the thing standing there, and a bar drawn
        /// across it would hide what the player is deciding about.
        /// </summary>
        void PlaceBar(bool fits)
        {
            _bar.position = _field.TileWorld(_draft.Stand.CentreCol, _draft.Stand.CentreRow)
                          + new Vector3(0f, BarLift * _field.Zoom, 0f);

            // Only Confirm goes dead, through the kit's own disabled tint. Turn and Remove
            // stay live on a piece that does not fit — turning is often what makes it fit, and
            // dimming the whole row would say the opposite.
            if (_confirm != null) _confirm.interactable = fits;
        }

        // ------------------------------------------------------------------ building
        void EnsureParts()
        {
            if (_ghost == null)
            {
                _ghost = UIKit.Img("DraftGhost", _parent, null, Color.white,
                                   new Vector2(140f, 140f), new Vector2(.5f, .5f), Vector2.zero);
                _ghost.raycastTarget = true;      // it is the drag handle; see the type's remarks
                _ghost.gameObject.AddComponent<DragRelay>().Dragged = OnDrag;
            }

            if (_bar != null) return;

            _bar = UIKit.Box("DraftBar", _parent, new Vector2(0f, BarHeight),
                             new Vector2(.5f, .5f), Vector2.zero);

            // Laid out from the middle, so a bar of one key, two or three is centred over the
            // piece rather than one of them being offset.
            _bar.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rebuilds the row of buttons for the draft that is open.
        ///
        /// <para>
        /// A lifted piece gets Remove and one from the inventory does not, because a piece that
        /// was never put down has nothing to take away — and a button that is present but dead
        /// is a button the player has to learn to ignore.
        /// </para>
        /// <para>
        /// <b>Turn is the same argument, and it was the row's one broken key.</b> It asks
        /// <see cref="GroveDraft.CanTurn"/> rather than naming residents, because "a companion
        /// is flat" is one instance of "this has one picture and a square footprint" and the
        /// roster holds eighteen more — a barrel, a boulder, a crate. Every one of them drew a
        /// TURN that did nothing at all when pressed, which is the control 16o refuses: a key
        /// that is live, animates and changes no pixel reads as the game having missed the tap.
        /// </para>
        /// </summary>
        public void BuildBar()
        {
            if (_draft == null || _bar == null) return;

            for (int i = _bar.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_bar.GetChild(i).gameObject);

            _keys.Clear();

            if (_draft.CanTurn)
                _keys.Add(Key("Turn", "btn_violet", "ui.grove.turn", OnTurn));

            if (_draft.Source == GroveDraftSource.Floor)
                _keys.Add(Key("Remove", "btn_red", "ui.grove.take_away", OnRemove));

            var confirm = Key("Confirm", "btn_green", "ui.grove.confirm", OnConfirm);
            _keys.Add(confirm);
            _confirm = confirm.GetComponent<Button>();

            Lay();
        }

        readonly List<Btn> _keys = new List<Btn>(3);

        /// <summary>
        /// One key of the bar, grown to hold its own caption.
        ///
        /// <para>
        /// <b>Measured rather than typed</b>, because the width that is wrong is a property of
        /// the string: TAKE AWAY overflowed a key cut for TURN, and a second constant for the
        /// wide one would be wrong again in the first language that disagrees with English
        /// about which word is longest. A caption that overflows is not clipped (invariant
        /// 37n), so the words simply print over the floor, which is what was reported.
        /// </para>
        /// <para>
        /// The inset is read back off the kit rather than copied: <c>TextButton</c> leaves the
        /// caption narrower than the key by a margin of its own, and this asks what that margin
        /// was instead of holding a second copy of it that can drift.
        /// </para>
        /// </summary>
        Btn Key(string name, string skin, string key, Action act)
        {
            var button = UIKit.TextButton(name, _bar, skin, Loc.Get(key), ButtonFont,
                                          new Vector2(ButtonWidth, BarHeight - 12f),
                                          new Vector2(.5f, .5f), Vector2.zero, act);

            var rect = (RectTransform)button.transform;
            if (button.Label == null) return button;

            float inset = rect.sizeDelta.x - button.LabelWidth;
            float want = Mathf.Max(ButtonWidth, button.Label.preferredWidth + inset + ButtonAir);
            if (want <= rect.sizeDelta.x) return button;

            rect.sizeDelta = new Vector2(want, rect.sizeDelta.y);

            var label = (RectTransform)button.Label.transform;
            label.sizeDelta = new Vector2(want - inset, label.sizeDelta.y);
            button.LabelWidth = want - inset;

            return button;
        }

        /// <summary>
        /// Spreads the keys along the bar, each centred in a slot of its own width.
        ///
        /// A second pass because the widths are not known until every caption has been
        /// measured, and the bar is centred over the ghost, so one key growing moves all of
        /// them rather than only the ones after it.
        /// </summary>
        void Lay()
        {
            if (_keys.Count == 0) return;

            float width = ButtonGap * (_keys.Count - 1);
            foreach (var key in _keys) width += ((RectTransform)key.transform).sizeDelta.x;

            _bar.sizeDelta = new Vector2(width, BarHeight);

            float x = -width * .5f;
            foreach (var key in _keys)
            {
                var rect = (RectTransform)key.transform;
                float wide = rect.sizeDelta.x;
                rect.anchoredPosition = new Vector2(x + wide * .5f, 0f);
                x += wide + ButtonGap;
            }
        }

        /// <summary>
        /// Turns a drag that begins on the ghost into a callback.
        ///
        /// Separate from the field's own drag handling on purpose — see the type's remarks about
        /// why the ghost has to take the drag rather than the floor.
        /// </summary>
        sealed class DragRelay : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Action<PointerEventData> Dragged;

            public void OnBeginDrag(PointerEventData e) => Dragged?.Invoke(e);
            public void OnDrag(PointerEventData e) => Dragged?.Invoke(e);
            public void OnEndDrag(PointerEventData e) => Dragged?.Invoke(e);
        }
    }
}
