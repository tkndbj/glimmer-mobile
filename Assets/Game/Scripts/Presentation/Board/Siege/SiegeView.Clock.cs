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
    /// The frame: stepping the rules, following what moved, and repainting the readouts.
    ///
    /// <para>
    /// Nothing here decides anything. It hands <c>SiegeBoard.Advance</c> the seconds that have
    /// gone by and draws what it is told happened.
    /// </para>
    /// </summary>
    public sealed partial class SiegeView
    {
        // ------------------------------------------------------------------ the clock
        /// <summary>
        /// Whether this run is under way at all, ignoring whether an animation is playing.
        ///
        /// <para>
        /// <b>It exists because <c>ProtoView.TakingInput</c> is the wrong question for this
        /// mode, and asking the wrong one stopped the clock.</b> Every other board on this shape
        /// is turn-based, so "is the board taking input" and "is the run advancing" are the same
        /// question and <c>ProtoScreen.Runnable</c> answers both with one. Here they are not:
        /// <c>Busy</c> is the latch that stops a second swap landing while the first is still
        /// falling, and a cascade is half a second long - so a run whose clock was gated on
        /// <c>TakingInput</c> would freeze the hill on every match, and a player could hold time
        /// still by swapping. <c>SiegeScreen.Runnable</c> reads this instead, which also means
        /// <c>Played</c> keeps counting through a cascade, which it should: a siege is running the
        /// whole time whether or not a finger would do anything.
        /// </para>
        /// </summary>
        public bool Advancing => Run != null && !Locked && !Over;

        /// <summary>As <see cref="Advancing"/>, and the screen has let the run begin.</summary>
        bool Live => Advancing && !Held;

        void Update()
        {
            if (!Live) return;

            var report = _board.Advance(Time.unscaledDeltaTime);

            Follow();
            Depth();
            Charge();

            _roared = false;

            if (report.Wave >= 0) Arrival(report.Wave);
            for (int i = 0; i < report.Bolts.Count; i++) Bolt(report.Bolts[i]);
            for (int i = 0; i < report.Casts.Count; i++) Cast(report.Casts[i]);

            // What the hill has done to the field. Drawn after the casts, so the mark a mote was
            // crossing toward lands after the mote arrives rather than under it.
            for (int i = 0; i < report.Meddles.Count; i++) Meddled(report.Meddles[i]);
            Freed(report.Meddles);
            for (int i = 0; i < report.Spells.Count; i++) Smite(report.Spells[i]);
            for (int i = 0; i < report.Blows.Count; i++) Blow(report.Blows[i]);

            Reap();

            if (report.Any) Changed?.Invoke();

            Judge();
        }

        /// <summary>Puts every raider widget where the model says it is.</summary>
        /// <summary>
        /// Repaints every turret's picture, for the moment the player's own four arrive.
        ///
        /// <b>The board is built on the starter's art and repaints when the scope lands</b>, which
        /// is invariant 7b's second clause: loading is asynchronous, so a screen has to repaint
        /// when a scope arrives rather than waiting for one — an <c>Image</c> with a null sprite
        /// is a white rectangle, not a blank.
        /// </summary>
        public void Redress()
        {
            if (_board == null || _posts == null) return;

            var wards = _board.Wards;

            for (int w = 0; w < _posts.Length && w < wards.Count; w++)
            {
                var post = _posts[w];
                if (post == null || post.Body == null) continue;

                post.Body.sprite = WardArt(wards[w]);
                post.Fire = FireArt(wards[w]);
            }
        }

        void Follow()
        {
            var raiders = _board.Raiders;

            for (int i = 0; i < raiders.Count; i++)
            {
                var raider = raiders[i];
                if (!raider.OnTheHill) continue;

                var mob = Widget(raider);
                if (mob == null) continue;

                mob.Node.anchoredPosition =
                    new Vector2(LaneX(raider.Lane), MarchY(raider.March));

                if (mob.Fill != null)
                {
                    float share = Mathf.Clamp01(raider.Health / (float)raider.MaxHealth);
                    mob.Fill.rectTransform.sizeDelta =
                        new Vector2(mob.Bar.sizeDelta.x * share - 4f, mob.Bar.sizeDelta.y - 4f);
                }

                // **White, not a coat** — the body carries its own colour now, so a hit is
                // drawn by washing it out toward cream and letting it come back.
                if (mob.Body != null)
                    mob.Body.color = raider.Flash > 0f
                                   ? Color.Lerp(Color.white, Pal.Cream, raider.Flash * 5f)
                                   : Color.white;

                // **A warlord walks on and then stands, and which of the two it wears is read off
                // the board rather than latched at spawn.** It was drawn in its idle for the whole
                // walk-in, which came back from play in one word — *floating* — and is exactly the
                // fault this mode's cast reels are walks to avoid (`make_siege_art.CAST_SET`). The
                // question is asked every frame and `Wear` answers it once.
                if (mob.Boss && !Throwing(mob))
                    Wear(mob, raider.InPlace || mob.Walking == null ? mob.Idle : mob.Walking);
            }
        }

        /// <summary>
        /// Puts the raiders in front of and behind each other by how far down the hill they are.
        ///
        /// <para>
        /// <b>Sorted, then indexed one at a time.</b> The first cut handed each widget
        /// <c>SetSiblingIndex(march * 1000)</c>, which reads as depth and is not: Unity clamps a
        /// sibling index to the number of children, so with eight raiders on the hill every one of
        /// those numbers clamped to the last slot and the order became whichever widget was
        /// written most recently. Reported from play as a raider walking down *behind* another and
        /// drawing on top of it. There is no way to say "put this one at depth 0.42" — the only
        /// thing a UI hierarchy understands is a run of positions, so the list has to be ordered
        /// and then laid out.
        /// </para>
        /// </summary>
        void Depth()
        {
            _order.Clear();

            for (int i = 0; i < _mob.Count; i++)
                if (_mob[i].Node != null) _order.Add(_mob[i]);

            // Furthest up the hill first, so it ends up at the back. An insertion sort, because
            // the list is nearly ordered every frame and never longer than a wave.
            for (int i = 1; i < _order.Count; i++)
            {
                var held = _order[i];
                float depth = DepthOf(held);

                int j = i - 1;
                while (j >= 0 && DepthOf(_order[j]) > depth)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = held;
            }

            for (int i = 0; i < _order.Count; i++) _order[i].Node.SetSiblingIndex(i);
        }

        float DepthOf(Mob mob)
        {
            var raider = _board.Find(mob.Id);
            return raider == null ? 2f : raider.March;
        }

        /// <summary>Paints every ward's fuel, its light and its health.</summary>
        void Charge()
        {
            for (int i = 0; i < _posts.Length; i++)
            {
                var post = _posts[i];
                var ward = _board.Wards[i];

                // **The rank is repainted from the model, and the *edge* is what raises the
                // fanfare.** A ward may go up while a cascade is still resolving and while this
                // view is mid-animation, so what a ward is drawn as has to be a fact read off the
                // board rather than a thing switched by whoever happened to see the event — and
                // `Post.Rank` is what turns that read into an edge exactly once.
                if (post.Rank != ward.Rank)
                {
                    post.Rank = ward.Rank;
                    post.Fire = FireArt(ward);

                    if (!post.Down && post.Body != null)
                        post.Body.sprite = WardArt(ward);
                    if (post.Shield != null) post.Shield.color = RankTint(ward.Rank);

                    if (post.Tier != null) post.Tier.text = ward.Level.ToString();

                    Rose(post);
                }

                float wide = post.Tube.sizeDelta.x - 4f;
                post.Juice.rectTransform.sizeDelta =
                    new Vector2(Mathf.Max(0f, wide * ward.Charge), post.Tube.sizeDelta.y - 4f);

                // The light is what says a ward is working, and it is the thing a player watches
                // out of the corner of an eye while looking at the field.
                float want = ward.Alive ? Mathf.Clamp01(ward.Charge) : 0f;
                post.Lit = Mathf.Lerp(post.Lit, want, Time.unscaledDeltaTime * 6f);

                float beat = ward.Fuelled
                           ? 1f + Mathf.Sin(Time.unscaledTime * 14f) * .06f : 1f;

                post.Glow.color = Pal.A(TintOf(ward.Colour), post.Lit * .62f);
                post.Glow.rectTransform.localScale = Vector3.one * (.7f + post.Lit * .55f) * beat;

                // The recoil: the turret's own shoot frames, walked while a shot is in flight.
                if (post.Recoil > 0f)
                {
                    post.Recoil = Mathf.Max(0f, post.Recoil - Time.unscaledDeltaTime);

                    if (post.Body != null && post.Fire != null && post.Fire.Length > 0)
                    {
                        int frame = Mathf.Clamp(
                            (int)((1f - post.Recoil / RecoilFor) * post.Fire.Length),
                            0, post.Fire.Length - 1);
                        post.Body.sprite = post.Fire[frame];
                    }
                }
                else if (post.Body != null && !post.Down)
                {
                    post.Body.sprite = WardArt(ward);
                    if (post.Shield != null) post.Shield.color = RankTint(ward.Rank);
                }

                // **A ward is never drawn darker than its own colour.** It used to fade toward
                // 72% of its coat when it had no fuel, which meant it dimmed on the first frame of
                // the run and stayed dim - reported from play as "they are bright when the match
                // starts and immediately dim down". Fuel now reads as a ward getting *brighter*,
                // which is the direction a light should move in.
                //
                // **A doused ward is the one exception, and it is an exception to the rule rather
                // than a hole in it** (invariant 37m): the reason that rule exists is that dim
                // must never be the resting state, and a blightcaller's dark is a thing that has
                // *happened* and is running out. It is drawn from the model every frame rather
                // than latched when the hex lands, so a surge that lifts it (`SiegeBoard.Surge`)
                // is a ward relighting on the frame the item is spent.
                if (post.Body != null && !post.Down)
                    post.Body.color = ward.Doused
                                    ? Color.Lerp(DarkCoat, post.Coat,
                                                 Mathf.PingPong(Time.unscaledTime * 2.2f, 1f) * .22f)
                                    : Color.Lerp(post.Coat, Color.white, post.Lit * .30f);

                // The pall over it: a cold veil that thins as the seconds run out, so how long is
                // left is on the board rather than in the player's head. It is the ward's own glow
                // widget re-tinted, which is what keeps it behind the turret rather than over it.
                if (ward.Doused)
                {
                    float left = Mathf.Clamp01(ward.Dark / SiegeTuning.Douse);
                    post.Glow.color = Pal.A(Casting(SiegeKind.Blightcaller), .18f + left * .44f);
                    post.Glow.rectTransform.localScale = Vector3.one * (1.05f + left * .3f);
                }

                // Cream, then gold, then ember: the line says how close it is to going in the
                // one place a player is already looking.
                float held = Mathf.Clamp01(ward.Health / (float)SiegeTuning.WardHealth);

                post.Fill.rectTransform.sizeDelta =
                    new Vector2((post.Bar.sizeDelta.x - 4f) * held, post.Bar.sizeDelta.y - 4f);

                post.Fill.color = held <= .34f ? Pal.Ember : held <= .67f ? Pal.Gold : Pal.Cream;

                if (ward.Alive || post.Down) continue;

                post.Down = true;
                Fell(post);
            }
        }

        /// <summary>
        /// A ward going up a rank: the one moment in this mode the player <em>made</em>.
        ///
        /// <para>
        /// <b>Drawn as an arrival rather than as a change.</b> The turret's picture has already
        /// swapped by the time this runs, so what is left to say is that it was earned — a ring
        /// out of the plinth, the badge punched, light gathered on the body, and the one sound in
        /// the set that means <em>you have got something</em>. Invariant 20m's rule: the event a
        /// player caused is the one that gets the biggest drawing in the mode.
        /// </para>
        /// <para>
        /// Nothing here reads the board, so it is safe to raise from <see cref="Charge"/> on the
        /// frame a rank changes, whatever else is mid-animation.
        /// </para>
        /// </summary>
        void Rose(Post post)
        {
            if (post == null || post.Node == null) return;

            // Nought is where every ward starts, so a "rank up" to it is the board being built.
            if (post.Rank <= 0) return;

            var tint = TintOf(post.Colour);
            var at = new Vector2(PostX(System.Array.IndexOf(_posts, post)), _lineY);

            Shockwave(at, Pal.Lift(tint, .45f), 3.2f, .42f);
            Pop(at, Pal.Lift(tint, .3f), 2.4f, .3f);
            Burst.Sparks(_fx, at + new Vector2(0f, Cell * .4f), tint, 16, Cell * 3.4f,
                         Cell * .26f, .55f);

            Tween.Punch(post.Node, .18f, .34f);

            if (post.Crest != null)
            {
                Tween.KillAll(post.Crest);
                post.Crest.localScale = Vector3.one * 1.9f;
                Tween.Scale(post.Crest, 1f, .4f, Ease.OutBack);
            }

            if (post.Body != null)
            {
                var body = post.Body;
                Tween.Run(.5f, Ease.OutQuad, t =>
                {
                    if (!body) return;
                    body.color = Color.Lerp(Color.white, post.Coat, t);
                }, body, "rose");
            }

            Audio.Sfx("reward", .55f, 1f);
            ShakeBoard(7f);
        }
    }
}
