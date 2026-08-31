using System;
using System.Collections.Generic;


namespace GlimmerGrove.Modes
{
    /// <summary>What one cue in a chain is.</summary>
    public enum BudCueKind
    {
        /// <summary>A flower that matched, winding up. <c>Over</c> runs until it goes off.</summary>
        Wind = 0,

        /// <summary>A flower going off.</summary>
        Burst = 1,

        /// <summary>Colour landing on a flower beside a bunch.</summary>
        Wash = 2,

        /// <summary>A cocoon taking a crack and holding.</summary>
        Crack = 3,

        /// <summary>A cocoon opening and a critter getting out.</summary>
        Free = 4,

        /// <summary>The grove's own answer to a wave: the jolt, the ring, the shake, the flash.</summary>
        Answer = 5,

        /// <summary>A piece coming down. <c>From</c> is where it fell from, -1 if it grew.</summary>
        Fall = 6,

        /// <summary>The board put back in step with the model, once, when nothing is moving.</summary>
        Tidy = 7,

        /// <summary>The word.</summary>
        Word = 8,

        /// <summary>The run may carry on.</summary>
        Done = 9,
    }

    /// <summary>
    /// One thing happening, at an absolute time from the tap.
    ///
    /// <para>
    /// <b>Absolute, and that is the whole point of this type.</b> Every delay in this mode used
    /// to be measured from whatever the caller happened to be holding — a fraction of a wave, a
    /// share of a burn, a ripple index — so two cues that had to be ordered were worked out by
    /// arithmetic that had never met. A cue carries the one number every other cue is comparable
    /// against, which is what makes <see cref="BudStage"/>'s rules statable at all: they are
    /// inequalities over <see cref="At"/>, so each is a test rather than a paragraph.
    /// </para>
    /// </summary>
    public readonly struct BudCue
    {
        public readonly BudCueKind Kind;

        /// <summary>Seconds from the tap.</summary>
        public readonly float At;

        /// <summary>How long it takes, where it takes any.</summary>
        public readonly float Over;

        public readonly int Wave;

        /// <summary>The cell it happens to, or -1 for a cue about the whole grove.</summary>
        public readonly int Cell;

        /// <summary>A fall's origin: the cell it came from, or -1 for one that grew.</summary>
        public readonly int From;

        /// <summary>A burst's colour, a wash's arriving colour. <c>Energy.None</c> otherwise.</summary>
        public readonly int Colour;

        /// <summary>How many were in the bunch, on a burst. 0 otherwise.</summary>
        public readonly int Bunch;

        /// <summary>
        /// How far a fall has to come, in cells.
        ///
        /// <b>Carried rather than re-derived, and that is invariant 9a at its smallest.</b> The
        /// stage decides a fall's <em>duration</em> from its distance, so a view working the
        /// distance out again from the board would be a second copy of one number — and the two
        /// only have to disagree by a cell for that piece to fall at a different speed from the
        /// one beside it, which is the fault this whole class exists to remove.
        /// </summary>
        public readonly int Rows;

        /// <summary>Its place among its own kind in its own wave, and how many there are.</summary>
        public readonly int Nth, Of;

        public BudCue(BudCueKind kind, float at, float over, int wave, int cell,
                      int from = -1, int colour = Energy.None, int bunch = 0,
                      int rows = 0, int nth = 0, int of = 1)
        {
            Kind = kind;
            At = at;
            Over = over;
            Wave = wave;
            Cell = cell;
            From = from;
            Colour = colour;
            Bunch = bunch;
            Rows = rows;
            Nth = nth;
            Of = of;
        }

        /// <summary>When it is finished with.</summary>
        public float Until => At + Over;
    }

    /// <summary>A whole chain, written out as a score.</summary>
    public sealed class BudScore
    {
        /// <summary>Every cue, in the order they happen.</summary>
        public readonly BudCue[] Cues;

        /// <summary>The chain itself: the tap to the last piece landing.</summary>
        public readonly float Body;

        /// <summary>Everything, the word included.</summary>
        public readonly float Length;

        /// <summary>
        /// What the slack had to be scaled by to meet <see cref="BudTempo.Ceiling"/>. 1 when the
        /// chain fitted on its own, which is every grove the chapter ships but its finale.
        /// </summary>
        public readonly float Squeeze;

        public BudScore(BudCue[] cues, float body, float length, float squeeze)
        {
            Cues = cues;
            Body = body;
            Length = length;
            Squeeze = squeeze;
        }
    }

