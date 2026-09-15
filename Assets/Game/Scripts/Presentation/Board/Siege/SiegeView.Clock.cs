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
        /// <summary>
        /// What a raider held by a stun is drawn in.
        ///
        /// <b>Darker and flatter, never brighter</b>, because <c>Image.color</c> is a multiply and
        /// cannot do anything else (invariant 37l) — so what says "this one has been stopped" is
        /// the colour draining out of it rather than a light coming on.
        /// </summary>
        static readonly Color Stunned = new Color(.56f, .60f, .72f);

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
            // **Before the gate, deliberately.** A board nobody may touch has to put the idle
            // clock back to nought rather than freeze it, and everything that stops a run being
            // touched — a cascade, a lesson, the pause menu, a panel over the board — reads as
            // `Playable` rather than as `Live`. See `SiegeView.Hint`.
            Idle(Time.unscaledDeltaTime);

            // **Before the gate too, and for the same reason.** A charm standing on a held board
            // is the one thing on this field that is meant to catch the eye, and a halo that
            // stopped breathing the moment a panel opened would tell a player the board had gone
            // dead. It draws nothing on a field carrying no charm, which is most frames.
            Breathe(Time.unscaledDeltaTime);

            // **Before the gate, with `Idle` and `Breathe`, and for their reason.** A dilation is
            // seconds of real time and has to expire whether or not the run is advancing - a panel
            // raised over a charm would otherwise park the slowdown at full and hand it back to a
            // board that had finished with it, which is a run that crawls for no reason anybody
            // can see. Nothing is lost by counting it down behind a panel, because the clock it
            // scales is not being read while the run is held.
            Pacing(Time.unscaledDeltaTime);

            if (!Live) return;

            // **The run's own clock, which is the only place in this mode it may be bent.**
            // Everything below is driven by what `Advance` is handed, so slowing this is slowing
            // the hill, the wards, the muster and the fuel together - see `Dilate`.
            var report = _board.Advance(Time.unscaledDeltaTime * _pace);

            // **The count-in is part of the run, so it is paced by the run.** Read off the
            // board's own quiet rather than timed beside it, which is what stops it running out
            // behind a first-timer's tip - see `CountIn`. After `Advance`, so the first beat
            // lands on the first frame the hill really starts walking.
            CountIn();

            // How much of a death is still being watched. Counted here rather than inside
            // `Judge`, so a hold armed early in a run cannot still be standing when the run is
            // won — see `_felling`. The run's own seconds, unscaled, like everything else here.
            Watching(Time.unscaledDeltaTime);

            Follow();
            Depth();
            Charge();

            // The two readouts that make the colour question answerable at a glance, and the
            // pulse that says a tube may be spent. All three read the board and decide nothing.
            Wanted();
            Ready();
            Foretell();

            // A boss is on the hill for far longer than it is casting, so something has to be
            // happening on it in between — see `SiegeView.Storm`. It draws nothing on a hill with
            // no boss on it, which is nine frames in ten.
            Ambient();

            _roared = false;

            if (report.Wave >= 0) Arrival(report.Wave);
            for (int i = 0; i < report.Bolts.Count; i++) Bolt(report.Bolts[i]);

            // **The volley a stormglass loosed, as one event.** It is drawn here rather than with
            // the beat that sprang it because the model books it exactly as it books a match's
            // fuel (invariant 37s): the bolts land when the motes do, and the one thing this may
            // not do is kill a raider before the gem that paid for it has finished bursting.
            Volley(report.Charmed);
            for (int i = 0; i < report.Casts.Count; i++) Cast(report.Casts[i]);

            // What a bomber left behind. Drawn after the bolts, so the bomb arrives after the
            // shot that killed the thing carrying it rather than under it.
            for (int i = 0; i < report.Dropped.Count; i++) Dropped(report.Dropped[i]);

            // And what a kill paid. After the bomb for the same reason the bomb is after the
            // bolts: the prize arrives once the thing that dropped it has visibly gone.
            for (int i = 0; i < report.Cogs.Count; i++) Dropped(report.Cogs[i]);

            for (int i = 0; i < report.Spells.Count; i++) Smite(report.Spells[i]);

            // **What a gravemaw ate.** After the spells, so the ring it opens is already there to
            // be pulled into, and before `Fuses` and `Gears` below, so this owns the widgets
            // rather than racing the polls that would otherwise sink them without saying why.
            if (report.Devoured.Count > 0) Swallowed(report.Devoured);

            for (int i = 0; i < report.Blows.Count; i++) Blow(report.Blows[i]);

            Reap();
            Fuses();
            Gears();
            Noticed(report);

            if (report.Any) Changed?.Invoke();

            Judge();
        }

        /// <summary>
        /// Raises the three "first time this happened" hooks the lessons hang on.
        ///
        /// <para>
        /// <b>Read off the board and the step's own report rather than latched where each thing is
        /// drawn</b>, so a moment that happens while the view is mid-animation is still noticed —
        /// and so every one of them is asked in the same place rather than three call sites
        /// remembering to. Each fires once for the life of the screen, which is what a lesson
        /// costs: <c>RunLessons.Teach</c> refuses one already seen, but it cannot refuse one
        /// offered while a panel is up, so offering it repeatedly would be a panel arriving at an
        /// arbitrary later moment.
        /// </para>
        /// </summary>
        void Noticed(SiegeReport report)
        {
            // **Offered every time rather than once, and the latch that was here was a bug.**
            // Latching *before* knowing whether the offer landed meant one made while another tip
            // was up was thrown away for ever — which is what happened: the overcharge's tip was
            // reported as never appearing. Offering again costs nothing.
            //
            // **And the other half of that fault is not here.** Every one of these three moments
            // happens *inside* a cascade — a tube fills from a match, a cog and a bomb are left by
            // a raider a bolt felled — and a cascade is exactly when `ProtoView.Busy` says the
            // board cannot be taught on. So the offer was refused at the only instant it was ever
            // made. `RunLessons.Teach` now takes these as moments that are *owed* and gives each
            // the first instant the board can take it, which is a beat after the cascade that
            // caused it. See `RunLessons._owed`.
            if (report.Brimmed.Count > 0) Brimmed?.Invoke();
            if (report.Cogs.Count > 0) Salvaged?.Invoke();

            // **And the bomb, which used to be announced by the bomber walking on.** It is here
            // with the other two rather than inside `Dropped` for this method's own reason: a
            // moment that happens while the view is mid-animation is still noticed, and all three
            // are asked in one place rather than three call sites remembering to.
            if (report.Dropped.Count > 0) Bombed?.Invoke();
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

        // ------------------------------------------------------------------ how fast time runs
        /// <summary>
        /// How fast the run's clock is running, as a share of real time.
        ///
        /// <para>
        /// <b>One seam, because there is one clock.</b> Everything about a siege that moves on its
        /// own — the hill walking, the wards firing, the muster, a boss's cadence, fuel crossing —
        /// is a consequence of what <c>SiegeBoard.Advance</c> is handed, so scaling that one number
        /// is the whole of what "slow motion" can mean here. Anything that tried to slow the
        /// raiders alone would be a second opinion about time, and two of those disagree.
        /// </para>
        /// <para>
        /// <b>It is difficulty-neutral by construction, which is why it is allowed to exist.</b>
        /// The model advances by delivered seconds and by nothing else, so a run played half in
        /// slow motion is the same run over more wall-clock: the hill has walked exactly as far
        /// per second of ward fire as it always did. <b>That is not true of a hold that only stops
        /// the drawing</b> — an earlier cut of the charms documented a hold that was never
        /// implemented, and had it been, every chapter's difficulty would have become a function
        /// of an animation constant. What a player does gain is time to <em>look</em>, which is
        /// the point, and time to think, which a lesson and a panel already hand them.
        /// </para>
        /// <para>
        /// <b>Invisible to every gate, and that is a fact rather than a loophole.</b>
        /// <c>SiegeRuleTests</c> steps the board itself and never builds a view, so nothing here
        /// reaches the hold simulation — because nothing here changes what the board is told.
        /// </para>
        /// </summary>
        float _pace = 1f;

        /// <summary>Seconds of real time left at <see cref="_slow"/>, and what it is.</summary>
        float _paceLeft, _slow = 1f;

        /// <summary>
        /// How long the clock takes to come back up to speed.
        ///
        /// <b>Back rather than snapped, because a hill that jumps from a crawl to full speed reads
        /// as a dropped frame.</b> Short enough not to be a second effect of its own.
        /// </summary>
        const float PaceBack = .28f;

        /// <summary>
        /// Runs the clock at <paramref name="pace"/> of real time for <paramref name="seconds"/>,
        /// then eases it back.
        ///
        /// <b>The slower of two running dilations wins, and the longer end time wins</b> — a
        /// cascade can spring two charms, and a stormglass's full stop must not be cut short by a
        /// lance's crawl starting a beat later. That is <see cref="Felling"/>'s rule about two
        /// deaths, said about time.
        /// </summary>
        void Dilate(float pace, float seconds)
        {
            if (seconds <= 0f) return;

            _slow = Mathf.Min(_pace, Mathf.Clamp01(pace));
            _pace = _slow;
            _paceLeft = Mathf.Max(_paceLeft, seconds);
        }

        /// <summary>How much longer a drawing that wants to keep step with the clock should take.</summary>
        float Stretched(float seconds) => seconds / Mathf.Max(_pace, .18f);

        /// <summary>Counts a dilation down and eases the clock back up. Real seconds, always.</summary>
        void Pacing(float dt)
        {
            if (_paceLeft > 0f)
            {
                _paceLeft -= dt;
                _pace = _slow;
                return;
            }

            if (_pace >= 1f) return;

            _pace = Mathf.Min(1f, _pace + dt / PaceBack);
            if (_pace >= 1f) _slow = 1f;
        }

        void Follow()
        {
            var raiders = _board.Raiders;

            for (int i = 0; i < raiders.Count; i++)
            {
                var raider = raiders[i];

                if (!raider.OnTheHill) continue;

                // **A dead raider is not followed, and that is what makes `Reap` safe to run
                // before the model has swept.** `OnTheHill` is only "has it walked on"; a raider
                // felled outside `Advance` — by a firepot or a storm — is still in the list until
                // the next step tidies up, and `Widget` *hatches* a body for anything it cannot
                // find. Without this clause, reaping a corpse and then following it would mint it
                // back for a frame.
                if (!raider.Alive) continue;

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
                //
                // **A stunned raider is drawn cold, and that is not decoration.** It stops where
                // it stands while its walk cycle carries on playing, which on its own reads as the
                // hill having jammed rather than as something the player's turret did — the class
                // of fault invariant 20g is about, met on a purchase. The flash wins while it is
                // running, because a hit landing is the newer piece of news.
                if (mob.Body != null)
                    mob.Body.color = raider.Flash > 0f
                                   ? Color.Lerp(Color.white, Pal.Cream, raider.Flash * 5f)
                                   : raider.Stunned ? Stunned : Color.white;

                // **A boss goes back to its own body the frame after a spell finishes, and which
                // body that is depends on whether it has arrived.**
                //
                // There used to be nothing to choose between — an insect stands in the reel it
                // walks in (see `Mob.Idle`) — so this only had to take the cast reel off. A boss
                // rendered out of 3D has a real walk and a real stand (`Mob.Walking`), and the
                // model already answers which one it is doing: `InPlace` is `March >= Hold`, the
                // same predicate the rules use to decide when it may start casting, so the
                // drawing and the fight cannot come to disagree about when it stopped.
                //
                // **A stunned one stands rather than walking on the spot.** Everything else on
                // this hill keeps its cycle running through a stun and says so with the cold tint
                // above, because an insect cycling in place is what it looks like standing
                // anyway; a boss with a stride would be visibly walking while going nowhere,
                // which reads as the hill having jammed (invariant 20g) rather than as something
                // the player's turret did.
                if (mob.Boss && !Throwing(mob))
                    Wear(mob, mob.Walking != null && !raider.InPlace && !raider.Stunned
                              ? mob.Walking : mob.Idle);

                // **A raider that has arrived swings, and goes back to walking if it is ever
                // moved off the line.** Every cast but the bone one answers null here and keeps
                // walking exactly as it always has (`SiegeMode.CastSwingArt`). `Wear` is asked
                // every frame and answers once, which is what it exists for.
                if (!mob.Boss && mob.Swinging != null)
                    Wear(mob, raider.AtTheLine ? mob.Swinging : mob.Idle);
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

                // **A chained ward is drawn bright and held, which is the opposite of a doused
                // one and has to be.** The two states look alike in the rules — a ward that is not
                // firing — and mean opposite things to the player: a douse took the fuel and is
                // answered by pouring more in, a bind left the fuel exactly where it was and is
                // answered by waiting or by feeding somebody else. So the body keeps its coat and
                // the tube keeps its reading (invariant 37m: a light goes *up*, and there is
                // nothing wrong with this one), and what says it cannot fire is iron laid over
                // it rather than the colour draining out of it.
                //
                // **Drawn from the model every frame rather than latched when the arrow lands**,
                // for the douse's reason one line above: the frame the seconds run out is the
                // frame the chain comes off.
                else if (ward.Shackled)
                {
                    float left = Mathf.Clamp01(ward.Bound / SiegeTuning.ShacklerBind);
                    post.Glow.color = Pal.A(Casting(SiegeKind.Shackler), .22f + left * .40f);
                    post.Glow.rectTransform.localScale = Vector3.one * (1.02f + left * .16f);
                }

                // Cream, then gold, then ember: the line says how close it is to going in the
                // one place a player is already looking.
                float held = Mathf.Clamp01(ward.Health / (float)ward.Full);

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
