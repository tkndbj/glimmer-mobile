using GlimmerGrove.Challenges;
using GlimmerGrove.Layout;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The daily challenge's defeat panel: the run's own paper panel and ribbon
    /// (<see cref="ModalView.MakePanel"/>), the line's own title and reason, and the two ways
    /// out — without the hearts, because a challenge has none to lose.
    ///
    /// <para>
    /// <b>The run's <c>DefeatOverlay</c> is a heart panel</b>: its stack is decided by whether a
    /// heart was charged, whether one is left, and whether a video or a purchase can put one
    /// back, and it rebuilds on the wallet. None of that exists here (invariant 56: no hearts,
    /// no continue, no reward path), so standing that panel would mean feeding it a heart
    /// price for a run that has no price. What is shared instead is the <em>design</em> — the
    /// same panel builder, the same measurements (<see cref="DefeatPanel"/>, with nothing on
    /// offer), the same button skins in the same seats — so a player who has lost on the
    /// ladder recognises this at a glance.
    /// </para>
    /// <para>
    /// <b>TRY AGAIN deals the same level again while the day allows</b>, which is what a loss
    /// means here (invariant 56g: a loss retries the same slot). When no play is left, the
    /// retry key is replaced by the sentence that says so, in the seat the run's panel gives
    /// its out-of-hearts note, and the one key left is the list.
    /// </para>
    /// </summary>
    public sealed class ChallengeDefeatOverlay : ModalView
    {
        /// <summary>The genre lost, so TRY AGAIN can deal it again.</summary>
        public ChallengeGenre Genre;

        protected override void Build()
        {
            // Live rather than remembered: a play is spent when a board is dealt, and the
            // answer is what the ledger says now.
            bool again = ChallengeLedger.CanPlay(Genre);

            var stack = DefeatPanel.Of(again, watching: false, rescuing: false);

            MakePanel(new Vector2(DefeatPanel.Width, stack.Height),
                      Loc.Get("ui.defeat.wards_title"), dismissOnScrim: false);

            // Why, in the sentence the siege says about a fallen line — it is the same line,
            // fed the same way — in the seat the run's panel keeps for its free-run sentence.
            UIKit.Shrinkable(Body("Why", Loc.Get("ui.defeat.wards_reason"),
                                  -DefeatPanel.FreeCentre(false), DefeatPanel.FreeHeight, Pal.Moss), 22);

            if (stack.HasRetry)
            {
                UIKit.TextButton("Retry", Panel, "btn_green", Loc.Get("ui.defeat.try_again"), 52,
                                 new Vector2(620f, DefeatPanel.RetryHeight), new Vector2(.5f, 1f),
                                 new Vector2(0f, -stack.Retry),
                                 () => Close(Again, quiet: true));
            }

            if (stack.HasNote)
                UIKit.Shrinkable(Body("Spent", Loc.Get("ui.challenges.no_plays"),
                                      -stack.Note, DefeatPanel.NoteHeight), 22);

            UIKit.TextButton("List", Panel, "btn_blue", Loc.Get("ui.challenges.title").ToUpperInvariant(), 46,
                             new Vector2(620f, DefeatPanel.GladesHeight), new Vector2(.5f, 1f),
                             new Vector2(0f, -stack.Glades),
                             () => Close(ToList));
        }

        void Again()
        {
            var genre = Genre;
            Flow.Go<ChallengeScreen>(v => v.Genre = genre);
        }

        static void ToList() => Flow.Go<DailyChallengesScreen>();

        Text Body(string name, string text, float y, float height, Color? colour = null)
            => UIKit.Titled(name, Panel, text, 32, colour ?? new Color(.36f, .25f, .18f),
                            TextAnchor.MiddleCenter, new Vector2(680f, height),
                            new Vector2(.5f, 1f), new Vector2(0f, y),
                            outline: 0f, shadow: 0f, wrap: true);

        public override bool OnBack()
        {
            Close(ToList);
            return true;
        }
    }
}
