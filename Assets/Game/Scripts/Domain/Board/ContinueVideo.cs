using GlimmerGrove.Ads;

namespace GlimmerGrove
{
    /// <summary>
    /// Watch a video, and a lost run carries on (2026-10-11): the same allowance the gem continue
    /// hands over, offered beside it on <c>ContinueOverlay</c>.
    ///
    /// <para>
    /// <b>The owner's call, after players were not watching enough</b>: losing a run is the moment a
    /// player most wants something, and the gem price alone was a dead end for everybody holding no
    /// gems. <b>A limited number a run</b> (<c>continueVideosPerRun</c>, one as shipped), so the
    /// next continue on the same run is the gem price and the video adds to the gem continue
    /// rather than replacing it. A video continue does not raise the gem price either: the ladder
    /// counts what was paid in gems (<c>RunContinueFlow.Taken</c>).
    /// </para>
    /// <para>
    /// <b>Nothing here is adjudicated, and nothing needs to be.</b> A continue mints nothing, is
    /// stored nowhere and is gone when the run ends, and it cannot buy a grade, because
    /// <c>RunContinue.Toll</c> charges a continued run past the two-star line whichever way it was
    /// paid for. A forged one buys exactly a finish worth one star, which replaying the level buys
    /// for nothing (invariant 13's fourth clause). The signed callback is acknowledged and pays
    /// nothing, as <c>turret_discount</c>'s is.
    /// </para>
    /// <para>
    /// <b>Outside the shared cooldown and the daily caps</b>, like the turret discount: a lost run
    /// whose video key is dead because a coin video played forty seconds ago is the broken button
    /// <c>AdRules.MaxCooldownSeconds</c> warns about, met at the worst moment. The per-run count is
    /// the bound, and the network's own fill is the ceiling.
    /// </para>
    /// </summary>
    public static class ContinueVideo
    {
        /// <summary>How many video continues one run may take. Nought when switched off.</summary>
        public static int PerRun => RewardedAds.Table.ContinueVideosPerRun;

        /// <summary>
        /// Whether the offer belongs on the panel at all: switched on, and an ad unit on this
        /// platform. False until the dashboard units exist, so a build can ship ahead of them.
        /// </summary>
        public static bool Offered => PerRun > 0 && AdConfig.HasUnit(AdPlacement.ContinueVideo);

        /// <summary>Whether a run that has already taken <paramref name="watched"/> may take another.</summary>
        public static bool CanOffer(int watched) => Offered && Allows(PerRun, watched);

        /// <summary>The per-run rule on its own, pure, for the tests.</summary>
        public static bool Allows(int perRun, int watched) => perRun > 0 && watched < perRun;

        /// <summary>Whether a video is loaded right now. Moves while the panel is up.</summary>
        public static bool Ready => Offered && RewardedAds.Provider.IsReady(AdPlacement.ContinueVideo);
    }
}
