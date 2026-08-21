using System.Collections.Generic;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds that survive any content file, and the number used when there is none.
    ///
    /// <para>
    /// Same job <c>HeartLimits</c> does for the gate: content is allowed to retune the
    /// difficulty, it is not allowed to redefine what difficulty is. Everything here is a
    /// compile-time constant precisely because it is what a published file is checked
    /// <em>against</em> — a limit that could itself be published would not be a limit.
    /// </para>
    /// </summary>
    public static class DifficultyLimits
    {
        public const float DefaultClockScale = 1.00f;

        /// <summary>
        /// The tightest clock a published file may ask for.
        ///
        /// <para>
        /// This is the one bound in the economy whose failure is not a mistuning but an
        /// unplayable game, so it is the one that has to be a constant. Every other
        /// published number is safe in the sense that its worst case is a worse deal; a
        /// clock scale of 0.2 pushed on a Friday would make every glade in the world
        /// impossible to finish, with no app update to roll back and no way for a player to
        /// tell it from the game being broken.
        /// </para>
        /// <para>
        /// 0.6 rather than something rounder because it is derived: the shipped ramp bottoms
        /// out at a <c>timeFactor</c> of 1.5, which at this floor still needs only 1.11 taps
        /// a second merely to finish — under <c>LevelValidator</c>'s 1.2 winnability rate.
        /// Content authored tighter than that is caught by the build gate rather than by
        /// this number; see <c>LevelValidator.CheckClock</c>.
        /// </para>
        /// </summary>
        public const float MinClockScale = 0.60f;

        /// <summary>
        /// The loosest a published file may ask for. Generous on purpose: this is the
        /// direction a retune goes in an emergency, and nothing about a longer clock can
        /// hurt anybody.
        /// </summary>
        public const float MaxClockScale = 2.00f;
    }

    /// <summary>
    /// How hard the game is, as one number — content, not code.
    ///
    /// <para>
    /// This is the single most likely thing in the build to be wrong on launch day. Every
    /// other number in <c>progression.json</c> was at least tuned against something
    /// observable; difficulty was tuned by people who already knew every solution, which is
    /// the one thing no player will ever be. The right value is discovered from
    /// first-attempt clear rates in the first fortnight, and a value that needs a store
    /// review to change is a value that stays at the launch-day guess for a month.
    /// </para>
    /// <para>
    /// It scales the <em>limit</em> and nothing else. The star thresholds are held against
    /// par (<see cref="LevelTuning.TimeGoldFactor"/>), so a retune moves where a run is
    /// <em>lost</em> without moving what a clear is <em>worth</em> — which matters because
    /// earned credits are derived from the star ledger, so a scale that reached the stars
    /// would silently retune the whole economy alongside the difficulty.
    /// </para>
    /// <para>
    /// It reaches nothing that is stored. What a run records is elapsed play time, never
    /// time left, so <c>LevelRecordDto.bestMillis</c> keeps its meaning across any retune,
    /// the map badge keeps reading <c>31 turns · 2:14</c>, the merge is untouched and
    /// <c>publishGroveStats</c> needs no redeploy. <c>CountdownTests</c> is what stops that
    /// property being traded away.
    /// </para>
    /// <para>
    /// Like every other optional block, this is deliberately <b>not</b> a schema bump — a
    /// client that predates it keeps the built-in scale of 1.
    /// </para>
    /// </summary>
    public sealed class DifficultyRuleTable
    {
        DifficultyRuleTable(float clockScale) => ClockScale = clockScale;

        /// <summary>Multiplies every glade's time limit. 1 is the authored content as-is.</summary>
        public float ClockScale { get; }

        /// <summary>The numbers that ship inside the build: the content exactly as authored.</summary>
        public static readonly DifficultyRuleTable Default =
            new DifficultyRuleTable(DifficultyLimits.DefaultClockScale);

        /// <summary>
        /// A time limit with the live scale applied.
        ///
        /// <para>
        /// <see cref="int.MaxValue"/> passes through untouched, because that is the sentinel
        /// an untimed glade uses so callers can compare without special-casing — scaling it
        /// would overflow into a limit, which is the one thing it must never become. The
        /// floor of 1 second is for the same class of reason: a limit rounded to zero is not
        /// a hard glade, it is a glade lost on the frame it appears.
        /// </para>
        /// </summary>
        public int ScaleLimit(int millis)
        {
            if (millis == int.MaxValue || millis <= 0) return millis;
            if (ClockScale == DifficultyLimits.DefaultClockScale) return millis;

            return Mathf.Max(1000, Mathf.CeilToInt(millis * ClockScale));
        }

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>difficulty</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in scale
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static DifficultyRuleTable Resolve(DifficultyDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            float scale = dto.clockScale;
            if (scale <= 0f) return Default;                     // unset, the DTO's own convention

            if (scale < DifficultyLimits.MinClockScale || scale > DifficultyLimits.MaxClockScale)
            {
                problems.Add($"difficulty clockScale is {scale:0.###}, outside the " +
                             $"{DifficultyLimits.MinClockScale:0.##}–{DifficultyLimits.MaxClockScale:0.##} " +
                             "band a published file may ask for; clamped");
                scale = Mathf.Clamp(scale, DifficultyLimits.MinClockScale, DifficultyLimits.MaxClockScale);
            }

            return new DifficultyRuleTable(scale);
        }
    }

    /// <summary>
    /// The live difficulty, read the way <c>HeartRules</c> is read — a facade over the
    /// published table, so every call site looks exactly as it did when this was a constant.
    /// </summary>
    public static class DifficultyRules
    {
        public static DifficultyRuleTable Table => ProgressionRules.Table.Difficulty;

        public static float ClockScale => Table.ClockScale;

        /// <summary>A time limit with the live scale applied.</summary>
        public static int ScaleLimit(int millis) => Table.ScaleLimit(millis);
    }
}
