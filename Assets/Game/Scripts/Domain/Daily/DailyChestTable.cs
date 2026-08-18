using System.Collections.Generic;
using GlimmerGrove.Content;

namespace GlimmerGrove.Daily
{
    /// <summary>An authored band: <c>min</c> to <c>max</c> of one kind, inclusive.</summary>
    public readonly struct ChestBand
    {
        public readonly ChestDropKind Kind;
        public readonly int Min;
        public readonly int Max;

        public ChestBand(ChestDropKind kind, int min, int max)
        {
            Kind = kind;
            Min = min < 1 ? 1 : min;
            Max = max < min ? Min : max;
        }

        public ChestDrop Resolve(ref ChestRandom random) => new ChestDrop(Kind, random.Between(Min, Max));

        /// <summary>True when the band is a single number, which reads better on a panel.</summary>
        public bool IsFixed => Min == Max;
    }

    /// <summary>A band that competes with others to be picked, carrying its weight.</summary>
    public readonly struct ChestOption
    {
        public readonly ChestBand Band;
        public readonly int Weight;

        public ChestOption(ChestBand band, int weight)
        {
            Band = band;
            Weight = weight < 1 ? 1 : weight;
        }
    }

    /// <summary>
    /// One chest: what it always contains, and the one thing it might.
    ///
    /// <para>
    /// The split is what makes the odds publishable in a sentence. A chest pays its
    /// guaranteed bands every time, then makes <b>exactly one</b> weighted pick. That is
    /// deliberately less flexible than N picks from a pool: with one pick the odds are a
    /// list of percentages that sum to a hundred, which is a thing a player can read, a
    /// store reviewer can check and a disclosure page can print. With several picks —
    /// with or without replacement — the true odds of any particular outcome stop being
    /// any number written in the file, and the honest disclosure becomes a simulation.
    /// </para>
    /// <para>
    /// The guaranteed component is also what makes a chest never a dud, which removes the
    /// usual reason games reach for a pity counter. A pity counter would need per-player
    /// state that survives the daily reset, would have to merge across devices, and would
    /// have to be recomputable by the server. None of that is worth building to solve a
    /// problem that a floor solves outright.
    /// </para>
    /// </summary>
    public sealed class ChestDefinition
    {
        readonly ChestBand[] _guaranteed;
        readonly ChestOption[] _options;

        public ChestDefinition(ChestBand[] guaranteed, ChestOption[] options)
        {
            _guaranteed = guaranteed ?? new ChestBand[0];
            _options = options ?? new ChestOption[0];

            TotalWeight = 0;
            for (int i = 0; i < _options.Length; i++) TotalWeight += _options[i].Weight;
        }

        public IReadOnlyList<ChestBand> Guaranteed => _guaranteed;
        public IReadOnlyList<ChestOption> Options => _options;

        public int TotalWeight { get; }

        /// <summary>
        /// The chance this option is the one picked, as a percentage. Rendered on the
        /// chest panel: odds a player cannot see are odds that will one day have to be
        /// explained to somebody with a legal department.
        /// </summary>
        public float ChanceOf(int optionIndex)
        {
            if (TotalWeight <= 0 || optionIndex < 0 || optionIndex >= _options.Length) return 0f;
            return 100f * _options[optionIndex].Weight / TotalWeight;
        }

        /// <summary>
        /// Everything in this chest, for the given player and day.
        ///
        /// Each band draws on its own stream so that adding a guaranteed band to a chest
        /// in a future content drop cannot shift the outcome of the weighted pick. Without
        /// that, retuning the floor would silently reroll every unopened chest in the
        /// world — including, for a few minutes either side of a config push, ones the
        /// server and the client would then disagree about.
        /// </summary>
        public List<ChestDrop> Roll(string playerKey, int dayKey, int chestIndex)
        {
            var drops = new List<ChestDrop>(_guaranteed.Length + 1);

            for (int i = 0; i < _guaranteed.Length; i++)
            {
                var random = new ChestRandom(playerKey, dayKey, chestIndex, StreamForGuaranteed(i));
                Merge(drops, _guaranteed[i].Resolve(ref random));
            }

            Merge(drops, Pick(playerKey, dayKey, chestIndex));

            return drops;
        }

        /// <summary>
        /// Folds a drop into the list, adding to an existing entry of the same kind.
        ///
        /// <para>
        /// Not cosmetic. A chest whose guaranteed band and whose bonus pick are both
        /// credits produces two drops, and both of them award the same currency — which
        /// means both would carry the id <c>daily:{day}:{chest}:credits</c>, and the
        /// second would be refused as a duplicate of the first. The player would be shown
        /// two rewards and paid for one. Worse, the server sums a chest's drops per
        /// currency, so it would grant the total and the two halves would disagree about
        /// a number in a wallet.
        /// </para>
        /// <para>
        /// Summing here fixes all of that at the source, and has the side benefit that a
        /// chest never shows the player two cards saying the same word.
        /// </para>
        /// </summary>
        static void Merge(List<ChestDrop> drops, ChestDrop drop)
        {
            if (!drop.IsValid) return;

            for (int i = 0; i < drops.Count; i++)
            {
                if (drops[i].Kind != drop.Kind) continue;
                drops[i] = new ChestDrop(drop.Kind, drops[i].Amount + drop.Amount);
                return;
            }

            drops.Add(drop);
        }

