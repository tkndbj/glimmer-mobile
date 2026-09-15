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
        /// What each further continue on the same run adds to the price, <em>after</em> the
        /// factor has been applied. Zero ships, so the escalation is purely geometric.
        ///
        /// <para>
        /// It is the addend of the one recurrence <see cref="ContinueTable.PriceFor"/> runs —
        /// see <see cref="DefaultGemsFactor"/> for why the two are one rule rather than two
        /// dials. Kept at zero and kept readable because a published table still carries the
        /// key, and because a flat surcharge on top of a doubling price is a retune somebody
        /// may want without a store review.
        /// </para>
        /// </summary>
        public const long DefaultGemsStep = 0L;

        /// <summary>
        /// What each continue already taken multiplies the next one's price by, in hundredths.
        /// Two hundred ships: the price <b>doubles</b> every time, so one run's ladder is
        /// 20, 40, 80, 160, 320 gems and on.
        ///
        /// <para>
        /// <b>Hundredths rather than a float, for the reason a threshold is</b> (invariant 22's
        /// hard-won fact): two runtimes round a float differently, and a price is something a
        /// player counts towards. <c>200</c> is exactly twice; <c>1.20f</c> is not exactly 1.20
        /// on any of the three code generators this game ships through.
        /// </para>
        /// <para>
        /// <b>Why it is geometric rather than the linear step that was already here.</b> A flat
        /// surcharge is overtaken by the player's balance — twenty, thirty, forty on a run is a
        /// ladder somebody holding a bulk pack simply walks up, so the fail state stops binding
        /// after the fourth or fifth purchase and a lost run becomes a shop transaction with no
        /// ceiling (invariant 5d, asked of a price). Doubling ends the ladder by arithmetic
        /// instead: the fifth continue costs sixteen times the first, so there is always a
        /// number of second chances beyond which the honest answer is to play the board again.
        /// </para>
        /// <para>
        /// <b>And it is one recurrence with two parameters rather than two escalation rules.</b>
        /// Each continue multiplies the price and then adds the step, so a factor of 100 with a
        /// step is exactly the linear ladder this shipped with, and a factor with no step is a
        /// pure doubling. Two independent dials each claiming to decide the price is the shape
        /// this file refuses everywhere else — one number, asked once.
        /// </para>
        /// </summary>
        public const long DefaultGemsFactor = 200L;

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
        /// <b>Retired.</b> Cells of light a Lightweave continue handed over.
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
        /// Motes a well's continue hands over.
        ///
        /// <para>
        /// Smaller than the other two because the unit is bigger. A Lightfall drop is a whole
        /// turn of thinking and a well that ships is dealt somewhere between six and twenty of
        /// them, so six is between a third and a whole board's worth of second chances — the
        /// same fraction of the same <c>par x budgetFactor</c> budget the other two are, in the
        /// unit this mode is graded in (invariant 22b).
        /// </para>
        /// <para>
        /// What makes six <em>enough</em> is not this number: a continue always clears the
        /// deficit first (see <see cref="ContinueOffer"/>), so on a well that was lost because
        /// the colour it still wanted had gone out of the procession, this is working room above
        /// however many drops it takes for that colour to come round again.
        /// </para>
        /// </summary>
        public const int DefaultMotes = 6;

        /// <summary>
        /// <b>Retired.</b> Tiles a Groovekeeper continue handed over. See
        /// <c>ContinueUnit.Tiles</c> for why the unit is kept.
        /// </summary>
        public const int DefaultTiles = 6;
        public const int DefaultTaps = 4;

        /// <summary>
        /// Moves a prototype board's continue hands over.
        ///
        /// <para>
        /// Four, which is a shade under a well's six and for the same arithmetic: these boards
        /// run to a par of three to six, so a larger number would be a continue that finishes the
        /// level rather than one that finishes what the player had started. Four is a mistake
        /// undone and a move or two to spend on the fix.
        /// </para>
        /// </summary>
        public const int DefaultMoves = 4;

        /// <summary>
        /// Wards a Thornwatch continue puts back up.
        ///
        /// <para>
        /// Four, which is the whole line — <c>SiegeTuning.MaxWards</c>, written here as a plain
        /// number because a price table may not reach into a mode. This is the one unit whose
        /// authored figure is not "working room on top of a shortfall": a siege is lost when the
        /// <em>last</em> ward falls, so every ward is down when the offer is made and anything
        /// short of the line would raise some arbitrary subset of it.
        /// </para>
        /// <para>
        /// A published figure smaller than the line is legal and does what it says — the wards
        /// nearest the left come back first, which is at least deterministic — and it is left
        /// legal rather than refused because the honest use of it is a retune that makes the
        /// offer meaner, not a mistake.
        /// </para>
        /// </summary>
        public const int DefaultWards = 4;

        /// <summary>
        /// Dearest a continue may be published at, and the ceiling a climbing price tops out at.
        ///
        /// A sanity bound rather than a design one, and it is deliberately far above anything
        /// sensible: the failure it guards is a misplaced zero in a content push, which would
        /// otherwise put a price on the panel that no player could ever meet and turn every
        /// defeat into a dead end.
        ///
        /// <para>
        /// <b>It now binds a second thing, and where it binds is stated rather than left to be
        /// discovered.</b> The shipped ladder doubles from twenty, so it reaches this ceiling on
        /// the <em>ninth</em> continue of one run (5,120 clamped to 5,000) and is flat above it.
        /// Getting there means having spent 5,100 gems inside a single lost run — more than the
        /// largest pack in the shop holds — so nothing real touches it, and both content gates
        /// print the ladder and say where it tops out (invariant 37cc: a ceiling that binds is
        /// a ceiling doing a ratio's job, and this one is checked rather than assumed).
        /// </para>
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

        /// <summary>
        /// Least the price may be published to climb by, in hundredths: a hundred, which holds
        /// it still.
        ///
        /// <para>
        /// Refused rather than clamped downward for the reason a free continue is refused. A
        /// factor below a hundred is a price that gets <em>cheaper</em> the more second chances
        /// somebody has already bought, which is a fail state that stops binding after the
        /// fourth purchase — invariant 5d's complaint about a rule that rejects nothing, said
        /// about a price. A published table that asks for it is named and clamped to flat.
        /// </para>
        /// </summary>
        public const long MinGemsFactor = 100L;

        /// <summary>
        /// Most the price may be published to climb by, in hundredths: ten times per continue.
        ///
        /// A sanity bound of <see cref="MaxGems"/>' kind rather than a design one — at ten
        /// times, the second continue on a run already costs more than the largest gem pack in
        /// the shop, so anything above this is a misplaced digit rather than a tuning.
        /// </summary>
        public const long MaxGemsFactor = 1_000L;
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
        ContinueTable(bool enabled, long gems, long gemsStep, long gemsFactor, int turns,
                      int ink, int motes, int tiles, int taps, int moves, int wards)
        {
            Moves = moves;
            Wards = wards;
            Enabled = enabled;
            Gems = gems;
            GemsStep = gemsStep;
            GemsFactor = gemsFactor;
            Turns = turns;
            Ink = ink;
            Motes = motes;
            Tiles = tiles;
            Taps = taps;
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

        /// <summary>
        /// What each continue already taken adds to the next one's price, after the factor.
        /// </summary>
        public long GemsStep { get; }

        /// <summary>
        /// What each continue already taken multiplies the next one's price by, in hundredths.
        /// 200 ships, so the price doubles. See <see cref="ContinueLimits.DefaultGemsFactor"/>.
        /// </summary>
        public long GemsFactor { get; }

        /// <summary>Turns a glade's continue hands over, above whatever it took to un-lose it.</summary>
        public int Turns { get; }

        /// <summary><b>Retired.</b> See <see cref="ContinueUnit.Ink"/>.</summary>
        public int Ink { get; }

        /// <summary>Motes a well's continue hands over, on the same terms.</summary>
        public int Motes { get; }

        /// <summary>Tiles a grove's continue hands over, on the same terms.</summary>
        public int Tiles { get; }

        /// <summary>Taps a thicket's continue hands over, above whatever it took to un-lose it.</summary>
        public int Taps { get; }

        /// <summary>Moves a prototype board's continue hands over, on the same terms.</summary>
        public int Moves { get; }

        /// <summary>
        /// Wards a siege's continue puts back up. Not "on the same terms" — see
        /// <see cref="ContinueLimits.DefaultWards"/> for why this one is the whole allowance
        /// rather than room above a shortfall.
        /// </summary>
        public int Wards { get; }

        /// <summary>The rule that ships inside the build, and the floor under any content mistake.</summary>
        public static readonly ContinueTable Default =
            new ContinueTable(true,
                              ContinueLimits.DefaultGems, ContinueLimits.DefaultGemsStep,
                              ContinueLimits.DefaultGemsFactor,
                              ContinueLimits.DefaultTurns, ContinueLimits.DefaultInk,
                              ContinueLimits.DefaultMotes, ContinueLimits.DefaultTiles,
                              ContinueLimits.DefaultTaps, ContinueLimits.DefaultMoves,
                              ContinueLimits.DefaultWards);

        /// <summary>A rule with the feature switched off, for a file that asks for that.</summary>
        public static readonly ContinueTable Off =
            new ContinueTable(false, ContinueLimits.DefaultGems, ContinueLimits.DefaultGemsStep,
                              ContinueLimits.DefaultGemsFactor,
                              ContinueLimits.DefaultTurns, ContinueLimits.DefaultInk,
                              ContinueLimits.DefaultMotes, ContinueLimits.DefaultTiles,
                              ContinueLimits.DefaultTaps, ContinueLimits.DefaultMoves,
                              ContinueLimits.DefaultWards);

        /// <summary>
        /// What the next continue costs, given how many this run has already had.
        ///
        /// <para>
        /// One recurrence, applied once per continue already taken: <c>price = price ×
        /// factor ÷ 100 + step</c>. At the shipped 200 and 0 that is a doubling — 20, 40, 80,
        /// 160, 320 — and at 100 and a step it is exactly the linear ladder this shipped
        /// with, which is why the two are one rule rather than two dials that could disagree
        /// about what a continue costs.
        /// </para>
        /// <para>
        /// Integer arithmetic and a clamp, for <c>ChapterGateTable.RequiredStars</c>' reason:
        /// a price is something a player counts towards, and two runtimes round a float
        /// differently — which this project has already paid for once, in a generator that
        /// dealt two different boards for one seed. The factor is hundredths and the divide
        /// is last, which is the same rule invariant 37bh is: ten per cent of 8 taken as a
        /// float truncates back to 8, and a star somebody paid for buys nothing.
        /// </para>
        /// <para>
        /// Saturating rather than wrapping, and <b>it returns early rather than iterating a
        /// count nothing bounds</b>. A run may be continued as often as somebody can pay, so
        /// <paramref name="taken"/> is attacker-shaped in the only sense that matters here —
        /// a loop that ran it out would be a frozen board over a defeat panel. Every branch
        /// that cannot climb any further answers at once: the flat case in closed form, a
        /// price at the ceiling, and a factor that truncates back to where it started (101
        /// hundredths of 20 is 20 in integer arithmetic, and no number of continues changes
        /// that).
        /// </para>
        /// <para>
        /// Overflow is unreachable rather than guarded, which is deliberate — a check that
        /// can only ever answer no is not a check (invariant 35c). <see cref="Gems"/> is
        /// clamped to <see cref="ContinueLimits.MaxGems"/> and <see cref="GemsFactor"/> to
        /// <see cref="ContinueLimits.MaxGemsFactor"/> by the only thing that builds a table,
        /// and the loop returns the moment a price reaches the ceiling, so the largest product
        /// this arithmetic can ever form is five million.
        /// </para>
        /// </summary>
        public long PriceFor(int taken)
        {
            if (taken <= 0) return Gems;

            long factor = GemsFactor < ContinueLimits.MinGemsFactor
                              ? ContinueLimits.MinGemsFactor : GemsFactor;

            if (factor == ContinueLimits.MinGemsFactor)
            {
                if (GemsStep <= 0L) return Gems;                   // flat, whatever was bought

                // Closed form rather than `taken` turns of a loop that adds a constant.
                // Guarded before the multiply for the reason a wrapped price is worse than a
                // clamped one: it would be a *cheap* continue, which is the direction that
                // costs money.
                long headroom = ContinueLimits.MaxGems - Gems;
                if (taken > headroom / GemsStep) return ContinueLimits.MaxGems;

                long flat = Gems + taken * GemsStep;
                return flat > ContinueLimits.MaxGems ? ContinueLimits.MaxGems : flat;
            }

            long price = Gems;
            for (int i = 0; i < taken; i++)
            {
                long next = price * factor / 100L + GemsStep;

                if (next >= ContinueLimits.MaxGems) return ContinueLimits.MaxGems;

                // A factor that truncates back to where it started climbs no further, however
                // many are bought. Answering now rather than spinning out a count nothing
                // bounds, for the same arithmetic in the other direction.
                if (next <= price) return price;

                price = next;
            }

            return price;
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
                case ContinueUnit.Motes: return Motes;
                case ContinueUnit.Tiles: return Tiles;
                case ContinueUnit.Taps: return Taps;
                case ContinueUnit.Moves: return Moves;
                case ContinueUnit.Wards: return Wards;
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
            long factor = dto.gemsFactor < 0L ? ContinueLimits.DefaultGemsFactor : dto.gemsFactor;
            int turns = dto.turns < 0 ? ContinueLimits.DefaultTurns : dto.turns;
            int ink = dto.ink < 0 ? ContinueLimits.DefaultInk : dto.ink;
            int motes = dto.motes < 0 ? ContinueLimits.DefaultMotes : dto.motes;
            int tiles = dto.tiles < 0 ? ContinueLimits.DefaultTiles : dto.tiles;
            int taps = dto.taps < 0 ? ContinueLimits.DefaultTaps : dto.taps;
            int moves = dto.moves < 0 ? ContinueLimits.DefaultMoves : dto.moves;
            int wards = dto.wards < 0 ? ContinueLimits.DefaultWards : dto.wards;

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

            // Below a hundred hundredths the price *falls* as more are bought, which is the
            // one setting here that would make the fail state stop binding altogether. Named
            // and clamped to flat rather than honoured.
            if (factor < ContinueLimits.MinGemsFactor)
            {
                problems.Add($"continueRun gemsFactor is {factor}, below the " +
                             $"{ContinueLimits.MinGemsFactor} hundredths that holds a price " +
                             "still - a continue that gets cheaper the more of them one run " +
                             "has bought is a fail state that stops binding; clamped to flat");
                factor = ContinueLimits.MinGemsFactor;
            }

            if (factor > ContinueLimits.MaxGemsFactor)
            {
                problems.Add($"continueRun gemsFactor is {factor}, above the " +
                             $"{ContinueLimits.MaxGemsFactor} hundredths a price may climb by " +
                             "per continue; clamped");
                factor = ContinueLimits.MaxGemsFactor;
            }

            turns = Bound(turns, "turns", ContinueLimits.DefaultTurns, problems);
            ink = Bound(ink, "ink", ContinueLimits.DefaultInk, problems);
            motes = Bound(motes, "motes", ContinueLimits.DefaultMotes, problems);
            tiles = Bound(tiles, "tiles", ContinueLimits.DefaultTiles, problems);
            taps = Bound(taps, "taps", ContinueLimits.DefaultTaps, problems);
            moves = Bound(moves, "moves", ContinueLimits.DefaultMoves, problems);
            wards = Bound(wards, "wards", ContinueLimits.DefaultWards, problems);

            return new ContinueTable(true, gems, step, factor, turns, ink, motes, tiles, taps,
                                     moves, wards);
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
