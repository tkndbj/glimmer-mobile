using GlimmerGrove.Content;
using GlimmerGrove.Persistence;

namespace GlimmerGrove.Progression
{
    /// <summary>
    /// What a run costs, and when it costs nothing, why.
    ///
    /// <para>
    /// Three values rather than a bool because the panel that follows a defeat has to
    /// <em>say</em> which silence it is looking at, and the two free ones are different news:
    /// "you are still finding your feet" and "you have already finished this one". A bool
    /// carried the first perfectly well and would leave the panel to guess at the second — which
    /// is the guess <c>HeartCharged</c> is already carried to avoid (see
    /// <c>RunLedger.LossRecord</c>).
    /// </para>
    /// </summary>
    public enum HeartPrice
    {
        /// <summary>A heart is owed, however the run ends.</summary>
        Charged,

        /// <summary>One of a mode's opening glades, free while the mode is being learnt.</summary>
        Opening,

        /// <summary>A glade this player has already finished, free from then on.</summary>
        Replay,
    }

    /// <summary>
    /// Which runs cost a heart, and which are free.
    ///
    /// <para>
    /// <b>The rule, in two clauses.</b> The first <see cref="HeartRules.GraceLevels"/> glades of
    /// the first chapter of <em>each</em> mode cost nothing, however they end; and so does any
    /// glade this player has <em>already finished</em>, for ever. Everything else is unchanged:
    /// a loss, a forfeit and a run the process never finished all cost what they have always
    /// cost.
    /// </para>
    /// <para>
    /// <b>Why the opening is free.</b> The heart gate is the only thing in this game that can
    /// stop somebody playing, and the worst possible moment to meet it is while they are still
    /// working out what the verb is. A player who loses their first three boards to a rule they
    /// have not been taught yet is being charged for our teaching, and they have not yet
    /// decided they like the game enough to wait eight hours. Per mode rather than once per
    /// account, because a mode shipped a year from now is somebody's first board of that mode —
    /// Lightweave is dragged rather than tapped and is lost on ink rather than turns, so a
    /// player arriving at it is a beginner again in every sense that matters here.
    /// </para>
    /// <para>
    /// <b>Why a replay is free.</b> The gate exists to pace a player through content they have
    /// not seen. A glade already cleared is not content: it is a board they beat, gone back to
    /// for a better rating or for the pleasure of it, and charging for that prices the one kind
    /// of play that cannot advance anybody. It also cannot pay for itself — stars are stored and
    /// only ever promoted (invariant 22), and credits are derived from the star ledger
    /// (invariant 9) — so a replay that beats nothing is worth nothing and the gate was guarding
    /// an empty room. What it was actually charging for was mastery, and the players who go back
    /// are the ones who liked the board.
    /// </para>
    /// <para>
    /// <b>Cleared, not attempted.</b> The clause reads <c>PlayerProgress.IsCleared</c>, which is
    /// <c>Stars &gt; 0</c> — the record of a glade finished. A glade tried and lost leaves a
    /// record too and that one still costs, which is the whole distinction: the gate keeps its
    /// grip on any board that is still beating somebody.
    /// </para>
    /// <para>
    /// <b>It is asked, never stored.</b> Nothing about a free run reaches the save file, the
    /// wire or the server: a heart is simply not spent, and the star ledger — which is what
    /// every reward derives from — cannot tell the difference. That is deliberate and it is what
    /// makes the window free to retune from a config push at any time (invariant 14: a rule that
    /// can be expressed as a function of things already known should be). The replay clause
    /// stores nothing either — it reads a record the save has kept since v1.
    /// </para>
    /// <para>
    /// <b>On keying a rule to position.</b> Invariant 1 forbids keying <em>stored</em> things —
    /// save data, analytics, remote config — on a level's position, and nothing here is stored.
    /// This is the same shape as <see cref="LevelUnlock"/>'s chain, which has always asked
    /// <c>CatalogIndex.Previous</c>: a derived reading of the order the manifest publishes. A
    /// drop that inserts a glade at the head of chapter one moves the window onto it, which is
    /// the intended meaning — "the first boards a player meets" — and moves nothing anybody has
    /// already earned.
    /// </para>
    /// </summary>
    public static class HeartStake
    {
        /// <summary>
        /// What this run costs, and if nothing, why.
        ///
        /// <para>
        /// The one question, asked by every place that can take a heart for a run — the defeat
        /// (<c>RunLedger.Loss</c>), the abandonment (<c>RunScreen</c>), the door onto the board
        /// (<c>LevelsScreen</c>) and the marker a crash leaves behind (<c>RunGuard</c>, through
        /// the screen that decides whether to write one). A second copy of it would be a second
        /// place a free glade can quietly start charging.
        /// </para>
        /// <para>
        /// The clauses are tried opening-first, so a beginner replaying their second board is
        /// told about the window rather than about the replay. Both are true of that glade and
        /// only one of them is news to somebody three boards into the game.
        /// </para>
        /// <para>
        /// <see cref="HeartPrice.Charged"/> for anything nothing can name, which is the safe
        /// direction: an unnamed level is not one of the first three of anything and holds no
        /// record saying it was finished, so it is priced like every other glade rather than
        /// handed out free by a typo.
        /// </para>
        /// </summary>
        public static HeartPrice PriceOf(CatalogIndex index, LevelId level)
        {
            if (!level.IsValid) return HeartPrice.Charged;
            if (IsOpening(index, level)) return HeartPrice.Opening;

            // Deliberately not asked of the index. A clear is the player's own record and means
            // what it means whether or not the catalog can currently name the glade — one held
            // back by minAppVersion is still a glade they finished. The opening clause above is
            // the half that needs an index, because "first" is a fact about published order.
            return PlayerProgress.IsCleared(level) ? HeartPrice.Replay : HeartPrice.Charged;
        }

