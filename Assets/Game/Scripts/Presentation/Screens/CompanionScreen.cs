using System;
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
    public sealed class CompanionScreen : View
    {
        public override string Track => "mus_menu";

        const float HeaderHeight = 250f;
        const int Columns = 3;
        const float CellW = 320f;
        const float CellH = 380f;

        RectTransform _viewport, _grid;
        Text _summary;

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
            // ring on the previously worn companion after a buy.
            Profile.AvatarChanged += Paint;

            // The roster itself can be republished by a content fetch landing mid-session,
            // which changes what this grid is a picture of.
            AvatarCatalog.Changed += Paint;
        }

        void OnDestroy()
        {
            Progression.CompanionLedger.Changed -= Paint;
            Profile.AvatarChanged -= Paint;
            AvatarCatalog.Changed -= Paint;

            // The profile shows a preview row from the same set, so going back does not
            // free it only to load it again a frame later.
            if (Flow.Current is ProfileScreen) return;
            CompanionArt.Close();
        }

        // ----------------------------------------------------------------- grid
        void BuildGrid()
        {
            _viewport = UIKit.Node("Viewport", Content);
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
        /// Rebuilt whole when a choice changes. Thirty cells is nothing next to keeping
        /// a selected ring, a caption and a hero portrait in step by hand.
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

            var roster = AvatarCatalog.All;
            int level = Profile.Rank;
            string worn = Profile.Avatar.Id;

            for (int i = 0; i < roster.Count; i++)
            {
                float x = (i % Columns - (Columns - 1) * .5f) * CellW;
                float y = -(i / Columns) * CellH - CellH * .5f - 12f;

                // The whole rule — reached by level or bought. Asking AvatarCatalog directly
                // here is what would draw a padlock over a companion the player paid for.
                Cell(roster[i], new Vector2(x, y), CompanionLedger.IsHeld(roster[i], level),
                     string.Equals(roster[i].Id, worn, StringComparison.Ordinal), i);
            }

            int rows = (roster.Count + Columns - 1) / Columns;
            _grid.sizeDelta = new Vector2(0f, rows * CellH + 40f);

            if (_summary) _summary.text = SummaryText(level);
        }

        void Cell(AvatarDefinition avatar, Vector2 at, bool unlocked, bool worn, int index)
        {
            var cell = UIKit.Button("A_" + avatar.Id, _grid, Art.Pixel, new Vector2(CellW - 16f, CellH - 20f),
                                    new Vector2(.5f, 1f), at, () => Choose(avatar, unlocked));
            cell.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0f);

            var plate = UIKit.Img("Plate", cell.transform, Art.Round(30),
                                  worn ? Pal.A(Pal.Hex("#0C4A44"), .92f) : new Color(.03f, .10f, .13f, .78f),
                                  new Vector2(CellW - 28f, CellH - 34f), new Vector2(.5f, .5f), Vector2.zero);
            var edge = UIKit.Img("Edge", plate.transform, Art.RoundOutline(30, worn ? 4f : 2f),
                                 worn ? Pal.A(Pal.Gold, .95f) : new Color(1f, 1f, 1f, unlocked ? .14f : .07f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            if (worn) UIKit.Halo(plate.transform, Pal.Gold, 300f, .26f);

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

            // A locked companion says its price rather than only its level gate. The gate is
            // still the honest headline for a companion the player will reach soon, but most
            // of this grid is gated above anything a shipped catalog can reach — so a cell
            // that only ever said "level 40" was telling a player about a wait that does not
            // end, and hiding the answer that does.
            UIKit.Shrinkable(
                UIKit.Titled("Sub", plate.transform,
                             worn ? Loc.Get("ui.profile.wearing")
                                  : unlocked ? Loc.Get("ui.profile.tap_to_wear")
                                             : avatar.IsForSale
                                                 ? Loc.Format("ui.profile.cost", avatar.UnlockCost)
                                                 : Loc.Format("ui.profile.locked_at", avatar.UnlockLevel),
                             24, worn ? Pal.Gold
                                      : unlocked ? new Color(1f, .96f, .88f, .5f)
                                                 : Pal.A(Pal.Sun, .90f),
                             TextAnchor.MiddleCenter, new Vector2(CellW - 60f, 32f), new Vector2(.5f, 0f),
                             new Vector2(0f, 36f), 3f, 0f), 18);

            // The gate keeps a line of its own on a priced cell, because the free route is
            // the one that must not disappear behind the paid one.
            if (!unlocked && avatar.IsForSale)
                UIKit.Titled("Gate", plate.transform,
                             Loc.Format("ui.profile.locked_at", avatar.UnlockLevel), 20,
                             new Color(1f, .95f, .88f, .42f), TextAnchor.MiddleCenter,
                             new Vector2(CellW - 60f, 26f), new Vector2(.5f, 0f),
                             new Vector2(0f, 12f), 3f, 0f);

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
            Haptic.Tap();
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

            UIKit.IconButton("Back", Content, "sq_dark", "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, 1f), new Vector2(96f, -132f), () => Flow.Go<ProfileScreen>());

            var banner = UIKit.Img("Banner", Content, Art.S("Ui/banner"), Color.white,
                                   new Vector2(560f, 148f), new Vector2(.5f, 1f), new Vector2(0f, -142f));
            UIKit.Titled("Title", banner.transform, Loc.Get("ui.profile.companions").ToUpperInvariant(), 40,
                         new Color(.36f, .24f, .16f), TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);
            banner.transform.localScale = Vector3.zero;
            Tween.Pop(banner.transform, 0f, .6f, .1f);

            _summary = UIKit.Titled("Summary", Content, SummaryText(Profile.Rank), 26,
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
