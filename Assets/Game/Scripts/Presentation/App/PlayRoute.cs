using GlimmerGrove.Content;

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
    /// Which screen plays a level, decided once.
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
    /// </summary>
    public static class PlayRoute
    {
        public static void Open(LevelId level)
        {
            var look = ModeLooks.Of(RunWording.ModeOf(level));
            Flow.Go(look.Screen, screen =>
            {
                if (screen is IPlaysLevel plays) plays.LevelId = level;
            });
        }
    }
}
