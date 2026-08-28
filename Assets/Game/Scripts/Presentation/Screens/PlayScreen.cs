using System;
using System.Collections;
using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Daily;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    public sealed class PlayScreen : RunScreen, IPlaysLevel
    {
        /// <summary>Which level to play. Set by whoever opened the screen.</summary>
        public LevelId LevelId;

        /// <summary>
        /// How <c>PlayRoute</c> hands the level over, without the field above having to
        /// become a property that every line in this file then reads through.
        /// </summary>
        LevelId IPlaysLevel.LevelId { set => LevelId = value; }

        public override string Track => "mus_play";

        BoardView _board;
        RectTransform _boardHost;
        Puzzle _puzzle;
        LevelDefinition _def;
        ChapterDefinition _chapter;
        Text _moves, _lamps, _hintCount;
        StarRow _pips;
        Btn _undo, _hint;
        bool _finished;

        /// <summary>
        /// Hints spent on this run, for <see cref="RunOutcome.HintsUsed"/> and the flawless
        /// stamp.
        ///
        /// <para>
        /// Counted here rather than derived from the board, which is what changed: a hint
        /// used to be an allowance the board handed out, so "spent" was the allowance minus
        /// what was left. It is now taken from an account pool that also refills on a clock
        /// and is spent on other glades, so nothing about the pool's count describes
        /// <em>this</em> run. Reset wherever a fresh board is handed over, beside the clock.
        /// </para>
        /// </summary>
        int _hintsThisRun;

        /// <summary>
        /// True once this run has been paid for — that is, once abandoning it costs a heart.
        ///
        /// <para>
        /// Set on the <em>first turn</em>, or after a few seconds of clock, whichever comes
        /// first. Both halves are needed. Waiting only for a turn lets a player study the board
        /// for the whole countdown, back out for free and re-enter knowing the answer, which is
        /// exactly the free planning the countdown's start edge was moved to prevent. Committing
        /// the instant the board appears would charge somebody who opened the wrong glade and
        /// left within a second. Three seconds is longer than a misplaced tap and far shorter
        /// than reading a 6x7 board.
        /// </para>
        /// </summary>

        /// <summary>How long the board may be looked at before the run is owed for.</summary>
        const float CommitGraceSeconds = 3f;
        bool _hasColourKey;
        float _startedAt;

        /// <summary>
        /// The authored route, measured on the untouched board.
        ///
        /// <para>
        /// Taken once, here, because <see cref="Puzzle.TurnsToSolution"/> is a live reading
        /// of the board in front of it — asking at the end of a run returns zero, since the
        /// board is by then solved. It survives a restart without being retaken:
        /// <see cref="Puzzle.Reset"/> restores the same start rotations, so the route is a
        /// fact about the glade rather than about the attempt.
        /// </para>
        /// </summary>
        int _route;

        /// <summary>
        /// Whole seconds already painted, so the readout builds one string a second instead
        /// of one a frame. -1 forces the next paint.
        /// </summary>

        public BoardView Board => _board;
        public LevelDefinition Level => _def;

        protected override void Build()
        {
            // The chapter is normally already in hand - the map loaded it to draw the
            // node that was just tapped - so this completes without yielding and the
            // board appears in the same frame it always did. It only ever waits when
            // the player arrived some other way: a deep link, or a "next" that stepped
            // over a chapter boundary.
            // The pool refills on a clock, so the badge cannot be a thing painted once when
            // the board was built — an eight-hour wait can land while somebody is staring at
            // a glade. An event rather than a poll in Update, because it fires perhaps twice
            // in a session and Update runs every frame.
            Wallet.HintsChanged += OnHintsChanged;

            StartCoroutine(ResolveThenBuild());
        }

        void OnDestroy()
        {
            Wallet.HintsChanged -= OnHintsChanged;
        }

        IEnumerator ResolveThenBuild()
        {
            var chapterId = GameContent.ChapterOf(LevelId);
            if (!chapterId.IsValid)
            {
                Debug.LogError($"[Play] unknown level '{LevelId}', returning to the map");
                yield return BailOut();
                yield break;
            }

            var task = GameContent.ChapterAsync(chapterId);
            while (!task.IsCompleted) yield return null;

            if (task.IsFaulted) Debug.LogException(task.Exception);

            var body = task.Result;
            _def = body?.Find(LevelId);
            _chapter = body?.Definition;

            if (_def == null || _chapter == null)
            {
                Debug.LogError($"[Play] level '{LevelId}' could not be read, returning to the map");
                yield return BailOut();
                yield break;
            }

            if (!PuzzleFactory.TryCreate(_def, out _puzzle, out var errors))
            {
                Debug.LogError($"[Play] level '{LevelId}' is unplayable: {string.Join("; ", errors)}");
                yield return BailOut();
                yield break;
            }

            // Must happen before anything asks for a sprite: it registers which
            // addresses belong to this chapter, and an asset fetched before that
            // registration would be filed as global and never released.
            _ = AssetLibrary.EnsureChapterAsync(body);

            if (!this) yield break;
            BuildResolved();
        }

        void BuildResolved()
        {
            Scenery.Cover(Content, "Bg/" + _def.Presentation.ResolveBackdrop(_chapter), 0f, .22f);
            Fireflies.Spawn(Content, 18, new Color(1f, .97f, .86f), 5f, 18f);

            BuildTopBar();
            BuildStatus();
            BuildBottomBar();

            // In Safe with the rest of the chrome. The board is the largest control on the
            // screen, and one laid out against the full canvas while the counters above it are
            // laid out against the inset is two rulers — which is how they come to overlap on
            // exactly the devices nobody has to hand.
            _boardHost = UIKit.Node("BoardHost", Safe);
            _boardHost.offsetMin = new Vector2(26f, 300f);
            _boardHost.offsetMax = new Vector2(-26f, _hasColourKey ? -BoardTopWithKey : -BoardTop);

            _board = _boardHost.gameObject.AddComponent<BoardView>();
            _board.OnChanged = Refresh;
            _board.OnSolved = Finish;
            _board.OnDefeated = Defeat;

            // Before a single turn is possible, and before the board view exists to allow one.
            _route = _puzzle.TurnsToSolution;

            // The first run of a glade goes through here rather than through RestartLevel, so
            // it needs arming too — a clock built with the screen has no limit yet, and one
            // that never learned this glade's would leave the opening attempt untimed while
            // every retry after it was not.
            ResetRun();

            _startedAt = Time.unscaledTime;
            PlayerProgress.NoteOpened(_def.Id);
            LevelAnalytics.TrackStarted(_def, PlayerProgress.Record(_def.Id).Clears + 1);

            StartCoroutine(RaiseBoard());
        }

        /// <summary>
        /// Retreats to the map once the incoming transition has finished. Flow ignores
        /// navigation while it is mid-transition, so leaving immediately would strand
        /// the player on an empty screen.
        /// </summary>
        IEnumerator BailOut()
        {
            while (Flow.Busy) yield return null;
            Flow.Go<LevelsScreen>();
        }

        IEnumerator RaiseBoard()
        {
            yield return null;
            Canvas.ForceUpdateCanvases();
            int guard = 0;
            while (_boardHost.rect.width < 40f && guard++ < 60) yield return null;

            _board.Build(_boardHost, _puzzle,
                         Pal.BoardTheme.From(_def.Presentation.ResolveSlate(_chapter)));
            Refresh();

            // Now that there are tiles to ring. The count has not changed — it came off the
            // puzzle when the header was built — but the targets have, and this is the cheapest
            // place to say so. See RunLessons.Ask.
            Teaching.Ask();
        }

        // -------------------------------------------------------------- chrome
        /// <summary>How tall the header band is. The clock and the two nav buttons live in it.</summary>
        const float BarHeight = 230f;

        /// <summary>Where the counter row sits, measured down from the safe area's top edge.</summary>
        const float StatusY = 308f;

        /// <summary>Where the blending chart sits, when the board has one.</summary>
        const float ColourKeyY = 392f;

        /// <summary>How much room the chrome above the board takes, with and without that chart.</summary>
        const float BoardTop = 370f, BoardTopWithKey = 444f;

        /// <summary>
        /// The header: a way back, a way to pause, and the clock.
        ///
        /// <para>
        /// <b>Built into <see cref="Flow.Safe"/> rather than <c>Content</c>, and that is the
        /// fix rather than the tidy-up.</b> Every control on this screen used to hang off the
        /// full-bleed node, so the safe-area inset this project added applied to every screen
        /// except the one a player spends their time on: on a phone with a cutout the top row
        /// ran underneath it. Art has not moved — the backdrop and the fireflies are supposed
        /// to run under a camera, and letterboxing a picture to dodge one is the worse answer.
        /// Only the things that have to be read or pressed moved.
        /// </para>
        /// <para>
        /// <b>The level's name and its tagline are gone.</b> They were the two highest things
        /// on the screen and so the two a cutout takes first, and neither was load-bearing: the
        /// player chose the level by name a screen ago. The tagline used to be offered back as
        /// a flavour line floating along the bottom of the board, and that is gone too — a box
        /// on every level of every mode is furniture, and the tips are what a board has to say.
        /// </para>
        /// </summary>
        void BuildTopBar()
        {
            var bar = UIKit.Box("TopBar", Safe, new Vector2(0f, BarHeight), new Vector2(.5f, 1f),
                                new Vector2(0f, -BarHeight * .5f));
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(0f, BarHeight);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .05f, .08f, .55f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, -40, 0, 0);
            ((RectTransform)shade.transform).localRotation = Quaternion.Euler(0, 0, 180f);

            UIKit.IconButton("Back", bar, Skins.Nav, "ic_left", new Vector2(118f, 118f),
                             new Vector2(0f, .5f), new Vector2(102f, -6f), LeaveToMap);
            UIKit.IconButton("Pause", bar, Skins.Nav, "ic_pause", new Vector2(118f, 118f),
                             new Vector2(1f, .5f), new Vector2(-102f, -6f), Pause);

            // Beside the pause key, and only on a glade that actually teaches something —
            // RunLessons decides that once the board has been read. See its BuildKey.
            Teaching.BuildKey(bar, new Vector2(-102f, -6f));
        }

        void BuildStatus()
        {
            var row = UIKit.Box("Status", Safe, new Vector2(0f, 96f), new Vector2(.5f, 1f),
                                new Vector2(0f, -StatusY));
            row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f);
            row.sizeDelta = new Vector2(0f, 96f);

            _moves = Scenery.Pill(row, "0", 40, new Vector2(230f, 84f), new Vector2(0f, .5f),
                                  new Vector2(160f, 0f), null, "ic_restart");
            _lamps = Scenery.Pill(row, "0/0", 40, new Vector2(230f, 84f), new Vector2(1f, .5f),
                                  new Vector2(-160f, 0f), null, "ic_check");
            _pips = StarRow.Create(row, new Vector2(.5f, .5f), Vector2.zero, 62f, 66f, 3);

            BuildColourKey();
        }

        /// <summary>
        /// The blending chart, sitting under the counters.
        ///
        /// Permanent rather than a tip, because it is a lookup and not a lesson: the
        /// rule takes five seconds to explain and a while to internalise, and a player
        /// mid-puzzle wants to check "what does red and blue make" without being taught
        /// anything. A modal cannot answer that; a chart on the wall can.
        ///
        /// Only drawn where it applies. On a single-colour glade it would be three rows
        /// of noise above the board, so a board with one heart colour gets nothing and
        /// keeps the space.
        /// </summary>
        void BuildColourKey()
        {
            if (!NeedsColourKey()) return;

            var strip = UIKit.Box("ColourKey", Safe, new Vector2(0f, 64f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -ColourKeyY));
            strip.anchorMin = new Vector2(0f, 1f);
            strip.anchorMax = new Vector2(1f, 1f);
            strip.sizeDelta = new Vector2(0f, 64f);

            // the three pairs; every other blend is these repeated
            Recipe(strip, -300f, Energy.R, Energy.G);
            Recipe(strip, 0f, Energy.R, Energy.B);
            Recipe(strip, 300f, Energy.G, Energy.B);

            // the board starts lower when the chart is there, so it is never covered.
            // Recorded rather than applied: BuildStatus runs before the host exists.
            _hasColourKey = true;
        }

        bool NeedsColourKey()
        {
            int first = -1;

            foreach (var cell in _puzzle.C)
            {
                if (cell.kind != Kind.Source) continue;
                if (first < 0) first = cell.colour;
                else if (cell.colour != first) return true;
            }

            return false;
        }

        /// <summary>One "a + b = c" of coloured dots. No words, so nothing to translate.</summary>
        static void Recipe(Transform parent, float x, int a, int b)
        {
            Dot(parent, x - 58f, a);
            Sign(parent, x - 29f, "+");
            Dot(parent, x, b);
            Sign(parent, x + 29f, "=");
            Dot(parent, x + 58f, a | b);
        }

        static void Dot(Transform parent, float x, int energy)
        {
            var colour = Pal.EnergyColour(energy);

            UIKit.Img("Glow", parent, Art.Glow(64, 1.9f), Pal.A(colour, .45f),
                      Vector2.one * 46f, new Vector2(.5f, .5f), new Vector2(x, 0f));
            UIKit.Img("Dot", parent, Art.Disc(64), Pal.Lift(colour, .25f),
                      Vector2.one * 26f, new Vector2(.5f, .5f), new Vector2(x, 0f));
        }

        static void Sign(Transform parent, float x, string glyph)
            => UIKit.Titled("S" + x, parent, glyph, 26, new Color(1f, .96f, .86f, .55f),
                            TextAnchor.MiddleCenter, new Vector2(30f, 30f),
                            new Vector2(.5f, .5f), new Vector2(x, 1f), 0f, 2f);

        void BuildBottomBar()
        {
            var bar = UIKit.Box("BottomBar", Safe, new Vector2(0f, 250f), new Vector2(.5f, 0f), new Vector2(0f, 125f));
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f);
            bar.sizeDelta = new Vector2(0f, 250f);

            var shade = UIKit.Img("Shade", bar, Art.FadeUp(64), new Color(.02f, .05f, .08f, .5f));
            UIKit.StretchTo((RectTransform)shade.transform, 0, 0, 0, -40);

            _undo = UIKit.IconButton("Undo", bar, "sq_blue", "ic_undo", new Vector2(150f, 150f),
                                     new Vector2(.5f, .5f), new Vector2(-215f, 10f), () => _board.Undo());
            _hint = UIKit.IconButton("Hint", bar, "sq_orange", "ic_hint", new Vector2(168f, 168f),
                                     new Vector2(.5f, .5f), new Vector2(0f, 10f), UseHint);
            // Routed through RestartLevel rather than straight at the board, so this button
            // and the pause menu's cannot disagree about what a restart resets — the clock
            // was the first thing they would have.
            UIKit.IconButton("Restart", bar, "sq_green", "ic_restart", new Vector2(150f, 150f),
                             new Vector2(.5f, .5f), new Vector2(215f, 10f), RestartLevel);

            var badge = UIKit.Img("Badge", _hint.transform, Art.Disc(64), Pal.Rose,
                                  new Vector2(58f, 58f), new Vector2(1f, 1f), new Vector2(-16f, -16f));
            _hintCount = UIKit.Titled("N", badge.transform, Wallet.Hints.Count.ToString(), 34,
                                      Pal.Cream, TextAnchor.MiddleCenter, outline: 0f, shadow: 2f);

            Caption(bar, "undo", -215f);
            Caption(bar, "hint", 0f);
            Caption(bar, "reset", 215f);

            foreach (Transform c in bar)
            {
                if (c.GetComponent<Btn>() == null) continue;
                c.localScale = Vector3.zero;
                Tween.Pop(c, 0f, .5f, .35f + Mathf.Abs(((RectTransform)c).anchoredPosition.x) * .0006f)
                     .OnDone(() => { var b = c.GetComponent<Btn>(); if (b) b.Rehome(); });
            }
        }

        static void Caption(Transform parent, string text, float x)
            => UIKit.Titled("Cap_" + text, parent, text, 26, new Color(1f, .95f, .84f, .62f),
                            TextAnchor.MiddleCenter, new Vector2(220f, 36f), new Vector2(.5f, .5f),
                            new Vector2(x, -84f), 3f, 0f);

        // --------------------------------------------------------------- the stake clock
        /// <summary>
        /// Accrues play time, and commits the run once there is enough of it.
        ///
        /// <para>
        /// The only thing left that measures a glade. There is no countdown: a run ends on
        /// the move budget, on a crumbled conduit, or on the glade being solved, and it is
        /// graded on turns alone. What survives is the stake — see <see cref="Commit"/> —
        /// which needs to tell a player studying a board from one who has begun playing it.
        /// </para>
        /// <para>
        /// Time only accrues while the board can actually be acted on. <c>Locked</c> covers
        /// the pause overlay, the win and defeat sequences and the brief animation locks; a
        /// backgrounded app contributes nothing at all, because no frames run. Whether the run
        /// has been allowed to begin — the screen still being presented, a first-timer still
        /// reading a lesson — is <c>RunScreen</c>'s half, and <c>Tick</c> asks it. Both
        /// readings are needed and only one is reliable alone: <c>Locked</c> has several
        /// writers, including tweens scheduled before anybody knew a lesson was coming. See
        /// <c>RunScreen.Hold</c>.
        /// </para>
        /// </summary>
        protected internal override bool Runnable
            => _puzzle != null && _board != null && !_board.Locked && !_finished;

        protected internal override void Running(bool running)
        {
            if (_puzzle == null) return;

            // Polled rather than hooked, for the reason every edge on this screen is: a poll
            // cannot miss the edge, cannot fire twice, and leaves nothing to unsubscribe.
            //
            // Asked whatever the frame answered, because a run is owed for the moment a tile is
            // turned — `Played` is the half that only accrues on frames the run was allowed,
            // which is what keeps a lesson being read from committing anybody.
            if (!Committed && !_finished && (_puzzle.Moves > 0 || Played > CommitGraceSeconds))
                Commit();
        }

        /// <summary>
        /// Arms a fresh run. Every path that hands the player a new board goes through here —
        /// the first presentation, a restart, and a retry after defeat.
        ///
        /// <para>
        /// One funnel rather than a line beside each caller, because everything cleared here
        /// sticks rather than merely looking wrong once: play time carried over would commit
        /// the next run before it was touched, and a hint count carried over would deny the
        /// player the flawless stamp on a run they solved unaided.
        /// </para>
        /// </summary>
        void ResetRun()
        {
            ResetPlayed();
            Continue.Reset();
            _hintsThisRun = 0;
            _lostBy = DefeatReason.OutOfMoves;
        }

        // --------------------------------------------------------------- state
        void Refresh()
        {
            if (_puzzle == null) return;
            if (_moves)
            {
                // Turns remaining, not turns spent. A budget the player has to subtract
                // in their head is not a budget they can plan against — and once it is
                // low the number itself is the tension, so it turns amber then red.
                string text = _puzzle.HasBudget
                    ? _puzzle.MovesLeft.ToString()
                    : _puzzle.Moves.ToString();

                bool changed = _moves.text != text;
                _moves.text = text;

                if (_puzzle.HasBudget)
                {
                    int left = _puzzle.MovesLeft;
                    _moves.color = left <= 3 ? Pal.Ember
                                 : left <= 8 ? Pal.Gold
                                 : Pal.Cream;

                    if (changed && left <= 3) Tween.Punch(_moves.transform, .34f, .34f);
                }

                if (changed) Tween.Punch(_moves.transform, .22f, .3f);
            }
            if (_lamps)
            {
                string s = $"{_puzzle.LampsLit}/{_puzzle.LampCount}";
                if (_lamps.text != s) { _lamps.text = s; Tween.Punch(_lamps.transform, .25f, .34f); }
            }
            // The stars already earned here, not what this run is currently on track
            // for. A fresh board is nought moves in, which projects to three stars and
            // reads as "already perfect" before the player has touched anything.
            if (_pips) _pips.SetInstant(PlayerProgress.Stars(_def.Id));
            if (_undo) _undo.Interactable = _board != null && _board.CanUndo;

            // Greyed on exactly the edges the other two are, which is why it is asked here:
            // BoardView.Locked raises OnChanged, so a latch taken by a cascade reaches this.
            Teaching.Refresh();
            if (_hint)
            {
                // Live whenever the board is taking input at all — not when it has a hint to
                // give, and never mind what the pool holds. Both refusals are sentences
                // worth reading (UseHint owns them) and one of them is the way to the offer
                // panel, so greying the button would hide the very control a player with an
                // empty pool needs. The rule CompanionUnlockOverlay already follows for a
                // short balance: a dead control teaches people the feature is broken.
                _hint.Interactable = _board != null && _board.Accepting;
                PaintHintCount();
            }
        }

        /// <summary>
        /// The badge over the hint button: how many the account holds right now.
        ///
        /// Separated from <see cref="Refresh"/> because the number moves for a reason the
        /// board knows nothing about — the refill clock — so it is also repainted from
        /// <see cref="OnHintsChanged"/>. Reading <c>Wallet.Hints</c> is what commits a refill
        /// that fell due while this screen was open.
        /// </summary>
        void PaintHintCount()
        {
            if (!_hintCount) return;

            var hints = Wallet.Hints;

            // A question mark rather than a nought, because the two say different things.
            // "0" reads as a spent control and invites nobody to press it; "?" is the state
            // the button is actually in — there is nothing in the pool, and this is where
            // you find out when there will be. It is a loc key rather than a literal for
            // invariant 6's reason: not every script writes this mark the way English does.
            string text = hints.CanSpend
                ? hints.Count.ToString()
                : Loc.Get("ui.play.hint_none");

            if (_hintCount.text == text) return;

            _hintCount.text = text;
            Tween.Punch(_hintCount.transform, .22f, .3f);
        }

        void OnHintsChanged(Hints hints)
        {
            if (this == null) return;
            PaintHintCount();
            if (_hint) _hint.Interactable = _board != null && _board.Accepting;
        }

        /// <summary>
        /// The hint button's one handler: reveal a conduit, open the offer, or say why
        /// neither is happening.
        ///
        /// <para>
        /// Which of the three is <see cref="HintPrompt.OnTap"/>, so the rule is provable
        /// offline and this method is only the doing. The board is asked before the pool,
        /// which is the safety: a board with nothing left to reveal cannot cost anybody a
        /// hint, and nobody is sold a video for one that could not have been spent. The pool
        /// is charged only once the reveal has actually begun, and each refusal says which
        /// one it is — "nothing happened" is how a player concludes a button is broken.
        /// </para>
        /// </summary>
        void UseHint()
        {
            if (_board == null || _finished) return;

            switch (HintPrompt.OnTap(_board.CanHint, Wallet.Hints.CanSpend))
            {
                case HintTap.NothingToReveal:
                    Scenery.Toast(Content, Loc.Get("ui.play.hint_nothing"), Pal.Parchment, 1.6f);
                    return;

                case HintTap.Offer:
                    OfferHint();
                    return;
            }

            if (!_board.Hint(HintRevealed)) return;

            Wallet.TrySpendHint();
            _hintsThisRun++;

            LevelAnalytics.TrackHintUsed(_def, Wallet.Hints.Count, _puzzle.Moves);
            Scenery.Toast(Content, Loc.Get("ui.play.hint_used"), Pal.Gold, 1.6f);
            Refresh();
        }

        /// <summary>
        /// The reveal has finished and the board is back. If that was the player's last
        /// hint, the offer follows it.
        ///
        /// <para>
        /// Raised from the board rather than from the spend, because the spend happens while
        /// the conduit is still turning: a panel thrown up then would land on a latched board
        /// and cover the very thing the hint was bought to show. The decision itself is
        /// <see cref="HintPrompt.OffersAfterSpending"/>, where it can be proved.
        /// </para>
        /// </summary>
        void HintRevealed()
        {
            if (this == null) return;

            bool live = !_finished && _board != null && _board.Accepting;

            if (!HintPrompt.OffersAfterSpending(live, Wallet.Hints.CanSpend,
                                                RewardedAds.ShouldOffer(AdPlacement.HintRefill)))
                return;

            OfferHint();
        }

        /// <summary>
        /// The pool is empty. Open the panel that says so, offers a video for one, and
        /// carries the countdown to the next one either way.
        ///
        /// <para>
        /// The board is latched behind the panel exactly as it is for the continue offer,
        /// and for the same reason: the clock only accrues on an unlocked board, so a player
        /// reading the panel or watching thirty seconds of video is not charged the time.
        /// <c>AdOfferOverlay</c> reports through <c>Dismissed</c> on every one of its six
        /// exits, which is what guarantees the board comes back however the panel goes away
        /// — the fault the pause menu shipped with.
        /// </para>
        /// <para>
        /// It opens on every state, including the ones with no video behind them at all —
        /// the hub's "+" rule (the panel for a resource is always the answer to tapping its
        /// control) and the same judgement <c>AdOfferOverlay</c> already makes internally:
        /// a placement the content table does not carry loses its watch button and keeps its
        /// facts, because "no hints" with no number beside it is the sentence that makes a
        /// resource feel broken. The countdown is the thing the player came for.
        /// </para>
        /// </summary>
        void OfferHint()
        {
            bool wasLocked = _board != null && _board.Locked;
            if (_board != null) _board.Locked = true;

            // Both handlers, and they must both hand the board back. AdOfferOverlay raises
            // *exactly one* of Rewarded and Dismissed — the paid branch does not also
            // dismiss — so unlocking in only one of them leaves a player who actually
            // watched the video sitting on a frozen board with a stopped clock, which is the
            // one outcome worse than not offering at all. Same shape as the pause menu's
            // unlatch, and the same rule: the safe outcome is what every exit does.
            Flow.Modal<AdOfferOverlay>(v =>
            {
                v.PlacementId = AdPlacement.HintRefill;
                v.Rewarded = () => CloseHintOffer(wasLocked);
                v.Dismissed = () => CloseHintOffer(wasLocked);
            });
        }

        /// <summary>
        /// Hands the board back after the hint offer, however it went away.
        ///
        /// Restores the latch to what it was rather than clearing it, so an offer raised over
        /// an already-locked board — nothing does that today, and something will — does not
        /// quietly unfreeze a run that was frozen for another reason.
        /// </summary>
        void CloseHintOffer(bool wasLocked)
        {
            if (this == null) return;

            if (_board != null && !wasLocked && !_finished) _board.Locked = false;

            PaintHintCount();
            Refresh();
        }

        void Pause()
        {
            if (_finished) return;
            if (_board != null) _board.Locked = true;
            Flow.Modal<PauseOverlay>(v => v.Screen = this);
        }

        /// <summary>
        /// Puts the glade back as it started. What a restart <em>costs</em> is
        /// <c>RunScreen.RestartLevel</c>'s, which is what asks before this runs.
        /// </summary>
        protected override void Rewind()
        {
            if (_board == null) return;

            // BoardView.Restart refuses while a celebration is playing, and the counters must
            // not be zeroed in that case either or the two would part company.
            if (_board.Locked && _finished) return;

            _board.Restart();
            ResetRun();
            Refresh();
        }

        // --------------------------------------------------------------- the stake
        /// <summary>
        /// What this run is staked on, and how it is written down when it is walked away from.
        /// Everything about hearts, <c>RunGuard</c> and the confirmation is <see cref="RunScreen"/>'s
        /// — see the remarks there for why it stopped being each mode's own.
        /// </summary>
        protected internal override LevelId StakeLevel => _def != null ? _def.Id : LevelId.None;

        protected override bool RunOver => _finished;

        protected override void NoteAbandoned(string reason)
        {
            if (_def == null) return;

            LevelAnalytics.TrackAbandoned(_def, _puzzle.Moves,
                                          Time.unscaledTime - _startedAt, reason);
        }

        void Finish()
        {
            if (_finished) return;
            _finished = true;

            // Paid for by winning it. Cleared before anything else, so a crash during the
            // celebration cannot charge for a run the player actually solved.
            Resolve();

            int moves = _puzzle.Moves;
            int stars = _puzzle.StarsFor(Mathf.Max(1, moves));

            // Everything an ending does to the account happens in one place for both modes -
            // the record, the chests, the streak, the reward and the analytics. See RunLedger,
            // which also owns the ordering this used to state in a comment.
            var done = RunLedger.Win(_def, stars, moves,
                                     Time.unscaledTime - _startedAt, HintsSpent, _route,
                                     _puzzle.LampsLit, _puzzle.LampCount);

            Flow.Modal<WinOverlay>(v =>
            {
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.XpGained = done.Xp;
                v.CreditsGained = done.Credits;

                // Already inside CreditsGained. Passed separately only so the panel can
                // say *why* the number is larger than the glade's usual, which is the
                // whole point of the bonus existing.
                v.GoldenPercent = done.GoldenPercent;

                // News rather than a reward, and it can only be told by the ledger - see
                // RunLedger.WinRecord.ChapterOpened.
                v.ChapterOpened = done.ChapterOpened;
            });
        }

        /// <summary>Hints spent on the run so far. See <see cref="_hintsThisRun"/>.</summary>
        int HintsSpent => _hintsThisRun;

        /// <summary>
        /// The run was lost.
        ///
        /// The heart is charged here rather than inside the board, because the board
        /// knows about turns and the screen knows about the player. Note there is no
        /// star, no move record and no reward: a defeat is not a worse clear, it simply
        /// did not happen, and <c>PlayerProgress</c> never hears about it.
        /// </summary>
        void Defeat(DefeatReason reason)
        {
            if (_finished) return;

            _lostBy = reason;

            // Locked before anything else, and it stays locked for as long as the player is
            // deciding. The board's own endings lock themselves on the way here; doing it for
            // every reason keeps the board dead behind whatever panel comes next rather than
            // depending on a scrim to swallow taps.
            if (_board != null) _board.Locked = true;

            // The offer comes first and the defeat is what happens when it is declined — see
            // RunContinueFlow.OfferOrLose. Nothing below this line runs until the player has
            // said no, which is what keeps a continued run from ever being recorded as a
            // loss, counted towards a chest or charged a heart.
            Continue.OfferOrLose(() => Concede(reason));
        }

        /// <summary>
        /// Why this run ended, kept so <see cref="Deficit"/> can say whether more turns would
        /// be worth anything.
        ///
        /// <para>
        /// A shattered conduit is the case that matters: the board is broken and no number of
        /// turns mends it, so it must never be sold one. Turns run out is the only ending a
        /// glade has that a continue can answer.
        /// </para>
        /// </summary>
        DefeatReason _lostBy = DefeatReason.OutOfMoves;

        /// <summary>
        /// A glade needs no allowance restored before a bought turn is a usable turn — every
        /// turn is a turn, and a board with one left is playable. So nought, and the offer is
        /// exactly what the table authored.
        ///
        /// <see cref="RunContinue.NoContinue"/> on a board a continue could not rescue: one
        /// with no budget to raise, and one whose conduit has already crumbled.
        /// </summary>
        protected internal override int ContinueDeficit
            => _puzzle != null && _puzzle.HasBudget && _lostBy == DefeatReason.OutOfMoves
                 ? 0 : RunContinue.NoContinue;

        /// <summary>
        /// The turns were paid for: raise the budget and wake the grove.
        ///
        /// <para>
        /// The model first and the view second, because <c>BoardView.Revive</c> refuses to
        /// hand back a board that is still out of turns — which is the guard that makes
        /// "a continue that does not continue" impossible rather than merely unlikely.
        /// </para>
        /// </summary>
        protected internal override void ContinueWith(int turns)
        {
            if (_puzzle == null) return;

            _puzzle.Grant(turns);

            if (_board != null) _board.Revive();
            Refresh();
        }

        /// <summary>
        /// The run is over: the heart, the record, the analytics and the panel.
        ///
        /// <para>
        /// This was <c>Defeat</c> in full until a lost run could be carried on. Nothing in it
        /// changed — it simply runs after the offer rather than instead of one, so every
        /// number it reads describes a run that really has ended.
        /// </para>
        /// </summary>
        void Concede(DefeatReason reason)
        {
            if (_finished) return;
            _finished = true;

            // The heart is charged below, so the marker's work is done — and clearing it here
            // rather than after the charge means a crash mid-defeat cannot charge twice.
            Resolve();

            if (_board != null) _board.Locked = true;

            // Read off the board before anything touches it. The panel this feeds offers
            // a retry, which restarts the very board being measured — so a screen that
            // asked afterwards would be describing a run that no longer exists.
            var done = RunLedger.Loss(_def, reason, _puzzle.Moves,
                                      Time.unscaledTime - _startedAt, HintsSpent, _route,
                                      _puzzle.TurnsToSolution,
                                      _puzzle.LampsLit, _puzzle.LampCount,
                                      price: Price);

            Flow.Modal<DefeatOverlay>(v =>
            {
                v.Screen = this;
                v.Run = done.Run;
                v.Streak = done.Streak;
                v.HeartsLeft = done.HeartsLeft;
                v.HeartWasCharged = done.HeartCharged;
                v.Price = done.Price;
            });
        }

        /// <summary>
        /// Another go after a defeat. Distinct from <see cref="RestartLevel"/> because
        /// the run had already been declared over — the finished latch has to come back
        /// off, and the board has to be rebuilt rather than merely rewound.
        /// </summary>
        public override void RetryAfterDefeat()
        {
            _finished = false;
            _startedAt = Time.unscaledTime;

            _board.Restart();

            // After the latch comes off, so the clock is armed for the new run rather than
            // still carrying the stopped reading of the one that just failed. The stake is
            // armed with it: this is a fresh run and it has not been paid for yet.
            Resolve();
            ResetRun();
            Refresh();
        }

        /// <summary>
        /// Long enough for the intro sweep to have finished, so the tile a tip rings is
        /// actually on screen to be ringed.
        /// </summary>
        protected internal override float LessonDelay => .75f;

        /// <summary>
        /// Every mechanic this board contains that has a lesson, in teaching order.
        ///
        /// <para>
        /// Derived from the board rather than authored, so a chapter shipped next year that
        /// happens to use brittle stone teaches it with no authoring and can never point at a
        /// mechanic the glade does not have. Whether the player has met any of it is
        /// <see cref="RunScreen"/>'s question, not this one's — see <see cref="Lessons"/> there
        /// for why the two are asked separately.
        /// </para>
        /// <para>
        /// Almost always empty: after the first few glades a board contains nothing that has a
        /// lesson attached, which is what keeps the review key off nearly every header.
        /// </para>
        /// </summary>
        protected internal override void Lessons(System.Collections.Generic.List<Lesson> into)
        {
            if (_puzzle == null) return;

            // The board is asked for a target rather than required to exist. The list itself
            // is a fact about the parsed puzzle, which this screen has in hand before it draws
            // a single tile — that is what lets the review key be shown while the iris is still
            // closed instead of appearing in front of the player a moment after it opens. A
            // lesson that cannot find its tile is still a lesson; it teaches without pointing.
            foreach (var sighting in MechanicScan.Taught(_puzzle))
                into.Add(Lesson.At(sighting.Mechanic, TargetFor(sighting), AlongsideFor(sighting)));
        }

        /// <summary>What a lesson rings, when there is anything drawn yet to ring.</summary>
        RectTransform TargetFor(MechanicSighting sighting)
        {
            if (!sighting.HasCell) return HudTargetFor(sighting.Mechanic);

            return _board != null ? _board.TileAt(sighting.CellIndex) : null;
        }

        /// <summary>
        /// The other tiles a lesson names — today, the hearts a blend comes from.
        ///
        /// <para>
        /// A tile that is not drawn yet is dropped rather than passed on as a null, for the
        /// reason <see cref="TargetFor"/> may answer null at all: a lesson that cannot find
        /// what it points at still teaches, and a hole cut around nothing would be a hole in
        /// the corner of the board.
        /// </para>
        /// </summary>
        RectTransform[] AlongsideFor(MechanicSighting sighting)
        {
            if (_board == null || sighting.Alongside.Length == 0) return null;

            var found = new System.Collections.Generic.List<RectTransform>(sighting.Alongside.Length);

            foreach (int cell in sighting.Alongside)
            {
                var tile = _board.TileAt(cell);
                if (tile) found.Add(tile);
            }

            return found.Count > 0 ? found.ToArray() : null;
        }

        /// <summary>
        /// A lesson may go up whenever the board is taking input — the same reading the undo
        /// and hint keys are drawn against, so the three cannot disagree about whether the
        /// board is busy.
        /// </summary>
        protected internal override bool Teachable => _board != null && _board.Accepting && !_finished;

        /// <summary>
        /// Holds the board while a lesson is up.
        ///
        /// <para>
        /// The lock is still worth taking even though the tip covers the whole screen and eats
        /// every tap: it is what the bottom bar reads to grey its own buttons, so without it a
        /// board being taught draws an undo and a hint that look live. What it is <em>not</em>
        /// any more is the thing holding the clock — the intro sweep unlatches this from a tween
        /// scheduled before the tip existed, which is exactly how the countdown used to end up
        /// running behind a lesson. See <see cref="RunScreen.Hold"/>.
        /// </para>
        /// </summary>
        protected internal override void Latch(bool latched)
        {
            if (_board == null) return;

            // A run that ended while a lesson was up keeps its locked board. Nothing can end
            // one here today, and that is a fact about today's panels rather than a rule.
            if (!latched && _finished) return;

            _board.Locked = latched;
        }

        /// <summary>
        /// Where to point for a rule that lives in the HUD rather than on the board.
        ///
        /// The move budget is the case that matters: a bubble floating in the middle of
        /// the screen saying "the counter at the top" makes the player hunt for the
        /// thing being described. Ringing the actual pill removes the hunt.
        ///
        /// Resolved here rather than in the scan because the scan is Domain and knows
        /// nothing about pills — it reports the mechanic, the screen knows where it is
        /// drawn.
        /// </summary>
        RectTransform HudTargetFor(Mechanic mechanic)
        {
            // The pill background, not the label inside it, so the ring frames the whole
            // readout rather than a few digits.
            if (mechanic.Equals(Mechanic.MoveBudget) && _moves)
                return _moves.transform.parent as RectTransform;

            return null;
        }

        public override bool OnBack()
        {
            if (_finished) return false;
            Pause();
            return true;
        }
    }
}
