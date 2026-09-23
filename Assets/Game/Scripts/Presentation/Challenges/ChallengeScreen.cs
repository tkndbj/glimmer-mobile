using System.Collections;
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
    /// <b>Opened by genre, dealt by the ledger.</b> The list says which genre; which level that
    /// is today, and whether a play is left, is <see cref="ChallengeLedger.Begin"/>'s answer —
    /// so a screen that is somehow reached with no play left goes straight back to the list,
    /// and a screen that is reached is one that has spent the play (invariant 56h: spent at the
    /// deal, never at the ending).
    /// </para>
    /// <para>
    /// <b>One door for every input.</b> A view hands its input to <see cref="Play"/>, which
    /// asks the run, then animates the puzzle's answer and replays the hill's, with the board
    /// latched until both have landed. A refused input shakes and costs nothing.
    /// </para>
    /// <para>
    /// <b>Leaving forfeits the run silently</b> — the play is already spent, the way a run that
    /// is quit is already paid for, and the way back in is one tap — so this is not a fourth
    /// confirmation.
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
        /// </summary>
        const float HillLeastUnits = 3.6f, HillMostUnits = 5.4f;

        /// <summary>
        /// The line band, in units of the siege's cell. Sized to the <em>post</em>
        /// (<see cref="ChallengeHillView.PostScale"/>), which is drawn smaller than a siege
        /// turret because it stands under a hill rather than a match-three board.
        /// </summary>
        const float LineBandUnits = 1.7f;

        /// <summary>The puzzle host's inset from the safe edge, either side.</summary>
        const float PuzzleInset = 24f;

        const float BottomPad = 36f, BandGap = 14f;

        AssetHold _hold;
        ChallengePlay _play;
        ChallengeRun _run;
        ChallengeHillView _hill;
        PuzzleView _puzzle;
        Text _turn, _goal, _next;
        RectTransform _hillHost, _puzzleHost;
        bool _ready, _busy, _ended;

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

            _hill = _hillHost.gameObject.AddComponent<ChallengeHillView>();
            _hill.Build(_hillHost, _run.Hill, unit, lineBand);

            _puzzle.Attach(_run, _puzzleHost, Play);

            var group = UIKit.Group(_puzzleHost);
            group.alpha = 0f;
            _puzzleHost.localScale = Vector3.one * .92f;
            Tween.Fade(group, 1f, .34f);
            Tween.Scale(_puzzleHost, 1f, .46f, Ease.OutBack);
        }

        /// <summary>The genre's view. A <c>switch</c> whose default refuses (invariant 44e).</summary>
        PuzzleView Make(ChallengeGenre genre)
        {
            switch (genre)
            {
                case ChallengeGenre.Pairs: return _puzzleHost.gameObject.AddComponent<PairsView>();
                case ChallengeGenre.Pipes: return _puzzleHost.gameObject.AddComponent<PipesView>();
                case ChallengeGenre.Merge: return _puzzleHost.gameObject.AddComponent<MergeView>();
                case ChallengeGenre.Sokoban: return _puzzleHost.gameObject.AddComponent<SokobanView>();
                default:
                    throw new System.InvalidOperationException($"genre '{genre}' has no view");
            }
        }

        // ------------------------------------------------------------------ playing
        void Play(ChallengeInput input)
        {
            if (!_ready || _busy || _ended || _run.State != ChallengeState.Playing) return;

            var report = _run.Play(input);
            if (report.Move == null) return;

            if (report.Move.Refused)
            {
                _puzzle.Refuse();
                return;
            }

            StartCoroutine(Resolve(report));
        }

        IEnumerator Resolve(ChallengeTurnReport report)
        {
            _busy = true;

            yield return _puzzle.Animate(report.Move);
            if (!this) yield break;

            if (report.Walked)
            {
                yield return _hill.Replay(report.Events);
                if (!this) yield break;
            }

            PaintReadout();
            _busy = false;

            if (report.State != ChallengeState.Playing) End(report.State == ChallengeState.Won);
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
                case PipesPuzzle pipes: return Loc.Format("ui.challenges.pipes", pipes.Joined, pipes.Asked);
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

        IEnumerator Curtain(bool won, ChallengeReward reward)
        {
            yield return new WaitForSecondsRealtime(.4f);
            if (!this) yield break;

            if (won)
            {
                Flow.Flash(Pal.Gold, .34f, .55f);
                Burst.Confetti(Content, 60);
                Audio.Sfx("win", .8f);
            }
            else
            {
                Flow.Flash(Pal.Rose, .22f, .5f);
                Audio.Sfx("felled", .7f);
            }

            var scrim = UIKit.Scrim(Content, .62f);
            var safe = SafeArea.Node("Ending", Content);

            UIKit.Halo(safe, won ? Pal.Gold : Pal.Rose, 760f, .30f, new Vector2(0f, 250f));

            var line = UIKit.Titled("Line", safe, Loc.Get(won ? "ui.challenges.won" : "ui.challenges.lost"), 60,
                                    Pal.Cream, TextAnchor.MiddleCenter, new Vector2(880f, 200f),
                                    new Vector2(.5f, .5f), new Vector2(0f, 250f), 3f, 5f, wrap: true);
            UIKit.Shrinkable(line, 32);
            line.transform.localScale = Vector3.zero;
            Tween.Pop(line.transform, 0f, .5f);

            UIKit.Titled("Turns", safe, Loc.Format("ui.challenges.turns", _run.Turns), 30,
                         Pal.A(Pal.Cream, .85f), TextAnchor.MiddleCenter, new Vector2(600f, 60f),
                         new Vector2(.5f, .5f), new Vector2(0f, 130f), 2f, 2f);

            // What the win paid, said as a picture and a sentence rather than as a bullet point
            // (45h): coins and XP, with the boost's share beside the XP when one is running.
            if (won && reward.Any)
            {
                string paid = reward.BonusXp > 0
                            ? Loc.Format("ui.challenges.reward_boost", reward.Coins, reward.Xp, reward.BonusXp)
                            : Loc.Format("ui.challenges.reward", reward.Coins, reward.Xp);
                var pill = Scenery.Pill(safe, paid, 30, new Vector2(620f, 74f), new Vector2(.5f, .5f),
                                        new Vector2(0f, 50f), Pal.A(Pal.Gold, .22f));
                UIKit.Shrinkable(pill, 20);
                pill.transform.parent.localScale = Vector3.zero;
                Tween.Pop(pill.transform.parent, .12f, .5f);
            }

            // The keys say what the day still allows. A play left offers the next level on a
            // win and another go on a loss — both are the same deal, one more play — and none
            // left says so in words rather than swallowing the tap (invariant 16o's rule).
            int left = ChallengeLedger.PlaysLeft(Genre);
            bool again = left > 0 && ChallengeLedger.CanPlay(Genre);

            if (again)
            {
                string key = won ? "ui.challenges.next_level" : "ui.challenges.retry";
                var go = UIKit.TextButton("Again", safe, Skins.Affirm, Loc.Get(key).ToUpperInvariant(),
                                          32, new Vector2(460f, 118f), new Vector2(.5f, .5f), new Vector2(0f, -70f),
                                          () => Flow.Go<ChallengeScreen>(v => v.Genre = Genre));
                Enter(go);

                var count = UIKit.Titled("Left", safe, Loc.Format("ui.challenges.plays_left", left, ChallengeLedger.Allowance),
                                         24, Pal.A(Pal.Cream, .78f), TextAnchor.MiddleCenter, new Vector2(600f, 44f),
                                         new Vector2(.5f, .5f), new Vector2(0f, -150f), 2f, 0f);
                UIKit.Shrinkable(count, 16);
            }
            else
            {
                var spent = UIKit.Titled("Spent", safe, Loc.Get("ui.challenges.no_plays"), 28,
                                         Pal.A(Pal.Cream, .85f), TextAnchor.MiddleCenter, new Vector2(720f, 60f),
                                         new Vector2(.5f, .5f), new Vector2(0f, -70f), 2f, 2f, wrap: true);
                UIKit.Shrinkable(spent, 18);
            }

            var done = UIKit.TextButton("Done", safe, Skins.Alternate, Loc.Get("ui.challenges.done").ToUpperInvariant(),
                                        32, new Vector2(460f, 118f), new Vector2(.5f, .5f), new Vector2(0f, -220f), Leave);
            Enter(done);

            if (scrim != null) scrim.raycastTarget = true;
        }

        static void Enter(Btn key)
        {
            var rt = (RectTransform)key.transform;
            var group = UIKit.Group(rt);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .3f).Delay(.22f);
        }

        void Leave() => Flow.Go<DailyChallengesScreen>();

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
