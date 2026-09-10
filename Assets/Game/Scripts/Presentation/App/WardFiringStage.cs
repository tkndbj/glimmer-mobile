using System;
using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Wards;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A turret standing at the foot of a box, firing at something, over and over.
    ///
    /// <para>
    /// <b>One widget because it is one job.</b> The preview a player opens from the loadout and the
    /// bench used to judge the nineteen projectiles ask exactly the same question — <em>what does
    /// this turret look like when it shoots?</em> — and two answers to that would be two places
    /// where a bolt is anchored, sized or timed differently, which is the fault this project keeps
    /// recording under other names (two copies of one rule, each correct until one of them is not).
    /// </para>
    /// <para>
    /// <b>It draws through the shipped arithmetic and invents none of its own.</b> The anchors are
    /// <c>SiegeView.HeadAt</c> and <c>SiegeView.MuzzleAt</c>, the sizes are the multiples of a cell
    /// that <c>SiegeView.Bolt</c> uses and the per-turret scale is <c>SiegeView.BoltScale</c>. A
    /// preview that flattered a turret would be worse than none: what it is for is deciding whether
    /// to spend nine thousand credits.
    /// </para>
    /// <para>
    /// <b>Its own asset scope, released when the object goes.</b> A turret's body, its recoil and
    /// its three reels are what a run loads for the four it stands (invariant 7b); a panel that
    /// took <c>AssetLibrary.LineScope</c> would release a live board's line when it closed.
    /// </para>
    /// </summary>
    public sealed class WardFiringStage : MonoBehaviour
    {
        /// <summary>
        /// The three sizes <c>SiegeView</c> draws the exchange at, and how long a bolt is in the
        /// air. Multiples of a cell, so a stage drawn at any size draws them in proportion.
        /// </summary>
        const float MuzzleWide = 2.7f, BoltWide = 1.0f, HitWide = 3.2f;
        const float Flight = .34f, RecoilFor = .18f;

        /// <summary>How long the stage waits between shots, so one can be watched at a time.</summary>
        const float Between = 1.05f;

        /// <summary>The turret's own footprint, in cells, and how far it stands off the floor.</summary>
        const float TurretWide = 1.72f, TurretTall = 2.15f, TurretFoot = .10f;

        /// <summary>
        /// How big the thing being shot at is, and how far down from the top it stands.
        ///
        /// <b>Far enough down for its own impact.</b> A hit is drawn 3.2 cells across and centred
        /// on the target, so anything under about 1.6 cells from the top has half of the loudest
        /// frame in the whole exchange outside the box — and the box is masked, so it is cut off
        /// rather than merely overhanging.
        /// </summary>
        const float TargetTall = 1.55f, TargetTop = 1.9f;

        RectTransform _node;
        Image _turret, _target, _shadow;
        float _cell;
        string _scope;

        WardModel _model;
        int _colour;
        int _generation;

        Coroutine _firing;

        char Letter => WardLine.Colours[Mathf.Clamp(_colour, 0, WardLine.Colours.Length - 1)];
        Color Tint => SiegeView.TintOf(_colour);

        /// <summary>Where the barrel and the target sit, measured down from the box's own top.</summary>
        float BarrelTop => _node.rect.height - (TurretFoot + TurretTall) * _cell;
        float TargetAt => TargetTop * _cell;

        /// <summary>
        /// Builds a stage of <paramref name="size"/> inside <paramref name="parent"/>.
        ///
        /// <b>Masked</b>, because a bolt's frame is over three times as tall as it is wide with its
        /// head near the top: one leaving the barrel hangs well below the floor, and a panel is not
        /// a board with somewhere for it to go.
        /// </summary>
        public static WardFiringStage Attach(RectTransform parent, Vector2 size, Vector2 anchor,
                                             Vector2 pos, float cell, string scope)
        {
            var node = UIKit.Box("Stage", parent, size, anchor, pos);
            node.gameObject.AddComponent<RectMask2D>();

            var stage = node.gameObject.AddComponent<WardFiringStage>();
            stage._node = node;
            stage._cell = cell;
            stage._scope = scope;

            stage._shadow = UIKit.Img("Shadow", node, Art.Glow(64, 3f), new Color(0f, 0f, 0f, .40f),
                                      new Vector2(cell * 1.3f, cell * .34f), new Vector2(.5f, 0f),
                                      new Vector2(0f, cell * TurretFoot + cell * .10f));

            stage._turret = UIKit.Img("Turret", node, null, Color.white,
                                      new Vector2(cell * TurretWide, cell * TurretTall),
                                      new Vector2(.5f, 0f),
                                      new Vector2(0f, cell * (TurretFoot + TurretTall * .5f)));
            stage._turret.preserveAspect = true;
            stage._turret.enabled = false;

            stage._target = UIKit.Img("Target", node, null, Color.white,
                                      new Vector2(cell * TargetTall, cell * TargetTall),
                                      new Vector2(.5f, 1f), new Vector2(0f, -stage.TargetAt));
            stage._target.preserveAspect = true;
            stage._target.enabled = false;

            return stage;
        }

        /// <summary>
        /// Points the stage at a turret, loads what it draws with, and starts firing.
        ///
        /// <b>Safe to call again</b> — the loadout's bench switches colour with it — and a load
        /// that lands after a second call is discarded rather than drawn, which is what
        /// <see cref="_generation"/> is for.
        /// </summary>
        public void Show(WardModel model, int colour)
        {
            _model = model;
            _colour = colour;
            _generation++;

            Stop();
            Load(_generation);
        }

        void OnDestroy()
        {
            Stop();
            AssetLibrary.ReleaseScope(_scope);
        }

        void Stop()
        {
            if (_firing == null) return;

            StopCoroutine(_firing);
            _firing = null;
        }

        /// <summary>
        /// <b><c>async void</c> with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently, and what
        /// is behind it is already drawn.
        /// </summary>
        async void Load(int generation)
        {
            if (_model == null) return;

            char colour = Letter;
            var wanted = new List<AssetRequest>(6)
            {
                AssetRequest.Sprite(AssetManifest.SiegeArt(_model.ArtFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeArt(_model.FireFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.ShotFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.MuzzleFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeFx(_model.HitFor(colour))),
                AssetRequest.SpriteSet(AssetManifest.SiegeArt(TargetReel(colour))),
            };

            try { await AssetLibrary.EnsureScopeAsync(_scope, wanted); }
            catch (Exception e) { Debug.LogException(e); return; }

            if (this == null || generation != _generation) return;

            Dress();

            if (isActiveAndEnabled) _firing = StartCoroutine(Firing());
        }

        /// <summary>
        /// What the turret is shooting at: one of the beetles that crawl down the hill.
        ///
        /// <b>A raider the game already draws rather than a target drawn for this panel.</b> It is
        /// cut, hue-rotated and shipped for the weaver (<c>make_siege_art.WEAVER_SET</c>), so a
        /// preview costs no art at all — and what a player is deciding here is how this turret will
        /// look against the things it will actually be shooting.
        /// </summary>
        static string TargetReel(char colour) => "weaver_" + colour;

        void Dress()
        {
            if (_turret != null)
            {
                var body = AssetLibrary.Sprite(AssetManifest.SiegeArt(_model.ArtFor(Letter)));
                _turret.sprite = body;
                _turret.enabled = body != null;
            }

            if (_target == null) return;

            // An `Image` with no sprite is a white rectangle rather than a blank (invariant 7b), so
            // the widget is disabled until its frames are in hand rather than drawn empty.
            var reel = AssetLibrary.Frames(AssetManifest.SiegeArt(TargetReel(Letter)));

            if (reel == null || reel.Length == 0) { _target.enabled = false; return; }

            _target.enabled = true;
            Flipbook.Ensure(_target, reel, 12f);
        }

        /// <summary>
        /// Fires on a loop, so a turret can be watched rather than poked at.
        ///
        /// <b>Real seconds throughout.</b> A modal sets <c>Time.timeScale</c> to nought — and this
        /// widget's whole reason for existing is to be shown inside one — so a coroutine waiting in
        /// scaled seconds is one that never finishes (invariant 30h).
        /// </summary>
        IEnumerator Firing()
        {
            yield return new WaitForSecondsRealtime(.22f);

            while (true)
            {
                Fire();
                yield return new WaitForSecondsRealtime(Between);
            }
        }

        void Fire()
        {
            if (_node == null || _model == null) return;

            float from = -BarrelTop;
            float to = -TargetAt;
            float scale = SiegeView.BoltScale(_model);

            Flash(from);
            Recoil();

            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.ShotFor(Letter)));
            if (frames == null || frames.Length == 0) { Land(to, scale); return; }

            var bolt = Reel("Bolt", frames, _cell * BoltWide * scale, from, SiegeView.HeadAt, 30f,
                            true);
            var halo = Halo(from, scale);

            Tween.Run(Flight, Ease.Linear, t =>
            {
                if (bolt == null) return;

                float y = Mathf.Lerp(from, to, t);
                Head(bolt, y, SiegeView.HeadAt);

                if (halo != null) halo.rectTransform.anchoredPosition = new Vector2(0f, y);

                // The same swell the board draws, which is the only thing `SiegeView` animates
                // about a bolt - the frames under it are doing the rest.
                bolt.rectTransform.localScale = Vector3.one * Mathf.Lerp(.86f, 1.12f, t);
            }, bolt).OnDone(() =>
            {
                if (bolt != null) Destroy(bolt.gameObject);
                if (halo != null) Destroy(halo.gameObject);
                Land(to, scale);
            });
        }

        void Flash(float y)
        {
            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.MuzzleFor(Letter)));
            if (frames == null || frames.Length == 0) return;

            Sweep(Reel("Muzzle", frames, _cell * MuzzleWide, y, SiegeView.MuzzleAt, 33f, false),
                  .32f);
        }

        void Land(float y, float scale)
        {
            var frames = AssetLibrary.Frames(AssetManifest.SiegeFx(_model.HitFor(Letter)));

            if (frames != null && frames.Length > 0)
                Sweep(Reel("Hit", frames, _cell * HitWide * scale, y, .5f, 35f, false), .40f);

            // The thing being shot at flinches, which is what makes this an exchange rather than a
            // turret firing into the air.
            if (_target != null && _target.enabled) Tween.Punch(_target.transform, .12f, .16f);
        }

        /// <summary>The turret's own recoil frames, exactly as the line plays them.</summary>
        void Recoil()
        {
            if (_turret == null || !_turret.enabled) return;

            var frames = AssetLibrary.Frames(AssetManifest.SiegeArt(_model.FireFor(Letter)));
            if (frames == null || frames.Length == 0) return;

            Flipbook.Attach(_turret, frames, frames.Length / RecoilFor, false).OnFinished = () =>
            {
                if (_turret == null) return;

                Flipbook.Detach(_turret);
                _turret.sprite = AssetLibrary.Sprite(AssetManifest.SiegeArt(_model.ArtFor(Letter)));
            };
        }

        // ----------------------------------------------------------------- widgets
        /// <summary>
        /// One reel on the stage, anchored the way the board anchors it.
        ///
        /// A comet's head sits <c>SiegeView.HeadAt</c> of the way up its own frame and a muzzle
        /// flash sits at <c>MuzzleAt</c>, so drawing one is placing its <em>anchor</em> rather than
        /// its centre — getting that wrong is a bolt that appears to land before it arrives.
        /// </summary>
        Image Reel(string name, Sprite[] frames, float wide, float y, float anchor, float fps,
                   bool loop)
        {
            var img = UIKit.Img(name, _node, frames[0], Color.white, new Vector2(wide, wide),
                                new Vector2(.5f, 1f), new Vector2(0f, y));
            img.raycastTarget = false;
            img.preserveAspect = true;

            var first = frames[0];
            float tall = first != null && first.rect.width > 0f
                ? wide * first.rect.height / first.rect.width
                : wide;

            img.rectTransform.sizeDelta = new Vector2(wide, tall);
            Head(img, y, anchor);

            Flipbook.Attach(img, frames, fps, loop);
            return img;
        }

        /// <summary>Puts a reel's anchor point on <paramref name="y"/>, not its centre.</summary>
        static void Head(Image img, float y, float anchor)
        {
            if (img == null) return;

            float tall = img.rectTransform.sizeDelta.y;
            img.rectTransform.anchoredPosition = new Vector2(0f, y - (anchor - .5f) * tall);
        }

        /// <summary>The light under a bolt's head — the one thing the board tints from `Pal`.</summary>
        Image Halo(float y, float scale)
        {
            var img = UIKit.Img("Halo", _node, Art.Glow(96, 2.0f), Pal.A(Pal.Lift(Tint, .5f), .8f),
                                Vector2.one * (_cell * 1.7f * scale), new Vector2(.5f, 1f),
                                new Vector2(0f, y));
            img.raycastTarget = false;
            img.transform.SetAsFirstSibling();
            return img;
        }

        /// <summary>Clears a one-shot reel away after it has played.</summary>
        void Sweep(Image img, float after)
        {
            if (img == null) return;
            Tween.After(after, () => { if (img != null) Destroy(img.gameObject); }, img);
        }
    }
}
