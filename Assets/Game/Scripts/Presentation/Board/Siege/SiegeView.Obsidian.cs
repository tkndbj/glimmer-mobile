using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The curse's three colours, exactly as <c>Tools/make_obsidian_art.py</c> paints them.
    ///
    /// <b>Its own class rather than three fields on <see cref="SiegeView"/></b> so a fixture can
    /// hold them to the art tool offline: the view's own static state needs a running engine to
    /// initialise, and a palette check that only the Editor can run is a check nobody runs on the
    /// way past (<c>SiegeObsidianTests.TheViewAndTheArtToolShareOnePalette</c>).
    /// </summary>
    public static class CurseLight
    {
        /// <summary>The curse's light: <c>make_obsidian_art.VIOLET</c>.</summary>
        public static readonly Color32 Violet = new Color32(168, 92, 255, 255);

        /// <summary>Its hottest part: <c>make_obsidian_art.LILAC</c>.</summary>
        public static readonly Color32 Lilac = new Color32(232, 206, 255, 255);

        /// <summary>The dark it gathers out of: <c>make_obsidian_art.DEEP</c>.</summary>
        public static readonly Color32 Deep = new Color32(70, 22, 128, 255);
    }

    /// <summary>
    /// The curse (<see cref="SiegeLayout.Obsidian"/>): the cursed stone on the field, the break
    /// that takes every one of them at once, the front the curse rides up the hill on, and the
    /// sigil a hexed body stands in until the hex runs out.
    ///
    /// <para>
    /// <b>Three beats on two boards, and the model books them the same way</b> (invariant 37s).
    /// The break is the field's beat and is drawn off <see cref="SiegeBeat.Broken"/> where the
    /// stones were; the curse falling on the hill is booked <c>FuelLands</c> later and is drawn
    /// off <see cref="SiegeReport.Hex"/> on the frame the model laid it; and the hex a body
    /// carries is a <em>state</em>, read off <see cref="SiegeRaider.Hexed"/> every frame
    /// (invariant 37ek - a lasting thing is a state the body wears, never a string of events).
    /// </para>
    /// <para>
    /// <b>Every piece of light is additive and in the curse's own three colours</b>
    /// (<see cref="Hex"/>, <see cref="HexCore"/>, <see cref="HexDeep"/>, painted by
    /// <c>Tools/make_obsidian_art.py</c> and held to it by <c>SiegeObsidianTests</c>), which is
    /// the strike kit's rule (invariant 37eu): light is added, never laid over. The one thing
    /// drawn <em>normally</em> is the void the break opens before it bursts, because what a
    /// curse gathers is dark, and adding black adds nothing.
    /// </para>
    /// <para>
    /// <b>It spends the window rather than shortening it</b> (invariant 37ac): the break slows
    /// the run's own clock while it is drawn (<see cref="Dilate"/>, 37cq - the model is handed
    /// the seconds, so the slow motion is free) and the fall waits for it, exactly as a lance's
    /// cross does.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the palette
        /// <summary>The curse's light. See <see cref="CurseLight"/>.</summary>
        public static readonly Color Hex = CurseLight.Violet;

        /// <summary>The hottest part of it. See <see cref="CurseLight"/>.</summary>
        public static readonly Color HexCore = CurseLight.Lilac;

        /// <summary>The dark it gathers out of. See <see cref="CurseLight"/>.</summary>
        public static readonly Color HexDeep = CurseLight.Deep;

        /// <summary>What a cell holding an obsidian is drawn as. See <see cref="SiegeLayout.ObsidianFace"/>.</summary>
        const int ObsidianColour = SiegeLayout.ObsidianFace;

        // ------------------------------------------------------------------ the lesson
        /// <summary>
        /// Raised once, the first time an obsidian is standing in its own socket on this field.
        ///
        /// <b>Held until one has settled, for <see cref="Flush"/>'s reason</b> (invariant 6b): a
        /// refill is minted mid-fall, and a lesson raised there rings a gem that is not in the
        /// cell it will land in. <see cref="Brood"/> asks every frame, which is cheap because
        /// it stops asking once it has answered.
        /// </summary>
        public System.Action StoneDealt;

        bool _stoneTaught;

        /// <summary>
        /// An obsidian standing in its own socket, for the lesson to ring - or null.
        /// Asked when the panel goes up, for <see cref="LiveCharm"/>'s reason.
        /// </summary>
        public RectTransform LiveObsidian()
        {
            if (_board == null) return null;

            for (int i = 0; i < _gems.Count && i < Width * Height; i++)
            {
                if (_board.At(i) != SiegeLayout.Obsidian) continue;

                var gem = _gems[i];
                if (gem == null || gem.Img == null || gem.Colour != ObsidianColour) continue;

                var rt = gem.Img.rectTransform;
                if ((rt.anchoredPosition - CentreOf(i)).sqrMagnitude > Cell * Cell * .01f) continue;

                return rt;
            }

            return null;
        }

        // ------------------------------------------------------------------ the stone, at rest
        /// <summary>
        /// Every obsidian on the field smoulders: a violet glow breathing on the stone, and now
        /// and then a wisp of the curse leaving it.
        ///
        /// <para>
        /// <b>What makes a stone that matches nothing read as a thing to gather</b>. A black gem
        /// among four saturated jewels is a hole in the board; one that is quietly giving off
        /// light is a thing with something inside it, which is the whole sentence the lesson
        /// then says in words.
        /// </para>
        /// <para>
        /// <b>Kept in step with the cell rather than with an event</b>, so a gem the field turned
        /// into or out of an obsidian (a refill, a shuffle, a recycled widget) wears the aura
        /// exactly while it is one - the charm halo's rule (`Charmed`), asked every frame
        /// because a <c>Dress</c> is not the only door a face changes through.
        /// </para>
        /// </summary>
        void Brood(float dt)
        {
            // Held in a local rather than read through `_board.Layout.` at the call site, for
            // the screen's reason: the offline compile refuses that shape, because a level that
            // is not a glade has no `LevelDefinition.Layout` and the check is deliberately coarse.
            var plan = _board != null ? _board.Layout : null;
            if (plan == null || !plan.Cursed) return;

            float now = Time.unscaledTime;

            for (int i = 0; i < _gems.Count; i++)
            {
                var gem = _gems[i];
                if (gem == null || gem.Img == null) continue;

                bool stone = gem.Colour == ObsidianColour;

                if (!stone)
                {
                    if (gem.Aura != null && gem.Aura.enabled) gem.Aura.enabled = false;
                    continue;
                }

                if (gem.Aura == null)
                {
                    gem.Aura = Lit("Curse aura", (RectTransform)gem.Img.transform,
                                   Art.Glow(96, 1.8f), Pal.A(Hex, 0f),
                                   new Vector2(Cell * 1.25f, Cell * 1.25f));
                    gem.Aura.rectTransform.SetAsFirstSibling();
                    gem.Wisp = now + Random.Range(.2f, 1.4f);
                }

                gem.Aura.enabled = true;

                // Slow and a little uneven, keyed on the cell so a field of them does not breathe
                // in lockstep - one pulse across four stones reads as a light switch.
                float k = .5f + .5f * Mathf.Sin(now * 1.9f + i * 1.7f);
                gem.Aura.color = Pal.A(Hex, .20f + .22f * k);
                gem.Aura.rectTransform.localScale = Vector3.one * (.92f + .12f * k);

                if (i < Width * Height && now >= gem.Wisp && !Busy)
                {
                    gem.Wisp = now + Random.Range(1.1f, 2.4f);
                    Wisp(gem.Img.rectTransform.anchoredPosition, Cell * .5f);
                }
            }

            // The lesson, the first time one is standing still in its socket.
            if (!_stoneTaught && StoneDealt != null && LiveObsidian() != null)
            {
                _stoneTaught = true;
                StoneDealt();
            }
        }

        /// <summary>One wisp of the curse rising off <paramref name="at"/> and going out.</summary>
        void Wisp(Vector2 at, float rise)
        {
            if (_fx == null) return;

            var mote = Lit("Wisp", _fx, StrikeFx.Star, Pal.A(HexCore, 0f),
                           Vector2.one * Cell * Random.Range(.16f, .26f));
            if (mote == null) return;

            var rt = mote.rectTransform;
            var from = at + new Vector2(Random.Range(-.22f, .22f) * Cell, Random.Range(-.1f, .15f) * Cell);
            float sway = Random.Range(-.25f, .25f) * Cell;
            float life = Random.Range(.9f, 1.3f);

            Tween.Run(life, Ease.Linear, t =>
            {
                if (!mote) return;
                rt.anchoredPosition = from + new Vector2(Mathf.Sin(t * 5f) * sway * t, rise * Ease.OutQuad(t));
                rt.localScale = Vector3.one * Mathf.Lerp(1f, .45f, t);
                mote.color = Pal.A(Color.Lerp(HexCore, Hex, t), Mathf.Sin(t * Mathf.PI) * .85f);
            }, mote).OnDone(() => { if (mote) Destroy(mote.gameObject); });
        }

        // ------------------------------------------------------------------ the break
        /// <summary>How long the break gathers before it bursts, in real seconds.</summary>
        const float CurseGather = .50f;

        /// <summary>How long the burst is watched before the board refills.</summary>
        const float CurseTail = .45f;

        /// <summary>How slowly the run's clock runs while the curse breaks. A lance's crawl, a shade slower.</summary>
        const float CursePace = .20f;

        /// <summary>
        /// The curse breaking on the field: every obsidian the model took is pulled into the run
        /// that broke it along a chain of violet light, the stones implode into a gathering dark,
        /// and it bursts.
        ///
        /// <para>
        /// <b>Answers the stones' widgets as taken</b>, so the ordinary clear in <c>Beat</c>
        /// skips them - a stone is not shattered where it stands like a jewel, it is dragged
        /// across the field, and two drawings of one gem going would be the two halves of one
        /// event disagreeing (invariant 30i).
        /// </para>
        /// <para>
        /// <b>Drawn from what the beat recorded</b> (<see cref="SiegeBeat.Broken"/>,
        /// <see cref="SiegeBeat.Breakers"/>): the cells are holes by the time this draws and
        /// refills by the time it ends, so nothing here reads the board.
        /// </para>
        /// </summary>
        HashSet<int> Unbinding(SiegeBeat beat)
        {
            var taken = new HashSet<int>();
            if (beat == null || !beat.Unbound || _fx == null) return taken;

            // The centre of the break is the run that broke it.
            var centre = Vector2.zero;
            int runs = 0;
            for (int i = 0; i < beat.Breakers.Count; i++) { centre += CentreOf(beat.Breakers[i]); runs++; }
            if (runs == 0) for (int i = 0; i < beat.Broken.Count; i++) { centre += CentreOf(beat.Broken[i]); runs++; }
            centre /= Mathf.Max(1, runs);

            // **The clock first**, so the slow motion is already running when the first chain
            // lights (the lance's rule: the first frame is the one the eye judges the speed by).
            Dilate(CursePace, CurseGather + CurseTail);
            Holding(CurseGather + CurseTail);

            var breakers = new HashSet<int>(beat.Breakers);

            for (int i = 0; i < beat.Broken.Count; i++)
            {
                int cell = beat.Broken[i];
                var gem = cell >= 0 && cell < _gems.Count ? _gems[cell] : null;
                taken.Add(cell);

                if (gem == null || gem.Img == null) continue;

                var img = gem.Img;
                var from = img.rectTransform.anchoredPosition;
                bool pulled = !breakers.Contains(cell);

                // A stone drawn across the field is chained to the break first, so the player
                // sees *why* a gem six cells away came apart.
                if (pulled) Tether(from, centre);

                Drawn(img, from, centre, pulled);
            }

            Gathering(centre, beat.Broken.Count);
            Tween.After(CurseGather, () => Erupt(centre, beat.Broken.Count), this);

            Audio.Sfx("charge", .72f, .82f);
            Audio.SfxVaried("whoosh", .46f, .05f);

            return taken;
        }

        /// <summary>
        /// A chain of the curse's light between a pulled stone and the break, crackling while the
        /// stone is dragged along it.
        /// </summary>
        void Tether(Vector2 from, Vector2 to)
        {
            var arc = Lightning.Grow("Curse chain", _fx, Hex, Cell * .06f, haloAlpha: .42f);
            int seed = Random.Range(0, 1 << 20);
            int beats = Mathf.CeilToInt(CurseGather / .05f);

            for (int b = 0; b < beats; b++)
            {
                int n = b;
                Tween.After(n * .05f, () =>
                {
                    if (arc.Host == null) return;

                    // The chain shortens with the stone: its far end is wherever the stone has
                    // got to, which is the same curve `Drawn` moves it on.
                    float t = Mathf.Clamp01(n * .05f / CurseGather);
                    var end = Vector2.Lerp(from, to, Ease.InCubic(t));
                    arc.Strike(end, to, Cell, .34f, 1, seed + n);
                    arc.Group.alpha = t < .1f ? t / .1f : 1f;
                }, arc.Host);
            }

            Tween.After(CurseGather + .02f, arc.Destroy, arc.Host);
        }

        /// <summary>
        /// One stone going into the break: a shudder where it stands, then drawn along a curve
        /// into the centre, spinning and shrinking, and gone as the burst lands.
        /// </summary>
        void Drawn(Image img, Vector2 from, Vector2 to, bool pulled)
        {
            var rt = img.rectTransform;
            float spin = Random.Range(220f, 420f) * (Random.value < .5f ? -1f : 1f);

            // A little bow in the path, so a stone is *dragged* rather than slid on a rail.
            var bow = (Vector2)(Quaternion.Euler(0f, 0f, 90f) * (to - from)).normalized
                    * Cell * Random.Range(-.6f, .6f);

            // Its own light swelling as it goes: the stone is giving up what it held.
            var flare = Lit("Stone flare", rt, Art.Glow(96, 1.6f), Pal.A(HexCore, 0f),
                            new Vector2(Cell * 1.4f, Cell * 1.4f));

            float shudder = pulled ? .10f : .16f;

            Tween.Run(CurseGather, Ease.Linear, t =>
            {
                if (!rt) return;

                float s = t * CurseGather;
                if (s < shudder)
                {
                    // Shaking where it stands before it lets go.
                    float k = s / shudder;
                    rt.anchoredPosition = from + new Vector2(Mathf.Sin(s * 90f), Mathf.Cos(s * 77f)) * Cell * .05f * k;
                    rt.localScale = Vector3.one * (1f + .12f * k);
                }
                else
                {
                    float k = Ease.InCubic((s - shudder) / (CurseGather - shudder));
                    rt.anchoredPosition = Vector2.Lerp(from, to, k) + bow * Mathf.Sin(k * Mathf.PI);
                    rt.localScale = Vector3.one * Mathf.Lerp(1.12f, .28f, k);
                    rt.localRotation = Quaternion.Euler(0f, 0f, spin * k);
                }

                if (flare) flare.color = Pal.A(HexCore, .85f * t);
            }, img).OnDone(() => { if (img) Destroy(img.gameObject); });
        }

        /// <summary>
        /// The break gathering: a dark opening where the stones are going, a ring closing on it,
        /// the sigil winding in over it and motes drawn in from all round.
        /// </summary>
        void Gathering(Vector2 at, int stones)
        {
            var host = UIKit.Node("Curse gather", _fx);
            float size = Cell * (1.9f + Mathf.Min(stones, 8) * .12f);

            // **The void, drawn normally rather than added** - a curse gathers *dark*, and dark
            // is the one thing the additive material cannot draw.
            var voidImg = UIKit.Img("Void", host, Art.Glow(128, 1.4f), Pal.A(HexDeep, 0f),
                                    Vector2.one * size);
            voidImg.raycastTarget = false;
            voidImg.rectTransform.anchoredPosition = at;
            Tween.Run(CurseGather, Ease.InQuad, t =>
            {
                if (!voidImg) return;
                voidImg.color = Pal.A(HexDeep, .85f * t);
                voidImg.rectTransform.localScale = Vector3.one * Mathf.Lerp(.2f, 1f, t);
            }, voidImg);

            // A ring closing on it - the one shape here that says *about to happen*.
            var ring = Lit("Close", host, StrikeFx.Ring, Pal.A(HexCore, 0f), Vector2.one * Cell * 3.6f);
            if (ring != null)
            {
                ring.rectTransform.anchoredPosition = at;
                Tween.Run(CurseGather, Ease.InQuad, t =>
                {
                    if (!ring) return;
                    ring.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, .1f, t);
                    ring.color = Pal.A(HexCore, Mathf.Min(1f, t * 3f));
                }, ring);
            }

            // The sigil winding in, fast, from wide to tight.
            var sigil = Sigil("Curse wind", host, at, Cell * 4.2f, 0f);
            if (sigil != null)
            {
                var rt = sigil.rectTransform;
                Tween.Run(CurseGather, Ease.Linear, t =>
                {
                    if (!sigil) return;
                    float k = Ease.InQuad(t);
                    rt.localScale = Vector3.one * Mathf.Lerp(1f, .32f, k);
                    rt.localRotation = Quaternion.Euler(0f, 0f, -540f * k);
                    sigil.color = Pal.A(Hex, Mathf.Min(.9f, t * 2.4f));
                }, sigil);
            }

            // Motes drawn in from all round, shrinking as they arrive.
            var star = Kit(StrikeFx.Star) ?? Art.Spark(64);
            for (int i = 0; i < 16; i++)
            {
                float ang = (i / 16f) * Mathf.PI * 2f + Random.Range(-.2f, .2f);
                var from = at + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Cell * Random.Range(1.6f, 2.6f);
                float delay = Random.Range(0f, CurseGather * .45f);
                float over = CurseGather - delay;

                var mote = Lit("m", host, star, Pal.A(HexCore, 0f), Vector2.one * Cell * Random.Range(.24f, .42f));
                var mrt = mote.rectTransform;
                mrt.anchoredPosition = from;
                Tween.Run(over, Ease.InCubic, t =>
                {
                    if (!mote) return;
                    mrt.anchoredPosition = Vector2.Lerp(from, at, t);
                    mrt.localScale = Vector3.one * Mathf.Lerp(1f, .2f, t);
                    mote.color = Pal.A(Color.Lerp(Hex, HexCore, t), Mathf.Min(1f, t * 3f));
                }, mote).Delay(delay);
            }

            // Arcs off the gathering, three on their own beats - the overcharge's crackle,
            // in the curse's light.
            for (int i = 0; i < 3; i++)
                Tween.After(i * CurseGather * .28f, () => Crackle(at, Hex, Cell * .8f, .12f), host);

            Tween.After(CurseGather + .05f, () => { if (host) Destroy(host.gameObject); }, host);
        }

        /// <summary>
        /// The curse bursting: a violet flash, the sigil thrown open across the field, rings on
        /// the ground, shards of stone and a wash of the curse's light over the whole board.
        /// </summary>
        void Erupt(Vector2 at, int stones)
        {
            if (_fx == null) return;

            float big = 3.2f + Mathf.Min(stones, 8) * .18f;

            Flash(at, Hex, big);
            Ripple(at, Hex, Cell * big * 1.6f, .55f, 0f);
            Ripple(at, HexCore, Cell * big * 1.05f, .42f, .08f);

            // The sigil thrown open, turning as it goes out.
            var sigil = Sigil("Curse burst", _fx, at, Cell * big * 1.3f, 0f);
            if (sigil != null)
            {
                var rt = sigil.rectTransform;
                Tween.Run(.7f, Ease.OutQuint, t =>
                {
                    if (!sigil) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.35f, 1.15f, t);
                    rt.localRotation = Quaternion.Euler(0f, 0f, 90f * t);
                    sigil.color = Pal.A(Color.Lerp(HexCore, Hex, t), 1f - t * t);
                }, sigil).OnDone(() => { if (sigil) Destroy(sigil.gameObject); });
            }

            // The crack the curse leaves in the board, violet, lingering a beat.
            var crack = Lit("Curse crack", _fx, StrikeFx.Crack, Pal.A(Hex, 0f), Vector2.one * Cell * big * 1.1f);
            if (crack != null)
            {
                var rt = crack.rectTransform;
                rt.anchoredPosition = at;
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                Tween.Run(.9f, Ease.Linear, t =>
                {
                    if (!crack) return;
                    rt.localScale = Vector3.one * Mathf.Lerp(.6f, 1f, Ease.OutQuint(Mathf.Clamp01(t / .15f)));
                    float a = t < .2f ? t / .2f : 1f - (t - .2f) / .8f;
                    crack.color = Pal.A(Hex, .8f * a);
                }, crack).OnDone(() => { if (crack) Destroy(crack.gameObject); });
            }

            // Shards of the stone itself - dark, drawn normally - and the curse's own sparks.
            Burst.Sparks(_fx, at, HexDeep, 16, Cell * big * 1.4f, Cell * .26f, .62f);
            Embers(at, Hex, 20, Cell * 1.8f, Cell * .30f);
            Twinkles(at, HexCore, 7, Cell * 1.4f);
            Lightup(Hex, .22f, .42f);
            Pop(at, HexCore, big * .55f, .24f);

            ShakeBoard(Cell * .15f);

            Audio.Sfx("shatter", .62f, .76f);
            Audio.Sfx("boom", .74f, .56f);
            Tween.After(.06f, () => Audio.Sfx("chime", .5f, .62f), this);
        }

        /// <summary>
        /// The sigil, as an additive image - or null when its art has not arrived (invariant 7b:
        /// a curse short of its sigil is a curse, a white square is a bug).
        /// </summary>
        Image Sigil(string name, RectTransform parent, Vector2 at, float size, float alpha)
        {
            var sprite = Piece("hex_sigil");
            if (sprite == null) return null;

            var img = Lit(name, parent, sprite, Pal.A(Hex, alpha), Vector2.one * size);
            img.rectTransform.anchoredPosition = at;
            return img;
        }

        // ------------------------------------------------------------------ the hill cursed
        /// <summary>How long the curse's front takes to climb the hill.</summary>
        const float HexSweep = 1.05f;

        /// <summary>How deep the front is drawn, in cells.</summary>
        const float HexFront = 1.6f;

        /// <summary>
        /// The curse falling on the hill: its front climbs from the line to the crest and every
        /// body it passes is stamped with the sigil as it goes.
        ///
        /// <para>
        /// <b>Drawn off the report, on the frame the model laid the hex</b> - the stone that paid
        /// for it broke <c>FuelLands</c> earlier and was drawn then. A front drawn on the swap
        /// would mark a hill that had not been marked (the stormglass's rule).
        /// </para>
        /// <para>
        /// <b>A curse that fell on an empty hill is drawn too</b>: the front leaves the line and
        /// dies a cell up the slope, and the banner says so. A payoff that silently did nothing
        /// would be a broken stone rather than a wrong choice - and the choice here is
        /// <em>when</em>, so the player has to be told this one was spent on nothing.
        /// </para>
        /// </summary>
        void Cursed(float seconds, IReadOnlyList<int> hexed)
        {
            if (_fx == null) return;

            bool landed = hexed != null && hexed.Count > 0;

            float from = _lineY + Cell * .5f;
            float to = landed ? _hillTop : Mathf.Lerp(from, _hillTop, .16f);
            float over = landed ? HexSweep : HexSweep * .38f;

            Wall("Hex front", Reel("hexwave"), from, to, over, HexFront, 1f, 0f,
                 landed ? .30f : 0f);

            if (landed)
            {
                // The wake behind the front: deeper, slower and fainter, so a curse reads as a
                // volume of the hill being taken rather than as a line crossing it.
                Wall("Hex wake", Reel("hexwave"), from, to, over * 1.25f, HexFront * 1.9f, .45f,
                     over * .16f);

                Lightup(Hex, .16f, .6f);
                Dilate(.34f, .7f);
                ShakeBoard(Cell * .10f);
            }

            Banner(Loc.Get(landed ? "ui.siege.hexed" : "ui.siege.hex_wasted"),
                   landed ? HexCore : Pal.A(HexCore, .75f), landed ? .86f : .6f,
                   () => Audio.Sfx("chime", landed ? .52f : .34f, landed ? .72f : .9f));

            Audio.SfxVaried("whoosh", landed ? .56f : .34f, .05f);
            if (!landed) return;

            Tween.After(.12f, () => Audio.Sfx("arc", .5f, .7f), this);

            // **Every body it marks, in the order the front reaches it**, so the curse visibly
            // travels. The stamp is the arrival; the sigil the body then stands in is `Hexing`'s,
            // off the model, for as long as the model says.
            var marked = new HashSet<int>(hexed);

            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null || mob.Falling || !marked.Contains(mob.Id)) continue;

                var node = mob.Node;
                float when = Mathf.Clamp01(Mathf.InverseLerp(from, _hillTop, node.anchoredPosition.y)) * over;
                float size = mob.Boss ? 2.6f : 1.5f;
                float height = mob.Height;
                bool boss = mob.Boss;

                Tween.After(when, () =>
                {
                    if (_fx == null || !node) return;

                    var stood = node.anchoredPosition + new Vector2(0f, height * .25f);

                    var stamp = Sigil("Hex stamp", _fx, stood, Cell * size * 1.6f, 0f);
                    if (stamp != null)
                    {
                        var rt = stamp.rectTransform;
                        Tween.Run(.45f, Ease.OutQuint, t =>
                        {
                            if (!stamp) return;
                            rt.localScale = new Vector3(Mathf.Lerp(1.3f, .7f, t), Mathf.Lerp(1.3f, .7f, t) * .62f, 1f);
                            rt.localRotation = Quaternion.Euler(0f, 0f, -60f * t);
                            stamp.color = Pal.A(Color.Lerp(HexCore, Hex, t), (1f - t) * .95f);
                        }, stamp).OnDone(() => { if (stamp) Destroy(stamp.gameObject); });
                    }

                    Pop(stood, HexCore, size * .8f, .26f);
                    Burst.Sparks(_fx, stood, Hex, boss ? 14 : 8, Cell * size * 1.2f, Cell * .18f, .5f);
                    Tween.Punch(node, boss ? .08f : .14f, .28f);
                });
            }
        }

        // ------------------------------------------------------------------ a body, hexed
        /// <summary>How long before a hex runs out its sigil starts to gutter, in model seconds.</summary>
        const float HexGutter = 1.4f;

        /// <summary>
        /// Every body carrying a hex stands in the curse's sigil for as long as the model says it
        /// carries one - turning slowly, breathing, guttering as it runs out - and gives off the
        /// curse's wisps.
        ///
        /// <para>
        /// <b>A state rather than an event, read off the model every frame</b> (invariant 37ek):
        /// the frame the hex runs out is the frame the sigil goes, and one extended by a second
        /// break brightens again with nothing told. A tween started on the stamp would still be
        /// turning under a body whose hex had lapsed.
        /// </para>
        /// <para>
        /// <b>On the ground under the body, squashed to the hill</b> (<c>GroundSquash</c>), which
        /// is where the strike kit's rings lie: a sigil standing upright over a body would hide
        /// the body, and a curse that hid what it cursed would be a curse nobody could aim at.
        /// </para>
        /// </summary>
        void Hexing(float dt)
        {
            if (_board == null) return;

            for (int i = 0; i < _mob.Count; i++)
            {
                var mob = _mob[i];
                if (mob == null || mob.Node == null) continue;

                var raider = mob.Falling ? null : _board.Find(mob.Id);
                if (raider == null || !raider.Cursed)
                {
                    Lifted(mob);
                    continue;
                }

                if (mob.Mark == null)
                {
                    float wide = (mob.Body != null ? mob.Body.rectTransform.sizeDelta.x : mob.Height)
                               * (mob.Boss ? 1.05f : 1.35f);

                    mob.Mark = Sigil("Hex mark", mob.Node, new Vector2(0f, FootOf(mob.Height, mob.Boss)),
                                     wide, 0f);
                    if (mob.Mark == null) continue;

                    // Under everything the raider is made of: the body stands *in* its curse.
                    mob.Mark.transform.SetSiblingIndex(
                        mob.Shadow != null ? mob.Shadow.transform.GetSiblingIndex() + 1 : 0);
                    mob.Marked = 0f;
                    mob.MarkWisp = 0f;
                }

                mob.Marked += dt;

                float arrive = Mathf.Clamp01(mob.Marked / .25f);
                float left = raider.Hexed;
                float gutter = left < HexGutter
                             ? (left / HexGutter) * (.65f + .35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 18f + mob.Id)))
                             : 1f;
                float breath = .80f + .20f * Mathf.Sin(Time.unscaledTime * 3.3f + mob.Id * .9f);

                var rt = mob.Mark.rectTransform;
                rt.localRotation = Quaternion.Euler(0f, 0f, -Time.unscaledTime * 40f - mob.Id * 23f);
                rt.localScale = new Vector3(arrive * (.95f + .05f * breath), arrive * (.95f + .05f * breath) * GroundSquash, 1f);
                mob.Mark.color = Pal.A(Hex, .78f * arrive * gutter * breath);

                // A wisp of the curse off the body now and then - fewer as it runs out.
                mob.MarkWisp -= dt;
                if (mob.MarkWisp > 0f || gutter < .4f) continue;

                mob.MarkWisp = Random.Range(.38f, .7f);
                Wisp(mob.Node.anchoredPosition + new Vector2(0f, mob.Height * (BodyLift + .2f)), mob.Height * .7f);
            }
        }

        /// <summary>Takes a lapsed hex's sigil off a body.</summary>
        void Lifted(Mob mob)
        {
            if (mob.Mark == null) return;

            Destroy(mob.Mark.gameObject);
            mob.Mark = null;
        }

        /// <summary>
        /// The light a hexed body is drawn in: its own colours washed toward the curse's, pulsing,
        /// so a cursed raider reads as cursed from across the board even under a crowd.
        /// </summary>
        Color HexedBody(SiegeRaider raider)
        {
            float k = .5f + .5f * Mathf.Sin(Time.unscaledTime * 4.2f + raider.Id * 1.3f);
            float fade = Mathf.Clamp01(raider.Hexed / .6f);
            return Color.Lerp(Color.white, HexCore, (.26f + .20f * k) * fade);
        }
    }
}
