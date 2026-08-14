using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove
{
    /// <summary>
    /// The read model the home screen paints: level, grove progress and balances.
    ///
    /// Nothing is stored here — it is a view over <see cref="PlayerProgression"/>,
    /// <see cref="Wallet"/> and the live catalog. Keeping it as a facade means the HUD
    /// has one place to ask, while progression, persistence and content each stay
    /// responsible for their own part.
    ///
    /// Note there are no setters for the balances any more. A settable balance is the
    /// exact shape of the bug the ledger exists to prevent: credits are earned by
    /// deriving them from cleared glades, granted by the server against a receipt, or
    /// spent through <see cref="PlayerProgression.TrySpend"/>. Assigning a number is
    /// none of those things.
    /// </summary>
    public static class Profile
    {
        public const int MaxHearts = Wallet.MaxHearts;

        public static string Name
        {
            get => Wallet.DisplayName;
            set => Wallet.DisplayName = value;
        }

        /// <summary>
        /// The heart state, already caught up on any refills that fell due.
        ///
        /// No setter, for the same reason the balances lost theirs: hearts are spent by
        /// losing a run, returned by the clock, or granted by the server, and assigning
        /// a number is none of those.
        /// </summary>
        public static Hearts HeartState => Wallet.Hearts;

        public static int Hearts => Wallet.Hearts.Count;

        /// <summary>Whether the player may start a run at all. The gate, asked politely.</summary>
        public static bool CanPlay => Wallet.Hearts.CanPlay;

        /// <summary>Seconds until the next heart, for a countdown. 0 when full.</summary>
        public static long SecondsToNextHeart => Wallet.Hearts.SecondsToNext(GameClock.NowUnix());

        /// <summary>
        /// A wait, written the way a person reads one.
        ///
        /// Hours get "7h 42m" and the last hour gets a ticking "42:15". A refill period
        /// of eight hours would otherwise render as "480:00", which is not a duration
        /// anybody can feel — and the seconds only earn their place once they are close
        /// enough to be worth watching.
        /// </summary>
        public static string Countdown(long seconds)
        {
            if (seconds < 0) seconds = 0;

            if (seconds >= 3600)
            {
                long hours = seconds / 3600;
                long minutes = seconds % 3600 / 60;
                return $"{hours}h {minutes:00}m";
            }

            return $"{seconds / 60}:{seconds % 60:00}";
        }

        /// <summary>The wait until the next heart, already formatted. Empty when full.</summary>
        public static string HeartCountdown()
            => Hearts >= MaxHearts ? string.Empty : Countdown(SecondsToNextHeart);

        // -- companion ----------------------------------------------------------
        /// <summary>
        /// The companion on the profile, resolved against the roster — so a save
        /// naming one this build does not ship still draws something.
        /// </summary>
        public static AvatarDefinition Avatar => AvatarCatalog.Resolve(Wallet.AvatarId);

        /// <summary>
        /// Records a choice. Refuses an id the roster does not have and one the player
        /// has not reached, so the screen cannot be the only thing enforcing the rule.
        ///
        /// Warms the new companion's art before returning, into the global cache rather
        /// than any screen's scope — the hub shows it long after the profile is closed,
        /// and a portrait freed with the picker would leave a hole in the top bar.
        /// </summary>
        public static bool TryWearAvatar(string avatarId)
        {
            var chosen = AvatarCatalog.Find(avatarId);
            if (!AvatarCatalog.IsUnlocked(chosen, Rank)) return false;

            Wallet.AvatarId = chosen.Id;
            WarmWornAvatar();
            return true;
        }

        /// <summary>
        /// Keeps the worn companion's art resident. Called at boot and on every change;
        /// one 45 KB portrait, and its flipbook only if it has one.
        ///
        /// Pinned before it is warmed, because on a change the portrait is usually
        /// already loaded — into the picker's scope, which is about to be released. Pin
        /// moves it to the global set so closing the picker cannot free the companion
        /// the hub is about to draw. For art nothing has loaded yet the pin does
        /// nothing and the preload does the work.
        /// </summary>
        public static void WarmWornAvatar()
        {
            var requests = AssetManifest.WornCompanionAssets(Avatar);
            foreach (var request in requests) AssetLibrary.Pin(request.Address);

            _ = AssetLibrary.PreloadAsync(requests);
        }

        // -- currency, derived from the ledger ----------------------------------
        public static long Coins => PlayerProgression.Credits;

        public static long Gems => PlayerProgression.Gems;

        public static bool CanAfford(long coins) => PlayerProgression.CanAfford(Currency.Credits, coins);

        public static bool Spend(long coins, string reason)
            => PlayerProgression.TrySpend(Currency.Credits, coins, reason);

        // -- level, derived from the star ledger and the reward table -----------
        public static PlayerLevel Level => PlayerProgression.Level;

        /// <summary>Kept as "rank" because that is what the HUD calls it.</summary>
        public static int Rank => PlayerProgression.Level.Level;

        /// <summary>0..1 through the current level.</summary>
        public static float RankProgress => PlayerProgression.Level.Progress01;

        public static long Xp => PlayerProgression.Xp;

        // -- grove completion, read straight from the star ledger ---------------
        // The index, not the catalog: totalling stars is a question about which glades
        // exist, and this is read every time the profile strip redraws.
        static CatalogIndex Index => GameContent.Index;

        public static int TotalStars => PlayerProgress.TotalStars(Index);

        public static int MaxStars => PlayerProgress.MaxStars(Index);

        public static float GroveProgress => MaxStars == 0 ? 0f : TotalStars / (float)MaxStars;

        /// <summary>Which milestone chests (a third, two thirds, all) have been reached.</summary>
        public static bool MilestoneReached(int index)
            => GroveProgress >= (index + 1) / 3f - 0.0001f;

        /// <summary>
        /// Glades cleared with all three stars.
        ///
        /// Counted from the records rather than stored, like everything else here. It
        /// walks the player's own history, not the catalog, so its cost is what they
        /// have played and not how much content exists.
        /// </summary>
        public static int PerfectGlades
        {
            get
            {
                int count = 0;
                foreach (var record in PlayerProgress.Records)
                    if (record.Stars >= 3) count++;
                return count;
            }
        }

        /// <summary>Chapters with every glade cleared.</summary>
        public static int ChaptersCompleted
        {
            get
            {
                var index = Index;
                int done = 0;

                foreach (var chapter in index.Chapters)
                {
                    var levels = index.LevelsOf(chapter.Id);
                    if (levels.Count == 0) continue;

                    bool all = true;
                    foreach (var level in levels)
                        if (!PlayerProgress.IsCleared(level)) { all = false; break; }

                    if (all) done++;
                }
                return done;
            }
        }

        public static int ChapterCount => Index.ChapterCount;

        /// <summary>Format 1250 as "1.2k" so pills stay narrow.</summary>
        public static string Short(long value)
        {
            if (value >= 1000000) return (value / 1000000f).ToString("0.#") + "M";
            if (value >= 10000) return (value / 1000f).ToString("0") + "k";
            return value.ToString();
        }
    }
}
