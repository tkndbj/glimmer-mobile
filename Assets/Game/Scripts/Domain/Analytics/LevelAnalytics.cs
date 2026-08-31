using GlimmerGrove.Content;
using GlimmerGrove.Progression;

namespace GlimmerGrove.Analytics
{
    /// <summary>
    /// The level funnel, named once so every sink sees the same event shapes.
    ///
    /// These events are what difficulty tuning actually needs: how often a level
    /// is started, how often it is finished, in how many moves, how often a player
    /// walks away instead, and how often one is lost outright.
    /// Everything else can be added later; without these, a level that is quietly
    /// killing retention in one market is invisible.
    ///
    /// <see cref="Defeated"/> is the one that decides whether the difficulty is tuned
    /// correctly. A glade players lose repeatedly is not "hard" once hearts gate
    /// play — it is a wall they pay to hit, and it needs to be visible from day one
    /// rather than inferred from a drop in <see cref="Completed"/>.
    /// </summary>
    public static class LevelAnalytics
    {
        public const string Started = "level_started";
        public const string Completed = "level_completed";
        public const string Abandoned = "level_abandoned";
        public const string Defeated = "level_defeated";
        public const string HintUsed = "level_hint_used";

        /// <summary>
        /// The two halves of the continue funnel.
        ///
        /// <para>
        /// Two events rather than one with a flag, because the number that decides whether the
        /// price is right is a <em>ratio</em> — how many of the players shown an offer took it
        /// — and a ratio needs both a numerator and a denominator that can be counted
        /// independently. One event carrying <c>accepted: false</c> would be the same data
        /// only for as long as nobody ever fails to emit it, and the panel has more ways to
        /// close than any other in the game.
        /// </para>
        /// <para>
        /// <b>Named <c>continue_*</c>, never <c>run_continue</c>.</b> That id belonged to a
        /// retired rewarded placement that bought seconds on a clock this game no longer has
        /// (invariant 22), and it still exists in a mediation dashboard; re-pointing it would
        /// silently re-label history in the one place nobody would think to look.
        /// </para>
        /// </summary>
        public const string ContinueOffered = "continue_offered";
        public const string ContinueBought = "continue_bought";

        /// <summary>
        /// The two halves of the heart-rescue funnel, which is the continue's funnel one step
        /// further down the same screen.
        ///
        /// <para>
        /// Separate events rather than a flag on the continue's, because the two offers answer
        /// different questions and a chart that could not tell them apart would answer neither.
        /// A continue is declined by somebody who <em>can</em> still play; a rescue is taken by
        /// somebody who cannot, so the ratio that matters here is against an empty heart bar
        /// rather than against a lost run. Reading one as the other would price the wrong
        /// number — and both prices are content, so the retune is cheap and the measurement is
        /// the whole cost.
        /// </para>
        /// </summary>
        public const string HeartRescueOffered = "heart_rescue_offered";
        public const string HeartRescueBought = "heart_rescue_bought";

        public static void TrackStarted(LevelDefinition level, int attempt)
        {
            if (level == null) return;
            Telemetry.Track(Started,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "attempt", attempt,
                "par", level.Tuning.Par);
        }

        public static void TrackCompleted(LevelDefinition level, int moves, int stars,
                                          int hintsUsed, float seconds, bool firstClear)
        {
            if (level == null) return;
            Telemetry.Track(Completed,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "stars", stars,
                "par", level.Tuning.Par,
                "hints_used", hintsUsed,
                "seconds", Round(seconds),
                "first_clear", firstClear);
        }

        public static void TrackAbandoned(LevelDefinition level, int moves, float seconds, string reason)
        {
            if (level == null) return;
            Telemetry.Track(Abandoned,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "seconds", Round(seconds),
                "reason", reason);
        }

        /// <summary>
        /// A lost run. Carries the hearts left afterwards, because the
        /// question that matters is not just how often players lose but how often
        /// losing is what stops them playing.
        /// </summary>
        public static void TrackDefeated(LevelDefinition level, int moves, float seconds,
                                         int heartsLeft, string reason)
        {
            if (level == null) return;
            Telemetry.Track(Defeated,
                "reason", reason,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "moves", moves,
                "seconds", Round(seconds),
                "hearts_left", heartsLeft);
        }

