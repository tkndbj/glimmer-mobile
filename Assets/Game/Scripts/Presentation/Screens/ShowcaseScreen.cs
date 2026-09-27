using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The advert: one siege played end to end by a drawn hand, on the biggest board the mode
    /// can put up, with nothing else on the screen.
    ///
    /// <para>
    /// <b>It is the real mode with a player beside it</b> — <see cref="TutorialScreen"/>'s
    /// shape, with the two panels replaced by a director. The board is <c>SiegeView</c> over
    /// <c>SiegeBoard</c>, dealt from <see cref="SiegeShowcase"/>; the hand is
    /// <see cref="ShowcaseHand"/>; every move the hand makes reaches the board through the door
    /// a finger reaches (<c>SiegeView.Swipe</c>, <c>TapWard</c>, <c>TapCog</c>, <c>TapBomb</c>,
    /// <c>TapHeap</c>), so nothing here can show a move the game would refuse. What decides
    /// the next move is <c>SiegeShowcase</c>'s model, which <c>ShowcaseTests</c> plays offline
    /// against the same board to prove the run wins with every turret standing.
    /// </para>
    /// <para>
    /// <b>No chrome, on purpose.</b> No title, no readouts, no utility bar, no back key, no
    /// caption on the hill (<c>SiegeView.Muted</c>) and no margin round the board: a recording
    /// wants the fight and nothing that would have to be cropped out. The hardware key leaves.
    /// <b>The ending is the run's own defeat</b>, at the owner's instruction after the first
    /// recording — the line goes, the board shakes and reddens, and the hand withdraws. A
    /// watcher is meant to feel the wall going. The board is tuned to lose
    /// (<c>SiegeShowcase.Tough</c>) and the fixture holds it to losing.
    /// </para>
    /// <para>
    /// <b>Reached from nowhere.</b> The CUSTOM keys that opened it (<c>Dev/ShowcaseDoor.cs</c>)
    /// were deleted after the recordings on 2026-09-27; the screen, the hand and the boards are
    /// inert, kept as the harness a future advert is re-cut from.
    /// </para>
    /// </summary>
    public sealed class ShowcaseScreen : View
    {
        /// <summary>
        /// How far the board sits from the screen's edges: nothing.
        ///
        /// <b>Edge to edge, on the whole display rather than inside the safe area</b>, at the
        /// owner's instruction after the first recording: the board fills its host
        /// (<c>SiegeView.Span</c> is the room less the plate's own margin), so a host the size
        /// of the display is a hill, a line and a field with no sky showing round them.
        /// </summary>
        static readonly Vector4 HostInset = Vector4.zero;

        /// <summary>A beat after the board has arrived before the first move.</summary>
        const float FirstBeat = 1.4f;

        /// <summary>
        /// The thinking time between moves at rest, and the least of it under pressure.
        ///
        /// <para>
        /// A person does not swap the instant the field settles; they look. A second when the
        /// hill is quiet, half of that when it is crowded or a boss is standing, and a seeded
        /// jitter on top so no two moves are the same length (<see cref="Pace"/>).
        /// </para>
        /// <para>
        /// <b>Public because the fixture plays by them.</b> <c>ShowcaseTests</c> hurries its
        /// rhythm by <c>Hurry / Think</c> while the boss stands, which is this rule and not a
        /// copy of it: the colossus buries turrets faster than an unhurried line digs them out,
        /// so a recording whose hand did not quicken for the boss would stall the fight and
        /// lose a turret — measured, not argued, and the reason the two are one constant.
        /// </para>
        /// </summary>
        public const float Think = 1.4f, Hurry = .9f;

        /// <summary>How long the drag itself takes, and where along it the board hears it.</summary>
        const float DragFor = .40f, DragFires = .55f;

        /// <summary>A beat after a tap before the hand goes looking for the next thing.</summary>
        const float AfterTap = .35f;

        /// <summary>The most a wait for the field to settle may take before the director moves on.</summary>
        const float SettleCeiling = 3f;

        /// <summary>Where the hand rests when it has nothing to do: low and to the right, off the gems.</summary>
        static readonly Vector2 Rest = new Vector2(300f, -560f);

        /// <summary>The seed the whole run draws from, so a recording can be re-taken like for like.</summary>
        const uint Seed = 0x5EED2026u;

        /// <summary>
        /// Which board this recording is of. Set by the door before the screen is presented;
        /// the first board otherwise, so a screen reached with nothing said still plays.
        /// </summary>
        public ShowcaseBoard Board = SiegeShowcase.Overrun;

        AssetHold _hold;
        SiegeView _siege;
        RectTransform _host;
        ShowcaseHand _hand;
        bool _ready, _ended;

        uint _seed = Seed;
        int _matches, _planted;

        /// <summary>
        /// A bomb the board refused, and until when the hand leaves it alone.
        ///
        /// <c>SiegeShowcase.Fuse</c> asks the board's own refusals before a tap, so this is
        /// belt and braces for the half-second between the reading and the hand arriving —
        /// a raider that dies in flight leaves a bomb the tap would refuse, and a refused bomb
        /// stays where it is. A person who tapped a bomb that did nothing moves on.
        /// </summary>
        int _shyBomb = -1;
        float _shyUntil;

        public override bool Ready => _ready;

        // ------------------------------------------------------------------ building
        protected override void Build() => StartCoroutine(Raise());

        IEnumerator Raise()
        {
            var task = Warm();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            if (!this) yield break;

            // A chapter's sky behind the board's own plate, which is translucent; the board
            // covers the display, so this is the tint through the plate and the corners and
            // nothing more. Built after the hold has resolved, so it is never a white rectangle.
            Scenery.Cover(Content, "Bg/" + Board.Backdrop, 0f, .14f);

            // The whole display, not the safe area: a recording is cropped by nothing.
            _host = UIKit.Node("Board", Content);
            _host.offsetMin = new Vector2(HostInset.x, HostInset.y);
            _host.offsetMax = new Vector2(-HostInset.z, -HostInset.w);

            // A board sized from a rect that has not been laid out yet is a board of nothing.
            yield return null;
            Canvas.ForceUpdateCanvases();

            int guard = 0;
            while (_host.rect.width < 40f && guard++ < 60) yield return null;
            if (!this) yield break;

            BuildBoard();

            _hand = ShowcaseHand.Build(Content, Rest);

            _ready = true;
            StartCoroutine(Direct());
        }

        /// <summary>
        /// The art this screen holds: the mode's own cast, the wild, the colossus, the ground,
        /// the line's four turrets and their reels, the sky, and the hand.
        ///
        /// <c>ArtFor(null)</c> is the mode's answer to "a siege with no chapter behind it" (the
        /// charm reels, the gems, the starter line) and everything else is asked for by the
        /// same static doors the run screen and the mode use, so nothing here is a second
        /// opinion about what a siege needs.
        /// </summary>
        System.Threading.Tasks.Task Warm()
        {
            var mode = LevelModes.Find(GameMode.Siege);
            var art = new List<AssetRequest>();

            if (mode != null) art.AddRange(mode.ArtFor(null));

            art.AddRange(SiegeMode.CastArt(Board.CastSet));

            var swings = SiegeMode.CastSwingArt(Board.CastSet);
            for (int i = 0; swings != null && i < swings.Count; i++)
                if (!string.IsNullOrEmpty(swings[i].Address)) art.Add(swings[i]);

            // Every boss the board sends, asked of the roster rather than of `HasBoss`: a pair
            // handed in as specs (Custom 2) carries two kinds and the grammar's one-boss
            // fields say nothing about either.
            var bosses = Board.BossKinds();
            for (int i = 0; i < bosses.Count; i++) SiegeMode.Bosses(bosses[i], art);

            art.Add(SiegeMode.Ground(Board.Ground));
            art.AddRange(Board.LineUp().Art());

            art.Add(AssetRequest.Sprite(AssetManifest.ArtRoot + "Bg/" + Board.Backdrop));
            art.Add(AssetRequest.Sprite(AssetManifest.Ui(ShowcaseHand.Picture)));

            _hold = _hold ?? AssetLibrary.Hold("showcase");
            return _hold.LoadAsync(art, null, Lifetime);
        }

        void BuildBoard()
        {
            _siege = _host.gameObject.AddComponent<SiegeView>();

            _siege.Rung = Board.Ground;
            _siege.CastSet = Board.CastSet;

            // This screen does its own pointing, so the board's idle nudge stands down.
            _siege.Coached = true;

            // And the hill says nothing: no count-in, no wave banner, no boss name, no chain.
            _siege.Muted = true;

            _siege.Solved = () => Finish(true);
            _siege.Lost = () => Finish(false);

            // A tapped bomb throws what a firepot throws. The run screen charges the run for it
            // through analytics; here there is no run to charge and nothing to record.
            _siege.Blew = Blew;

            _siege.Begin(_host, Board.Rules(), ProtoBudget.Unlimited);
        }

        SiegeUse Blew(int id, List<SiegeStrike> strikes)
        {
            var board = _siege != null ? _siege.Siege : null;
            if (board == null) return SiegeUse.Refused;

            int absorbed = board.Detonate(id, strikes);
            if (absorbed <= 0)
            {
                _shyBomb = id;
                _shyUntil = Time.unscaledTime + 1.5f;
                return SiegeUse.Refused;
            }

            return new SiegeUse(true, SiegeUtility.MatchesFor(absorbed), absorbed, -1);
        }

        void OnDestroy()
        {
            _hand?.Destroy();
            _hand = null;

            _hold?.Dispose();
            _hold = null;
        }

        // ------------------------------------------------------------------ the director
        /// <summary>
        /// Plays the run.
        ///
        /// <para>
        /// <b>One loop, one question a pass: what would a good player do right now.</b> Rubble
        /// first (a buried turret is a turret not firing), then a cog lying on the hill, then a
        /// bomb standing under something, then a full tube with a crowd to throw at, and only
        /// then a swap — and a swap only once the field is at rest and the thinking time has
        /// passed. Every answer is <c>SiegeShowcase</c>'s reading of the board and every act is
        /// the hand doing it, so the loop itself decides nothing.
        /// </para>
        /// <para>
        /// <b>The charms are stood on the field between moves</b> (<c>SiegeShowcase.Plant</c>),
        /// once the cascade has settled and never while one stands, and the gem wearing one
        /// pops so the arrival is seen.
        /// </para>
        /// </summary>
        IEnumerator Direct()
        {
            var board = _siege.Siege;
            if (board == null) yield break;

            yield return new WaitForSecondsRealtime(ProtoView.Entrance + FirstBeat);
            if (!this || _ended) yield break;

            yield return _hand.Show();

            _siege.Held = false;

            float next = Time.unscaledTime + Think;

            while (!_ended && this)
            {
                // ---- rubble ---------------------------------------------------------
                int buried = SiegeShowcase.Buried(board);
                if (buried >= 0 && _siege.Tappable)
                {
                    var heap = _siege.HeapAt(buried);
                    if (heap != null)
                    {
                        yield return _hand.Tap(Centre(heap), () => _siege.TapHeap(buried), .10f);

                        // Three taps clear a boulder (`SiegeTuning.RubbleTaps`); a thumb does
                        // them in under a second without lifting far. Bounded, so a heap the
                        // board will not let go of (a latch, a fallen turret) costs a beat
                        // rather than the rest of the recording.
                        for (int more = 0; more < SiegeTuning.RubbleTaps + 1 && !_ended
                                           && SiegeShowcase.Buried(board) == buried; more++)
                        {
                            yield return _hand.TapAgain(() => _siege.TapHeap(buried), .10f);
                            if (_siege.HeapAt(buried) == null) break;
                        }

                        yield return new WaitForSecondsRealtime(AfterTap);
                        continue;
                    }
                }

                // ---- a cog on the hill ----------------------------------------------
                int cog = SiegeShowcase.Loot(board);
                if (cog >= 0 && _siege.Tappable)
                {
                    var node = _siege.CogAt(cog);
                    if (node != null)
                    {
                        yield return _hand.Tap(Centre(node), () => _siege.TapCog(cog), .10f);
                        yield return new WaitForSecondsRealtime(AfterTap);
                        continue;
                    }
                }

                // ---- a bomb under something -----------------------------------------
                int bomb = SiegeShowcase.Fuse(board);
                if (bomb == _shyBomb && Time.unscaledTime < _shyUntil) bomb = -1;
                if (bomb >= 0 && _siege.Tappable)
                {
                    var node = _siege.BombAt(bomb);
                    if (node != null)
                    {
                        yield return _hand.Tap(Centre(node), () => _siege.TapBomb(bomb), .10f);
                        yield return new WaitForSecondsRealtime(AfterTap);
                        continue;
                    }
                }

                // ---- a full tube with a crowd to throw at ----------------------------
                int armed = SiegeShowcase.Armed(board);
                if (armed >= 0 && _siege.Tappable)
                {
                    var post = _siege.WardAt(armed);
                    if (post != null)
                    {
                        yield return _hand.Tap(Centre(post), () => _siege.TapWard(armed), .16f);
                        yield return new WaitForSecondsRealtime(AfterTap);
                        continue;
                    }
                }

                // ---- a swap ---------------------------------------------------------
                if (_siege.AtRest && _siege.Playable && Time.unscaledTime >= next
                    && SiegeShowcase.Aimed(board, ref _seed, out int a, out int b))
                {
                    yield return Swipe(a, b);
                    if (!this || _ended) yield break;

                    _matches++;
                    next = Time.unscaledTime + Pace(board);

                    yield return Settle();
                    if (!this || _ended) yield break;

                    Gift(board);
                    continue;
                }

                yield return null;
            }
        }

        /// <summary>The hand plays one swap: reach, press, drag, and the board hears it mid-drag.</summary>
        IEnumerator Swipe(int a, int b)
        {
            var from = _siege.GemAt(a);
            var to = _siege.GemAt(b);
            if (from == null || to == null) yield break;

            var start = Centre(from);
            var end = Centre(to);

            yield return _hand.MoveTo(start);
            yield return _hand.Press(.16f);

            // A thumb settles on a gem before it moves it.
            yield return new WaitForSecondsRealtime(.12f);

            bool landed = false;
            yield return _hand.DragTo(end, DragFor, DragFires, () => landed = _siege.Swipe(a, b));

            yield return _hand.Release(.18f);

            // A move the board refused — the field moved under a slow reach — costs the player
            // a beat, which is what it costs a thumb.
            if (!landed) yield return new WaitForSecondsRealtime(.3f);
        }

        /// <summary>Waits for the cascade to finish drawing, bounded.</summary>
        IEnumerator Settle()
        {
            float until = Time.unscaledTime + SettleCeiling;
            while (this && !_ended && !_siege.AtRest && Time.unscaledTime < until) yield return null;
        }

        /// <summary>Stands the next owed charm on the field, and pops the gem wearing it.</summary>
        void Gift(SiegeBoard board)
        {
            var due = SiegeShowcase.Due(_matches, _planted);
            if (due == SiegeCharm.None) return;

            int cell = SiegeShowcase.Plant(board, due, ref _seed);
            if (cell < 0) return;

            _planted++;

            if (!_siege.Restyle()) return;

            var gem = _siege.GemAt(cell);
            if (gem != null) Tween.Pop(gem, .55f, .42f);
        }

        /// <summary>
        /// How long the player thinks before the next swap: a second on a quiet hill, half of
        /// that with a crowd on it, and a jitter so no two waits are alike.
        /// </summary>
        float Pace(SiegeBoard board)
        {
            float crowd = board.BossStanding ? 1f : Mathf.Clamp01((board.OnTheHill - 1) / 6f);
            float think = Mathf.Lerp(Think, Hurry, crowd);
            return think + SiegeShowcase.Unit(ref _seed) * .45f;
        }

        Vector2 Centre(RectTransform target)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            // In the hand's own node, which is `TipOverlay.RectOf`'s arithmetic: the widget's
            // world corners read back through the space the hand is positioned in.
            var space = _hand != null && _hand.Space != null ? _hand.Space : Content;

            var min = (Vector2)space.InverseTransformPoint(corners[0]);
            var max = (Vector2)space.InverseTransformPoint(corners[2]);

            return (min + max) * .5f;
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The run is over. The hand withdraws and the board is left as it stands.
        ///
        /// <b>Nothing is drawn over a defeat</b>, on purpose: the board's own ruin — the shake,
        /// the red flash, the fallen posts — is the ending, and a panel over it would be the
        /// thing a watcher reads instead of the wall going. A victory, which the board is tuned
        /// never to reach, is left equally bare so a mis-tuned run cannot congratulate itself.
        /// </summary>
        void Finish(bool won)
        {
            if (_ended) return;
            _ended = true;

            if (_siege != null) _siege.Locked = true;

            StartCoroutine(Curtain());
        }

        IEnumerator Curtain()
        {
            // The hand lingers a beat over the falling line before it goes: a player who has
            // just lost does not vanish, they stop.
            yield return new WaitForSecondsRealtime(.8f);
            if (!this) yield break;

            yield return _hand.Hide(.6f);
        }

        /// <summary>The hardware key leaves for the map, which is where the door is.</summary>
        public override bool OnBack()
        {
            Flow.Go<LevelsScreen>();
            return true;
        }
    }
}
