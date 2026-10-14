using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>How the Shuffle lane's secrets and evolutions look</b> (<c>SiegeBoard.Secrets</c>,
    /// MODES.md 59n): each power changes how the board looks for as long as the run holds it -
    /// a storm cloud over the hill, blades circling every post, the lower hill frozen, a golden
    /// dome over the line, a second sun, a prism - and each thing it does is a set piece: a bolt
    /// out of the cloud, a geyser, a plague bursting, comets off the gems, a rainbow off a
    /// cascade, a black hole, a fire dragon, a solar beam, a meteor shower, time stopping, titan
    /// shells, a sniper's round, bomblets, ball lightning, a swarm, a blizzard, shards, a prism's
    /// beams.
    ///
    /// <para>
    /// <b>The Shuffle lane's alone, through the arsenal's doors</b> (<c>SiegeView.Arsenal</c>):
    /// reached from a hit whose <see cref="SiegeBolt.Via"/> a power reported, a
    /// <see cref="SiegeVolley"/> one reported, a beat's <see cref="SiegeBeat.Comets"/>, or
    /// <see cref="Secrets"/>, which returns before it reads anything on a build that has unlocked
    /// nothing - and the plain line reports none of them.
    /// </para>
    /// <para>
    /// <b>Cheap where it repeats</b>, for the arsenal's reason: what a power leaves standing is
    /// built once and moved every frame, every hit is pooled embers or a pooled dart, and what a
    /// power throws is a handful of images for under a second. A late hill with every power on
    /// it costs what a busy arsenal does.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the colours
        static readonly Color StormHue = new Color(.62f, .84f, 1f);
        static readonly Color StormDark = new Color(.10f, .13f, .24f);
        static readonly Color SteamHue = new Color(.9f, .97f, 1f);
        static readonly Color BladeHue = new Color(.70f, 1f, .95f);
        static readonly Color PlagueHue = new Color(.62f, 1f, .32f);
        static readonly Color FrostHue = new Color(.70f, .92f, 1f);
        static readonly Color HoleWash = new Color(1f, .62f, .30f);
        static readonly Color ChronoHue = new Color(.58f, .86f, 1f);

        /// <summary>The rainbow a cascade and a prism are drawn in, red to violet.</summary>
        static readonly Color[] Spectrum = { Pal.Poppy, Pal.Marigold, Pal.Pollen, Pal.Verdant, Pal.Azure, Pal.Foxglove };

        // ------------------------------------------------------------------ the frame
        /// <summary>
        /// Keeps everything an unlocked power leaves standing drawn true, every frame. <b>Returns
        /// before it reads anything on a build that has unlocked nothing</b>, and tears down what
        /// it built when the board in front of it holds none (a run dealt again).
        /// </summary>
        void Secrets(float dt)
        {
            if (_board == null || _fx == null) return;

            if (!_board.Boosts.AnyPower)
            {
                if (_secretsUp) ScrapSecrets();
                return;
            }

            _secretsUp = true;
            _prismBeam = 0;

            Cloud(dt);
            Orbiting(dt);
            Glacier(dt);
            Abyss(dt);
            Wyrm(dt);
            Dome(dt);
            Sun(dt);
            Chrono(dt);
            Balls(dt);
            Snow(dt);
            Crystal(dt);
        }

        bool _secretsUp;

        void ScrapSecrets()
        {
            _secretsUp = false;
            Scrap(ref _cloud);
            Scrap(ref _blades);
            Scrap(ref _glacier);
            Scrap(ref _dome);
            Scrap(ref _sun);
            Scrap(ref _clock);
            Scrap(ref _crystal);
            ScrapAbyss();
            ScrapWyrm();

            foreach (var ball in _balls.Values) if (ball != null && ball.Host) Destroy(ball.Host.gameObject);
            _balls.Clear();
            foreach (var flurry in _flurries.Values) if (flurry != null && flurry.Host) Destroy(flurry.Host.gameObject);
            _flurries.Clear();
        }

        static void Scrap(ref RectTransform host)
        {
            if (host) Destroy(host.gameObject);
            host = null;
        }

        // ------------------------------------------------------------------ a hit a power dealt
        /// <summary>
        /// Draws one hit a power reported, answering whether it was one. Every one lands its own
        /// number through <paramref name="land"/>, the arsenal's door, once its picture arrives.
        /// </summary>
        bool SecretHit(SiegeBolt shot, Mob mob, Vector2 to, Vector2 source, Color tint, System.Action land)
        {
            switch (shot.Via)
            {
                case SiegeVia.Storm:
                    Downpour(to);
                    land();
                    return true;

                case SiegeVia.Steam:
                    Pop(to, SteamHue, 1.6f, .24f);
                    Cinder(to, SteamHue, Cell * .9f, .5f, new Vector2(0f, Cell * 1.2f));
                    land();
                    return true;

                case SiegeVia.Blade:
                {
                    // Out from the post to the first, back through the second and home again.
                    var from = shot.From >= 0 ? source : Muzzle(shot.Ward);
                    float flight = shot.From >= 0 ? .14f : .2f;
                    Shuriken(from, to, flight, () =>
                    {
                        if (this == null) return;
                        Pop(to, BladeHue, 1.4f, .18f);
                        Burst.Sparks(_fx, to, BladeHue, 5, Cell * 1.6f, Cell * .14f, .26f);
                        land();
                        if (shot.From >= 0) Shuriken(to, Muzzle(shot.Ward), .22f, null);
                    });
                    if (!Flourished(SiegeVia.Blade, shot.Ward)) Audio.SfxVaried("whoosh", .22f, .1f);
                    return true;
                }

                case SiegeVia.Plague:
                    Pop(to, PlagueHue, 1.4f, .22f);
                    Cinder(to, PlagueHue, Cell * .6f, .5f, new Vector2(0f, Cell * .7f));
                    land();
                    return true;

                case SiegeVia.Comet:
                case SiegeVia.Armageddon:
                case SiegeVia.Shard:
                    Pop(to, Pal.Sun, 1.5f, .22f);
                    Cinder(to, Pal.Ember, Cell * .7f, .4f, new Vector2(Random.Range(-Cell, Cell) * .5f, Cell));
                    land();
                    return true;

                case SiegeVia.Rainbow:
                    for (int i = 0; i < 3; i++)
                        Pop(to, Spectrum[Random.Range(0, Spectrum.Length)], 1.2f + i * .4f, .2f + i * .05f);
                    land();
                    return true;

                case SiegeVia.Permafrost:
                    Spike(to);
                    land();
                    return true;

                case SiegeVia.Horizon:
                {
                    var eye = _abyss != null ? _abyss.Pit.At : to;
                    Cinder(to, HoleWash, Cell * .6f, .3f, (eye - to) * .8f);
                    if (shot.Killed) Pop(to, Pal.Bloom, 1.8f, .24f);
                    land();
                    return true;
                }

                case SiegeVia.Dragon:
                    Pop(to, Pal.Sun, 1.7f, .22f);
                    Cinder(to, Pal.Ember, Cell * .8f, .45f, new Vector2(0f, Cell * 1.1f));
                    land();
                    return true;

                case SiegeVia.Solar:
                    Pop(to, Pal.Radiance, 2f, .26f);
                    Cinder(to, Pal.Sun, Cell * .9f, .4f, new Vector2(0f, Cell * 1.3f));
                    land();
                    return true;

                case SiegeVia.Shell:
                    if (shot.From < 0)
                        Fly(Muzzle(shot.Ward), to, Pal.Sun, Cell * .9f, .12f, 0f, Pal.Ember, () =>
                        {
                            if (this == null) return;
                            Pop(to, Pal.Radiance, 2.2f, .24f);
                            land();
                        });
                    else
                    {
                        Pop(to, Pal.Marigold, 1.4f, .2f);
                        land();
                    }
                    return true;

                case SiegeVia.Snipe:
                    Sniped(shot.Ward, mob, to, land);
                    return true;

                case SiegeVia.Reflect:
                    Pop(to, Pal.Gold, 1.6f, .22f);
                    Embers(to, Pal.Parchment, 5, Cell * .4f, Cell * .18f);
                    land();
                    return true;

                case SiegeVia.Bomblet:
                    Pop(to, Pal.Marigold, 1.3f, .2f);
                    land();
                    return true;

                case SiegeVia.Orb:
                {
                    var from = NearestBall(to);
                    Arc(from, to, StormHue, Cell * .07f, .2f, 1, .36f);
                    Pop(to, StormHue, 1.4f, .2f);
                    land();
                    return true;
                }

                case SiegeVia.Swarm:
                {
                    var from = Muzzle(shot.Ward) + new Vector2(Random.Range(-Cell, Cell) * .3f, Cell * .2f);
                    Fly(from, to, Pal.Marigold, Cell * .3f, Random.Range(.28f, .42f),
                        Random.value < .5f ? Random.Range(.25f, .5f) : -Random.Range(.25f, .5f), Pal.Hollow, () =>
                        {
                            if (this == null) return;
                            Boom(to, Blast("boom_fire"), Cell * 1.2f);
                            Pop(to, Pal.Marigold, 1.2f, .18f);
                            land();
                        });
                    if (!Flourished(SiegeVia.Swarm, shot.Ward)) Audio.SfxVaried("whoosh", .3f, .1f);
                    return true;
                }

                case SiegeVia.Dust:
                    Pop(to, Pal.Glass, 1.3f, .2f);
                    land();
                    return true;

                case SiegeVia.Prism:
                {
                    var tint2 = Spectrum[_prismBeam++ % Spectrum.Length];
                    Lase(PrismAt, to, tint2, .2f, 0f, false);
                    Pop(to, tint2, 1.8f, .22f);
                    land();
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>Which beam of this frame's prism a hit is, for its colour.</summary>
        int _prismBeam;

        // ------------------------------------------------------------------ a place a power set off
        /// <summary>Draws one <see cref="SiegeVolley"/> a power reported, answering whether it was one.</summary>
        bool SecretVolley(SiegeVolley volley)
        {
            var at = new Vector2(LaneX(volley.Lane), MarchY(volley.March));

            switch (volley.Via)
            {
                case SiegeVia.Storm:
                    Thunder();
                    return true;

                case SiegeVia.Steam:
                    Geyser(at);
                    return true;

                case SiegeVia.Plague:
                    Plagued(at);
                    return true;

                case SiegeVia.Comet:
                    Flash(at, Pal.Sun, 3.4f);
                    Fireball(at, .4f, .7f);
                    Ripple(at, Pal.Marigold, Cell * 4.2f, .45f, 0f);
                    Embers(at, Pal.Ember, 12, Cell * .8f, Cell * .28f);
                    ShakeBoard(Cell * .1f);
                    Audio.SfxVaried("boom", .4f, .08f);
                    return true;

                case SiegeVia.Rainbow:
                    Flash(at, Color.white, 3f);
                    for (int i = 0; i < Spectrum.Length; i++)
                        Ripple(at, Spectrum[i], Cell * (2.2f + i * .55f), .5f, i * .035f);
                    Twinkles(at, Pal.Radiance, 8, Cell * 1.4f);
                    Audio.SfxVaried("arc", .3f, .15f);
                    return true;

                case SiegeVia.Permafrost:
                    Frostbite();
                    return true;

                case SiegeVia.Horizon:
                    if (volley.In > 0f) AbyssOpens(at);
                    else AbyssCrushes();
                    return true;

                case SiegeVia.Dragon:
                    Lightup(Pal.Ember, .12f, .5f);
                    Audio.Sfx("whoosh", .6f, .55f);
                    Audio.Sfx("boom", .35f, .45f);
                    return true;

                case SiegeVia.Dome:
                    if (volley.Ward < 0) DomeRaised();
                    else DomeBlocked(at, volley.Reach);
                    return true;

                case SiegeVia.Solar:
                    if (volley.In > 0f) SolarCharging(volley.Lane);
                    else SolarFires(volley.Lane);
                    return true;

                case SiegeVia.Armageddon:
                    if (volley.Reach == 0) DoomSky(volley.In);
                    else if (volley.In > 0f) Rock(at, volley.In);
                    else Impact(at, 1f);
                    return true;

                case SiegeVia.TimeStop:
                    if (volley.In > 0f) TimeStops();
                    else TimeStarts();
                    return true;

                case SiegeVia.Shell:
                    Boom(at, Blast("boom_fire"), Cell * 3.2f);
                    Answer(at, Pal.Sun, 3.4f, StrikeFx.Crack, 2.6f);
                    Ripple(at, Pal.Marigold, Cell * 4.6f, .45f, 0f);
                    Embers(at, Pal.Ember, 12, Cell * .9f, Cell * .3f);
                    ShakeBoard(Cell * .14f);
                    Audio.SfxVaried("boom", .5f, .08f);
                    return true;

                case SiegeVia.Fortress:
                    Walled(volley.Ward);
                    return true;

                case SiegeVia.Bomblet:
                    if (volley.In > 0f) Lob(volley.Ward, BoxAt(Mathf.RoundToInt(volley.Lane), SiegeTuning.RowOf(volley.March)), volley.In);
                    else Impact(BoxAt(Mathf.RoundToInt(volley.Lane), SiegeTuning.RowOf(volley.March)), .6f);
                    return true;

                case SiegeVia.Orb:
                    Flash(Muzzle(volley.Ward), StormHue, 2.2f);
                    Audio.SfxVaried("arc", .3f, .1f);
                    return true;

                case SiegeVia.Dust:
                    Ripple(at, Pal.Glass, Cell * 5f, .5f, 0f);
                    Twinkles(at, Pal.Glass, 10, Cell * 1.8f);
                    Lightup(FrostHue, .08f, .35f);
                    Audio.SfxVaried("shatter", .4f, .1f);
                    return true;

                case SiegeVia.Shard:
                {
                    var box = BoxAt(Mathf.RoundToInt(volley.Lane), SiegeTuning.RowOf(volley.March));
                    if (volley.In > 0f)
                    {
                        var sky = box + new Vector2(Random.Range(-Cell, Cell) * 1.4f, Cell * 3.2f);
                        Fly(sky, box, Pal.Sun, Cell * .55f, volley.In, 0f, Pal.Ember, null);
                    }
                    else Impact(box, .55f);
                    return true;
                }

                case SiegeVia.Prism:
                    if (volley.In > 0f) PrismCharges(volley.In);
                    else PrismBursts();
                    return true;

                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ thunderhead
        /// <summary>The storm cloud: dark banks along the top of the hill, lit from inside.</summary>
        RectTransform _cloud;

        Image[] _cloudBanks, _cloudLights;
        float[] _cloudFlash;
        Lightning.Layered _cloudArc;
        float _cloudAge, _cloudArcIn;

        const int CloudBanks = 7;

        Vector2 CloudAt => new Vector2(0f, MarchY(0f) + Cell * .55f);

        void Cloud(float dt)
        {
            if (!_board.Holds(SiegePower.Thunderhead)) { Scrap(ref _cloud); return; }

            if (!_cloud)
            {
                _cloud = UIKit.Node("Thunderhead", _fx);
                _cloud.SetAsFirstSibling();
                _cloudBanks = new Image[CloudBanks];
                _cloudLights = new Image[CloudBanks];
                _cloudFlash = new float[CloudBanks];

                for (int i = 0; i < CloudBanks; i++)
                {
                    var bank = UIKit.Img("Bank", _cloud, Art.Glow(128, 1.2f), Pal.A(StormDark, 0f),
                                         new Vector2(Cell * Random.Range(2.6f, 3.6f), Cell * Random.Range(1.4f, 1.9f)));
                    bank.raycastTarget = false;
                    _cloudBanks[i] = bank;
                    _cloudLights[i] = Lit("Light", _cloud, Art.Glow(128, 2f), Color.clear, Vector2.one * Cell * 2.6f);
                }

                _cloudArc = Lightning.Grow("Cloud arc", _cloud, StormHue, Cell * .045f, haloAlpha: .45f);
                _cloudArc.Group.alpha = 0f;
                _cloudAge = 0f;
                _cloudArcIn = .6f;

                Lightup(StormHue, .1f, .6f);
            }

            _cloudAge += dt;
            float grown = Mathf.Clamp01(_cloudAge / .8f);
            var centre = CloudAt;
            float spread = Span.x * .52f;

            for (int i = 0; i < CloudBanks; i++)
            {
                float u = CloudBanks <= 1 ? 0f : i / (CloudBanks - 1f);
                float drift = Mathf.Sin(_cloudAge * .35f + i * 1.7f) * Cell * .35f;
                float bob = Mathf.Sin(_cloudAge * .6f + i * 2.3f) * Cell * .12f;
                var spot = centre + new Vector2(Mathf.Lerp(-spread, spread, u) + drift, bob + (i % 2 == 0 ? Cell * .2f : 0f));

                var bank = _cloudBanks[i];
                if (bank)
                {
                    bank.rectTransform.anchoredPosition = spot;
                    bank.color = Pal.A(StormDark, .62f * grown);
                }

                _cloudFlash[i] = Mathf.MoveTowards(_cloudFlash[i], 0f, dt * 3.2f);
                var light = _cloudLights[i];
                if (!light) continue;

                // A storm lights itself from the inside: a slow churn, and a flash that dies fast.
                float churn = .08f + .06f * Mathf.Sin(_cloudAge * 2.1f + i * 1.3f);
                light.rectTransform.anchoredPosition = spot + new Vector2(0f, -Cell * .15f);
                light.color = Pal.A(Color.Lerp(StormHue, Color.white, _cloudFlash[i]), (churn + _cloudFlash[i] * .8f) * grown);
                light.rectTransform.localScale = Vector3.one * (1f + .25f * _cloudFlash[i]);
            }

            // Lightning walking through the cloud between two banks, on an uneven beat.
            _cloudArcIn -= dt;
            if (_cloudArcIn <= 0f && _cloudArc != null && _cloudArc.Host != null)
            {
                _cloudArcIn = Random.Range(.5f, 1.6f);

                int a = Random.Range(0, CloudBanks - 1);
                var from = _cloudBanks[a] ? _cloudBanks[a].rectTransform.anchoredPosition : centre;
                var to = _cloudBanks[a + 1] ? _cloudBanks[a + 1].rectTransform.anchoredPosition : centre;
                _cloudArc.Strike(from, to, Cell, .5f, 1, Random.Range(0, 1 << 20));
                _cloudArc.Group.alpha = 1f;
                _cloudFlash[a] = 1f;
                _cloudFlash[a + 1] = .7f;
            }

            if (_cloudArc != null && _cloudArc.Group != null)
                _cloudArc.Group.alpha = Mathf.MoveTowards(_cloudArc.Group.alpha, 0f, dt * 5f);
        }

        /// <summary>The whole cloud lit at once when it strikes, with the thunder under it.</summary>
        void Thunder()
        {
            if (_cloudFlash != null)
                for (int i = 0; i < _cloudFlash.Length; i++) _cloudFlash[i] = Random.Range(.7f, 1f);

            Lightup(StormHue, .14f, .3f);
            Audio.Sfx("thunder", .55f, Random.Range(.85f, 1.1f));
        }

        /// <summary>One bolt out of the cloud onto a body: the channel, re-struck, the flash and the ground.</summary>
        void Downpour(Vector2 at)
        {
            if (_sky == null || _fx == null) return;

            var from = new Vector2(at.x + Random.Range(-Cell, Cell) * .6f, CloudAt.y);
            int seed = Random.Range(0, 1 << 20);

            var bolt = Lightning.Grow("Downpour", _sky, StormHue, Cell * .08f, haloAlpha: .55f);
            bolt.Strike(from, at, Cell, .42f, 2, seed);
            bolt.Group.alpha = 1f;

            Tween.After(.08f, () => { if (bolt.Host != null) bolt.Strike(from, at, Cell, .42f, 1, seed + 1); }, bolt.Host);

            Tween.Run(.42f, Ease.Linear, t =>
            {
                if (bolt.Host == null) return;
                bolt.Group.alpha = (1f - t * t) * (.6f + .4f * Mathf.Abs(Mathf.Sin(t * 47f)));
            }, bolt.Host).OnDone(bolt.Destroy);

            Tween.After(.5f, bolt.Destroy, bolt.Host);

            Flash(at, StormHue, 3f);
            Ripple(at, StormHue, Cell * 3.4f, .4f, 0f);
            Embers(at, StormHue, 10, Cell * 1.4f, Cell * .26f);
            ShakeBoard(Cell * .06f);
            Audio.SfxVaried("arc", .45f, .1f);
        }

        // ------------------------------------------------------------------ frostfire
        /// <summary>
        /// A geyser: the ground cracks, a white column of steam stands up out of it with fire and
        /// frost spiralling round each other up its length, and clouds of steam roll off its top.
        /// </summary>
        void Geyser(Vector2 at)
        {
            var host = UIKit.Box("Geyser", _fx, Vector2.zero, new Vector2(.5f, .5f), at);

            var column = Lit("Column", host, Art.Shaft(64, 64, 1.1f, .25f), Pal.A(SteamHue, 0f),
                             new Vector2(Cell * 1.8f, Cell * 4.5f));
            var crt = column.rectTransform;
            crt.localRotation = Quaternion.Euler(0f, 0f, 180f);

            var core = Lit("Core", host, Art.Shaft(64, 64, 2f, .25f), Pal.A(Color.white, 0f),
                           new Vector2(Cell * .7f, Cell * 4.5f));
            var krt = core.rectTransform;
            krt.localRotation = Quaternion.Euler(0f, 0f, 180f);

            const float Life = 1.1f;
            float nextCurl = 0f;

            Tween.Run(Life, Ease.Linear, t =>
            {
                if (!host) return;

                float s = t * Life;
                float rise = Ease.OutQuint(Mathf.Clamp01(s / .22f));
                float tall = Cell * 4.5f * rise;
                float fade = 1f - Mathf.Clamp01((s - .5f) / .6f);

                crt.sizeDelta = new Vector2(Cell * Mathf.Lerp(1.2f, 2.1f, t), tall);
                crt.anchoredPosition = new Vector2(0f, tall * .5f);
                column.color = Pal.A(SteamHue, .7f * fade);

                krt.sizeDelta = new Vector2(Cell * .6f, tall * .9f);
                krt.anchoredPosition = new Vector2(0f, tall * .45f);
                core.color = Pal.A(Color.white, .9f * fade);

                // Fire and frost winding round each other up the column.
                if (s >= nextCurl && s < .75f)
                {
                    nextCurl = s + .035f;
                    float turn = s * 18f;
                    for (int k = 0; k < 2; k++)
                    {
                        float side = Mathf.Sin(turn + k * Mathf.PI) * Cell * .7f;
                        var hue = k == 0 ? Pal.Ember : FrostHue;
                        Cinder(at + new Vector2(side, Cell * .3f), hue, Cell * Random.Range(.35f, .55f), .55f,
                               new Vector2(-side * .6f, Cell * Random.Range(4f, 5.5f)));
                    }
                }
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(Life + .1f, () => { if (host) Destroy(host.gameObject); }, host);

            Answer(at, FrostHue, 3.2f, StrikeFx.Crack, 2.6f);
            Ripple(at, SteamHue, Cell * 5f, .5f, 0f);
            Ripple(at, Pal.Ember, Cell * 3.4f, .42f, .06f);
            Boom(at + new Vector2(0f, Cell * 2.6f), Blast("boom_smoke"), Cell * 4f, 18f);
            Twinkles(at, FrostHue, 6, Cell * 1.4f);
            ShakeBoard(Cell * .08f);

            Audio.Sfx("whoosh", .55f, .7f);
            Audio.SfxVaried("burst", .4f, .1f);
        }

        // ------------------------------------------------------------------ blade orbit
        RectTransform _blades;
        Image[] _bladeArt;
        float _bladeTurn;

        const int BladesPerPost = 3;

        /// <summary>Three blades circling every standing post, turning on their own as they go.</summary>
        void Orbiting(float dt)
        {
            if (!_board.Holds(SiegePower.BladeOrbit) || _posts == null) { Scrap(ref _blades); return; }

            int want = _posts.Length * BladesPerPost;
            if (!_blades || _bladeArt == null || _bladeArt.Length != want)
            {
                Scrap(ref _blades);
                _blades = UIKit.Node("Blades", _fx);
                _bladeArt = new Image[want];
                for (int i = 0; i < want; i++)
                    _bladeArt[i] = Lit("Blade", _blades, Art.Glint(96, 4), Color.clear, Vector2.one * Cell * .62f);

                Twinkles(new Vector2(0f, _lineY + Cell * .4f), BladeHue, 10, Span.x * .45f);
            }

            _bladeTurn += dt * 2.6f;

            for (int w = 0; w < _posts.Length; w++)
            {
                bool standing = w < _board.Wards.Count && _board.Wards[w].Alive;
                var centre = new Vector2(PostX(w), _lineY + Cell * .45f);

                for (int b = 0; b < BladesPerPost; b++)
                {
                    var blade = _bladeArt[w * BladesPerPost + b];
                    if (!blade) continue;

                    blade.enabled = standing;
                    if (!standing) continue;

                    float a = _bladeTurn + b * Mathf.PI * 2f / BladesPerPost + w * .7f;
                    var rt = blade.rectTransform;
                    rt.anchoredPosition = centre + new Vector2(Mathf.Cos(a) * Cell * 1.05f, Mathf.Sin(a) * Cell * .42f);
                    rt.localRotation = Quaternion.Euler(0f, 0f, -_bladeTurn * 260f);

                    // Brighter on the near side of the circle, which is what makes it a circle.
                    float near = .55f + .45f * Mathf.Clamp01(-Mathf.Sin(a) + .3f);
                    blade.color = Pal.A(Color.Lerp(BladeHue, Color.white, .3f), .9f * near);
                    rt.localScale = Vector3.one * (.8f + .3f * near);
                }
            }
        }

        /// <summary>A blade thrown: a spinning star flying from one place to another, trailing light.</summary>
        void Shuriken(Vector2 a, Vector2 b, float flight, System.Action landed)
        {
            if (_fx == null) { landed?.Invoke(); return; }

            var star = Lit("Shuriken", _fx, Art.Glint(96, 4), Pal.A(Color.white, 1f), Vector2.one * Cell * .7f);
            var halo = Lit("Halo", star.rectTransform, Art.Glow(96, 2f), Pal.A(BladeHue, .7f), Vector2.one * Cell * 1.3f);
            var rt = star.rectTransform;
            rt.anchoredPosition = a;
            var span = b - a;
            var side = span.sqrMagnitude > 1f ? new Vector2(-span.y, span.x).normalized : Vector2.zero;
            float nextTrail = 0f;

            Tween.Run(flight, Ease.Linear, t =>
            {
                if (!star) return;
                var at = Vector2.Lerp(a, b, t) + side * Mathf.Sin(t * Mathf.PI) * span.magnitude * .18f;
                rt.anchoredPosition = at;
                rt.localRotation = Quaternion.Euler(0f, 0f, -t * 1080f);
                if (halo) halo.color = Pal.A(BladeHue, .7f);

                if (t >= nextTrail)
                {
                    nextTrail = t + .2f;
                    Cinder(at, BladeHue, Cell * .4f, .22f, Vector2.zero);
                }
            }, star).OnDone(() =>
            {
                if (star) Destroy(star.gameObject);
                landed?.Invoke();
            });

            Tween.After(flight + .2f, () => { if (star) Destroy(star.gameObject); }, star);
        }

        // ------------------------------------------------------------------ plague
        /// <summary>A plague bursting out of a poisoned body: a green bloom, spores flung out, the ground bubbling.</summary>
        void Plagued(Vector2 at)
        {
            Flash(at, PlagueHue, 2.8f);
            Ripple(at, PlagueHue, Cell * 4.6f, .5f, 0f);
            Ripple(at, Pal.Verdant, Cell * 3f, .42f, .08f);
            Boom(at, Blast("boom_smoke"), Cell * 3.4f, 20f);

            for (int i = 0; i < 12; i++)
            {
                float ang = i * Mathf.PI * 2f / 12f + Random.Range(-.2f, .2f);
                var kick = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * GroundSquash) * Cell * Random.Range(1.6f, 2.6f);
                Cinder(at, i % 3 == 0 ? Color.white : PlagueHue, Cell * Random.Range(.3f, .5f), .55f, kick);
            }

            Audio.SfxVaried("burst", .35f, .12f);
        }

        // ------------------------------------------------------------------ permafrost
        RectTransform _glacier;
        Image _ice, _iceEdge, _iceGlow;
        Image[] _iceGlints;
        float _iceAge, _iceMist, _iceFlash;

        const int IceGlints = 12;

        float IceTop => MarchY(SiegeSecrets.IceFrom);

        /// <summary>The lower hill frozen over: a sheet of ice brighter toward the line, an edge, glints and mist.</summary>
        void Glacier(float dt)
        {
            if (!_board.Holds(SiegePower.Permafrost) || _hill == null) { Scrap(ref _glacier); return; }

            float top = IceTop, foot = _hillFoot + Cell * .35f;
            float tall = Mathf.Max(Cell, top - foot);
            float wide = Span.x;

            if (!_glacier)
            {
                // **Under the raiders**, in the hill's own layer: a body walks on ice, not behind it.
                _glacier = UIKit.Node("Permafrost", _hill);

                _ice = Lit("Sheet", _glacier, Art.FadeUp(64), Color.clear, new Vector2(wide, tall));
                _iceGlow = Lit("Glow", _glacier, Art.Glow(128, 1.4f), Color.clear, new Vector2(wide * 1.1f, tall * 1.4f));
                _iceEdge = Lit("Edge", _glacier, Art.SoftCapsule(40, 120), Color.clear, new Vector2(Cell * .3f, wide));
                _iceEdge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                _iceGlints = new Image[IceGlints];
                for (int i = 0; i < IceGlints; i++)
                {
                    _iceGlints[i] = Lit("Glint", _glacier, Art.Glint(96, 4), Color.clear, Vector2.one * Cell * Random.Range(.3f, .6f));
                    _iceGlints[i].rectTransform.anchoredPosition = new Vector2(Random.Range(-wide, wide) * .48f,
                                                                               Random.Range(foot, top));
                }

                _iceAge = 0f;
                Lightup(FrostHue, .12f, .6f);
            }

            _iceAge += dt;
            _iceFlash = Mathf.MoveTowards(_iceFlash, 0f, dt * 2.5f);
            float grown = Mathf.Clamp01(_iceAge / .9f);
            float breathe = .85f + .15f * Mathf.Sin(_iceAge * 1.7f);

            if (_ice)
            {
                _ice.rectTransform.sizeDelta = new Vector2(wide, tall);
                _ice.rectTransform.anchoredPosition = new Vector2(0f, foot + tall * .5f);
                // FadeUp is brightest at its foot: brightest at the line, fading up the slope.
                _ice.color = Pal.A(Color.Lerp(FrostHue, Color.white, _iceFlash * .5f), (.26f + .2f * _iceFlash) * grown * breathe);
            }

            if (_iceGlow)
            {
                _iceGlow.rectTransform.anchoredPosition = new Vector2(0f, foot + tall * .3f);
                _iceGlow.color = Pal.A(FrostHue, (.1f + .25f * _iceFlash) * grown);
            }

            if (_iceEdge)
            {
                _iceEdge.rectTransform.sizeDelta = new Vector2(Cell * .28f, wide * 1.02f);
                _iceEdge.rectTransform.anchoredPosition = new Vector2(0f, top);
                _iceEdge.color = Pal.A(Color.Lerp(FrostHue, Color.white, .4f), (.45f + .35f * _iceFlash) * grown * breathe);
            }

            for (int i = 0; i < _iceGlints.Length; i++)
            {
                var glint = _iceGlints[i];
                if (!glint) continue;

                float twinkle = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(_iceAge * (1.3f + i * .17f) + i * 2.1f)), 6f);
                glint.color = Pal.A(Color.white, (twinkle * .9f + _iceFlash * .5f) * grown);
                glint.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _iceAge * 20f + i * 30f);
            }

            // Cold mist breathing off the ice.
            _iceMist -= dt;
            if (_iceMist <= 0f)
            {
                _iceMist = .16f;
                var from = new Vector2(Random.Range(-wide, wide) * .45f, Random.Range(foot, top));
                Cinder(from, FrostHue, Cell * Random.Range(.5f, .8f), .9f, new Vector2(Random.Range(-Cell, Cell) * .3f, Cell * .5f));
            }
        }

        /// <summary>The ice spiking: the sheet flashes, and crystals burst up out of it all along the slope.</summary>
        void Frostbite()
        {
            _iceFlash = 1f;
            if (_fx == null) return;

            float top = IceTop, foot = _hillFoot + Cell * .35f;
            for (int i = 0; i < 6; i++)
            {
                var at = new Vector2(Random.Range(-Span.x, Span.x) * .45f, Random.Range(foot, top));
                Tween.After(i * .04f, () => { if (this) Spike(at); }, this);
            }

            Audio.SfxVaried("shatter", .3f, .1f);
        }

        /// <summary>One ice spike bursting up out of the ground and shattering.</summary>
        void Spike(Vector2 at)
        {
            if (_fx == null) return;

            var spike = Lit("Spike", _fx, Art.Crystal(128), Pal.A(FrostHue, 0f), new Vector2(Cell * .7f, Cell * 1.7f));
            var rt = spike.rectTransform;
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = at - new Vector2(0f, Cell * .3f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));

            Tween.Run(.42f, Ease.Linear, t =>
            {
                if (!spike) return;
                float up = Ease.OutBack(Mathf.Clamp01(t / .3f));
                rt.localScale = new Vector3(1f, Mathf.Max(0f, up), 1f);
                spike.color = Pal.A(Color.Lerp(Color.white, FrostHue, t), 1f - Mathf.Clamp01((t - .6f) / .4f));
            }, spike).OnDone(() =>
            {
                if (spike) Destroy(spike.gameObject);
                if (_fx != null) Burst.Sparks(_fx, at + new Vector2(0f, Cell * .6f), Pal.Glass, 5, Cell * 1.5f, Cell * .14f, .3f);
            });

            Tween.After(.55f, () => { if (spike) Destroy(spike.gameObject); }, spike);
        }

        // ------------------------------------------------------------------ event horizon
        sealed class Hole
        {
            public Pit Pit;
            public float Shut = -1f;
        }

        Hole _abyss;

        const float AbyssWide = 6.4f, AbyssCore = 1.7f, AbyssShuts = .36f;

        /// <summary>The black hole, put up, turned and taken down off <c>HorizonOpen</c>: the well's stack in dark and fire.</summary>
        void Abyss(float dt)
        {
            bool open = _board.Holds(SiegePower.EventHorizon) && _board.HorizonOpen;

            if (_abyss != null && (!_abyss.Pit.Under || !_abyss.Pit.Over)) _abyss = null;
            if (open && _abyss == null) _abyss = RaiseAbyss();
            if (_abyss == null) return;

            var hole = _abyss;
            var pit = hole.Pit;
            pit.Age += dt;

            if (!open && hole.Shut < 0f) hole.Shut = 0f;
            if (hole.Shut >= 0f)
            {
                hole.Shut += dt;
                if (hole.Shut >= AbyssShuts) { ScrapAbyss(); return; }
            }

            float grown = Ease.OutBack(Mathf.Clamp01(pit.Age / WellOpens));
            float gone = hole.Shut < 0f ? 0f : Ease.InCubic(Mathf.Clamp01(hole.Shut / AbyssShuts));
            float size = grown * (1f - gone) * (1f + .05f * Mathf.Sin(pit.Age * 9f));
            float light = Mathf.Clamp01(pit.Age / (WellOpens * .6f)) * (1f - gone);

            pit.Under.localScale = pit.Over.localScale = Vector3.one * Mathf.Max(0f, size);

            // Faster the longer it stands: it is eating.
            float haste = Mathf.Lerp(1.6f, 4.2f, Mathf.Clamp01(pit.Age / SiegeSecrets.HorizonFor));
            pit.Spin -= dt * WellSpin * haste;

            Twirl(pit.DiscBack, pit.Spin, light, HoleWash);
            Twirl(pit.DiscFront, pit.Spin, light, HoleWash);
            Twirl(pit.ArmsBack, pit.Spin * 2.6f, light, Pal.Sun);
            Twirl(pit.ArmsFront, pit.Spin * 2.6f, light, Pal.Sun);
            Twirl(pit.Inner, -pit.Spin * .8f, light * .8f, Pal.Poppy);

            if (pit.Ring != null) pit.Ring.color = Pal.A(Color.white, light * (.75f + .25f * Mathf.Sin(pit.Age * 15f)));
            if (pit.Veil != null) pit.Veil.color = Pal.A(Color.black, .8f * light);

            if (hole.Shut >= 0f) return;

            pit.Mote += dt;
            while (pit.Mote >= .035f) { pit.Mote -= .035f; Infall(pit); }

            pit.Pulse += dt;
            if (pit.Pulse >= .3f) { pit.Pulse -= .3f; Closing(pit, AbyssWide * 1.1f, .4f); }

            Streaming(pit.At, dt);
        }

        Hole RaiseAbyss()
        {
            if (_hill == null || _fx == null) return null;

            var at = new Vector2(LaneX(_board.HorizonLane), MarchY(_board.HorizonMarch));
            var pit = Raise(at, _hill, _fx, Cell * AbyssWide, Cell * AbyssCore);
            pit.Over.SetAsFirstSibling();
            return new Hole { Pit = pit };
        }

        void ScrapAbyss()
        {
            if (_abyss == null) return;
            if (_abyss.Pit.Under) Destroy(_abyss.Pit.Under.gameObject);
            if (_abyss.Pit.Over) Destroy(_abyss.Pit.Over.gameObject);
            _abyss = null;
        }

        /// <summary>The hole tearing open: the light goes out of the sky, a ring slams shut on the place, and the hill is drawn in.</summary>
        void AbyssOpens(Vector2 at)
        {
            Lightup(Pal.Poppy, .12f, .5f);
            Flash(at, HoleWash, 4f);
            ShakeBoard(Cell * .18f);
            if (_abyss != null) Closing(_abyss.Pit, AbyssWide * 2.2f, WellOpens + .15f);

            float reach = Cell * AbyssWide * .6f;
            int drawn = 0;
            for (int i = 0; i < _mob.Count && drawn < 14; i++)
            {
                var mob = _mob[i];
                if (mob == null || !mob.Node || mob.Falling || mob.Boss) continue;

                var from = mob.Node.anchoredPosition;
                if ((from - at).sqrMagnitude > reach * reach) continue;

                Taken(from, at, i);
                drawn++;
            }

            Audio.Sfx("voidblast", .6f, .7f);
            Audio.Sfx("charge", .6f, .45f);
        }

        /// <summary>The hole collapsing on what it holds: everything in it crushed, and the light thrown back out.</summary>
        void AbyssCrushes()
        {
            var at = _abyss != null ? _abyss.Pit.At
                   : new Vector2(LaneX(_board.HorizonLane), MarchY(_board.HorizonMarch));

            Dilate(.5f, .35f);
            Tween.After(AbyssShuts * .7f, () =>
            {
                if (_fx == null) return;

                Lightup(Color.white, .35f, .35f);
                Flash(at, Pal.Radiance, 6f);
                Ripple(at, HoleWash, Cell * 9f, .6f, 0f, .7f);
                Ripple(at, Pal.Sun, Cell * 6f, .5f, .06f);
                Ripple(at, Color.white, Cell * 4f, .4f, .1f, 1f);
                Embers(at, Pal.Sun, 22, Cell * 1.6f, Cell * .32f);
                Twinkles(at, HoleWash, 10, Cell * 2f);
                ShakeBoard(Cell * .32f);

                Audio.Sfx("boom", .85f, .45f);
                Audio.Sfx("thunder", .4f, .7f);
            }, _fx);
        }

        // ------------------------------------------------------------------ fire dragon
        sealed class Dragon
        {
            public RectTransform Host;
            public Image Head, Aura, Crown, Jaw;
            public Image[] Body;
            public Image[] Wings;
            public readonly List<Vector2> Path = new List<Vector2>(64);
            public float Age, Shut = -1f, Breath;
        }

        Dragon _wyrm;

        const int WyrmSegments = 16;
        const float WyrmStep = .32f, WyrmShuts = .4f;

        /// <summary>
        /// The fire dragon, flown off <c>DragonLive</c>: a white-hot head in a gold mane, a body of
        /// sixteen embers following the path the head took, wings of flame beating, fire pouring
        /// off it onto the hill - and, when the model lets go, the whole of it burning out.
        /// </summary>
        void Wyrm(float dt)
        {
            bool live = _board.Holds(SiegePower.FireDragon) && _board.DragonLive;

            if (_wyrm != null && !_wyrm.Host) _wyrm = null;
            if (live && _wyrm != null && _wyrm.Shut >= 0f) ScrapWyrm();
            if (live && _wyrm == null) _wyrm = RaiseWyrm();
            if (_wyrm == null) return;

            var d = _wyrm;
            d.Age += dt;

            if (!live && d.Shut < 0f) d.Shut = 0f;
            if (d.Shut >= 0f)
            {
                d.Shut += dt;
                if (d.Shut >= WyrmShuts) { ScrapWyrm(); return; }
            }

            float alpha = Mathf.Clamp01(d.Age / .2f) * (d.Shut < 0f ? 1f : 1f - d.Shut / WyrmShuts);
            var head = live ? new Vector2(LaneX(_board.DragonLane), MarchY(_board.DragonMarch)) : (d.Path.Count > 0 ? d.Path[0] : Vector2.zero);

            if (live && (d.Path.Count == 0 || (d.Path[0] - head).sqrMagnitude > Cell * Cell * .01f)) d.Path.Insert(0, head);
            while (d.Path.Count > 64) d.Path.RemoveAt(d.Path.Count - 1);

            var ahead = d.Path.Count > 1 ? (d.Path[0] - d.Path[1]).normalized : Vector2.right;
            float facing = Mathf.Atan2(ahead.y, ahead.x) * Mathf.Rad2Deg;

            if (d.Head)
            {
                d.Head.rectTransform.anchoredPosition = head;
                d.Head.color = Pal.A(Color.white, alpha);
            }

            if (d.Aura)
            {
                d.Aura.rectTransform.anchoredPosition = head;
                d.Aura.rectTransform.localScale = Vector3.one * (1f + .12f * Mathf.Sin(d.Age * 30f));
                d.Aura.color = Pal.A(Pal.Sun, .9f * alpha);
            }

            if (d.Crown)
            {
                d.Crown.rectTransform.anchoredPosition = head;
                d.Crown.rectTransform.localRotation = Quaternion.Euler(0f, 0f, d.Age * 420f);
                d.Crown.color = Pal.A(Pal.Marigold, .85f * alpha);
            }

            if (d.Jaw)
            {
                d.Jaw.rectTransform.anchoredPosition = head + ahead * Cell * .7f;
                d.Jaw.rectTransform.localRotation = Quaternion.Euler(0f, 0f, facing - 90f);
                d.Jaw.color = Pal.A(Pal.Radiance, .8f * alpha);
            }

            // The body follows the head's own path, a fixed stride apart, cooling toward the tail.
            float stride = Cell * WyrmStep;
            float travelled = 0f;
            int at = 0;
            for (int s = 0; s < d.Body.Length; s++)
            {
                float want = (s + 1) * stride;
                while (at + 1 < d.Path.Count && travelled + Vector2.Distance(d.Path[at], d.Path[at + 1]) < want)
                {
                    travelled += Vector2.Distance(d.Path[at], d.Path[at + 1]);
                    at++;
                }

                var seg = d.Body[s];
                if (!seg) continue;

                Vector2 spot;
                if (at + 1 < d.Path.Count)
                {
                    float gap = Vector2.Distance(d.Path[at], d.Path[at + 1]);
                    float u = gap > .01f ? (want - travelled) / gap : 0f;
                    spot = Vector2.Lerp(d.Path[at], d.Path[at + 1], u);
                }
                else spot = d.Path.Count > 0 ? d.Path[d.Path.Count - 1] : head;

                float k = s / (float)d.Body.Length;
                float ripple = Mathf.Sin(d.Age * 14f - s * .7f) * Cell * .08f;
                seg.rectTransform.anchoredPosition = spot + new Vector2(0f, ripple);
                seg.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, .35f, k);
                seg.color = Pal.A(Color.Lerp(Color.Lerp(Pal.Sun, Pal.Ember, k * 1.4f), Pal.Poppy, k * k), alpha * Mathf.Lerp(.95f, .4f, k));
            }

            // Wings: two sheets of flame off the shoulders, beating.
            if (d.Wings != null && d.Body.Length > 2 && d.Body[2])
            {
                var shoulder = d.Body[2].rectTransform.anchoredPosition;
                float beat = Mathf.Sin(d.Age * 16f);
                for (int side = 0; side < d.Wings.Length; side++)
                {
                    var wing = d.Wings[side];
                    if (!wing) continue;

                    float sign = side == 0 ? 1f : -1f;
                    float lift = 35f + 30f * beat;
                    var wrt = wing.rectTransform;
                    wrt.anchoredPosition = shoulder;
                    wrt.localRotation = Quaternion.Euler(0f, 0f, facing + 90f * sign + (side == 0 ? -lift : lift) * .5f);
                    wrt.localScale = new Vector3(1f, .75f + .35f * Mathf.Abs(beat), 1f);
                    wing.color = Pal.A(Color.Lerp(Pal.Ember, Pal.Sun, .5f + .5f * beat), .75f * alpha);
                }
            }

            if (d.Shut >= 0f || !live) return;

            // Fire pouring off it onto the hill below, and sparks off its mane.
            d.Breath -= dt;
            if (d.Breath <= 0f)
            {
                d.Breath = .03f;
                Cinder(head + ahead * Cell * .5f, Random.value < .5f ? Pal.Sun : Pal.Ember, Cell * Random.Range(.5f, .8f), .45f,
                       ahead * Cell * 1.8f + new Vector2(0f, -Cell * Random.Range(.8f, 1.6f)));
                if (d.Body.Length > 4 && d.Body[4])
                    Cinder(d.Body[4].rectTransform.anchoredPosition, Pal.Ember, Cell * .4f, .5f,
                           new Vector2(Random.Range(-Cell, Cell) * .4f, Cell * .9f));
            }
        }

        Dragon RaiseWyrm()
        {
            var d = new Dragon { Host = UIKit.Node("Dragon", _fx) };
            var host = d.Host;

            d.Wings = new Image[2];
            for (int i = 0; i < 2; i++)
            {
                d.Wings[i] = Lit("Wing", host, Art.SoftCapsule(40, 120), Color.clear, new Vector2(Cell * 1.1f, Cell * 2.8f));
                d.Wings[i].rectTransform.pivot = new Vector2(.5f, 0f);
            }

            d.Body = new Image[WyrmSegments];
            for (int i = WyrmSegments - 1; i >= 0; i--)
                d.Body[i] = Lit("Scale", host, Art.Glow(96, 1.7f), Color.clear, Vector2.one * Cell * 1.5f);

            d.Aura = Lit("Mane", host, Art.Glow(128, 1.4f), Color.clear, Vector2.one * Cell * 2.6f);
            d.Crown = Lit("Crown", host, StrikeFx.Flare, Color.clear, Vector2.one * Cell * 2.4f);
            d.Jaw = Lit("Jaw", host, Art.SoftCapsule(40, 120), Color.clear, new Vector2(Cell * .5f, Cell * 1.1f));
            d.Head = Lit("Head", host, Art.Glow(96, 2.6f), Color.clear, Vector2.one * Cell * 1.1f);
            return d;
        }

        void ScrapWyrm()
        {
            if (_wyrm == null) return;
            if (_wyrm.Host) Destroy(_wyrm.Host.gameObject);
            _wyrm = null;
        }

        // ------------------------------------------------------------------ golden dome
        RectTransform _dome;
        Image _domeArc, _domeFill, _domeRim;
        Image[] _domeSparks;
        float _domeAge, _domeFlash, _domeShown;

        const int DomeSparks = 6;

        float DomeWide => Span.x * 1.02f;
        float DomeTall => Cell * 3.4f;
        Vector2 DomeFoot => new Vector2(0f, _lineY - Cell * .25f);

        /// <summary>
        /// The dome over the line, as bright as the blows it still has left: a golden arch, a
        /// soft gold air under it, a rim of light along the ground and sparks running over it.
        /// </summary>
        void Dome(float dt)
        {
            if (!_board.Holds(SiegePower.GoldenDome)) { Scrap(ref _dome); return; }

            if (!_dome)
            {
                _dome = UIKit.Box("Dome", _fx, Vector2.zero, new Vector2(.5f, .5f), DomeFoot);
                _dome.SetAsFirstSibling();

                // The upper half of an ellipse, cut by the well's own stencil.
                var tilt = Half("Arch", _dome, DomeWide, far: true);
                tilt.localScale = new Vector3(1f, DomeTall * 2f / DomeWide, 1f);
                _domeArc = Lit("Arc", tilt, StrikeFx.Ring, Color.clear, Vector2.one * DomeWide);
                if (_domeArc == null) _domeArc = Lit("Arc", tilt, Art.Ring(256, 5f), Color.clear, Vector2.one * DomeWide);
                _domeFill = Lit("Air", tilt, Art.Glow(128, 1.1f), Color.clear, Vector2.one * DomeWide * .98f);

                _domeRim = Lit("Rim", _dome, Art.SoftCapsule(40, 120), Color.clear, new Vector2(Cell * .4f, DomeWide));
                _domeRim.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 90f);

                _domeSparks = new Image[DomeSparks];
                for (int i = 0; i < DomeSparks; i++)
                    _domeSparks[i] = Lit("Spark", _dome, Art.Glint(96, 4), Color.clear, Vector2.one * Cell * .6f);

                _domeAge = 0f;
                _domeShown = 0f;
            }

            _domeAge += dt;
            _domeFlash = Mathf.MoveTowards(_domeFlash, 0f, dt * 3f);

            float left = _board.DomeLeft / (float)SiegeSecrets.DomeBlocks;
            _domeShown = Mathf.MoveTowards(_domeShown, left, dt * 2.5f);

            float breathe = .85f + .15f * Mathf.Sin(_domeAge * 2.2f);
            float strength = _domeShown;
            var hue = Color.Lerp(Pal.Gold, Color.white, _domeFlash * .6f);

            _dome.anchoredPosition = DomeFoot;

            if (_domeArc) _domeArc.color = Pal.A(hue, (.25f + .6f * strength) * breathe + _domeFlash * .4f);
            if (_domeFill) _domeFill.color = Pal.A(Pal.Sun, (.04f + .1f * strength) * breathe + _domeFlash * .15f);
            if (_domeRim) _domeRim.color = Pal.A(hue, (.15f + .45f * strength) * breathe);

            // Sparks running over the arch from foot to foot.
            for (int i = 0; i < _domeSparks.Length; i++)
            {
                var spark = _domeSparks[i];
                if (!spark) continue;

                float run = _domeAge * .22f + i / (float)_domeSparks.Length;
                run -= Mathf.Floor(run);
                float a = Mathf.PI * run;

                spark.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * DomeWide * .5f, Mathf.Sin(a) * DomeTall);
                spark.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _domeAge * 90f + i * 40f);
                spark.color = Pal.A(Color.white, Mathf.Sin(run * Mathf.PI) * .9f * strength);
            }
        }

        /// <summary>The dome raised for a new wave: the arch sweeps up out of the ground in a flash of gold.</summary>
        void DomeRaised()
        {
            _domeFlash = 1f;
            if (_fx == null) return;

            var foot = DomeFoot;
            Ripple(foot + new Vector2(0f, Cell), Pal.Gold, DomeWide * .9f, .6f, 0f);
            Twinkles(foot + new Vector2(0f, DomeTall * .6f), Pal.Gold, 10, DomeWide * .4f);
            Audio.Sfx("charge", .35f, 1.5f);
        }

        /// <summary>A blow caught on the dome: a hexagon of light where it struck, the dome flaring - and, on its last block, the dome shattering.</summary>
        void DomeBlocked(Vector2 at, int left)
        {
            _domeFlash = 1f;
            if (_fx == null) return;

            // Where the blow met the arch, above the post it was swung at.
            float x = Mathf.Clamp(at.x, -DomeWide * .48f, DomeWide * .48f);
            float u = x / (DomeWide * .5f);
            var hit = DomeFoot + new Vector2(x, Mathf.Sqrt(Mathf.Max(0f, 1f - u * u)) * DomeTall);

            var hex = Lit("Ward", _fx, Art.HexRing(128, 10f), Pal.A(Pal.Gold, 1f), Vector2.one * Cell * 2.2f);
            var rt = hex.rectTransform;
            rt.anchoredPosition = hit;
            Tween.Run(.4f, Ease.OutQuad, t =>
            {
                if (!hex) return;
                rt.localScale = Vector3.one * Mathf.Lerp(.5f, 1.4f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, t * 60f);
                hex.color = Pal.A(Color.Lerp(Color.white, Pal.Gold, t), 1f - t);
            }, hex).OnDone(() => { if (hex) Destroy(hex.gameObject); });

            Flash(hit, Pal.Gold, 2.6f);
            Ripple(hit, Pal.Gold, Cell * 3.6f, .4f, 0f, 1f);
            Burst.Sparks(_fx, hit, Pal.Gold, 8, Cell * 2f, Cell * .18f, .35f);
            Audio.SfxVaried("land", .45f, .12f);

            if (left > 0) return;

            // The last block: the dome comes down in pieces.
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.PI * (i + .5f) / 14f;
                var spot = DomeFoot + new Vector2(Mathf.Cos(a) * DomeWide * .5f, Mathf.Sin(a) * DomeTall);
                Cinder(spot, i % 2 == 0 ? Pal.Gold : Color.white, Cell * Random.Range(.4f, .7f), .6f,
                       new Vector2(Random.Range(-Cell, Cell) * .6f, -Cell * Random.Range(1.5f, 3f)));
            }

            Lightup(Pal.Gold, .14f, .35f);
            Audio.Sfx("shatter", .6f, .8f);
        }

        // ------------------------------------------------------------------ solar cannon
        RectTransform _sun;
        Image _sunCore, _sunGlow, _sunRays, _sunFlare, _sunSight;
        float _sunAge;

        Vector2 SunAt => new Vector2(0f, MarchY(0f) + Cell * 1.35f);

        /// <summary>The second sun over the hill: a white core in a gold glow, rays turning, a flare - swelling as it charges.</summary>
        void Sun(float dt)
        {
            if (!_board.Holds(SiegePower.SolarCannon)) { Scrap(ref _sun); return; }

            if (!_sun)
            {
                _sun = UIKit.Box("Second sun", _fx, Vector2.zero, new Vector2(.5f, .5f), SunAt);
                _sunSight = Lit("Sight", _fx, Art.Shaft(64, 64, 1.4f, .2f), Color.clear, Vector2.one);
                _sunSight.transform.SetParent(_sun, false);
                _sunGlow = Lit("Glow", _sun, Art.Glow(128, 1.3f), Color.clear, Vector2.one * Cell * 4.6f);
                _sunRays = Lit("Rays", _sun, Art.Rays(256, 12), Color.clear, Vector2.one * Cell * 4.2f);
                _sunFlare = Lit("Flare", _sun, StrikeFx.Flare, Color.clear, Vector2.one * Cell * 3.2f);
                _sunCore = Lit("Core", _sun, Art.Glow(96, 2.6f), Color.clear, Vector2.one * Cell * 1.5f);
                _sunAge = 0f;

                Lightup(Pal.Sun, .12f, .6f);
            }

            _sunAge += dt;
            float grown = Ease.OutBack(Mathf.Clamp01(_sunAge / .6f));
            float charge = _board.SolarCharge;
            float pulse = 1f + .05f * Mathf.Sin(_sunAge * 3f) + charge * (.35f + .1f * Mathf.Sin(_sunAge * 40f));

            _sun.anchoredPosition = SunAt;
            _sun.localScale = Vector3.one * Mathf.Max(0f, grown) * pulse;

            if (_sunGlow) _sunGlow.color = Pal.A(Color.Lerp(Pal.Ember, Pal.Sun, .5f + .5f * charge), .55f + .35f * charge);
            if (_sunRays)
            {
                _sunRays.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _sunAge * (12f + charge * 160f));
                _sunRays.color = Pal.A(Pal.Sun, .55f + .4f * charge);
            }
            if (_sunFlare)
            {
                _sunFlare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_sunAge * (20f + charge * 200f));
                _sunFlare.color = Pal.A(Pal.Radiance, .5f + .5f * charge);
            }
            if (_sunCore) _sunCore.color = Pal.A(Color.white, .9f);

            // While it charges, a thread of light already lies down the lane it will burn.
            if (_sunSight)
            {
                _sunSight.enabled = charge > 0f;
                if (charge > 0f)
                {
                    float x = LaneX(_board.SolarLane) - SunAt.x;
                    var foot = new Vector2(x, _lineY + Cell * .4f - SunAt.y) / Mathf.Max(.01f, _sun.localScale.x);
                    var srt = _sunSight.rectTransform;
                    float len = foot.magnitude;
                    srt.anchoredPosition = foot * .5f;
                    srt.sizeDelta = new Vector2(Cell * (.25f + .3f * charge), len);
                    srt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(foot.y, foot.x) * Mathf.Rad2Deg - 90f);
                    _sunSight.color = Pal.A(Pal.Sun, .25f + .55f * charge * (.6f + .4f * Mathf.Sin(_sunAge * 60f)));

                    if (Random.value < dt * 30f)
                    {
                        var from = SunAt + Random.insideUnitCircle.normalized * Cell * 3f;
                        Cinder(from, Pal.Sun, Cell * .4f, .3f, (SunAt - from) * 3f);
                    }
                }
            }
        }

        void SolarCharging(float lane)
        {
            Audio.Sfx("charge", .65f, .7f);
            Lightup(Pal.Sun, .08f, SiegeSecrets.SolarCharge);
        }

        /// <summary>
        /// The second sun fires: a column of light four layers deep from the sun to the line down
        /// the whole lane, the sky blown white, the ground scorched along it and the board thrown.
        /// </summary>
        void SolarFires(float lane)
        {
            if (_fx == null) return;

            var top = SunAt;
            var foot = new Vector2(LaneX(lane), _lineY + Cell * .2f);
            var span = foot - top;
            float len = span.magnitude;
            float turn = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;

            var host = UIKit.Node("Solar beam", _fx);
            var layers = new (float wide, Color tint, float alpha, float power)[]
            {
                (3.6f, Pal.Ember, .45f, .8f),
                (2.4f, Pal.Sun, .6f, 1.1f),
                (1.4f, Pal.Radiance, .8f, 1.5f),
                (.55f, Color.white, 1f, 2f),
            };

            var images = new Image[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                images[i] = Lit("Layer", host, Art.Shaft(64, 64, layers[i].power), Color.clear, Vector2.one);
                var rt = images[i].rectTransform;
                rt.anchoredPosition = (top + foot) * .5f;
                rt.localRotation = Quaternion.Euler(0f, 0f, turn);
            }

            const float Life = .7f;
            Tween.Run(Life, Ease.Linear, t =>
            {
                if (!host) return;
                float open = Ease.OutBack(Mathf.Clamp01(t / .12f));
                float close = 1f - Ease.InQuad(Mathf.Clamp01((t - .45f) / .55f));
                float flick = 1f + .08f * Mathf.Sin(t * 120f);

                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    if (!img) continue;
                    bool core = i == images.Length - 1;
                    float across = layers[i].wide * Cell * Mathf.Max(0f, open) * (core ? Mathf.Max(close, .3f) : close) * flick;
                    img.rectTransform.sizeDelta = new Vector2(across, len + Cell);
                    img.color = Pal.A(layers[i].tint, layers[i].alpha * (core ? Mathf.Clamp01(close + .2f) : close));
                }
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });

            Tween.After(Life + .1f, () => { if (host) Destroy(host.gameObject); }, host);

            Dilate(.45f, .4f);
            Lightup(Color.white, .45f, .5f);
            Lightup(Pal.Sun, .25f, 1.1f);
            Flash(top, Pal.Radiance, 6f);
            Flash(foot, Pal.Sun, 6f);
            Answer(foot, Pal.Sun, 5f, StrikeFx.Crack, 4f);
            Ripple(foot, Pal.Sun, Cell * 8f, .6f, 0f);
            Ripple(foot, Color.white, Cell * 5f, .45f, .05f, 1f);

            for (int i = 0; i < 10; i++)
            {
                var along = Vector2.Lerp(top, foot, i / 9f);
                Cinder(along, i % 2 == 0 ? Pal.Sun : Color.white, Cell * Random.Range(.5f, .9f), .5f,
                       new Vector2(Random.Range(-1f, 1f) * Cell * 2f, Random.Range(-.5f, 1f) * Cell));
            }

            Embers(foot, Pal.Sun, 24, Cell * 1.6f, Cell * .34f);
            ShakeBoard(Cell * .4f);

            Audio.Sfx("boom", .9f, .55f);
            Audio.Sfx("thunder", .5f, 1.1f);
            Audio.Sfx("charge", .4f, 1.8f);
        }

        // ------------------------------------------------------------------ armageddon
        /// <summary>The sky turning red over the hill for the length of the shower's first fall, and a rumble under it.</summary>
        void DoomSky(float over)
        {
            Dilate(.65f, .5f);
            Lightup(Pal.Poppy, .26f, over + .9f);
            Lightup(Pal.Ember, .14f, over + 1.6f);
            ShakeBoard(Cell * .1f);
            Audio.Sfx("thunder", .7f, .6f);
            Audio.Sfx("whoosh", .5f, .45f);
        }

        /// <summary>A rock landing: lighter than a meteor card's, because eight of them come at once.</summary>
        void Impact(Vector2 at, float scale)
        {
            Flash(at, Pal.Sun, 3.2f * scale);
            Boom(at, Blast("boom_fire"), Cell * 2.8f * scale);
            Ripple(at, MeteorHue, Cell * 4.4f * scale, .45f, 0f);
            Embers(at, Pal.Ember, Mathf.RoundToInt(10 * scale), Cell * .8f * scale, Cell * .28f);
            ShakeBoard(Cell * .1f * scale);
            Audio.SfxVaried("boom", .45f * scale, .1f);
        }

        /// <summary>A bomblet lobbed from a post's muzzle onto a box, over its fuse.</summary>
        void Lob(int ward, Vector2 at, float over)
        {
            if (ward < 0 || _posts == null || ward >= _posts.Length) return;

            var from = Muzzle(ward) + new Vector2(0f, Cell * .3f);
            Fly(from, at, Pal.Poppy, Cell * .45f, over, Random.value < .5f ? .4f : -.4f, Pal.Hollow, () =>
            {
                if (this == null || _fx == null) return;
                Pop(at, Pal.Poppy, 1.4f, .2f);
            });
        }

        // ------------------------------------------------------------------ time stop
        RectTransform _clock;
        Image _clockVeil, _clockFace, _clockInner, _clockHour, _clockMinute;
        Image[] _clockTicks;
        float _clockAge, _clockShown;

        Vector2 ClockAt => new Vector2(0f, MarchY(.45f));

        /// <summary>
        /// Stopped time, drawn off <c>TimeStopLeft</c>: the hill washed cold, and a great clock
        /// face over it whose hands spin backwards and slow to a stop.
        /// </summary>
        void Chrono(float dt)
        {
            if (!_board.Holds(SiegePower.TimeStop)) { Scrap(ref _clock); return; }

            bool stopped = _board.TimeStopLeft > 0f;
            _clockShown = Mathf.MoveTowards(_clockShown, stopped ? 1f : 0f, dt * (stopped ? 4f : 3f));

            if (_clockShown <= 0f)
            {
                if (_clock) _clock.gameObject.SetActive(false);
                return;
            }

            if (!_clock)
            {
                _clock = UIKit.Box("Stopped time", _fx, Vector2.zero, new Vector2(.5f, .5f), Vector2.zero);
                _clockVeil = Lit("Veil", _clock, Art.Round(4), Color.clear, Span);
                _clockFace = Lit("Face", _clock, Art.Ring(256, 6f), Color.clear, Vector2.one * Cell * 6.6f);
                _clockInner = Lit("Inner", _clock, Art.Ring(256, 3f), Color.clear, Vector2.one * Cell * 5.4f);

                _clockTicks = new Image[12];
                for (int i = 0; i < 12; i++)
                {
                    _clockTicks[i] = Lit("Tick", _clock, Art.Capsule(24, 96), Color.clear,
                                         new Vector2(Cell * (i % 3 == 0 ? .2f : .12f), Cell * (i % 3 == 0 ? .7f : .45f)));
                    float a = i * Mathf.PI / 6f;
                    _clockTicks[i].rectTransform.anchoredPosition = ClockAt + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Cell * 2.95f;
                    _clockTicks[i].rectTransform.localRotation = Quaternion.Euler(0f, 0f, -a * Mathf.Rad2Deg);
                }

                _clockHour = Lit("Hour", _clock, Art.Capsule(24, 96), Color.clear, new Vector2(Cell * .26f, Cell * 1.7f));
                _clockMinute = Lit("Minute", _clock, Art.Capsule(24, 96), Color.clear, new Vector2(Cell * .18f, Cell * 2.5f));
                _clockHour.rectTransform.pivot = _clockMinute.rectTransform.pivot = new Vector2(.5f, .08f);
            }

            _clock.gameObject.SetActive(true);
            if (stopped) _clockAge += dt;

            float shown = _clockShown;
            float into = Mathf.Clamp01(_clockAge / SiegeSecrets.TimeFor);
            float slowing = 1f - Ease.OutCubic(into);

            if (_clockVeil)
            {
                _clockVeil.rectTransform.sizeDelta = Span;
                _clockVeil.color = Pal.A(ChronoHue, .16f * shown);
            }

            if (_clockFace)
            {
                _clockFace.rectTransform.anchoredPosition = ClockAt;
                _clockFace.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, Ease.OutBack(shown));
                _clockFace.color = Pal.A(ChronoHue, .75f * shown);
            }

            if (_clockInner)
            {
                _clockInner.rectTransform.anchoredPosition = ClockAt;
                _clockInner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _clockAge * 25f);
                _clockInner.color = Pal.A(Color.white, .35f * shown);
            }

            for (int i = 0; i < _clockTicks.Length; i++)
                if (_clockTicks[i]) _clockTicks[i].color = Pal.A(Color.Lerp(ChronoHue, Color.white, .4f), .85f * shown);

            // Backwards, fast at first and slowing to a stop: time being held, not passing.
            float minute = 720f * (slowing * slowing) + _clockAge * 40f;
            if (_clockMinute)
            {
                _clockMinute.rectTransform.anchoredPosition = ClockAt;
                _clockMinute.rectTransform.localRotation = Quaternion.Euler(0f, 0f, minute);
                _clockMinute.color = Pal.A(Color.white, .95f * shown);
            }

            if (_clockHour)
            {
                _clockHour.rectTransform.anchoredPosition = ClockAt;
                _clockHour.rectTransform.localRotation = Quaternion.Euler(0f, 0f, minute / 12f + 60f);
                _clockHour.color = Pal.A(ChronoHue, .95f * shown);
            }
        }

        /// <summary>Time stopping: a shock of cold light from the clock's face, the sky washed, a deep sound.</summary>
        void TimeStops()
        {
            _clockAge = 0f;

            Dilate(.4f, .3f);
            Lightup(Color.white, .3f, .3f);
            Lightup(ChronoHue, .2f, .9f);
            Flash(ClockAt, ChronoHue, 5f);
            Ripple(ClockAt, ChronoHue, Span.x * 1.3f, .7f, 0f, 1f);
            Ripple(ClockAt, Color.white, Span.x * .8f, .5f, .08f, 1f);
            Twinkles(ClockAt, ChronoHue, 12, Cell * 3f);

            Audio.Sfx("charge", .7f, .4f);
            Audio.Sfx("whoosh", .5f, .5f);
        }

        /// <summary>Time starting again: the held breath let go in one ring outward.</summary>
        void TimeStarts()
        {
            Ripple(ClockAt, ChronoHue, Span.x * 1.6f, .6f, 0f, 1f);
            Flash(ClockAt, Color.white, 3.6f);
            Audio.Sfx("whoosh", .5f, 1.4f);
        }

        // ------------------------------------------------------------------ titan shells, deadeye, fortress
        /// <summary>A sniper's round: a red sight laid on the mark, a gold tracer down it, and a crosshair snapping shut.</summary>
        void Sniped(int ward, Mob mob, Vector2 to, System.Action land)
        {
            if (_fx == null || ward < 0) { land(); return; }

            var from = Muzzle(ward);
            var span = to - from;

            var sight = Lit("Sight", _fx, Art.Capsule(24, 96), Pal.A(Pal.Poppy, 0f), Vector2.one);
            var srt = sight.rectTransform;
            srt.anchoredPosition = (from + to) * .5f;
            srt.sizeDelta = new Vector2(Cell * .07f, span.magnitude);
            srt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f);

            var cross = Lit("Cross", _fx, StrikeFx.Ring, Pal.A(Pal.Poppy, 0f), Vector2.one * Cell * 1.8f);
            if (cross != null) cross.rectTransform.anchoredPosition = to;

            Tween.Run(.22f, Ease.Linear, t =>
            {
                if (!sight) return;
                sight.color = Pal.A(Pal.Poppy, .8f * Mathf.Sin(t * Mathf.PI));
                if (cross)
                {
                    cross.rectTransform.localScale = Vector3.one * Mathf.Lerp(2f, .6f, Ease.OutCubic(t));
                    cross.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 90f);
                    cross.color = Pal.A(Pal.Poppy, t);
                }
            }, sight).OnDone(() =>
            {
                if (sight) Destroy(sight.gameObject);
                if (cross) Destroy(cross.gameObject);
                if (this == null || _fx == null) return;

                Fly(from, to, Pal.Gold, Cell * .5f, .06f, 0f, Color.clear, () =>
                {
                    if (this == null) return;
                    Flash(to, Pal.Gold, 2.6f);
                    Burst.Sparks(_fx, to, Pal.Gold, 8, Cell * 2f, Cell * .16f, .3f);
                    if (mob != null && mob.Node != null) Tween.Shake(mob.Node, Cell * .15f, .2f);
                    land();
                });
                Audio.SfxVaried("zap", .5f, .1f);
            });

            Tween.After(.4f, () => { if (sight) Destroy(sight.gameObject); if (cross) Destroy(cross.gameObject); }, sight);
        }

        /// <summary>A fortress wall mended for the wave: stone thrown up round the post and gold rising off it.</summary>
        void Walled(int ward)
        {
            if (ward < 0 || _posts == null || ward >= _posts.Length || _fx == null) return;

            var at = new Vector2(PostX(ward), _lineY + Cell * .3f);
            Ripple(at, Pal.Parchment, Cell * 3.4f, .5f, 0f);
            Embers(at, Pal.Parchment, 8, Cell * .6f, Cell * .26f);

            for (int i = 0; i < 6; i++)
                Cinder(at + new Vector2(Random.Range(-Cell, Cell) * .6f, 0f), Pal.Gold, Cell * Random.Range(.35f, .55f), .7f,
                       new Vector2(0f, Cell * Random.Range(1.4f, 2.2f)));

            if (!Flourished(SiegeVia.Fortress, -1)) Audio.SfxVaried("land", .4f, .1f);
        }

        // ------------------------------------------------------------------ ball lightning
        sealed class Ball
        {
            public RectTransform Host;
            public Image Core, Aura;
            public Lightning.Layered Crackle;
            public float Clock;
            public int Seed;
            public bool Seen;
        }

        readonly Dictionary<int, Ball> _balls = new Dictionary<int, Ball>(4);
        readonly List<int> _ballsGone = new List<int>(4);

        /// <summary>The balls of lightning, one a post at most, drawn off <c>SiegeBoard.Orbs</c>.</summary>
        void Balls(float dt)
        {
            if (_balls.Count == 0 && !_board.Holds(SiegePower.BallLightning)) return;

            foreach (var ball in _balls.Values) ball.Seen = false;

            var orbs = _board.Orbs;
            for (int i = 0; i < orbs.Count; i++)
            {
                var orb = orbs[i];
                if (!_balls.TryGetValue(orb.Ward, out var ball) || ball == null || !ball.Host)
                {
                    ball = new Ball { Host = UIKit.Node("Ball", _fx), Seed = Random.Range(0, 1 << 20) };
                    ball.Aura = Lit("Aura", ball.Host, Art.Glow(128, 1.5f), Color.clear, Vector2.one * Cell * 2.4f);
                    ball.Core = Lit("Core", ball.Host, Art.Glow(96, 2.6f), Color.clear, Vector2.one * Cell * .9f);
                    ball.Crackle = Lightning.Grow("Crackle", ball.Host, StormHue, Cell * .04f, haloAlpha: .4f);
                    _balls[orb.Ward] = ball;
                }

                ball.Seen = true;
                var at = new Vector2(LaneX(orb.Lane), MarchY(orb.March));

                if (ball.Aura)
                {
                    ball.Aura.rectTransform.anchoredPosition = at;
                    ball.Aura.rectTransform.localScale = Vector3.one * (1f + .15f * Mathf.Sin(Time.unscaledTime * 25f + orb.Ward));
                    ball.Aura.color = Pal.A(StormHue, .8f);
                }

                if (ball.Core)
                {
                    ball.Core.rectTransform.anchoredPosition = at;
                    ball.Core.color = Pal.A(Color.white, 1f);
                }

                ball.Clock -= dt;
                if (ball.Crackle != null && ball.Crackle.Host != null)
                {
                    if (ball.Clock <= 0f)
                    {
                        ball.Clock = .05f;
                        ball.Crackle.Nova(at, Cell * .3f, Cell * 1.3f, 4 + (ball.Seed & 1), Cell, .6f, ++ball.Seed);
                    }

                    ball.Crackle.Group.alpha = .55f + .45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 41f + orb.Ward));
                }

                if (Random.value < dt * 20f)
                    Cinder(at, StormHue, Cell * .35f, .3f, new Vector2(Random.Range(-Cell, Cell) * .3f, -Cell * .6f));
            }

            _ballsGone.Clear();
            foreach (var pair in _balls) if (!pair.Value.Seen) _ballsGone.Add(pair.Key);
            foreach (int key in _ballsGone)
            {
                var ball = _balls[key];
                if (ball.Host)
                {
                    var spot = ball.Core ? ball.Core.rectTransform.anchoredPosition : Vector2.zero;
                    Flash(spot, StormHue, 2.4f);
                    Destroy(ball.Host.gameObject);
                }

                _balls.Remove(key);
            }
        }

        /// <summary>Where the ball nearest <paramref name="to"/> is drawn, or the post-ward side of it when none is.</summary>
        Vector2 NearestBall(Vector2 to)
        {
            Vector2 best = to + new Vector2(0f, -Cell);
            float near = float.MaxValue;

            foreach (var ball in _balls.Values)
            {
                if (ball == null || !ball.Core) continue;
                var at = ball.Core.rectTransform.anchoredPosition;
                float gap = (at - to).sqrMagnitude;
                if (gap >= near) continue;
                near = gap;
                best = at;
            }

            return best;
        }

        // ------------------------------------------------------------------ diamond dust
        sealed class Flurry
        {
            public RectTransform Host;
            public Image Swirl, Haze;
            public Image[] Flakes;
            public float Age;
            public bool Seen;
        }

        readonly Dictionary<int, Flurry> _flurries = new Dictionary<int, Flurry>(4);
        readonly List<int> _flurriesGone = new List<int>(4);

        /// <summary>The blizzards, one a box, drawn off <c>SiegeBoard.Blizzards</c>: a white whirl, a haze and flakes circling in it.</summary>
        void Snow(float dt)
        {
            if (_flurries.Count == 0 && !_board.Holds(SiegePower.DiamondDust)) return;

            foreach (var f in _flurries.Values) f.Seen = false;

            var storms = _board.Blizzards;
            for (int i = 0; i < storms.Count; i++)
            {
                var storm = storms[i];
                int key = storm.Row * SiegeTuning.Lanes + storm.Lane;
                var at = BoxAt(storm.Lane, storm.Row);

                if (!_flurries.TryGetValue(key, out var flurry) || flurry == null || !flurry.Host)
                {
                    flurry = new Flurry { Host = UIKit.Box("Blizzard", _fx, Vector2.zero, new Vector2(.5f, .5f), at) };
                    flurry.Haze = Lit("Haze", flurry.Host, Art.Glow(128, 1.2f), Color.clear,
                                      new Vector2(BoxWide * 3f, BoxWide * 3f * GroundSquash * 1.4f));
                    flurry.Swirl = Lit("Swirl", flurry.Host, Art.Whorl(96, 14f, .38f), Color.clear,
                                       new Vector2(BoxWide * 2.6f, BoxWide * 2.6f * GroundSquash * 1.4f));
                    flurry.Flakes = new Image[10];
                    for (int k = 0; k < flurry.Flakes.Length; k++)
                        flurry.Flakes[k] = Lit("Flake", flurry.Host, Art.Glint(96, 6), Color.clear, Vector2.one * Cell * Random.Range(.25f, .45f));
                    _flurries[key] = flurry;
                }

                flurry.Seen = true;
                flurry.Age += dt;
                flurry.Host.anchoredPosition = at;

                float life = storm.For > 0f ? Mathf.Clamp01(storm.Left / storm.For) : 0f;
                float shown = Mathf.Clamp01(flurry.Age / .25f) * Mathf.Clamp01(life * 4f);

                if (flurry.Haze) flurry.Haze.color = Pal.A(FrostHue, .35f * shown);
                if (flurry.Swirl)
                {
                    flurry.Swirl.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -flurry.Age * 300f);
                    flurry.Swirl.color = Pal.A(Color.white, .55f * shown);
                }

                float reach = BoxWide * 1.2f;
                for (int k = 0; k < flurry.Flakes.Length; k++)
                {
                    var flake = flurry.Flakes[k];
                    if (!flake) continue;

                    float a = flurry.Age * (3f + k * .3f) + k * 1.9f;
                    float r = reach * (.3f + .7f * ((k * .37f) % 1f));
                    flake.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * GroundSquash + Mathf.Sin(a * 2f) * Cell * .2f);
                    flake.rectTransform.localRotation = Quaternion.Euler(0f, 0f, flurry.Age * 200f + k * 30f);
                    flake.color = Pal.A(Color.white, .9f * shown);
                }
            }

            _flurriesGone.Clear();
            foreach (var pair in _flurries) if (!pair.Value.Seen) _flurriesGone.Add(pair.Key);
            foreach (int key in _flurriesGone)
            {
                var flurry = _flurries[key];
                if (flurry.Host)
                {
                    Twinkles(flurry.Host.anchoredPosition, Pal.Glass, 6, Cell);
                    Destroy(flurry.Host.gameObject);
                }

                _flurries.Remove(key);
            }
        }

        // ------------------------------------------------------------------ prism lance
        RectTransform _crystal;
        Image _crystalBody, _crystalGlow, _crystalFlare;
        float _crystalAge;

        Vector2 PrismAt => new Vector2(0f, _lineY + Cell * 2.1f);

        /// <summary>The prism over the line: a crystal turning through the spectrum, brighter and faster as it charges.</summary>
        void Crystal(float dt)
        {
            if (!_board.Holds(SiegePower.PrismLance)) { Scrap(ref _crystal); return; }

            if (!_crystal)
            {
                _crystal = UIKit.Box("Prism", _fx, Vector2.zero, new Vector2(.5f, .5f), PrismAt);
                _crystalGlow = Lit("Glow", _crystal, Art.Glow(128, 1.6f), Color.clear, Vector2.one * Cell * 3f);
                _crystalFlare = Lit("Flare", _crystal, StrikeFx.Flare, Color.clear, Vector2.one * Cell * 2.6f);
                _crystalBody = Lit("Crystal", _crystal, Art.Crystal(128), Color.clear, new Vector2(Cell * 1.1f, Cell * 1.6f));
                _crystalAge = 0f;
            }

            _crystalAge += dt;
            float charge = _board.PrismCharge;
            float grown = Ease.OutBack(Mathf.Clamp01(_crystalAge / .5f));
            var hue = Spectrum[Mathf.FloorToInt(_crystalAge * (2f + charge * 10f)) % Spectrum.Length];
            var next = Spectrum[(Mathf.FloorToInt(_crystalAge * (2f + charge * 10f)) + 1) % Spectrum.Length];
            float blend = (_crystalAge * (2f + charge * 10f)) % 1f;
            var tint = Color.Lerp(hue, next, blend);

            _crystal.anchoredPosition = PrismAt + new Vector2(0f, Mathf.Sin(_crystalAge * 1.6f) * Cell * .12f);
            _crystal.localScale = Vector3.one * Mathf.Max(0f, grown) * (1f + charge * .4f);

            if (_crystalBody)
            {
                _crystalBody.color = Pal.A(Color.Lerp(tint, Color.white, .5f), .95f);
                _crystalBody.rectTransform.localScale = new Vector3(Mathf.Cos(_crystalAge * (1.5f + charge * 8f)), 1f, 1f);
            }

            if (_crystalGlow) _crystalGlow.color = Pal.A(tint, .45f + .45f * charge);
            if (_crystalFlare)
            {
                _crystalFlare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _crystalAge * (30f + charge * 400f));
                _crystalFlare.color = Pal.A(Color.white, .3f + .6f * charge);
            }
        }

        /// <summary>The prism charging: a beam from every standing post into it for the length of the charge.</summary>
        void PrismCharges(float over)
        {
            int posts = _posts != null ? _posts.Length : 0;
            for (int w = 0; w < posts; w++)
            {
                if (w >= _board.Wards.Count || !_board.Wards[w].Alive) continue;

                for (int n = 0; n < 3; n++)
                    Lase(Muzzle(w), PrismAt, Spectrum[(w * 2 + n) % Spectrum.Length], .12f, n * over / 3f, n == 0);
            }

            Audio.Sfx("charge", .55f, 1.2f);
        }

        /// <summary>The prism splitting light: a white flash and the spectrum thrown out in rings, the beams following.</summary>
        void PrismBursts()
        {
            var at = PrismAt;
            Flash(at, Color.white, 4.6f);
            for (int i = 0; i < Spectrum.Length; i++)
                Ripple(at, Spectrum[i], Cell * (3f + i * .7f), .55f, i * .03f, 1f);
            Twinkles(at, Pal.Radiance, 10, Cell * 1.6f);
            Lightup(Color.white, .16f, .3f);
            ShakeBoard(Cell * .12f);

            Audio.Sfx("arc", .6f, 1.3f);
            Audio.Sfx("charge", .4f, 2f);
        }

        // ------------------------------------------------------------------ comets off the field
        /// <summary>
        /// Every comet and rainbow bolt a beat threw (<see cref="SiegeBeat.Comets"/>): flown from
        /// the gem it left to the body it was thrown at, following that body as it walks, over the
        /// flight the model books. Where it lands is the model's volley's to draw. Nothing on a
        /// beat without one, which is every beat but a build's.
        /// </summary>
        void Comets(SiegeBeat beat)
        {
            if (beat == null || beat.Comets.Count == 0 || _fx == null) return;

            for (int i = 0; i < beat.Comets.Count; i++)
            {
                var comet = beat.Comets[i];
                var from = CentreOf(comet.Cell);
                bool rainbow = comet.Via == SiegeVia.Rainbow;
                var tint = rainbow ? Color.white : comet.Colour >= 0 ? TintOf(comet.Colour) : Pal.Sun;

                Comet(from, comet.Raider, tint, rainbow, i * .05f);
            }

            Audio.SfxVaried("whoosh", .45f, .12f);
        }

        void Comet(Vector2 from, int raider, Color tint, bool rainbow, float delay)
        {
            var host = UIKit.Node("Comet", _fx);
            var tails = new Image[rainbow ? 3 : 2];
            for (int i = 0; i < tails.Length; i++)
                tails[i] = Lit("Tail", host, Art.SoftCapsule(40, 120), Color.clear, Vector2.one);
            var head = Lit("Head", host, Art.Glow(96, 2.2f), Color.clear, Vector2.one * Cell * .9f);
            var flare = Lit("Flare", host, StrikeFx.Flare, Color.clear, Vector2.one * Cell * 1.6f);

            float flight = Mathf.Max(.2f, SiegeSecrets.CometFlight - delay);
            var last = from;
            float nextSpark = 0f;
            var middle = new Vector2(0f, MarchY(.5f));

            Tween.Run(flight, Ease.Linear, t =>
            {
                if (!host) return;

                // Aimed at wherever the body is now, so it lands on it however far it walked.
                var mob = raider >= 0 ? MobOf(raider) : null;
                var to = mob != null && mob.Node != null ? mob.Node.anchoredPosition : middle;

                // Up off the field and over, then down onto it.
                var straight = Vector2.Lerp(from, to, Ease.InQuad(t));
                var at = straight + new Vector2(0f, Mathf.Sin(t * Mathf.PI) * Cell * 2.2f);
                var dir = at - last;

                if (head) { head.rectTransform.anchoredPosition = at; head.color = Pal.A(Color.white, 1f); }
                if (flare)
                {
                    flare.rectTransform.anchoredPosition = at;
                    flare.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 720f);
                    flare.color = Pal.A(Color.Lerp(tint, Color.white, .3f), .9f);
                }

                if (dir.sqrMagnitude > .01f)
                {
                    float turn = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                    float len = Mathf.Clamp(dir.magnitude * 6f, Cell, Cell * 4f);
                    for (int i = 0; i < tails.Length; i++)
                    {
                        var tail = tails[i];
                        if (!tail) continue;
                        var rt = tail.rectTransform;
                        var side = new Vector2(-dir.normalized.y, dir.normalized.x) * (i - (tails.Length - 1) * .5f) * Cell * .18f;
                        rt.sizeDelta = new Vector2(Cell * (rainbow ? .35f : .6f), len);
                        rt.anchoredPosition = at - dir.normalized * len * .45f + side;
                        rt.localRotation = Quaternion.Euler(0f, 0f, turn);
                        var hue = rainbow ? Spectrum[(i * 2 + Mathf.FloorToInt(t * 12f)) % Spectrum.Length] : (i == 0 ? tint : Pal.Sun);
                        tail.color = Pal.A(hue, .85f);
                    }
                }

                if (t >= nextSpark)
                {
                    nextSpark = t + .08f;
                    var hue = rainbow ? Spectrum[Random.Range(0, Spectrum.Length)] : tint;
                    Cinder(at, hue, Cell * Random.Range(.3f, .5f), .35f, -dir.normalized * Cell * .8f);
                }

                last = at;
            }, host).Delay(delay).OnDone(() =>
            {
                if (host)
                {
                    var spot = head ? head.rectTransform.anchoredPosition : last;
                    Destroy(host.gameObject);
                    if (_fx != null) Burst.Sparks(_fx, spot, rainbow ? Pal.Radiance : tint, 8, Cell * 1.8f, Cell * .16f, .3f);
                }
            });

            Tween.After(delay + flight + .2f, () => { if (host) Destroy(host.gameObject); }, host);
        }
    }
}
