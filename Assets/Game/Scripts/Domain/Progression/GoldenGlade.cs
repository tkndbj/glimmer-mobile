using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Progression
{
    /// <summary>The ceilings that bound whatever a content file asks the bonus to pay.</summary>
    public static class GoldenRules
    {
        /// <summary>
        /// The floor on a band, and the reason the whole feature is safe.
        ///
        /// A hundred percent is the ordinary payout. Nothing may be authored below it, so
        /// the bonus can only ever <em>add</em> — it is never a tax dressed as a prize, and
        /// no player is ever quietly paid less for a glade than the reward rule says. That
        /// is not only decency: an economy where the base is a maximum and the roll is a
        /// deduction is one where the published reward is a lie, and the published reward
        /// is what a store listing and a support reply both quote.
        /// </summary>
        public const int MinPercent = 100;

        /// <summary>
        /// Most a band may pay, as a percentage. Ten times is already a story a player
        /// tells; anything past it is a typo with a currency attached.
        /// </summary>
        public const int MaxPercent = 1000;

        /// <summary>Bands a content file may author. Enough for a long tail, bounded for a phone.</summary>
        public const int MaxBands = 12;

        /// <summary>
        /// The stream this table draws on. Part of the wire contract with the server —
        /// see invariant 9c — and never renumbered.
        /// </summary>
        public const int Stream = 0;

        /// <summary>
        /// The tag mixed into the seed, separating this table's draws from any other that
        /// is keyed to a level id. Contract, like the stream.
        /// </summary>
        public const string Tag = "golden";
    }

    /// <summary>One outcome: a payout percentage and how often it comes up.</summary>
    public readonly struct GoldenBand
    {
        /// <summary>Percentage of the ordinary credit reward. Never below 100.</summary>
        public readonly int Percent;

        public readonly int Weight;

        public GoldenBand(int percent, int weight)
        {
            Percent = percent < GoldenRules.MinPercent ? GoldenRules.MinPercent
                    : percent > GoldenRules.MaxPercent ? GoldenRules.MaxPercent
                    : percent;
            Weight = weight < 0 ? 0 : weight;
        }

        /// <summary>True when this band pays more than the ordinary reward.</summary>
        public bool IsBonus => Percent > GoldenRules.MinPercent;
    }

    /// <summary>
    /// The golden: a glade that quietly pays more than it should.
    ///
    /// <para>
    /// <b>What this is for.</b> Every other reward in the game is exactly predictable — a
    /// glade is worth what the table says, every time. That is fair, legible, and, as a
    /// piece of reinforcement, weak: a reward the player can compute before they earn it
    /// stops registering as a reward at all. What does not habituate is variance. So a
    /// small fraction of glades pay more, the player cannot tell which until they clear
    /// one, and the same run that was always worth thirty credits is now worth thirty
    /// most of the time and a great deal occasionally.
    /// </para>
    /// <para>
    /// <b>Why this shape and not a roll at the end of the run.</b> The obvious
    /// implementation — draw a number when the glade is finished — is one this codebase
    /// cannot have. Currency the client decides is currency the server has to be told
    /// about, which means a claim; a claim needs an id the server can recompute; and to
    /// recompute a per-run roll the server would need to trust an attempt counter that
    /// lives in the player's own save. That is the road that ends in <c>claimAwards</c>
    /// rejecting an id forever (see <c>StreakTable</c> for where it does end).
    /// </para>
    /// <para>
    /// Seeding from <b>(account, level)</b> avoids all of it. Earned credits are already
    /// derived from the star ledger and recomputed by the server on every sync — invariant
    /// 9 — so a multiplier that is a pure function of the account and the level id simply
    /// becomes part of that derivation. Nothing is claimed, nothing is granted, nothing is
    /// stored, and the server arrives at the same number from the same two facts. It also
    /// cannot be farmed: the bonus belongs to the glade, so replaying it pays nothing, and
    /// force-quitting re-rolls nothing.
    /// </para>
    /// <para>
    /// From the player's side it is still a variable reward, which is the part that
    /// matters. They cannot know which glade is lucky until they finish it, and there are
    /// always more glades. What they cannot do is make one lucky twice.
    /// </para>
    /// </summary>
    public sealed class GoldenTable
    {
        readonly GoldenBand[] _bands;

        GoldenTable(GoldenBand[] bands)
        {
            _bands = bands;
            for (int i = 0; i < bands.Length; i++) TotalWeight += bands[i].Weight;
        }

        public IReadOnlyList<GoldenBand> Bands => _bands;

        public int TotalWeight { get; }

        /// <summary>What share of glades land in a band, as a percentage. For the disclosure.</summary>
        public float ChanceOf(int index)
        {
            if (TotalWeight <= 0 || index < 0 || index >= _bands.Length) return 0f;
            return 100f * _bands[index].Weight / TotalWeight;
        }

        /// <summary>
        /// The table that ships inside the build.
        ///
        /// <para>
        /// Four in five glades pay exactly what the reward rule says, which is what keeps
        /// the rule honest and the bonus a bonus. The tail is deliberately long and thin —
        /// a one-in-a-hundred fivefold glade is the one a player remembers and mentions,
        /// and it costs the economy about four percent on average, which is inside the
        /// noise of any tuning pass.
        /// </para>
        /// </summary>
        public static readonly GoldenTable Default = new GoldenTable(new[]
        {
            new GoldenBand(100, 80),
            new GoldenBand(150, 13),
            new GoldenBand(250, 6),
            new GoldenBand(500, 1),
        });

        /// <summary>
        /// What multiplier this player's copy of this glade pays, as a percentage.
        ///
        /// <para>
        /// Returns 100 — the ordinary reward, no bonus — when there is no account to seed
        /// from. That is the same refusal <c>DailyChests.CanOpen</c> makes and for the same
        /// reason: before the first sign-in the client would roll against a device id while
        /// the server re-rolled against the uid, and the two would disagree about money.
        /// Paying the base until an account exists is the direction that cannot cost
        /// anybody anything — the earned floor means the number can only rise afterwards.
        /// </para>
        /// </summary>
        public int PercentFor(string playerKey, LevelId level)
        {
            if (string.IsNullOrEmpty(playerKey) || !level.IsValid) return GoldenRules.MinPercent;
            if (_bands.Length == 0 || TotalWeight <= 0) return GoldenRules.MinPercent;

            var chooser = new ChestRandom(playerKey, GoldenRules.Tag, level.ToString(),
                                          GoldenRules.Stream);
            int target = chooser.Below(TotalWeight);

            int accumulated = 0;
            for (int i = 0; i < _bands.Length; i++)
            {
                accumulated += _bands[i].Weight;
                if (target < accumulated) return _bands[i].Percent;
            }

            // Unreachable while the weights sum to TotalWeight, and kept because
            // "unreachable" has a way of becoming reachable when a reader is retuned.
            return GoldenRules.MinPercent;
        }

        /// <summary>
        /// Applies a percentage to an amount, the one way, in one place.
        ///
        /// <para>
        /// Integer arithmetic with the multiply before the divide, because JavaScript has
        /// to reproduce this exactly and floating point would not. <c>long</c> on the way
        /// in and out, and the intermediate cannot overflow: credits are in the hundreds
        /// and the percentage is capped at a thousand.
        /// </para>
        /// </summary>
        public static long Apply(long credits, int percent)
        {
            if (credits <= 0) return 0;
            if (percent <= GoldenRules.MinPercent) return credits;

            return credits * percent / 100;
        }

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>golden</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in table
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static GoldenTable Resolve(GoldenDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null) return Default;                    // absent is not an error

            if (dto.bands == null || dto.bands.Length == 0)
            {
                problems.Add("golden block lists no bands; using the built-in table");
                return Default;
            }

            if (dto.bands.Length > GoldenRules.MaxBands)
            {
                problems.Add($"golden lists {dto.bands.Length} bands, above the supported " +
                             $"{GoldenRules.MaxBands}; using the built-in table");
                return Default;
            }

            var bands = new List<GoldenBand>(dto.bands.Length);

            for (int i = 0; i < dto.bands.Length; i++)
            {
                var band = dto.bands[i];
                if (band == null) { problems.Add($"golden band {i} is empty"); return Default; }

                if (band.percent < GoldenRules.MinPercent)
                {
                    problems.Add($"golden band {i} pays {band.percent}%, below {GoldenRules.MinPercent}%; " +
                                 "the bonus may only ever add. A band under 100 would quietly pay a " +
                                 "player less for a glade than the reward rule promises");
                    return Default;
                }

                if (band.percent > GoldenRules.MaxPercent)
                {
                    problems.Add($"golden band {i} pays {band.percent}%, above the supported " +
                                 $"{GoldenRules.MaxPercent}%; clamped");
                }

                if (band.weight < 1)
                {
                    problems.Add($"golden band {i} has weight {band.weight}; remove the band " +
                                 "rather than weighting it to nothing, so the published odds " +
                                 "stay a list a player can read");
                    return Default;
                }

                bands.Add(new GoldenBand(band.percent, band.weight));
            }

            return new GoldenTable(bands.ToArray());
        }
    }
}
