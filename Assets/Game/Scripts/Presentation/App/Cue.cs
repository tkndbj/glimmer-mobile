using System;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// A cue sheet: beats of a celebration declared in the order they happen, each one a
    /// gap after the last.
    ///
    /// <para>
    /// <b>Why not just call <see cref="Tween.After"/>.</b> A payoff sequence is the one
    /// piece of interface where the timing <em>is</em> the design — a star that lands
    /// before the panel has settled, or a reward line that arrives on top of the rank,
    /// costs the moment its whole effect. Written as a set of absolute delays, that
    /// timing is held in arithmetic scattered across a build method, every constant
    /// silently depending on every earlier one. It had already drifted here: the win
    /// panel computed <c>.95 + .42 × stars</c> in one place and <c>1.15 + .42 × stars</c>
    /// in another, so a three-star win put its rank and its reward on the same frame
    /// while a one-star win spaced them comfortably. Nothing about that is visible when
    /// reading either line.
    /// </para>
    /// <para>
    /// A playhead makes the gaps relative and therefore local: each beat says only how
    /// long after the previous one it lands, inserting a beat cannot desynchronise the
    /// ones after it, and the sequence reads top to bottom in the order the player sees
    /// it. Beats that genuinely belong together — a sound and the pop it accompanies —
    /// share one with <see cref="With"/>.
    /// </para>
    /// <para>
    /// Every beat is owned, so a player who taps away mid-celebration cancels the rest of
    /// it rather than running it against a destroyed panel. The owner is normally the
    /// view that built the sequence.
    /// </para>
    /// </summary>
    public sealed class Cue
    {
        readonly UnityEngine.Object _owner;
        float _at;

        /// <param name="owner">Killed with this. Beats never outlive it.</param>
        /// <param name="startAt">Where the playhead begins, for a sequence joining one already running.</param>
        public Cue(UnityEngine.Object owner, float startAt = 0f)
        {
            _owner = owner;
            _at = startAt < 0f ? 0f : startAt;
        }

        /// <summary>Seconds from the sequence's start to the most recently placed beat.</summary>
        public float Playhead => _at;

        /// <summary>Advances the playhead without scheduling anything — a held breath.</summary>
        public Cue Wait(float seconds)
        {
            if (seconds > 0f) _at += seconds;
            return this;
        }

        /// <summary>
        /// Places a beat <paramref name="gap"/> seconds after the previous one, and moves
        /// the playhead to it.
        /// </summary>
        public Cue Then(float gap, Action beat)
        {
            Wait(gap);
            return With(beat);
        }

        /// <summary>
        /// Places a beat at the playhead without advancing it, for the second half of a
        /// moment that is really one thing — the flash under a bang, the sparks under a
        /// star.
        /// </summary>
        public Cue With(Action beat)
        {
            if (beat == null) return this;

            // Scheduled even at zero rather than invoked straight away, so that a caller
            // reading the sequence top to bottom gets the order it describes regardless of
            // whether the first beat happens to sit on the current frame.
            Tween.After(_at, beat, _owner);
            return this;
        }

        /// <summary>
        /// Repeats a beat <paramref name="count"/> times, <paramref name="gap"/> apart,
        /// leaving the playhead on the last of them. The index runs from zero.
        ///
        /// For the shape a payoff keeps reaching for: N of something, each a little
        /// louder and a little later than the one before.
        /// </summary>
        public Cue Repeat(int count, float gap, Action<int> beat)
        {
            for (int i = 0; i < count; i++)
            {
                int k = i;
                Then(i == 0 ? 0f : gap, () => beat(k));
            }
            return this;
        }
    }

    /// <summary>
    /// Numbers that count up to their value instead of appearing at it.
    ///
    /// <para>
    /// The reward for the run is the one line on a victory panel a player actually reads,
    /// and printing it finished spends it in a single frame. Rolling it hands the same
    /// information back as anticipation — the number is visibly still climbing, so the
    /// interesting part is always a moment ahead — which is the difference between being
    /// told what you won and watching yourself win it.
    /// </para>
    /// <para>
    /// The ticking is deliberately capped rather than one sound per unit. A hundred and
    /// fifty credits at one tick each is not a flourish, it is a machine gun, and on a
    /// phone speaker it clips. A fixed budget of ticks stretched across the roll keeps
    /// the rhythm identical whether the prize is eight or eight hundred, which also means
    /// retuning the economy can never change how the celebration sounds.
    /// </para>
    /// </summary>
    public static class Roll
    {
        /// <summary>How many ticks a roll spends, however far it has to travel.</summary>
        public const int Ticks = 9;

        /// <summary>
        /// Runs a roll from 0 to 1, ticking as it goes, and hands the eased progress to
        /// <paramref name="apply"/>. The general form — for a line carrying more than one
        /// number, where the caller owns the formatting.
        /// </summary>
        public static Tw Progress(float duration, Action<float> apply, UnityEngine.Object owner,
                                  string sfx = "tick", float volume = .34f,
                                  float pitchFrom = .92f, float pitchTo = 1.62f)
        {
            int ticked = 0;

            return Tween.Run(duration, Ease.OutCubic, t =>
            {
                apply?.Invoke(t);

                // Driven off eased progress rather than a timer, so the ticks crowd
                // together exactly where the number does and the ear hears the same
                // deceleration the eye does.
                int due = Mathf.Clamp(Mathf.FloorToInt(t * Ticks), 0, Ticks);
                while (ticked < due)
                {
                    float k = Ticks <= 1 ? 1f : ticked / (float)(Ticks - 1);
                    Audio.Sfx(sfx, volume, Mathf.Lerp(pitchFrom, pitchTo, k));
                    ticked++;
                }
            }, owner);
        }

        /// <summary>
        /// Rolls one label from <paramref name="from"/> to <paramref name="to"/>.
        /// <paramref name="format"/> turns the running value into the line to show.
        ///
        /// The final frame is written from <paramref name="to"/> itself rather than from
        /// the interpolation, so a rounding error can never leave a reward reading one
        /// short of what was actually banked.
        /// </summary>
        public static Tw Number(Text label, long from, long to, float duration,
                                Func<long, string> format, UnityEngine.Object owner = null)
        {
            if (label == null || format == null) return Tween.Run(.001f, Ease.Linear, null);

            // Explicit rather than `owner ?? label`: a destroyed UnityEngine.Object is
            // only fake-null, so the coalescing operator would hand the tween a corpse to
            // hang its lifetime on and the roll would outlive the panel.
            UnityEngine.Object host = owner != null ? owner : label;
            long span = to - from;

            return Progress(duration, t =>
            {
                if (!label) return;
                label.text = format(from + (long)Mathf.Round(span * t));
            }, host).OnDone(() =>
            {
                if (!label) return;
                label.text = format(to);
                Tween.Punch(label.transform, .16f, .3f);
            });
        }
    }
}
