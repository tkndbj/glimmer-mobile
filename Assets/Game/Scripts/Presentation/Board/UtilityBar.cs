using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The row of utilities across the foot of a board: what the player is holding, how many, and
    /// which one is armed.
    ///
    /// <para>
    /// <b>Written against <c>UtilityLedger</c> and not against any mode</b>, which is the point.
    /// Thornwatch is the only mode that offers utilities today; the bar knows nothing about
    /// wards, hills or matches, and a second mode gets it for the price of saying which of its
    /// cells a utility may be aimed at. That is <c>CellDrag</c>'s lesson taken before it cost
    /// anything: shared machinery living inside one mode's file is machinery that leaves with
    /// that mode (invariant 38).
    /// </para>
    /// <para>
    /// <b>Every slot in the catalog is drawn, including the empty ones.</b> Hiding what a player
    /// is not holding would make the bar change width between runs and — worse — would hide the
    /// only place the game ever says these things exist. An empty slot is the shop, which is the
    /// moment somebody has decided they want one: <c>HomesteadBuyOverlay</c>'s argument that a
    /// short balance keeps a live button, one step earlier.
    /// </para>
    /// <para>
    /// <b>Arming is a mode, and it is exclusive and reversible.</b> Tapping an armed slot
    /// disarms it, tapping another moves the arming, and the board is told either way — because
    /// a targeting state the player cannot leave is a run they have to lose to escape.
    /// </para>
    /// <para>
    /// <b>A cooling slot is drawn counting down and takes no tap at all, and that is the whole
    /// of the explanation.</b> A sweep over the cell with the seconds on it is the idiom every
    /// action bar in the genre uses, so it needs no sentence — and a button that visibly refuses
    /// is better than one that accepts a tap and does nothing, which is what a toast on top of
    /// an interactive cell would have been. It is <see cref="Cooling"/> that owns the clock;
    /// this only draws it and refuses on it (see <see cref="UtilityCooldown"/> for why nothing
    /// about it reaches the save file).
    /// </para>
    /// </summary>
    public sealed class UtilityBar : MonoBehaviour
    {
        /// <summary>
        /// How tall the bar is. What a screen has to leave under its board.
        ///
        /// <para>
        /// <b>It fills the room under the board rather than sitting in it.</b> The first cut was a
        /// strip of loose squares floating at the foot of the screen with the board's old margin
        /// above it, and it read as three buttons somebody had left there rather than as a tray
        /// you keep things in. What a player is carrying is a permanent part of this mode's
        /// furniture, so it is drawn as furniture: a dark shelf across the whole width, meeting
        /// the board's own plate.
        /// </para>
        /// <para>
        /// Kept in step with <c>Tools/make_utility_art.py</c>'s <c>TRAY_H</c>, <c>CELL</c> and
        /// <c>SLOTS</c> by hand — the shelf is one sprite at a fixed size, so the numbers have to
        /// agree or the cells sit off the plate.
        /// </para>
        /// </summary>
        public const float Height = 228f;

        /// <summary>
        /// How many cells the shelf holds, whatever the catalog currently fills.
        ///
        /// <para>
        /// <b>Five, and three of them have something in them today.</b> A bar sized to the catalog
        /// would move every slot under a player's thumb the day a fourth utility shipped — the
        /// muscle memory for "the mending is the middle one" is worth more than the empty cells
        /// cost. It is also honest: the two on the right are where the next two go.
        /// </para>
        /// <para>
        /// A catalog longer than this draws its first five. That is a content mistake rather than
        /// a state to design for — <c>ContentValidation</c> is where it is caught — and drawing
        /// what fits beats drawing off the end of the shelf.
        /// </para>
        /// </summary>
        public const int Slots = 5;

        /// <summary>One cell, square, matching the tray sprite's own inset spacing.</summary>
        const float SlotSize = 184f;

        const float IconSize = 136f, BadgeSize = 60f;

        sealed class Slot
        {
            public UtilityItem Item;
            public Btn Button;
            public Image Face;
            public Image Ring;
            public Image Badge;
            public Text Count;
            public CanvasGroup Group;

            /// <summary>The wedge over the cell, radial-filled from what is left to wait.</summary>
            public Image Sweep;

            /// <summary>The seconds, over the middle of the cell.</summary>
            public Text Clock;
        }

        readonly List<Slot> _slots = new List<Slot>(Slots);

        RectTransform _row;
        bool _live = true;

        /// <summary>
        /// What is cooling and for how much longer. One per bar, so one per run.
        ///
        /// <para>
        /// <b>Owned here rather than by a screen</b>, for the reason <see cref="Armed"/> is: it
        /// is state about this bar over this board, it is built with the bar and dies with it,
        /// and a second copy on a screen would be a second thing to keep in step with the cells
        /// that draw it. A mode advances it through <see cref="Tick"/> with the same seconds it
        /// gives its board.
        /// </para>
        /// </summary>
        public UtilityCooldown Cooling { get; } = new UtilityCooldown();

        /// <summary>Which utility is armed, or null. Set only through <see cref="Arm"/>.</summary>
        public UtilityItem Armed { get; private set; }

        /// <summary>Raised when a utility is armed, with the item — or null when disarmed.</summary>
        public Action<UtilityItem> Aiming { get; set; }

        /// <summary>Raised when an empty slot is tapped, so the screen can offer the shop.</summary>
        public Action<UtilityItem> Wanted { get; set; }

        /// <summary>
        /// Whether the bar takes input.
        ///
        /// The screen owns this and sets it from whatever it already uses to decide the board is
        /// playable, so the bar cannot come to disagree with the board about whether a run is
        /// under way — which is the second thing a screen would otherwise have to remember.
        /// </summary>
        public bool Live
        {
            get => _live;
            set
            {
                if (_live == value) return;
                _live = value;

                if (!_live) Arm(null);
                Paint();
            }
        }

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Builds the bar into a host, filling its bottom edge.
        ///
        /// Built once per screen rather than rebuilt on every change: <c>GridView</c>'s rule
        /// (invariant 16d) at the smallest possible scale — a repaint that destroys and rebuilds
        /// its cells replays their entrance, so a bar that flashed on every use would be a bar
        /// that flashed on every use.
        /// </summary>
        public void Build(RectTransform host)
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            _slots.Clear();

            var rt = (RectTransform)transform;
            rt.SetParent(host, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(.5f, 0f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = new Vector2(0f, Height);

            // The shelf itself, stretched over the whole bar. One sprite rather than a nine-slice
            // because nothing here writes a sprite border — see the art tool.
            var plate = UIKit.Img("Tray", rt, Art.S("Ui/Utility/tray"), Color.white);
            plate.rectTransform.anchorMin = Vector2.zero;
            plate.rectTransform.anchorMax = Vector2.one;
            plate.rectTransform.offsetMin = Vector2.zero;
            plate.rectTransform.offsetMax = Vector2.zero;
            plate.raycastTarget = false;

            _row = rt;

            var items = UtilityLedger.Catalog.Items;

            for (int i = 0; i < Slots; i++)
                _slots.Add(BuildSlot(i < items.Count ? items[i] : null, i));

            // Detached first. Build is called once per screen today, but a subscription that
            // depends on that staying true is a subscription that silently doubles the day
            // somebody rebuilds a bar — and `Changed` fires on every chest opened anywhere.
            UtilityLedger.Changed -= Paint;
            UtilityLedger.Changed += Paint;

            Paint();
        }

        /// <summary>
        /// One cell at its own even share of the width, holding a utility or nothing.
        ///
        /// A cell with no utility is drawn and never wired: it is a place, not a control, and a
        /// button that answers a tap with nothing is worse than a surface that does not.
        /// </summary>
        Slot BuildSlot(UtilityItem item, int index)
        {
            var slot = new Slot { Item = item };

            // Anchored to its share of the width so the row follows the safe area rather than
            // assuming the canvas's own reference width.
            float share = (2f * index + 1f) / (2f * Slots);

            slot.Button = UIKit.Button("Slot" + index, _row, Art.S("Ui/Utility/slot"),
                                       Vector2.one * SlotSize, new Vector2(share, .5f),
                                       Vector2.zero, () => Tap(slot));

            slot.Button.PressScale = item == null ? 1f : .95f;
            if (item == null) slot.Button.ClickSfx = null;

            slot.Group = UIKit.Group((RectTransform)slot.Button.transform);

            if (item == null) return slot;

            // Behind the cell, so an armed slot reads as lit from within rather than as an
            // outline drawn over a picture.
            slot.Ring = UIKit.Img("Ring", slot.Button.transform, Art.Ring(160, 10f), Pal.Sun,
                                  Vector2.one * (SlotSize + 18f));
            slot.Ring.transform.SetAsFirstSibling();
            slot.Ring.enabled = false;

            slot.Face = UIKit.Img("Face", slot.Button.transform, Art.S(item.Art), Color.white,
                                  Vector2.one * IconSize);
            slot.Face.preserveAspect = true;

            // **Top-right, over the cell's own rim.** At the foot it sat where a thumb rests and
            // where the icon is widest; the corner above is the one part of a cell nothing else
            // uses.
            slot.Badge = UIKit.Img("Badge", slot.Button.transform, Art.Disc(96), Pal.Ink,
                                   Vector2.one * BadgeSize, new Vector2(1f, 1f),
                                   new Vector2(-4f, 4f));

            // Shrinkable, because the ceiling is a hundred. At nine this was one glyph in a
            // 60-unit disc and a fixed 34 was right; three digits at 34 overflow a badge that
            // small, and a `UIKit.Label` that overflows is not clipped — it simply keeps
            // drawing, out over the cell beside it (the toast's lesson, on a badge).
            slot.Count = UIKit.Shrinkable(
                UIKit.Label("Count", slot.Badge.transform, "0", 34, Pal.Cream,
                            TextAnchor.MiddleCenter, Vector2.one * (BadgeSize - 8f),
                            new Vector2(.5f, .5f), Vector2.zero, FontStyle.Bold), 20);

            // **The cell's own sprite, tinted dark and radial-filled**, so the wedge is exactly
            // the shape of the well it covers rather than a square laid over a rounded one. It
            // is added after the face and before the badge, which is the order it has to read
            // in: the picture is under the sweep because that is what "not yet" means, and the
            // count stays over it because how many you hold is true either way.
            // **A pale veil rather than a dark one, and the first cut had it the wrong way
            // round.** Every action bar in the genre darkens the part still to wait, which works
            // because those bars are drawn on something lit. This one is not: the well is
            // deliberately the darkest thing on the shelf (invariant 39d), so ink over ink says
            // nothing at all — measured, the wedge was invisible on three of the four icons.
            // Light is what this cell has room for, which is invariant 37m's rule about a ward
            // arriving on a widget: a state that has *happened* reads as brighter, never dimmer.
            slot.Sweep = UIKit.Img("Sweep", slot.Button.transform, Art.S("Ui/Utility/slot"),
                                   Pal.A(Pal.Glass, .33f), Vector2.one * SlotSize);
            slot.Sweep.type = Image.Type.Filled;
            slot.Sweep.fillMethod = Image.FillMethod.Radial360;

            // From the top and clockwise, which is the direction every action bar in the genre
            // sweeps. `fillAmount` is what is *left*, so the wedge shrinks away rather than
            // growing — see `Sweep()`.
            slot.Sweep.fillOrigin = (int)Image.Origin360.Top;
            slot.Sweep.fillClockwise = true;
            slot.Sweep.enabled = false;
            slot.Sweep.transform.SetSiblingIndex(
                slot.Face.transform.GetSiblingIndex() + 1);

            // **Outlined, because it is the one label here drawn over two grounds.** The
            // middle of a cooling cell is pale on one side of the wedge and near-black on the
            // other, so cream alone reads on half of it — and which half moves as the seconds
            // run out.
            slot.Clock = UIKit.Shrinkable(
                UIKit.Titled("Clock", slot.Button.transform, string.Empty, 56, Pal.Cream,
                             TextAnchor.MiddleCenter, Vector2.one * (SlotSize - 24f),
                             new Vector2(.5f, .5f), Vector2.zero, outline: 3f, shadow: 3f), 28);
            slot.Clock.fontStyle = FontStyle.Bold;
            slot.Clock.enabled = false;

            return slot;
        }

        // ------------------------------------------------------------------ input
        void Tap(Slot slot)
        {
            if (!_live || slot?.Item == null) return;

            // **Cooling wins over empty**, so a cell counting down never opens the shop under
            // the player's thumb. It cannot be reached anyway — a cooling slot is drawn
            // uninteractable — and it is written down because the two states can overlap: the
            // use that started the cooldown may have been the last one held.
            if (!Cooling.Ready(slot.Item)) return;

            if (UtilityLedger.Held(slot.Item) <= 0)
            {
                // Empty: this is the shop, and the shop is the answer rather than a refusal.
                // Arming is dropped first, or a player would come back from the panel still
                // aiming something they had put down.
                Arm(null);
                Wanted?.Invoke(slot.Item);
                return;
            }

            Arm(Armed == slot.Item ? null : slot.Item);
        }

        /// <summary>
        /// Arms a utility, or disarms with null. The one door, so nothing can leave the bar and
        /// the board disagreeing about what is being aimed.
        /// </summary>
        public void Arm(UtilityItem item)
        {
            if (item != null && UtilityLedger.Held(item) <= 0) item = null;
            if (item != null && !Cooling.Ready(item)) item = null;
            if (Armed == item) return;

            Armed = item;
            Paint();
            Aiming?.Invoke(Armed);
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Redraws every slot from the ledger.
        ///
        /// <b>A redraw and never a rebuild</b> — invariant 16d's distinction: this is raised by
        /// an event, so building cells here would replay their entrance every time a chest was
        /// opened on another screen.
        /// </summary>
        public void Paint()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];

                // A cell holding nothing at all: a place on the shelf, dimmed and inert.
                if (slot.Item == null)
                {
                    slot.Group.alpha = _live ? .55f : .30f;
                    slot.Button.Interactable = false;
                    continue;
                }

                int held = UtilityLedger.Held(slot.Item);
                bool cooling = !Cooling.Ready(slot.Item);

                slot.Count.text = held.ToString();
                slot.Badge.enabled = held > 0;
                slot.Count.enabled = held > 0;

                slot.Ring.enabled = Armed == slot.Item;

                // **The picture stays bright while a cell is cooling, and that is what makes
                // the sweep visible at all.** The first cut faded it and let the wedge darken
                // the cell — which says nothing, because the well is deliberately the darkest
                // thing on the bar (invariant 39d) and there is nothing left to take away. A
                // veil needs something lit to veil, so what is dimmed is only what is *empty*
                // and the wedge is drawn over a picture at full strength. Caught by
                // `Tools/render_siege.py --cooling` and by nothing else.
                slot.Face.color = held > 0 ? Color.white : new Color(1f, 1f, 1f, .36f);

                Sweep(slot);

                // A slot with nothing in it stays *interactable* while the bar is live, because
                // it is the shop. What dims is the picture, not the control — a disabled button
                // over the one route to buying more teaches the player the feature is broken,
                // which is `HomesteadBuyOverlay`'s argument for keeping a short balance live.
                slot.Group.alpha = _live ? 1f : .40f;

                // **A cooling cell takes no tap at all**, which is the one place this bar hands
                // back a dead control on purpose. The rule everywhere else is that a slot stays
                // live because it is the shop; here the cell is visibly counting down, so a
                // press that answered with nothing would read as the bar being broken rather
                // than as the item not being ready.
                slot.Button.Interactable = _live && !cooling;
            }
        }

        /// <summary>Draws one cell's cooldown, or takes it off when there is none.</summary>
        void Sweep(Slot slot)
        {
            if (slot.Sweep == null || slot.Clock == null) return;

            float left = Cooling.Fraction(slot.Item);

            if (left <= 0f)
            {
                slot.Sweep.enabled = false;
                slot.Clock.enabled = false;
                return;
            }

            slot.Sweep.enabled = true;
            slot.Sweep.fillAmount = left;

            slot.Clock.enabled = true;
            slot.Clock.text = Cooling.Seconds(slot.Item).ToString();
        }

        // ------------------------------------------------------------------ the frame
        /// <summary>
        /// Gives every cooldown some seconds of the run.
        ///
        /// <para>
        /// <b>The seconds come from the mode rather than from a clock here</b>, and that is the
        /// whole reason this is a method and not an <c>Update</c>. A cooldown must burn on the
        /// run's own time: counted off a wall clock, a player could pay one off by opening the
        /// shop, since a panel over a board holds the run (<c>RunHold.Covered</c>) and stops
        /// everything else. Handing it the same seconds the board is advanced with makes that
        /// unrepresentable rather than merely unlikely.
        /// </para>
        /// <para>
        /// A cell is repainted in full only on the frame something became usable again — the
        /// edge rather than the poll, which is <c>SiegeView.Charge</c>'s rule about a ward's
        /// rank; every other frame moves two numbers on the cells that are counting.
        /// </para>
        /// </summary>
        public void Tick(float seconds)
        {
            if (!Cooling.Any) return;

            if (Cooling.Advance(seconds)) { Paint(); return; }

            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].Item != null) Sweep(_slots[i]);
        }

        /// <summary>
        /// Starts a utility's cooldown, because one was just spent on the board.
        ///
        /// <b>Called for a use that landed and never for one that was refused</b>, beside
        /// <c>UtilityLedger.TryUse</c> and for its reason: a firepot that reached nobody costs
        /// the player nothing, so it must not cost them the next ten seconds either.
        /// </summary>
        public void Spent(UtilityItem item)
        {
            if (item == null || !item.Cools) return;

            Cooling.Spend(item);

            // Whatever was armed is put down by the board's own `Done`, but the cell has to stop
            // being a control on this frame rather than the next: a bar painted a frame late is
            // one tap of a window in which the item can be used again for free.
            if (Armed == item) Arm(null);
            else Paint();
        }

        /// <summary>
        /// Forgets every cooldown, because this is a fresh run.
        ///
        /// A restart is a new board, so what was cooling on the one that was thrown away is not
        /// a debt the next one inherits.
        /// </summary>
        public void Cooled()
        {
            Cooling.Clear();
            Paint();
        }

        /// <summary>
        /// What the armed utility is, as a sentence, for whatever is saying "now pick a target".
        /// Empty when nothing is armed.
        /// </summary>
        public string AimingNote
            => Armed == null ? string.Empty
             : Loc.Get(Armed.Target == UtilityTarget.Ward
                       ? "utility.aim.ward" : "utility.aim.hill");

        /// <summary>Whether this bar is drawing this utility at all.</summary>
        public bool Shows(UtilityItem item)
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i].Item == item) return true;
            return false;
        }

        void OnDestroy() => UtilityLedger.Changed -= Paint;
    }
}
