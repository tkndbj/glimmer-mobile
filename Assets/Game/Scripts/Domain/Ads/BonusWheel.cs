using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;

namespace GlimmerGrove.Ads
{
    /// <summary>The ceilings that bound whatever a content file asks the wheel to pay.</summary>
    public static class WheelRules
    {
        /// <summary>
        /// The floor on a slice, and the reason the whole feature is safe.
        ///
        /// <para>
        /// A hundred percent is the placement's authored payout — what the button promised
        /// before there was a wheel, and what a support reply quotes. Nothing may be authored
        /// below it, so the wheel can only ever <em>add</em>: a player who spins badly is paid
        /// exactly what a flat offer would have paid them. That is <c>GoldenRules.MinPercent</c>
        /// word for word, and for the same reason — a wheel whose worst slice bit would turn a
        /// published reward into a maximum, and a published reward that is really a maximum is
        /// a lie told in a store listing.
        /// </para>
        /// </summary>
        public const int MinPercent = 100;

        /// <summary>
        /// Most a slice may pay, as a percentage of the placement's amount.
        ///
        /// Ten times is already a story a player tells; past it the number stops being a prize
        /// and starts being a typo with a currency attached. Mirrors <c>GoldenRules.MaxPercent</c>
        /// deliberately: the two are the game's only variable credit multipliers, and a reader
        /// comparing them should not have to wonder why the ceilings differ.
        /// </summary>
        public const int MaxPercent = 1000;

        /// <summary>
        /// Fewest slices a wheel may have.
        ///
        /// Below four there is no wheel — there is a coin flip drawn as one, and the spin
        /// animation becomes a claim about how much was at stake that the table cannot honour.
        /// </summary>
        public const int MinSlices = 4;

        /// <summary>
        /// Most slices a wheel may have, and it is a legibility bound rather than a memory one.
        ///
        /// A slice has to carry a readable figure at about a hundred points of arc on the
        /// shortest canvas this game is drawn on. Twelve is where that stops being true, and a
        /// wheel nobody can read while it turns is a wheel nobody believes when it stops.
        /// </summary>
        public const int MaxSlices = 12;

        /// <summary>
        /// The tag mixed into the seed, separating this table's draws from every other one
        /// keyed to a subject. Contract with <c>functions/src/wheel.ts</c> — invariant 9c —
        /// and never renamed.
        /// </summary>
        public const string Tag = "wheel";

        /// <summary>The stream this table draws on. Contract, like the tag, and never renumbered.</summary>
        public const int Stream = 0;

        /// <summary>
        /// The subject a spin is seeded against: the day, and which of that day's spins it is.
        ///
        /// <para>
        /// Written out here rather than at the call sites, because it <b>is</b> the wire
        /// contract — <c>wheel.ts</c> builds the same string, and a difference in the separator
        /// re-rolls every unspun wheel in the world. The same argument
        /// <see cref="Daily.ChestRandom"/> makes about its own seed layout, one level up.
        /// </para>
        /// </summary>
        public static string Subject(int dayKey, int spinIndex) => dayKey + ":" + spinIndex;
    }

    /// <summary>One slice: what it multiplies the placement's payout by.</summary>
    public readonly struct WheelSlice
    {
        /// <summary>Percentage of the placement's authored amount. Never below 100.</summary>
        public readonly int Percent;

        public WheelSlice(int percent)
        {
            Percent = percent < WheelRules.MinPercent ? WheelRules.MinPercent
                    : percent > WheelRules.MaxPercent ? WheelRules.MaxPercent
                    : percent;
        }

        /// <summary>True when this slice pays more than the flat offer would have.</summary>
        public bool IsBonus => Percent > WheelRules.MinPercent;

        /// <summary>What this slice pays, given what the placement is worth.</summary>
        public int Pays(int baseAmount) => (int)BonusWheel.Apply(baseAmount, Percent);
    }

