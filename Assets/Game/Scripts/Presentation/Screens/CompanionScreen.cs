using System;
using System.Collections.Generic;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The whole companion roster, shown properly.
    ///
    /// <para>
    /// A screen rather than a modal, because the roster is unbounded — thirty today and
    /// a hundred after a year of drops — and a panel that scrolls inside a scrim is a
    /// worse place to browse than a page that owns the display. It is reached from the
    /// profile and returns there.
    /// </para>
    /// <para>
    /// The grid is built once from the roster and its cells are laid out by index, so a
    /// content drop that adds ten companions changes nothing here but the number of
    /// rows. Portraits are stills: this is precisely the screen that would otherwise
    /// have decoded a hundred flipbooks to show a hundred faces.
    /// </para>
    /// </summary>
    public sealed class CompanionScreen : View, IDrawsCompanionArt
    {
        public override string Track => "mus_menu";

        const float HeaderHeight = 250f;
        const int Columns = 3;
        const float CellW = 320f;
        const float CellH = 380f;

        /// <summary>Corner radius of a cell's plate and of the edge drawn over it.</summary>
        const int CellRadius = 30;

        RectTransform _viewport, _grid;
        Text _summary;

        /// <summary>
        /// The parts of a built cell whose look depends on which companion is worn, kept so
        /// that changing the choice can restyle two cells instead of rebuilding the roster.
        ///
        /// <para>
        /// Nothing here is a second copy of the cell's state — it is a handful of references
        /// into the objects <see cref="Cell"/> already made. <see cref="StyleWorn"/> is the
        /// only thing that reads them, and it is also what <see cref="Cell"/> calls to paint a
        /// cell the first time, so there is exactly one description of what "worn" looks like.
        /// That is the property the old rebuild-everything approach was protecting, and it is
        /// kept here rather than traded away: the alternative that would have been a mistake
        /// is patching the ring in one place and the caption in another.
        /// </para>
        /// </summary>
        sealed class CellView
        {
            public string Id;
            public bool Unlocked;
            public Image Plate, Edge, Halo;
            public Text Sub;
        }

        readonly List<CellView> _cells = new List<CellView>();

        /// <summary>
        /// The companion the cells are currently drawn for. Held so a restyle knows which cell
        /// is losing the ring as well as which is gaining it, and so the flourish fires only
        /// when the choice actually moved rather than on every repaint that happens to run.
        /// </summary>
        string _worn;

        protected override void Build()
        {
            Scenery.Layered(Content, "home", .26f);
            Fireflies.Spawn(Content, 18, new Color(1f, .93f, .70f), 6f, 20f);

            BuildGrid();
            BuildHeader();
            NavBar.Build(Content, NavBar.Tab.Profile);

            // Arriving from the profile the scope is usually already warm and this
            // repaints immediately; arriving any other way it is the load that fills
            // the screen.
            CompanionArt.OpenAsync(() => { if (this) Paint(); });

            // Repainted on the ledger's own event rather than on a callback from whatever
            // opened the unlock panel. A callback has to be threaded through every exit that
            // panel has — the wear button, the corner cross, the scrim — and the two silent
            // ones are exactly how a companion the player just bought stayed behind a padlock
            // until the screen was left and re-entered. An event cannot be forgotten.
            Progression.CompanionLedger.Changed += Paint;

            // Which companion is worn is a second fact, moved a step after the held set on a
            // purchase — see Profile.AvatarChanged. Listening to only the ledger left the gold
            // ring on the previously worn companion after a buy. It restyles rather than
            // rebuilds: nothing about the roster changed, only which cell wears the ring.
            Profile.AvatarChanged += PaintWorn;

            // The roster itself can be republished by a content fetch landing mid-session,
            // which changes what this grid is a picture of.
            AvatarCatalog.Changed += Paint;
        }

        void OnDestroy()
        {
            Progression.CompanionLedger.Changed -= Paint;
            Profile.AvatarChanged -= PaintWorn;
            AvatarCatalog.Changed -= Paint;

            // The profile shows a preview row from the same set, so going back does not
            // free it only to load it again a frame later.
            if (Flow.Current is ProfileScreen) return;
            CompanionArt.CloseUnlessWanted();
        }

        // ----------------------------------------------------------------- grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Safe);
            _viewport.offsetMin = new Vector2(0f, NavBar.Height);
            _viewport.offsetMax = new Vector2(0f, -HeaderHeight);

            var catcher = _viewport.gameObject.AddComponent<Image>();
            catcher.color = new Color(0, 0, 0, 0);
            catcher.raycastTarget = true;
            _viewport.gameObject.AddComponent<RectMask2D>();

            _grid = UIKit.Node("Grid", _viewport);
            _grid.anchorMin = new Vector2(0f, 1f);
            _grid.anchorMax = new Vector2(1f, 1f);
            _grid.pivot = new Vector2(.5f, 1f);
            _grid.anchoredPosition = Vector2.zero;

            Paint();

            var scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = _grid;
            scroll.viewport = _viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = .14f;
            scroll.inertia = true;
            scroll.decelerationRate = .04f;
            scroll.scrollSensitivity = 55f;
        }

        /// <summary>
        /// Rebuilds the grid. The answer to a change in <em>what the roster is</em> — a
        /// companion becoming held, or a content fetch republishing the catalog.
        ///
        /// <para>
        /// It is deliberately no longer the answer to changing which companion is worn. That
        /// used to come through here too, and the argument for it — that thirty cells are
        /// cheaper than keeping a ring, a caption and a plate in step by hand — traded away
        /// two things it did not have to. Rebuilding replays the staggered entrance in
        /// <see cref="Cell"/>, so a small confirmation was answered with the animation that
        /// says "you have just arrived", and for half a second afterwards every cell was
        /// scaling under the player's finger. And the cost is the roster's size on every tap:
        /// eight or nine objects per cell, thirty companions today and, by this screen's own
        /// reckoning, a hundred after a year of drops.
        /// </para>
        /// <para>
        /// <see cref="PaintWorn"/> takes that case instead, and the drift the old comment
        /// feared is prevented by construction rather than by rebuilding — see
        /// <see cref="CellView"/>.
        /// </para>
        /// </summary>
        void Paint()
        {
            if (_grid == null) return;

            for (int i = _grid.childCount - 1; i >= 0; i--)
            {
                var old = _grid.GetChild(i).gameObject;
                old.SetActive(false);              // Destroy only lands at end of frame
                Destroy(old);
            }

            // Cleared with the cells they point at. A CellView outliving its objects would be
            // a restyle writing to a destroyed Image on the next wear.
            _cells.Clear();

            var roster = AvatarCatalog.All;
            int level = Profile.Rank;
            _worn = Profile.Avatar.Id;

            for (int i = 0; i < roster.Count; i++)
            {
                float x = (i % Columns - (Columns - 1) * .5f) * CellW;
                float y = -(i / Columns) * CellH - CellH * .5f - 12f;

                // The whole rule — reached by level or bought. Asking AvatarCatalog directly
                // here is what would draw a padlock over a companion the player paid for.
                Cell(roster[i], new Vector2(x, y), CompanionLedger.IsHeld(roster[i], level), i);
            }

            int rows = (roster.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * CellH + 40f);

            if (_summary) _summary.text = SummaryText(level);
        }

        /// <summary>
        /// Moves the ring to the companion now being worn, and takes it off the one that was.
        ///
        /// <para>
        /// Touches the two cells whose look actually changed and leaves the other twenty-nine
        /// alone — no destruction, no entrance animation, no scroll to re-find. The flourish
        /// that used to be the whole grid cascading is now a bump and a spark on the cell that
        /// was chosen, which is both the thing the player is looking at and the only thing the
        /// tap was about.
        /// </para>
        /// <para>
        /// Guarded on the choice having moved, because this also runs on the second of the two
        /// events a purchase raises (see <c>Profile.AvatarChanged</c>) and on a repaint that
        /// merely follows a rebuild — neither should throw sparks at a cell nothing happened to.
        /// </para>
        /// </summary>
        void PaintWorn()
        {
            string worn = Profile.Avatar.Id;
            bool moved = !string.Equals(worn, _worn, StringComparison.Ordinal);
            _worn = worn;

            for (int i = 0; i < _cells.Count; i++)
            {
                var view = _cells[i];

                // Paint clears the list with the cells it destroys, so this should not happen —
                // but a restyle writing into a destroyed Image would be a hard error, and the
                // cost of being sure is one comparison per cell.
                if (view.Plate == null) continue;

                bool isWorn = string.Equals(view.Id, worn, StringComparison.Ordinal);
                StyleWorn(view, isWorn);

                if (!moved || !isWorn) continue;

                // Squared up first. Punch remembers the scale it starts from and restores it
                // when it finishes, but a punch killed part-way through — which is what
                // starting a second one on the same plate does — never runs its restore, so the
                // next one would take a mid-bump scale as the size to settle back to. Reachable
                // by wearing A, then B, then A again inside a third of a second.
                view.Plate.transform.localScale = Vector3.one;

                Tween.Punch(view.Plate.transform, .13f, .36f);
                Burst.Sparks(view.Plate.transform, Vector2.zero, Pal.Gold, 12, 200f, 24f, .55f);
            }
        }

        /// <summary>
        /// Everything about a cell that depends on whether its companion is the one being worn,
        /// in one place. Called by <see cref="Cell"/> to paint it the first time and by
        /// <see cref="PaintWorn"/> to repaint it, so the two can never describe it differently.
        /// </summary>
        void StyleWorn(CellView view, bool worn)
        {
            if (view.Plate)
                view.Plate.color = worn ? Pal.A(Pal.Hex("#0C4A44"), .92f)
                                        : new Color(.03f, .10f, .13f, .78f);

            if (view.Edge)
            {
                view.Edge.sprite = Art.RoundOutline(CellRadius, worn ? 4f : 2f);
                view.Edge.color = worn ? Pal.A(Pal.Gold, .95f)
                                       : new Color(1f, 1f, 1f, view.Unlocked ? .14f : .07f);
            }

            // Made on first wear rather than up front, so the roster does not carry one hidden
            // Image per companion for a decoration at most one of them shows at a time. Halo
            // drops itself behind its siblings, which stays true whenever it is created.
            if (worn && view.Halo == null && view.Plate)
                view.Halo = UIKit.Halo(view.Plate.transform, Pal.Gold, 300f, .26f);

            if (view.Halo) view.Halo.enabled = worn;

            // A locked cell's caption is its price or its gate, which the choice cannot change,
            // so it is left where Cell put it rather than given a reading it cannot have.
            // Shrinkable fits on Unity's own best-fit pass, so a new caption re-fits itself.
            if (view.Sub && view.Unlocked)
            {
                view.Sub.text = worn ? Loc.Get("ui.profile.wearing")
                                     : Loc.Get("ui.profile.tap_to_wear");
                view.Sub.color = worn ? Pal.Gold : new Color(1f, .96f, .88f, .5f);
            }
        }

        void Cell(AvatarDefinition avatar, Vector2 at, bool unlocked, int index)
        {
            var cell = UIKit.Button("A_" + avatar.Id, _grid, Art.Pixel, new Vector2(CellW - 16f, CellH - 20f),
                                    new Vector2(.5f, 1f), at, () => Choose(avatar, unlocked));
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var plate = UIKit.Img("Plate", cell.transform, Art.Round(CellRadius), Color.white,
                                  new Vector2(CellW - 28f, CellH - 34f), new Vector2(.5f, .5f), Vector2.zero);
            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(CellRadius, 2f), Color.white);
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var face = UIKit.Img("Face", plate.transform, null,
                                 unlocked ? Color.white : new Color(.15f, .21f, .25f, .95f),
                                 new Vector2(196f, 196f), new Vector2(.5f, 1f), new Vector2(0f, -118f));
            face.preserveAspect = true;
            CompanionArt.Paint(face, avatar);
            Tween.Bob((RectTransform)face.transform, 5f, 2.8f + index % 5 * .17f);

            if (!unlocked)
            {
                var padlock = UIKit.Img("Lock", plate.transform, Art.S("Ui/padlock"), Color.white,
                                        new Vector2(72f, 72f), new Vector2(1f, 1f), new Vector2(-26f, -26f));
                padlock.preserveAspect = true;
            }

            UIKit.Titled("Name", plate.transform,
                         unlocked ? Loc.Get(avatar.NameKey) : Loc.Get("ui.profile.locked"),
                         30, unlocked ? Pal.Cream : new Color(1f, .95f, .88f, .55f),
                         TextAnchor.MiddleCenter, new Vector2(CellW - 60f, 40f), new Vector2(.5f, 0f),
                         new Vector2(0f, 76f), 3f, 3f);

            // A locked cell leads with whichever half is actually stopping the player, and
            // carries the other underneath. Both are required — the rule is keeper level AND
            // purchase — so a cell quoting only the price was telling somebody four levels
            // short that 8,000 coins would do it, and a cell quoting only the gate was hiding
            // the second thing they still have to find. Which one leads is therefore a
            // question about *this* player, not about the companion.
            //
            // An unlocked cell's caption is StyleWorn's to write, so it is built empty rather
            // than given a value here that would have to agree with the one over there.
            bool gated = !unlocked && !AvatarCatalog.ReachedBy(avatar, Profile.Rank);

            string lead = !avatar.IsForSale || gated
                ? Loc.Format("ui.profile.locked_at", avatar.UnlockLevel)
                : Loc.Format("ui.profile.cost", Compact.Number(avatar.UnlockCost));

            var sub = UIKit.Shrinkable(
                UIKit.Titled("Sub", plate.transform,
                             unlocked ? string.Empty : lead,
                             24, unlocked ? Color.white
                                          : gated ? Pal.A(Pal.Aqua, .95f) : Pal.A(Pal.Sun, .90f),
                             TextAnchor.MiddleCenter, new Vector2(CellW - 60f, 32f), new Vector2(.5f, 0f),
                             new Vector2(0f, 36f), 3f, 0f), 18);

            // The other half, quieter. Only on a priced cell whose gate is closed: once the
            // gate is open the price is the whole remaining story and a second line repeating
            // a level the player has already passed is noise.
            if (gated && avatar.IsForSale)
                UIKit.Titled("Gate", plate.transform,
                             Loc.Format("ui.profile.cost", Compact.Number(avatar.UnlockCost)), 20,
                             new Color(1f, .95f, .88f, .42f), TextAnchor.MiddleCenter,
                             new Vector2(CellW - 60f, 26f), new Vector2(.5f, 0f),
                             new Vector2(0f, 12f), 3f, 0f);

            var view = new CellView
            {
                Id = avatar.Id,
                Unlocked = unlocked,
                Plate = plate,
                Edge = edge,
                Sub = sub
            };

            // Registered even when locked. A locked cell can never be the worn one, but keeping
            // one list of every cell means PaintWorn does not have to know which kind it is
            // holding, and a cell unlocked by a purchase arrives through a rebuild anyway.
            _cells.Add(view);

            // The first and only place a cell's worn look is decided — the same call PaintWorn
            // makes later, so a cell built worn and a cell restyled worn cannot differ.
            StyleWorn(view, string.Equals(avatar.Id, _worn, StringComparison.Ordinal));

            cell.transform.localScale = Vector3.zero;
            Tween.Pop(cell.transform, 0f, .5f, .04f * Mathf.Min(index, 12));
        }

        /// <summary>
        /// Wears a held companion, or opens the panel that explains an unheld one.
        ///
        /// A locked cell used to answer with a toast naming its level gate. That was true and
        /// useless for most of this grid — the gates run past anything the shipped catalog can
        /// reach — so a tap now opens the panel, which gives both routes and a way to take one.
        /// </summary>
        void Choose(AvatarDefinition avatar, bool unlocked)
        {
            if (!unlocked)
            {
                Audio.Sfx("chime", .45f);
                Flow.Modal<CompanionUnlockOverlay>(v => v.Avatar = avatar);
                return;
            }

            // The repaint is Profile.AvatarChanged's, not this method's. Leaving it here as
            // well would paint the grid twice on the one path that already had a repaint, and
            // would leave the purchase path — which never comes through here — as the only
            // one relying on a call site to remember.
            if (!Profile.TryWearAvatar(avatar.Id)) return;

            Audio.Sfx("chime2", .5f);
        }

        // --------------------------------------------------------------- chrome
        void BuildHeader()
        {
            var fade = UIKit.Img("TopFade", Content, Art.FadeUp(64), new Color(.02f, .06f, .09f, .82f));
            var frt = (RectTransform)fade.transform;
            frt.anchorMin = new Vector2(0f, 1f); frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(.5f, 1f);
            frt.sizeDelta = new Vector2(0f, 300f);
            frt.anchoredPosition = Vector2.zero;
            frt.localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<ProfileScreen>());

            var banner = UIKit.Img("Banner", Safe, Art.S("Ui/banner"), Color.white,
                                   new Vector2(560f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -142f));
            UIKit.Titled("Title", banner.transform, Loc.Get("ui.profile.companions").ToUpperInvariant(), 40,
                         new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            _summary = UIKit.Titled("Summary", Safe, SummaryText(Profile.Rank), 26,
                                    new Color(1f, .96f, .88f, .70f), TextAnchor.MiddleCenter,
                                    new Vector2(900f, 34f), new Vector2(.5f, 1f), new Vector2(0f, -222f), 3f, 0f);
        }

        /// <summary>
        /// "12 of 31 awake", plus what is next. The second half is the point: a locked
        /// grid with no stated way forward reads as a paywall rather than a goal.
        ///
        /// The count is of companions <em>held</em>, by either route — a purchased one that
        /// did not count here would make the caption disagree with the grid under it.
        /// </summary>
        static string SummaryText(int level)
        {
            string held = Loc.Format("ui.profile.unlocked", CompanionLedger.HeldCount(level),
                                     AvatarCatalog.All.Count);

            var next = CompanionLedger.NextUnheld(level);
            return next.IsValid
                ? held + "  ·  " + Loc.Format("ui.profile.next_at", next.UnlockLevel)
                : held;
        }

        public override bool OnBack() { Flow.Go<ProfileScreen>(); return true; }
    }
}
