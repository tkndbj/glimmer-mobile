using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// <b>The ladder's and the Infinite lane's long matches, drawn</b> (MODES.md 37fk,
    /// <c>SiegeBoard.LongMatch</c>): four gems spiralling into a gem bomb, the bomb standing on the
    /// field with its fuse alight, the fuse burning down under a tap and the bomb erupting over its
    /// square, and a run of five launching a salvo of rockets that lock on, climb out of the field
    /// and dive onto the hill.
    ///
    /// <para>
    /// <b>Every drawing here is a replay of what the model already decided</b> (invariant 30i): a
    /// merge arrives as a <see cref="SiegeForging"/> of <see cref="SiegeCharm.Bomb"/>, a blast as a
    /// <see cref="SiegeSpark"/> with its cells in the beat's own <see cref="SiegeBeat.Cleared"/>, a
    /// salvo as the beat's <see cref="SiegeBeat.Rockets"/> and each landing as a
    /// <see cref="SiegeRocketHit"/>. Nothing here works out what was taken or what it paid.
    /// </para>
    /// <para>
    /// <b>Additive light out of the strike kit and the bought reels</b> (invariant 37eu), never a
    /// picture of light, and each payoff a <em>sequence</em> rather than a frame (37cn): a merge is
    /// a vortex the run is pulled into, a hexagonal seal stamped as the bomb is struck, and the
    /// fuse catching; a blast is a squeeze, the fire reel, the square it covers flaring as a frame
    /// with flame licking out to every cell in it; a salvo is a launch rail along the run,
    /// rockets climbing out under exhaust and smoke, a lock closing on every mark, and a fireball
    /// on each landing with the player's own number over it.
    /// </para>
    /// <para>
    /// <b>The rockets fly on the run's own seconds</b> (<see cref="Flying"/>, handed what
    /// <c>SiegeBoard.Advance</c> was handed), so a held run holds them and a bent clock bends
    /// them - and the model's landing is what makes one explode, so a rocket can never burst on a
    /// raider the model has not hurt yet.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the figures
        /// <summary>How long a merging run takes to spiral in, and how long the fall waits for the whole merge.</summary>
        const float MergeIn = .24f, MergeFor = .62f;

        /// <summary>
        /// The squeeze before a bomb blows - the beat the eye arrives on - and how long the fall
        /// waits for the whole blast. The clock runs at <see cref="EruptPace"/> for the same window,
        /// for the lance's reason (<c>Dilate</c>): the board is held and the hill with it.
        /// </summary>
        const float BombLead = .09f, EruptFor = .74f, EruptPace = .32f;

        /// <summary>The seconds between two rockets of one salvo leaving the field.</summary>
        const float RocketStagger = .07f;

        /// <summary>The longest a rocket circles its mark waiting for the model to land it, in run seconds.</summary>
        const float RocketLinger = 1.2f;

        /// <summary>How much faster a rocket flies once the model says it has already landed.</summary>
        const float RocketHurry = 5f;

        /// <summary>
        /// Where the fuse's tip is on a gem bomb, in sides of the stone from its centre. Measured off
        /// the cut art (<c>make_siege_art.bomb_gem</c>), so the spark sits on the fuse rather than
        /// beside it.
        /// </summary>
        static readonly Vector2 FuseTip = new Vector2(-.18f, .46f);

        /// <summary>The spark, smoke and fire a rocket is made of, warmer than any gem colour so it reads as fire.</summary>
        static readonly Color SmokeHue = new Color(.78f, .74f, .70f, 1f);

        // ------------------------------------------------------------------ the bomb on the field
        /// <summary>
        /// The light a gem bomb wears while it stands: a warning ring ticking outward, and a fuse
        /// alight at its tip - a flare, a white core and sparks spitting off it. Built once by
        /// <c>Empowered</c> and animated by <see cref="Fizzing"/>.
        /// </summary>
        Image[] FuseParts(RectTransform host, Color tint)
        {
            var parts = new Image[4 + FuseSpits];

            parts[0] = Lit("Tick", host, Art.Ring(128, 6f), Pal.A(Pal.Lift(tint, .5f), 0f), Vector2.one * Cell * 1.05f);
            parts[1] = Lit("Fuse flare", host, StrikeFx.Flare, Pal.A(Pal.Sun, 0f), Vector2.one * Cell * .62f);
            parts[2] = Lit("Fuse heat", host, Art.Glow(96, 1.8f), Pal.A(Pal.Ember, 0f), Vector2.one * Cell * .55f);
            parts[3] = Lit("Fuse core", host, Art.Glow(96, 2.4f), Pal.A(Color.white, 0f), Vector2.one * Cell * .26f);

            for (int i = 0; i < FuseSpits; i++)
                parts[4 + i] = Lit("Spit", host, Art.Spark(64), Pal.A(Pal.Sun, 0f), Vector2.one * Cell * .14f);

            return parts;
        }

        /// <summary>How many sparks a lit fuse spits.</summary>
        const int FuseSpits = 4;

        /// <summary>Where the fuse's tip is, in the gem's own space.</summary>
        Vector2 TipOf => FuseTip * (Cell * GemInset * CharmInset);

        /// <summary>
        /// One frame of a standing bomb. <b>Organic rather than periodic</b>: the fuse flickers on
        /// two incommensurate sines, so a field with two bombs on it never shows them burning in
        /// step, and each spark leaves on its own phase.
        /// </summary>
        void Fizzing(Gem gem, float age, Color tint)
        {
            var parts = gem.PowerParts;
            if (parts == null || parts.Length < 4) return;

            var tip = TipOf;
            float flick = .72f + .17f * Mathf.Sin(age * 31f) + .11f * Mathf.Sin(age * 53f + 1.3f);

            // The tick: a ring leaving the stone once a second, which is what says "live".
            float k = (age % 1.05f) / 1.05f;
            if (parts[0])
            {
                parts[0].rectTransform.localScale = Vector3.one * Mathf.Lerp(.92f, 1.6f, k);
                parts[0].color = Pal.A(Pal.Lift(tint, .5f), (1f - k) * (1f - k) * .6f);
            }

            if (parts[1])
            {
                var rt = parts[1].rectTransform;
                rt.anchoredPosition = tip;
                rt.localRotation = Quaternion.Euler(0f, 0f, -age * 280f);
                rt.localScale = Vector3.one * (.75f + .45f * flick);
                parts[1].color = Pal.A(Pal.Sun, .95f * flick);
            }

            if (parts[2])
            {
                parts[2].rectTransform.anchoredPosition = tip;
                parts[2].rectTransform.localScale = Vector3.one * (.8f + .4f * flick);
                parts[2].color = Pal.A(Pal.Ember, .7f * flick);
            }

            if (parts[3])
            {
                parts[3].rectTransform.anchoredPosition = tip;
                parts[3].rectTransform.localScale = Vector3.one * (.7f + .5f * flick);
                parts[3].color = Pal.A(Color.white, .95f);
            }

            // Sparks thrown up and out of the fuse in a fan, falling back as they fade.
            for (int s = 0; s < FuseSpits; s++)
            {
                var spit = parts[4 + s];
                if (!spit) continue;

                float run = age * 1.7f + s * .27f;
                run -= Mathf.Floor(run);

                float angle = (100f + s * 26f) * Mathf.Deg2Rad;
                var throw_ = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Cell * .62f * run;
                throw_.y -= Cell * .9f * run * run;

                spit.rectTransform.anchoredPosition = tip + throw_;
                spit.color = Pal.A(Color.Lerp(Pal.Sun, Color.white, .4f), Mathf.Sin(run * Mathf.PI) * .95f);
            }
        }

        // ------------------------------------------------------------------ the merge
        /// <summary>Whether <paramref name="forge"/> is a gem bomb this beat merged, rather than a power gem it forged.</summary>
        static bool MergesInto(SiegeBeat beat, int forge)
        {
            for (int i = 0; i < beat.Forged.Count; i++)
                if (beat.Forged[i].Cell == forge && SiegeCharms.IsBomb(beat.Forged[i].Charm)) return true;

            return false;
        }

        /// <summary>
        /// One gem of a merging run going into its bomb: swept round on a curve rather than pulled
        /// straight, spinning and shrinking as it goes, so the four read as being <em>wound</em>
        /// into one thing - and its fuel leaving from the bomb, as a forge's does.
        /// </summary>
        void IntoBomb(Gem gem, int cell, int forge, int paid, Color tint)
        {
            var img = gem.Img;
            var rt = img.rectTransform;
            var from = CentreOf(cell);
            var to = CentreOf(forge);

            var span = to - from;
            var bow = new Vector2(-span.y, span.x).normalized * Cell * .55f * (cell < forge ? 1f : -1f);
            float spin = cell < forge ? 320f : -320f;
            var lift = Color.Lerp(tint, Color.white, .5f);
            float nextCinder = 0f;

            Tween.KillAll(rt);
            Tween.Run(MergeIn, Ease.InQuad, t =>
            {
                if (!rt) return;

                var at = Vector2.Lerp(from, to, t) + bow * Mathf.Sin(t * Mathf.PI) * (1f - t * .4f);
                rt.anchoredPosition = at;
                rt.localScale = Vector3.one * Mathf.Lerp(1f, .28f, t);
                rt.localRotation = Quaternion.Euler(0f, 0f, spin * t);

                if (t < nextCinder) return;
                nextCinder = t + .2f;
                Cinder(at, lift, Cell * .42f, .3f, -span.normalized * Cell * .6f);
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });

            int ward = paid >= 0 && paid < SiegeLayout.Letters.Length ? _layout.WardOf(SiegeLayout.Letters[paid]) : -1;
            if (ward >= 0) Mote(to, ward, tint, MergeIn, Stretched(SiegeTuning.FuelFlight));
        }

        /// <summary>
        /// The merge itself: a vortex opens on the cell and the run is drawn into it, then the bomb
        /// is struck - a white flash, a hexagonal seal stamped and spun away, the stone popping into
        /// its socket and its fuse catching - and the merge's own pay leaves for the turret.
        /// </summary>
        void Merged(SiegeForging forged)
        {
            var at = CentreOf(forged.Cell);
            var tint = TintOf(forged.Colour);
            var lift = Color.Lerp(tint, Color.white, .5f);

            Holding(MergeFor);

            // The draw: a whorl turning against the run's spin, closing as the gems arrive.
            var whorl = Lit("Merge vortex", _fx, Art.Whorl(96, 14f, .38f), Pal.A(lift, 0f), Vector2.one * Cell * 2.7f);
            if (whorl)
            {
                var wrt = whorl.rectTransform;
                wrt.anchoredPosition = at;

                Tween.Run(MergeIn, Ease.InQuad, t =>
                {
                    if (!wrt) return;
                    wrt.localRotation = Quaternion.Euler(0f, 0f, -t * 560f);
                    wrt.localScale = Vector3.one * Mathf.Lerp(1.25f, .45f, t);
                    whorl.color = Pal.A(lift, Mathf.Sin(t * Mathf.PI * .9f) * .9f);
                }, whorl).OnDone(() => { if (whorl) Destroy(whorl.gameObject); });
            }

            Charge(at, tint, MergeIn);

            var gem = forged.Cell >= 0 && forged.Cell < _gems.Count ? _gems[forged.Cell] : null;
            int ward = _layout.WardOf(SiegeLayout.Letters[forged.Colour]);

            Tween.After(MergeIn, () =>
            {
                if (_fx == null) return;

                // The bomb, struck into being where the run met.
                if (gem != null && gem.Img != null)
                {
                    Charmed(gem, forged.Charm);
                    Tween.KillAll(gem.Img.transform);
                    Tween.Pop(gem.Img.transform, .2f, .4f);
                }

                FxPop(at, Pal.Cream, 2.4f, .22f);
                Flash(at, tint, 2.9f);
                Ripple(at, lift, Cell * 3.8f, .45f, 0f, 1f);
                Twinkles(at, tint, 5, Cell * .9f);
                FxSparks(_fx, at, lift, 16, Cell * 2.8f, Cell * .2f, .48f);

                // **The seal**: a hexagon stamped over the new bomb and spun off it, which is the one
                // shape no other payoff on this field draws - so a merge reads as itself.
                var seal = Lit("Merge seal", _fx, Art.HexRing(128, 9f), Pal.A(lift, 1f), Vector2.one * Cell * 1.15f);
                if (seal)
                {
                    var srt = seal.rectTransform;
                    srt.anchoredPosition = at;

                    Tween.Run(.46f, Ease.OutQuad, t =>
                    {
                        if (!srt) return;
                        srt.localScale = Vector3.one * Mathf.Lerp(.9f, 2.6f, t);
                        srt.localRotation = Quaternion.Euler(0f, 0f, t * 75f);
                        seal.color = Pal.A(lift, (1f - t) * (1f - t));
                    }, seal).OnDone(() => { if (seal) Destroy(seal.gameObject); });
                }

                // The fuse catching.
                var tip = at + TipOf;
                FxPop(tip, Pal.Sun, 1.1f, .2f);
                FxSparks(_fx, tip, Pal.Sun, 9, Cell * 1.9f, Cell * .14f, .36f);

                // **The merge's own pay, said separately** (the owner's rule that merging is never
                // free): brighter, bigger motes than a gem's, so the bonus is seen arriving.
                for (int i = 0; ward >= 0 && i < SiegeLongMatch.MergeGems; i++) Bounty(at, ward, tint, i * .07f);

                Audio.Sfx("merge", .55f);
            }, _fx);
        }

        /// <summary>
        /// One gem's worth of the merge's own fuel crossing to its turret: a bright mote with a
        /// white heart and a sparkle trail, landing as the model lands the fuel (the run has already
        /// spent <see cref="MergeIn"/> of the flight spiralling in, so it crosses that much faster).
        /// </summary>
        void Bounty(Vector2 from, int ward, Color tint, float delay)
        {
            var to = new Vector2(PostX(ward), TubeY);
            var lift = Color.Lerp(tint, Color.white, .5f);

            var host = UIKit.Node("Merge pay", _fx);
            Lit("Glow", host, Art.Glow(96, 1.8f), Pal.A(tint, .9f), Vector2.one * Cell * .8f);
            Lit("Core", host, Art.Spark(64), Pal.A(Color.white, 1f), Vector2.one * Cell * .44f);
            host.anchoredPosition = from;

            float flight = Stretched(Mathf.Max(.22f, SiegeTuning.FuelFlight - MergeIn));
            float bow = (ward < _posts.Length / 2 ? -1f : 1f) * Cell * Random.Range(.8f, 1.4f);
            float nextCinder = 0f;

            Tween.Run(flight, Ease.InOutSine, t =>
            {
                if (!host) return;

                var at = Vector2.Lerp(from, to, t);
                at.x += Mathf.Sin(t * Mathf.PI) * bow;
                host.anchoredPosition = at;
                host.localScale = Vector3.one * Mathf.Lerp(.8f, 1.3f, t);

                if (t < nextCinder) return;
                nextCinder = t + .14f;
                Cinder(at, lift, Cell * .3f, .28f, Vector2.zero);
            }, host).Delay(delay).OnDone(() =>
            {
                if (host) Destroy(host.gameObject);
                if (_fx == null) return;

                FxPop(to, lift, 1.2f, .22f);
                if (ward < _posts.Length) Fed(ward, tint);
            });
        }

        // ------------------------------------------------------------------ the fuse burning down
        /// <summary>
        /// A tapped bomb's fuse burning down, over exactly the swap's beat: a flare racing from the
        /// tip into the stone, the bomb shaking and swelling as it goes, sparks spraying off it.
        /// </summary>
        void Lighting(int cell)
        {
            if (cell < 0 || cell >= _gems.Count) return;

            var at = CentreOf(cell);
            var tip = at + TipOf;
            var crown = at + new Vector2(0f, Cell * .2f);
            float burn = SiegeTuning.SwapFor;

            var flare = Lit("Fuse burn", _fx, StrikeFx.Flare, Pal.A(Pal.Sun, 1f), Vector2.one * Cell * .8f);
            if (flare)
            {
                var frt = flare.rectTransform;
                frt.anchoredPosition = tip;

                Tween.Run(burn, Ease.InQuad, t =>
                {
                    if (!frt) return;
                    frt.anchoredPosition = Vector2.Lerp(tip, crown, t);
                    frt.localScale = Vector3.one * (1f + t * .8f);
                    frt.localRotation = Quaternion.Euler(0f, 0f, t * 420f);
                }, flare).OnDone(() => { if (flare) Destroy(flare.gameObject); });
            }

            FxSparks(_fx, tip, Pal.Sun, 10, Cell * 2.4f, Cell * .15f, .34f);

            var gem = _gems[cell];
            if (gem != null && gem.Img != null)
            {
                Tween.Shake(gem.Img.rectTransform, Cell * .07f, burn);
                Tween.Scale(gem.Img.transform, 1.18f, burn, Ease.InQuad);
            }

            Audio.Sfx("charge", .42f, 1.7f);
        }

        // ------------------------------------------------------------------ the blast
        /// <summary>
        /// A gem bomb going off: a squeeze - a white core swelling as the stone tightens - and then
        /// the eruption: the bought fire reel and the colour's own burst, the square it covers
        /// flaring as a frame, flame licking out to every cell in it, the gems in it thrown outward
        /// and bursting as the wave reaches them, the board kicked and the room lit warm.
        /// </summary>
        void Erupted(int cell, int colour, Color tint)
        {
            var at = CentreOf(cell);
            var hot = Color.Lerp(tint, Pal.Ember, .35f);
            var lift = Color.Lerp(tint, Color.white, .5f);
            int reach = SiegeLongMatch.BombReach;

            Dilate(EruptPace, EruptFor);
            Holding(EruptFor);

            // The gems round it come apart as the shockwave reaches them (`ClearDelay`).
            _sprangLead = BombLead;

            // The squeeze.
            var core = Lit("Bomb core", _fx, Art.Glow(128, 2.2f), Pal.A(Color.white, 0f), Vector2.one * Cell * 1.2f);
            if (core)
            {
                var crt = core.rectTransform;
                crt.anchoredPosition = at;

                Tween.Run(BombLead, Ease.InQuad, t =>
                {
                    if (!crt) return;
                    crt.localScale = Vector3.one * Mathf.Lerp(.5f, 2.1f, t);
                    core.color = Pal.A(Color.white, t);
                }, core).OnDone(() =>
                {
                    if (!core) return;
                    Tween.Run(.28f, Ease.OutQuad, t =>
                    {
                        if (!crt) return;
                        crt.localScale = Vector3.one * Mathf.Lerp(2.1f, 3.6f, t);
                        core.color = Pal.A(Color.white, 1f - t);
                    }, core).OnDone(() => { if (core) Destroy(core.gameObject); });
                });
            }

            Tween.After(BombLead, () =>
            {
                if (_fx == null) return;

                Boom(at, Blast("boom_fire"), Cell * (2.6f + 1.2f * reach), 24f);
                ColourBurst(at, colour, Cell * 4.2f);

                FxPop(at, Pal.Cream, 3.4f, .24f);
                Flash(at, hot, 3.1f);
                FxWave(at, Pal.Lift(tint, .6f), 3.3f, .38f);
                FxWave(at, Pal.Sun, 4.8f, .52f);
                Ripple(at, Pal.Ember, Cell * 5.2f, .5f, 0f, 1f);

                Square(at, reach, hot, lift);

                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;

                        int x = cell % Width + dx, y = cell / Width + dy;
                        if (x < 0 || y < 0 || x >= Width || y >= Height) continue;

                        Jet(at, CentreOf(IndexOf(x, y)), hot);
                    }

                Embers(at, hot, 14, Cell * 1.5f, Cell * .3f);
                FxSparks(_fx, at, lift, 26, Cell * 4.4f, Cell * .3f, .72f);
                Lightup(Pal.Ember, .16f, .42f);
                ShakeBoard(Cell * .15f);

                Audio.Sfx("bomb", .7f);
            }, _fx);

            // The gems it takes: thrown outward from the stone, each bursting in fire as the wave
            // reaches it - the clear itself draws the shatter and the fuel leaving for each colour's
            // own turret, which is the half of this the player is paid in.
            var beat = _beatNow;
            if (beat == null) return;

            for (int i = 0; i < beat.Cleared.Count; i++)
            {
                int c = beat.Cleared[i];
                if (c == cell) continue;

                int far = Mathf.Max(Mathf.Abs(c % Width - cell % Width), Mathf.Abs(c / Width - cell / Width));
                if (far > reach) continue;

                var spot = CentreOf(c);
                var gem = c < _gems.Count ? _gems[c] : null;

                if (gem != null && gem.Img != null)
                {
                    var rt = gem.Img.rectTransform;
                    var push = (spot - at).sqrMagnitude > 1f ? (spot - at).normalized : Vector2.up;

                    Tween.After(BombLead, () =>
                    {
                        if (!rt) return;
                        Tween.Move(rt, spot + push * Cell * .3f, WaveStep * far, Ease.OutQuad);
                        Tween.RotateBy(rt, Random.Range(-110f, 110f), WaveStep * far + .12f, Ease.OutQuad);
                    });
                }

                Tween.After(BombLead + far * WaveStep, () =>
                {
                    if (_fx == null) return;
                    Boom(spot, Blast("boom_fire"), Cell * 1.6f, 30f);
                }, _fx);
            }
        }

        /// <summary>
        /// The colour's own burst reel, played once at <paramref name="size"/> - the same bake every
        /// charm detonates in (<c>Detonate</c>), without its sound and its shake, which the blast
        /// says for itself.
        /// </summary>
        void ColourBurst(Vector2 at, int colour, float size)
        {
            var frames = CharmBlast(colour);
            if (frames == null || frames.Length == 0) return;

            var img = UIKit.Img("Bomb blast", _fx, frames[0], Color.white, new Vector2(size, size));
            img.raycastTarget = false;
            img.preserveAspect = true;
            img.rectTransform.anchoredPosition = at;
            img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

            var book = Flipbook.Attach(img, frames, CharmFps, false);
            if (book != null) book.OnFinished = () => { if (img) Destroy(img.gameObject); };
            else Tween.After(.9f, () => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// <b>The square a blast covers, drawn as a frame of fire snapping open to its edge</b> - the
        /// area of effect said out loud, so a player learns where the next bomb should stand by
        /// watching this one go. Four soft bars with a white core in each, held a beat and let go.
        /// </summary>
        void Square(Vector2 at, int reach, Color hot, Color lift)
        {
            float full = Cell * (reach * 2 + 1);
            var host = UIKit.Node("Bomb square", _fx);
            host.anchoredPosition = at;

            var bars = new Image[8];
            for (int i = 0; i < 4; i++)
            {
                bars[i] = Lit("Edge", host, Art.SoftCapsule(40, 120), Pal.A(hot, 0f), Vector2.one);
                bars[4 + i] = Lit("Core", host, Art.Capsule(24, 96), Pal.A(Color.white, 0f), Vector2.one);
            }

            Tween.Run(.5f, Ease.Linear, t =>
            {
                if (!host) return;

                float open = Mathf.Clamp01(t / .22f);
                open = 1f - (1f - open) * (1f - open);
                float side = Mathf.Lerp(Cell * .6f, full, open);
                float alpha = t < .4f ? 1f : 1f - (t - .4f) / .6f;

                for (int i = 0; i < 4; i++)
                {
                    bool across = i < 2;
                    float sign = i % 2 == 0 ? 1f : -1f;
                    var pos = across ? new Vector2(0f, sign * side * .5f) : new Vector2(sign * side * .5f, 0f);
                    float turn = across ? 90f : 0f;

                    var edge = bars[i];
                    if (edge)
                    {
                        edge.rectTransform.anchoredPosition = pos;
                        edge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn);
                        edge.rectTransform.sizeDelta = new Vector2(Cell * .42f, side + Cell * .42f);
                        edge.color = Pal.A(hot, .85f * alpha);
                    }

                    var coreBar = bars[4 + i];
                    if (coreBar)
                    {
                        coreBar.rectTransform.anchoredPosition = pos;
                        coreBar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, turn);
                        coreBar.rectTransform.sizeDelta = new Vector2(Cell * .1f, side);
                        coreBar.color = Pal.A(lift, alpha);
                    }
                }
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });
        }

        /// <summary>One tongue of flame licking out from the bomb to a cell its blast takes.</summary>
        void Jet(Vector2 from, Vector2 to, Color hot)
        {
            var span = to - from;
            float far = span.magnitude;
            if (far < 1f) return;

            var tongue = Lit("Jet", _fx, Art.SoftCapsule(40, 120), Pal.A(hot, 0f), Vector2.one);
            if (!tongue) return;

            var rt = tongue.rectTransform;
            float turn = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;
            var dir = span / far;

            Tween.Run(.36f, Ease.OutQuad, t =>
            {
                if (!rt) return;

                float grow = Mathf.Clamp01(t / .3f);
                float length = far * grow + Cell * .3f;

                rt.anchoredPosition = from + dir * length * .5f;
                rt.localRotation = Quaternion.Euler(0f, 0f, turn);
                rt.sizeDelta = new Vector2(Cell * Mathf.Lerp(.7f, .25f, t), length);
                tongue.color = Pal.A(Color.Lerp(Pal.Sun, hot, t), (1f - t) * .9f);
            }, tongue).OnDone(() => { if (tongue) Destroy(tongue.gameObject); });
        }

        // ------------------------------------------------------------------ the salvo
        /// <summary>One rocket of a salvo in the air: what it is drawn with and where it is in its flight.</summary>
        sealed class Rocket
        {
            public int Id, Raider, Colour;
            public RectTransform Host, Lock;
            public Image Shell, Body, Nose, Flame, Glow, LockRing, LockStar;
            public Vector2 From, Last, Aim;

            /// <summary>Run seconds before it leaves, and how far into its flight it is, of how long.</summary>
            public float Wait, Age, For;

            /// <summary>Which way it swings out of the field: rockets of one salvo fan to both sides.</summary>
            public float Side;

            public float Smoke, Lingered;

            /// <summary>Whether the model has landed it, and what the landing was.</summary>
            public bool Landed;
            public SiegeRocketHit Hit;

            public bool Lit;
        }

        readonly List<Rocket> _rockets = new List<Rocket>(SiegeLongMatch.MostRockets * 2);

        /// <summary>The frame the last landing's sound played on, so a salvo landing together is one sound.</summary>
        int _impactFrame = -1;

        /// <summary>
        /// A beat's salvo leaving the field: the run lit as a launch rail, then each rocket climbing
        /// out of the cell it leaves from a beat after the last, with a lock closing on its mark.
        /// </summary>
        void Launched(SiegeBeat beat)
        {
            if (beat == null || beat.Rockets.Count == 0 || _fx == null) return;

            int first = beat.Rockets[0].Cell, last = first;
            for (int i = 0; i < beat.Rockets.Count; i++)
            {
                first = Mathf.Min(first, beat.Rockets[i].Cell);
                last = Mathf.Max(last, beat.Rockets[i].Cell);
            }

            var tint = TintOf(beat.Rockets[0].Colour);
            Rail(CentreOf(first), CentreOf(last), tint);

            for (int i = 0; i < beat.Rockets.Count; i++) Launch(beat.Rockets[i], i);

            Lightup(tint, .1f, .32f);
            Audio.Sfx("launch", .6f);
        }

        /// <summary>The run a salvo leaves from, lit end to end for a moment - the launch rail.</summary>
        void Rail(Vector2 a, Vector2 b, Color tint)
        {
            var span = b - a;
            var mid = (a + b) * .5f;
            float length = span.magnitude + Cell;
            float turn = Mathf.Atan2(span.y, span.x) * Mathf.Rad2Deg - 90f;

            var host = UIKit.Node("Launch rail", _fx);
            host.anchoredPosition = mid;
            host.localRotation = Quaternion.Euler(0f, 0f, turn);

            var glow = Lit("Glow", host, Art.SoftCapsule(40, 120), Pal.A(tint, 0f), new Vector2(Cell * 1.1f, length));
            var core = Lit("Core", host, Art.Capsule(24, 96), Pal.A(Color.white, 0f), new Vector2(Cell * .2f, length));

            Tween.Run(.42f, Ease.OutQuad, t =>
            {
                if (!host) return;
                float a1 = t < .25f ? t / .25f : 1f - (t - .25f) / .75f;
                if (glow) glow.color = Pal.A(tint, .8f * a1);
                if (core) core.color = Pal.A(Color.white, a1);
                host.localScale = new Vector3(Mathf.Lerp(.6f, 1.15f, t), 1f, 1f);
            }, host).OnDone(() => { if (host) Destroy(host.gameObject); });
        }

        /// <summary>Builds one rocket and its lock, unlit until its turn to leave.</summary>
        void Launch(SiegeRocket fired, int index)
        {
            var tint = TintOf(fired.Colour);
            var lift = Color.Lerp(tint, Color.white, .5f);

            var r = new Rocket
            {
                Id = fired.Id,
                Raider = fired.Raider,
                Colour = fired.Colour,
                From = CentreOf(fired.Cell),
                Wait = index * RocketStagger,
                For = Mathf.Max(.3f, SiegeLongMatch.RocketFlight - index * RocketStagger),
                Side = (index % 2 == 0 ? -1f : 1f) * (.6f + .35f * (index / 2)),
            };

            r.Last = r.From;
            r.Aim = AimOf(r);

            r.Host = UIKit.Node("Rocket", _fx);
            r.Host.anchoredPosition = r.From;
            r.Glow = Lit("Glow", r.Host, Art.Glow(128, 1.6f), Pal.A(tint, 0f), Vector2.one * Cell * 1.6f);
            r.Flame = Lit("Flame", r.Host, StrikeFx.Flare, Pal.A(Pal.Sun, 0f), Vector2.one * Cell * .95f);
            r.Shell = Lit("Shell", r.Host, Art.SoftCapsule(40, 120), Pal.A(tint, 0f), new Vector2(Cell * .36f, Cell * .82f));
            r.Body = Lit("Body", r.Host, Art.Capsule(24, 96), Pal.A(Color.white, 0f), new Vector2(Cell * .15f, Cell * .58f));
            r.Nose = Lit("Nose", r.Host, Art.Glow(96, 2.4f), Pal.A(Color.white, 0f), Vector2.one * Cell * .36f);

            if (r.Flame) r.Flame.rectTransform.anchoredPosition = new Vector2(0f, -Cell * .44f);
            if (r.Nose) r.Nose.rectTransform.anchoredPosition = new Vector2(0f, Cell * .3f);

            // The lock: a ring closing on the mark over the whole flight, with a glint at its heart.
            r.Lock = UIKit.Node("Rocket lock", _fx);
            r.Lock.anchoredPosition = r.Aim;
            r.LockRing = Lit("Ring", r.Lock, StrikeFx.Ring, Pal.A(lift, 0f), Vector2.one * Cell * 1.5f);
            r.LockStar = Lit("Star", r.Lock, StrikeFx.Glint, Pal.A(Color.white, 0f), Vector2.one * Cell * .8f);

            _rockets.Add(r);
        }

        /// <summary>Where a rocket is aimed this frame: its mark if it is still drawn, else where it last was, else mid-hill.</summary>
        Vector2 AimOf(Rocket r)
        {
            var mob = r.Raider >= 0 ? MobOf(r.Raider) : null;
            if (mob != null && mob.Node != null) return mob.Node.anchoredPosition + new Vector2(0f, Cell * .2f);

            return r.Aim != Vector2.zero ? r.Aim : new Vector2(0f, MarchY(.5f));
        }

        /// <summary>
        /// Every rocket in the air, one step of the run's own clock (<paramref name="dt"/> is what
        /// <c>SiegeBoard.Advance</c> was handed): waiting its turn, climbing out of the field and
        /// diving onto its mark along a curve that follows the mark as it walks, shedding smoke and
        /// sparks - and exploding the moment it has both arrived and been landed by the model.
        /// </summary>
        void Flying(float dt)
        {
            if (_rockets.Count == 0) return;

            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];

                if (!r.Host)
                {
                    Drop(r);
                    _rockets.RemoveAt(i);
                    continue;
                }

                if (r.Wait > 0f)
                {
                    r.Wait -= dt;
                    if (r.Wait > 0f) continue;
                }

                if (!r.Lit) Ignite(r);

                r.Age += dt * (r.Landed && r.Age < r.For ? RocketHurry : 1f);
                r.Aim = AimOf(r);

                float t = Mathf.Clamp01(r.Age / r.For);
                var pos = Path(r, t);
                var dir = pos - r.Last;

                r.Host.anchoredPosition = pos;
                if (dir.sqrMagnitude > .01f)
                    r.Host.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);

                float flick = .8f + .2f * Mathf.Sin(r.Age * 70f + r.Id);
                if (r.Flame) r.Flame.rectTransform.localScale = new Vector3(.8f * flick, 1.25f * flick, 1f);

                // Smoke off the tail, and now and then a spark in the rocket's own colour.
                r.Smoke -= dt;
                if (r.Smoke <= 0f && dir.sqrMagnitude > .01f)
                {
                    r.Smoke = .03f;
                    var back = -dir.normalized;
                    Cinder(pos + back * Cell * .45f, SmokeHue, Cell * Random.Range(.36f, .6f), .5f,
                           back * Cell * .5f + new Vector2(Random.Range(-.3f, .3f), .5f) * Cell);

                    if (Random.value < .45f)
                        Cinder(pos + back * Cell * .35f, Color.Lerp(TintOf(r.Colour), Color.white, .4f),
                               Cell * .22f, .32f, back * Cell * 1.4f);
                }

                Locking(r, t);
                r.Last = pos;

                if (t < 1f) continue;

                if (r.Landed)
                {
                    Explode(r, pos);
                    Drop(r);
                    _rockets.RemoveAt(i);
                    continue;
                }

                // Arrived before the model has landed it: it circles the mark, briefly, and goes
                // out on its own if the landing never comes (a run that ended under it).
                r.Lingered += dt;
                if (r.Lingered < RocketLinger) continue;

                Drop(r);
                _rockets.RemoveAt(i);
            }
        }

        /// <summary>
        /// Where a rocket is at <paramref name="t"/> of its flight: a cubic curve that climbs
        /// straight out of the field, swings to its side and comes down on its mark from above -
        /// accelerating all the way, as a rocket does.
        /// </summary>
        Vector2 Path(Rocket r, float t)
        {
            float u = Mathf.Pow(t, 1.35f);

            var p0 = r.From;
            var p1 = r.From + new Vector2(r.Side * Cell * .9f, Cell * 3.2f);
            var p2 = r.Aim + new Vector2(-r.Side * Cell * 1.6f, Cell * 2.6f);
            var p3 = r.Aim;

            float v = 1f - u;
            var at = v * v * v * p0 + 3f * v * v * u * p1 + 3f * v * u * u * p2 + u * u * u * p3;

            // Once it has landed, circle the mark rather than sit on it.
            if (t >= 1f && !r.Landed)
                at += new Vector2(Mathf.Cos(r.Lingered * 9f), Mathf.Sin(r.Lingered * 9f)) * Cell * .3f;

            return at;
        }

        /// <summary>The moment a rocket leaves: lit, a flash and a ring on the cell it left, sparks off the pad.</summary>
        void Ignite(Rocket r)
        {
            r.Lit = true;

            var tint = TintOf(r.Colour);
            var lift = Color.Lerp(tint, Color.white, .5f);

            if (r.Glow) r.Glow.color = Pal.A(tint, .55f);
            if (r.Flame) r.Flame.color = Pal.A(Pal.Sun, 1f);
            if (r.Shell) r.Shell.color = Pal.A(tint, .95f);
            if (r.Body) r.Body.color = Pal.A(Color.white, 1f);
            if (r.Nose) r.Nose.color = Pal.A(Color.white, 1f);

            FxPop(r.From, lift, 1.7f, .22f);
            Ripple(r.From, tint, Cell * 2.2f, .32f, 0f, 1f);
            FxSparks(_fx, r.From, Pal.Sun, 10, Cell * 2.2f, Cell * .16f, .36f);
            Audio.SfxVaried("whoosh", .32f, .08f);
        }

        /// <summary>The lock on a rocket's mark: a ring closing and turning as the rocket comes down.</summary>
        void Locking(Rocket r, float t)
        {
            if (!r.Lock) return;

            r.Lock.anchoredPosition = r.Aim;
            var lift = Color.Lerp(TintOf(r.Colour), Color.white, .4f);
            float alpha = Mathf.Clamp01(t * 3f) * (t > .92f ? (1f - t) / .08f : 1f);

            if (r.LockRing)
            {
                r.LockRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(2.3f, .85f, t);
                r.LockRing.rectTransform.localRotation = Quaternion.Euler(0f, 0f, t * 220f);
                r.LockRing.color = Pal.A(lift, .85f * alpha);
            }

            if (r.LockStar)
            {
                r.LockStar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -t * 300f);
                r.LockStar.color = Pal.A(Color.white, .7f * alpha * (.6f + .4f * Mathf.Sin(t * 40f)));
            }
        }

        /// <summary>
        /// The model has landed these rockets (<see cref="SiegeReport.Rocketed"/>): each one that has
        /// reached its mark explodes now, and one still in the air is told to hurry and explodes on
        /// arrival. A landing with no rocket drawn - a board rebuilt under it - is drawn on the spot.
        /// </summary>
        void Touchdown(IReadOnlyList<SiegeRocketHit> hits)
        {
            for (int h = 0; h < hits.Count; h++)
            {
                var hit = hits[h];
                Rocket flying = null;

                for (int i = 0; i < _rockets.Count; i++)
                    if (_rockets[i].Id == hit.Id) { flying = _rockets[i]; break; }

                if (flying == null)
                {
                    var mob = hit.Raider >= 0 ? MobOf(hit.Raider) : null;
                    var spot = mob != null && mob.Node != null ? mob.Node.anchoredPosition : new Vector2(0f, MarchY(.5f));
                    Explode(new Rocket { Id = hit.Id, Raider = hit.Raider, Colour = 0, Hit = hit, Landed = true }, spot);
                    continue;
                }

                flying.Landed = true;
                flying.Hit = hit;

                if (flying.Lit && flying.Age >= flying.For)
                {
                    Explode(flying, flying.Last);
                    Drop(flying);
                    _rockets.Remove(flying);
                }
            }
        }

        /// <summary>
        /// One rocket's landing: a fireball on the body it struck - the bought fire reel over the
        /// colour's burst, a flash, a ground ring and embers - with the player's own number over it,
        /// or a burst on the ground where an empty hill gave it nothing to hit.
        /// </summary>
        void Explode(Rocket r, Vector2 at)
        {
            if (_fx == null) return;

            var hit = r.Hit;
            var tint = TintOf(r.Colour);
            var hot = Color.Lerp(tint, Pal.Ember, .4f);
            var lift = Color.Lerp(tint, Color.white, .5f);

            var mob = hit.Raider >= 0 ? MobOf(hit.Raider) : null;
            var spot = mob != null && mob.Node != null ? mob.Node.anchoredPosition : at;

            Boom(spot, Blast("boom_fire"), Cell * 2.7f, 26f);
            ColourBurst(spot, r.Colour, Cell * 2.6f);
            FxPop(spot, Pal.Cream, 2.3f, .2f);
            Flash(spot, hot, 2.3f);
            FxWave(spot, lift, 2.6f, .32f);
            Ripple(spot, Pal.Ember, Cell * 3.4f, .42f, 0f);
            Embers(spot, hot, 8, Cell * 1.1f, Cell * .24f);
            FxSparks(_fx, spot, lift, 14, Cell * 3f, Cell * .24f, .5f);

            if (mob != null && mob.Node != null && hit.Damage > 0)
            {
                // The player's own number - drawn bigger and hotter than a bolt's (`Number`).
                Number(hit.Raider, spot, hit.Damage, false, mine: true);
                if (!hit.Killed) Tween.Shake(mob.Node, Cell * .14f, .24f);
            }

            if (_impactFrame == Time.frameCount) return;
            _impactFrame = Time.frameCount;

            ShakeBoard(Cell * .05f);
            Audio.Sfx("impact", .62f, Random.Range(.95f, 1.06f));
        }

        /// <summary>Takes a rocket and its lock off the screen.</summary>
        static void Drop(Rocket r)
        {
            if (r.Host) Destroy(r.Host.gameObject);
            if (r.Lock) Destroy(r.Lock.gameObject);
        }
    }
}
