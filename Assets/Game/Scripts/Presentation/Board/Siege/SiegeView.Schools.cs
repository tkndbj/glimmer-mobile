using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>How the Shuffle lane's schools look</b> (<c>SiegeBoard.Schools</c>, MODES.md 59r): every
    /// element card's hit drawn as itself - a focusing beam, a prism, venom dripping, ground on
    /// fire, a body bursting into flame, a raider frozen solid, static crawling between bodies, a
    /// thunderclap - and the five ascensions as set pieces that change the hill for as long as the
    /// run holds them: a laser fence and a sky laser, the dead rising as ghouls, a volcano and its
    /// lava river, an ice age, a lightning tempest.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, through the arsenal's doors</b> (<c>SiegeView.Arsenal</c>):
    /// reached from a hit whose <see cref="SiegeBolt.Via"/> a school reported, a
    /// <see cref="SiegeVolley"/> one reported, or <see cref="Schools"/>, which returns before it
    /// reads anything on a build holding no element card - and the plain line reports none of them.
    /// </para>
    /// <para>
    /// <b>Cheap where it repeats, built once where it stands</b>, for the arsenal's reason: a set
    /// piece is a handful of images made when it rises and moved every frame after; everything a
    /// hit throws is the batched particle layer (<c>ShuffleFx</c>), pooled embers and pooled darts;
    /// what lies on the ground has a canvas of its own (<c>_puddles</c>' reason); and a flourish
    /// that would stack on one spot is drawn once a frame per source (<see cref="Flourished"/>).
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the colours
        static readonly Color LaserHue = new Color(1f, .30f, .52f);
        static readonly Color LaserWhite = new Color(1f, .86f, .92f);
        static readonly Color VenomHue = new Color(.52f, 1f, .22f);
        static readonly Color VenomDeep = new Color(.08f, .26f, .04f);
        static readonly Color VoltHue = new Color(.72f, .62f, 1f);
        static readonly Color LavaHot = new Color(1f, .55f, .12f);
        static readonly Color LavaDeep = new Color(.42f, .06f, .02f);
        static readonly Color GhoulHue = new Color(.55f, 1f, .62f);

        // ------------------------------------------------------------------ the frame
        bool _schoolsUp;

        /// <summary>
        /// Keeps everything a school leaves standing drawn true, every frame. <b>Returns before it
        /// reads anything on a build holding no element card</b>, and tears down what it built when
        /// the board in front of it holds none (a run dealt again).
        /// </summary>
        void Schools(float dt)
        {
            if (_board == null || _fx == null) return;

            if (!_board.Boosts.AnySchool)
            {
                if (_schoolsUp) ScrapSchools();
                return;
            }

            _schoolsUp = true;

            Flames(dt);
            Afflictions(dt);
            Fence(dt);
            Reticles(dt);
            Ghosts(dt);
            Crater(dt);
            Glaciation(dt);
            Twister(dt);
        }

        void ScrapSchools()
        {
            _schoolsUp = false;

            foreach (var look in _flameArt.Values) if (look != null && look.Root) Destroy(look.Root.gameObject);
            _flameArt.Clear();
            Scrap(ref _embersLayer);

            foreach (var ghost in _ghosts.Values) if (ghost != null && ghost.Root) Destroy(ghost.Root.gameObject);
            _ghosts.Clear();

            Scrap(ref _fence);
            Scrap(ref _reticleHost);
            _reticles = null;
            Scrap(ref _crater);
            Scrap(ref _river);
            Scrap(ref _winter);
            ScrapTwister();

            for (int i = 0; i < _mob.Count; i++) Unafflict(_mob[i]);
        }

        // ------------------------------------------------------------------ a hit a school dealt
        /// <summary>Draws one hit a school reported, answering whether it was one. Each lands its own number through <paramref name="land"/>.</summary>
        bool SchoolHit(SiegeBolt shot, Mob mob, Vector2 to, Vector2 source, Color tint, System.Action land)
        {
            switch (shot.Via)
            {
                case SiegeVia.Focus:
                {
                    // One beam a ward a frame, as thick as the focus is deep.
                    if (!Flourished(SiegeVia.Focus, shot.Ward))
                    {
                        int focus = Mathf.Max(1, _board.FocusOf(shot.Ward));
                        Lase(Muzzle(shot.Ward), to, Color.Lerp(LaserHue, LaserWhite, focus * .12f),
                             .05f + .022f * focus, 0f, focus >= 3);
                    }

                    FxPop(to, LaserWhite, 1.1f + .1f * _board.FocusOf(shot.Ward), .18f);
                    land();
                    return true;
                }

                case SiegeVia.Refract:
                {
                    if (!Flourished(SiegeVia.Refract, shot.From))
                    {
                        Flash(source, Color.white, 2.4f);
                        Twinkles(source, LaserWhite, 5, Cell * .8f);
                        Audio.SfxVaried("arc", .22f, .2f);
                    }

                    var hue = Spectrum[(shot.Raider & 0x7fff) % Spectrum.Length];
                    Lase(source, to, hue, .1f, 0f, false);
                    FxPop(to, hue, 1.5f, .2f);
                    land();
                    return true;
                }

                case SiegeVia.Venom:
                    FxPop(to + new Vector2(0f, Cell * .2f), VenomHue, .9f, .2f);
                    Cinder(to + new Vector2(Random.Range(-.2f, .2f) * Cell, Cell * .3f), VenomHue, Cell * .3f, .5f,
                           new Vector2(0f, Cell * .55f));
                    land();
                    return true;

                case SiegeVia.Flame:
                    FxPop(to, Pal.Ember, 1.1f, .2f);
                    Cinder(to, Pal.Sun, Cell * .35f, .4f, new Vector2(0f, Cell * .9f));
                    land();
                    return true;

                case SiegeVia.Combust:
                    FxPop(to, Pal.Sun, 1.6f, .22f);
                    land();
                    return true;

                case SiegeVia.Static:
                    if (shot.From >= 0) Arc(source, to, VoltHue, Cell * .06f, .22f, 1, .42f);
                    else FxSparks(_fx, to, VoltHue, 8, Cell * 1.4f, Cell * .12f, .26f);
                    FxPop(to, VoltHue, 1.4f, .2f);
                    if (!Flourished(SiegeVia.Static, shot.Ward)) Audio.SfxVaried("zap", .28f, .12f);
                    land();
                    return true;

                case SiegeVia.Thunderclap:
                    FxPop(to, StormHue, 1.5f, .22f);
                    land();
                    return true;

                case SiegeVia.Jolt:
                    Arc(source, to, StormHue, Cell * .05f, .18f, 0, .32f);
                    FxPop(to, StormHue, 1.1f, .16f);
                    land();
                    return true;

                case SiegeVia.Grid:
                    FxPop(to, LaserHue, 1.1f, .16f);
                    FxSparks(_fx, new Vector2(to.x, FenceY), LaserWhite, 4, Cell * 1.2f, Cell * .1f, .22f);
                    land();
                    return true;

                case SiegeVia.Orbital:
                    Flash(to, LaserHue, 2.4f);
                    land();
                    return true;

                case SiegeVia.Ghoul:
                    FxPop(to, VenomHue, 1.5f, .22f);
                    land();
                    return true;

                case SiegeVia.Lava:
                    FxPop(to, LavaHot, 1.3f, .2f);
                    Cinder(to, Pal.Sun, Cell * .4f, .45f, new Vector2(Random.Range(-.3f, .3f) * Cell, Cell));
                    land();
                    return true;

                case SiegeVia.IceAge:
                    if (shot.From >= 0) FxPop(to, Pal.Glass, 1.3f, .2f);
                    land();
                    return true;

                case SiegeVia.Tempest:
                {
                    var eye = TwisterEye;
                    Arc(eye, to, StormHue, Cell * .07f, .22f, 1, .4f);
                    FxPop(to, Color.Lerp(StormHue, Color.white, .4f), 1.5f, .2f);
                    land();
                    return true;
                }

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ a place a school set off
        Vector2 _leapFrom, _sporeFrom;

        /// <summary>Draws one <see cref="SiegeVolley"/> a school reported, answering whether it was one.</summary>
        bool SchoolVolley(SiegeVolley volley)
        {
            var at = new Vector2(LaneX(volley.Lane), MarchY(volley.March));

            switch (volley.Via)
            {
                case SiegeVia.Contagion:
                    if (volley.Reach > 0)
                    {
                        _sporeFrom = at;
                        Flash(at, VenomHue, 2.2f);
                        Ripple(at, VenomHue, Cell * 3.6f, .42f, 0f);
                        Audio.SfxVaried("burst", .22f, .15f);
                    }
                    else
                    {
                        var from = _sporeFrom;
                        Fly(from, at, VenomHue, Cell * .34f, .32f, Random.value < .5f ? .3f : -.3f, VenomHue, () =>
                        {
                            if (this == null || _fx == null) return;
                            FxPop(at, VenomHue, 1.3f, .2f);
                            FxSparks(_fx, at, VenomHue, 5, Cell * 1.2f, Cell * .12f, .3f);
                        });
                    }
                    return true;

                case SiegeVia.Inferno:
                    if (volley.Reach > 0) _leapFrom = at;
                    else
                    {
                        var from = _leapFrom;
                        Fly(from, at, Pal.Sun, Cell * .42f, .26f, Random.value < .5f ? .45f : -.45f, Pal.Ember, () =>
                        {
                            if (this == null || _fx == null) return;
                            FxPop(at, Pal.Ember, 1.5f, .22f);
                            Embers(at, Pal.Ember, 5, Cell * .5f, Cell * .2f);
                        });
                        if (!Flourished(SiegeVia.Inferno, 0)) Audio.SfxVaried("whoosh", .2f, .15f);
                    }
                    return true;

                case SiegeVia.Freeze:
                    Ripple(at, Pal.Glass, Cell * 2.6f, .32f, 0f);
                    Twinkles(at, Pal.Glass, 4, Cell * .6f);
                    FxSparks(_fx, at, Pal.Glass, 8, Cell * 1.6f, Cell * .12f, .3f);
                    if (!Flourished(SiegeVia.Freeze, 0)) Audio.SfxVaried("shatter", .2f, .15f);
                    return true;

                case SiegeVia.Thunderclap:
                    Downpour(at);
                    Ripple(at, StormHue, Cell * 4.2f, .42f, .04f);
                    if (!Flourished(SiegeVia.Thunderclap, 0)) Audio.Sfx("thunder", .3f, Random.Range(1.05f, 1.25f));
                    return true;

                case SiegeVia.Combust:
                    Combusted(at);
                    return true;

                case SiegeVia.Orbital:
                    if (volley.In > 0f)
                    {
                        if (!Flourished(SiegeVia.Orbital, 0)) Audio.Sfx("charge", .45f, 1.35f);
                    }
                    else OrbitalStrike(at, volley.Reach);
                    return true;

                case SiegeVia.Ghoul:
                    if (volley.Reach > 0) GhoulBursts(at);
                    else GhoulRises(at);
                    return true;

                case SiegeVia.Volcano:
                    if (volley.In > 0f) Rumbles(at);
                    else Erupts(at);
                    return true;

                case SiegeVia.Lava:
                {
                    var box = BoxAt(Mathf.RoundToInt(volley.Lane), SiegeTuning.RowOf(volley.March));
                    if (volley.In > 0f) LavaBomb(box, volley.In);
                    else Impact(box, .6f);
                    return true;
                }

                case SiegeVia.IceAge:
                    if (volley.Reach > 0) Shattered(at);
                    else if (volley.In > 0f) WinterComes();
                    else WinterFalls();
                    return true;

                case SiegeVia.Tempest:
                    Lightup(StormHue, .14f, .5f);
                    Audio.Sfx("thunder", .55f, .8f);
                    Audio.Sfx("whoosh", .5f, .5f);
                    return true;

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ the ground on fire
        /// <summary>One flame on the ground, built once and moved every frame: a scorch, a glow, tongues of fire licking up.</summary>
        sealed class FlameLook
        {
            public RectTransform Root;
            public Image Scorch, Glow, Core;
            public Image[] Tongues;
            public float[] Phase;
            public float Age;
        }

        const int FlameTongues = 3;

        /// <summary>Seconds a flame takes to catch, and the seconds of its life it spends dying down.</summary>
        const float FlameCatches = .22f, FlameDies = .5f;

        /// <summary>The flames' own layer: on the ground under the raiders, with a canvas of its own (<c>_puddles</c>' reason).</summary>
        RectTransform _embersLayer;

        readonly Dictionary<int, FlameLook> _flameArt = new Dictionary<int, FlameLook>(8);
        readonly HashSet<int> _flamesSeen = new HashSet<int>();
        readonly List<int> _flamesGone = new List<int>(8);
        float _smoke;

        void Flames(float dt)
        {
            var flames = _board.Flames;
            _flamesSeen.Clear();

            _smoke -= dt;
            bool smoke = _smoke <= 0f;
            if (smoke) _smoke = .1f;

            if (flames.Count > 0 && !_embersLayer && _hill)
            {
                _embersLayer = UIKit.Node("Flames", _hill);
                _embersLayer.gameObject.AddComponent<Canvas>().overrideSorting = false;
            }

            float wide = BoxWide * .9f, tall = Mathf.Min(BoxTall * .6f, wide * .5f);
            float clock = Time.unscaledTime;

            for (int i = 0; i < flames.Count && _embersLayer; i++)
            {
                var flame = flames[i];
                int key = flame.Row * SiegeTuning.Lanes + flame.Lane;
                _flamesSeen.Add(key);

                var at = BoxAt(flame.Lane, flame.Row);

                if (!_flameArt.TryGetValue(key, out var look) || look == null || !look.Root)
                {
                    look = Kindling(at, wide, tall);
                    _flameArt[key] = look;
                    FxWave(at, Pal.Ember, 2.2f, .3f);
                }

                look.Age += dt;
                look.Root.anchoredPosition = at;

                float caught = Mathf.Clamp01(look.Age / FlameCatches);
                float dying = Mathf.Clamp01(flame.Left / FlameDies);
                float shown = Mathf.Min(caught, dying);
                look.Root.localScale = Vector3.one * (Ease.OutBack(caught) * Mathf.Lerp(.55f, 1f, dying));

                float flicker = .7f + .3f * Mathf.PerlinNoise(clock * 7f, key * 3.1f);
                look.Scorch.color = Pal.A(new Color(.12f, .04f, .02f), .5f * shown);
                look.Glow.color = Pal.A(Pal.Ember, (.45f + .25f * flicker) * shown);
                look.Core.color = Pal.A(Pal.Sun, (.35f + .3f * flicker) * shown);

                // Tongues of fire, each on its own beat: taller and narrower as they lick up.
                for (int t = 0; t < look.Tongues.Length; t++)
                {
                    var tongue = look.Tongues[t];
                    if (!tongue) continue;

                    float lick = Mathf.PerlinNoise(clock * (5.5f + t), look.Phase[t]);
                    var rt = tongue.rectTransform;
                    rt.sizeDelta = new Vector2(wide * (.26f - .05f * lick), Cell * (.7f + 1.1f * lick));
                    rt.anchoredPosition = new Vector2((t - 1) * wide * .22f + Mathf.Sin(clock * 3f + t) * wide * .03f,
                                                      -tall * .1f);
                    tongue.color = Pal.A(Color.Lerp(Pal.Ember, Pal.Sun, lick), (.55f + .4f * lick) * shown);
                }

                if (smoke && Random.value < .6f * shown)
                    Cinder(at + new Vector2(Random.Range(-wide, wide) * .35f, tall * .2f),
                           Random.value < .5f ? Pal.Sun : Pal.Ember, Cell * Random.Range(.22f, .36f), .6f,
                           new Vector2(Random.Range(-.2f, .2f) * Cell, Cell * Random.Range(1f, 1.6f)));
            }

            if (_flameArt.Count == _flamesSeen.Count) return;

            _flamesGone.Clear();
            foreach (var pair in _flameArt) if (!_flamesSeen.Contains(pair.Key)) _flamesGone.Add(pair.Key);
            foreach (int key in _flamesGone)
            {
                var look = _flameArt[key];
                if (look != null && look.Root) Destroy(look.Root.gameObject);
                _flameArt.Remove(key);
            }
        }

        FlameLook Kindling(Vector2 at, float wide, float tall)
        {
            var look = new FlameLook { Root = UIKit.Node("Flame", _embersLayer) };
            look.Root.anchoredPosition = at;
            look.Root.localScale = Vector3.zero;

            // A scorch in ordinary blending, so the fire reads on a bright hill where light alone vanishes.
            look.Scorch = UIKit.Img("Scorch", look.Root, Art.Glow(128, .7f), Color.clear, new Vector2(wide * 1.05f, tall * 1.1f));
            look.Scorch.raycastTarget = false;

            look.Glow = Lit("Glow", look.Root, Art.Glow(128, 1.2f), Color.clear, new Vector2(wide * 1.3f, tall * 1.8f));
            look.Core = Lit("Core", look.Root, Art.Glow(96, 2f), Color.clear, new Vector2(wide * .7f, tall * .7f));

            look.Tongues = new Image[FlameTongues];
            look.Phase = new float[FlameTongues];
            for (int t = 0; t < FlameTongues; t++)
            {
                var tongue = Lit("Tongue", look.Root, Art.Shaft(64, 128, 1.5f, .08f), Color.clear, Vector2.one);
                tongue.rectTransform.pivot = new Vector2(.5f, 0f);
                look.Tongues[t] = tongue;
                look.Phase[t] = Random.Range(0f, 50f);
            }

            return look;
        }

        /// <summary>A body that burned long enough bursting: a fireball, a flash, a ring on the ground and burning debris.</summary>
        void Combusted(Vector2 at)
        {
            Boom(at, Blast("boom_fire"), Cell * 3.4f);
            Fireball(at, .55f, .55f);
            Flash(at, Pal.Sun, 3.2f);
            Ripple(at, Pal.Ember, Cell * 4.6f, .45f, 0f);
            Ripple(at, Pal.Sun, Cell * 3f, .36f, .05f);
            Embers(at, Pal.Ember, 14, Cell * .9f, Cell * .3f);
            ShakeBoard(Cell * .1f);
            if (!Flourished(SiegeVia.Combust, 0)) Audio.SfxVaried("boom", .4f, .1f);
        }

        // ------------------------------------------------------------------ venom and ice on a body
        /// <summary>Venom and ice drawn on each body: a sickly glow and drips while venom is on it, a crystal round it while it is frozen solid.</summary>
        void Afflictions(float dt)
        {
            float clock = Time.unscaledTime;
            int drips = 0;

            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null) continue;

                var raider = mob.Falling ? null : _board.Find(mob.Id);

                // ---- venom
                bool venom = raider != null && raider.Envenomed;
                if (venom)
                {
                    if (mob.Venom == null)
                    {
                        SealOf(mob, out float seat, out float wide);
                        mob.Venom = Lit("Venom", mob.Node, Art.Glow(96, 1.3f), Color.clear, new Vector2(wide, wide * .8f));
                        mob.Venom.rectTransform.anchoredPosition = new Vector2(0f, seat);
                        mob.Venom.transform.SetSiblingIndex(mob.Shadow != null ? mob.Shadow.transform.GetSiblingIndex() + 1 : 0);
                    }

                    float dose = Mathf.Clamp01(raider.Venom / 6f);
                    float throb = .75f + .25f * Mathf.Sin(clock * (5f + 3f * dose) + mob.Id);
                    mob.Venom.color = Pal.A(Color.Lerp(VenomHue, Pal.Pollen, .15f), (.32f + .4f * dose) * throb);
                    mob.Venom.rectTransform.localScale = Vector3.one * (.9f + .25f * dose);

                    mob.VenomDrip -= dt;
                    if (mob.VenomDrip <= 0f && drips < 6)
                    {
                        drips++;
                        mob.VenomDrip = Mathf.Lerp(.55f, .2f, dose);
                        var top = mob.Node.anchoredPosition + new Vector2(Random.Range(-.25f, .25f) * mob.Height, mob.Height * .45f);
                        Cinder(top, Random.value < .3f ? Pal.Pollen : VenomHue, Cell * Random.Range(.18f, .28f), .55f,
                               new Vector2(0f, Cell * Random.Range(.35f, .6f)));
                    }
                }
                else if (mob.Venom != null)
                {
                    Destroy(mob.Venom.gameObject);
                    mob.Venom = null;
                }

                // ---- ice
                float encased = raider != null ? raider.Encased : 0f;
                if (encased > 0f)
                {
                    if (mob.Ice == null)
                    {
                        SealOf(mob, out float seat, out float wide);
                        float tall = mob.Height * BodyFill(mob.Boss);
                        mob.Ice = Lit("Ice", mob.Node, Art.Crystal(128), Color.clear, new Vector2(wide * .78f, tall * 1.18f));
                        mob.Ice.rectTransform.anchoredPosition = new Vector2(0f, seat + tall * .5f);
                        mob.Ice.transform.SetAsLastSibling();
                        mob.IceGlow = Lit("Ice glow", mob.Node, Art.Glow(96, 1.6f), Color.clear, new Vector2(wide * 1.1f, tall * 1.3f));
                        mob.IceGlow.rectTransform.anchoredPosition = new Vector2(0f, seat + tall * .45f);
                        mob.Iced = 0f;
                        FxSparks(_fx, mob.Node.anchoredPosition + new Vector2(0f, tall * .5f), Pal.Glass, 6, Cell * 1.2f, Cell * .1f, .28f);
                    }

                    mob.Iced += dt;
                    float set = Ease.OutBack(Mathf.Clamp01(mob.Iced / .14f));
                    float thaw = Mathf.Clamp01(encased / .2f);
                    float glint = .5f + .5f * Mathf.Sin(clock * 6f + mob.Id * 1.7f);

                    mob.Ice.rectTransform.localScale = new Vector3(set, set, 1f);
                    mob.Ice.color = Pal.A(Color.Lerp(Pal.Glass, Color.white, .25f * glint), .78f * thaw);
                    if (mob.IceGlow != null) mob.IceGlow.color = Pal.A(FrostHue, (.28f + .12f * glint) * thaw);
                }
                else if (mob.Ice != null)
                {
                    // **The thaw cracks**: the crystal breaks off in a puff of ice.
                    FxSparks(_fx, mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .45f), Pal.Glass, 7, Cell * 1.5f, Cell * .12f, .32f);
                    Destroy(mob.Ice.gameObject);
                    mob.Ice = null;
                    if (mob.IceGlow != null) Destroy(mob.IceGlow.gameObject);
                    mob.IceGlow = null;
                }
            }
        }

        static void Unafflict(Mob mob)
        {
            if (mob == null) return;
            if (mob.Venom != null) Destroy(mob.Venom.gameObject);
            if (mob.Ice != null) Destroy(mob.Ice.gameObject);
            if (mob.IceGlow != null) Destroy(mob.IceGlow.gameObject);
            mob.Venom = mob.Ice = mob.IceGlow = null;
        }

        // ------------------------------------------------------------------ the laser grid
        /// <summary>The fence: three layers of light across the hill, a node over every lane, a pylon off every post, sparks running along it.</summary>
        RectTransform _fence;

        Image _fenceHalo, _fenceSheath, _fenceCore;
        Image[] _fenceNodes, _pylons, _runners;
        float _fenceAge;

        const int FenceRunners = 3;

        float FenceY => MarchY(SiegeSchools.GridMarch);

        void Fence(float dt)
        {
            if (!_board.Holds(SiegePower.LaserGrid) || _posts == null) { Scrap(ref _fence); return; }

            float wide = Span.x;

            if (!_fence)
            {
                _fence = UIKit.Node("Laser grid", _fx);
                _fence.SetAsFirstSibling();

                _fenceHalo = Lit("Halo", _fence, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
                _fenceSheath = Lit("Sheath", _fence, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
                _fenceCore = Lit("Core", _fence, Art.Capsule(24, 96), Color.clear, Vector2.one);
                foreach (var layer in new[] { _fenceHalo, _fenceSheath, _fenceCore })
                    layer.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                _pylons = new Image[_posts.Length];
                for (int w = 0; w < _pylons.Length; w++)
                    _pylons[w] = Lit("Pylon", _fence, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);

                _fenceNodes = new Image[SiegeTuning.Lanes];
                for (int x = 0; x < _fenceNodes.Length; x++)
                    _fenceNodes[x] = Lit("Node", _fence, Art.Glint(96, 4), Color.clear, Vector2.one * Cell * .8f);

                _runners = new Image[FenceRunners];
                for (int r = 0; r < FenceRunners; r++)
                    _runners[r] = Lit("Runner", _fence, Art.Glow(96, 2.4f), Color.clear, new Vector2(Cell * 1.4f, Cell * .55f));

                _fenceAge = 0f;
                Lightup(LaserHue, .12f, .45f);
                Twinkles(new Vector2(0f, FenceY), LaserWhite, 12, wide * .45f);
            }

            _fenceAge += dt;
            float grown = Mathf.Clamp01(_fenceAge / .5f);
            float y = FenceY;
            float pulse = .8f + .2f * Mathf.Sin(_fenceAge * 9f);

            // The fence grows out from the middle to both edges when it is first raised.
            float span = wide * Ease.OutQuint(grown);
            void Lay(Image img, float across, Color c, float a)
            {
                if (!img) return;
                img.rectTransform.anchoredPosition = new Vector2(0f, y);
                img.rectTransform.sizeDelta = new Vector2(across, span);
                img.color = Pal.A(c, a);
            }

            Lay(_fenceHalo, Cell * .95f * pulse, LaserHue, .38f * grown);
            Lay(_fenceSheath, Cell * .3f * pulse, LaserHue, .85f * grown);
            Lay(_fenceCore, Cell * .09f, LaserWhite, grown);

            for (int x = 0; x < _fenceNodes.Length; x++)
            {
                var node = _fenceNodes[x];
                if (!node) continue;

                float beat = .6f + .4f * Mathf.Sin(_fenceAge * 7f + x * 1.3f);
                node.rectTransform.anchoredPosition = new Vector2(LaneX(x), y);
                node.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _fenceAge * 60f + x * 18f);
                node.rectTransform.localScale = Vector3.one * (.8f + .35f * beat);
                node.color = Pal.A(Color.Lerp(LaserHue, Color.white, .5f), grown * beat);
            }

            // A pylon of light off every standing post's muzzle up into the fence: the line is the grid.
            for (int w = 0; w < _pylons.Length; w++)
            {
                var pylon = _pylons[w];
                if (!pylon) continue;

                bool standing = w < _board.Wards.Count && _board.Wards[w].Alive;
                if (!standing) { pylon.color = Color.clear; continue; }

                var foot = Muzzle(w);
                var top = new Vector2(Mathf.Lerp(foot.x, LaneX(Mathf.Clamp(Mathf.RoundToInt((w + .5f) * SiegeTuning.Lanes / _pylons.Length - .5f), 0, SiegeTuning.Lanes - 1)), .5f), y);
                var d = top - foot;
                var rt = pylon.rectTransform;
                rt.anchoredPosition = (foot + top) * .5f;
                rt.sizeDelta = new Vector2(Cell * .22f, d.magnitude);
                rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90f);
                pylon.color = Pal.A(LaserHue, (.3f + .15f * Mathf.Sin(_fenceAge * 11f + w)) * grown);
            }

            // Sparks of light running along it, to and fro.
            for (int r = 0; r < _runners.Length; r++)
            {
                var runner = _runners[r];
                if (!runner) continue;

                float u = Mathf.PingPong(_fenceAge * (.55f + r * .23f) + r * .37f, 1f);
                runner.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-wide * .5f, wide * .5f, u), y);
                runner.color = Pal.A(LaserWhite, .9f * grown);
            }
        }

        // ------------------------------------------------------------------ the sky laser
        /// <summary>One reticle closing on a body the sky laser has marked.</summary>
        sealed class Reticle
        {
            public RectTransform Root;
            public Image Ring, Inner, Guide;
            public Image[] Ticks;
        }

        RectTransform _reticleHost;
        Reticle[] _reticles;

        void Reticles(float dt)
        {
            if (!_board.Holds(SiegePower.LaserGrid)) { Scrap(ref _reticleHost); _reticles = null; return; }

            var marks = _board.OrbitalMarks;
            float charge = _board.OrbitalCharge;

            if (marks.Count == 0)
            {
                if (_reticleHost) _reticleHost.gameObject.SetActive(false);
                return;
            }

            if (!_reticleHost)
            {
                _reticleHost = UIKit.Node("Sky laser", _fx);
                _reticles = new Reticle[SiegeSchools.OrbitalStrikes];

                for (int i = 0; i < _reticles.Length; i++)
                {
                    var r = new Reticle { Root = UIKit.Node("Reticle", _reticleHost) };
                    r.Guide = Lit("Guide", r.Root, Art.Shaft(64, 64, 2f, .3f), Color.clear, Vector2.one);
                    r.Guide.rectTransform.pivot = new Vector2(.5f, 0f);
                    r.Ring = Lit("Ring", r.Root, Art.Ring(128, 7f), Color.clear, Vector2.one * Cell * 2f);
                    r.Inner = Lit("Inner", r.Root, Art.HexRing(128, 8f), Color.clear, Vector2.one * Cell * 1.2f);
                    r.Ticks = new Image[4];
                    for (int t = 0; t < 4; t++) r.Ticks[t] = Lit("Tick", r.Root, Art.Capsule(24, 96), Color.clear, new Vector2(Cell * .1f, Cell * .42f));
                    _reticles[i] = r;
                }
            }

            _reticleHost.gameObject.SetActive(true);
            float clock = Time.unscaledTime;

            for (int i = 0; i < _reticles.Length; i++)
            {
                var r = _reticles[i];
                var mob = i < marks.Count ? MobOf(marks[i]) : null;
                bool on = mob != null && mob.Node != null && !mob.Falling;

                r.Root.gameObject.SetActive(on);
                if (!on) continue;

                var at = mob.Node.anchoredPosition + new Vector2(0f, mob.Height * .4f);
                r.Root.anchoredPosition = at;

                float close = Ease.OutQuint(charge);
                float scale = Mathf.Lerp(1.9f, .85f, close);
                float blink = charge > .7f ? .6f + .4f * Mathf.Abs(Mathf.Sin(clock * 34f)) : 1f;

                r.Ring.rectTransform.localScale = Vector3.one * scale;
                r.Ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, clock * 140f + i * 40f);
                r.Ring.color = Pal.A(LaserHue, .85f * blink);

                r.Inner.rectTransform.localScale = Vector3.one * (scale * .8f);
                r.Inner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -clock * 220f);
                r.Inner.color = Pal.A(LaserWhite, .7f * close * blink);

                for (int t = 0; t < r.Ticks.Length; t++)
                {
                    float ang = t * 90f + clock * 140f + i * 40f;
                    var dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                    var tick = r.Ticks[t].rectTransform;
                    tick.anchoredPosition = dir * Cell * 1.05f * scale;
                    tick.localRotation = Quaternion.Euler(0f, 0f, ang - 90f);
                    r.Ticks[t].color = Pal.A(Color.white, .9f * blink);
                }

                // The guide beam from the sky, a thread that thickens as it charges.
                float sky = Span.y * .5f + Cell - at.y;
                r.Guide.rectTransform.anchoredPosition = Vector2.zero;
                r.Guide.rectTransform.sizeDelta = new Vector2(Cell * Mathf.Lerp(.08f, .5f, close), sky);
                r.Guide.color = Pal.A(LaserHue, .55f * close * blink);
            }
        }

        /// <summary>The sky laser striking: a column of light out of the sky, white at its heart, and the ground answering.</summary>
        void OrbitalStrike(Vector2 at, int ordinal)
        {
            if (_fx == null) return;

            float top = Span.y * .5f + Cell * 2f;
            float tall = top - at.y;

            var host = UIKit.Box("Orbital", _fx, Vector2.zero, new Vector2(.5f, .5f), at);
            var halo = Lit("Halo", host, Art.Shaft(64, 64, 1.1f, .05f), Color.clear, new Vector2(Cell * 3f, tall));
            var beam = Lit("Beam", host, Art.Shaft(64, 64, 1.8f, .05f), Color.clear, new Vector2(Cell * 1.3f, tall));
            var core = Lit("Core", host, Art.Shaft(64, 64, 3f, .05f), Color.clear, new Vector2(Cell * .45f, tall));
            foreach (var img in new[] { halo, beam, core })
            {
                img.rectTransform.pivot = new Vector2(.5f, 0f);
                img.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .2f);
            }

            const float Life = .55f;
            float delay = ordinal * .07f;

            Tween.Run(Life, Ease.Linear, t =>
            {
                if (!host) return;

                float punch = t < .12f ? Ease.OutQuint(t / .12f) : 1f - Ease.InCubic((t - .12f) / .88f);
                float wobble = 1f + .12f * Mathf.Sin(t * 70f);
                halo.color = Pal.A(LaserHue, .55f * punch);
                beam.color = Pal.A(Color.Lerp(LaserHue, Color.white, .4f), .9f * punch);
                core.color = Pal.A(Color.white, punch);
                halo.rectTransform.localScale = new Vector3(wobble * (1f + .4f * (1f - punch)), 1f, 1f);
                beam.rectTransform.localScale = new Vector3(wobble * punch, 1f, 1f);
                core.rectTransform.localScale = new Vector3(Mathf.Max(.15f, punch), 1f, 1f);
            }, host).Delay(delay).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(Life + delay + .1f, () => { if (host) Destroy(host.gameObject); }, host);

            Tween.After(delay, () =>
            {
                if (this == null || _fx == null) return;

                Flash(at, LaserHue, 3.8f);
                Answer(at, LaserHue, 3.2f, StrikeFx.Crack, 2.6f);
                Ripple(at, LaserHue, Cell * 5.4f, .5f, 0f);
                Ripple(at, Color.white, Cell * 3.2f, .36f, .05f);
                Embers(at, LaserHue, 16, Cell * 1.4f, Cell * .28f);
                Twinkles(at, LaserWhite, 6, Cell * 1.3f);
                ShakeBoard(Cell * .16f);
                if (!Flourished(SiegeVia.Orbital, 1)) { Lightup(LaserHue, .14f, .35f); Audio.Sfx("boom", .5f, 1.25f); Audio.SfxVaried("zap", .5f); }
            }, _fx);
        }

        // ------------------------------------------------------------------ necromancy
        /// <summary>A ghoul as drawn: the body it rose out of, washed spectral green and turned up the hill, in a glow, trailing wisps.</summary>
        sealed class Ghost
        {
            public RectTransform Root;
            public Image Body, Aura, Halo;
            public float Age, Wisp;
        }

        readonly Dictionary<int, Ghost> _ghosts = new Dictionary<int, Ghost>(SiegeSchools.MostGhouls);
        readonly HashSet<int> _ghostsSeen = new HashSet<int>();
        readonly List<int> _ghostsGone = new List<int>(SiegeSchools.MostGhouls);

        void Ghosts(float dt)
        {
            var ghouls = _board.Ghouls;
            _ghostsSeen.Clear();
            float clock = Time.unscaledTime;

            for (int i = 0; i < ghouls.Count; i++)
            {
                var g = ghouls[i];
                _ghostsSeen.Add(g.Id);

                if (!_ghosts.TryGetValue(g.Id, out var ghost) || ghost == null || !ghost.Root)
                {
                    ghost = Summon(g);
                    _ghosts[g.Id] = ghost;
                }

                ghost.Age += dt;
                var at = new Vector2(LaneX(g.Lane), MarchY(g.March));
                float bob = Mathf.Sin(clock * 7f + g.Id) * Cell * .08f;
                ghost.Root.anchoredPosition = at + new Vector2(0f, bob);

                float rise = Ease.OutBack(Mathf.Clamp01(ghost.Age / .3f));
                float fade = Mathf.Clamp01(g.Life / .15f);
                ghost.Root.localScale = Vector3.one * rise;

                float flicker = .8f + .2f * Mathf.Sin(clock * 23f + g.Id * 2f);
                if (ghost.Body) ghost.Body.color = Pal.A(GhoulHue, .82f * fade * flicker);
                if (ghost.Aura) ghost.Aura.color = Pal.A(VenomHue, .45f * fade);
                if (ghost.Halo)
                {
                    ghost.Halo.rectTransform.localRotation = Quaternion.Euler(0f, 0f, clock * 90f + g.Id * 30f);
                    ghost.Halo.color = Pal.A(Color.Lerp(VenomHue, Color.white, .3f), .5f * fade);
                }

                ghost.Wisp -= dt;
                if (ghost.Wisp > 0f) continue;

                ghost.Wisp = .06f;
                Cinder(at + new Vector2(Random.Range(-.25f, .25f) * Cell, -Cell * .2f),
                       Random.value < .35f ? Color.white : VenomHue, Cell * Random.Range(.25f, .4f), .45f,
                       new Vector2(Random.Range(-.2f, .2f) * Cell, -Cell * .6f));
            }

            if (_ghosts.Count == _ghostsSeen.Count) return;

            _ghostsGone.Clear();
            foreach (var pair in _ghosts) if (!_ghostsSeen.Contains(pair.Key)) _ghostsGone.Add(pair.Key);
            foreach (int key in _ghostsGone)
            {
                var ghost = _ghosts[key];
                if (ghost != null && ghost.Root) Destroy(ghost.Root.gameObject);
                _ghosts.Remove(key);
            }
        }

        Ghost Summon(SiegeGhoul g)
        {
            var ghost = new Ghost { Root = UIKit.Node("Ghoul", _fx) };

            // **The body it rose out of**, taken off the fallen raider's own widget while it is still
            // on the board, so a ghoul is that raider's ghost and never a stock picture.
            var from = MobOf(g.From);
            Sprite sprite = from != null && from.Body != null ? from.Body.sprite : null;
            float tall = from != null ? from.Height * .9f : Cell * 1.1f;

            ghost.Aura = Lit("Aura", ghost.Root, Art.Glow(128, 1.4f), Color.clear, Vector2.one * tall * 1.7f);
            ghost.Halo = Lit("Halo", ghost.Root, Art.Ring(128, 6f), Color.clear, new Vector2(tall * 1.2f, tall * .5f));
            ghost.Halo.rectTransform.anchoredPosition = new Vector2(0f, -tall * .35f);

            if (sprite != null)
            {
                float aspect = sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;
                ghost.Body = Lit("Body", ghost.Root, sprite, Color.clear, new Vector2(tall * aspect, tall));
                ghost.Body.preserveAspect = true;

                // Turned to climb the hill rather than walk down it.
                ghost.Body.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            }
            else ghost.Body = Lit("Body", ghost.Root, Art.Glow(96, 2.6f), Color.clear, Vector2.one * tall);

            ghost.Wisp = 0f;
            return ghost;
        }

        /// <summary>The dead rising: the ground bursts green under a fallen body, and its ghost stands up out of it.</summary>
        void GhoulRises(Vector2 at)
        {
            Flash(at, VenomHue, 2.4f);
            Ripple(at, VenomHue, Cell * 3.4f, .45f, 0f);
            Answer(at, VenomHue, 2.2f, StrikeFx.Crack, 1.8f, .7f);

            for (int i = 0; i < 8; i++)
                Cinder(at + new Vector2(Random.Range(-.4f, .4f) * Cell, 0f), i % 3 == 0 ? Color.white : VenomHue,
                       Cell * Random.Range(.28f, .42f), .6f, new Vector2(Random.Range(-.3f, .3f) * Cell, Cell * Random.Range(1f, 1.8f)));

            if (!Flourished(SiegeVia.Ghoul, 0)) Audio.Sfx("voidblast", .32f, 1.5f);
        }

        /// <summary>A ghoul bursting on the horde: a green blast, spores flung out, the ground poisoned.</summary>
        void GhoulBursts(Vector2 at)
        {
            Boom(at, Blast("boom_smoke"), Cell * 3.4f, 22f);
            Flash(at, VenomHue, 3.2f);
            Ripple(at, VenomHue, Cell * 5f, .5f, 0f);
            Ripple(at, Pal.Pollen, Cell * 3.2f, .4f, .06f);
            FxSparks(_fx, at, VenomHue, 12, Cell * 2.4f, Cell * .16f, .45f);
            Embers(at, VenomHue, 10, Cell * .9f, Cell * .26f);
            ShakeBoard(Cell * .1f);
            if (!Flourished(SiegeVia.Ghoul, 1)) Audio.SfxVaried("burst", .45f, .12f);
        }

        // ------------------------------------------------------------------ the volcano
        /// <summary>The crater as drawn: a crack in the ground glowing hotter as it rumbles. And the river: lava down a lane.</summary>
        RectTransform _crater, _river;

        Image _craterCrack, _craterGlow, _craterHeat;
        Image _riverBed, _riverGlow, _riverCore;
        float _craterAge, _quake, _riverShown, _riverDrip;

        void Crater(float dt)
        {
            if (!_board.Holds(SiegePower.Volcano) || _hill == null)
            {
                Scrap(ref _crater);
                Scrap(ref _river);
                return;
            }

            // ---- the crater, while it rumbles
            if (_board.VolcanoRumbling)
            {
                var at = BoxAt(_board.CraterLane, _board.CraterRow);

                if (!_crater)
                {
                    _crater = UIKit.Node("Crater", _hill);
                    _craterGlow = Lit("Glow", _crater, Art.Glow(128, 1.2f), Color.clear, new Vector2(BoxWide * 1.9f, BoxWide * .9f));
                    _craterHeat = Lit("Heat", _crater, Art.Glow(96, 2.4f), Color.clear, new Vector2(BoxWide * .9f, BoxWide * .45f));
                    _craterCrack = Lit("Crack", _crater, StrikeFx.Crack, Color.clear, new Vector2(BoxWide * 1.6f, BoxWide * 1.6f * GroundSquash));
                    _craterAge = 0f;
                }

                _crater.gameObject.SetActive(true);
                _craterAge += dt;
                float heat = Mathf.Clamp01(_craterAge / SiegeSchools.VolcanoRumble);
                float flick = .75f + .25f * Mathf.PerlinNoise(Time.unscaledTime * 12f, 3.7f);

                foreach (var img in new[] { _craterGlow, _craterHeat, _craterCrack })
                    if (img) img.rectTransform.anchoredPosition = at;

                if (_craterGlow) _craterGlow.color = Pal.A(LavaHot, .55f * heat * flick);
                if (_craterHeat) _craterHeat.color = Pal.A(Pal.Sun, .8f * heat * flick);
                if (_craterCrack)
                {
                    _craterCrack.color = Pal.A(Color.Lerp(LavaHot, Pal.Sun, heat), heat);
                    _craterCrack.rectTransform.localScale = Vector3.one * Mathf.Lerp(.4f, 1f, Ease.OutQuint(heat));
                }

                _quake -= dt;
                if (_quake <= 0f)
                {
                    _quake = .1f;
                    ShakeBoard(Cell * .035f * (.5f + heat));
                    Cinder(at + new Vector2(Random.Range(-.6f, .6f) * BoxWide, 0f), Random.value < .5f ? Pal.Hollow : LavaHot,
                           Cell * Random.Range(.35f, .6f), .8f, new Vector2(Random.Range(-.2f, .2f) * Cell, Cell * 1.4f));
                }
            }
            else if (_crater) _crater.gameObject.SetActive(false);

            // ---- the river
            bool flowing = _board.RiverLeft > 0f;
            _riverShown = Mathf.MoveTowards(_riverShown, flowing ? 1f : 0f, dt * (flowing ? 4f : 2.5f));

            if (_riverShown <= 0f)
            {
                if (_river) _river.gameObject.SetActive(false);
                return;
            }

            float x = LaneX(_board.RiverLane);
            float from = MarchY((float)_board.RiverFrom / SiegeTuning.BlastRows);
            float to = _lineY + Cell * .2f;
            float length = Mathf.Max(Cell, from - to);

            if (!_river)
            {
                _river = UIKit.Node("Lava river", _hill);
                _river.gameObject.AddComponent<Canvas>().overrideSorting = false;
                _riverBed = UIKit.Img("Bed", _river, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
                _riverBed.raycastTarget = false;
                _riverGlow = Lit("Glow", _river, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
                _riverCore = Lit("Core", _river, Art.Capsule(24, 96), Color.clear, Vector2.one);
            }

            _river.gameObject.SetActive(true);
            float churn = .8f + .2f * Mathf.Sin(Time.unscaledTime * 6f);
            var mid = new Vector2(x, (from + to) * .5f);

            void Lay(Image img, float across, Color c, float a)
            {
                if (!img) return;
                img.rectTransform.anchoredPosition = mid;
                img.rectTransform.sizeDelta = new Vector2(across, length + across * .3f);
                img.color = Pal.A(c, a * _riverShown);
            }

            Lay(_riverBed, BoxWide * .78f, LavaDeep, .75f);
            Lay(_riverGlow, BoxWide * 1.15f, LavaHot, .55f * churn);
            Lay(_riverCore, BoxWide * .28f, Pal.Sun, .85f * churn);

            // Hot blobs flowing down it to the line.
            _riverDrip -= dt;
            if (_riverDrip <= 0f && flowing)
            {
                _riverDrip = .05f;
                Cinder(new Vector2(x + Random.Range(-.3f, .3f) * BoxWide, from), Random.value < .5f ? Pal.Sun : LavaHot,
                       Cell * Random.Range(.35f, .55f), .7f, new Vector2(0f, -(from - to) * 1.2f));
            }
        }

        /// <summary>The volcano warming: the ground rumbles under the crowd and the sky reddens.</summary>
        void Rumbles(Vector2 at)
        {
            Lightup(Pal.Ember, .1f, SiegeSchools.VolcanoRumble);
            Ripple(at, LavaHot, Cell * 4f, SiegeSchools.VolcanoRumble, 0f);
            Audio.Sfx("charge", .5f, .45f);
        }

        /// <summary>The eruption: a column of fire out of the crater, the blast, the ground thrown up, burning rock in the air.</summary>
        void Erupts(Vector2 at)
        {
            if (_fx == null) return;

            var host = UIKit.Box("Eruption", _fx, Vector2.zero, new Vector2(.5f, .5f), at);
            var column = Lit("Column", host, Art.Shaft(64, 128, 1.1f, .2f), Color.clear, new Vector2(Cell * 2.4f, Cell * 6f));
            var core = Lit("Core", host, Art.Shaft(64, 128, 2.2f, .2f), Color.clear, new Vector2(Cell * .9f, Cell * 6f));
            column.rectTransform.pivot = core.rectTransform.pivot = new Vector2(.5f, 0f);

            const float Life = 1.1f;
            float nextRock = 0f;

            Tween.Run(Life, Ease.Linear, t =>
            {
                if (!host) return;

                float s = t * Life;
                float rise = Ease.OutQuint(Mathf.Clamp01(s / .2f));
                float fade = 1f - Mathf.Clamp01((s - .45f) / .65f);
                column.rectTransform.sizeDelta = new Vector2(Cell * Mathf.Lerp(1.6f, 2.8f, t), Cell * 6f * rise);
                core.rectTransform.sizeDelta = new Vector2(Cell * .8f, Cell * 5.5f * rise);
                column.color = Pal.A(Color.Lerp(Pal.Sun, LavaHot, t), .85f * fade);
                core.color = Pal.A(Color.Lerp(Color.white, Pal.Sun, t), fade);

                // Burning rock thrown up out of the column and falling back.
                if (s >= nextRock && s < .7f)
                {
                    nextRock = s + .04f;
                    var lift = new Vector2(Random.Range(-1.6f, 1.6f) * Cell, Cell * Random.Range(4f, 6.5f));
                    Cinder(at + new Vector2(0f, Cell * Random.Range(.5f, 3f)), Random.value < .5f ? Pal.Sun : LavaHot,
                           Cell * Random.Range(.4f, .7f), .7f, lift);
                }
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(Life + .1f, () => { if (host) Destroy(host.gameObject); }, host);

            Boom(at, Blast("boom_fire"), Cell * 5.2f);
            Boom(at + new Vector2(0f, Cell * 3.2f), Blast("boom_smoke"), Cell * 5f, 18f);
            Flash(at, Pal.Sun, 5f);
            Lightup(Pal.Ember, .22f, .6f);
            Answer(at, LavaHot, 4f, StrikeFx.Crack, 3.4f);
            Ripple(at, LavaHot, Cell * 7f, .6f, 0f);
            Ripple(at, Pal.Sun, Cell * 4.4f, .45f, .06f);
            Embers(at, LavaHot, 24, Cell * 1.6f, Cell * .34f);
            ShakeBoard(Cell * .34f);

            Audio.Sfx("boom", .85f, .55f);
            Audio.Sfx("thunder", .45f, .6f);
        }

        /// <summary>A lava bomb thrown from the crater onto a box, landing as the rule lands it.</summary>
        void LavaBomb(Vector2 box, float over)
        {
            var crater = BoxAt(_board.CraterLane, _board.CraterRow) + new Vector2(0f, Cell * 2.4f);
            Fly(crater, box, Pal.Sun, Cell * .62f, over, Random.value < .5f ? .55f : -.55f, LavaHot, null);
        }

        // ------------------------------------------------------------------ the ice age
        /// <summary>The winter as drawn: the hill washed cold, frost creeping in at its edges, snow driving across it.</summary>
        RectTransform _winter;

        Image _winterWash, _winterSheet, _winterVignette;
        Image[] _winterGlints;
        float _winterShown, _winterFlash, _snow;

        const int WinterGlints = 14;

        void Glaciation(float dt)
        {
            if (!_board.Holds(SiegePower.IceAge)) { Scrap(ref _winter); return; }

            bool coming = _board.IceAgeComing;
            bool frozen = _board.IceAgeLeft > 0f;
            float want = frozen ? 1f : coming ? .55f : 0f;
            _winterShown = Mathf.MoveTowards(_winterShown, want, dt * (want > _winterShown ? 2.4f : 1.2f));
            _winterFlash = Mathf.MoveTowards(_winterFlash, 0f, dt * 2f);

            if (_winterShown <= 0f && _winterFlash <= 0f)
            {
                if (_winter) _winter.gameObject.SetActive(false);
                return;
            }

            if (!_winter)
            {
                _winter = UIKit.Node("Ice age", _fx);
                _winter.SetAsFirstSibling();
                _winterWash = Lit("Wash", _winter, Art.Round(4), Color.clear, Span);
                _winterSheet = Lit("Sheet", _winter, Art.FadeUp(64), Color.clear, Vector2.one);
                _winterVignette = Lit("Frost", _winter, Art.Vignette(256), Color.clear, Span * 1.02f);
                _winterGlints = new Image[WinterGlints];
                for (int i = 0; i < WinterGlints; i++)
                {
                    _winterGlints[i] = Lit("Glint", _winter, Art.Glint(96, 4), Color.clear, Vector2.one * Cell * Random.Range(.35f, .7f));
                    _winterGlints[i].rectTransform.anchoredPosition =
                        new Vector2(Random.Range(-.48f, .48f) * Span.x, Random.Range(_hillFoot, _hillTop));
                }
            }

            _winter.gameObject.SetActive(true);
            float clock = Time.unscaledTime;
            float tall = _hillTop - _hillFoot;

            _winterWash.color = Pal.A(FrostHue, .1f * _winterShown + .25f * _winterFlash);
            _winterSheet.rectTransform.sizeDelta = new Vector2(Span.x, tall);
            _winterSheet.rectTransform.anchoredPosition = new Vector2(0f, _hillFoot + tall * .5f);
            _winterSheet.color = Pal.A(Color.Lerp(FrostHue, Color.white, .3f), .3f * _winterShown * (frozen ? 1f : .5f));
            _winterVignette.color = Pal.A(Color.Lerp(FrostHue, Color.white, .5f), .55f * _winterShown);

            for (int i = 0; i < _winterGlints.Length; i++)
            {
                var g = _winterGlints[i];
                if (!g) continue;
                float tw = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(clock * (1.6f + i * .21f) + i * 2.3f)), 5f);
                g.color = Pal.A(Color.white, (tw * .9f + _winterFlash * .6f) * _winterShown * (frozen ? 1f : .3f));
                g.rectTransform.localRotation = Quaternion.Euler(0f, 0f, clock * 25f + i * 30f);
            }

            // Snow driving across the hill, thicker in the deep of it.
            _snow -= dt;
            if (_snow > 0f) return;

            _snow = Mathf.Lerp(.08f, .02f, _winterShown);
            var from = new Vector2(Random.Range(-.6f, .5f) * Span.x, Random.Range(_hillFoot, _hillTop + Cell));
            Cinder(from, Random.value < .4f ? Color.white : Pal.Glass, Cell * Random.Range(.16f, .3f), 1.1f,
                   new Vector2(Cell * Random.Range(1.6f, 2.6f), -Cell * Random.Range(.8f, 1.4f)));
        }

        /// <summary>The cold coming down: the light goes blue and a wind rises.</summary>
        void WinterComes()
        {
            Lightup(FrostHue, .14f, SiegeSchools.IceAgeComing);
            Audio.Sfx("whoosh", .55f, .45f);
        }

        /// <summary>The freeze: one white beat, a ring of frost across the whole hill, every body caught in ice.</summary>
        void WinterFalls()
        {
            _winterFlash = 1f;
            var middle = new Vector2(0f, MarchY(.5f));

            Lightup(Color.white, .38f, .5f);
            Ripple(middle, Pal.Glass, Span.x * 1.3f, .6f, 0f, .55f);
            Ripple(middle, FrostHue, Span.x * .9f, .5f, .08f, .55f);
            Twinkles(middle, Pal.Glass, 14, Span.x * .45f);
            ShakeBoard(Cell * .2f);
            Dilate(.55f, .3f);

            Audio.Sfx("shatter", .7f, .7f);
            Audio.Sfx("thunder", .3f, 1.4f);
        }

        /// <summary>A weakened body shattering in the freeze: a burst of ice, shards flung out, a cold flash.</summary>
        void Shattered(Vector2 at)
        {
            Flash(at, Pal.Glass, 2.8f);
            Ripple(at, Pal.Glass, Cell * 3.6f, .4f, 0f);
            FxSparks(_fx, at, Pal.Glass, 14, Cell * 2.6f, Cell * .16f, .42f);

            for (int i = 0; i < 6; i++)
            {
                float ang = i * Mathf.PI / 3f + Random.Range(-.3f, .3f);
                var to = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * GroundSquash) * Cell * Random.Range(1.2f, 1.9f);
                Fly(at, to, Pal.Glass, Cell * .32f, Random.Range(.2f, .3f), 0f, Color.clear, null);
            }

            if (!Flourished(SiegeVia.IceAge, 2)) Audio.SfxVaried("shatter", .45f, .15f);
        }

        // ------------------------------------------------------------------ the tempest
        /// <summary>The tornado as drawn: rings of wind turning round a dark core, lightning crawling up it, debris spiralling.</summary>
        sealed class Funnel
        {
            public RectTransform Root;
            public Image Core, Glow, Foot;
            public RectTransform[] Rings;
            public Image[] Arcs;
            public Lightning.Layered Storm;
            public float Age, Crackle, Shut = -1f;
            public int Seed;
        }

        Funnel _twister;

        const int FunnelRings = 7;
        const float FunnelShuts = .35f;

        Vector2 TwisterEye => new Vector2(LaneX(_board.TempestLane), MarchY(Mathf.Clamp01(_board.TempestMarch)));

        void Twister(float dt)
        {
            bool live = _board.Holds(SiegePower.Tempest) && _board.TempestLive && !Over;

            if (_twister != null && !_twister.Root) _twister = null;
            if (live && _twister != null && _twister.Shut >= 0f) ScrapTwister();
            if (live && _twister == null) _twister = RaiseTwister();
            if (_twister == null) return;

            var f = _twister;
            f.Age += dt;

            if (!live && f.Shut < 0f) f.Shut = 0f;
            if (f.Shut >= 0f)
            {
                f.Shut += dt;
                if (f.Shut >= FunnelShuts) { ScrapTwister(); return; }
            }

            float grown = Ease.OutBack(Mathf.Clamp01(f.Age / .3f));
            float gone = f.Shut < 0f ? 0f : Ease.InCubic(Mathf.Clamp01(f.Shut / FunnelShuts));
            float shown = grown * (1f - gone);

            if (live) f.Root.anchoredPosition = TwisterEye;
            f.Root.localScale = Vector3.one * Mathf.Max(0f, shown);

            float clock = f.Age;
            for (int k = 0; k < f.Rings.Length; k++)
            {
                var ring = f.Rings[k];
                if (!ring) continue;

                float sway = Mathf.Sin(clock * 3f + k * .7f) * Cell * .18f * k / FunnelRings;
                ring.anchoredPosition = new Vector2(sway, k * Cell * .55f);

                var arc = f.Arcs[k];
                if (!arc) continue;

                arc.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -clock * (420f + k * 70f) - k * 50f);
                float flick = .7f + .3f * Mathf.Sin(clock * 30f + k * 2f);
                arc.color = Pal.A(Color.Lerp(StormHue, Color.white, (float)k / FunnelRings * .5f), .85f * flick);
            }

            if (f.Core) f.Core.color = Pal.A(StormDark, .55f);
            if (f.Glow) f.Glow.color = Pal.A(StormHue, .35f + .15f * Mathf.Sin(clock * 17f));
            if (f.Foot)
            {
                f.Foot.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -clock * 300f);
                f.Foot.color = Pal.A(Color.Lerp(StormHue, Color.white, .3f), .7f);
            }

            // Lightning crawling up the funnel: re-struck, never animated.
            if (f.Storm != null && f.Storm.Host != null)
            {
                f.Crackle += dt;
                if (f.Crackle >= .07f && f.Shut < 0f)
                {
                    f.Crackle = 0f;
                    float top = (FunnelRings - 1) * Cell * .55f;
                    var a = new Vector2(Random.Range(-.3f, .3f) * Cell, Random.Range(0f, .3f) * top);
                    var b = new Vector2(Random.Range(-1.6f, 1.6f) * Cell, Random.Range(.6f, 1f) * top);
                    f.Storm.Strike(a, b, Cell, .5f, 1, ++f.Seed);
                }

                f.Storm.Group.alpha = shown * (.55f + .45f * Mathf.Abs(Mathf.Sin(clock * 37f)));
            }

            if (f.Shut >= 0f) return;

            // Debris thrown round it.
            if (Random.value < .6f)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                var spot = f.Root.anchoredPosition + new Vector2(Mathf.Cos(ang) * Cell * 1.4f, Mathf.Sin(ang) * Cell * .4f + Cell * .3f);
                Cinder(spot, Random.value < .5f ? StormHue : Pal.Hollow, Cell * Random.Range(.2f, .34f), .45f,
                       new Vector2(-Mathf.Sin(ang), Mathf.Cos(ang) * .3f + .8f) * Cell * 2.2f);
            }
        }

        Funnel RaiseTwister()
        {
            if (_fx == null) return null;

            var f = new Funnel { Root = UIKit.Box("Tempest", _fx, Vector2.zero, new Vector2(.5f, .5f), TwisterEye) };
            float top = (FunnelRings - 1) * Cell * .55f;

            f.Glow = Lit("Glow", f.Root, Art.Shaft(64, 128, 1.2f, .1f), Color.clear, new Vector2(Cell * 4.2f, top + Cell * 1.6f));
            f.Glow.rectTransform.pivot = new Vector2(.5f, 0f);
            f.Core = UIKit.Img("Core", f.Root, Art.Shaft(64, 128, 1.6f, .1f), Color.clear, new Vector2(Cell * 2.2f, top + Cell));
            f.Core.raycastTarget = false;
            f.Core.rectTransform.pivot = new Vector2(.5f, 0f);
            f.Foot = Lit("Foot", f.Root, Art.Whorl(96, 14f, .38f), Color.clear, new Vector2(Cell * 2.4f, Cell * 2.4f * GroundSquash));

            f.Rings = new RectTransform[FunnelRings];
            f.Arcs = new Image[FunnelRings];
            for (int k = 0; k < FunnelRings; k++)
            {
                // **A ring is an arc turning inside a node squashed flat**: a rotation inside a
                // squashed parent sweeps an ellipse, which is what reads as wind going round.
                var ring = UIKit.Node("Ring", f.Root);
                float wide = Cell * (1.1f + k * .5f);
                ring.localScale = new Vector3(1f, .3f, 1f);
                f.Rings[k] = ring;

                var arc = Lit("Arc", ring, Art.Arc(96, 9f, .62f), Color.clear, Vector2.one * wide);
                f.Arcs[k] = arc;
            }

            f.Storm = Lightning.Grow("Tempest storm", f.Root, StormHue, Cell * .05f, haloAlpha: .5f);
            f.Storm.Group.alpha = 0f;

            Lightup(StormHue, .12f, .4f);
            return f;
        }

        void ScrapTwister()
        {
            if (_twister == null) return;
            if (_twister.Storm != null) _twister.Storm.Destroy();
            if (_twister.Root) Destroy(_twister.Root.gameObject);
            _twister = null;
        }
    }
}
