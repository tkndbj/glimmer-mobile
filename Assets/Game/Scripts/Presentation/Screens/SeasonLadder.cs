using System;
using System.Collections.Generic;
using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using GlimmerGrove.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The season's forty rungs, two chests each, as a scrolling list that keeps only the
    /// rows it can see.
    ///
    /// <para>
    /// <b>Its own type because it recycles, and it recycles because forty rungs is eighty
    /// chests.</b> Built whole, the ladder is around five hundred images standing on a
    /// phone for the life of the screen — bounded by <see cref="EventRules.MaxMilestones"/>
    /// rather than unbounded, so not quite the grid invariant 16d is about, but the same
    /// arithmetic with the same answer. Nine rows fit a tall phone, so eleven are built and
    /// rebound as they scroll.
    /// </para>
    /// <para>
    /// <b>Bind writes everything, and it never animates.</b> A recycled row is a different
    /// rung a frame later, so an entrance played on bind would fire every time the list
    /// moved — <c>CRAFT.md</c>'s rule that <c>Show</c> animates and <c>Refresh</c> does not,
    /// arriving where it is easiest to get wrong. The one thing that does move is the
    /// breathing on a chest that can be opened, and it is started and killed by the bind
    /// that changed the state rather than on every pass.
    /// </para>
    /// <para>
    /// Nothing here claims anything. A tap calls back to <see cref="EventScreen"/>, which
    /// owns the ceremony and the repaint, because a row that opened its own chest would be
    /// a second place the panel is raised.
    /// </para>
    /// </summary>
    public sealed class SeasonLadder
    {
        // ------------------------------------------------------------------ geometry
        /// <summary>
        /// A row is as short as its chests allow, because forty rungs is a long list and how
        /// many fit on a phone at once is the difference between reading a ladder and
        /// scrolling one. The chest is what sets the floor: drawn smaller than this it stops
        /// being a picture of treasure and becomes an inventory icon (invariant 45g's whole
        /// argument, on a row rather than an arch).
        ///
        /// <para>
        /// <b>The floor moved up, on the owner's verdict after playing it.</b> A rung pays a
        /// chest and the chest was the smallest thing on the card that named it; 164 with a 97
        /// chest in it read as a table of contents. The row is a scrolling list, so the whole
        /// cost of the change is how many rungs a phone shows at once — one fewer — against a
        /// prize that is now a picture. See <see cref="ChestTall"/> for the other half.
        /// </para>
        /// </summary>
        public const float RowHeight = 192f;
        public const float RowGap = 14f;
        public const float Pitch = RowHeight + RowGap;

        /// <summary>Where the two chest columns stand, from the middle of a row.</summary>
        public const float FreeX = 20f;
        public const float PassX = 310f;

        /// <summary>The hairline between the two columns, so the card reads as two halves.</summary>
        public const float SplitX = (FreeX + PassX) * .5f;

        /// <summary>The rung disc and the goal, at the left end.</summary>
        const float DiscX = -404f;
        const float GoalX = -330f;

        /// <summary>
        /// How tall the chest is <em>drawn</em>, and the sprite box and offset that produces.
        ///
        /// <para>
        /// <b>This row typed its own box and was the last screen in the game still doing it.</b>
        /// A closed chest is frame nought of its opening reel, so the 176x244 sprite carries the
        /// lid's headroom: a box set straight to 110x152 draws a chest 97 tall — two thirds of
        /// what the number says — and hangs it <b>24 units below</b> the middle of the cell it
        /// claims to be centred in, which is what "the chests are not centred in their cards"
        /// was. The hub, the tasks page and the streak board all convert through
        /// <see cref="ChestPack"/>; this one did not, and nothing could see it because
        /// <c>render_season.py</c> crops the icon to its own alpha before drawing it and so drew
        /// the picture the screen was supposed to be drawing (invariant 48j, the same trap from
        /// the other end).
        /// </para>
        /// <para>
        /// So the height here is a <em>drawn</em> height, the sprite's box is derived from it and
        /// the sprite is hung <see cref="ChestPack.Lift"/> of that height higher, which puts the
        /// drawn chest on the cell's own middle. The box overhangs the card at the top by the
        /// headroom it carries; nothing is clipped, because that part of the sprite is empty.
        /// </para>
        /// </summary>
        const float ChestTall = 122f;

        static readonly Vector2 ChestBox =
            new Vector2(ChestTall / ChestPack.Fill * ChestPack.Aspect, ChestTall / ChestPack.Fill);

        static readonly Vector2 ChestAt = new Vector2(0f, ChestTall * ChestPack.Lift);

        /// <summary>
        /// The seal and the padlock, on the lower right of the <em>drawn</em> chest rather than
        /// of its sprite — measured off <see cref="ChestTall"/> so a retune takes them with it,
        /// which is what a hand-typed pair of offsets stopped doing the moment the chest grew.
        /// </summary>
        static readonly Vector2 StampAt = new Vector2(ChestTall * .46f, -ChestTall * .42f);

        static readonly Vector2 Centre = new Vector2(.5f, .5f);
        static readonly Vector2 Left = new Vector2(0f, .5f);
        static readonly Vector2 Top = new Vector2(.5f, 1f);

        /// <summary>What a cell is drawing, so a bind only touches what changed.</summary>
        enum Face { Blank, Locked, Ready, Claimed, Sealed }

        /// <summary>One track's chest inside one row.</summary>
        sealed class Cell
        {
            /// <summary>
            /// The cell's own box, centred on the drawn chest. What anything <em>outside</em>
            /// this type is handed (<see cref="ChestOf"/>), because the chest's own transform is
            /// the sprite's box and that sits a fifth of a chest higher than the picture in it.
            /// </summary>
            public RectTransform Host;

            public Image Chest;
            public Image Halo;
            public RectTransform Seal;
            public Image Lock;
            public Btn Tap;
            public Face Painted;
        }

        /// <summary>One rung's card. Reused; <see cref="Index"/> says which rung it is drawing.</summary>
        sealed class Row
        {
            public int Index = -1;
            public RectTransform Root;
            public Image Pool;
            public Image Rim;
            public Text Number;
            public Text Goal;
            public Image Spine;
            public Cell Free;
            public Cell Pass;
            public bool Lit;
        }

        readonly List<Row> _rows = new List<Row>();
        readonly Action<int, SeasonTrack> _onTap;

        GroveEvent _season;
        EventProgress _progress;
        bool _owned;

        RectTransform _band, _list, _lights;
        float _visible, _width;
        int _first = -1;

        public SeasonLadder(Action<int, SeasonTrack> onTap)
        {
            _onTap = onTap;
        }

        // --------------------------------------------------------------------- build
        /// <summary>
        /// Builds the scrolling band between <paramref name="top"/> and
        /// <paramref name="bottom"/>, both measured from the safe area's own edges.
        /// </summary>
        public void Build(RectTransform host, float width, float top, float bottom,
                          GroveEvent season)
        {
            _season = season;
            _width = width;

            _band = UIKit.Node("Ladder", host);
            UIKit.StretchTo(_band, 0f, bottom, 0f, top);
            _band.gameObject.AddComponent<RectMask2D>();

            _list = UIKit.Node("List", _band);
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(.5f, 1f);

            // Every lit row's pool lives here, built before any card so all of them are under
            // every card. A light hung off a card would have to be either a child — which
            // draws over the plate it is meant to light — or a sibling inserted beside it,
            // which draws over the row above. The tasks page's rule, kept.
            _lights = UIKit.Node("Lights", _list);
            _lights.anchorMin = new Vector2(0f, 1f);
            _lights.anchorMax = new Vector2(1f, 1f);
            _lights.pivot = new Vector2(.5f, 1f);
            UIKit.StretchTo(_lights, 0f, 0f, 0f, 0f);

            int rungs = season?.Milestones.Count ?? 0;
            _list.sizeDelta = new Vector2(0f, 8f + rungs * Pitch);

            var catcher = _band.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var scroll = _band.gameObject.AddComponent<ScrollRect>();
            scroll.content = _list;
            scroll.viewport = _band;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;

            // Measured after a layout pass rather than assumed, so a short phone builds fewer
            // rows and a tablet builds more instead of either guessing.
            Canvas.ForceUpdateCanvases();
            _visible = _band.rect.height;

            int pool = Mathf.Min(rungs, Mathf.CeilToInt(_visible / Pitch) + 2);
            for (int i = 0; i < pool; i++) _rows.Add(BuildRow());
        }

        Row BuildRow()
        {
            var row = new Row();

            row.Pool = UIKit.Img("Light", _lights, Art.Glow(128, 1.35f), Pal.A(Pal.Sun, 0f),
                                 new Vector2(_width + 150f, RowHeight + 130f), Top, Vector2.zero);

            // `Skins.PlateNavy` — the profile's own blue box, darker. One reward row means one
            // thing across this game, so the season's ladder, the streak's board and the tasks
            // page draw the same card (invariant 44: the way to restyle every screen at once is
            // to re-cut what a name points at). See `Skins.PlateNavy`.
            var card = UIKit.Img("Rung", _list, Art.S("Ui/" + Skins.PlateNavy), Color.white,
                                 new Vector2(_width, RowHeight), Top, Vector2.zero);
            row.Root = (RectTransform)card.transform;

            // The rung's number, on the kit's own inset seat.
            var seat = UIKit.Img("Seat", row.Root, Art.S("Ui/" + Skins.Slot), Color.white,
                                 new Vector2(104f, 104f), Left, new Vector2(_width * .5f + DiscX, 0f));
            row.Number = UIKit.Shrinkable(
                UIKit.Titled("N", seat.transform, string.Empty, 40, Pal.Cream, TextAnchor.MiddleCenter,
                             new Vector2(88f, 56f), Centre, new Vector2(0f, 2f), 3f, 3f), 22);

            row.Goal = UIKit.Shrinkable(
                UIKit.Titled("Goal", row.Root, string.Empty, 24, Pal.A(Pal.Cream, .82f),
                             TextAnchor.MiddleLeft, new Vector2(170f, 34f), Left,
                             new Vector2(_width * .5f + GoalX + 85f, 0f), 3f, 3f), 14);

            // The rule between the two columns. A rung is one goal paying two chests, and with
            // nothing separating them the card reads as two unrelated pictures sitting on it;
            // with a line *joining* them — which is what this was first — it reads as one
            // reward drawn twice. A divider is the arrangement the headings above already
            // promise, so it is the one that needs no explaining.
            row.Spine = UIKit.Img("Split", row.Root, Art.Pixel, new Color(1f, 1f, 1f, .14f),
                                  new Vector2(3f, RowHeight - 44f), Centre,
                                  new Vector2(SplitX, 0f));

            row.Free = BuildCell(row, FreeX, SeasonTrack.Free);
            row.Pass = BuildCell(row, PassX, SeasonTrack.Pass);

            // The rim, on the card's own edge and over everything on it — the half of the lit
            // state that has to be a child, because a light behind a plate the kit cuts opaque
            // is a light with a hole in the middle of it.
            row.Rim = UIKit.Img("Rim", row.Root, Art.RoundOutline(30, 7f), Pal.A(Pal.Sun, 0f));
            UIKit.StretchTo((RectTransform)row.Rim.transform, 0f, 0f, 0f, 0f);

            return row;
        }

        Cell BuildCell(Row row, float x, SeasonTrack track)
        {
            var cell = new Cell();
            var host = UIKit.Box("C_" + SeasonTracks.Id(track), row.Root, new Vector2(240f, RowHeight - 12f),
                                 Centre, new Vector2(x, 0f));
            cell.Host = host;

            // On the drawn chest's own middle — which is this host's middle, because the sprite
            // is the thing that moves (see ChestTall). A halo centred on the sprite's box would
            // light the empty air the lid opens into.
            cell.Halo = UIKit.Img("Halo", host, Art.Glow(128, 2f), Pal.A(Pal.Gold, 0f),
                                  new Vector2(ChestTall * 2f, ChestTall * 2f), Centre, Vector2.zero);

            cell.Chest = UIKit.Img("Chest", host, null, Color.white, ChestBox, Centre, ChestAt);
            cell.Chest.preserveAspect = true;

            var seal = UIKit.Img("Seal", host, Art.Disc(96), Pal.Mint,
                                 new Vector2(64f, 64f), Centre, StampAt);
            var tick = UIKit.Img("Tick", seal.transform, Art.S("Ui/ic_check"), Color.white,
                                 new Vector2(38f, 38f), Centre, Vector2.zero);
            tick.preserveAspect = true;
            cell.Seal = (RectTransform)seal.transform;
            cell.Seal.gameObject.SetActive(false);

            cell.Lock = UIKit.Img("Lock", host, Art.S("Ui/ic_lock"), Pal.A(Pal.Cream, .92f),
                                  new Vector2(52f, 52f), Centre, StampAt);
            cell.Lock.preserveAspect = true;
            cell.Lock.gameObject.SetActive(false);

            // The whole cell is the target rather than the drawn chest, so the two columns have
            // the same reach whatever a tier's picture happens to be; and it stays live in every
            // state, because a locked pass cell is how somebody finds the pass.
            cell.Tap = UIKit.Button("Tap", host, Art.Pixel, new Vector2(234f, RowHeight - 16f),
                                    Centre, Vector2.zero, () => Tap(row, track));
            cell.Tap.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);
            cell.Tap.ClickSfx = null;
            cell.Tap.PressScale = .94f;

            return cell;
        }

        void Tap(Row row, SeasonTrack track)
        {
            if (row.Index < 0) return;
            _onTap?.Invoke(row.Index, track);
        }

        // -------------------------------------------------------------------- reading
        /// <summary>
        /// Which rung a row is drawing, so the caller can find a card to burst on.
        ///
        /// <b>The cell rather than the chest image</b>, because the image is the sprite's box
        /// and that is hung a fifth of a chest above the picture inside it — sparks thrown from
        /// it come out of the air over the lid rather than off the chest.
        /// </summary>
        public RectTransform ChestOf(int index, SeasonTrack track)
        {
            foreach (var row in _rows)
            {
                if (row.Index != index) continue;
                var cell = track == SeasonTrack.Pass ? row.Pass : row.Free;
                return cell.Host ? cell.Host : null;
            }

            return null;
        }

        /// <summary>
        /// Puts the rung the player is working on at the top of the viewport.
        ///
        /// The first <em>unclaimed</em> rung rather than the first unreached one, so a player
        /// coming back to three waiting chests opens on them instead of on the empty ground
        /// above. Called once, from Build; scrolling afterwards is the player's.
        /// </summary>
        public void FocusOnNext(EventProgress progress, bool owned)
        {
            if (_season == null || _list == null) return;

            int focus = _season.Milestones.Count - 1;

            for (int i = 0; i < _season.Milestones.Count; i++)
            {
                var rung = _season.Milestones[i];
                bool freeOpen = rung.Pays(SeasonTrack.Free) &&
                                !SeasonLedger.IsClaimed(_season, rung, SeasonTrack.Free);
                bool passOpen = owned && rung.Pays(SeasonTrack.Pass) &&
                                !SeasonLedger.IsClaimed(_season, rung, SeasonTrack.Pass);

                if (!freeOpen && !passOpen) continue;
                focus = i;
                break;
            }

            float top = Mathf.Clamp(focus * Pitch - Pitch * .5f,
                                    0f, Mathf.Max(0f, _list.rect.height - _visible));
            _list.anchoredPosition = new Vector2(0f, top);
        }

        // ------------------------------------------------------------------ painting
        /// <summary>
        /// Writes the ledger onto whichever rows are on screen. Called on every scroll frame
        /// and on every change, so it does as little as it can: a row already bound to the
        /// right rung in the right state is left alone.
        /// </summary>
        public void Repaint(EventProgress progress, bool owned, bool force = false)
        {
            if (_season == null || _list == null) return;

            _progress = progress;
            _owned = owned;

            int rungs = _season.Milestones.Count;
            float scrolled = _list.anchoredPosition.y;
            int first = Mathf.Clamp(Mathf.FloorToInt(scrolled / Pitch), 0, Mathf.Max(0, rungs - _rows.Count));

            bool moved = force || first != _first;
            _first = first;

            for (int i = 0; i < _rows.Count; i++)
            {
                int index = first + i;
                var row = _rows[i];

                if (index >= rungs)
                {
                    if (row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(false);
                    if (row.Pool) row.Pool.gameObject.SetActive(false);
                    row.Index = -1;
                    continue;
                }

                if (!row.Root.gameObject.activeSelf) row.Root.gameObject.SetActive(true);
                if (row.Pool && !row.Pool.gameObject.activeSelf) row.Pool.gameObject.SetActive(true);

                float y = -(8f + index * Pitch + RowHeight * .5f);
                row.Root.anchoredPosition = new Vector2(0f, y);
                if (row.Pool) ((RectTransform)row.Pool.transform).anchoredPosition = new Vector2(0f, y);

                bool rebound = moved || row.Index != index;
                row.Index = index;
                Paint(row, _season.Milestones[index], index, rebound);
            }
        }

        void Paint(Row row, EventMilestone rung, int index, bool rebound)
        {
            bool reached = _progress.Marks >= rung.Goal;

            if (rebound)
            {
                row.Number.text = (index + 1).ToString();
                row.Goal.text = Loc.Format("ui.mark.rung_goal", rung.Goal);
            }

            row.Goal.color = reached ? Pal.A(Pal.Gold, .95f) : Pal.A(Pal.Cream, .62f);
            row.Number.color = reached ? Pal.Cream : Pal.A(Pal.Cream, .55f);
            if (row.Spine) row.Spine.color = new Color(1f, 1f, 1f, reached ? .16f : .07f);

            bool lit = Paint(row.Free, rung, SeasonTrack.Free, reached, rebound)
                     | Paint(row.Pass, rung, SeasonTrack.Pass, reached, rebound);

            // **On the state changing and never on the rebind**, which is this type's own
            // rule (see the class note) and was broken here. `rebound` is true for *every*
            // visible row each time the list crosses a rung, so scrolling re-entered `Shine`
            // eleven times a pitch: each call kills the loop and starts it again at its
            // dimmest, so every lit card on the screen dipped and rose together — read as a
            // flicker, and the faster the scroll the more of them. A light that is already
            // breathing on a row that is still lit wants nothing done to it, whichever rung
            // the row is now showing.
            if (lit != row.Lit) Shine(row, lit);
            row.Lit = lit;
        }

        /// <summary>One cell. Returns whether it is waiting to be opened.</summary>
        bool Paint(Cell cell, EventMilestone rung, SeasonTrack track, bool reached, bool rebound)
        {
            var tier = rung.TierOn(track);
            bool pays = rung.Pays(track);
            bool walled = track == SeasonTrack.Pass && !_owned;
            bool claimed = pays && SeasonLedger.IsClaimed(_season, rung, track);
            bool ready = pays && reached && !claimed && !walled && tier != null;

            var face = !pays || tier == null ? Face.Blank
                     : claimed ? Face.Sealed
                     : walled ? Face.Locked
                     : ready ? Face.Ready
                     : Face.Claimed;      // reached-but-not-yet, or simply not reached: drawn plain

            // The picture is the tier's, and it only ever changes when the rung does — a
            // sprite assignment per scroll frame is a material rebind the list does not need.
            if (rebound) cell.Chest.sprite = tier == null ? null : Art.S(tier.Icon);

            if (face == cell.Painted && !rebound) return ready;

            bool wasReady = cell.Painted == Face.Ready;
            cell.Painted = face;

            cell.Chest.enabled = face != Face.Blank;
            cell.Seal.gameObject.SetActive(face == Face.Sealed);
            cell.Lock.gameObject.SetActive(face == Face.Locked);
            cell.Tap.gameObject.SetActive(face != Face.Blank);

            cell.Chest.color = face == Face.Ready ? Color.white
                             : face == Face.Sealed ? new Color(.74f, .78f, .84f, 1f)
                             : new Color(.62f, .66f, .74f, .88f);

            if (face == Face.Ready)
            {
                // Both on the *change* into Ready, for the reason above: a rebind that found
                // the cell already ready restarted the breathe from scale one and started a
                // second tint toward a colour the halo was already wearing — a jump on every
                // chest that could be opened, once per rung scrolled.
                if (!wasReady)
                {
                    Tween.Tint(cell.Halo, Pal.A(Pal.Gold, .55f), .35f);
                    Tween.KillChannel(cell.Chest.transform, "breathe");
                    Tween.Breathe(cell.Chest.transform, .07f, 1.5f);
                }
            }
            else
            {
                cell.Halo.color = Pal.A(Pal.Gold, 0f);
                Tween.KillChannel(cell.Chest.transform, "breathe");
                cell.Chest.transform.localScale = Vector3.one;
            }

            return ready;
        }

        /// <summary>
        /// The light a rung with something open stands in: a warm pool that reaches past the
        /// card and a bright rim on its edge, breathing together on one tween.
        ///
        /// It breathes rather than flashing, for the tasks page's reason and one sharper: a
        /// player who has not opened the game for a week can have a dozen of these on one
        /// list, and a dozen things flashing is 37h's rule broken twelve times over.
        /// </summary>
        static void Shine(Row row, bool on)
        {
            if (row.Pool) Tween.KillChannel(row.Pool.transform, "holy");

            if (!on)
            {
                if (row.Pool) row.Pool.color = Pal.A(Pal.Sun, 0f);
                if (row.Rim) row.Rim.color = Pal.A(Pal.Sun, 0f);
                return;
            }

            Tween.Run(1.8f, Ease.InOutSine, t =>
            {
                if (row.Pool) row.Pool.color = Pal.A(Pal.Sun, Mathf.Lerp(.48f, .88f, t));
                if (row.Rim) row.Rim.color = Pal.A(Pal.Radiance, Mathf.Lerp(.60f, 1f, t));
            }, row.Pool, "holy").Loop(-1, true);
        }
    }
}
