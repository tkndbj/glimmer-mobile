using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Challenges;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// One daily challenge being played: the hill and the line above, the puzzle below. There
    /// is no readout row: the turn, goal and wave counters were taken off at the owner's
    /// instruction (2026-09-27) and the hill starts under the ribbon.
    ///
    /// <para>
    /// <b>A screen of its own, sharing the world and nothing about being a run</b> (MODES.md
    /// 20b, read the other way): a challenge is not a level. It has no <c>LevelId</c>, no
    /// record, no stars, no hearts, no continue and no lesson, so it goes through none of
    /// <c>RunScreen</c>, <c>ProtoScreen</c> or the reward path - by the owner's instruction that
    /// tuning a challenge must never move the core game. What it shares is the art
    /// (<see cref="ChallengeArt"/>), the kit and the flow.
    /// </para>
    /// <para>
    /// <b>Opened by genre, dealt by the ledger, spent at the first move.</b> The list says
    /// which genre; which level that is today, and whether a play is left, is
    /// <see cref="ChallengeLedger.Begin"/>'s answer - so a screen that is somehow reached
    /// with no play left goes straight back to the list. The play itself is taken by
    /// <see cref="ChallengeLedger.Commit"/> on the first move the rules accept (invariant
    /// 56g), so a board opened, looked at and backed out of costs nothing and is dealt
    /// again untouched - the owner's instruction, 2026-09-26.
    /// </para>
    /// <para>
    /// <b>One door for every input, and nothing behind it is thrown away.</b> A view hands
    /// its input to <see cref="Play"/>, which asks the run, animates the puzzle's answer and
    /// <em>queues</em> the hill's (<see cref="ChallengeHillView.Enqueue"/>): the hill draws
    /// its turns on its own and hurries when it falls behind, so the board is never latched
    /// for a walk it is not part of. The board is latched only for its own landing, and an
    /// input made inside that is <b>held, not dropped</b> - one deep, the latest wins - and
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
    /// <b>A genre is taught by one preview, not by tip boxes</b> (<see cref="ChallengePreviewOverlay"/>,
    /// the owner's instruction on 2026-10-02): a first visit to a genre raises its demonstration
    /// once the board has landed, the info key raises it again on request, and nothing is taught
    /// at an event. The board is latched while the panel is up, exactly as it is while a move is
    /// landing. The gate is the genre's verb lesson in the tip ledger (invariant 56s).
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

        /// <summary>The air between the ribbon and the hill.</summary>
        const float RibbonGap = 12f;

        /// <summary>
        /// The hill's bounds, in units of the siege's cell (see <see cref="Unit"/>). <b>The
        /// board is asked first and the hill takes the rest</b>: a board is laid out at the
        /// widest cell the safe width allows, so a wide, short board leaves the hill most of
        /// the screen, and the hill is bounded so a tall phone does not make a walk of it and
        /// a short one cannot squeeze it to a strip - below the floor it is the <em>board</em>
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
        /// ground (<see cref="Ground"/>) - the owner's instruction on 2026-09-23: "make the
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
        RectTransform _hillHost, _puzzleHost;
        bool _ready, _busy, _ended, _teaching;

        /// <summary>
        /// The inputs held while the board lands its last (the class note), oldest first and
        /// never more than the board asks for (<see cref="PuzzleView.InputsHeld"/>).
        /// </summary>
        readonly Queue<ChallengeInput> _pending = new Queue<ChallengeInput>(2);

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

            _run = new ChallengeRun(_play.Definition, ChallengeRules.Table.Line, _play.Deal);

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

            StartCoroutine(Opening());
        }

        /// <summary>The longest the opening preview waits on a board's own entrance, in seconds.</summary>
        const float LandedMost = 2f;

        /// <summary>
        /// A first-timer's preview, after the board's entrance has landed - a panel going up
        /// over a board still sweeping in reads as the board being interrupted. Raised only
        /// while the genre's verb has not been seen, which is the gate the old tip box kept
        /// (<see cref="ChallengePreviewOverlay"/>), so a player who already read that box is
        /// never shown this unasked.
        /// </summary>
        IEnumerator Opening()
        {
            yield return new WaitForSecondsRealtime(.6f);

            // A board with an entrance of its own (the glade's sweep, which grows with its
            // width) is waited on, never for longer than a board has any reason to take.
            float until = Time.unscaledTime + LandedMost;
            while (this && !_ended && !_puzzle.Landed && Time.unscaledTime < until) yield return null;
            if (!this || _ended) yield break;

            // A player who opened the preview from the key before this arrived has seen it;
            // what they dismissed is marked seen, so the gate below holds it back.
            while (this && _teaching) yield return null;
            if (!this || _ended) yield break;

            if (TipLedger.HasSeen(Mechanic.ChallengeVerb(Genre))) yield break;

            Preview();
        }

        /// <summary>
        /// The genre's demonstration, over a latched board. One door for the opening and the
        /// info key, so the two cannot disagree about the latch; <c>Flow.Modal</c> refuses a
        /// second while one is up, and the latch is let go however the panel leaves.
        /// </summary>
        void Preview()
        {
            _teaching = true;

            Flow.Modal<ChallengePreviewOverlay>(v =>
            {
                v.Genre = Genre;
                v.Dismissed = () => { if (this) _teaching = false; };
            });
        }

        System.Threading.Tasks.Task Warm()
        {
            _hold = _hold ?? AssetLibrary.Hold(ChallengeArt.Hold);
            return _hold.LoadAsync(ChallengeArt.Requests(_play.Genre), null, Lifetime);
        }

        void BuildChrome(ChallengeDefinition def)
        {
            float cy = -(22f + BannerH * .5f);

            UIKit.IconButton("Back", Safe, Skins.Nav, "ic_left", Vector2.one * ChromeSize,
                             new Vector2(0f, 1f), new Vector2(76f, cy), Leave);

            // The genre's tips on demand, mirroring the way out (the owner, 2026-09-27).
            UIKit.IconButton("Info", Safe, Skins.Aside, "ic_info", Vector2.one * ChromeSize,
                             new Vector2(1f, 1f), new Vector2(-76f, cy), Review);

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get(def.NameKey).Upper(),
                                             new Vector2(BannerW, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), BannerSize);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);
        }

        void BuildBands()
        {
            float top = 22f + BannerH + RibbonGap;
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
            // fly at and a lesson to ring.
            _puzzle.PostOf = _hill.PostNode;
            _puzzle.FedPost = _hill.Fed;

            _puzzle.Attach(_run, _puzzleHost, Play);

            var group = UIKit.Group(_puzzleHost);
            group.alpha = 0f;
            _puzzleHost.localScale = Vector3.one * .92f;
            Tween.Fade(group, 1f, .34f);
            Tween.Scale(_puzzleHost, 1f, .46f, Ease.OutBack);
        }

        /// <summary>
        /// The opaque ground under the puzzle band: full width, from the band's top edge to
        /// the foot of the canvas - past the safe area's bottom inset, so a home indicator sits
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
                // frame the board is free. As deep as the board asks and no deeper, latest
                // wins - one for a swipe, whose older intention the newest replaces; two for
                // Pairs, where a quick player turns two cards while a match is still landing
                // and dropping the first would be a tap that did not register.
                int deep = Mathf.Max(1, _puzzle.InputsHeld);
                while (_pending.Count >= deep) _pending.Dequeue();
                _pending.Enqueue(input);

                // A board holding something up to be looked at (a Pairs miss) lets go early:
                // the player has seen it and moved on, so the peek ends at their pace.
                _puzzle.Hurry();
                return;
            }

            var report = _run.Play(input);
            if (report.Move == null) return;

            if (report.Move.Refused)
            {
                _puzzle.Refuse();
                return;
            }

            // The first move the rules accepted is the play being spent - written before
            // anything is drawn, so a process killed on the board finds it spent on relaunch.
            ChallengeLedger.Commit(_play);

            StartCoroutine(Resolve(report));
        }

        /// <summary>
        /// Play the held inputs, oldest first, until one keeps the board busy. A refused one
        /// (a tap on a card a held tap already turned) costs nothing and must not strand the
        /// taps behind it, so the loop goes on past it; <see cref="Resolve"/> marks the board
        /// busy before its first yield, which is what stops it.
        /// </summary>
        void Flush()
        {
            while (_pending.Count > 0 && !_busy && !_ended && _run.State == ChallengeState.Playing)
                Play(_pending.Dequeue());
        }

        IEnumerator Resolve(ChallengeTurnReport report)
        {
            _busy = true;

            yield return _puzzle.Animate(report.Move);
            if (!this) yield break;

            // The hill is queued, never awaited: it draws this turn after whatever it is
            // still drawing, and the board is free to take the next move meanwhile.
            if (report.Walked) _hill.Enqueue(report.Events);

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
            _pending.Clear();
            while (this && !_hill.Idle) yield return null;
            if (!this) yield break;

            End(won);
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The run is over. <b>The ledger is told before anything is drawn</b>, so a process
        /// killed during the curtain has still paid - a win is a claim in the save and a tally
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
        /// panel - the victory design over what the clear paid
        /// (<see cref="ChallengeWinOverlay"/>), or the defeat design with the line's reason and
        /// no hearts (<see cref="ChallengeDefeatOverlay"/>) - by the owner's instruction on
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

        /// <summary>
        /// The info key: the genre's preview again, whether or not it has been seen - the
        /// loadout's key and for its reason. The board is latched exactly as it is for the
        /// opening preview, and the key is refused while a move is landing, a panel is already
        /// up or the run is over, so two can never be up at once.
        /// </summary>
        void Review()
        {
            if (!this || !_ready || _busy || _teaching || _ended || _asking || Flow.HasModal) return;
            if (_run.State != ChallengeState.Playing) return;

            Preview();
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