    /// <summary>
    /// Where everything in a chain happens, worked out once before a frame is drawn.
    ///
    /// <para>
    /// <b>This exists because the mode had no timeline, and every complaint about its animation
    /// was that one fault seen from a different angle.</b> A chain used to be played by a
    /// coroutine that, per wave, fired several dozen independent tweens and then waited out a
    /// fixed beat. Each of those tweens worked its own delay out of its own share of that beat —
    /// the bursts from a ripple over the burst count, the falls from a budget split between a
    /// hold and a drop, the grove's answer from nothing at all — so two cues that were
    /// <em>causally</em> related were timed by arithmetic that had never met. Measured on the ten
    /// shipped groves' best opening taps, <b>seven of them dropped a flower into a hole before the
    /// burst that made the hole had been drawn</b>, six times over on the finale. What that looks
    /// like from the sofa is flowers sliding in at random spots with nothing to have caused them,
    /// and that is exactly how it was reported.
    /// </para>
    /// <para>
    /// So a chain is written out as a score first and played second, and four rules hold over it.
    /// </para>
    /// <list type="number">
    /// <item><b>Nothing is drawn before its cause.</b> A piece never begins to fall until every
    /// burst it falls into or past has gone off; a wash never lands before the bunch that sent
    /// it; a cocoon never cracks before the flower beside it bursts.</item>
    /// <item><b>One gravity.</b> Every falling piece in the game moves at
    /// <see cref="BudTempo.Pace"/>, so a fall's length is decided by its distance and by nothing
    /// else. The rule this replaced clamped a piece's duration into a per-wave budget, which
    /// meant that inside one wave a six-row drop fell half again faster than the one-row drop
    /// beside it — measured at up to a third of a cell a frame, which is the "it skips frames"
    /// this was reported as.</item>
    /// <item><b>A column collapses from the bottom.</b> The lowest piece leaves first and the one
    /// above follows, so a column reads as falling <em>into</em> something. That is a correctness
    /// rule as much as a look: two pieces of one column moving at once overlap.</item>
    /// <item><b>The ceiling is met by squeezing the slack, never the falls.</b> "The rate gives
    /// way" is this mode's own doctrine and the old code honoured it in the one place it must not
    /// — the individual piece. Dead air compresses; gravity does not.</item>
    /// </list>
    /// <para>
    /// <b>And the word rides the climax rather than following it.</b> It used to be raised after
    /// the last collapse had landed <em>and</em> after the whole board had been repainted, so the
    /// biggest thing this mode says arrived into dead air over a board that had visibly just
    /// reset — reported as the text turning up once the animation was over. It is scheduled on
    /// the last wave's answer, which is the loudest instant in the chain, and the regrowth comes
    /// down underneath it.
    /// </para>
    /// </summary>
    public static class BudStage
    {
        static readonly BudPulse[] NoPulses = new BudPulse[0];
        static readonly BudWash[] NoWashes = new BudWash[0];
        static readonly BudDrop[] NoDrops = new BudDrop[0];

        /// <summary>How many halvings the squeeze takes to settle. Twelve is under a millisecond.</summary>
        const int Halvings = 12;

        /// <summary>
        /// Writes the score for one tap.
        /// </summary>
        /// <param name="waves">How many waves the chain ran, from <c>BudChainResult</c>.</param>
        /// <param name="pulses">Every cell that did something, with the wave it did it on.</param>
        /// <param name="washes">Every flower a bunch turned.</param>
        /// <param name="drops">Every piece that moved, and where it came from.</param>
        /// <param name="width">The grove's width, which is what makes a cell a row and a column.</param>
        public static BudScore Of(int waves, BudPulse[] pulses, BudWash[] washes,
                                  BudDrop[] drops, int width)
        {
            if (pulses == null) pulses = NoPulses;
            if (washes == null) washes = NoWashes;
            if (drops == null) drops = NoDrops;
            if (width < 1) width = 1;
            if (waves < 0) waves = 0;

            // **Built at full slack first and squeezed only if it does not fit.** The squeeze is
            // a bisection rather than a formula because a wave ends when the last of several
            // things ends, so the length is a maximum over sums: piecewise linear in the slack,
            // with no closed form. Twelve halvings over arrays of a few dozen items, once a tap.
            var score = Build(waves, pulses, washes, drops, width, 1f);
            if (score.Body <= BudTempo.Ceiling) return score;

            var tight = Build(waves, pulses, washes, drops, width, BudTempo.SlackFloor);

            // Squeezed as far as it goes and still over. That is honest rather than a failure: a
            // chain deep enough to need it is the best thing that happens in this mode, and
            // running a little long is the right way to be wrong. The alternative is the one this
            // class exists to remove, which is making the pieces move faster than the eye.
            if (tight.Body > BudTempo.Ceiling) return tight;

            float lo = BudTempo.SlackFloor, hi = 1f;
            for (int i = 0; i < Halvings; i++)
            {
                float mid = (lo + hi) * .5f;
                var probe = Build(waves, pulses, washes, drops, width, mid);
                if (probe.Body > BudTempo.Ceiling) hi = mid;
                else { lo = mid; tight = probe; }
            }

            return tight;
        }

