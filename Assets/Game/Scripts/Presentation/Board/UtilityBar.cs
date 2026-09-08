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
    /// </summary>
    public sealed class UtilityBar : MonoBehaviour
    {
        /// <summary>
        /// How tall the bar is. What a screen has to leave under its board.
        ///
        /// <para>
        /// <b>It fills the room under the board rather than sitting in it.</b> The first cut was a
        /// 148-point strip of loose squares floating at the foot of the screen with the board's
        /// old margin above it, and it read as three buttons somebody had left there rather than
        /// as a tray you keep things in. What a player is carrying is a permanent part of this
        /// mode's furniture, so it is drawn as furniture: a hazard-railed steel shelf across the
        /// whole width, meeting the board's own plate.
        /// </para>
        /// <para>
        /// Kept in step with <c>Tools/make_utility_art.py</c>'s <c>TRAY_H</c> and <c>CELL</c> by
        /// hand — the tray is one sprite at a fixed size, so the two numbers have to agree or the
        /// cells sit off the plate.
        /// </para>
        /// </summary>
        public const float Height = 290f;

        /// <summary>One cell, square, matching the tray sprite's own inset spacing.</summary>
        const float SlotSize = 224f;

        /// <summary>Where a cell's middle sits inside the tray, from its top.</summary>
        const float SlotTop = 46f;

        const float IconSize = 162f, BadgeSize = 64f, PriceSize = 46f;

        sealed class Slot
        {
            public UtilityItem Item;
            public Btn Button;
            public Image Face;
            public Image Ring;
            public Image Badge;
            public Text Count;
            public RectTransform Price;
            public Text Gems;
            public CanvasGroup Group;
        }

        readonly List<Slot> _slots = new List<Slot>(4);

        RectTransform _row;
        bool _live = true;

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

            var items = UtilityLedger.Catalog.Items;
            if (items.Count == 0) return;

            // The shelf itself, stretched over the whole bar. One sprite rather than a nine-slice
            // because nothing here writes a sprite border, and the rail's stripes have a period a
            // stretched middle would destroy — see the art tool.
            var plate = UIKit.Img("Tray", rt, Art.S("Ui/Utility/tray"), Color.white);
            plate.rectTransform.anchorMin = Vector2.zero;
            plate.rectTransform.anchorMax = Vector2.one;
            plate.rectTransform.offsetMin = Vector2.zero;
            plate.rectTransform.offsetMax = Vector2.zero;
            plate.raycastTarget = false;

            _row = rt;

            for (int i = 0; i < items.Count; i++) _slots.Add(BuildSlot(items[i], i, items.Count));

            // Detached first. Build is called once per screen today, but a subscription that
            // depends on that staying true is a subscription that silently doubles the day
            // somebody rebuilds a bar — and `Changed` fires on every chest opened anywhere.
            UtilityLedger.Changed -= Paint;
            UtilityLedger.Changed += Paint;

            Paint();
        }

        /// <summary>
        /// One cell, placed at its own even share of the width.
        ///
        /// <b>Spread rather than bunched.</b> Three cells centred on a full-width shelf leaves a
        /// third of it empty at each end, which reads as a tray built for more than it holds. At
        /// odd sixths they are as far from each other as from the ends, so a fourth utility
        /// re-spaces the row rather than making it look finished for the first time.
        /// </summary>
        Slot BuildSlot(UtilityItem item, int index, int count)
        {
            var slot = new Slot { Item = item };

            // Anchored to its share of the width so the row follows the safe area rather than
            // assuming the canvas's own 1080.
            float share = (2f * index + 1f) / (2f * count);

            slot.Button = UIKit.Button("Slot_" + item.Id, _row, Art.S("Ui/Utility/slot"),
                                       Vector2.one * SlotSize, new Vector2(share, 1f),
                                       new Vector2(0f, -(SlotTop + SlotSize * .5f)), () => Tap(slot));

            slot.Button.PressScale = .95f;
            slot.Group = UIKit.Group((RectTransform)slot.Button.transform);

            // Behind the cell, so an armed slot reads as lit from within rather than as an
            // outline drawn over a picture.
            slot.Ring = UIKit.Img("Ring", slot.Button.transform, Art.Ring(160, 10f), Pal.Sun,
                                  Vector2.one * (SlotSize + 18f));
            slot.Ring.transform.SetAsFirstSibling();
            slot.Ring.enabled = false;

            slot.Face = UIKit.Img("Face", slot.Button.transform, Art.S(item.Art), Color.white,
                                  Vector2.one * IconSize, new Vector2(.5f, .5f),
                                  new Vector2(0f, SlotSize * .06f));
            slot.Face.preserveAspect = true;

            slot.Badge = UIKit.Img("Badge", slot.Button.transform, Art.Disc(96), Pal.Ink,
                                   Vector2.one * BadgeSize, new Vector2(1f, 0f),
                                   new Vector2(-6f, 8f));

            slot.Count = UIKit.Label("Count", slot.Badge.transform, "0", 36, Pal.Cream,
                                     TextAnchor.MiddleCenter, Vector2.one * BadgeSize,
                                     new Vector2(.5f, .5f), Vector2.zero, FontStyle.Bold);

            // What an empty cell says instead of a count: a gem and a price, which is the whole
            // of the shop from here. A cell this size has room for the number, and a "+" alone
            // was a control that did not say what it would cost.
            slot.Price = UIKit.Box("Price", slot.Button.transform,
                                   new Vector2(SlotSize, PriceSize), new Vector2(.5f, 0f),
                                   new Vector2(0f, PriceSize * .58f));

            var gem = UIKit.Img("Gem", slot.Price, Art.S("Ui/ic_gem"), Pal.Sun,
                                Vector2.one * (PriceSize * .68f), new Vector2(.5f, .5f),
                                new Vector2(-PriceSize * .62f, 0f));
            gem.preserveAspect = true;

            slot.Gems = UIKit.Label("Cost", slot.Price, string.Empty, 34, Pal.Sun,
                                    TextAnchor.MiddleLeft, new Vector2(SlotSize * .5f, PriceSize),
                                    new Vector2(.5f, .5f), new Vector2(PriceSize * .18f, 0f),
                                    FontStyle.Bold);

            return slot;
        }

        // ------------------------------------------------------------------ input
        void Tap(Slot slot)
        {
            if (!_live || slot?.Item == null) return;

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
                int held = UtilityLedger.Held(slot.Item);
                bool armed = Armed == slot.Item;

                slot.Count.text = held.ToString();
                slot.Badge.enabled = held > 0;
                slot.Count.enabled = held > 0;

                bool selling = held <= 0 && slot.Item.ForSale;
                slot.Price.gameObject.SetActive(selling);
                if (selling) slot.Gems.text = slot.Item.GemPrice.ToString();

                slot.Ring.enabled = armed;
                slot.Face.color = held > 0 ? Color.white : new Color(1f, 1f, 1f, .36f);

                // A slot with nothing in it stays *interactable* while the bar is live, because
                // it is the shop. What dims is the picture, not the control — a disabled button
                // over the one route to buying more teaches the player the feature is broken,
                // which is `HomesteadBuyOverlay`'s argument for keeping a short balance live.
                slot.Group.alpha = _live ? 1f : .40f;
                slot.Button.Interactable = _live;
            }
        }

        /// <summary>
        /// What the armed utility is, as a sentence, for whatever is saying "now pick a target".
        /// Empty when nothing is armed.
        /// </summary>
        public string AimingNote
            => Armed == null ? string.Empty
             : Loc.Get(Armed.Target == UtilityTarget.Ward
                       ? "utility.aim.ward" : "utility.aim.hill");

        void OnDestroy() => UtilityLedger.Changed -= Paint;
    }
}
