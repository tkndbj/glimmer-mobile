using System;
using GlimmerGrove.Ads;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    // ==================================================================== victory
    /// <summary>
    /// The payoff, and the only panel a finished glade shows.
    ///
    /// <para>
    /// <b>One panel, because two was a tax.</b> The route measurement used to live in its own
    /// overlay, slipped in front of the Next button — so the control labelled "next glade"
    /// answered a different question the first time it was tapped. That is the same mistake
    /// the hub's <c>+</c> buttons made before <c>AdOfferOverlay</c> became the one destination
    /// for a resource, and it fails the same way: a player who wants the next glade taps once
    /// for a panel they did not ask for and again for the thing they did. The measurement is
    /// now a section of this panel, the button goes where it says, and nothing was cut.
    /// </para>
    /// <para>
    /// <b>The timing is the design.</b> Information arriving all at once is information; the
    /// same information arriving in an order, each piece a beat after the last, is a reward,
    /// because at every moment there is still something about to happen. That is why the
    /// sequence is declared as a <see cref="Cue"/> rather than as a scatter of absolute
    /// delays: the beats are relative, so inserting one cannot silently shove two others onto
    /// the same frame. They had been — the rank and the reward were computed from two
    /// different formulae over the star count and collided on a three-star win.
    /// </para>
    /// <para>
    /// <b>Everything is built hidden and then revealed</b>, never built by the beats. That is
    /// what lets the buttons be live from the first frame: a player who has seen this three
    /// hundred times can tap through it, and tapping destroys the view, which kills every
    /// pending beat with it (see <see cref="Cue"/>). A panel assembled *by* its own
    /// choreography cannot offer that.
    /// </para>
    /// <para>
    /// <b>It never runs off the screen.</b> The canvas is width-matched at 1080, so its height
    /// is whatever the device's aspect makes it — 1920 on a 16:9 phone, 2400 on a tall one and
    /// 1440 on a 4:3 tablet. A panel whose height depends on how much the run earned cannot be
    /// laid out against a fixed screen, so the whole block is measured and then fitted; see
    /// <see cref="Fit"/>.
    /// </para>
    /// </summary>
    public sealed class WinOverlay : ModalView
    {
        /// <summary>The run that was won, decided by the screen. See <see cref="RunOutcome"/>.</summary>
        public RunOutcome Run { get; set; }

        /// <summary>What the streak did. See <c>StreakToast</c>.</summary>
        public StreakNote Streak { get; set; }

        /// <summary>What this run added, already folded into the ledger. Zero on a worse replay.</summary>
        public long XpGained, CreditsGained;

        /// <summary>
        /// This glade's golden multiplier, as a percentage. 100 is the ordinary reward.
        /// Already inside <see cref="CreditsGained"/> — see <c>GoldenTable</c>.
        /// </summary>
        public int GoldenPercent = 100;

        /// <summary>
        /// Written out rather than assembled as "ui.win.rank" + stars, so the keys are
        /// visible to the build's string checker. A key that only exists at runtime is
        /// a key nothing can verify.
        /// </summary>
        internal static readonly string[] RankKeys = { "ui.win.rank1", "ui.win.rank2", "ui.win.rank3" };

        /// <summary>
        /// The run line and the grove's turn count, written out for the reason
        /// <see cref="RankKeys"/> is.
        ///
        /// <para>
        /// These are the four keys the <em>map</em> quotes a record with
        /// (<c>LevelsScreen.RecordKey</c>), reused deliberately: the node and this panel then
        /// state a run in exactly one format, so a player never sees the same pair of numbers
        /// written two ways. Four of them because "1 turns" is wrong in English and worse in
        /// languages with real plural rules, and because a run that resolved before the clock
        /// could read anything has a move count and no time — a dash where the time goes reads
        /// as a broken record rather than an untimed one.
        /// </para>
        /// </summary>
        static string TurnsKey(int moves, int millis)
            => millis > 0
                ? (moves == 1 ? "ui.rank.record_one" : "ui.rank.record")
                : (moves == 1 ? "ui.rank.untimed_one" : "ui.rank.untimed");

        /// <summary>Seconds between one star landing and the next.</summary>
        const float StarGap = .42f;

        /// <summary>How much of the first payout chip the second waits for.</summary>
        const float PayoutOverlap = .45f;

        // ------------------------------------------------------------- geometry
        /// <summary>
        /// The panel, and how far the crown reaches above its top edge.
        ///
        /// <para>
        /// The crest deliberately breaks the frame — a crown over a banner sitting <em>on</em>
        /// the panel rather than inside it is what stops a tall rectangle reading as a
        /// rectangle. <see cref="CrestReach"/> is what the fit has to allow for, so it is a
        /// constant rather than something measured: the art is fixed and a measured version
        /// would only be a slower way of writing 202 down. It has to move whenever
        /// <see cref="CrownY"/> does, or the fit stops reserving the room the crown needs.
        /// </para>
        /// </summary>
        const float PanelWidth = 900f, CrestReach = 202f;

        /// <summary>
        /// The frame's own tint.
        ///
        /// The window art is a mid teal-green; driven down to about 60% it becomes the deep
        /// forest the gold and the cream sing against. Left at white the panel is brighter
        /// than the stars on it, which is the wrong way round for the loudest screen in the
        /// game.
        /// </summary>
        static readonly Color PanelInk = new Color(.588f, .722f, .690f, 1f);

        /// <summary>
        /// Where the crest's two pieces sit, measured from the panel's top edge.
        ///
        /// The crown is lifted until its base rests on the banner's top edge rather than
        /// sinking into it — at 88 it sat inside the ribbon and read as one lumpy shape.
        /// Raising it costs <see cref="CrestReach"/> the same 26px; the two move together.
        /// </summary>
        const float CrownY = 114f, BannerY = -30f;

        /// <summary>How large the banner is drawn, at the art's own aspect.</summary>
        static readonly Vector2 BannerSize = new Vector2(566f, 157f);

        /// <summary>
        /// Where the rank word sits on the banner, and how much of it it may use.
        ///
        /// <para>
        /// <b>The banner's flat face is not the banner's centre</b>, and centring the word on
        /// the sprite is what had it hanging off the bottom edge onto the draped tails. Measured
        /// from the art: in the 361&#215;100 source the face runs from y&#8239;2 to y&#8239;54, so
        /// its middle is 22px above the sprite's middle, and the sprite is drawn at
        /// 566/361&#8239;=&#8239;1.568&#215;. That is where 34 comes from.
        /// </para>
        /// <para>
        /// The width is measured the same way. At the face's own middle the red runs 231 source
        /// pixels wide — 362 drawn — so a box of 430 let a long translation run out over the
        /// folds. <see cref="UIKit.Shrinkable"/> then keeps it inside 356 rather than letting it
        /// spill, which for a word this short only ever affects a translation.
        /// </para>
        /// </summary>
        const float RankLift = 34f;
        static readonly Vector2 RankBox = new Vector2(356f, 74f);

        /// <summary>
        /// The star row: where its centre sits, how big a star is, and how far apart they are.
        /// <see cref="StarsBottom"/> is where the rows below start — the row's own extent plus
        /// the air a 176px star needs to not touch a caption.
        /// </summary>
        const float StarsY = 268f, StarSize = 176f, StarSpacing = 186f, StarsBottom = 402f;

        /// <summary>
        /// What each optional row adds to the panel's height.
        ///
        /// <para>
        /// Rows are stacked with a cursor rather than placed at absolute offsets, which is the
        /// whole reason a conditional section can be inserted without touching anything below
        /// it. The old panel added a fixed <c>PaidRoom</c> to a fixed <c>PanelBase</c> and then
        /// positioned the payout from a separately-derived <c>rewardY</c>; the two agreed only
        /// as long as nobody edited one of them.
        /// </para>
        /// </summary>
        const float HintRow = 54f, LaneHeight = 96f, VerdictRow = 66f,
                    StandRow = 112f, PayoutRow = 148f, GoldenRow = 74f, BonusRow = 152f;

        /// <summary>Air under the last row, and the block the buttons own at the bottom.</summary>
        const float Tail = 40f, ButtonBlock = 208f;

        /// <summary>The comparison bars: how long the track is and how thick.</summary>
        const float TrackWidth = 720f, BarHeight = 42f;

        /// <summary>Inset of a fill from its track, so the track always shows as a rim.</summary>
        const float BarInset = 4f;

        /// <summary>The wooden marker that carries the grove's own number.</summary>
        static readonly Vector2 MarkSize = new Vector2(70f, 87f);

        /// <summary>
        /// The "i" beside the grove's caption: the drawn disc, the tap target around it, and the
        /// gap between it and the caption it explains.
        ///
        /// The two sizes differ on purpose. 54px is what the mark should look like next to a
        /// 27pt caption; 54px is also about 3.5mm on a 1080-wide phone, which is half of what a
        /// thumb needs. The button is the larger box and the disc is drawn inside it.
        /// </summary>
        const float InfoDotSize = 54f, InfoTapSize = 96f, InfoGap = 16f;

        /// <summary>The bubble: how wide, its inner padding, and how far it hangs below the dot.</summary>
        const float BubbleWidth = 720f, BubblePad = 30f, BubbleDrop = 24f, BubbleTail = 30f;

        /// <summary>
        /// Cream paper and the warm brown the rest of the game's explanatory copy is set in
        /// (<c>EventInfoOverlay</c>, <c>StreakInfoOverlay</c>). A bubble is the only white thing
        /// on this panel, so it has to be the same white those panels use or it reads as a
        /// system dialog that wandered in.
        /// </summary>
        static readonly Color BubbleInk = new Color(.40f, .30f, .22f);

        // ------------------------------------------------------------- state
        RectTransform _fit;
        StarRow _stars;
        Image _youFill, _youOver, _groveFill;
        RectTransform _mark;
        float _youWidth, _overWidth, _groveWidth;

        Btn _infoDot;
        Image _infoVeil;
        RectTransform _bubble;

        /// <summary>
        /// True when this run completed the last uncleared glade of its chapter. Derived
        /// from the catalog rather than counted, so it stays correct when a chapter gains
        /// levels in a content drop.
        /// </summary>
        bool FinishedAChapter(CatalogIndex index)
        {
            var chapter = index.ChapterOf(Run.Level);
            if (!chapter.IsValid) return false;

            foreach (var sibling in index.LevelsOf(chapter))
                if (!PlayerProgress.IsCleared(sibling)) return false;

            return true;
        }

        protected override void Build()
        {
            // "Last" means the catalog has nothing after this level, not a fixed count,
            // so publishing a new chapter turns the end screen back into a next button
            // without any code here changing.
            var index = GameContent.Index;
            var next = index.Next(Run.Level);
            bool last = !next.IsValid;

            int stars = Mathf.Clamp(Run.Stars, 1, 3);

            // Every one of these decides the panel's height, so all of them are answered
            // before a single thing is built. Asking later and growing the panel afterwards is
            // how a line ends up drawn a few pixels outside the frame it belongs to — which
            // looks fine on the one device it was tuned on.
            bool paid = XpGained > 0 || CreditsGained > 0;
            bool goldened = paid && GoldenPercent > 100;
            bool hint = stars < 3 && Run.Target > 0;

            // The player's own record after this run, against everybody else's.
            int record = Run.NewBest ? Run.Moves
                       : Run.PreviousBest > 0 ? Mathf.Min(Run.Moves, Run.PreviousBest)
                       : Run.Moves;
            var population = GroveStats.For(Run.Level);
            var band = RankTier.Of(population.PercentSlower(record));
            bool ranked = band != RankBand.None;

            // The comparison is drawn on every win that has a route, because merging the panels
            // made it free — it costs no tap and no navigation. Only the *sentence* stays
            // upward-only: a bar that happens to be longer than the grove's is a neutral fact
            // the player can act on, while a line reading "twenty turns from a perfect route"
            // after every win is a scolding. The rule lives in Domain rather than here so
            // RouteTests can pin it — see RunOutcome.RouteWorthSaying, which had to give up its
            // personal-best clause when this panel absorbed the other one.
            bool route = Run.HasRoute;
            bool praise = Run.RouteWorthSaying;

            // ---------------------------------------------------------- the stack
            float y = StarsBottom;
            float hintY = 0f, youCapY, youBarY = 0f, groveCapY = 0f, groveBarY = 0f;
            float verdictY = 0f, standY = 0f, payY = 0f, goldY = 0f;

            if (hint) { hintY = y + 24f; y += HintRow; }

            youCapY = y + 22f;
            if (route)
            {
                youBarY = y + 73f;
                groveCapY = y + 134f;
                groveBarY = y + 185f;
                y += LaneHeight * 2f + 22f;
                if (praise) { verdictY = y + 30f; y += VerdictRow; }
            }
            else y += 60f;

            if (ranked) { standY = y + 52f; y += StandRow; }
            if (paid) { payY = y + 70f; y += PayoutRow; }
            if (goldened) { goldY = y + 34f; y += GoldenRow; }

            // Offered only on a run that actually paid, which is the honest reading of "on top
            // of what this glade earned" — a replay that beat nothing earns nothing, and a
            // bonus stacked on zero is a coin offer wearing a victory panel's clothes. That is
            // the same condition the payout chips are built under, deliberately: the offer sits
            // directly beneath the number it is doubling and disappears with it.
            //
            // ShouldOffer, not CanOffer, matching every other surface: a cooldown draws the
            // button with its own countdown on it rather than vanishing. The refusals that
            // cannot resolve by waiting — no provider, no account to pay coins into — hide the
            // row entirely, so a signed-out first launch never sees it.
            bool bonus = paid && RewardedAds.ShouldOffer(AdPlacement.WinBonus);

            float bonusY = 0f;
            if (bonus) { bonusY = y + BonusRow * .5f; y += BonusRow; }

            float panelH = y + Tail + ButtonBlock;

            UIKit.Scrim(Content, .66f);

            _fit = Fit(panelH);

            // ------------------------------------------------------ the light show
            // On the fit rather than on Content, so a panel scaled down on a short screen
            // takes its own halo with it instead of sitting in a fan sized for a taller one.
            // Built before the panel, which is what puts it behind.
            float crestY = panelH * .5f - CrestReach * .5f;

            var fan = UIKit.Img("Rays", _fit, Art.Rays(256, 14), new Color(1f, .80f, .30f, .20f),
                                Vector2.one * 1680f, new Vector2(.5f, .5f), new Vector2(0f, crestY - 240f));
            Tween.Run(46f, Ease.Linear,
                      t => { if (fan) fan.transform.localRotation = Quaternion.Euler(0f, 0f, t * 360f); },
                      fan.gameObject, "spin").Loop(-1, false);

            UIKit.Img("Bloom", _fit, Art.Glow(128, 2.4f), new Color(1f, .82f, .38f, .22f),
                      new Vector2(1240f, 1000f), new Vector2(.5f, .5f), new Vector2(0f, crestY - 200f));

            // ---------------------------------------------------------- the frame
            // Nine-sliced, which is new and was a real defect rather than a refinement: the
            // window sprite had no border, so an 880x1330 panel stretched a 720x642 image to
            // twice its aspect and smeared its corners and its inner hairline. It also carried
            // a header tab nothing ever drew. Both are fixed in the art.
            Backing = UIKit.Img("Panel", _fit, Art.S("Ui/Win/window"), PanelInk,
                                new Vector2(PanelWidth, panelH), new Vector2(.5f, .5f),
                                new Vector2(0f, -CrestReach * .5f));
            Panel = (RectTransform)Backing.transform;
            Panel.localScale = Vector3.zero;

            // Light pooling under the crest, inside the frame. A soft gradient rather than a
            // plate, for the reason the feature beacon's seat is one: it reads as light around
            // an award and can never be mistaken for a mislaid rectangle.
            UIKit.Img("Pool", Panel, Art.Glow(128, 1.9f), new Color(1f, .96f, .82f, .11f),
                      new Vector2(PanelWidth - 60f, 700f), new Vector2(.5f, 1f), new Vector2(0f, -70f));

            // ---------------------------------------------------------- the crest
            var crown = BuildCrest(stars, out var banner, out var rank);

            // ---------------------------------------------------------- the stars
            // A seat of shadow under the row, then gold light over it. The dark half is not
            // decoration: star_empty is a brown star, and on the frame's own green an unearned
            // star reads as a hole rather than as a slot waiting to be filled.
            UIKit.Img("Seat", Panel, Art.Glow(128, 1.7f), new Color(.016f, .047f, .063f, .47f),
                      new Vector2(940f, 470f), new Vector2(.5f, 1f), new Vector2(0f, -StarsY));
            UIKit.Img("Shine", Panel, Art.Glow(128, 2.1f), new Color(1f, .78f, .26f, .22f),
                      new Vector2(880f, 430f), new Vector2(.5f, 1f), new Vector2(0f, -StarsY));

            _stars = StarRow.Create(Panel, new Vector2(.5f, 1f), new Vector2(0f, -StarsY),
                                    StarSize, StarSpacing, 0, true);

            // The record, as a wax seal over the row's right shoulder. A stamp rather than the
            // sentence it replaces ("a new best for this glade"), because beating your own
            // record is an award and a line of prose under a row of stars is a footnote.
            var seal = Run.NewBest ? BuildSeal() : null;

            // ---------------------------------------------------------- the readout
            // Both halves of the star rule when the glade is timed, because stars are the
            // worse of the two (LevelTuning.StarsFor) and naming only the turns would tell a
            // player who made the turn count that they had done everything asked.
            string hintText = Run.HasTimeLimit
                ? Loc.Format("ui.win.three_stars_timed", Run.Target,
                             RunClock.Format(Mathf.CeilToInt(Run.TimeLimit * LevelTuning.TimeGoldFraction)))
                : Loc.Format("ui.win.three_stars", Run.Target);

            Text hintLine = hint ? Row("Hint", -hintY, hintText,
                                       30, new Color(1f, .95f, .86f, .68f), 640f, 22) : null;

            string yours = Run.Millis > 0
                ? Loc.Format(TurnsKey(Run.Moves, Run.Millis), Run.Moves, RunClock.Format(Run.Millis))
                : Loc.Format(TurnsKey(Run.Moves, 0), Run.Moves);

            var youCap = Caption("YouCap", -youCapY, Loc.Get("ui.win.route_you"));
            var youVal = Value("YouVal", -youCapY, yours, Pal.Cream);

            Text groveCap = null, verdict = null;
            if (route)
            {
                // One scale for both bars, which is the entire readability of the comparison:
                // the shorter bar is the better run and no number is needed to see it. The
                // longer of the two fills the track, so a player who came in under the route
                // sees their own bar stop short of a full one.
                float span = Mathf.Max(Run.Moves, Run.Route);
                float inner = TrackWidth - BarInset * 2f;

                _youWidth = inner * Mathf.Min(Run.Moves, Run.Route) / span;
                _overWidth = Run.TurnsOverRoute > 0 ? inner * Run.Moves / span : 0f;
                _groveWidth = inner * Run.Route / span;

                // The overrun is drawn first and therefore behind, so the amber only shows
                // past the gold's end. Two lengths of one bar rather than a second bar butted
                // against the first: a gold run that visibly continues in a warmer colour says
                // "these turns were spare" without the panel having to say it in words.
                var youTrack = Rail("You", -youBarY);
                _youOver = _overWidth > 0f ? Fill(youTrack, "Over", Pal.Amber) : null;
                _youFill = Fill(youTrack, "Fill", Pal.Gold);

                var groveTrack = Rail("Grove", -groveBarY);
                _groveFill = Fill(groveTrack, "Fill", new Color(1f, .96f, .81f, .52f));

                groveCap = Caption("GroveCap", -groveCapY, Loc.Get("ui.win.route_grove"));
                _infoDot = InfoDot(-groveCapY, groveCap);

                // The grove's number rides a carved marker at the end of its own bar rather
                // than sitting in the value column opposite its caption. Two reasons: the
                // number then means "the mark is here", which is what the bar is for, and the
                // column would otherwise print a turn count directly under the player's,
                // inviting them to read the pair as one line.
                _mark = BuildMark(groveTrack);

                if (praise)
                {
                    verdict = Row("Verdict", -verdictY, VerdictLine(), 34, VerdictInk(), 780f, 22);
                    verdict.transform.localScale = Vector3.zero;
                }
            }

            var stand = ranked ? BuildStanding(-standY, band, population.PercentSlower(record)) : null;

            // ---------------------------------------------------------- the payout
            // Two chips rather than one sentence, each number put there by things the player
            // watches leave the stars — see Payout for why the flight is what makes it land.
            // Built only when the run improved the record: a replay that beat nothing earns
            // nothing, and a chip reading "+0 XP" looks like a bug.
            Payout xpChip = null, coinChip = null;
            if (paid)
            {
                float spread = XpGained > 0 && CreditsGained > 0 ? 190f : 0f;

                if (XpGained > 0)
                {
                    // A mint gem, because that is what experience already is everywhere else
                    // here. Art.Gem carries its own colours, so like every other reward glyph
                    // it is drawn white rather than tinted, and being generated it cannot
                    // arrive a frame late as a white rectangle (invariant 7b).
                    xpChip = Payout.Chip("Xp", Panel, new Vector2(.5f, 1f), new Vector2(-spread, -payY),
                                         Art.Gem(128, Pal.Mint), Pal.Mint,
                                         n => Loc.Format("ui.win.xp", n), XpGained,
                                         Art.Gem(64, Pal.Mint), Color.white, "tick");
                }

                if (CreditsGained > 0)
                {
                    // Credits are the spinning coin, which has no single sprite — the glyph is
                    // finished by RewardArt, fallback and all. The tokens use the coin's first
                    // frame flat and untinted: gold art washed in gold stops reading as a coin.
                    var frames = Art.Frames("Ui/Coin");
                    bool minted = frames != null && frames.Length > 0;

                    coinChip = Payout.Chip("Coins", Panel, new Vector2(.5f, 1f), new Vector2(spread, -payY),
                                           null, Pal.Gold, n => Loc.Format("ui.win.coins", n), CreditsGained,
                                           minted ? frames[0] : Art.Disc(128),
                                           minted ? Color.white : Pal.Gold, "pop");
                    RewardArt.Glyph(coinChip.Glyph, ChestDropKind.Credits, 14f);
                }

                if (xpChip != null) xpChip.Root.localScale = Vector3.zero;
                if (coinChip != null) coinChip.Root.localScale = Vector3.zero;
            }

            // A glade that paid more than it should have, said out loud. Built here with the
            // rest of the furniture because its beat belongs to the payout's lane.
            var goldenLine = goldened
                ? Row("Golden", -goldY, Loc.Format("ui.win.golden", GoldenPercent), 40, Pal.Gold, 780f, 26)
                : null;
            if (goldenLine) goldenLine.transform.localScale = Vector3.zero;

            // ---------------------------------------------------------- the bonus
            if (bonus) BuildBonus(bonusY);

            // ---------------------------------------------------------- the exits
            var nextButton = BuildButtons(index, last, next);

            // ------------------------------------------------------- the sequence
            // Read this top to bottom: it is the order the player sees, and every number is a
            // gap after the line above rather than a time from the start.
            var cue = new Cue(this);

            // The crown arrives silently. The fanfare is not missing — BoardView.Celebrate plays
            // "win" as the board solves, and this panel opens about a second later, so sounding
            // it again here was the same cue twice with a gap in it. Everything from the banner
            // down has its own beat.
            cue.With(() => { if (crown) Tween.Pop(crown.transform, 0f, .5f); });

            // The panel arrives under a crown that is still settling, which is what makes the
            // two read as one movement rather than two.
            cue.Then(.42f, () => Tween.Scale(Panel, 1f, .55f, Ease.OutBack));

            cue.Then(.20f, () =>
            {
                if (banner) Tween.Pop(banner.transform, 0f, .5f);
                Audio.Sfx("whoosh", .34f, 1.15f);
            });

            // Stars land one at a time, each a semitone above the last. The row schedules its
            // own beats, so the playhead is walked over them by hand.
            cue.Then(.30f, () => _stars.Reveal(stars, 0f, StarGap));

            // ------------------------------------------------------ the payout lane
            // A second lane, opened at the same instant as the stars rather than queued behind
            // them. It used to run after the rank and the record, which put three or four
            // seconds between a player landing their last star and seeing what it was worth —
            // long enough that the reward read as a separate announcement instead of as the
            // consequence of the thing they had just watched. The two belong together: the
            // reward is derived from exactly those stars (see ProgressionLedger), and the
            // tokens are thrown out of the star row to say so.
            //
            // A lane rather than more beats on the main one, because the sequences overlap and
            // a single playhead cannot express that — walking it forward over the payout would
            // push the rank behind it, which is the problem in reverse.
            float payoutEnds = cue.Playhead;
            if (paid)
            {
                var payout = new Cue(this, cue.Playhead);
                float lastStart = payout.Playhead, lastRuns = 0f;

                payout.With(() =>
                {
                    if (xpChip != null) Tween.Pop(xpChip.Root, .4f, .44f);
                    if (coinChip != null) Tween.Pop(coinChip.Root, .4f, .44f, .09f);
                    Audio.Sfx("whoosh", .32f, 1.2f);
                });

                if (xpChip != null)
                {
                    // Timed so the first sparks leave as the first star lands, not before it:
                    // tokens thrown out of an empty row would be coming from nothing.
                    payout.Then(StarGap * .8f, () => xpChip.Play(_stars.transform));
                    lastStart = payout.Playhead; lastRuns = xpChip.Duration;

                    // Held for a fraction of the first chip rather than all of it: the coins
                    // leave while the sparks are still landing, so the two read as one
                    // cascade. Played end to end they read as a list being recited.
                    if (coinChip != null) payout.Wait(xpChip.Duration * PayoutOverlap);
                }

                // Lands before the coins so the number the player then watches climb is
                // already explained — "something special happened", then the evidence. Only
                // ever on a paid run: announcing a multiplier on top of no credits would read
                // as the game owing the player money.
                if (goldenLine)
                {
                    payout.Then(xpChip != null ? 0f : StarGap * .8f, () =>
                    {
                        if (!goldenLine) return;
                        Tween.Pop(goldenLine.transform, 0f, .55f);
                        Tween.Breathe(goldenLine.transform, .035f, 1.8f);
                        Audio.Sfx("chime2", .7f, 1.18f);
                        Burst.Sparks(goldenLine.transform, Vector2.zero, Pal.Gold, 18, 300f, 24f, .7f);
                        Flow.Flash(new Color(1f, .93f, .70f), .3f, .5f);
                    });
                    payout.Wait(.28f);
                }

                if (coinChip != null)
                {
                    payout.Then(0f, () => coinChip.Play(_stars.transform));
                    lastStart = payout.Playhead; lastRuns = coinChip.Duration;
                }

                payoutEnds = lastStart + lastRuns;
            }

            // -------------------------------------------------------- the main lane
            cue.Wait(StarGap * stars);

            // The last star is the loudest moment on the panel, and only a full row earns the
            // wash. Spending it on every win spends it on most of them and marks out none —
            // the same argument that keeps the map's rays for the top tier alone.
            //
            // Light only: no confetti and no haptic anywhere in this sequence. Both were tried
            // and both are gone by request. Worth knowing why they were easy to lose — the
            // board has already thrown confetti and buzzed once when it solved (see
            // BoardView.Celebrate), so the panel was restating a celebration the player had
            // just had rather than adding one.
            if (stars >= 3)
                cue.Then(.06f, () => Flow.Flash(new Color(1f, .96f, .84f), .34f, .55f));

            cue.Then(.28f, () =>
            {
                if (!rank) return;
                Tween.Pop(rank.transform, 0f, .6f);
                Audio.Sfx("chime", .55f, 1f + .08f * stars);
            });

            if (seal)
            {
                // Stamped rather than popped: down hard from oversize, which is the one
                // gesture that reads as a seal being pressed into wax.
                cue.Then(.22f, () =>
                {
                    if (!seal) return;
                    seal.transform.localScale = Vector3.one * 2.1f;
                    Tween.Scale(seal.transform, 1f, .3f, Ease.InCubic).OnDone(() =>
                    {
                        if (!seal) return;
                        Tween.Punch(seal.transform, .18f, .34f);
                        Burst.Sparks(seal.transform, Vector2.zero, Pal.Gold, 14, 220f, 20f, .55f);
                    });
                    Audio.Sfx("pop2", .6f, .9f);
                });
            }

            // The readout follows quickly. These are facts rather than spectacle, and a player
            // who has to wait through them learns to tap past the celebration.
            if (hintLine) cue.Then(.20f, () => Reveal(hintLine));

            cue.Then(.16f, () =>
            {
                Reveal(youCap);
                Reveal(youVal);
            });

            if (route)
            {
                // The player's bar draws itself first and the grove's answers it, which is the
                // order that makes the comparison a reveal rather than a chart.
                cue.Then(.10f, () => Grow(_youFill, _youWidth, _youOver, _overWidth, "tick", 1f));
                cue.Then(.34f, () =>
                {
                    Reveal(groveCap);
                    if (_infoDot) Tween.Pop(_infoDot.transform, 0f, .4f);
                    Grow(_groveFill, _groveWidth, null, 0f, "tock", .9f);
                });

                if (_mark != null)
                {
                    cue.Then(.30f, () =>
                    {
                        if (_mark == null) return;
                        Tween.Pop(_mark, 0f, .42f);
                        Audio.Sfx("pop", .38f, .95f);
                    });
                }

                if (verdict)
                {
                    cue.Then(.16f, () =>
                    {
                        if (!verdict) return;
                        Tween.Pop(verdict.transform, 0f, .5f);
                        Audio.Sfx("chime2", .55f, Run.BeatRoute ? 1.3f : 1.1f);
                        if (Run.BeatRoute || Run.MatchedRoute)
                            Burst.Sparks(verdict.transform, Vector2.zero, VerdictInk(), 16, 280f, 22f, .7f);
                    });
                }
            }

            if (stand != null)
            {
                cue.Then(.22f, () =>
                {
                    if (stand == null) return;
                    Tween.Pop(stand, 0f, .46f);
                    Audio.Sfx("bell", .45f, 1.1f);
                });
            }

            // The two lanes rejoin here. Everything below is either news in its own right or a
            // call to leave, and none of it should arrive while coins are still in the air —
            // least of all the shine on the Next button, which exists to ask for attention
            // once there is nothing left worth watching.
            cue.Wait(Mathf.Max(0f, payoutEnds - cue.Playhead));

            // A new glade opening is the strongest single line on this panel, so it lands
            // after the run has finished being scored rather than over the top of it.
            if (Run.FirstClear && !last)
            {
                string opened = Loc.Format("ui.win.opened", Loc.Get(LevelDefinition.DefaultNameKey(next)));
                cue.Then(.30f, () =>
                {
                    Audio.Sfx("unlock", .6f);
                    Scenery.Toast(Content, opened, Pal.Gold, 2.2f, new Vector2(.5f, 0f), 190f);
                });
            }

            // The streak, when this run is what moved it. After the glade unlock, because an
            // unlock is about the game and a streak is about the player, and the player should
            // be the last thing said.
            cue.Then(.30f, () => StreakToast.Show(this, Streak, 0f));
            if (Streak.WorthSaying) cue.Wait(.45f);

            // Last of the content, and after the streak: it is an offer rather than news, and
            // nothing on this panel should be asking the player for something while it is still
            // telling them what they did.
            if (_bonus) cue.Then(.28f, () => { if (_bonus) Tween.Pop(_bonus.transform, 0f, .46f); });

            cue.Then(.35f, () =>
            {
                if (!nextButton) return;
                Sheen.Attach((RectTransform)nextButton.transform, 3.2f);
                Tween.Breathe(nextButton.transform, .025f, 2f);
            });
        }

        // ================================================================ the bonus
        Btn _bonus;

        /// <summary>
        /// Keeps the offer's caption live while the panel is open.
        ///
        /// A victory panel is somewhere players sit — reading the comparison, looking at the
        /// standing — so a cooldown that only updated on reopen would tick down invisibly and
        /// leave the button stale. The same reason <c>DefeatOverlay</c> has one, and the paint
        /// is a no-op on any frame the caption did not change.
        /// </summary>
        void Update() => AdOfferButton.Paint(_bonus, AdPlacement.WinBonus, "ui.ads.bonus_cta");

        /// <summary>
        /// One green button under the payout: more credits, for a video.
        ///
        /// <para>
        /// <b>A flat amount, and the caption says which.</b> The obvious framing is "double
        /// your reward", and it cannot be honoured here: earned credits are derived from the
        /// star ledger (invariant 9), so there is no accumulated figure to multiply, and
        /// doubling one run would mean storing which runs had been doubled — a forgeable
        /// per-level set that pays, which invariant 15 sends straight back to 13. What the
        /// server can actually attest to is "a view of this placement happened", so the amount
        /// is content and the button prints it. A multiplier the panel cannot honour is worse
        /// than a smaller number it can: the player checks, once.
        /// </para>
        /// <para>
        /// It does not compete with <c>Next</c>. The exits keep their own block below this, the
        /// sheen still lands on <c>Next</c> at the end of the sequence, and this row is drawn
        /// last of the content rather than first — an offer placed above the thing a player came
        /// for is the mistake the hub's <c>+</c> buttons made before <c>AdOfferOverlay</c>
        /// became the single destination for a resource.
        /// </para>
        /// </summary>
        void BuildBonus(float y)
        {
            var offer = RewardedAds.Table.Offer(AdPlacement.WinBonus);

            _bonus = UIKit.TextButton("Bonus", Panel, "btn_green",
                                      Loc.Format("ui.ads.bonus_cta_n", offer.Amount), 40,
                                      new Vector2(600f, 124f), new Vector2(.5f, 1f),
                                      new Vector2(0f, -y), OnBonus, "ic_play");

            _bonus.transform.localScale = Vector3.zero;
            AdOfferButton.Paint(_bonus, AdPlacement.WinBonus, "ui.ads.bonus_cta");
        }

        /// <summary>
        /// Opens the offer over the panel rather than closing it.
        ///
        /// The win panel is the thing the player is looking at and the reason the offer makes
        /// sense; closing it to show an advert and then dropping them on the map would be a
        /// worse version of the two-panel tax that got <c>RouteOverlay</c> deleted. The button
        /// simply repaints when the offer resolves — the credits themselves arrive on the next
        /// sync, because an ad grant is the server's to make (invariant 10d).
        /// </summary>
        void OnBonus()
        {
            Flow.Modal<AdOfferOverlay>(v => v.PlacementId = AdPlacement.WinBonus);
        }

        // ================================================================= the fit
        /// <summary>
        /// A layer between the scrim and the panel, scaled so the whole block — crest included
        /// — fits the screen it landed on.
        ///
        /// <para>
        /// <b>Why a layer rather than a scale on the panel itself.</b> <see cref="Panel"/> is
        /// what <see cref="ModalView.Close"/> scales out, and it does so to an absolute value;
        /// a panel resting at 0.94 would visibly <em>grow</em> on the way out. Everything that
        /// animates a child — <see cref="Tween.Pop"/>, <see cref="Tween.Punch"/> — writes
        /// absolute local scales too. Keeping the fit on a parent means every one of those
        /// numbers stays what it was written as.
        /// </para>
        /// <para>
        /// The panel is offset upward by half the crest inside this layer, so what is centred
        /// on the screen is the block the player sees rather than the frame's own rectangle.
        /// </para>
        /// </summary>
        RectTransform Fit(float panelH)
        {
            var host = UIKit.Node("Fit", Content);

            float reach = panelH + CrestReach;
            float room = Flow.Size.y - FitMargin * 2f;
            if (reach > room && reach > 1f) host.localScale = Vector3.one * (room / reach);

            return host;
        }

        /// <summary>Air kept between the block and the top and bottom of the screen.</summary>
        const float FitMargin = 20f;

        // =============================================================== the crest
        /// <summary>
        /// A crown over a banner carrying the rank word.
        ///
        /// <para>
        /// The banner is deliberately blank artwork with the word drawn on it as text. The pack
        /// ships a matching "VICTORY" graphic and it is the one piece not imported: a word
        /// painted into a texture cannot be translated, and invariant 6 says every
        /// player-facing string is a loc key. Blank ribbon plus a key is the same picture and
        /// ships in every language.
        /// </para>
        /// <para>
        /// Two herald's horns flanked this and were cut. They read as a fanfare in a still
        /// frame and as clutter on the device: the crest is the one thing on the panel that has
        /// to be legible in a quarter of a second, and three gold shapes at three angles is not
        /// that. The art is out of the project with them, because an addressed sprite nothing
        /// draws is still built into the bundle and preloaded at every launch.
        /// </para>
        /// </summary>
        Image BuildCrest(int stars, out Image banner, out Text rank)
        {
            var crown = UIKit.Img("Crown", Panel, Art.S("Ui/Win/crown"), Color.white,
                                  new Vector2(180f, 162f), new Vector2(.5f, 1f), new Vector2(0f, CrownY));
            crown.preserveAspect = true;
            crown.transform.localScale = Vector3.zero;

            banner = UIKit.Img("Banner", Panel, Art.S("Ui/Win/banner"), Color.white,
                               BannerSize, new Vector2(.5f, 1f), new Vector2(0f, BannerY));
            banner.preserveAspect = true;
            banner.transform.localScale = Vector3.zero;

            // Lifted onto the ribbon's flat face rather than centred on the sprite — see RankLift.
            rank = UIKit.Titled("Rank", banner.transform, Loc.Get(RankKeys[stars - 1]), 58,
                                Pal.Cream, TextAnchor.MiddleCenter, RankBox,
                                new Vector2(.5f, .5f), new Vector2(0f, RankLift), 5f, 5f);
            UIKit.Shrinkable(rank, 32);
            rank.transform.localScale = Vector3.zero;

            return crown;
        }

        /// <summary>
        /// The record, as a wax seal.
        ///
        /// The caption is one key rather than two lines written into the string table, and it
        /// is <see cref="UIKit.Shrinkable"/> — which switches wrapping on, so "NEW BEST" folds
        /// onto the seal by itself and a longer translation shrinks instead of running off the
        /// disc. Two hard-coded lines would need a translator to know the shape of the art.
        /// </summary>
        Image BuildSeal()
        {
            var pos = new Vector2(320f, -(StarsY - 116f));

            UIKit.Img("SealGlow", Panel, Art.Glow(128, 2.2f), new Color(1f, .80f, .30f, .50f),
                      Vector2.one * 280f, new Vector2(.5f, 1f), pos);

            var seal = UIKit.Img("Seal", Panel, Art.S("Ui/seal_gold"), Color.white,
                                 Vector2.one * 150f, new Vector2(.5f, 1f), pos);
            seal.preserveAspect = true;
            seal.transform.localRotation = Quaternion.Euler(0f, 0f, 11f);
            seal.transform.localScale = Vector3.zero;

            var label = UIKit.Titled("T", seal.transform, Loc.Get("ui.win.best_stamp"), 27, Pal.Cream,
                                     TextAnchor.MiddleCenter, new Vector2(112f, 68f),
                                     new Vector2(.5f, .5f), Vector2.zero, 0f, 2f);
            UIKit.Shrinkable(label, 15);

            return seal;
        }

        // ============================================================== the readout
        /// <summary>A centred line of the panel's own copy, hidden until its beat.</summary>
        Text Row(string name, float y, string text, int size, Color ink, float width, int minSize)
        {
            var t = UIKit.Titled(name, Panel, text, size, ink, TextAnchor.MiddleCenter,
                                 new Vector2(width, size + 12f), new Vector2(.5f, 1f),
                                 new Vector2(0f, y), 4f, 3f);
            UIKit.Shrinkable(t, minSize);
            t.transform.localScale = Vector3.zero;
            return t;
        }

        /// <summary>A bar's caption: small, dim, hard against the track's left edge.</summary>
        Text Caption(string name, float y, string text)
        {
            var t = UIKit.Titled(name, Panel, text, 27, new Color(1f, .95f, .86f, .62f),
                                 TextAnchor.MiddleLeft, new Vector2(TrackWidth * .5f, 40f),
                                 new Vector2(.5f, 1f), new Vector2(-TrackWidth * .25f, y), 3f, 2f);
            UIKit.Shrinkable(t, 18);
            t.transform.localScale = Vector3.zero;
            return t;
        }

        // ------------------------------------------------------------- the "i"
        /// <summary>
        /// The "i" that explains the grove's route, set immediately after the caption.
        ///
        /// <para>
        /// <b>Measured rather than placed.</b> The caption is a translated string, so the dot
        /// cannot sit at a constant x without either colliding with a long one or floating away
        /// from a short one. <see cref="Text.preferredWidth"/> is read directly: uGUI computes
        /// it from the font's cached character info on demand, so it is correct in the same
        /// frame the text was assigned — the same trick <see cref="UIKit.FitLabel"/> uses.
        /// </para>
        /// <para>
        /// It is clamped to the caption's own box, which is what stops a very long translation
        /// pushing the dot out over the track. A caption that long has already been shrunk by
        /// <see cref="UIKit.Shrinkable"/> and will render narrower than it measured, so the dot
        /// drifts right by a few pixels in that case rather than leaving the panel.
        /// </para>
        /// </summary>
        Btn InfoDot(float y, Text beside)
        {
            float room = TrackWidth * .5f - InfoTapSize * .5f - InfoGap;
            float text = Mathf.Clamp(beside.preferredWidth, 0f, room);
            float x = -TrackWidth * .5f + text + InfoGap + InfoTapSize * .5f;

            // The button's own image is the tap target and nothing else. A transparent Image
            // still raycasts, so the thumb gets 96px while the eye gets 54.
            var b = UIKit.Button("RouteInfo", Panel, Art.Disc(64), Vector2.one * InfoTapSize,
                                 new Vector2(.5f, 1f), new Vector2(x, y), ToggleRouteInfo);
            var hit = b.GetComponent<Image>();
            if (hit) hit.color = new Color(0f, 0f, 0f, 0f);

            UIKit.Img("Disc", b.transform, Art.Disc(64), new Color(1f, .96f, .86f, .15f),
                      Vector2.one * InfoDotSize, new Vector2(.5f, .5f), Vector2.zero);
            UIKit.Img("Rim", b.transform, Art.Ring(64, 5f), new Color(1f, .96f, .86f, .48f),
                      Vector2.one * InfoDotSize, new Vector2(.5f, .5f), Vector2.zero);

            var glyph = UIKit.Img("Glyph", b.transform, Art.S("Ui/ic_info"),
                                  new Color(1f, .97f, .88f, .90f),
                                  Vector2.one * (InfoDotSize * .46f), new Vector2(.5f, .5f), Vector2.zero);
            glyph.preserveAspect = true;

            b.transform.localScale = Vector3.zero;
            return b;
        }

        /// <summary>Tapping the dot opens the bubble; tapping it again, or anywhere, closes it.</summary>
        void ToggleRouteInfo()
        {
            if (_bubble) CloseRouteInfo();
            else OpenRouteInfo();
        }

        /// <summary>
        /// A cream bubble under the dot, explaining what the grove's route is.
        ///
        /// <para>
        /// <b>Why a bubble and not another modal.</b> The game's other explanations
        /// (<c>StreakInfoOverlay</c>, <c>EventInfoOverlay</c>) are full panels, and they earn
        /// it — they answer three questions each about a whole screen. This answers one
        /// question about one row, and a panel that covers the thing it is describing makes the
        /// player close it to check. The bubble hangs <em>below</em> the row so both bars stay
        /// visible while it is read.
        /// </para>
        /// <para>
        /// <b>Everything lives inside the fit.</b> The veil and the bubble are children of the
        /// same scaled node the panel is, so on a short screen they shrink with it and the tail
        /// stays under the dot. The veil is deliberately built oversized — the fit may be
        /// scaled below one, and a screen-sized catcher inside it would leave an uncovered
        /// margin where a tap did nothing at all.
        /// </para>
        /// <para>
        /// The copy has one hard constraint: it must never call the route a minimum. It is the
        /// distance to the <em>authored</em> solution and a player can beat it, which is the
        /// whole reason the verdict has three readings — see <see cref="VerdictLine"/>.
        /// </para>
        /// </summary>
        void OpenRouteInfo()
        {
            if (_fit == null || _infoDot == null) return;

            var at = InFit(_infoDot.transform);

            _infoVeil = UIKit.Img("InfoVeil", _fit, Art.Pixel, new Color(.03f, .06f, .09f, 0f),
                                  Flow.Size * 1.6f, new Vector2(.5f, .5f), Vector2.zero);
            _infoVeil.raycastTarget = true;
            _infoVeil.gameObject.AddComponent<Btn>().Setup(CloseRouteInfo, silent: true);
            Tween.Fade(_infoVeil, .38f, .16f);

            var host = UIKit.Box("RouteBubble", _fit, new Vector2(BubbleWidth, 200f),
                                 new Vector2(.5f, .5f), Vector2.zero);

            // The body is built first so its height can be measured, then the paper is slipped
            // in behind it. Sizing the paper first would mean guessing at a wrapped translation.
            var body = UIKit.Label("Body", host, Loc.Get("ui.win.route_info"), 29, BubbleInk,
                                   TextAnchor.MiddleCenter,
                                   new Vector2(BubbleWidth - BubblePad * 2f, 400f),
                                   new Vector2(.5f, .5f), Vector2.zero, wrap: true);

            float h = Mathf.Ceil(body.preferredHeight) + BubblePad * 2f;
            host.sizeDelta = new Vector2(BubbleWidth, h);
            body.rectTransform.sizeDelta = new Vector2(BubbleWidth - BubblePad * 2f, h - BubblePad * 2f);

            // Centred on the fit rather than on the dot, so no translation can push it off the
            // side; only the tail tracks the dot, clamped to stay on the paper.
            float tailX = Mathf.Clamp(at.x, -BubbleWidth * .5f + 56f, BubbleWidth * .5f - 56f);
            host.anchoredPosition = new Vector2(0f, at.y - InfoTapSize * .5f - BubbleDrop - h * .5f);

            var tail = UIKit.Img("Tail", host, Art.Pixel, Pal.Cream, Vector2.one * BubbleTail,
                                 new Vector2(.5f, 1f), new Vector2(tailX, 0f));
            tail.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            tail.transform.SetAsFirstSibling();

            var paper = UIKit.Img("Paper", host, Art.Round(26), Pal.Cream,
                                  new Vector2(BubbleWidth, h), new Vector2(.5f, .5f), Vector2.zero);
            paper.transform.SetAsFirstSibling();

            var shade = UIKit.Img("Shade", host, Art.Glow(96, 1.9f), new Color(0f, 0f, 0f, .5f),
                                  new Vector2(BubbleWidth + 110f, h + 110f), new Vector2(.5f, .5f),
                                  new Vector2(0f, -14f));
            shade.transform.SetAsFirstSibling();

            host.localScale = Vector3.zero;
            Tween.Scale(host, 1f, .28f, Ease.OutBack);
            Audio.Sfx("pop", .42f, 1.3f);

            _bubble = host;
        }

        void CloseRouteInfo()
        {
            if (_infoVeil)
            {
                var veil = _infoVeil;
                _infoVeil = null;
                Tween.Fade(veil, 0f, .14f).OnDone(() => { if (veil) Destroy(veil.gameObject); });
            }

            if (_bubble)
            {
                var bubble = _bubble;
                _bubble = null;
                Tween.Scale(bubble, 0f, .15f, Ease.InQuad)
                     .OnDone(() => { if (bubble) Destroy(bubble.gameObject); });
                Audio.SfxVaried("back", .34f);
            }
        }

        /// <summary>
        /// Where <paramref name="target"/> sits in the fit's own coordinates.
        ///
        /// Measured through world space rather than read off an anchoredPosition, because the
        /// dot is anchored to the panel's top edge and the bubble to the fit's centre — their
        /// local numbers are not in the same frame. Same reason <c>Payout.LocalIn</c> does it.
        /// </summary>
        Vector2 InFit(Transform target)
        {
            Vector3 local = _fit.InverseTransformPoint(target.position);
            Vector2 centre = _fit.rect.center;
            return new Vector2(local.x - centre.x, local.y - centre.y);
        }

        /// <summary>The value opposite a caption, right-aligned to the track's other edge.</summary>
        Text Value(string name, float y, string text, Color ink)
        {
            var t = UIKit.Titled(name, Panel, text, 36, ink, TextAnchor.MiddleRight,
                                 new Vector2(TrackWidth * .5f, 48f), new Vector2(.5f, 1f),
                                 new Vector2(TrackWidth * .25f, y), 4f, 3f);
            UIKit.Shrinkable(t, 22);
            t.transform.localScale = Vector3.zero;
            return t;
        }

        /// <summary>
        /// An empty track, sunk into the frame.
        ///
        /// Named <c>Rail</c> rather than <c>Track</c> because <see cref="View.Track"/> is the
        /// screen's music track, and a method quietly hiding it compiles with a warning nobody
        /// reads and then confuses the next reader about which one a call meant.
        /// </summary>
        Image Rail(string name, float y)
        {
            var track = UIKit.Img(name + "Track", Panel, Art.Round(16), new Color(0f, 0f, 0f, .41f),
                                  new Vector2(TrackWidth, BarHeight), new Vector2(.5f, 1f),
                                  new Vector2(0f, y));

            var edge = UIKit.Img("Edge", track.transform, Art.RoundOutline(16, 3f),
                                 new Color(1f, .96f, .86f, .12f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);
            edge.raycastTarget = false;

            return track;
        }

        /// <summary>
        /// A fill pinned to its track's left edge, so growing it extends rightwards rather
        /// than from the middle — which is what makes two bars share one origin.
        /// </summary>
        Image Fill(Image track, string name, Color ink)
        {
            var fill = UIKit.Img(name, track.transform, Art.Round(14), ink,
                                 new Vector2(0f, BarHeight - 8f), new Vector2(0f, .5f), Vector2.zero);

            var rt = (RectTransform)fill.transform;
            rt.pivot = new Vector2(0f, .5f);
            rt.anchoredPosition = new Vector2(BarInset, 0f);
            return fill;
        }

        /// <summary>
        /// Grows a bar, ticking as it goes. The overrun rides the same progress so the two
        /// lengths can never arrive out of step.
        /// </summary>
        void Grow(Image fill, float width, Image over, float overWidth, string sfx, float pitch)
        {
            if (!fill) return;

            var rt = (RectTransform)fill.transform;
            var ort = over ? (RectTransform)over.transform : null;

            Roll.Progress(.58f, t =>
            {
                if (rt) rt.sizeDelta = new Vector2(width * t, BarHeight - 8f);
                if (ort) ort.sizeDelta = new Vector2(overWidth * t, BarHeight - 8f);
            }, this, sfx, .28f, pitch, pitch * 1.5f);
        }

        /// <summary>
        /// The carved marker at the end of the grove's own bar.
        ///
        /// A painted shield rather than a number in a column, because the number's job here is
        /// to say <em>where the mark is</em>. The art is the win pack's own plaque, re-cut: the
        /// shipped crop used the atlas rectangle verbatim, which left it on its side and bled a
        /// sliver of a horn into one edge.
        /// </summary>
        RectTransform BuildMark(Image track)
        {
            float x = _groveWidth + BarInset - TrackWidth * .5f;

            var mark = UIKit.Img("Mark", track.transform, Art.S("Ui/Win/shield"), Color.white,
                                 MarkSize, new Vector2(.5f, .5f), new Vector2(x, 0f));
            mark.preserveAspect = true;
            mark.transform.localScale = Vector3.zero;

            // Lifted off centre because the art is a shield pointing down: its flat face sits
            // above the middle, and a number centred on the rect reads as sliding off the tip.
            var n = UIKit.Titled("N", mark.transform, Run.Route.ToString(), 30, Pal.Parchment,
                                 TextAnchor.MiddleCenter, new Vector2(60f, 40f),
                                 new Vector2(.5f, .5f), new Vector2(0f, 8f), 3f, 2f);
            UIKit.Shrinkable(n, 18);

            return (RectTransform)mark.transform;
        }

        /// <summary>
        /// The population standing, as a struck medal on a pill.
        ///
        /// <para>
        /// Same vocabulary as the map's own mark (<c>LevelsScreen.Medal</c>) and deliberately
        /// so: a trophy rather than a star, because the stars directly above are already
        /// counting something else and a gold star here would read as a fourth one. One glyph
        /// in three colours rather than three glyphs, because a medal ladder needs no teaching.
        /// The rim is cream on every tier — ringing a bronze medal in bronze makes the rim
        /// vanish.
        /// </para>
        /// </summary>
        RectTransform BuildStanding(float y, RankBand band, int percent)
        {
            bool top = band == RankBand.Top10;
            Color ink = top ? Pal.Gold
                      : band == RankBand.Top25 ? Pal.Parchment
                      : new Color(1f, .95f, .86f, .84f);

            const float PillW = 560f, PillH = 96f;

            var host = UIKit.Box("Standing", Panel, new Vector2(PillW, PillH),
                                 new Vector2(.5f, 1f), new Vector2(0f, y));
            host.localScale = Vector3.zero;

            var pill = UIKit.Img("Pill", host, Art.Round(28), new Color(.04f, .09f, .13f, .84f),
                                 new Vector2(PillW, PillH), new Vector2(.5f, .5f), Vector2.zero);

            var edge = UIKit.Img("Edge", pill.transform, Art.RoundOutline(28, 3f),
                                 new Color(ink.r, ink.g, ink.b, top ? .62f : .40f));
            UIKit.StretchTo((RectTransform)edge.transform, 0f, 0f, 0f, 0f);

            var seat = new Vector2(-PillW * .5f + 64f, 0f);

            UIKit.Img("Halo", host, Art.Glow(96, 2.2f), new Color(ink.r, ink.g, ink.b, top ? .42f : .28f),
                      Vector2.one * 158f, new Vector2(.5f, .5f), seat);

            var disc = UIKit.Img("Disc", host, Art.Disc(128), ink,
                                 Vector2.one * 74f, new Vector2(.5f, .5f), seat);

            UIKit.Img("Rim", host, Art.Ring(128, 9f), new Color(1f, .98f, .90f, top ? .92f : .74f),
                      Vector2.one * 74f, new Vector2(.5f, .5f), seat);

            var glyph = UIKit.Img("Trophy", host, Art.S("Ui/ic_trophy"), new Color(.20f, .13f, .07f, .92f),
                                  Vector2.one * 40f, new Vector2(.5f, .5f), seat);
            glyph.preserveAspect = true;

            var line = UIKit.Titled("Band", host, Loc.Get(RankTier.KeyOf(band)), 33, ink,
                                    TextAnchor.MiddleCenter, new Vector2(PillW - 160f, 46f),
                                    new Vector2(.5f, .5f), new Vector2(36f, 0f), 3f, 3f);
            UIKit.Shrinkable(line, 20);

            // Only the best tier breathes, for the reason the map gives: motion is the loudest
            // thing on the panel, so spending it on every ranked glade singles out none.
            if (top) Tween.Breathe(disc.transform, .055f, 2.4f);

            return host;
        }

        // =============================================================== the exits
        /// <summary>
        /// Next, replay and map, on one row.
        ///
        /// <para>
        /// The primary is flanked rather than stacked over the two secondaries, which is what
        /// the row costs 80px less than and reads as one set of three choices instead of a
        /// button with an afterthought under it.
        /// </para>
        /// </summary>
        Btn BuildButtons(CatalogIndex index, bool last, LevelId next)
        {
            var nextId = last ? LevelId.None : next;

            // Offered here and nowhere else. A player who has just finished a chapter has
            // something worth keeping, which is exactly when asking them to protect it is a
            // service rather than an obstacle — and the answer costs nothing either way.
            bool offerAccount = FinishedAChapter(index) && AccountOverlay.ShouldOffer();

            var nextButton = UIKit.TextButton("Next", Panel, "btn_green",
                                              Loc.Get(last ? "ui.win.glades" : "ui.win.next"), 50,
                                              new Vector2(520f, 152f), new Vector2(.5f, 0f),
                                              new Vector2(0f, ButtonY),
                                              () => Close(() =>
                                              {
                                                  if (offerAccount)
                                                  {
                                                      AccountOverlay.NoteOffered();
                                                      Flow.Modal<AccountOverlay>();
                                                      return;
                                                  }
                                                  if (last) Flow.Go<LevelsScreen>();
                                                  else Flow.Go<PlayScreen>(v => v.LevelId = nextId);
                                              }));
            UIKit.Halo(nextButton.transform, Pal.Mint, 620f, .28f);

            var replayId = Run.Level;
            UIKit.IconButton("Replay", Panel, "sq_orange", "ic_restart", new Vector2(138f, 138f),
                             new Vector2(.5f, 0f), new Vector2(-SideButtonX, ButtonY),
                             () => Close(() => Flow.Go<PlayScreen>(v => v.LevelId = replayId)));

            // Skins.Nav rather than the literal, so this panel moves with the rule the rest of
            // the chrome now follows. Replay keeps its own orange: only the greys moved.
            UIKit.IconButton("Map", Panel, Skins.Nav, "ic_list", new Vector2(138f, 138f),
                             new Vector2(.5f, 0f), new Vector2(SideButtonX, ButtonY),
                             () => Close(() => Flow.Go<LevelsScreen>()));

            return nextButton;
        }

        /// <summary>Where the button row sits above the frame's bottom edge, and how far out.</summary>
        const float ButtonY = 132f, SideButtonX = 340f;

        // =============================================================== the verdict
        /// <summary>
        /// Written out rather than assembled, so the build's string checker can see every key.
        ///
        /// Three readings, not two: <see cref="RunOutcome.Route"/> is the distance to the
        /// <em>authored</em> solution and a player can beat it, because a glade is won when
        /// every lamp is lit and spare conduits may be left pointing anywhere. "Under" is the
        /// rarest and best thing this panel can say, and a design that promised the route was
        /// unbeatable would show that player a bug.
        /// </summary>
        string VerdictLine()
        {
            int over = Run.TurnsOverRoute;

            if (Run.BeatRoute)
                return over == -1 ? Loc.Get("ui.route.under_one") : Loc.Format("ui.route.under", -over);

            if (Run.MatchedRoute) return Loc.Get("ui.route.perfect");

            return over == 1 ? Loc.Get("ui.route.close_one") : Loc.Format("ui.route.close", over);
        }

        /// <summary>Gold for beating the route, radiance for matching it, parchment for close.</summary>
        Color VerdictInk()
            => Run.BeatRoute ? Pal.Gold
             : Run.MatchedRoute ? Pal.Radiance
             : Pal.Parchment;

        static void Reveal(Text t)
        {
            if (t) Tween.Pop(t.transform, 0f, .4f);
        }

        /// <summary>
        /// Back closes the bubble first when one is open. A popover that ignores the hardware
        /// key and takes the whole panel with it is how a player loses a celebration they were
        /// only trying to read a footnote on.
        /// </summary>
        public override bool OnBack()
        {
            if (_bubble) { CloseRouteInfo(); return true; }

            Close(() => Flow.Go<LevelsScreen>());
            return true;
        }
    }
}