        // ------------------------------------------------------------------ the build
        static BudScore Build(int waves, BudPulse[] pulses, BudWash[] washes,
                              BudDrop[] drops, int width, float slack)
        {
            float wind = BudTempo.Wind * slack;
            float hold = BudTempo.Hold * slack;
            float rowLag = BudTempo.RowLag * slack;
            float gap = BudTempo.WaveGap * slack;
            float washLag = BudTempo.WashLag * slack;
            float crackLag = BudTempo.CrackLag * slack;

            var cues = new List<BudCue>(pulses.Length * 2 + washes.Length + drops.Length + 8);

            // When each burst of the wave being laid out goes off, by cell. Rebuilt per wave and
            // read by everything that has to come after one — the washes, the cocoons and, above
            // all, the rain.
            var burstAt = new Dictionary<int, float>(64);

            float t = 0f, body = 0f, wordAt = -1f;

            for (int wave = 0; wave <= waves; wave++)
            {
                burstAt.Clear();

                // ------------------------------------------------------ the bunch winds up
                int inWave = Count(pulses, wave, BudPulseKind.Burst);

                // A wave with nothing going off in it is the regrowth, which answers to no burst
                // and so waits for no wind-up.
                float from = inWave > 0 ? t + wind : t;
                float last = from;

                float step = inWave > 1
                    ? Min(BudTempo.BurstStep, BudTempo.BurstBody / (inWave - 1))
                    : 0f;

                int nth = 0;
                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave || pulses[i].Kind != BudPulseKind.Burst) continue;

                    float at = from + nth * step;
                    burstAt[pulses[i].Cell] = at;
                    if (at > last) last = at;

                    // **Its wind-up runs right up to the moment it goes off, rather than for a
                    // fixed beat with the remainder spent standing still.** The ripple means a
                    // flower late in a wave has longer to gather than one early in it, and
                    // drawing that costs nothing: the shape holds at its peak once it gets there
                    // (`BudTempo.Peak`), so a longer wind-up is a longer hold rather than a
                    // slower swell. What it buys is that nothing on the board is ever motionless
                    // while its neighbours are going off.
                    cues.Add(new BudCue(BudCueKind.Wind, t, at - t, wave, pulses[i].Cell,
                                        colour: pulses[i].Colour, bunch: pulses[i].Bunch,
                                        nth: nth, of: inWave));
                    cues.Add(new BudCue(BudCueKind.Burst, at, 0f, wave, pulses[i].Cell,
                                        colour: pulses[i].Colour, bunch: pulses[i].Bunch,
                                        nth: nth, of: inWave));
                    nth++;
                }

                // ------------------------------------------------------ what the bunch reached
                // A wash, a crack and a critter getting out are all *consequences* of a burst
                // beside them, so each waits on the last burst of this wave that touches it. That
                // is what makes a wave read outward from where it went off rather than as one
                // flat event with a ripple painted over the top.
                int sends = Count(washes, wave), sent = 0;
                for (int i = 0; i < washes.Length; i++)
                {
                    if (washes[i].Wave != wave) continue;

                    float at = Beside(burstAt, washes[i].Cell, width, from) + washLag;
                    cues.Add(new BudCue(BudCueKind.Wash, at, 0f, wave, washes[i].Cell,
                                        colour: washes[i].To, nth: sent++, of: sends));
                    if (at > last) last = at;
                }

                int cracks = Count(pulses, wave, BudPulseKind.Crack);
                int frees = Count(pulses, wave, BudPulseKind.Freed);
                int crack = 0, freed = 0;
                float lastGreet = float.NegativeInfinity;

