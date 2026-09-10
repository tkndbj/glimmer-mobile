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
        /// is a place. It is tapped, it is the thing that lights, and it is exactly what burns —
        /// invariant 33g at its strongest, because <c>SiegeBoard.Blast</c> and this loop now share
        /// their integers rather than agreeing about a mapping.
        /// </para>
        /// <para>
        /// <b>The panes are geometry and never outcome</b> (invariant 32c). They say where the
        /// boxes are, which is a fact about the board; nothing here counts what is standing in one
        /// or marks the ones worth throwing at. That is the question the player is being asked.
        /// </para>
        /// </summary>
        void AimHill()
        {
            float top = _hillTop + Cell * .35f;
            float wide = Span.x / SiegeTuning.Lanes;
            float tall = (top - _hillFoot) / SiegeTuning.BlastRows;

            for (int row = 0; row < SiegeTuning.BlastRows; row++)
            {
                for (int lane = 0; lane < SiegeTuning.Lanes; lane++)
                {
                    int atLane = lane, atRow = row;

                    // Boxes are laid out from the top of the hill down, which is the direction
                    // `march` runs: row 0 is where a wave walks on.
                    var at = new Vector2(LaneX(lane), top - (row + .5f) * tall);

                    var pane = UIKit.Button("Aim" + lane + "_" + row, _aim, Art.Round(18),
                                            new Vector2(wide - 6f, tall - 6f),
                                            new Vector2(.5f, .5f), at,
                                            () => Loose(SiegeAim.OnTheHill(atLane, atRow)));

                    pane.PressScale = .96f;
                    pane.ClickSfx = null;

                    var face = pane.GetComponent<Image>();
                    if (face != null)
                    {
                        face.type = Image.Type.Sliced;
                        face.color = Pal.A(Pal.Azure, .18f);
                    }

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

        void Firepot(SiegeAim aim)
        {
            // The middle of the box that was tapped, worked out the way the panes were laid out —
            // one arithmetic, so the burst lands where the ring the player aimed at was.
            float top = _hillTop + Cell * .35f;
            float tall = (top - _hillFoot) / SiegeTuning.BlastRows;
            var at = new Vector2(LaneX(aim.Lane), top - (aim.Row + .5f) * tall);

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
            if (mob == null || mob.Node == null || _fx == null) return;

            var at = mob.Node.anchoredPosition;
            var frames = StormBolt;
            if (frames == null || frames.Length == 0) return;

            // **Drawn upside down, and that is a fact about the pack rather than a trick.** The
            // bought effect is authored as a strike seen from the side: its flash is at the top
            // of the frame with the bolt hanging below, which is a bolt *leaving* a cloud. This
            // board needs the other end - the flash on the raider and the bolt trailing up out
            // of shot - so the reel is flipped and anchored by its bright end.
            //
            // **And it is drawn much wider than its own frame.** The pack cuts a real bolt, which
            // is a few pixels of core inside a tall picture; at its own aspect on a phone that is
            // a bright thread nobody sees. Stretching it across is the one distortion lightning
            // survives - a wide bolt still reads as a bolt, where a wide fireball would not.
            float tall = Cell * StormTall;
            float wide = tall * frames[0].rect.width / frames[0].rect.height * StormWiden;

            var shaft = UIKit.Img("Bolt", _fx, frames[0], Color.white, new Vector2(wide, tall));
            shaft.raycastTarget = false;
            shaft.rectTransform.localScale = new Vector3(1f, -1f, 1f);
            shaft.rectTransform.anchoredPosition = new Vector2(at.x, at.y + tall * .5f);

            var group = UIKit.Group(shaft.rectTransform);
            var book = Flipbook.Attach(shaft, frames, StormFps, false);

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
        /// How a storm's bolt is drawn: its height in cells, how far it is stretched across, how
        /// fast the reel runs and how long it is held after it has run out.
        ///
        /// <b>All four exist because "I do not even see the lightning" was the verdict on the
        /// first cut.</b> It was 4.6 cells tall at its own aspect, run at 30fps and destroyed on
        /// its last frame - a thread on screen for six tenths of a second. Wider, taller, half the
        /// frame rate and a held fade is the same bought asset made legible.
        /// </summary>
        const float StormTall = 5.4f, StormWiden = 2.8f, StormFps = 15f, StormLinger = .34f;

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