        /// <summary>
        /// Whether this run costs a heart when it goes wrong — the bool half of
        /// <see cref="PriceOf"/>, for the callers that price something rather than say why.
        /// </summary>
        public static bool IsFree(CatalogIndex index, LevelId level)
            => PriceOf(index, level) != HeartPrice.Charged;

        /// <summary>
        /// Whether a run priced like this may <em>begin</em> at all, with this many hearts in
        /// hand.
        ///
        /// <para>
        /// <b>The gate, and the reason it has to exist as a predicate rather than as a line in
        /// one screen.</b> A heart is charged when a run ends badly, and the gate is asked when
        /// a run begins — two different moments, joined by one rule: a run may only start if the
        /// player could pay for it if it went wrong. That rule was written into the map's node
        /// tap and nowhere else, and the map is not the only door. The victory panel's
        /// <b>next</b>, an event's tile and the restart key are three more, and all three opened
        /// a charged run on an empty bar. The restart was the expensive one, because at nought
        /// hearts the charge for the run it abandons silently takes nothing
        /// (<c>Wallet.TrySpendHeart</c> reports "already out" rather than refusing) — so the
        /// board came back, free, for ever. The one thing in this game that can stop somebody
        /// playing could be walked straight past by tapping restart.
        /// </para>
        /// <para>
        /// <c>hearts &gt; 0</c> rather than <c>hearts &gt;= DefeatCost</c>, deliberately: that
        /// is exactly <c>Hearts.CanPlay</c>, which is what the door has always asked and what
        /// <c>Wallet.TrySpendHeart</c> itself tests before spending. A published cost above one
        /// is a decision about how much a defeat takes, not about who is allowed to sit down,
        /// and reading it as an entry requirement here would quietly lock a player out of the
        /// game with a heart in hand.
        /// </para>
        /// </summary>
        public static bool CanBegin(HeartPrice price, int hearts)
            => price != HeartPrice.Charged || hearts > 0;

        /// <summary>The same question of a level, for the doors that hold one rather than a price.</summary>
        public static bool CanBegin(CatalogIndex index, LevelId level, int hearts)
            => CanBegin(PriceOf(index, level), hearts);

