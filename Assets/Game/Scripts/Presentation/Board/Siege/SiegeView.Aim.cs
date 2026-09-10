using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The action bar's half of the board: arming a utility, drawing what it would hit, and
    /// spending it.
    ///
    /// <para>
    /// <b>Geometry and never outcome</b> (invariant 32c): the grid over the hill and the ring
    /// round a ward are facts the player can already read, drawn faster. What a blast would
    /// <em>catch</em> is deliberately not drawn.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ aiming
        /// <summary>
        /// Builds or tears down the targeting layer for whatever is armed.
        ///
        /// <para>
        /// Built on arming rather than kept and hidden, because it is a <em>layer over the
        /// board</em>: a pad that stayed alive would sit in front of the gems for the whole run
        /// and swallow every swap, which is the class of fault a hidden raycast target always is.
        /// </para>
        /// </summary>
        void Aiming()
        {
            if (_aim != null)
            {
                Destroy(_aim.gameObject);
                _aim = null;
            }

            if (_arming == null || _fx == null) return;

            _aim = Layer("Aim");
            _aim.SetAsLastSibling();

            // **An `Everywhere` utility is never aimed and never gets a layer.** It lands on
            // the whole hill, so there is nothing to point at - and a targeting mode that accepts
            // any tap and ignores where it was is a control that rejects nothing (invariant 5d).
            // `SiegeScreen` fires one on the tap that arms it, so this is only ever reached if
            // something armed it another way; tearing the layer down and standing there is the
            // safe answer.
            if (_arming.Target == UtilityTarget.Everywhere) return;

            if (_arming.Target == UtilityTarget.Ward) AimWards();
            else AimHill();
        }

        /// <summary>
        /// The hill, drawn as the grid the rule reads: one box per lane per band, each a pane of
        /// blue with a ring in the middle of it.
        ///
        /// <para>
        /// <b>Boxes rather than a ring that follows a finger, and that is the whole change.</b> A
        /// radius round wherever a drag ended is exact in the rule and unreadable on the board:
        /// what a player had to do was judge a distance against raiders that were walking. A box
        /// is a place. It is tapped, it is the thing that lights, and <see cref="Scorch"/> then
        /// lights exactly what burned — invariant 33g at its strongest, because
        /// <c>SiegeBoard.Blast</c> and this loop share their integers through <see cref="BoxAt"/>
        /// rather than agreeing about a mapping. For most of this mode's life they did not: see
        /// <see cref="BoxAt"/> for the two ways they disagreed.
        /// </para>
        /// <para>
        /// <b>The panes are geometry and never outcome</b> (invariant 32c). They say where the
        /// boxes are, which is a fact about the board; nothing here counts what is standing in one
        /// or marks the ones worth throwing at. That is the question the player is being asked.
        /// </para>
        /// </summary>
        void AimHill()
        {
            float wide = BoxWide, tall = BoxTall;

            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                for (int lane = 0; lane < SiegeTuning.Lanes; lane++)
                {
                    int atLane = lane, atRow = row;

                    // Boxes are laid out from the top of the hill down, which is the direction
                    // `march` runs: row 0 is where a wave walks on. `BoxAt` is the only place the
                    // grid is worked out, and it is worked out from `MarchY` — see its remarks for
                    // the two ways this used to disagree with the rule that reads it.
                    var at = BoxAt(lane, row);

                    // **The target is the whole box; the gutter is painted, not cut.** It used to
                    // be the pane itself that was six units under the box, which drew the gutter
                    // and put a dead seam between every pair of boxes on the one layer whose whole
                    // job is to catch a tap. The button now tiles the hill exactly and the air is a
                    // fact about the face inside it.
                    var pane = UIKit.Button("Aim" + lane + "_" + row, _aim, Art.Round(18),
                                            new Vector2(wide, tall),
                                            new Vector2(.5f, .5f), at,
                                            () => Loose(SiegeAim.OnTheHill(atLane, atRow)));

                    pane.PressScale = .96f;
                    pane.ClickSfx = null;

                    // Kept as the raycast target and painted out. A transparent `Image` still
                    // catches a tap, which is what lets the hit box and the drawing differ by the
                    // gutter without the gutter costing anybody a tap.
                    var catcher = pane.GetComponent<Image>();
                    if (catcher != null) catcher.color = new Color(0f, 0f, 0f, 0f);

                    var face = UIKit.Img("Face", pane.transform, Art.Round(18),
                                         Pal.A(Pal.Azure, .18f),
                                         new Vector2(wide - Gutter, tall - Gutter));
                    face.type = Image.Type.Sliced;
                    face.raycastTarget = false;

                    // The ring in the middle: what says the box is a target rather than a tile,
                    // and where the burst will be centred.
                    var eye = UIKit.Img("Eye", pane.transform, Art.Ring(96, 6f),
                                        Pal.A(Pal.Azure, .78f),
                                        Vector2.one * Mathf.Min(wide, tall) * .46f);
                    eye.raycastTarget = false;

                    Tween.Breathe(eye.transform, .07f, 1.6f, row * .12f + lane * .05f);
                }
            }
        }

        /// <summary>A target over each standing ward. A fallen one is not a target.</summary>
        void AimWards()
        {
            if (_posts == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                if (_board.Wards[i] == null || !_board.Wards[i].Alive) continue;

                int ward = i;

                // **Sized to the post it is round, not to a guess.** A ward's node is 1.8 by 2.3
                // cells with its body standing a hair above the middle of it, so a circle of 1.6
                // sat high and covered the barrel rather than the turret — reported as exactly
                // that. This is an ellipse round the whole of it, which is also what makes it a
                // target big enough to hit with a thumb.
                var size = new Vector2(Cell * 2.05f, Cell * 2.6f);

                var hit = UIKit.Button("AimWard" + i, _aim, Art.Ring(128, 7f),
                                       size, new Vector2(.5f, .5f),
                                       new Vector2(PostX(i), _lineY + Cell * .06f),
                                       () => Loose(SiegeAim.AtWard(ward)));

                hit.PressScale = .92f;
                hit.ClickSfx = null;

                var img = hit.GetComponent<Image>();
                if (img != null) img.color = Pal.A(Pal.Sun, .9f);

                Tween.Breathe(hit.transform, .06f, .9f);
            }
        }

        /// <summary>
        /// Hands a chosen target to the screen and draws whatever came back.
        ///
        /// <b>Disarmed first, whatever happens.</b> A refusal that left the layer up would put
        /// the player back in a targeting mode they had just been told they could not use, and a
        /// success that left it up would arm a second use of an item they may no longer hold.
        /// </summary>
        /// <summary>
        /// Uses a utility that is aimed at nothing, from outside.
        ///
        /// <b>It never goes through <see cref="Arming"/></b>, because there is nothing to aim: the
        /// screen calls this on the tap that would have armed it, so the item is used at once and
        /// the board is never left in a targeting mode with no target.
        /// </summary>
        public void Loose(UtilityItem item) => Spend(item, default);

        void Loose(SiegeAim aim) => Spend(_arming, aim);

        void Spend(UtilityItem item, SiegeAim aim)
        {
            if (item == null || Fire == null) { Arming = null; return; }

            _strikes.Clear();
            var use = Fire(item, aim, _strikes);

            Arming = null;
            Done?.Invoke();

            if (!use.Landed)
            {
                Rejected?.Invoke();
                return;
            }

            Charged(use.Matches);

            // **A storm judges itself when it has finished falling.** Everything else here
            // resolves in one frame, so asking whether the run is over immediately after is
            // right; a storm is drawn over a second or more, and judging it at once would put
            // the victory panel up while bolts were still landing on raiders the player can
            // still see.
            if (Struck(item, aim, use)) Judge();
        }

        /// <summary>
        /// What a landed utility looks like. Answers false when it will judge the run itself.
        /// </summary>
        bool Struck(UtilityItem item, SiegeAim aim, SiegeUse use)
        {
            switch (item.Kind)
            {
                case UtilityKind.Blast:
                    Firepot(aim);
                    break;

                case UtilityKind.Mend:
                    Mended(use.Ward);
                    break;

                case UtilityKind.Surge:
                    Surged(use.Ward);
                    break;

                case UtilityKind.Storm:
                    // **The one utility whose strikes are not drawn here**, because they are not
                    // drawn all at once: a storm is a run of bolts falling one after another
                    // across the hill, so the list is walked by a coroutine which fells each
                    // raider as its own bolt lands and judges the run at the end.
                    Stormcall(_strikes);
                    return false;
            }

            for (int i = 0; i < _strikes.Count; i++) Hurt(_strikes[i]);

            Reap();
            Changed?.Invoke();
            return true;
        }

        /// <summary>Air between one box of the aiming grid and the next, in units.</summary>
        const float Gutter = 6f;

        /// <summary>
        /// A firepot going off: the boxes it burns, lit for a beat, and the explosion in the middle
        /// of them.
        ///
        /// <para>
        /// <b>The burned boxes are drawn, and that is the whole of what makes a wider blast
        /// legible.</b> A firepot takes the box that was tapped and the four touching it
        /// (<see cref="SiegeTuning.BlastReach"/>), so an explosion drawn only in the middle would
        /// be a rule the player has to infer from which raiders fell. Lighting the plus says it
        /// once, at the moment it happens, in the same twenty rectangles they were just aiming at —
        /// which is invariant 33g asked of the feedback rather than of the input, and 32c is not
        /// troubled by it because this is what the item <em>did</em> rather than a preview of what
        /// it would do.
        /// </para>
        /// </summary>
        void Firepot(SiegeAim aim)
        {
            // The middle of the box that was tapped, from `BoxAt` — the same arithmetic the panes
            // were laid out with, so the burst lands on the ring the player aimed at.
            var at = BoxAt(aim.Lane, aim.Row);

            Scorch(aim);

            Boom(at, Blast("boom_fire"), Cell * 3.4f);
            Burst.Sparks(_fx, at, Pal.Ember, 22, Cell * 3f, Cell * .3f);
            Shockwave(at, Pal.Sun, Cell * 4.5f, .34f);
            ShakeBoard(26f);

            // Its own clip rather than the `burst` a raider's death plays. That one is struck
            // thirteen times in a wave and is tuned to be the shortest, brightest thing in the
            // set; a firepot is one event a run and the loudest thing a player can cause, so
            // sharing a sound would tune the big moment by the small one.
            Audio.SfxVaried("boom", .8f);
        }

        /// <summary>Every box a firepot burned, lit for a beat and gone.</summary>
        void Scorch(SiegeAim aim)
        {
            if (_fx == null) return;

            var size = new Vector2(BoxWide - Gutter, BoxTall - Gutter);

            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                for (int lane = 0; lane < SiegeTuning.Lanes; lane++)
                {
                    // Asked of the rule rather than worked out here, so the boxes that light and
                    // the boxes that burn cannot come apart — including at an edge, where the plus
                    // simply has fewer arms.
                    if (!SiegeTuning.InBlast(aim.Lane, aim.Row, lane, row)) continue;

                    var pane = UIKit.Img("Scorch", _fx, Art.Round(18),
                                         Pal.A(Pal.Ember, .55f), size);
                    pane.type = Image.Type.Sliced;
                    pane.raycastTarget = false;
                    pane.rectTransform.anchoredPosition = BoxAt(lane, row);

                    var group = UIKit.Group(pane.rectTransform);

                    // The middle goes last, because it is the one under the explosion: an arm that
                    // outlived it would read as the fire spreading rather than as the box that was
                    // hit being part of the same blast.
                    float held = lane == aim.Lane && row == aim.Row ? .40f : .30f;

                    Tween.Fade(group, 0f, held)
                         .OnDone(() => { if (pane) Destroy(pane.gameObject); });
                }
            }
        }

        void Mended(int ward)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);

            Burst.Sparks(_fx, at, Pal.Mint, 16, Cell * 2f, Cell * .2f, .5f);
            Shockwave(at, Pal.Mint, Cell * 2.6f, .30f);
            Tween.Pop(_posts[ward].Node, 1.12f, .28f);

            // A spell rather than an object. `chime` read as a coin landing here, which is the
            // wrong news about a ward being put back together.
            Audio.Sfx("mend", .75f);
        }

        void Surged(int ward)
        {
            if (_posts == null || ward < 0 || ward >= _posts.Length) return;

            var post = _posts[ward];
            var at = new Vector2(PostX(ward), _lineY + Cell * .5f);
            var tint = TintOf(_board.Wards[ward].Colour);

            Burst.Sparks(_fx, at, tint, 20, Cell * 2.2f, Cell * .2f, .45f);
            Shockwave(at, tint, Cell * 3f, .30f);
            Tween.Shake(post.Node, Cell * .1f, .3f);

            Audio.Sfx("lit", .7f, .85f);
        }

        /// <summary>One raider taking a hit from the player's own hand.</summary>
        /// <summary>
        /// A storm: the sky goes white and bolts come down one at a time, wherever the board put
        /// them.
        ///
        /// <para>
        /// <b>Staggered rather than simultaneous, and that is the whole drawing.</b> Every strike
        /// lands in the rules the instant the item is used - the board is already resolved before
        /// a single pixel moves - so what this decides is only what the player watches. All of it
        /// at once is one white frame and a hill that is suddenly empty, which reads as the game
        /// skipping something; one bolt every <see cref="StormStep"/> is a thing happening, and it
        /// is what the item is being paid for.
        /// </para>
        /// <para>
        /// <b>The order is the board's, not the view's</b> - <c>SiegeBoard.Storm</c> shuffles the
        /// strikes off the same stream that deals the field, so the bolts fall here and there
        /// rather than down the list, and they fall the same way on two devices (invariant 37e).
        /// </para>
        /// <para>
        /// <b>It waits in real seconds</b>, because every wait in a screen coroutine here has to:
        /// a modal sets <c>Time.timeScale</c> to nought, and a scaled wait behind one never
        /// finishes.
        /// </para>
        /// </summary>
        void Stormcall(List<SiegeStrike> hits)
        {
            Sky();
            ShakeBoard(22f);

            // Not `boom`, which is the firepot's. That one is an explosion and this is not.
            Audio.Sfx("shatter", .9f, .72f);

            // **Claimed before a single bolt falls.** The rules killed all of these the instant
            // the item was used, so `Reap` — which runs every frame and takes down anything dead —
            // would clear the hill one frame in and leave the rest of the storm falling on empty
            // ground. See `_striking`.
            _striking.Clear();
            for (int i = 0; i < hits.Count; i++)
                if (hits[i].Killed) _striking.Add(hits[i].Raider);

            // **And the ending is held for the whole storm, said once here rather than left to
            // fall out of the per-bolt holds.** A storm very often *is* the killing blow — the
            // rules resolve it in one instant, so the verdict is already Won while the first
            // bolt is still in the air — and `Judge` runs every frame. Arming it from the length
            // of the reel means it cannot come apart if a strike stops being a kill or the step
            // is retuned. See `_felling`.
            Felling(hits.Count * StormStep + DyingFor);

            // Copied, because `_strikes` is cleared by the next use and this outlives the call.
            StartCoroutine(Storming(new List<SiegeStrike>(hits)));
        }

        /// <summary>
        /// Seconds between one bolt of a storm and the next.
        ///
        /// <b>Long enough that they are separate events.</b> It was .085, which over a full hill
        /// is eight bolts inside a second - fast enough that what a player sees is one flash and
        /// an empty hill, which is the thing drawing them one at a time exists to avoid.
        /// </summary>
        const float StormStep = .30f;

        IEnumerator Storming(List<SiegeStrike> hits)
        {
            for (int i = 0; i < hits.Count; i++)
            {
                Bolt(hits[i]);
                Hurt(hits[i]);

                // **Each raider dies to its own bolt.** The rules resolve the whole storm in one
                // instant - they have to, or a run could be won half way through one - so if the
                // widgets were all cleared at the end the hill would empty in a single frame
                // after a second of bolts, which is exactly what "it kills them all at once"
                // describes. Felling the one just struck is what makes the drawing match the item.
                if (hits[i].Killed) Fell(hits[i].Raider);

                // Given up as it is struck, so a raider this storm never reaches — the board was
                // dealt again, the reel was cut short — is claimed by nothing and `Reap` has it
                // back on the next frame.
                _striking.Remove(hits[i].Raider);

                yield return new WaitForSecondsRealtime(StormStep);
            }

            _striking.Clear();

            // Anything the strikes did not account for, then the verdict - held to here so a
            // victory panel cannot arrive over a hill that is still being struck.
            Reap();
            Changed?.Invoke();
            Judge();
        }

        /// <summary>Kills one raider's widget now, rather than waiting for <see cref="Reap"/>.</summary>
        void Fell(int raiderId)
        {
            for (int i = _mob.Count - 1; i >= 0; i--)
            {
                var mob = _mob[i];
                if (mob.Id != raiderId || mob.Falling) continue;

                mob.Falling = true;
                Blasted(mob);
                _mob.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// A raider killed by lightning, which is not a raider killed by a bomb.
        ///
        /// <b>No fireball.</b> <see cref="Die"/> draws <c>boom_fire</c> and throws ember sparks,
        /// which is right for a bolt or a firepot and reads as an explosion - and an explosion is
        /// the one thing a lightning strike is not. This one goes white, stiffens and drops.
        /// </summary>
        void Blasted(Mob mob)
        {
            if (mob.Boss) { Die(mob); return; }

            // The run may not be told until this has been watched, exactly as `Die`'s is — a
            // storm's last bolt is very often the killing blow. See `_felling`.
            Felling(DyingFor);

            Audio.SfxVaried("blocked", .30f);

            var node = mob.Node;
            var body = mob.Body;
            var group = UIKit.Group(node);

            Tween.Run(.40f, Ease.OutQuad, t =>
            {
                if (!node) return;
                if (body) body.color = Color.Lerp(Color.white, Pal.Cream, 1f - t);
                node.localScale = new Vector3(1f - t * .25f, 1f - t * .45f, 1f);
                if (group) group.alpha = 1f - t;
            }, node).OnDone(() => { if (node) Destroy(node.gameObject); });
        }

        /// <summary>One bolt of a storm, falling on one raider.</summary>
        void Bolt(SiegeStrike hit)
        {
            // **`MobOf`, never `Widget`.** A raider can only be struck once it is on the hill, so
            // its widget already exists — and `Widget` *hatches* one when it does not, which on a
            // raider this very call is about to kill would put a fresh body on the board to be
            // torn down again. Asking only what is already drawn cannot resurrect anything.
            var mob = MobOf(hit.Raider);
            if (mob == null || mob.Node == null || _fx == null || _sky == null) return;

            var at = mob.Node.anchoredPosition;
            var frames = StormBolt;
            if (frames == null || frames.Length == 0) return;

            // **Drawn upside down, and that is a fact about the pack rather than a trick.** The
            // bought effect is authored with its flash at the prefab's own origin and the bolt
            // running *downward* from it, which is a strike seen from the cloud's end. This board
            // needs the other one - the flash on the raider and the bolt trailing up out of shot -
            // so the reel is flipped. The flip is about the sprite's centre, so a point
            // <see cref="StrikeAt"/> of the way up the baked frame is drawn that far *down* from
            // the top, which is the complement the bake is given.
            //
            // **At its own aspect.** It was stretched 2.8x across, on the argument that the pack
            // cuts a few pixels of core inside a tall picture and a wide bolt still reads as a
            // bolt. What a 2.8x horizontal scale actually reads as is a smear, which was reported
            // from a device as the effect being blurry - and the thing it was compensating for is
            // fixed where it was caused: the reel is baked with a bloom now, so the bolt is thick
            // and lit rather than a thread that had to be stretched to be seen at all.
            // **One size wherever it lands, and cut off at the top of the board.** Sizing it to
            // the room above whatever it hit was tried first and is worse than the fault it
            // fixes: a hill is about four cells deep, so a strike on a raider half way up came
            // out a cell and a half long and read as a spark. A bolt that runs off the top of the
            // picture is what lightning looks like — it comes from somewhere above the frame —
            // and `_sky` is clipped to the board so it can.
            float tall = Cell * StormTall;
            float wide = tall * frames[0].rect.width / frames[0].rect.height;

            var shaft = UIKit.Img("Bolt", _sky, frames[0], Color.white,
                                  new Vector2(wide, tall));
            shaft.raycastTarget = false;
            shaft.rectTransform.localScale = new Vector3(1f, -1f, 1f);

            // **Anchored by where the strike lands, never by the frame's middle.** `UIKit.Box`
            // pivots at centre, so `at.y + tall * .5f` - what this shipped as - put the *centre*
            // of a 5.4-cell frame on the raider's feet and the flash half a frame above it. On
            // screen that is a bolt going off nearly three cells over the raider's head with
            // nothing at all drawn where it was aimed, which is exactly what "it strikes at
            // random spots, not even at enemies" was. The flash sits `StrikeAt` up the drawn
            // frame, so the frame's centre goes `(.5 - StrikeAt)` of it *below* the target.
            shaft.rectTransform.anchoredPosition =
                new Vector2(at.x, at.y - tall * (.5f - StrikeAt));

            var group = UIKit.Group(shaft.rectTransform);
            var book = Flipbook.Attach(shaft, frames, StormFps, false);

            Struck(at);

            // Held after the reel has run out rather than cut at its last frame: the strike fades
            // over about a fifth of a second, and what was reported is that it is gone before it
            // has been seen.
            if (book != null)
                book.OnFinished = () => Tween.Fade(group, 0f, StormLinger)
                                             .OnDone(() => { if (shaft) Destroy(shaft.gameObject); });
            else
                Tween.After(1.2f, () => { if (shaft) Destroy(shaft.gameObject); });
        }

        /// <summary>
        /// The ground where a bolt just landed: a flash, a ring and a spray of sparks.
        ///
        /// <para>
        /// <b>Drawn by the board rather than baked into the reel, because it is the half that has
        /// to know where the hill is.</b> A reel is one rectangle of pixels at a fixed size; what
        /// makes a strike read as having *arrived somewhere* is light thrown onto the ground under
        /// it, and the ground is a different colour and a different size on every rung. This is
        /// also the cheap half of the difference between the vendor's picture and ours - theirs is
        /// a lit scene and a bolt, and a bolt on its own is a picture of lightning rather than of
        /// something being struck.
        /// </para>
        /// <para>
        /// <b>Warm rather than gold</b>, which is the same ladder the reel itself is graded on
        /// (white core, <c>Pal.Sun</c> body, <c>Pal.Ember</c> haze): the ring and the ground flash
        /// are the outermost rung, so they are the ember, and the sparks that fly are the middle
        /// one. A single colour for all three is what makes a burst read as a decal.
        /// </para>
        /// </summary>
        void Struck(Vector2 at)
        {
            if (_fx == null) return;

            // The scorch on the floor: wide, warm, and gone almost at once. Behind everything
            // else in the layer, so the bolt and its sparks are drawn over their own light.
            // **Small, and it was not.** The first cut spread an ember glow three and a half cells
            // across and rang a ring the same size; drawn on three raiders a third of a second
            // apart, that is most of the hill under overlapping brown discs — which reads as the
            // board being stained rather than as anything being struck. The reel already carries
            // the bright half of the burst; what this adds is only the warm rung under it.
            var scorch = UIKit.Img("Scorch", _fx, Art.Glow(128, 2.2f), Pal.A(Pal.Ember, .62f),
                                   new Vector2(Cell * 1.7f, Cell * .72f));
            scorch.raycastTarget = false;
            scorch.rectTransform.anchoredPosition = at;
            scorch.transform.SetAsFirstSibling();

            Tween.Run(.34f, Ease.OutQuint, t =>
            {
                if (!scorch) return;
                float k = Mathf.Lerp(.5f, 1.15f, t);
                scorch.transform.localScale = new Vector3(k, k, 1f);
                var c = scorch.color; c.a = .62f * (1f - t * t); scorch.color = c;
            }, scorch).OnDone(() => { if (scorch) Destroy(scorch.gameObject); });

            Shockwave(at, Pal.Ember, Cell * 1.9f, .30f);
            Burst.Sparks(_fx, at, Pal.Sun, 14, Cell * 1.9f, Cell * .14f, .38f);
        }

        /// <summary>
        /// How a storm's bolt is drawn: its height in cells, how fast the reel runs and how long
        /// it is held after it has run out.
        ///
        /// <para>
        /// <b>They exist because "I do not even see the lightning" was the verdict on the first
        /// cut</b>, which was 4.6 cells tall, run at 30fps and destroyed on its last frame - a
        /// thread on screen for six tenths of a second. Taller, slower and held is that same
        /// bought asset made legible.
        /// </para>
        /// <para>
        /// <b>A fourth number is gone and its going is the point.</b> `StormWiden` stretched the
        /// reel 2.8x across, which is the same complaint answered in the drawing rather than at
        /// its cause - and a horizontal scale is the one thing that cannot make a bolt brighter,
        /// only wider and softer. The reel carries a bloom now (invariant 37af, which this reel
        /// was never given), so there is nothing left to compensate for.
        /// </para>
        /// <para>
        /// <b>And the reel runs faster than it did.</b> 15fps over eighteen frames is a bolt
        /// standing on the hill for a second and a fifth, which reads as a picture rather than as
        /// a strike; what makes lightning legible is that it is bright, not that it is slow.
        /// </para>
        /// </summary>
        const float StormTall = 5.0f, StormFps = 22f, StormLinger = .34f;

        /// <summary>
        /// How far up its own frame a strike's flash sits, as a fraction of the frame's height.
        ///
        /// <para>
        /// <b>Declared here and read by the bake</b> (<c>SiegeShotBake</c> references this
        /// constant), exactly as <see cref="HeadAt"/> and <see cref="MuzzleAt"/> are: the number
        /// that frames the render and the number that positions the sprite have to be one number,
        /// or the strike lands somewhere the reel was never framed around. That is not a
        /// hypothetical - this reel shipped framed at a half and drawn at a half *above* the
        /// target, which is a strike two and a half cells wide of everything it hit.
        /// </para>
        /// <para>
        /// <b>Low, because a strike is nearly all bolt.</b> What is above the flash is the whole
        /// length of the lightning and what is below is only the ground burst it leaves, so the
        /// flash sits near the foot of the frame and the rest is sky. It is measured from the
        /// bottom of the frame *as drawn*; the reel is flipped on the way to the screen, so the
        /// bake is handed the complement.
        /// </para>
        /// </summary>
        public const float StrikeAt = .17f;

        static Sprite[] StormBolt => Blast("storm") ?? Blast("shot_y");

        /// <summary>The sky going white over the whole board for a beat.</summary>
        void Sky()
        {
            if (_fx == null) return;

            var flash = UIKit.Img("Sky", _fx, Art.Round(4), Pal.A(Pal.Cream, .42f),
                                  new Vector2(Span.x, Span.y));
            flash.raycastTarget = false;
            flash.rectTransform.anchoredPosition = Vector2.zero;
            flash.transform.SetAsLastSibling();

            var group = UIKit.Group(flash.rectTransform);
            Tween.Fade(group, 0f, .38f)
                 .OnDone(() => { if (flash) Destroy(flash.gameObject); });
        }

        void Hurt(SiegeStrike hit)
        {
            // `MobOf` rather than `Widget`, for the reason `Bolt` says: a strike never needs a
            // widget minted, and minting one for a raider that has just died draws a corpse.
            var mob = MobOf(hit.Raider);
            if (mob == null || mob.Node == null) return;

            // Drawn as the player's own, which is bigger and hotter than a bolt's: a firepot
            // is one event a run and the loudest thing they can cause, and a figure the same size
            // as the eighteen a lit line throws every second is a figure nobody reads as theirs.
            Number(hit.Raider, mob.Node.anchoredPosition, hit.Damage, false, mine: true);

            if (!hit.Killed) Tween.Shake(mob.Node, Cell * .12f, .22f);
        }

        /// <summary>The widget for a raider the board has already taken off the hill.</summary>
        Mob MobOf(int id)
        {
            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Id == id) return _mob[i];

            return null;
        }

        void Banner()
        {
            _waveLabel = UIKit.Label("Wave", _fx, string.Empty, Mathf.RoundToInt(Cell * .46f),
                                     Pal.Cream, TextAnchor.MiddleCenter,
                                     new Vector2(Span.x, Cell * .9f));
            _waveLabel.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot + Cell * 1.5f);

            var group = UIKit.Group(_waveLabel.rectTransform);
            group.alpha = 0f;
        }
    }
}