    /// <summary>
    /// The wheel a won glade spins for its video bonus.
    ///
    /// <para>
    /// <b>What this is, in one line.</b> It is not a second reward — it is
    /// <see cref="AdPlacement.WinBonus"/>'s payout, made variable. Everything about the
    /// placement is unchanged: one video, one server-adjudicated grant, one daily cap, one
    /// entry in the published ad table. The wheel decides the multiplier and nothing else,
    /// which is why it costs the save file nothing, costs <c>claimAwards</c> nothing, and
    /// cannot be spun without watching the ad it belongs to.
    /// </para>
    /// <para>
    /// <b>Why a multiplier and not eight authored prizes.</b> A prize the client names is a
    /// prize the server has to be told about, and invariant 10d is exactly why it cannot be
    /// told: LevelPlay 9 carries no per-impression token from the phone to the verification
    /// callback, so "the client says it won a thousand" is not evidence of anything. What the
    /// server <em>can</em> do is recompute — the daily chest's trick, invariant 9c — and a
    /// multiplier over an amount it already publishes is the smallest thing there is to
    /// recompute. So the slice is a pure function of (account, day, spin index), and the
    /// number the phone draws is the number the callback grants, arrived at independently.
    /// </para>
    /// <para>
    /// <b>Why the odds are uniform, and why that is a feature.</b> Every slice is the same
    /// size and every slice is equally likely, so the odds are one-in-<em>n</em> and can be
    /// printed on the panel — the property invariant 10b protects for the daily chest, and for
    /// the same reason: a weighted wheel drawn with equal slices is a lie the picture tells,
    /// and it is the specific lie loot-box regulation exists to catch. The variance lives in
    /// the ladder of multipliers, where a player can see all of it at once.
    /// </para>
    /// <para>
    /// <b>A spin cannot be re-rolled, and that falls out of the seed rather than being
    /// enforced.</b> Backing out of the panel, force-quitting mid-animation, or coming back an
    /// hour later all recompute the same slice from the same three inputs, so there is nothing
    /// to shop for. What advances the index is a <em>paid</em> spin — a view the server
    /// granted — and the server is what counts them; see <see cref="WheelStand"/>.
    /// </para>
    /// </summary>
    public sealed class BonusWheel
    {
        readonly WheelSlice[] _slices;

        BonusWheel(WheelSlice[] slices) => _slices = slices;

        public IReadOnlyList<WheelSlice> Slices => _slices;

        public int Count => _slices.Length;

        /// <summary>True when this wheel is worth drawing at all.</summary>
        public bool IsUsable => _slices.Length >= WheelRules.MinSlices;

        /// <summary>
        /// The chance of any one slice, as a percentage. Uniform by construction, and exposed
        /// rather than assumed so the panel prints the odds it is actually running.
        /// </summary>
        public float ChanceEach => _slices.Length == 0 ? 0f : 100f / _slices.Length;

        /// <summary>
        /// The mean multiplier in hundredths — what the placement really pays now.
        ///
        /// Rounded rather than truncated: this figure is printed by the build gate against a
        /// tuned economy, and a systematic half-percent downward bias in a validator is a
        /// validator quietly agreeing with itself.
        /// </summary>
        public int MeanPercent
        {
            get
            {
                if (_slices.Length == 0) return WheelRules.MinPercent;

                int total = 0;
                for (int i = 0; i < _slices.Length; i++) total += _slices[i].Percent;

                return (total + _slices.Length / 2) / _slices.Length;
            }
        }

        /// <summary>The best slice on the wheel, which is what a panel leads with.</summary>
        public int TopPercent
        {
            get
            {
                int top = WheelRules.MinPercent;
                for (int i = 0; i < _slices.Length; i++)
                    if (_slices[i].Percent > top) top = _slices[i].Percent;

                return top;
            }
        }

        public WheelSlice SliceAt(int index)
            => _slices.Length == 0 ? new WheelSlice(WheelRules.MinPercent)
             : _slices[((index % _slices.Length) + _slices.Length) % _slices.Length];

        /// <summary>
        /// Which slice this account's <paramref name="spinIndex"/>'th spin of the day lands on.
        ///
        /// <para>
        /// Returns -1 when there is nothing to seed from, which is the same refusal
        /// <c>DailyChests.CanOpen</c> makes and for the same reason: before the first sign-in
        /// the client would roll against a device id while the server re-rolled against the
        /// uid, and the two would disagree about money. A caller that gets -1 must show no
        /// wheel at all rather than substituting a slice — see <see cref="WheelStand.IsOpen"/>.
        /// </para>
        /// </summary>
        public int Landing(string playerKey, int dayKey, int spinIndex)
        {
            if (string.IsNullOrEmpty(playerKey) || !IsUsable) return -1;
            if (dayKey < 0 || spinIndex < 0) return -1;

            var chooser = new ChestRandom(playerKey, WheelRules.Tag,
                                          WheelRules.Subject(dayKey, spinIndex), WheelRules.Stream);

            return chooser.Below(_slices.Length);
        }

        /// <summary>
        /// Applies a percentage to an amount, the one way, in one place.
        ///
        /// Integer arithmetic with the multiply before the divide, because JavaScript has to
        /// reproduce this exactly and floating point would not. It is <c>GoldenTable.Apply</c>
        /// again, kept separate only so neither table's ceiling can be silently applied to the
        /// other's numbers.
        /// </summary>
        public static long Apply(long amount, int percent)
        {
            if (amount <= 0) return 0;
            if (percent <= WheelRules.MinPercent) return amount;

            return amount * percent / 100;
        }

