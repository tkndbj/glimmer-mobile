using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds a published <c>endless</c> block is checked against, and the numbers used
    /// when there is none.
    ///
    /// <para>
    /// <see cref="Persistence.HintLimits"/>'s job for the Infinite lane, and its argument
    /// transfers whole: content may retune what a wave is worth, it may not redefine what the
    /// ledger is allowed to hold. Everything here is a compile-time constant precisely because
    /// it is what a published file is checked <em>against</em> — a limit that could itself be
    /// published would not be a limit.
    /// </para>
    /// </summary>
    public static class EndlessLimits
    {
        // ------------------------------------------------------- the structural bound
        /// <summary>
        /// The most lifetime waves one row of <see cref="EndlessLedger"/> may ever hold.
        ///
        /// <para>
        /// <b>Not the ceiling a player experiences</b> — that is
        /// <see cref="EndlessRewardTable.MaxWaves"/>, and it is content. This is the bound the
        /// ledger's own clamp uses, and the distinction is the most important thing in this
        /// file. <see cref="Persistence.HintLimits.HardCeiling"/> carries the full argument; the
        /// short version is that clamping a stored monotonic count against a <em>published</em>
        /// number would cut it downward on whichever devices had fetched a lowered table, and a
        /// count that only ever rises is the whole of what makes it mergeable (invariant 11b).
        /// </para>
        /// <para>
        /// Deliberately far wider than anything the reward ceiling will ever be set to. This
        /// number can never be <em>lowered</em> without reintroducing exactly the bug it exists
        /// to prevent, so it is chosen once and chosen wide.
        /// </para>
        /// </summary>
        public const int HardMaxWaves = 1000000;

        // ------------------------------------------------------------ published bounds
        /// <summary>
        /// The most a content file may pay for one wave.
        ///
        /// A guard against a typo rather than a design opinion: a misplaced nought here is
        /// unbounded keeper levels for every player at once, and unlike a credit figure there is
        /// nothing downstream that would refuse it — XP is floored, so it cannot be taken back
        /// (<see cref="ProgressionStore"/>).
        /// </summary>
        public const int MaxXpPerWave = 1000;

        /// <summary>The most lifetime waves a content file may pay for. See <see cref="HardMaxWaves"/>.</summary>
        public const int MaxMaxWaves = HardMaxWaves;

        // ------------------------------------------------------------------ defaults
        /// <summary>
        /// Fifteen, which is a tenth of what a three-starred glade pays under the shipped rule
        /// (<c>60 + 30 x 3</c>), so a run that sees off ten waves is worth one such glade.
        /// </summary>
        public const int DefaultXpPerWave = 15;

        /// <summary>
        /// Ninety-nine thousand nine hundred and ninety, which is ten thousand runs of the ten
        /// waves the figure above is priced against.
        ///
        /// <para>
        /// <b>What the ceiling is for, in one sentence:</b> a lifetime wave count comes out of a
        /// run no server ever saw and cannot be recomputed from anything else in the save
        /// (invariant 10d's shape), so the only defence is that a forged one stays inside the
        /// range an honest one is drawn in. The Infinite lane is bought at the gate
        /// (<c>HeartStake.IsPaidAtDoor</c>), so honest play is heart-limited to a few runs a day
        /// and could not approach this in years.
        /// </para>
        /// <para>
        /// It is deliberately not tuned any tighter than that, for <see cref="EndlessLedger.MaxWave"/>'s
        /// reason: a ceiling a real player could ever meet is a ceiling that silently stops
        /// paying them.
        /// </para>
        /// </summary>
        public const int DefaultMaxWaves = 99990;
    }

    /// <summary>
    /// What the Infinite lane pays, in XP per wave cleared — content, not code.
    ///
    /// <para>
    /// <b>This is the one source of XP in the game that is not the star ledger, and everything
    /// awkward about it follows from that.</b> Invariant 9 says XP is derived and never
    /// accumulated, and a wave count is the one reading here that cannot be recomputed from the
    /// records the server already validates. So this shape is the narrowest thing that pays at
    /// all: a single monotonic count per level (<see cref="EndlessLedger"/>), a rate, and a
    /// ceiling. Nothing is claimed, nothing is granted, and there is no per-run state anywhere —
    /// the XP is a pure function of a number that only ever rises, which is invariant 14a's
    /// floor with a multiplier on it.
    /// </para>
    /// <para>
    /// <b>It changes nothing about how an ordinary glade pays.</b> <see cref="ProgressionLedger"/>
    /// is untouched and still walks the star ledger alone; this is a separate addend, folded in
    /// once, in <see cref="PlayerProgression"/>. That separation is not tidiness — it is what
    /// lets the shared reward vectors (invariant 9a) go on proving the star rule against the
    /// server's copy without either side learning about the Infinite lane.
    /// </para>
    /// <para>
    /// <b>The server derives the same figure</b>, in <c>functions/src/grove.ts</c>, because a
    /// published card's keeper level is recomputed there and a level the two halves disagree
    /// about is a card that silently drops whatever that level gated (invariant 19a). Change the
    /// arithmetic here and change it there; the shared vectors hold the pair.
    /// </para>
    /// <para>
    /// Like every other optional block this is deliberately <b>not</b> a schema bump. A client
    /// that predates it ignores it; a client that has it reads a file written before it existed
    /// and keeps the built-in numbers.
    /// </para>
    /// </summary>
    public sealed class EndlessRewardTable
    {
        EndlessRewardTable(int xpPerWave, int maxWaves)
        {
            XpPerWave = xpPerWave;
            MaxWaves = maxWaves;
        }

        /// <summary>
        /// XP for one wave seen off. Nought withdraws the payment without withdrawing the lane —
        /// the board, the best wave, the public board and the map badge are all untouched.
        /// </summary>
        public int XpPerWave { get; }

        /// <summary>
        /// The most lifetime waves that are ever paid for, across every row.
        ///
        /// Applied at <em>derivation</em> and never at storage, which is
        /// <see cref="EndlessLimits.HardMaxWaves"/>'s whole distinction: lowering this stops
        /// paying beyond the new figure and takes nothing out of a save, and because XP is
        /// floored (<see cref="ProgressionStore"/>) it cannot take a keeper level away either.
        /// </summary>
        public int MaxWaves { get; }

        /// <summary>The numbers that ship inside the build, and the floor under any content mistake.</summary>
        public static readonly EndlessRewardTable Default = new EndlessRewardTable(
            EndlessLimits.DefaultXpPerWave,
            EndlessLimits.DefaultMaxWaves);

        /// <summary>Whether this table pays anything at all.</summary>
        public bool Pays => XpPerWave > 0 && MaxWaves > 0;

        /// <summary>
        /// What a lifetime wave count is worth.
        ///
        /// <para>
        /// The ceiling is applied to the count and not to the product, so the two sides of the
        /// wire cannot disagree about rounding — there is none. <c>long</c> throughout because
        /// the published maximum times the published rate overflows an <c>int</c> and a wrapped
        /// XP total is a keeper level of one for somebody who earned a hundred and forty-six.
        /// </para>
        /// </summary>
        public long XpFor(long lifetimeWaves)
        {
            if (lifetimeWaves <= 0L || XpPerWave <= 0) return 0L;

            long waves = lifetimeWaves > MaxWaves ? MaxWaves : lifetimeWaves;
            return waves * XpPerWave;
        }

        /// <summary>The most this table could ever pay. Printed by both content gates.</summary>
        public long MaxXp => (long)MaxWaves * XpPerWave;

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>endless</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in numbers stand,
        /// because a content mistake must fail a build and never a session.
        ///
        /// <para>
        /// <b>Absent falls back to the built-in figures rather than to nothing</b>, and that is
        /// the one place this block differs from the referral one, which fails closed. A server
        /// that has not been seeded with this block computes a <em>lower</em> keeper level than
        /// the device does, and 19a drops — rather than clamps — whatever that level gated, in
        /// silence. Defaulting both halves to the same constants means a stale deploy agrees
        /// with the client instead of quietly publishing a smaller player. Withdrawing the
        /// payment is <c>"xpPerWave": 0</c>, which is authored and therefore visible.
        /// </para>
        /// </summary>
        public static EndlessRewardTable Resolve(EndlessRewardDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                     // absent is not an error

            int xpPerWave = Read(dto.xpPerWave, EndlessLimits.DefaultXpPerWave, 0,
                                 EndlessLimits.MaxXpPerWave, "endless xpPerWave", problems);

            int maxWaves = Read(dto.maxWaves, EndlessLimits.DefaultMaxWaves, 0,
                                EndlessLimits.MaxMaxWaves, "endless maxWaves", problems);

            // **A nought in either field means the lane pays nothing, and it is not repaired.**
            // The first version of this raised a nought ceiling back to the built-in one, on the
            // reasoning that an author writing a rate plainly meant the lane to pay — and the
            // shared vectors caught it at once, because the server does no such thing and simply
            // pays nought. Two halves that disagree about a published keeper level is the one
            // failure this whole file is written around (invariant 19a), and it is worth far more
            // than a guess at what a typo meant.
            //
            // Nothing is given up by refusing to repair. An *unwritten* ceiling is -1 and inherits
            // the built-in figure a few lines up, so the dangerous shape — a rate with no bound at
            // all — is unreachable; and nought errs toward paying less, which is the safe
            // direction for a number that cannot be taken back once floored
            // (<see cref="ProgressionStore"/>). Both content gates print the effective figures, so
            // a lane that has quietly stopped paying is visible rather than inferred.
            return new EndlessRewardTable(xpPerWave, maxWaves);
        }

        /// <summary>
        /// One authored number: unwritten inherits, out of range is clamped and named.
        /// <c>HintRuleTable</c>'s reader, and clamped for its reason — refusing one scalar would
        /// mean discarding the whole block.
        /// </summary>
        static int Read(int authored, int fallback, int min, int max, string name, List<string> problems)
        {
            if (authored < 0) return fallback;                   // -1 is "not written"

            if (authored < min)
            {
                problems.Add($"{name} is {authored}, below the supported minimum {min}; clamped");
                return min;
            }

            if (authored > max)
            {
                problems.Add($"{name} is {authored}, above the supported maximum {max}; clamped");
                return max;
            }

            return authored;
        }
    }
}
