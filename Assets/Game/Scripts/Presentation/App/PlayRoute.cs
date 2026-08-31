using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;

namespace GlimmerGrove
{
    /// <summary>
    /// A screen that plays one authored level. The only thing <see cref="PlayRoute"/> needs of a
    /// mode's screen, so a new mode implements one property rather than being special-cased.
    /// </summary>
    public interface IPlaysLevel
    {
        LevelId LevelId { set; }
    }

    /// <summary>
    /// Which screen plays a level, decided once — and whether the player may be let onto it at
    /// all.
    ///
    /// <para>
    /// There are four doors into a run — a node on the map, the victory panel's <b>next</b> and
    /// its replay, and an event's tile — and before a second mode existed all four could safely
    /// say <c>Flow.Go&lt;PlayScreen&gt;</c>. Two of them still did after one arrived, which is a
    /// bug with a particularly unhelpful shape: the button labelled "next" opens a screen that
    /// finds no board, logs an error and sends the player back to the map, at the one moment
    /// they were most engaged.
    /// </para>
    /// <para>
    /// The answer comes from <see cref="ModeLooks"/>, so a mode is routed by being registered
    /// rather than by anybody remembering to add a branch here.
    /// </para>
    /// <para>
    /// <b>The heart gate is asked here for exactly that reason.</b> It used to live in the map's
    /// node tap alone, so three of these four doors opened a charged run on an empty heart bar —
    /// the victory panel's <b>next</b> and both of an event's ways in. Nothing about that is
    /// visible in a compile or a validator: the run opens, plays and can even be won, and what
    /// is broken is the one rule in this game that can stop somebody playing. A funnel every
    /// door already walks through is the only place a rule like that can be asked once, which is
    /// the same argument that put the routing here — see <c>HeartStake.CanBegin</c>.
    /// </para>
    /// </summary>
    public static class PlayRoute
    {
        /// <summary>
        /// Whether this level may be opened right now: free runs always, charged ones only with
        /// a heart to lose.
        ///
        /// <para>
        /// Public because a door with something better to say than the panel asks first — the
        /// map shakes the node it refused, and the victory panel stays up so its replay and its
        /// map keys are still under the player's thumb. Everything else lets <see cref="Open"/>
        /// answer, which is what makes the safe behaviour the default rather than the thing
        /// somebody remembered.
        /// </para>
        /// </summary>
        public static bool CanOpen(LevelId level)
            => HeartStake.CanBegin(GameContent.Index, level, Wallet.Hearts.Count);

        /// <summary>
        /// Opens the level, or refuses and says why. Answers whether the door opened, so a
        /// caller that was about to close itself can stay put.
        ///
        /// <para>
        /// The refusal raises <c>OutOfHeartsOverlay</c>, which is right here and only here: every
        /// caller of this method is <em>navigating</em>, so nothing is frozen behind the panel
        /// and its shop button may leave. A run already under way must never be refused this way
        /// — walking out of one through <c>Flow.Go</c> abandons it without resolving it, and the
        /// marker on disk then charges a heart at the next launch for a run nobody finished. That
        /// is why the restart key answers with a line over the board instead
        /// (<c>RunScreen.RestartLevel</c>), which is invariant 23's rule about a shelf rather
        /// than a navigation whenever something is standing behind the panel.
        /// </para>
        /// </summary>
        public static bool Open(LevelId level)
        {
            if (!CanOpen(level)) { Flow.Modal<OutOfHeartsOverlay>(); return false; }

            var look = ModeLooks.Of(RunWording.ModeOf(level));
            Flow.Go(look.Screen, screen =>
            {
                if (screen is IPlaysLevel plays) plays.LevelId = level;
            });

            return true;
        }
    }
}