        /// <summary>The weighted pick alone, which is the half the odds describe.</summary>
        public ChestDrop Pick(string playerKey, int dayKey, int chestIndex)
        {
            if (_options.Length == 0 || TotalWeight <= 0) return ChestDrop.None;

            var chooser = new ChestRandom(playerKey, dayKey, chestIndex, StreamPick);
            int target = chooser.Below(TotalWeight);

            for (int i = 0; i < _options.Length; i++)
            {
                target -= _options[i].Weight;
                if (target >= 0) continue;

                // A second stream for the amount, so two options of the same kind and
                // band pay the same distribution regardless of where they sit in the list.
                var amount = new ChestRandom(playerKey, dayKey, chestIndex, StreamAmount);
                return _options[i].Band.Resolve(ref amount);
            }

            // Unreachable while the weights sum to TotalWeight; kept because "unreachable"
            // has a way of becoming reachable, and paying the last option is a better
            // failure than paying nothing.
            var fallback = new ChestRandom(playerKey, dayKey, chestIndex, StreamAmount);
            return _options[_options.Length - 1].Band.Resolve(ref fallback);
        }

        // Stream numbers are part of the wire contract with the server. Never renumber
        // them: doing so rerolls every chest in the world that has not yet been opened.
        internal const int StreamPick = 0;
        internal const int StreamAmount = 1;
        internal static int StreamForGuaranteed(int index) => 100 + index;
    }

    /// <summary>
    /// The daily chest rules, immutable once built.
    ///
    /// Content, not code, and for the same reason the XP curve is: the daily loop is the
    /// single most retuned system in a live puzzle game, and a build that has to go
    /// through two store reviews to change a drop rate is a build that never gets tuned.
    /// It rides in <c>progression.json</c> beside the reward table because the two are
    /// tuned together and delivered together.
    ///
    /// <para>
    /// Deliberately <b>not</b> its own schema version. It is an optional block: a client
    /// that predates it ignores the field and keeps its built-in table, and a client that
    /// has it reads a file written before it existed and does the same. That is the rule
    /// <see cref="Progression.ProgressionSchema"/> already states for new optional fields,
    /// and honouring it here is what stops a daily-chest retune from invalidating the XP
    /// curve for every player who has not updated.
    /// </para>
    /// </summary>
    public sealed class DailyChestTable
    {
        readonly ChestDefinition[] _chests;

        DailyChestTable(int runsPerChest, ChestDefinition[] chests)
        {
            RunsPerChest = runsPerChest;
            _chests = chests;
        }

        /// <summary>Runs that must be finished to earn each successive chest.</summary>
        public int RunsPerChest { get; }

        public int ChestCount => _chests.Length;

        /// <summary>Runs needed before chest <paramref name="index"/> can be opened.</summary>
        public int RunsFor(int index) => (index + 1) * RunsPerChest;

        /// <summary>Runs needed to earn every chest in a day. What the panel's bar measures.</summary>
        public int RunsForAll => ChestCount * RunsPerChest;

        public ChestDefinition Chest(int index)
            => index < 0 || index >= _chests.Length ? _chests[_chests.Length - 1] : _chests[index];

        public List<ChestDrop> Roll(string playerKey, int dayKey, int chestIndex)
            => Chest(chestIndex).Roll(playerKey, dayKey, chestIndex);