        /// <summary>
        /// The wheel that ships inside the build.
        ///
        /// <para>
        /// Eight slices, uniform, from the flat offer to five times it, and the shape of the
        /// ladder is the design. Two slices pay exactly what the placement paid before there
        /// was a wheel, so a quarter of spins are "the old reward" and nobody is ever worse
        /// off; the middle is a gentle spread; and one slice in eight is a figure worth telling
        /// somebody about. The mean is 218.75%, which is why <c>win_bonus</c>'s daily cap moved
        /// from twelve to six in the same drop — the day's ceiling is very nearly where it was
        /// and every individual video is worth more than twice what it was. Fewer, better
        /// videos, which is this project's whole position on advertising.
        /// </para>
        /// <para>
        /// <b>The authored order is the drawn order</b>, which is why this list is interleaved
        /// rather than sorted: two neighbouring slices carrying the same figure make a wheel
        /// look like it has fewer prizes than it has, and the player reads that as the rim
        /// being padded. A derived shuffle was considered and dropped — it would put a
        /// permutation between "the slice the seed picked" and "the wedge under the pointer",
        /// which is one more mapping between a roll and a payout than this feature can afford.
        /// <c>ContentValidation</c> warns when a published table puts two equal figures side by
        /// side, so the rule is checked rather than merely observed here.
        /// </para>
        /// </summary>
        public static readonly BonusWheel Default = new BonusWheel(new[]
        {
            new WheelSlice(100),
            new WheelSlice(200),
            new WheelSlice(150),
            new WheelSlice(300),
            new WheelSlice(100),
            new WheelSlice(250),
            new WheelSlice(150),
            new WheelSlice(500),
        });

        /// <summary>
        /// A wheel with no slices, which is how "this build has no wheel" is expressed.
        ///
        /// <para>
        /// Distinct from <see cref="Default"/> on purpose, and the distinction is load-bearing.
        /// An absent <c>wheel</c> block means the <em>flat offer</em>, not the built-in ladder:
        /// a published table that has never heard of the wheel must keep paying exactly what it
        /// authored, or a client taking a content push would start drawing multipliers a server
        /// on the same config would never grant. Every other table here falls back to its
        /// built-in default; this one falls back to not existing.
        /// </para>
        /// </summary>
        public static readonly BonusWheel None = new BonusWheel(new WheelSlice[0]);

        // ------------------------------------------------------------- building
        /// <summary>
        /// Reads the optional <c>wheel</c> block inside <c>ads</c>. Never throws and never
        /// returns null: anything wrong is named in <paramref name="problems"/> and the flat
        /// offer stands, because a content mistake must fail a build and never a session.
        /// </summary>
        public static BonusWheel Resolve(AdWheelDto dto, List<string> problems)
        {
            problems ??= new List<string>();
            if (dto == null || dto.slices == null || dto.slices.Length == 0) return None;

            if (dto.slices.Length < WheelRules.MinSlices)
            {
                problems.Add($"ads wheel has {dto.slices.Length} slices, below the supported " +
                             $"{WheelRules.MinSlices}; the flat offer stands. Fewer than four is a " +
                             "coin flip drawn as a wheel");
                return None;
            }

            if (dto.slices.Length > WheelRules.MaxSlices)
            {
                problems.Add($"ads wheel has {dto.slices.Length} slices, above the supported " +
                             $"{WheelRules.MaxSlices}; the flat offer stands");
                return None;
            }

            var slices = new WheelSlice[dto.slices.Length];
            bool anyBonus = false;

            for (int i = 0; i < dto.slices.Length; i++)
            {
                var slice = dto.slices[i];
                if (slice == null) { problems.Add($"ads wheel slice {i} is empty"); return None; }

                if (slice.percent < WheelRules.MinPercent)
                {
                    problems.Add($"ads wheel slice {i} pays {slice.percent}%, below " +
                                 $"{WheelRules.MinPercent}%. The wheel may only ever add — a slice " +
                                 "under 100 would pay less than the flat offer the button promised");
                    return None;
                }

                if (slice.percent > WheelRules.MaxPercent)
                {
                    problems.Add($"ads wheel slice {i} pays {slice.percent}%, above the supported " +
                                 $"{WheelRules.MaxPercent}%; clamped");
                }

                slices[i] = new WheelSlice(slice.percent);
                anyBonus |= slices[i].IsBonus;
            }

            // Invariant 5d's complaint, applied to a reward table: a wheel every slice of which
            // pays the same is not a cheap wheel, it is a spin animation in front of a fixed
            // number — and the player finds out on their second spin. Refused rather than
            // warned about, because the flat offer says the same thing in one tap fewer.
            if (!anyBonus)
            {
                problems.Add("ads wheel has no slice paying above the flat offer; every spin would " +
                             "land on the same figure, so the flat offer stands");
                return None;
            }

            return new BonusWheel(slices);
        }
    }
}
