using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One daily challenge being played: the hill and the line above, the puzzle below, and a
    /// readout between them.
    ///
    /// <para>
    /// <b>A screen of its own, sharing the world and nothing about being a run</b> (MODES.md
    /// 20b, read the other way): a challenge is not a level. It has no <c>LevelId</c>, no
    /// record, no stars, no hearts, no continue and no lesson, so it goes through none of
    /// <c>RunScreen</c>, <c>ProtoScreen</c> or the reward path — by the owner's instruction that
    /// tuning a challenge must never move the core game. What it shares is the art
    /// (<see cref="ChallengeArt"/>), the kit and the flow.
    /// </para>
    /// <para>
    /// <b>Opened by genre, dealt by the ledger, spent at the first move.</b> The list says
    /// which genre; which level that is today, and whether a play is left, is
    /// <see cref="ChallengeLedger.Begin"/>'s answer — so a screen that is somehow reached
    /// with no play left goes straight back to the list. The play itself is taken by
    /// <see cref="ChallengeLedger.Commit"/> on the first move the rules accept (invariant
    /// 56g), so a board opened, looked at and backed out of costs nothing and is dealt
    /// again untouched — the owner's instruction, 2026-09-26.
    /// </para>
    /// <para>
    /// <b>One door for every input, and nothing behind it is thrown away.</b> A view hands
    /// its input to <see cref="Play"/>, which asks the run, animates the puzzle's answer and
    /// <em>queues</em> the hill's (<see cref="ChallengeHillView.Enqueue"/>): the hill draws
    /// its turns on its own and hurries when it falls behind, so the board is never latched
    /// for a walk it is not part of. The board is latched only for its own landing, and an
    /// input made inside that is <b>held, not dropped</b> — one deep, the latest wins — and
    /// played the frame the board is free. The first cut latched for both and refused every
    /// tap inside the second they took, which on the glade was reported as conduits that
    /// "sometimes don't rotate" (2026-09-26). A refused input shakes and costs nothing.
    /// </para>
    /// <para>
    /// <b>Leaving a board that has been moved on asks first, through the run's own
    /// confirmation</b> (<see cref="ForfeitOverlay"/> with a play on the price tag rather
    /// than a heart), because the play is spent and walking out is the one thing here a
    /// player could want back. Leaving an untouched board asks nothing, for the reason that
    /// overlay gives: a confirmation over a free exit teaches a player to tap through the
    /// one that costs something. The count of confirmations in this game is still three.
    /// </para>
    /// <para>
    /// <b>A board declares its lessons and <c>ScreenLessons</c> sequences them</b> (invariant
    /// 6a). Two moments: the opening, once the entrance has landed, and the beat after a move
    /// has been drawn and before the hill replays it, so a lesson taught at an event rings a
    /// thing that has just happened (6b). The board is latched while a panel is up, exactly as
    /// it is while a move is landing.
    /// </para>
    /// </summary>
    public sealed class ChallengeScreen : View
    {
        /// <summary>Set by the list before <c>Build</c>. The genre to deal.</summary>
        public ChallengeGenre Genre;

        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerW = 620f, BannerH = 112f;
        const int BannerSize = 34;

        /// <summary>The readout row under the ribbon, and the air above the hill.</summary>
        const float ReadoutH = 56f, ReadoutGap = 12f;

        /// <summary>
        /// The hill's bounds, in units of the siege's cell (see <see cref="Unit"/>). <b>The
        /// board is asked first and the hill takes the rest</b>: a board is laid out at the
        /// widest cell the safe width allows, so a wide, short board leaves the hill most of
        /// the screen, and the hill is bounded so a tall phone does not make a walk of it and
        /// a short one cannot squeeze it to a strip — below the floor it is the <em>board</em>
        /// that shrinks its cells. The owner's reading of the first cut (2026-09-23) was
        /// "the hills are too small": at a fixed share of the room the hill was 3.3 cells
        /// against a 4x4 board given 6.6, and the four posts stood two cells tall over it.
        /// <b>The ceiling went from 5.4 to 8 on 2026-09-26</b>, because at 5.4 a 19.5:9 phone
        /// had four cells of room the hill was refused, and they landed as a bare slab between
        /// the line and the board (the owner's red circle). Whatever a phone has over eight
        /// goes into the board's frame rather than into a gap (<see cref="PuzzleView"/>).
        /// </summary>
        const float HillLeastUnits = 3.6f, HillMostUnits = 8.0f;

        /// <summary>
        /// The line band, in units of the siege's cell. Sized to the <em>post</em>
        /// (<see cref="ChallengeHillView.PostScale"/>), which is drawn smaller than a siege
        /// turret because it stands under a hill rather than a match-three board.
        /// </summary>
        const float LineBandUnits = 1.7f;

        /// <summary>
        /// The puzzle band runs edge to edge and to the foot of the screen, on an opaque
        /// ground (<see cref="Ground"/>) — the owner's instruction on 2026-09-23: "make the
        /// board cover the full area and make it non-transparent". So there is no inset, no
        /// pad under it and no gap between it and the line; the board is centred in the whole
        /// of what the hill leaves, and the brick backdrop stops at the rampart.
        /// </summary>
        const float PuzzleInset = 0f, BottomPad = 0f, BandGap = 0f;

        /// <summary>The ground under the puzzle: the first chapter's slate, opaque.</summary>
        public static readonly Color Ground = new Color(.059f, .165f, .290f, 1f);

        AssetHold _hold;
        ChallengePlay _play;
        ChallengeRun _run;
        ChallengeHillView _hill;
        PuzzleView _puzzle;
        Text _turn, _goal, _next;
        RectTransform _hillHost, _puzzleHost;
        bool _ready, _busy, _ended, _teaching;

        /// <summary>The one input held while the board lands its last (the class note).</summary>
        ChallengeInput _pending;
        bool _holding;

        /// <summary>Whether the leave confirmation is up. The board takes nothing under it.</summary>
        bool _asking;

        public override bool Ready => _ready;

        /// <summary>The siege's cell: an eighth of the safe width, which is what its posts are sized off.</summary>
        float Unit => Safe.rect.width / 8f;

        protected override void Build() => StartCoroutine(Raise());

        IEnumerator Raise()
        {
            _play = ChallengeLedger.Begin(Genre);
            if (_play == null)
            {
                Debug.LogWarning($"[Challenges] no play of '{ChallengeGenres.NameOf(Genre)}' left today; back to the list");
                Flow.Go<DailyChallengesScreen>();
                yield break;
            }

            _run = new ChallengeRun(_play.Definition, ChallengeRules.Table.Line);

            var task = Warm();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            if (!this) yield break;

            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 10, Pal.A(Pal.Gold, .9f), 4f, 14f);

            BuildChrome(_play.Definition);

            yield return null;
            Canvas.ForceUpdateCanvases();

            int guard = 0;
            while (Safe.rect.width < 40f && guard++ < 60) yield return null;
            if (!this) yield break;

            BuildBands();

            _ready = true;
            PaintReadout();

            StartCoroutine(Opening());
        }

        /// <summary>
        /// The board's opening lessons, after its entrance has landed - a ring drawn round a
        /// plate still springing up is a ring round the wrong rectangle.
        /// </summary>
        IEnumerator Opening()
        {
            yield return new WaitForSecondsRealtime(.6f);
            if (!this || _ended) yield break;

            var lessons = new List<ScreenLesson>(2);
            _puzzle.Lessons(lessons);
            if (lessons.Count == 0) yield break;

            _teaching = true;
            ScreenLessons.Show(this, lessons, () => _teaching = false);
        }

        System.Threading.Tasks.Task Warm()
        {
            _hold = _hold ?? AssetLibrary.Hold(ChallengeArt.Hold);
            return _hold.LoadAsync(ChallengeArt.Requests(), null, Lifetime);
        }

        void BuildChrome(ChallengeDefinition def)
        {
            float cy = -(22f + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy), Leave);

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get(def.NameKey).ToUpperInvariant(),
                                             new Vector2(BannerW, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), BannerSize);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);

            float ry = cy - BannerH * .5f - ReadoutGap - ReadoutH * .5f;
            var size = new Vector2(300f, ReadoutH);

            _turn = Scenery.Pill(Safe, string.Empty, 24, size, new Vector2(0f, 1f), new Vector2(40f + size.x * .5f, ry));
            _goal = Scenery.Pill(Safe, string.Empty, 24, new Vector2(360f, ReadoutH), new Vector2(.5f, 1f), new Vector2(0f, ry));
            _next = Scenery.Pill(Safe, string.Empty, 24, size, new Vector2(1f, 1f), new Vector2(-(40f + size.x * .5f), ry));
        }

        void BuildBands()
        {
            float top = 22f + BannerH + ReadoutGap + ReadoutH + ReadoutGap;
            float room = Safe.rect.height - top - BottomPad;

            float unit = Unit;
            float lineBand = unit * LineBandUnits;

            // The board first: made, and asked what it wants at the width it will be given.
            _puzzleHost = UIKit.Node("PuzzleHost", Safe);
            _puzzleHost.anchorMin = new Vector2(0f, 0f);
            _puzzleHost.anchorMax = new Vector2(1f, 1f);
            _puzzle = Make(_run.Puzzle.Genre);

            float want = _puzzle.BandWanted(_run.Puzzle.Width, _run.Puzzle.Height, Safe.rect.width - PuzzleInset * 2f);
            float hill = Mathf.Clamp(room - lineBand - BandGap - want, unit * HillLeastUnits, unit * HillMostUnits);

            _hillHost = UIKit.Node("HillHost", Safe);
            _hillHost.anchorMin = new Vector2(0f, 1f);
            _hillHost.anchorMax = new Vector2(1f, 1f);
            _hillHost.pivot = new Vector2(.5f, 1f);
            _hillHost.offsetMin = new Vector2(0f, -(top + hill + lineBand));
            _hillHost.offsetMax = new Vector2(0f, -top);
            _hillHost.SetSiblingIndex(_puzzleHost.GetSiblingIndex());

            _puzzleHost.offsetMin = new Vector2(PuzzleInset, BottomPad);
            _puzzleHost.offsetMax = new Vector2(-PuzzleInset, -(top + hill + lineBand + BandGap));

            Canvas.ForceUpdateCanvases();

            GroundUnder(_puzzleHost);

            _hill = _hillHost.gameObject.AddComponent<ChallengeHillView>();
            _hill.Build(_hillHost, _run.Hill, unit, lineBand);

            // The board's one-way view of the line: where a colour's post is, for a feed to
            // fly at and a lesson to ring, and the readout a goal lesson rings.
            _puzzle.PostOf = _hill.PostNode;
            _puzzle.FedPost = _hill.Fed;
            _puzzle.GoalReadout = _goal != null ? _goal.transform.parent as RectTransform : null;

            _puzzle.Attach(_run, _puzzleHost, Play);

            var group = UIKit.Group(_puzzleHost);
            group.alpha = 0f;
            _puzzleHost.localScale = Vector3.one * .92f;
            Tween.Fade(group, 1f, .34f);
            Tween.Scale(_puzzleHost, 1f, .46f, Ease.OutBack);
        }

        /// <summary>
        /// The opaque ground under the puzzle band: full width, from the band's top edge to
        /// the foot of the canvas — past the safe area's bottom inset, so a home indicator sits
        /// on ground and not on brick. It goes into <c>Content</c> (which is full-bleed)
        /// directly under the safe layer, so it covers the scenery and nothing else.
        /// </summary>
        void GroundUnder(RectTransform host)
        {
            var corners = new Vector3[4];
            host.GetWorldCorners(corners);
            float top = Content.InverseTransformPoint(corners[1]).y;

            var ground = UIKit.Img("Ground", Content, Art.Pixel, Ground);
            ground.raycastTarget = false;
            var rt = ground.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, Mathf.Max(0f, top - Content.rect.yMin));

            if (Safe.parent == Content) rt.SetSiblingIndex(Safe.GetSiblingIndex());
        }

        /// <summary>The genre's view. A <c>switch</c> whose default refuses (invariant 44e).</summary>
        PuzzleView Make(ChallengeGenre genre)
        {
            switch (genre)
            {
                case ChallengeGenre.Pairs: return _puzzleHost.gameObject.AddComponent<PairsView>();
                case ChallengeGenre.Glade: return _puzzleHost.gameObject.AddComponent<GladeView>();
                case ChallengeGenre.Merge: return _puzzleHost.gameObject.AddComponent<MergeView>();
                case ChallengeGenre.Sokoban: return _puzzleHost.gameObject.AddComponent<SokobanView>();
                default:
                    throw new System.InvalidOperationException($"genre '{genre}' has no view");
            }
        }

        // ------------------------------------------------------------------ playing
        void Play(ChallengeInput input)
        {
            if (!_ready || _teaching || _ended || _asking || _run.State != ChallengeState.Playing) return;

            if (_busy)
            {
                // The board is still landing its last move: keep this one and play it the
                // frame the board is free. One deep, latest wins — the model has not moved,
                // so the newest intention is the only one worth keeping.
                _pending = input;
                _holding = true;
                return;
            }

            var report = _run.Play(input);
            if (report.Move == null) return;

            if (report.Move.Refused)
            {
                _puzzle.Refuse();
                return;
            }

            // The first move the rules accepted is the play being spent — written before
            // anything is drawn, so a process killed on the board finds it spent on relaunch.
            ChallengeLedger.Commit(_play);

            StartCoroutine(Resolve(report));
        }

        /// <summary>Play the held input, if any, now that the board is free.</summary>
        void Flush()
        {
            if (!_holding) return;
            _holding = false;
            var input = _pending;
            Play(input);
        }

        IEnumerator Resolve(ChallengeTurnReport report)
        {
            _busy = true;

            yield return _puzzle.Animate(report.Move);
            if (!this) yield break;

            // A lesson the move just made true goes up here, between the board landing and the
            // hill answering, so what it rings is what the move did and the bolt it bought is
            // watched after the sentence rather than under it.
            var lessons = new List<ScreenLesson>(1);
            _puzzle.LessonsAfter(report.Move, lessons);
            if (lessons.Count > 0)
            {
                bool waiting = true;
                _teaching = true;
                ScreenLessons.Show(this, lessons, () => { _teaching = false; waiting = false; });
                while (waiting && this) yield return null;
                if (!this) yield break;
            }

            // The hill is queued, never awaited: it draws this turn after whatever it is
            // still drawing, and the board is free to take the next move meanwhile.
            if (report.Walked) _hill.Enqueue(report.Events);

            PaintReadout();
            _busy = false;

            if (report.State != ChallengeState.Playing)
            {
                StartCoroutine(Ending(report.State == ChallengeState.Won));
                yield break;
            }

            Flush();
        }

        /// <summary>
        /// The run is decided; the curtain waits for the hill to finish drawing what decided
        /// it, so a line that fell is seen to fall before the panel says so. Input is already
        /// shut, because the run's own state refuses it.
        /// </summary>
        IEnumerator Ending(bool won)
        {
            _holding = false;
            while (this && !_hill.Idle) yield return null;
            if (!this) yield break;

            End(won);
        }

        void PaintReadout()
        {
            if (_run == null || _turn == null) return;

            _turn.text = Loc.Format("ui.challenges.turns", _run.Turns);
            _goal.text = Goal();

            var wave = _run.Hill.NextWave;
            _next.text = wave == null ? string.Empty : Loc.Format("ui.challenges.next", wave.Turn - _run.Turns);
            _next.transform.parent.gameObject.SetActive(wave != null);
        }

        /// <summary>What is left to do, in the genre's own count.</summary>
        string Goal()
        {
            switch (_run.Puzzle)
            {
                case PairsPuzzle pairs: return Loc.Format("ui.challenges.pairs", pairs.Matched, pairs.Pairs);
                case GladePuzzle glade: return Loc.Format("ui.challenges.lit", glade.LampsLit, glade.LampCount);
                case MergePuzzle merge: return Loc.Format("ui.challenges.rank", 1 << merge.Target);
                case SokobanPuzzle sokoban: return Loc.Format("ui.challenges.pads", sokoban.SeatedCount, sokoban.Pads);
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The run is over. <b>The ledger is told before anything is drawn</b>, so a process
        /// killed during the curtain has still paid — a win is a claim in the save and a tally
        /// moved, both persisted by the ledger's own save, and the curtain merely reports them.
        /// </summary>
        void End(bool won)
        {
            if (_ended) return;
            _ended = true;

            var reward = ChallengeReward.None;
            if (won) reward = ChallengeLedger.Win(_play);
            else ChallengeLedger.Lose(_play, _run.Turns);

            StartCoroutine(Curtain(won, reward));
        }

        /// <summary>
        /// A beat, the flash the board did not already give, and then the run's own ending
        /// panel — the victory design over what the clear paid
        /// (<see cref="ChallengeWinOverlay"/>), or the defeat design with the line's reason and
        /// no hearts (<see cref="ChallengeDefeatOverlay"/>) — by the owner's instruction on
        /// 2026-09-24. The boost's percentage is read here, at the moment the panel is raised,
        /// because the ledger banked the bonus at the win and only the window knows its rate.
        /// </summary>
        IEnumerator Curtain(bool won, ChallengeReward reward)
        {
            yield return new WaitForSecondsRealtime(.4f);
            if (!this) yield break;

            // A board that celebrated its own win (the glade's fanfare) gets no second one here:
            // a flash and confetti a second after the fanfare read as one celebration
            // stuttering rather than as two (BoardView.Celebrate's own note).
            if (won && !_puzzle.CelebratesItself)
            {
                Flow.Flash(Pal.Gold, .34f, .55f);
                Burst.Confetti(Content, 60);
                Audio.Sfx("win", .8f);
            }
            else if (!won)
            {
                Flow.Flash(Pal.Rose, .22f, .5f);
                Audio.Sfx("felled", .7f);
            }

            var genre = Genre;
            var level = _play.Definition;

            if (won)
            {
                int boost = reward.BonusXp > 0L ? GlimmerGrove.Progression.XpBoost.Percent : 0;
                Flow.Modal<ChallengeWinOverlay>(v =>
                {
                    v.Genre = genre;
                    v.Reward = reward;
                    v.BoostPercent = boost;
                    v.Level = level;
                });
            }
            else
            {
                Flow.Modal<ChallengeDefeatOverlay>(v => v.Genre = genre);
            }
        }

        /// <summary>
        /// The way out. A board nobody has moved on, or a run already decided, is left at
        /// once; a board with a spent play on it and a run still open is asked about first
        /// (the class note). While the question is up the board takes nothing.
        /// </summary>
        void Leave()
        {
            if (_asking) return;

            bool committed = _play != null && _play.Spent;
            bool open = _run != null && _run.State == ChallengeState.Playing && !_ended;
            if (!committed || !open)
            {
                Flow.Go<DailyChallengesScreen>();
                return;
            }

            _asking = true;
            Flow.Modal<ForfeitOverlay>(v =>
            {
                v.Choice = ForfeitOverlay.Kind.Leave;
                v.Stake = ForfeitOverlay.Stakes.Play;
                v.OnConfirm = () =>
                {
                    if (!this) return;
                    Telemetry.Track("challenge_left",
                                    "genre", ChallengeGenres.NameOf(Genre),
                                    "level", _play.Definition.Id,
                                    "turns", _run.Turns);
                    Flow.Go<DailyChallengesScreen>();
                };
                v.OnCancel = () => { if (this) _asking = false; };
            });
        }

        public override bool OnBack()
        {
            Leave();
            return true;
        }

        void OnDestroy()
        {
            _hold?.Dispose();
            _hold = null;
        }
    }
}
