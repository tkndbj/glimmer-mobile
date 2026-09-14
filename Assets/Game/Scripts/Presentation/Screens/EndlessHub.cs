using System;
using GlimmerGrove.Content;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// What the map draws for a lane that has no ladder: the record as a medal, a plate of three
    /// lines saying what the lane is, and the key that starts it.
    ///
    /// <para>
    /// <b>It exists because the map was drawing the wrong picture, not because the map was
    /// wrong.</b> A chapter map is a painting of a <em>chain</em> — strips of island, a trail of
    /// drifting dots, a disc per level and a sealed teaser capping the run — and every one of
    /// those says "there is more of this further along". The Infinite lane has one level and
    /// nothing after it (<c>GameTrack.Laddered</c>), so all of that drew a column of scenery with
    /// a single node loose on it and a teaser promising a chapter that will never exist.
    /// </para>
    /// <para>
    /// <b>The record is the hero, and the first cut of this screen got that wrong.</b> It made an
    /// emblem the subject and printed the furthest wave as a line of text underneath — which is
    /// this screen with the one number the lane is graded on buried in a caption. A lane played
    /// for how far it got should put how far you got in the middle of it.
    /// </para>
    /// <para>
    /// <b>And it is furniture rather than loose parts.</b> The lines sit on a plate and their
    /// marks sit in the kit's own seats, because text and icons floating on a patterned ground
    /// read as a settings page: the first cut did exactly that and was rejected on sight. Every
    /// plate, seat, disc and burst here is the interface kit the whole game is drawn in
    /// (invariant 44) — what is bought from outside it is three <em>pictures</em>, which is the
    /// one thing the kit has nothing to say about.
    /// </para>
    /// <para>
    /// <b>Where things sit is <see cref="EndlessHubLayout"/>'s and not this file's</b> (invariant
    /// 8a's seventh instance; see <c>PanelStack</c>), and what it looks like is
    /// <c>Tools/render_endless.py</c>'s — which caught the emblem's ring standing outside its own
    /// box, a caption plate drawn at a third of the width asked for, and a band that had
    /// understated the header by 136 units.
    /// </para>
    /// </summary>
    public static class EndlessHub
    {
        /// <summary>
        /// The three marks, as whole addresses, in the order the lines are read.
        ///
        /// <para>
        /// <b>Whole addresses in a table, and the table is held to the manifest by a test.</b>
        /// <c>Tools/verify/artnames.py</c> reads literals off a call site and a name assembled
        /// there — <c>Art.S("Ui/" + mark)</c> — is a fragment it can never check, which on a
        /// missing sprite is a white rectangle rather than a blank (invariant 7b). This is
        /// <c>Skins</c>' own answer to the same problem: name them in one place and close the
        /// chain with reflection instead (<c>EndlessHubTests.TheHubsMarksAreGlobalArt</c>).
        /// </para>
        /// <para>
        /// They are cut by <c>make_siege_art.HUB_ICONS</c>.
        /// </para>
        /// </summary>
        public static readonly string[] Marks = { "Ui/ic_endless", "Ui/ic_surge", "Ui/ic_rank" };

        /// <summary>Seconds the starburst takes to turn once. Slow enough to be motion, not spin.</summary>
        const float BurstTurn = 26f;

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Draws the hub. <paramref name="ground"/> takes the full-bleed art and
        /// <paramref name="safe"/> takes everything a player reads or presses, which is
        /// <c>View.Safe</c>'s rule.
        /// </summary>
        /// <param name="headerFoot">
        /// How far the map's header column really reaches into the safe layer.
        ///
        /// <b>Handed in rather than read off a constant, because the constant is wrong for this.</b>
        /// <c>LevelsScreen.HeaderUnderside</c> is the bottom of the <em>mode</em> switcher's slot,
        /// and the track switcher is drawn under it when both are shown — 136 units further down.
        /// A map never noticed, because a map scrolls under its own header; a column placed against
        /// one does. Only the screen knows which pills it drew.
        /// </param>
        /// <param name="owner">
        /// What the tweens are filed under, so they die with the screen rather than with whatever
        /// object happened to be handy.
        /// </param>
        public static void Build(RectTransform safe, RectTransform ground, MonoBehaviour owner,
                                 GameMode mode, GameTrack lane, LevelDefinition level,
                                 float headerFoot, bool unlocked, Action open)
        {
            if (safe == null || ground == null) return;

            // **Behind everything the header already put down.** `BuildHeader` runs first (it is
            // index knowledge and must not wait on a file), so its fade and the safe layer are
            // older siblings and uGUI draws younger ones in front — the same trap the map's own
            // viewport records, arriving on the screen that replaced it.
            var art = UIKit.Node("HubArt", ground);
            art.SetAsFirstSibling();

            Scenery.Plain(art);

            float band = Band(headerFoot);
            float top = headerFoot + EndlessHubLayout.HeadClear + EndlessHubLayout.TopIn(band);

            var host = UIKit.Node("Hub", safe);

            Medal(host, owner, level, top);
            Lines(host, lane, top);
            Battle(host, unlocked, open, top);
        }

        /// <summary>
        /// The room the column has: the safe area, less the header column above it and whatever
        /// the loadout shelf reaches up into it.
        ///
        /// <para>
        /// <b>The canvas is asked of <c>Boot</c> rather than measured off a rect</b>, because a
        /// screen built in the same frame as the canvas can trust neither its rect nor its scale —
        /// <c>Boot.CanvasHeight</c> is a pure function of <c>Screen</c> and is right on the first
        /// frame.
        /// </para>
        /// <para>
        /// <b>The shelf is counted as what it reaches <em>into the safe layer</em>, and its tab is
        /// counted with it.</b> The bar hangs off the full-bleed layer and its lower part is the
        /// display's own foot, which the safe layer has already given up — subtracting the whole of
        /// it would charge that inset twice. What must <em>not</em> be left out is
        /// <c>LoadoutBar.Overhang</c>: the bar's rect is the shelf and its orange tab is drawn
        /// outside it, so a key measured against the rect alone is a key the tab can reach.
        /// </para>
        /// </summary>
        static float Band(float headerFoot)
        {
            float safe = Boot.CanvasHeight - SafeArea.Top - SafeArea.Bottom;
            float shelf = Mathf.Max(0f, LoadoutBar.Height + LoadoutBar.Overhang - SafeArea.Bottom);

            return safe - headerFoot - EndlessHubLayout.HeadClear - shelf;
        }

        // ------------------------------------------------------------------ the medal
        /// <summary>
        /// The furthest wave this lane has ever been held to, drawn as the thing it is: a medal.
        ///
        /// <para>
        /// <b>Two states and neither of them is a nought.</b> "Best wave 0" is a bad score and what
        /// a player who has never run this lane has is not a bad score, it is no score — so an
        /// unplayed medal is the kit's own <em>empty</em> star on a dark cap, which is a thing to
        /// earn rather than a number to be ashamed of. A dash was tried first and read as a
        /// missing character.
        /// </para>
        /// <para>
        /// <b>Generated art under the bought art</b>, which is invariant 7b's rule for anything a
        /// screen cannot afford to be missing: the glow is <c>Art</c>'s own and the burst, cap and
        /// plate are global, so nothing here can arrive as a white rectangle.
        /// </para>
        /// </summary>
        static void Medal(RectTransform host, MonoBehaviour owner, LevelDefinition level, float top)
        {
            int best = level != null ? EndlessLedger.BestFor(level.Id) : 0;
            bool held = best > 0;

            var seat = UIKit.Box("Medal", host,
                                 new Vector2(EndlessHubLayout.BurstSize,
                                             EndlessHubLayout.HeroHeight),
                                 new Vector2(.5f, 1f),
                                 new Vector2(0f, -(top + EndlessHubLayout.HeroCentre)));

            // Everything in the medal is placed against the *disc's* centre rather than the
            // block's, because the block is taller than the burst: the plate hangs off its foot.
            float discY = EndlessHubLayout.HeroCentre - EndlessHubLayout.DiscDown;

            UIKit.Halo(seat, held ? Pal.Sun : Pal.Slate, EndlessHubLayout.BurstSize * 1.6f, .20f,
                       new Vector2(0f, discY));

            var burst = UIKit.Img("Burst", seat, Art.S("Ui/Hud/burst"),
                                  held ? Pal.Gold : new Color(.41f, .50f, .69f),
                                  Vector2.one * EndlessHubLayout.BurstSize,
                                  new Vector2(.5f, .5f), new Vector2(0f, discY));

            if (burst != null)
            {
                burst.preserveAspect = true;
                burst.raycastTarget = false;

                Tween.Run(BurstTurn, Ease.Linear,
                          t => { if (burst) burst.transform.localRotation = Quaternion.Euler(0f, 0f, -360f * t); },
                          owner, "burst").Loop(-1, false);
            }

            var disc = UIKit.Img("Disc", seat, Art.S("Ui/Hud/" + (held ? "cap_on" : "cap_off")),
                                 Color.white, Vector2.one * EndlessHubLayout.DiscSize,
                                 new Vector2(.5f, .5f), new Vector2(0f, discY));
            if (disc != null) disc.preserveAspect = true;

            if (held)
            {
                // **Dark ink on a gold face.** The cap is the brightest thing on the screen, so a
                // cream number on it is the one caption here with nothing to read against.
                UIKit.Titled("Wave", seat, best.ToString(), 116, new Color(.32f, .21f, .06f),
                             TextAnchor.MiddleCenter,
                             new Vector2(EndlessHubLayout.DiscSize * .82f,
                                         EndlessHubLayout.DiscSize * .60f),
                             new Vector2(.5f, .5f), new Vector2(0f, discY + 4f), 0f, 0f);
            }
            else
            {
                var empty = UIKit.Img("Empty", seat, Art.S("Ui/star_empty"), Color.white,
                                      Vector2.one * (EndlessHubLayout.DiscSize * .52f),
                                      new Vector2(.5f, .5f), new Vector2(0f, discY));
                if (empty != null) empty.preserveAspect = true;
            }

            // The nameplate under it. A sliced trough rather than the kit's ribbon: `ribbon_orange`
            // is taller than it is wide and a caption plate is the other way round, so fitting one
            // here drew it at a third of the width asked for — which only a render could see.
            var plate = UIKit.Img("Plate", seat, Art.S("Ui/Hud/trough"), Color.white,
                                  new Vector2(EndlessHubLayout.PlateWidth,
                                              EndlessHubLayout.PlateHeight),
                                  new Vector2(.5f, .5f),
                                  new Vector2(0f, EndlessHubLayout.HeroCentre
                                                - EndlessHubLayout.PlateDown));
            if (plate != null) plate.type = Image.Type.Sliced;

            var caption = UIKit.Titled("Says", plate != null ? plate.transform : seat.transform,
                                       Loc.Get(held ? "ui.endless.best_label"
                                                    : "ui.endless.unplayed").ToUpperInvariant(),
                                       34, Pal.Cream, TextAnchor.MiddleCenter,
                                       new Vector2(EndlessHubLayout.PlateWidth - 40f,
                                                   EndlessHubLayout.PlateHeight), default, default,
                                       3f, 3f);
            UIKit.Shrinkable(caption, 22);

            seat.localScale = Vector3.zero;
            Tween.Pop(seat, 0f, .62f, .08f);
        }

        // ------------------------------------------------------------------ the lines
        /// <summary>
        /// The three short lines saying what this lane is, on one plate, each opened by a mark in
        /// the kit's own seat.
        ///
        /// <b>A row whose mark is missing still draws its sentence</b>, because a row is a sentence
        /// with a mark in front of it and not a mark with a caption.
        /// </summary>
        static void Lines(RectTransform host, GameTrack lane, float top)
        {
            var panel = UIKit.Img("Lines", host, Art.S("Ui/Hud/panel"), Color.white,
                                  new Vector2(EndlessHubLayout.PanelWidth,
                                              EndlessHubLayout.PanelHeight),
                                  new Vector2(.5f, 1f),
                                  new Vector2(0f, -(top + EndlessHubLayout.PanelCentre)));

            if (panel == null) return;

            panel.type = Image.Type.Sliced;

            var plate = (RectTransform)panel.transform;

            for (int i = 0; i < EndlessHubLayout.Points; i++)
            {
                // Measured down from the plate's own top edge, which is what the layout counts in.
                float y = EndlessHubLayout.PanelHeight * .5f - EndlessHubLayout.RowCentre(i);

                float seatX = -EndlessHubLayout.PanelWidth * .5f + EndlessHubLayout.PanelPad
                            + 12f + EndlessHubLayout.SlotSize * .5f;

                var seat = UIKit.Img("Seat" + i, plate, Art.S("Ui/Hud/slot"), Color.white,
                                     Vector2.one * EndlessHubLayout.SlotSize,
                                     new Vector2(.5f, .5f), new Vector2(seatX, y));
                if (seat != null) seat.type = Image.Type.Sliced;

                var mark = UIKit.Img("Mark" + i, seat != null ? seat.transform : plate,
                                     Art.S(Marks[i]), Color.white,
                                     Vector2.one * EndlessHubLayout.IconSize,
                                     new Vector2(.5f, .5f),
                                     seat != null ? Vector2.zero : new Vector2(seatX, y));
                if (mark != null) mark.preserveAspect = true;

                float textX = seatX + EndlessHubLayout.SlotSize * .5f + 26f;
                float room = EndlessHubLayout.PanelWidth * .5f - EndlessHubLayout.PanelPad - textX;

                var says = UIKit.Titled("Says" + i, plate, Loc.Get(lane.PointKey(i + 1)), 34,
                                        Pal.Cream, TextAnchor.MiddleLeft,
                                        new Vector2(room * 2f, EndlessHubLayout.RowHeight),
                                        new Vector2(.5f, .5f),
                                        new Vector2(textX + room, y), 3f, 3f);
                UIKit.Shrinkable(says, 22);
            }

            plate.localScale = Vector3.zero;
            Tween.Pop(plate, 0f, .55f, .18f);
        }

        // ------------------------------------------------------------------ the way in
        /// <summary>
        /// The key that starts a run, drawn exactly as the hub's own is: the same skin, the same
        /// size, the same glyph treatment and the same arrival.
        ///
        /// <b>The same on purpose.</b> This is the second place in the game a player presses to
        /// begin, and a second design for it would be a second answer to "what does starting look
        /// like" — the rule <c>ProductCard</c> records about the two shops, asked of a button.
        /// </summary>
        static void Battle(RectTransform host, bool unlocked, Action open, float top)
        {
            var play = UIKit.TextButton("Battle", host, Skins.Battle,
                                        Loc.Get("ui.endless.battle"), 62,
                                        new Vector2(EndlessHubLayout.ButtonWidth,
                                                    EndlessHubLayout.ButtonHeight),
                                        new Vector2(.5f, 1f),
                                        new Vector2(0f, -(top + EndlessHubLayout.ButtonCentre)),
                                        () => open?.Invoke(), "ic_battle");

            // The kit sizes a pill's glyph at a third of its height, which is small for the one
            // control a screen is about. The glyph is a painted picture rather than a silhouette,
            // so it is drawn white; and `FitLabel` centres the glyph and the caption as one block
            // off the glyph's own width, so it has to run again after the resize.
            if (play.Icon)
            {
                ((RectTransform)play.Icon.transform).sizeDelta = Vector2.one * 112f;
                play.Icon.color = Color.white;
                UIKit.FitLabel(play);
            }

            UIKit.OneLine(play, 34);
            UIKit.Halo(play.transform, Pal.Sun, 760f, .26f);

            play.transform.localScale = Vector3.zero;
            Tween.Pop(play.transform, 0f, .7f, .42f).OnDone(() =>
            {
                if (!play) return;

                play.Rehome();
                Tween.Breathe(play.transform, .03f, 2.1f);

                // Only when it is live. A sheen is an invitation, and inviting somebody to press a
                // key that will refuse them is worse than drawing nothing.
                if (unlocked) Sheen.Attach((RectTransform)play.transform, 3.4f);
            });
        }
    }
}