                for (int i = 0; i < pulses.Length; i++)
                {
                    if (pulses[i].Wave != wave) continue;

                    if (pulses[i].Kind == BudPulseKind.Crack)
                    {
                        float at = Beside(burstAt, pulses[i].Cell, width, from) + crackLag;
                        cues.Add(new BudCue(BudCueKind.Crack, at, 0f, wave, pulses[i].Cell,
                                            nth: crack++, of: cracks));
                        if (at > last) last = at;
                        continue;
                    }

                    if (pulses[i].Kind != BudPulseKind.Freed) continue;

                    // **Two critters may never get out at once**, and this is the one spread here
                    // doing more than pacing: each carries a sound, a halo, a shockwave and a
                    // creature, and the chapter's finale frees ten on one wave. Held apart by a
                    // floor rather than by a share of anything, because what has to be true is
                    // that the player sees each of them — a fact about a person, not about how
                    // long the wave happens to be.
                    float greet = Beside(burstAt, pulses[i].Cell, width, from) + crackLag;
                    if (greet < lastGreet + BudTempo.GreetLag)
                        greet = lastGreet + BudTempo.GreetLag;
                    lastGreet = greet;

                    cues.Add(new BudCue(BudCueKind.Free, greet, 0f, wave, pulses[i].Cell,
                                        nth: freed++, of: frees));
                    if (greet > last) last = greet;
                }

                // ------------------------------------------------------ and the grove answers
                // **On the body of the ripple, not on its first frame.** The jolt, the ring, the
                // fireworks, the shake and the flash used to fire the instant the wave's *first*
                // flower went off, so on a thirteen-flower wave the screen had answered before
                // most of the wave existed. It lands where the wave actually is.
                if (inWave > 0)
                {
                    float answer = from + (last - from) * BudTempo.AnswerAt;
                    cues.Add(new BudCue(BudCueKind.Answer, answer, 0f, wave, -1,
                                        nth: wave, of: waves));

                    // The word belongs to the loudest instant in the chain, which is the last
                    // wave answering. Everything after it — the final collapse, the regrowth, the
                    // board being put back in step — happens underneath it.
                    if (wave == waves - 1) wordAt = answer;
                }

                // ------------------------------------------------------ and the grove falls
                float ended = Rain(cues, drops, wave, width, burstAt, from, hold, rowLag);
                if (ended > last) last = ended;

                if (last > body) body = last;
                t = last + gap;
            }

            // ------------------------------------------------------------------ the finish
            // Nothing is put back in step until nothing is moving. The repaint is cheap and
            // mostly a no-op, but it is a board-*wide* event, and a board-wide event laid over a
            // board still settling is the "it all resets at once" this was reported as.
            cues.Add(new BudCue(BudCueKind.Tidy, body, 0f, waves, -1));

            float length = body;

            string word = BudChain.WordKey(waves);
            if (word != null)
            {
                if (wordAt < 0f) wordAt = body;
                cues.Add(new BudCue(BudCueKind.Word, wordAt, BudTempo.Fanfare, waves, -1));

                float until = wordAt + BudTempo.Fanfare;
                if (until > length) length = until;
            }
            else if (BudChain.Counts(waves))
            {
                length += BudTempo.CountPop(waves) * 2f;
            }

            cues.Add(new BudCue(BudCueKind.Done, length, 0f, waves, -1));

            var score = cues.ToArray();
            Array.Sort(score, Order);