        /// <summary>
        /// The table that ships inside the build.
        ///
        /// Present so the feature works on a first launch that has not reached the
        /// content yet, and so a malformed file costs a retune rather than a session —
        /// the same bargain <see cref="Progression.ProgressionTable.Default"/> makes.
        ///
        /// The shape of it is the design: every chest guarantees credits, so no chest is
        /// ever a disappointment; hearts appear only in the first, where the floor already
        /// carries the reward and a full player loses least; and the last chest's pick is
        /// between two premium outcomes, so finishing the day always feels like a prize
        /// rather than a roll of the dice.
        /// </summary>
        public static readonly DailyChestTable Default = new DailyChestTable(3, new[]
        {
            new ChestDefinition(
                new[] { new ChestBand(ChestDropKind.Credits, 60, 90) },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Credits, 40, 70), 45),
                    new ChestOption(new ChestBand(ChestDropKind.Hearts, 1, 1), 30),
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 1, 1), 25),
                }),

            new ChestDefinition(
                new[] { new ChestBand(ChestDropKind.Credits, 110, 160) },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Credits, 80, 130), 45),
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 2, 3), 40),
                    new ChestOption(new ChestBand(ChestDropKind.HeartBoost, 24, 24), 15),
                }),

            new ChestDefinition(
                new[] { new ChestBand(ChestDropKind.Credits, 200, 280) },
                new[]
                {
                    new ChestOption(new ChestBand(ChestDropKind.Gems, 4, 6), 62),
                    new ChestOption(new ChestBand(ChestDropKind.HeartBoost, 24, 24), 38),
                }),
        });

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>daily</c> block. Never throws and never returns null:
        /// anything wrong is named in <paramref name="problems"/> and the built-in table
        /// stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static DailyChestTable Resolve(DailyChestDto dto, List<string> problems)
        {
            if (problems == null) problems = new List<string>();
            if (dto == null) return Default;                    // absent is not an error

            // Unwritten reads as -1 and inherits, the same tri-state the reward rules use.
            // Zero would mean a chest nobody has to play for, so it inherits too rather
            // than being honoured — there is no reading of "0 runs" worth shipping.
            int runsPerChest = dto.runsPerChest > 0 ? dto.runsPerChest : Default.RunsPerChest;

            if (dto.chests == null || dto.chests.Length == 0)
            {
                problems.Add("daily block lists no chests; using the built-in table");
                return Default;
            }

            if (dto.chests.Length > DailyRules.MaxChests)
            {
                problems.Add($"daily lists {dto.chests.Length} chests, more than the supported " +
                             $"{DailyRules.MaxChests}; using the built-in table");
                return Default;
            }

            var chests = new ChestDefinition[dto.chests.Length];

            for (int i = 0; i < dto.chests.Length; i++)
            {
                var chest = ReadChest(dto.chests[i], i, problems);
                if (chest == null) return Default;              // named already; refuse the lot
                chests[i] = chest;
            }

            return new DailyChestTable(runsPerChest, chests);
        }

        /// <summary>
        /// One chest, or null when it breaks a rule the whole feature depends on.
        ///
        /// The rules are few and each exists because of a specific way a live drop table
        /// goes wrong: a chest with no floor can pay nothing, a zero weight is an option
        /// that is authored but unreachable, and an unknown kind is content written
        /// against a newer build than the one reading it.
        /// </summary>
        static ChestDefinition ReadChest(DailyChestEntryDto dto, int index, List<string> problems)
        {
            if (dto == null) { problems.Add($"daily chest {index} is empty"); return null; }

            var guaranteed = new List<ChestBand>();
            if (dto.guaranteed != null)
            {
                foreach (var band in dto.guaranteed)
                {
                    if (!TryReadBand(band, index, "guaranteed", problems, out var read)) continue;
                    guaranteed.Add(read);
                }
            }

            if (guaranteed.Count == 0)
            {
                problems.Add($"daily chest {index} guarantees nothing; every chest must pay " +
                             "something or a player can open one and receive an empty screen");
                return null;
            }

            var options = new List<ChestOption>();
            if (dto.options != null)
            {
                foreach (var option in dto.options)
                {
                    if (option == null) continue;
                    if (!TryReadBand(option.AsBand(), index, "option", problems, out var read)) continue;

                    if (option.weight < 1)
                    {
                        problems.Add($"daily chest {index} option '{option.kind}' has weight " +
                                     $"{option.weight}; an option that can never be picked is " +
                                     "an odds table that lies about itself");
                        return null;
                    }

                    options.Add(new ChestOption(read, option.weight));
                }
            }

            // A chest with no options is legal — it is simply a fixed reward, which is a
            // perfectly good thing for a first chest to be.
            return new ChestDefinition(guaranteed.ToArray(), options.ToArray());
        }

        static bool TryReadBand(DailyDropDto dto, int chestIndex, string role,
                                List<string> problems, out ChestBand band)
        {
            band = default;
            if (dto == null) return false;

            var kind = ChestDropKinds.Parse(dto.kind);
            if (kind == ChestDropKind.None)
            {
                // Skipped rather than fatal: an unknown kind is how a newer content pack
                // reaches an older build, and dropping the entry degrades the table
                // instead of the game.
                problems.Add($"daily chest {chestIndex} {role} names unknown reward kind " +
                             $"'{dto.kind}'; skipped");
                return false;
            }

            // A chest is opened on the home screen, where there is no run to extend, so a
            // rolled one would pay nothing at all — and being a *guaranteed* slot it would
            // pay nothing reliably. Refused rather than skipped: an unknown kind is a newer
            // content pack reaching an older build and degrading is right, but this kind is
            // one this build knows and knows to be wrong here.
            if (ChestDropKinds.IsTransient(kind))
            {
                problems.Add($"daily chest {chestIndex} {role} pays '{dto.kind}', which is " +
                             "spent inside a run; a chest is opened where there is no run");
                return false;
            }

            if (dto.min < 1 || dto.max < dto.min)
            {
                problems.Add($"daily chest {chestIndex} {role} '{dto.kind}' has band " +
                             $"{dto.min}..{dto.max}; a band must be at least 1 and not run backwards");
                return false;
            }

            band = new ChestBand(kind, dto.min, dto.max);
            return true;
        }
    }
}
