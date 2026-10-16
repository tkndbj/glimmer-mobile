using System;
using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using GlimmerGrove.Content;

namespace GlimmerGrove.Shuffle
{
    /// <summary>
    /// The Shuffle payout contract: ten XP and ten credits per cleared wave, on every run.
    /// Counts are kept per installation and joined by max per installation, then summed, so
    /// concurrent offline runs on two phones survive a merge without paying a retry twice.
    /// Best waves and grades never contribute. Pre-reward saves therefore start at zero.
    /// </summary>
    public static class ShuffleRewards
    {
        // Fixed payout contract, mirrored in functions/src/shuffle-rewards.ts and shared vectors.
        // Changing these re-prices lifetime progress and requires an explicit economy migration.
        public const int XpPerWave = 10;
        public const int CreditsPerWave = 10;

        // Structural guards, not daily or per-run reward caps. Even the largest accepted sum
        // and its payout fit exactly in both a C# long and a JavaScript safe integer.
        public const int MaxDevices = 64;
        public const int MaxDeviceIdLength = 64;
        public const long MaxWavesPerDevice = 1_000_000_000L;
        const string DeviceKey = "glimmer.shuffle.rewards.device";

        internal static string DeviceId
        {
            get
            {
                var store = EndlessCoins.Store;
                string id = store.Read(DeviceKey);
                if (ValidDevice(id)) return id;
                id = Guid.NewGuid().ToString("N");
                store.Write(DeviceKey, id);
                return id;
            }
        }

        public static long XpFor(long waves) => Positive(waves) * XpPerWave;
        public static long CreditsFor(long waves) => Positive(waves) * CreditsPerWave;

        static long Positive(long waves)
        {
            const long max = (long)ShuffleLedger.MaxRows * MaxDevices * MaxWavesPerDevice;
            return waves <= 0L ? 0L : waves > max ? max : waves;
        }

        internal static bool ValidDevice(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > MaxDeviceIdLength) return false;
            foreach (char c in id)
                if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '_' && c != '-')
                    return false;
            return true;
        }

        /// <summary>Strict numeric wire reading, clamped before narrowing a Firestore double.</summary>
        public static long ReadWaves(object value)
        {
            double number;
            switch (value)
            {
                case long l: number = l; break;
                case int i: number = i; break;
                case double d: number = d; break;
                case float f: number = f; break;
                default: return 0L;
            }
            if (double.IsNaN(number) || double.IsInfinity(number) || number <= 0) return 0L;
            return number >= MaxWavesPerDevice ? MaxWavesPerDevice : (long)number;
        }

        public static ShuffleWavesDto[] Join(ShuffleWavesDto[] a, ShuffleWavesDto[] b)
        {
            var counts = new SortedDictionary<string, long>(StringComparer.Ordinal);
            Absorb(counts, a);
            Absorb(counts, b);
            var rows = new List<ShuffleWavesDto>();
            foreach (var pair in counts)
            {
                if (rows.Count == MaxDevices) break;
                rows.Add(new ShuffleWavesDto { device = pair.Key, waves = pair.Value });
            }
            return rows.ToArray();
        }

        static void Absorb(SortedDictionary<string, long> counts, ShuffleWavesDto[] rows)
        {
            if (rows == null) return;
            int take = Math.Min(rows.Length, MaxDevices);
            for (int i = 0; i < take; i++)
            {
                var row = rows[i];
                if (row == null || !ValidDevice(row.device) || row.waves <= 0L) continue;
                long waves = Math.Min(row.waves, MaxWavesPerDevice);
                if (!counts.TryGetValue(row.device, out long held) || waves > held)
                    counts[row.device] = waves;
            }
        }

        internal static long Total(ShuffleWavesDto[] rows)
        {
            long total = 0L;
            foreach (var row in Join(rows, null)) total += row.waves;
            return total;
        }

        /// <summary>Canonical reading used by progression, publication and shared vectors.</summary>
        public static long WavesIn(SaveFileDto save)
        {
            long total = 0L;
            foreach (var row in ShuffleLedger.Join(save?.shuffleBest, null))
                total += Total(row.rewards);
            return total;
        }

        /// <summary>Currency only counts levels the published catalog vouches for.</summary>
        public static long CreditsIn(SaveFileDto save, IChapterMap chapters)
        {
            long waves = 0L;
            if (chapters == null) return 0L;
            foreach (var row in ShuffleLedger.Join(save?.shuffleBest, null))
                if (LevelId.TryParse(row.level, out var level, out _) && chapters.TryGetChapter(level, out _))
                    waves += Total(row.rewards);
            return CreditsFor(waves);
        }

        public static bool Same(ShuffleWavesDto[] a, ShuffleWavesDto[] b)
        {
            var x = Join(a, null);
            var y = Join(b, null);
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++)
                if (x[i].device != y[i].device || x[i].waves != y[i].waves) return false;
            return true;
        }
    }
}