        /// <summary>
        /// A hint was spent on this glade.
        ///
        /// <paramref name="hintsRemaining"/> is what the <em>account</em> holds afterwards,
        /// not what is left on this board — the per-glade allowance is gone, and the pool
        /// refills on a clock and is spent across every glade. Worth knowing before reading
        /// a chart: a zero here means the player is now waiting, which is a fact about their
        /// session rather than about this level.
        /// </summary>
        public static void TrackHintUsed(LevelDefinition level, int hintsRemaining, int moves)
        {
            if (level == null) return;
            Telemetry.Track(HintUsed,
                "level_id", level.Id.Value,
                "chapter_id", level.Chapter.Value,
                "hints_remaining", hintsRemaining,
                "moves", moves);
        }

        /// <summary>
        /// A lost run was offered a way to carry on.
        ///
        /// <para>
        /// <paramref name="offer"/> carries what it cost, what it would have handed over and
        /// how many the player had already bought on this run — so a chart can separate "the
        /// price is too high" from "the second one is too high", which are different retunes
        /// of different fields. <c>choice</c> is what the player was actually in a position to
        /// do, because an offer to somebody with no gems is a different funnel entirely.
        /// </para>
        /// </summary>
        public static void TrackContinueOffered(LevelId level, ContinueOffer offer)
            => TrackContinue(ContinueOffered, level, offer);

        /// <summary>
        /// The gems were taken and the run carried on. Emitted after the debit lands, never
        /// before, so this count and the ledger cannot disagree.
        /// </summary>
        public static void TrackContinueBought(LevelId level, ContinueOffer offer)
            => TrackContinue(ContinueBought, level, offer);

        /// <summary>
        /// One shape for both halves, so the funnel's two ends stay joinable on every field.
        /// The chapter is looked up rather than passed because the caller is <c>RunScreen</c>,
        /// which holds the staked level id and not the definition behind it.
        /// </summary>
        static void TrackContinue(string name, LevelId level, ContinueOffer offer)
        {
            if (!level.IsValid) return;

            Telemetry.Track(name,
                "level_id", level.Value,
                "chapter_id", GameContent.ChapterOf(level).Value,
                "unit", offer.Unit == ContinueUnit.Ink ? "ink" : "turns",
                "gems", offer.Gems,
                "amount", offer.Amount,
                "taken", offer.Taken,
                "choice", offer.Choice == GemChoice.Spend ? "spend" : "buy_gems");
        }

        /// <summary>
        /// A player with an empty heart bar was offered hearts for gems.
        ///
        /// <paramref name="offer"/> carries the price and the amount, so a retune of either
        /// can be read against the take-up it produced, and <c>choice</c> separates the two
        /// funnels that share this panel — somebody holding the gems is one tap from playing,
        /// somebody who is not has a shop to visit first and a much longer way to fall out.
        /// </summary>
        public static void TrackHeartRescueOffered(LevelId level, HeartRescueOffer offer,
                                                   HeartRescueWhere where)
            => TrackHeartRescue(HeartRescueOffered, level, offer, where);

        /// <summary>
        /// The gems were taken and the hearts granted. Emitted after the debit lands, never
        /// before, so this count and the ledger cannot disagree.
        /// </summary>
        public static void TrackHeartRescueBought(LevelId level, HeartRescueOffer offer,
                                                  HeartRescueWhere where)
            => TrackHeartRescue(HeartRescueBought, level, offer, where);

        /// <summary>One shape for both halves, so the funnel stays joinable on every field.</summary>
        static void TrackHeartRescue(string name, LevelId level, HeartRescueOffer offer,
                                     HeartRescueWhere where)
        {
            if (!level.IsValid) return;

            Telemetry.Track(name,
                "level_id", level.Value,
                "chapter_id", GameContent.ChapterOf(level).Value,
                "gems", offer.Gems,
                "hearts", offer.Hearts,
                "where", where == HeartRescueWhere.Restart ? "restart" : "defeat",
                "choice", offer.Choice == GemChoice.Spend ? "spend" : "buy_gems");
        }

        static float Round(float seconds) => UnityEngine.Mathf.Round(seconds * 10f) / 10f;
    }
}
