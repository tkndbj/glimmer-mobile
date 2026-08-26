using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// The bounds a published continue rule is checked against, and the numbers used when
    /// there is none.
    ///
    /// <para>
    /// <c>ChapterGateLimits</c>' job for the second chance: content may retune what a
    /// continue costs and how much it hands over, it may not redefine what a continue is.
    /// Everything here is a compile-time constant precisely because it is what a published
    /// file is checked <em>against</em> — a limit that could itself be published would not be
    /// a limit.
    /// </para>
    /// </summary>
    public static class ContinueLimits
    {
        /// <summary>
        /// What one more go costs, in gems.
        ///
        /// <para>
        /// Twenty against a hundred-gem entry rung is about twenty cents, and against the
        /// six gems a day free play yields it is three days of saving — dear enough that
        /// nobody buys one out of boredom, cheap enough to be the obvious answer to losing a
        /// board that was nearly finished. It is the number most likely to be wrong on the
        /// first guess, which is exactly why it is content.
        /// </para>
        /// </summary>
        public const long DefaultGems = 20L;

        /// <summary>
        /// What each further continue on the same run adds to the price. Zero ships, so the
        /// price is flat and a run may be continued as often as the player can pay for.
        ///
        /// <para>
        /// It exists — as one integer, defaulting to the behaviour that has no escalation at
        /// all — because an escalating continue price is the single commonest retune in this
        /// genre, and the shape of the offer decides whether that retune is a content push or
        /// a store review. Nothing reads it today beyond <see cref="ContinueTable.PriceFor"/>,
        /// and that is the point: the lever is built, set to off, and costs four lines.
        /// </para>
        /// </summary>
        public const long DefaultGemsStep = 0L;

        /// <summary>
        /// Turns a glade's continue hands over.
        ///
        /// <para>
        /// Fifteen is roughly a quarter of a mid-chapter budget (par 36 is dealt 57 turns),
        /// which is enough to finish a board that ran out while it was close and nowhere near
        /// enough to brute-force one that was not. Note what it deliberately is not: a
        /// fraction of par. A player buying a second chance has to be told a number before
        /// they pay, and "+15 turns" is a promise they can check against the counter, where a
        /// figure derived from the board is one they have to take on trust.
        /// </para>
        /// </summary>
        public const int DefaultTurns = 15;

        /// <summary>
        /// Cells of light a weave's continue hands over.
        ///
        /// <para>
        /// The same fraction of the same budget, in the unit the mode is graded in
        /// (invariant 22b): a weave's ink is <c>par × budgetFactor</c> exactly as a glade's
        /// turns are, so twenty cells is between a fifth and two thirds of a grove's pot
        /// depending on its size — two or three channels' worth on any grove that ships.
        /// </para>
        /// <para>
        /// What makes twenty <em>enough</em> on the largest grove is not this number: it is
        /// that a continue always clears the deficit first (see <see cref="ContinueOffer"/>),
        /// so this is working room above whatever it took to un-lose the run rather than the
        /// whole of what is handed over.
        /// </para>
        /// </summary>
        public const int DefaultInk = 20;

        /// <summary>
        /// Dearest a continue may be published at.
        ///
        /// A sanity bound rather than a design one, and it is deliberately far above anything
        /// sensible: the failure it guards is a misplaced zero in a content push, which would
        /// otherwise put a price on the panel that no player could ever meet and turn every
        /// defeat into a dead end.
        /// </summary>
        public const long MaxGems = 5_000L;

        /// <summary>Most a continue may hand over, in either unit. Above any sensible tuning.</summary>
        public const int MaxAmount = 999;

        /// <summary>
        /// Most the price may climb per continue already taken.
        ///
        /// Bounded because <see cref="ContinueTable.PriceFor"/> multiplies it by a count with
        /// no ceiling of its own — a run may be continued as often as somebody can pay — and
        /// an unbounded step times an unbounded count is the one piece of arithmetic here
        /// that could overflow.
        /// </summary>
        public const long MaxGemsStep = 500L;
    }

    /// <summary>
    /// What a second chance costs and what it hands over — content, not code.
    ///
    /// <para>
    /// It is here for the reason the heart gate, the ad caps and the chapter gate are here.
    /// This is a price, and a price is the number in a mobile game most certain to be wrong
    /// on the first guess and most expensive to leave wrong: too high and a defeat is a dead
    /// end, too low and the move budget stops being a fail state at all. The right value is
    /// discovered from live conversion rather than known in advance, and shipping it as a
    /// <c>const</c> would mean finding out costs a store review.
    /// </para>
    /// <para>
    /// <b>It is deliberately not published to <c>config/progression</c> by the seeder</b>, for
    /// <c>chapterGate</c>'s reason: nothing about a continue is adjudicated. The gems come out
    /// of <c>CurrencyLedger.TrySpend</c>, which carries an idempotency key and is refused by
    /// <c>submitSpends</c> on the next sync if the server-derived balance could not cover it —
    /// so the money half is already defended where money is always defended here. What the
    /// gems <em>buy</em> is turns on a board, which mints nothing, is stored nowhere and is
    /// gone when the run ends. There is no second answer for a retune to put out of step with
    /// the first.
    /// </para>
    /// <para>
    /// Like every other optional block in the progression file this is not a schema bump — a
    /// client that predates it keeps the built-in numbers, and a client that has it reads a
    /// file written before the block existed and falls back to them too.
    /// </para>
    /// </summary>
    public sealed class ContinueTable
    {
        ContinueTable(bool enabled, long gems, long gemsStep, int turns, int ink)
        {
            Enabled = enabled;
            Gems = gems;
            GemsStep = gemsStep;
            Turns = turns;
            Ink = ink;
        }

        /// <summary>
        /// Whether a lost run may be continued at all.
        ///
        /// <para>
        /// A switch rather than a price of zero, because those are different statements: a
        /// free continue is a broken economy and an absent one is a design decision. It is
        /// the lever that turns the whole feature off in minutes if it ever has to be —
        /// a store review objection, a market where paying to continue is regulated, or a
        /// price that turned out to read as a trap.
        /// </para>
        /// </summary>
        public bool Enabled { get; }

        /// <summary>What the first continue on a run costs, in gems.</summary>
        public long Gems { get; }

        /// <summary>What each continue already taken adds to the next one's price.</summary>
        public long GemsStep { get; }

        /// <summary>Turns a glade's continue hands over, above whatever it took to un-lose it.</summary>
        public int Turns { get; }

        /// <summary>Cells of light a weave's continue hands over, on the same terms.</summary>
        public int Ink { get; }

        /// <summary>The rule that ships inside the build, and the floor under any content mistake.</summary>
        public static readonly ContinueTable Default =
            new ContinueTable(true,
                              ContinueLimits.DefaultGems, ContinueLimits.DefaultGemsStep,
                              ContinueLimits.DefaultTurns, ContinueLimits.DefaultInk);

        /// <summary>A rule with the feature switched off, for a file that asks for that.</summary>
        public static readonly ContinueTable Off =
            new ContinueTable(false, ContinueLimits.DefaultGems, ContinueLimits.DefaultGemsStep,
                              ContinueLimits.DefaultTurns, ContinueLimits.DefaultInk);

        /// <summary>
        /// What the next continue costs, given how many this run has already had.
        ///
        /// <para>
        /// Integer arithmetic and a clamp, for <c>ChapterGateTable.RequiredStars</c>' reason:
        /// a price is something a player counts towards, and two runtimes round a float
        /// differently — which this project has already paid for once, in a generator that
        /// dealt two different boards for one seed.
        /// </para>
        /// <para>
        /// Saturating rather than wrapping. Nothing bounds <paramref name="taken"/> — a run
        /// may be continued as often as somebody can pay — so the one arithmetic here that
        /// could run away is bounded by the same ceiling a published price is.
        /// </para>
        /// </summary>
        public long PriceFor(int taken)
        {
            if (taken <= 0 || GemsStep <= 0L) return Gems;

            // Guarded before the multiply rather than after it: `taken * GemsStep` is what
            // would overflow, and a wrapped price is a *cheap* continue rather than a dear
            // one, which is the direction that costs money.
            long headroom = ContinueLimits.MaxGems - Gems;
            if (taken > headroom / GemsStep) return ContinueLimits.MaxGems;

            long price = Gems + taken * GemsStep;
            return price > ContinueLimits.MaxGems ? ContinueLimits.MaxGems : price;
        }

        /// <summary>
        /// The working room a continue hands over in one mode's unit.
        ///
        /// Written as a switch over the unit rather than as two call sites picking a field,
        /// so a third mode with a third fail state has one place to be added to and the
        /// compiler names it.
        /// </summary>
        public int AmountFor(ContinueUnit unit)
        {
            switch (unit)
            {
                case ContinueUnit.Ink: return Ink;
                default: return Turns;
            }
        }

        // ------------------------------------------------------------------ building
        /// <summary>
        /// Reads the optional <c>continueRun</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in rule
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static ContinueTable Resolve(ContinueDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                  // absent is not an error

            // Zero withdraws the offer; below zero is "not written, inherit". It cannot be a
            // bool — see ContinueDto for why a file written before this block existed would
            // otherwise silently switch the feature off on every client.
            if (dto.enabled == 0) return Off;

            long gems = dto.gems < 0L ? ContinueLimits.DefaultGems : dto.gems;
            long step = dto.gemsStep < 0L ? ContinueLimits.DefaultGemsStep : dto.gemsStep;
            int turns = dto.turns < 0 ? ContinueLimits.DefaultTurns : dto.turns;
            int ink = dto.ink < 0 ? ContinueLimits.DefaultInk : dto.ink;

            // Zero is refused rather than clamped, and it is the one refusal here worth
            // stating: a continue that costs nothing is not a cheap continue, it is a move
            // budget that no longer ends a run — which is invariant 5d's complaint about a
            // rule that rejects nothing, applied to a fail state.
            if (gems <= 0L)
            {
                problems.Add("continueRun gems is 0, which would make a lost run free to " +
                             "continue and the move budget stop being a fail state; use " +
                             "\"enabled\": false to withdraw the offer instead");
                gems = ContinueLimits.DefaultGems;
            }

            if (gems > ContinueLimits.MaxGems)
            {
                problems.Add($"continueRun gems is {gems}, above the " +
                             $"{ContinueLimits.MaxGems} a continue may be priced at; clamped");
                gems = ContinueLimits.MaxGems;
            }

            if (step > ContinueLimits.MaxGemsStep)
            {
                problems.Add($"continueRun gemsStep is {step}, above the " +
                             $"{ContinueLimits.MaxGemsStep} a price may climb per continue; clamped");
                step = ContinueLimits.MaxGemsStep;
            }

            turns = Bound(turns, "turns", ContinueLimits.DefaultTurns, problems);
            ink = Bound(ink, "ink", ContinueLimits.DefaultInk, problems);

            return new ContinueTable(true, gems, step, turns, ink);
        }

        /// <summary>
        /// One allowance, bounded. Zero is refused for the price's reason from the other
        /// side: a continue that hands over nothing charges for a run that is still lost.
        /// </summary>
        static int Bound(int amount, string field, int fallback, List<string> problems)
        {
            if (amount <= 0)
            {
                problems.Add($"continueRun {field} is {amount}, so a paid continue would hand " +
                             "over nothing and the run would be lost again at once; " +
                             $"using {fallback}");
                return fallback;
            }

            if (amount > ContinueLimits.MaxAmount)
            {
                problems.Add($"continueRun {field} is {amount}, above the " +
                             $"{ContinueLimits.MaxAmount} a continue may hand over; clamped");
                return ContinueLimits.MaxAmount;
            }

            return amount;
        }
    }

    /// <summary>
    /// The live rule, read the way <c>HeartRules</c> and <c>ChapterGateRules</c> are — a
    /// facade over the published table, so a call site reads as it did when this was a rule
    /// nobody could tune.
    /// </summary>
    public static class ContinueRules
    {
        public static ContinueTable Table => ProgressionRules.Table.Continue;
    }
}
