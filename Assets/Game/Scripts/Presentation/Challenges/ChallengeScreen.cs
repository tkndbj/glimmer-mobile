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
    /// record, no stars, no hearts, no continue, no XP, no credits and no lesson, so it goes
    /// through none of <c>RunScreen</c>, <c>ProtoScreen</c> or the reward path — by the owner's
    /// instruction that tuning a challenge must never move the core game. What it shares is
    /// the art (<see cref="ChallengeArt"/>), the kit and the flow.
    /// </para>
    /// <para>
    /// <b>One door for every input.</b> A view hands its input to <see cref="Play"/>, which
    /// asks the run, then animates the puzzle's answer and replays the hill's, with the board
    /// latched until both have landed. A refused input shakes and costs nothing.
    /// </para>
    /// <para>
    /// <b>It stores nothing.</b> Leaving forfeits the run silently — there is nothing to lose
    /// but the run, and the way back in is one tap — so this is not a fourth confirmation.
    /// </para>
    /// </summary>
    public sealed class ChallengeScreen : View
    {
        /// <summary>Set by the list before <c>Build</c>. The challenge to play.</summary>
        public string Id;

        public override string Track => "mus_menu";

        const float ChromeSize = 92f;
        const float BannerW = 620f, BannerH = 112f;
        const int BannerSize = 34;

        /// <summary>The readout row under the ribbon, and the air above the hill.</summary>
        const float ReadoutH = 56f, ReadoutGap = 12f;

        /// <summary>The hill's share of the room, bounded so a tall phone does not make a walk of it.</summary>
        const float HillShare = .27f, HillLeast = 330f, HillMost = 520f;

        /// <summary>The line band, in units of the siege's cell (see <see cref="Unit"/>).</summary>
        const float LineBandUnits = 2.3f;

        const float BottomPad = 36f, BandGap = 14f;

        AssetHold _hold;
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
            var def = ChallengeRules.Table.Find(Id);
            if (def == null)
            {
                Debug.LogError($"[Challenges] no challenge '{Id}'; back to the list");
                Flow.Go<DailyChallengesScreen>();
                yield break;
            }

            _run = new ChallengeRun(def, ChallengeRules.Table.Line);

            var task = Warm();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            if (!this) yield break;

            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 10, Pal.A(Pal.Gold, .9f), 4f, 14f);

            BuildChrome(def);

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
            float hill = Mathf.Clamp(room * HillShare, HillLeast, HillMost);

            _hillHost = UIKit.Node("HillHost", Safe);
            _hillHost.anchorMin = new Vector2(0f, 1f);
            _hillHost.anchorMax = new Vector2(1f, 1f);
            _hillHost.pivot = new Vector2(.5f, 1f);
            _hillHost.offsetMin = new Vector2(0f, -(top + hill + lineBand));
            _hillHost.offsetMax = new Vector2(0f, -top);

            _puzzleHost = UIKit.Node("PuzzleHost", Safe);
            _puzzleHost.anchorMin = new Vector2(0f, 0f);
            _puzzleHost.anchorMax = new Vector2(1f, 1f);
            _puzzleHost.offsetMin = new Vector2(24f, BottomPad);
            _puzzleHost.offsetMax = new Vector2(-24f, -(top + hill + lineBand + BandGap));

            Canvas.ForceUpdateCanvases();

            _hill = _hillHost.gameObject.AddComponent<ChallengeHillView>();
            _hill.Build(_hillHost, _run.Hill, unit, lineBand);

            _puzzle = Make(_run.Puzzle.Genre);
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
        void End(bool won)
        {
            if (_ended) return;
            _ended = true;

            StartCoroutine(Curtain(won));
        }

        IEnumerator Curtain(bool won)
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

            UIKit.Halo(safe, won ? Pal.Gold : Pal.Rose, 760f, .30f, new Vector2(0f, 220f));

            var line = UIKit.Titled("Line", safe, Loc.Get(won ? "ui.challenges.won" : "ui.challenges.lost"), 60,
                                    Pal.Cream, TextAnchor.MiddleCenter, new Vector2(880f, 200f),
                                    new Vector2(.5f, .5f), new Vector2(0f, 220f), 3f, 5f, wrap: true);
            UIKit.Shrinkable(line, 32);
            line.transform.localScale = Vector3.zero;
            Tween.Pop(line.transform, 0f, .5f);

            var turns = UIKit.Titled("Turns", safe, Loc.Format("ui.challenges.turns", _run.Turns), 30,
                                     Pal.A(Pal.Cream, .85f), TextAnchor.MiddleCenter, new Vector2(600f, 60f),
                                     new Vector2(.5f, .5f), new Vector2(0f, 90f), 2f, 2f);

            var retry = UIKit.TextButton("Retry", safe, Skins.Affirm, Loc.Get("ui.challenges.retry").ToUpperInvariant(),
                                         32, new Vector2(460f, 118f), new Vector2(.5f, .5f), new Vector2(0f, -50f),
                                         () => Flow.Go<ChallengeScreen>(v => v.Id = Id));
            var done = UIKit.TextButton("Done", safe, Skins.Alternate, Loc.Get("ui.challenges.done").ToUpperInvariant(),
                                        32, new Vector2(460f, 118f), new Vector2(.5f, .5f), new Vector2(0f, -190f), Leave);

            foreach (var key in new[] { retry, done })
            {
                var rt = (RectTransform)key.transform;
                var group = UIKit.Group(rt);
                group.alpha = 0f;
                Tween.Fade(group, 1f, .3f).Delay(.22f);
            }

            if (scrim != null) scrim.raycastTarget = true;
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
