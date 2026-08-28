using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Localization;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The free way back onto a board somebody has been stopped from playing: the button, the
    /// video, the celebration that hands the hearts over, and the way onward when it closes.
    ///
    /// <para>
    /// <b>A collaborator rather than more of the panel that draws it</b>, for
    /// <see cref="DefeatRescueFlow"/>'s reason and directly beside it — that one sells hearts
    /// for gems, this one sells them for thirty seconds, and a defeat panel that owned both
    /// would be back to the six responsibilities the rescue was lifted out of. The split is the
    /// same: this owns what is being offered and what happens when it pays, and the panel owns
    /// where the button goes and what "onward" means, because only a panel knows that.
    /// </para>
    /// <para>
    /// <b>There is no explanatory panel in front of the video any more, and that is the point of
    /// this class.</b> Both entry points used to raise <see cref="AdOfferOverlay"/> — a panel
    /// listing how hearts regenerate, when the next one lands and how many videos the day has
    /// left — and then pay out by turning its watch button into a COLLECT. Two things were wrong
    /// with that and they compound. The panel answers a question nobody asked here: a player
    /// stopped mid-session has already decided, and the button they just tapped said what it
    /// did. And the payoff was drawn as the smallest change on the screen — a caption swap on
    /// the control that had asked for the ad — which is the fault <see cref="PrizeOverlay"/> was
    /// built to fix for the bonus wheel and is worse here, because this is the moment the game
    /// is handed back. So the tap shows the video, and what returns is the celebration.
    /// </para>
    /// <para>
    /// <b><see cref="AdOfferOverlay"/> is not retired by this and must not be.</b> It is still
    /// what the <c>+</c> beside the heart pill opens, and there the panel <em>is</em> the
    /// answer — that control means "tell me about this resource", and the house rule is that it
    /// always opens that resource's panel whatever state the offer is in. The distinction is
    /// whether the player asked a question or asked to play.
    /// </para>
    /// <para>
    /// It holds a <c>View</c>, which is a <c>MonoBehaviour</c>, so <c>if (_host)</c> is Unity's
    /// own lifetime check — the same bargain <see cref="RunContinueFlow"/> makes with its screen.
    /// </para>
    /// </summary>
    public sealed class HeartVideoFlow
    {
        /// <summary>The panel this belongs to. Used for its lifetime and for somewhere to speak.</summary>
        readonly View _host;

        /// <summary>
        /// Where the player goes once the prize has been taken.
        ///
        /// <para>
        /// Called through <see cref="PrizeOverlay.Collected"/>, so it runs however the
        /// celebration ended rather than only when COLLECT was pressed. That is not tidiness:
        /// the hearts are banked the instant the video finishes, so the panel underneath is
        /// already stale — a defeat screen reading "you are out of hearts" over a wallet holding
        /// two — and a player who dismisses with the back key has to be led out of it just as a
        /// player who collects is.
        /// </para>
        /// <para>
        /// It is the caller's own lambda and must guard its own lifetime: this can run when the
        /// panel that supplied it is long gone, because a player who backgrounds the app during
        /// a video may come back somewhere else entirely.
        /// </para>
        /// </summary>
        readonly Action _onward;

        Btn _button;

        /// <summary>
        /// True from the tap until either the celebration is raised or a refusal is spoken.
        ///
        /// It is what stops <see cref="Paint"/> — which runs every frame off the host's
        /// <c>Update</c> — from writing WATCH FOR HEARTS back over the opening caption and
        /// re-enabling a button whose video is already on its way up.
        /// </summary>
        bool _watching;

        /// <param name="host">The panel drawing the button. Its lifetime governs this.</param>
        /// <param name="onward">
        /// What to do once the prize has been taken, guarded by the caller for its own lifetime.
        /// </param>
        public HeartVideoFlow(View host, Action onward)
        {
            _host = host;
            _onward = onward;
        }

        // ------------------------------------------------------------------ the button
        /// <summary>
        /// Draws the offer, with the caption the placement's own state asks for.
        ///
        /// <para>
        /// Through <see cref="AdOfferButton"/> rather than a plain caption, because a rewarded
        /// ad has five ways of not happening and the button is now the only thing that says so —
        /// there is no panel behind it carrying the sentence any more. A cooldown reads as a
        /// countdown, a spent allowance says so, and an unloaded video greys itself out rather
        /// than opening onto a video that is not there.
        /// </para>
        /// <para>
        /// Pinned to one line, which the defeat panel's copy of this button never was. The
        /// captions that replace the resting one are phrases rather than words, and
        /// <c>UIKit.TextButton</c> switches Unity's best-fit on for any button carrying a glyph —
        /// best-fit concedes the <em>line</em> before it concedes the size, so a caption that
        /// long folds in half instead of shrinking. See <c>UIKit.OneLine</c>.
        /// </para>
        /// <para>
        /// <b>Callable again, and it has to be.</b> A panel that rebuilds throws its buttons
        /// away, and the defeat panel rebuilds whenever the gem balance changes what the
        /// <em>rescue</em> beside this is offering — which a cloud sync can do at any moment,
        /// including while a video is up. So the flow outlives the button it drew, and a
        /// redrawn button is put back into whatever state this is already in rather than into
        /// its resting one. Without that, a rebuild mid-video would hand back an armed WATCH
        /// button and a second tap would start a second video over the first.
        /// </para>
        /// </summary>
        public void Draw(Transform parent, Vector2 size, Vector2 anchor, Vector2 pos)
        {
            _button = UIKit.TextButton("WatchAd", parent, "btn_green",
                                       Loc.Get("ui.ads.hearts_cta"), 44,
                                       size, anchor, pos, Watch, "ic_play");

            UIKit.OneLine(_button, 24);

            if (_watching)
            {
                _button.Interactable = false;
                _button.SetCaption(Loc.Get("ui.ads.opening"));
                return;
            }

            Paint();
        }

        /// <summary>
        /// Keeps the caption honest while the panel is open.
        ///
        /// <para>
        /// A defeat panel is somewhere players sit for a while, deciding, and a cooldown that
        /// only updated when the screen was reopened would tick down invisibly with the button
        /// still saying WATCH. Cheap enough to run every frame: <c>Btn.SetCaption</c> only
        /// touches the text mesh on the frames the string actually changed.
        /// </para>
        /// </summary>
        public void Paint()
        {
            if (_watching) return;

            AdOfferButton.Paint(_button, AdPlacement.HeartRefill, "ui.ads.hearts_cta");
        }

        // ------------------------------------------------------------------ watching
        /// <summary>
        /// Straight to the video. Nothing stands between the tap and the ad.
        ///
        /// <para>
        /// The offer is re-read at the instant of the tap rather than trusted from the last
        /// paint, which is reachable rather than defensive: the allowance and the cooldown both
        /// move on their own, and a frame between the paint and the finger landing is all it
        /// takes. A refusal here repaints instead of opening nothing, so the button explains
        /// itself.
        /// </para>
        /// </summary>
        void Watch()
        {
            if (_watching || !_host) return;

            if (!RewardedAds.CanOffer(AdPlacement.HeartRefill)) { Paint(); return; }

            _watching = true;

            if (_button)
            {
                _button.Interactable = false;
                _button.SetCaption(Loc.Get("ui.ads.opening"));
            }

            Show();
        }

        /// <summary>
        /// Shows it, and turns what came back into either a celebration or a sentence.
        ///
        /// <para>
        /// The show itself is <see cref="RewardedVideo.Watch"/> — one copy of the five steps and
        /// the two orderings inside them that matter. What is here is the half only this flow
        /// can answer, and the asymmetry in it is deliberate: <b>a prize is raised before the
        /// host is checked for life, and a refusal after it.</b> The hearts are banked by the
        /// redeem whether or not anybody is still looking at this panel, so the celebration is
        /// the only thing that will ever tell the player they have them; a refusal, by contrast,
        /// is news about a button that no longer exists.
        /// </para>
        /// </summary>
        async void Show()
        {
            var payment = await RewardedVideo.Watch(AdPlacement.HeartRefill);

            if (payment.Paid) { Celebrate(payment); return; }

            if (!_host) return;

            _watching = false;

            // Spoken over the panel rather than written into it. The panel derives its height
            // from the rows it is drawing (see DefeatPanel), so a sentence that only exists on
            // the refusals would either be a fourth shape to hold under PanelStack.TallestPanel
            // or a line drawn through the button under it. A toast owes the layout nothing and
            // says the same thing.
            Scenery.Toast(_host.Content, RewardedVideo.Refusal(payment), Pal.Rose, 2.6f,
                          new Vector2(.5f, 1f), -250f);

            Audio.SfxVaried("back", .5f);
            Paint();
        }

        /// <summary>
        /// Hands the hearts over, and the game back.
        ///
        /// <para>
        /// Loud on purpose, which <see cref="PrizeOverlay.Loud"/> is otherwise sparing with: a
        /// player meets this at the one moment in a session when they have been told they cannot
        /// play, so it is exactly the moment worth marking. The colour is the resource's own
        /// rather than a chosen one, so the hearts arrive in the colour every other heart in the
        /// game is drawn in.
        /// </para>
        /// <para>
        /// Raised through <c>Flow.Modal</c>, which hands back a celebration that is already
        /// standing rather than building a second one — so even an impossible double redeem is
        /// one panel with one way onward.
        /// </para>
        /// </summary>
        void Celebrate(VideoPayment payment)
        {
            Flow.Modal<PrizeOverlay>(v =>
            {
                v.Drop = payment.Drop;
                v.TitleKey = "ui.ads.hearts_prize";
                v.Tint = RewardArt.Tint(payment.Drop.Kind);
                v.Loud = true;
                v.Flight = payment.Flight;
                v.Collected = _onward;
            });
        }
    }
}