        /// <summary>
        /// Whether a run may be <em>restarted</em> — which is <see cref="CanBegin"/> asked after
        /// the run being walked away from has been paid for.
        ///
        /// <para>
        /// <b>A restart abandons one run and begins another</b>, and this project prices it that
        /// way on purpose (<c>RunScreen.RestartLevel</c>). So it is two answers with a charge
        /// between them, and asking only the first is what let a player with one heart restart
        /// into a board they could not afford — the abandonment took their last heart and the
        /// fresh run was never paid for at all. Refusing there is not a stricter rule than the
        /// map's, it is the <em>same</em> rule: leaving to the map and walking back in costs
        /// exactly the same heart and is refused at exactly the same point.
        /// </para>
        /// <para>
        /// <paramref name="owed"/> is whether the outgoing run has been committed. An
        /// uncommitted one is charged nothing — a player who taps restart before touching the
        /// board is putting back a run that never began — so the question collapses to
        /// <see cref="CanBegin"/>, which their entry through the door has already answered.
        /// </para>
        /// <para>
        /// The subtraction uses <c>HeartRules.DefeatCost</c> because that is what the
        /// abandonment really spends (<c>RunScreen.Forfeit</c> calls the same table through
        /// <c>Wallet.TrySpendHeart</c>), and it may go negative without harm: nothing below one
        /// is allowed to begin a charged run anyway.
        /// </para>
        /// </summary>
        public static bool CanRestart(HeartPrice price, int hearts, bool owed)
            => CanBegin(price, owed && price == HeartPrice.Charged
                                   ? hearts - HeartRules.DefeatCost
                                   : hearts);

        /// <summary>
        /// Whether this glade is one of its mode's free openings — the first clause alone, with
        /// no reference to anything the player has done.
        /// </summary>
        public static bool IsOpening(CatalogIndex index, LevelId level)
            => IsOpening(index, level, HeartRules.Table);

        /// <summary>
        /// The same question of a table that is not (yet) the live one.
        ///
        /// <para>
        /// The overload exists for <c>ContentValidation</c>, which checks the table a build
        /// <em>would</em> publish rather than the one running — a different object, possibly
        /// different numbers. Without it the validator had to re-implement the rule to report
        /// on it, which is a copy of a rule about charging players kept in the one place whose
        /// job is to prove such copies do not exist. It is the opening clause rather than the
        /// whole price for the same reason: a build gate has no player and no save, so asking
        /// what a run costs <em>this</em> player is a question with no answer at authoring time.
        /// </para>
        /// </summary>
        public static bool IsOpening(CatalogIndex index, LevelId level, HeartRuleTable hearts)
        {
            int grace = hearts?.GraceLevels ?? 0;
            if (grace <= 0 || index == null || !index.Contains(level)) return false;

            // Position within this level's own mode, which the index already keeps and which
            // starts at the mode's first chapter. Cheaper than walking a chapter's level list,
            // and it is the same reading the unlock chain takes.
            int order = index.OrderOf(level);
            if (order < 0 || order >= grace) return false;

            // The window stops at the end of the first chapter even when the published number
            // is longer than that chapter is. A chapter is where a mode's teaching is bounded,
            // and a window spilling into the second one would make "the first chapter is free"
            // untrue in the one place it is printed — see FreeLevelsIn.
            var first = index.FirstChapterIn(index.ModeOf(level));
            return first != null && index.ChapterOf(level) == first.Id;
        }

        /// <summary>
        /// How many of this chapter's glades are free to fail — nought for every chapter but
        /// the first of its mode.
        ///
        /// <para>
        /// What the information panel prints, and it is a count rather than a sentence for the
        /// reason every number on that panel is read from a table: a panel explaining the game
        /// is the first thing to rot when the game is retuned, and copy holding its own "three"
        /// would keep saying three the day the window is pushed to five.
        /// </para>
        /// <para>
        /// The opening clause only. The replay clause has no count — it is true of however many
        /// glades this player has finished, which is a fact about them rather than about the
        /// chapter, and the panel states it as a rule instead.
        /// </para>
        /// </summary>
        public static int FreeLevelsIn(CatalogIndex index, ChapterId chapter)
            => FreeLevelsIn(index, chapter, HeartRules.Table);

        /// <summary>The same count, of a table that is not (yet) the live one.</summary>
        public static int FreeLevelsIn(CatalogIndex index, ChapterId chapter, HeartRuleTable hearts)
        {
            int grace = hearts?.GraceLevels ?? 0;
            if (grace <= 0 || index == null) return 0;

            var entry = index.FindChapter(chapter);
            if (entry == null) return 0;

            var first = index.FirstChapterIn(entry.Mode);
            if (first == null || first.Id != entry.Id) return 0;

            return grace < entry.LevelCount ? grace : entry.LevelCount;
        }
    }
}
