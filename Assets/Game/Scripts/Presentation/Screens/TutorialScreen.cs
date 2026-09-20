using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The first thing a new player ever plays: one wave, two sentences, and a way out.
    ///
    /// <para>
    /// <b>It is the real mode with a script beside it.</b> The board is <c>SiegeView</c> over
    /// <c>SiegeBoard</c> — the same gems, the same turrets, the same clock and the same raiders
    /// the first rung runs — dealt from <see cref="SiegeTutorial"/> rather than from a chapter.
    /// Nothing here re-implements a rule, and nothing the player learns has to be translated onto
    /// a different-looking board afterwards.
    /// </para>
    /// <para>
    /// <b>Two panels and no more.</b> A match feeds the turret of its colour, and a full turret
    /// is a button. Those are the only two things in this mode a player cannot find by looking,
    /// and they are the two the first rung used to teach on its own — see
    /// <see cref="TutorialGate"/> for why teaching them here takes them off the level rather than
    /// duplicating them. Everything else the board demonstrates: the hill walks, the tubes fill,
    /// the bolts fly, the raiders fall.
    /// </para>
    /// <para>
    /// <b>Nothing here can be lost, stalled or left behind</b>, and each of those is a separate
    /// mechanism rather than a hope. The line cannot fall, structurally: <c>SiegeBoard.Sheltered</c>
    /// is set once and read where a ward is hurt, so a turret under it is never damaged rather
    /// than repaired afterwards. The board is latched while a panel is up, so the hill never
    /// walks over a sentence somebody is reading. Every wait ends on a state the board reports
    /// rather than on a count this screen keeps, and the two that could in principle wait on the
    /// hill carry a ceiling. The ending is the mode's own victory, reached because the last beat
    /// keeps the whole line burning (<c>SiegeBoard.Kindle</c>) rather than because a timer said
    /// so — and it is also wired to the defeat hook, so even a state this screen believes
    /// impossible ends in the same place. Every wait is a coroutine on this screen, so leaving
    /// takes the script with it, and the art is held by this screen and released with it.
    /// </para>
    /// <para>
    /// <b>The way out is always drawn.</b> Skip is a real control in the corner, it closes the
    /// gate exactly as finishing does, and the hardware key points at it rather than acting on
    /// its own — a first-timer must never be dropped out of the game's opening by a gesture they
    /// did not mean.
    /// </para>
    /// </summary>
    public sealed class TutorialScreen : View
    {
        /// <summary>How far the board sits from the screen's edges.</summary>
        /// <remarks>
        /// Tighter than <c>ProtoScreen.HostInset</c> at both ends, because this screen carries
        /// neither the readout row nor the nav bar: what is above the board is the title and what
        /// is below it is air.
        /// </remarks>
        static readonly Vector4 HostInset = new Vector4(24f, 96f, 24f, 214f);

        const float ChromeSize = 92f;

        /// <summary>
        /// The title cloth, and it is deliberately smaller than the one every other page wears.
        ///
        /// <b>Because this heading is the least important thing on the screen.</b> Elsewhere a
        /// ribbon names a page somebody navigated to and has to be found; here it is a greeting
        /// over a board that is already doing the teaching, and at the page-standard 720x138 it
        /// was the loudest thing in the frame — reported as simply too big. Sized down to the
        /// point where it reads as a welcome rather than as a banner, and the caption drops with
        /// it so the cloth is not just cropped tighter round the same lettering.
        /// </summary>
        const float BannerW = 620f, BannerH = 112f;
        const int BannerSize = 34;

        /// <summary>A beat after the board has finished arriving, before the first panel.</summary>
        const float FirstBeat = .35f;

        /// <summary>A beat between an action landing and the panel that follows it.</summary>
        const float Settle = .45f;

        /// <summary>How long the player's own throw is watched before the line joins in.</summary>
        const float Applause = .8f;

        /// <summary>
        /// The longest the last beat may run before the screen finishes anyway.
        ///
        /// <para>
        /// <b>A ceiling and not a plan</b>, for <c>Flow.ReadyTimeout</c>'s reason: the victory is
        /// the mode's own and this only exists because a first launch is the one place in the
        /// game where being stuck has no way back.
        /// </para>
        /// <para>
        /// <b>Measured against the worst case rather than the usual one, and the worst case is a
        /// <em>fast</em> player.</b> The sweep cannot end before the last raider has walked on,
        /// and a wave musters one body at a time (<c>SiegeTuning.RaiderSpacing</c>) — so somebody
        /// who arms the tube in five seconds waits out the whole muster and sweeps for 15.3s,
        /// where an ordinary player sweeps for 11.9s. At 20 that left under five seconds of
        /// headroom on a ceiling whose whole job is never to fire: reached, it would draw the
        /// finale over a hill that still had raiders walking down it. Generous costs nothing,
        /// because nothing in a healthy run waits on it.
        /// </para>
        /// </summary>
        const float SweepCeiling = 35f;

        /// <summary>
        /// Where the closing line and its key stand, measured up from the middle of the display.
        ///
        /// <b>Both are well above centre, and a render is why.</b> The middle of this screen is
        /// exactly where the ward line stands — three turrets, their health bars and their rank
        /// crests — so a panel centred the ordinary way lands its sentence across the turrets and
        /// puts its one key on top of them, which reads as something dropped on the board rather
        /// than as the board being finished with. Seated over the hill instead, which is the
        /// ground the player has just cleared and the only part of this screen that is empty by
        /// the time they are read. <c>Tools/render_tutorial.py --finale</c> is the only thing
        /// that can say so.
        /// </summary>
        const float LineSeat = 480f, KeySeat = 250f;

        /// <summary>
        /// The longest the second panel waits for a crowd before settling for one raider.
        ///
        /// <b>The other half of <c>SiegeTutorial.Crowd</c>, and it lives here because it is a
        /// clock.</b> The shipped wave reaches two bodies about a second and a half after the
        /// first, so this is never spent — it is here because a wave somebody later shortens to
        /// one raider would otherwise leave the script waiting for a second one for ever.
        /// </summary>
        const float CrowdCeiling = 6f;

        AssetHold _hold;
        SiegeView _siege;
        RectTransform _host, _coach;
        Btn _skip;
        bool _ready, _ended;

        public override bool Ready => _ready;

        // ------------------------------------------------------------------ building
        protected override void Build() => StartCoroutine(Raise());

        /// <summary>
        /// Loads the art, then draws the screen on top of it.
        ///
        /// <para>
        /// <b>Everything is built after the hold resolves, and that is what keeps the first
        /// screen of the game honest.</b> A board built first and repainted later is the right
        /// answer on a rung, where the player is already somewhere and the alternative is a wait;
        /// here the iris is still shut (<see cref="Ready"/>), so there is nobody to show a
        /// half-dressed board to and no reason to draw one.
        /// </para>
        /// <para>
        /// In practice it costs nothing: the splash has already pulled the opening chapter's art
        /// in, which is this same cast, and <c>AssetLibrary</c> hands back what is resident
        /// without touching the disk.
        /// </para>
        /// </summary>
        IEnumerator Raise()
        {
            var task = Warm();
            while (!task.IsCompleted) yield return null;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            if (!this) yield break;

            // **The blue wall, not a chapter's sky.** This screen used to stand on `sky_00` —
            // the first chapter's backdrop, on the argument that the tutorial should look like
            // the game rather than like a menu. That backdrop is a *salmon* dawn, which behind
            // a grey hill and a dark gem plate reads as neither; the owner's call was blue.
            // `Scenery.Plain` is the wall every list screen in the game already stands on, it
            // is in the global set so it can never be missing or late (invariant 7b), and it
            // carries no shade, no vignette and no parallax — which is right behind a board
            // that is already the busiest thing on the display.
            Scenery.Plain(Content);
            Fireflies.Spawn(Content, 10, Pal.A(Pal.Gold, .9f), 4f, 14f);

            BuildChrome();

            _host = UIKit.Node("Board", Safe);
            _host.offsetMin = new Vector2(HostInset.x, HostInset.y);
            _host.offsetMax = new Vector2(-HostInset.z, -HostInset.w);

            // A board sized from a rect that has not been laid out yet is a board of nothing.
            // ModeScreen's own guard, and it is wanted here for the same reason.
            yield return null;
            Canvas.ForceUpdateCanvases();

            int guard = 0;
            while (_host.rect.width < 40f && guard++ < 60) yield return null;
            if (!this) yield break;

            BuildBoard();

            _ready = true;
            StartCoroutine(Teach());
        }

        /// <summary>
        /// The art this screen holds: the mode's own cast and its starter line.
        ///
        /// <b><c>ArtFor(null)</c> rather than a list written out here</b> — that overload answers
        /// exactly "a siege with no chapter behind it", which is what this is, and it already
        /// names the insects and the starter turrets. A list typed here would be a second opinion
        /// about what a siege needs, and the kind that goes stale in silence.
        /// </b>
        /// </summary>
        System.Threading.Tasks.Task Warm()
        {
            var mode = LevelModes.Find(GameMode.Siege);
            var art = new List<AssetRequest>();

            if (mode != null) art.AddRange(mode.ArtFor(null));

            // The ground is not held here: `Bg/plain` is in the global set (`Scenery.Plain`),
            // so it is resident before this screen exists and outlives it.

            _hold = _hold ?? AssetLibrary.Hold("tutorial");
            return _hold.LoadAsync(art, null, Lifetime);
        }

        void BuildChrome()
        {
            float cy = -(22f + BannerH * .5f);

            var ribbon = Scenery.TitleRibbon(Safe, Loc.Get("ui.tutorial.title").ToUpperInvariant(),
                                             new Vector2(BannerW, BannerH), new Vector2(.5f, 1f),
                                             new Vector2(0f, cy), BannerSize);
            ribbon.transform.localScale = Vector3.zero;
            Tween.Pop(ribbon.transform, 0f, .5f, .06f);

            // **A worded key rather than a glyph.** Every other corner key in this game is an
            // arrow or a gear, and a player who has been in the app for four seconds has no
            // vocabulary for either — the one control on this screen that has to be understood
            // without being explained is the one that gets you out of it.
            _skip = UIKit.TextButton("Skip", Safe, Skins.Shut,
                                     Loc.Get("ui.tutorial.skip").ToUpperInvariant(), 26,
                                     new Vector2(168f, ChromeSize), new Vector2(1f, 1f),
                                     new Vector2(-96f, cy), Skip);

            var group = UIKit.Group((RectTransform)_skip.transform);
            group.alpha = 0f;
            Tween.Fade(group, 1f, .4f).Delay(.35f);
        }

        void BuildBoard()
        {
            _siege = _host.gameObject.AddComponent<SiegeView>();

            // The first rung's ground and the first chapter's cast, because this is the board
            // the tutorial leads into. Both are arithmetic elsewhere (invariant 7c); here there
            // is no chapter to do arithmetic on, so they are said once.
            _siege.Rung = 0;
            _siege.CastSet = SiegeMode.Insects;

            // This screen does its own pointing while a hand is up, so the board's idle nudge
            // stands down for those stretches. See `SiegeView.Coached`.
            _siege.Coached = true;

            _siege.Solved = Finish;

            // **And the defeat hook goes to the same place.** `SiegeTutorial.Steady` is what
            // makes losing this board impossible, and a screen whose only ending is the one it
            // believes is guaranteed is a screen with no ending at all if that belief is ever
            // wrong.
            _siege.Lost = Finish;

            _siege.Begin(_host, SiegeTutorial.Rules(), ProtoBudget.Unlimited);

            // **The tutorial cannot be lost, and it is the board that knows it.** Set once, on
            // the board, and read where a ward is hurt — see `SiegeBoard.Sheltered`. It was a
            // repair this screen applied sixty times a second, which worked and put the model
            // in a state it should never have been in first.
            var board = _siege.Siege;
            if (board != null) board.Sheltered = true;
        }

        void OnDestroy()
        {
            _hold?.Dispose();
            _hold = null;
        }

        // ------------------------------------------------------------------ the script
        IEnumerator Teach()
        {
            var board = _siege.Siege;
            if (board == null) yield break;

            yield return new WaitForSecondsRealtime(ProtoView.Entrance + FirstBeat);
            if (!this || _ended) yield break;

            // ---- the verb -------------------------------------------------------------
            var swap = SiegeTutorial.Taught(board);

            yield return Tip(TutorialGate.Verb, _siege.GemAt(swap.A),
                             Route(swap.A, swap.B));
            if (!this || _ended) yield break;

            // The hill only starts walking once the first sentence has been read. Everything
            // after this is the mode running at its own pace.
            _siege.Held = false;

            PointAlong(swap.A, swap.B);

            // ---- the tube ------------------------------------------------------------
            // **Three matches, not one**, and the middle one is where the hand comes off. A
            // turret that arms on a single move teaches that an overcharge is something that
            // happens to you; a tube that climbs twice before it lights teaches that it is
            // something you build, which is the loop the whole mode is. See
            // `SiegeTutorial.MatchesToArm`.
            // **Ended on a banked charge rather than on a count of feeds**, which is what stops
            // it hanging: a match's own fuel lands beside the tutorial's share, so the tube can
            // brim a feed early — and a loop still owed one would wait on `Fed`, which skips a
            // ward that has already banked. See `SiegeTutorial.Charged`.
            int match = 0;

            while (SiegeTutorial.Charged(board) < 0)
            {
                if (!this || _ended) yield break;

                int fed = SiegeTutorial.Fed(board);
                if (fed < 0) { yield return null; continue; }

                match++;

                if (match == 1)
                {
                    // **The hand goes after the first match and the board takes over.** It has
                    // been shown; standing it over every move afterwards is a game playing
                    // itself. What answers a player who then stalls is the board's own idle
                    // nudge, which is exactly the feature for it and has been held off until
                    // now (`SiegeView.Coached`).
                    Unpoint();
                    _siege.Coached = false;
                }

                yield return Pour(board, fed, match);
            }

            // The hand comes back for the one control nobody would find on their own.
            _siege.Coached = true;

            // ---- the overcharge -------------------------------------------------------
            // Waited for rather than assumed, twice over: a tube with nothing on the hill to
            // throw at is a control that would refuse the tap (`SiegeBoard.CanOvercharge`), and
            // a hill with one body on it is the mode's biggest payoff spent on its smallest
            // target. The ceiling is what stops the second half being able to wait for ever.
            float patient = Time.unscaledTime + CrowdCeiling;

            int armed;
            while ((armed = SiegeTutorial.Target(board, Time.unscaledTime < patient)) < 0)
            {
                if (!this || _ended) yield break;
                yield return null;
            }

            yield return new WaitForSecondsRealtime(Settle);
            if (!this || _ended) yield break;

            yield return Tip(TutorialGate.Charge, _siege.ArmedWard(), null);
            if (!this || _ended) yield break;

            PointAt(_siege.ArmedWard());

            while (board.Wards[armed].Charges > 0)
            {
                if (!this || _ended) yield break;
                yield return null;
            }

            Unpoint();

            // ---- the sweep ------------------------------------------------------------
            // **A beat for the throw to be seen on its own, before the line joins in.** The
            // blast takes the raider it was aimed at and the box around it, and the sweep below
            // opens up a moment later — run together, the player cannot tell which of the two
            // was theirs, which is the whole thing this panel just taught them.
            yield return new WaitForSecondsRealtime(Applause);
            if (!this || _ended) yield break;

            // The grove answers with everything it has, and the run ends the way every siege
            // ends. `Finish` is raised by the board, not from here.
            float until = Time.unscaledTime + SweepCeiling;

            while (!_ended && Time.unscaledTime < until)
            {
                board.Kindle();
                yield return null;
            }

            if (!_ended) Finish();
        }

        /// <summary>
        /// One match's worth of fuel, climbing the tube over <c>SiegeTutorial.PourSeconds</c>.
        ///
        /// <para>
        /// <b>Dripped rather than set, and every drop goes through <c>SiegeBoard.Pour</c></b> —
        /// which is <c>SiegeWard.Fill</c>'s own door, so a tube filled by this script is filled
        /// the way a match fills it: the charge cap, the carried overflow and a sunlord's toll
        /// are all that method's business and none of them is reimplemented here.
        /// </para>
        /// <para>
        /// <b>How much is asked once, at the top.</b> The ward goes on firing while this runs —
        /// it is <c>Fuelled</c> the moment the first drop lands — so a target recomputed each
        /// frame would chase its own tail on a turret that is spending what it is given.
        /// </para>
        /// </summary>
        IEnumerator Pour(SiegeBoard board, int ward, int match)
        {
            float want = SiegeTutorial.Feed(board, ward, match);
            if (want <= 0f) yield break;

            float given = 0f;

            while (given < want)
            {
                float step = want * Time.unscaledDeltaTime / SiegeTutorial.PourSeconds;
                if (step > want - given) step = want - given;

                given += step;

                // A charge banked early — the player fed this turret twice over — is this feed
                // finished with, whatever is left of it.
                if (board.Pour(ward, step)) yield break;

                if (!this || _ended) yield break;
                yield return null;
            }
        }

        /// <summary>
        /// One panel, and the board latched behind it.
        ///
        /// <para>
        /// <b>Raised through <c>Flow.Modal</c> and waited on through <c>Dismissed</c></b>, which
        /// fires exactly once however the panel goes away — accepted, backed out of, or destroyed
        /// under a navigation — so this can never wait for ever. The latch is <c>Locked</c>
        /// rather than <c>Held</c>: both stop the hill, and <c>Locked</c> is the one every other
        /// panel in the game already uses for "something is over the board".
        /// </para>
        /// </summary>
        IEnumerator Tip(Mechanic mechanic, RectTransform target, RectTransform[] trace)
        {
            bool waiting = true;

            _siege.Locked = true;

            Flow.Modal<TipOverlay>(v =>
            {
                v.Mechanic = mechanic;
                v.Target = target;
                v.Trace = trace;
                v.TraceCells = 1;
                v.TraceTint = Pal.Cream;
                v.Dismissed = () => waiting = false;
            });

            while (waiting && this && !_ended) yield return null;

            if (_siege != null) _siege.Locked = false;
        }

        RectTransform[] Route(int a, int b)
        {
            var from = _siege.GemAt(a);
            var to = _siege.GemAt(b);

            return from == null || to == null ? null : new[] { from, to };
        }

        // ------------------------------------------------------------------ the pointer
        /// <summary>
        /// The ring and the hand that stay on the board after a panel has closed.
        ///
        /// <para>
        /// <b>A panel says what, and this says which and how.</b> <c>TipOverlay</c> already rings
        /// its subject and can already trace a route, and both go away with the panel — which is
        /// right for a tip over a run somebody is already playing, and wrong for the first thing
        /// they ever touch: the sentence has to be dismissed before the board can be reached, so
        /// the only moment the demonstration is worth anything is the moment it is gone. So it is
        /// put back, on the board, and left there until the player does the thing.
        /// </para>
        /// <para>
        /// <b>Built in <see cref="View.Content"/> rather than on the board</b>, so it outlives a
        /// cascade rebuilding the gems under it, and so it is drawn above every layer of the
        /// board rather than inside one of them. The positions are read off the real widgets at
        /// the moment it goes up, which is the same conversion <c>TipOverlay</c> does and for the
        /// same reason.
        /// </para>
        /// </summary>
        void PointAlong(int a, int b)
        {
            var from = _siege.GemAt(a);
            var to = _siege.GemAt(b);
            if (from == null || to == null) return;

            var host = Pointer();
            var route = new List<Vector2> { Centre(from, host), Centre(to, host) };

            Ring(host, from);
            Ring(host, to);

            CoachHand.Show(host, route, Pal.Cream, 1, host);
        }

        void PointAt(RectTransform target)
        {
            if (target == null) return;

            var host = Pointer();

            Ring(host, target);
            CoachHand.Tap(host, Centre(target, host), Pal.Cream, host);
        }

        RectTransform Pointer()
        {
            Unpoint();
            _coach = UIKit.Node("Coach", Content);
            return _coach;
        }

        void Unpoint()
        {
            if (_coach == null) return;

            Tween.KillAll(_coach);
            Destroy(_coach.gameObject);
            _coach = null;
        }

        /// <summary>A ring round one thing, breathing, so the eye lands on it before the hand does.</summary>
        static void Ring(RectTransform host, RectTransform target)
        {
            var box = BoxIn(target, host);
            float side = Mathf.Max(box.width, box.height) * 1.34f;

            var ring = UIKit.Img("Ring", host, Art.Ring(128, 9f), Pal.A(Pal.Gold, .95f),
                                 Vector2.one * side, new Vector2(.5f, .5f), box.center);

            Tween.Breathe(ring.transform, .10f, 1.15f);
            Tween.Value(.95f, .45f, .58f, v => { if (ring) ring.color = Pal.A(Pal.Gold, v); },
                        Ease.InOutSine, ring).Loop(-1);
        }

        static Vector2 Centre(RectTransform target, RectTransform space) => BoxIn(target, space).center;

        /// <summary>A widget's rectangle in another node's space. <c>TipOverlay.RectOf</c>'s arithmetic.</summary>
        static Rect BoxIn(RectTransform target, RectTransform space)
        {
            var corners = new Vector3[4];
            target.GetWorldCorners(corners);

            var min = (Vector2)space.InverseTransformPoint(corners[0]);
            var max = (Vector2)space.InverseTransformPoint(corners[2]);

            return new Rect(min.x, min.y, max.x - min.x, max.y - min.y);
        }

        // ------------------------------------------------------------------ the endings
        /// <summary>
        /// The hill is clear. One panel, one key, and the game.
        ///
        /// <b>Latched, because there are three ways in</b> — the board's victory, the board's
        /// defeat, and the ceiling on the last beat — and a celebration drawn twice is a
        /// celebration drawn over itself.
        /// </summary>
        void Finish()
        {
            if (_ended) return;
            _ended = true;

            Unpoint();
            if (_siege != null) _siege.Locked = true;

            StartCoroutine(Curtain());
        }

        IEnumerator Curtain()
        {
            // A beat for the board's own celebration to be seen before anything covers it.
            yield return new WaitForSecondsRealtime(.35f);
            if (!this) yield break;

            Flow.Flash(Pal.Gold, .34f, .55f);

            // **The way out goes first, because there is nothing left to skip.** A key that says
            // SKIP standing beside one that says CONTINUE is two answers to a question with one,
            // and the scrim below only stops it being *pressed*.
            Retire();

            var scrim = UIKit.Scrim(Content, .66f);
            var safe = SafeArea.Node("Finale", Content);

            UIKit.Halo(safe, Pal.Gold, 820f, .34f, new Vector2(0f, LineSeat));

            var line = UIKit.Titled("Line", safe, Loc.Get("ui.tutorial.win"), 66, Pal.Cream,
                                    TextAnchor.MiddleCenter, new Vector2(880f, 240f),
                                    new Vector2(.5f, .5f), new Vector2(0f, LineSeat),
                                    3f, 5f, wrap: true);
            UIKit.Shrinkable(line, 34);

            line.transform.localScale = Vector3.zero;
            Tween.Pop(line.transform, 0f, .58f);

            Burst.Sparks(safe, new Vector2(0f, LineSeat), Pal.Gold, 22, 260f);

            var go = UIKit.TextButton("Go", safe, Skins.Affirm,
                                      Loc.Get("ui.tutorial.go").ToUpperInvariant(), 34,
                                      new Vector2(460f, 122f), new Vector2(.5f, .5f),
                                      new Vector2(0f, KeySeat), Done);

            var rt = (RectTransform)go.transform;
            var group = UIKit.Group(rt);
            group.alpha = 0f;

            var seat = rt.anchoredPosition;
            rt.anchoredPosition = seat + new Vector2(0f, -54f);

            Tween.Fade(group, 1f, .34f).Delay(.26f);

            // **A sheen rather than a breath, and it is not a taste.** A breathing key writes
            // `localScale` every frame for as long as it stands, and `Btn`'s own press does the
            // same — so the two fight and the breath wins, which is a button that does not
            // depress under a thumb. A sheen is the house cue for the one key on a screen
            // (the chest's COLLECT, the hub's PLAY) and it touches nothing the press wants.
            Tween.Move(rt, seat, .42f, Ease.OutBack).Delay(.26f)
                 .OnDone(() =>
                 {
                     if (!rt) return;

                     // The rest scale is read after the arrival has finished with it, which is
                     // `Btn.Rehome`'s whole reason: a key whose home was captured mid-pop
                     // springs to the wrong size on the first press.
                     go.Rehome();
                     Sheen.Attach(rt, 3.2f);
                 });

            if (scrim != null) scrim.raycastTarget = true;
        }

        /// <summary>
        /// Takes the way out off the screen, once there is nothing left to leave.
        ///
        /// Faded and switched off rather than destroyed, so <see cref="OnBack"/> has something to
        /// point at for the frames before the finale's own key arrives.
        /// </summary>
        void Retire()
        {
            if (_skip == null) return;

            _skip.Interactable = false;
            Tween.Fade(UIKit.Group((RectTransform)_skip.transform), 0f, .22f);
        }

        void Done() => Leave();

        /// <summary>
        /// The corner key: out of the tutorial and into the game, exactly as finishing it is.
        ///
        /// <b>It closes the gate too</b>, and that is the whole meaning of the word: a player who
        /// says they know this must not then be shown the same two panels on the first rung. See
        /// <see cref="TutorialGate.Spend"/>.
        /// </summary>
        void Skip()
        {
            if (_ended) return;
            _ended = true;

            Unpoint();
            Leave();
        }

        void Leave()
        {
            TutorialGate.Spend();
            Flow.Go<HomeScreen>();
        }

        /// <summary>
        /// The hardware key points at the way out rather than taking it.
        ///
        /// <b>An accidental back gesture must not be how somebody leaves the opening of the
        /// game.</b> The control that does it is already on the screen and already says what it
        /// is, so the honest answer to the key is to make it obvious — and to swallow the press,
        /// because the alternative (<c>Flow</c>'s default) is no navigation at all from here.
        /// </b>
        /// </summary>
        public override bool OnBack()
        {
            if (_ended) return true;

            if (_skip != null) Tween.Punch(_skip.transform, .22f);
            return true;
        }
    }
}