            return new BudScore(score, body, length, slack);
        }

        // ------------------------------------------------------------------ the rain
        /// <summary>
        /// Every piece of one wave coming down, and when the last of them lands.
        ///
        /// <para>
        /// <b>A column at a time, from the bottom, each piece after the burst it is falling
        /// into.</b> Those are rules 1 and 3 and they share a loop because they share a fact: a
        /// column empties from wherever its bursts were, so the piece nearest the floor is both
        /// the first that <em>may</em> move and the first that <em>has to</em>.
        /// </para>
        /// </summary>
        static float Rain(List<BudCue> cues, BudDrop[] drops, int wave, int width,
                          Dictionary<int, float> burstAt, float from, float hold, float rowLag)
        {
            int falling = 0;
            for (int i = 0; i < drops.Length; i++) if (drops[i].Wave == wave) falling++;
            if (falling == 0) return 0f;

            // Gathered per column so the bottom-up rule has something to be bottom-up over. The
            // model already emits them this way — `BudBoard.Fall` walks each column up from the
            // floor — but a rule that holds only because of the order somebody else happened to
            // write things in is not a rule.
            var order = new int[falling];
            int n = 0;
            for (int i = 0; i < drops.Length; i++) if (drops[i].Wave == wave) order[n++] = i;

            var by = drops;
            Array.Sort(order, (a, b) =>
            {
                int ca = by[a].Cell % width, cb = by[b].Cell % width;
                if (ca != cb) return ca - cb;

                // Deepest destination first: the piece nearest the floor leaves first.
                return (by[b].Cell / width) - (by[a].Cell / width);
            });

            float ended = 0f, previous = float.NegativeInfinity;
            int column = -1, nth = 0;

            for (int k = 0; k < order.Length; k++)
            {
                var drop = drops[order[k]];
                int at = drop.Cell % width;
                if (at != column) { column = at; previous = float.NegativeInfinity; }

                int to = drop.Cell / width;
                int came = drop.Grew ? -1 : drop.From / width;

                // **Every burst this piece falls into or past**, which is the exact causal set:
                // the cells between where it stood and where it comes to rest are precisely the
                // ones that emptied. A flower that grew enters from over the top of the grove, so
                // everything in its column at or above its destination is on its way down.
                float cause = from;
                foreach (var pair in burstAt)
                {
                    if (pair.Key % width != at) continue;

                    int row = pair.Key / width;
                    if (row <= came || row > to) continue;
                    if (pair.Value > cause) cause = pair.Value;
                }

                float start = cause + hold;
                if (start < previous + rowLag) start = previous + rowLag;
                previous = start;

                int rows = drop.Grew ? Deep(drops, wave, width, at) : to - came;
                if (rows < 1) rows = 1;

                float over = BudTempo.Falling(rows);
                cues.Add(new BudCue(BudCueKind.Fall, start, over, wave, drop.Cell,
                                    from: drop.From, rows: rows, nth: nth++, of: falling));

                if (start + over > ended) ended = start + over;
            }

            return ended;
        }

        /// <summary>
        /// How far a flower that grew into one column has to come: the depth of the hole that
        /// column lost, so a column's new flowers enter as a block and stay one.
        /// </summary>
        static int Deep(BudDrop[] drops, int wave, int width, int column)
        {
            int count = 0;
            for (int i = 0; i < drops.Length; i++)
                if (drops[i].Wave == wave && drops[i].Grew && drops[i].Cell % width == column)
                    count++;

            return count < 1 ? 1 : count;
        }

        // ------------------------------------------------------------------ small things
        /// <summary>
        /// When the last burst of this wave beside <paramref name="cell"/> went off, or
        /// <paramref name="fallback"/> if this wave put none there.
        /// </summary>
        static float Beside(Dictionary<int, float> burstAt, int cell, int width, float fallback)
        {
            float at = float.NegativeInfinity;

            if (burstAt.TryGetValue(cell - width, out float up) && up > at) at = up;
            if (burstAt.TryGetValue(cell + width, out float down) && down > at) at = down;
            if (cell % width > 0 && burstAt.TryGetValue(cell - 1, out float left) && left > at)
                at = left;
            if (cell % width < width - 1 && burstAt.TryGetValue(cell + 1, out float right)
                && right > at) at = right;

            return float.IsNegativeInfinity(at) ? fallback : at;
        }

        static int Count(BudPulse[] pulses, int wave, BudPulseKind kind)
        {
            int n = 0;
            for (int i = 0; i < pulses.Length; i++)
                if (pulses[i].Wave == wave && pulses[i].Kind == kind) n++;

            return n;
        }

        static int Count(BudWash[] washes, int wave)
        {
            int n = 0;
            for (int i = 0; i < washes.Length; i++) if (washes[i].Wave == wave) n++;

            return n;
        }

        static float Min(float a, float b) => a < b ? a : b;

        /// <summary>
        /// By time, and by kind where two land together, so a player walking the score in order
        /// never draws a burst before the wind-up it ended or a fall before the burst it is
        /// falling into. Ties are ordinary rather than exceptional — a wave of one has its wind
        /// and its burst a hair apart, and two pieces in different columns start together all the
        /// time.
        /// </summary>
        static int Order(BudCue a, BudCue b)
        {
            if (a.At < b.At) return -1;
            if (a.At > b.At) return 1;
            if (a.Kind != b.Kind) return (int)a.Kind - (int)b.Kind;

            return a.Nth - b.Nth;
        }
    }
}
