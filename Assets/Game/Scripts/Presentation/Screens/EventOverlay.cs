using GlimmerGrove.Content;
using GlimmerGrove.Events;
using GlimmerGrove.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// The live event: what it is, how far through it the player is, and the way in.
    ///
    /// <para>
    /// The panel is mostly a <em>track</em> — the milestones laid out in order with the
    /// reached ones lit — because that is the shape of the thing a player is being asked to
    /// care about. A number saying "2 of 4" is information; a row of rungs with two behind
    /// you and two ahead is a plan, and the difference is entirely in whether the next
    /// reward is visible before it is earned.
    /// </para>
    /// <para>
    /// Every reward on it has already been paid, in the sense that matters: an event's
    /// milestone credits are <em>derived</em> from the glades finished inside the window —
    /// see <see cref="EventLedger"/> — so nothing here collects anything, and there is no
    /// button that could be missed, no claim that could fail and no state that could get
    /// out of step with what is drawn. The panel reports; it never grants.
    /// </para>
    /// </summary>
    public sealed class EventOverlay : ModalView
    {
        GroveEvent _event;
        EventProgress _progress;
        Text _clock;

        protected override void Build()
        {
            _event = GroveEvents.Live;

            // Only reachable from a strip that is only drawn when an event is live, but a
            // window can close between the tap and the frame that builds this. Closing is
            // better than an empty panel that says nothing.
            if (_event == null) { Close(); return; }

            _progress = GroveEvents.ProgressOf(_event);

            MakePanel(new Vector2(900f, 1080f), Loc.Get("ui.event.title"));

            UIKit.Shrinkable(
                UIKit.Titled("Name", Panel, Loc.Get(_event.NameKey), 52, Pal.Bloom,
                             TextAnchor.MiddleCenter, new Vector2(740f, 66f), new Vector2(.5f, 1f),
                             new Vector2(0f, -196f), 3f, 3f), 34);

            UIKit.Titled("Blurb", Panel, Loc.Get(_event.BlurbKey), 30,
                         new Color(.40f, .28f, .20f), TextAnchor.UpperCenter,
                         new Vector2(700f, 110f), new Vector2(.5f, 1f), new Vector2(0f, -262f),
                         0f, 0f, wrap: true);

            // Live, because this is a panel a player reads for a while and a deadline they
            // have to re-open a screen to measure is a deadline they stop believing.
            _clock = UIKit.Titled("Clock", Panel, ClockLine(), 34, Pal.Rose,
                                  TextAnchor.MiddleCenter, new Vector2(700f, 44f),
                                  new Vector2(.5f, 1f), new Vector2(0f, -382f), 2f, 2f);

            BuildTrack();
            BuildFooter();
        }

        void Update()
        {
            if (_clock) _clock.text = ClockLine();
        }

        string ClockLine()
        {
            if (_event == null) return string.Empty;

            long left = _event.SecondsLeftAt(GameClock.NowUnix());
            return left <= 0
                ? Loc.Get("ui.event.ended")
                : Loc.Format("ui.event.ends_in", Profile.LongCountdown(left));
        }

        /// <summary>
        /// The milestones, in order, with the reached ones lit.
        ///
        /// Drawn as a row of rungs on a rail rather than as a list, so the distance left is
        /// a length the player can see rather than a subtraction they have to do. The rail
        /// fills to exactly the glades finished, which is why it can sit between rungs —
        /// being visibly part of the way to the next one is the whole point.
        /// </summary>
        void BuildTrack()
        {
            var host = UIKit.Box("Track", Panel, new Vector2(760f, 210f),
                                 new Vector2(.5f, 1f), new Vector2(0f, -540f));

            int goal = Mathf.Max(1, _event.FinalGoal);
            const float Width = 640f;

            var rail = UIKit.Img("Rail", host, Art.Round(12), new Color(.24f, .18f, .12f, .55f),
                                 new Vector2(Width, 22f), new Vector2(.5f, .5f), new Vector2(0f, 30f));
            var fill = UIKit.Img("Fill", rail.transform, Art.Round(10), Pal.Bloom,
                                 new Vector2(0f, 16f), new Vector2(0f, .5f), new Vector2(3f, 0f));
            var fillRT = (RectTransform)fill.transform;
            fillRT.pivot = new Vector2(0f, .5f);

            float reached = (Width - 6f) * Mathf.Clamp01(_progress.Finished / (float)goal);
            Tween.Run(.9f, Ease.OutCubic,
                      t => { if (fillRT) fillRT.sizeDelta = new Vector2(reached * t, 16f); },
                      fill).Delay(.45f);

            for (int i = 0; i < _event.Milestones.Count; i++)
            {
                var milestone = _event.Milestones[i];
                bool earned = _progress.Finished >= milestone.Goal;

                float x = -Width * .5f + Width * (milestone.Goal / (float)goal);

                var disc = UIKit.Img("M" + i, host, Art.Disc(96),
                                     earned ? Pal.Bloom : new Color(.30f, .26f, .24f, .9f),
                                     new Vector2(66f, 66f), new Vector2(.5f, .5f), new Vector2(x, 30f));

                var ring = UIKit.Img("R", disc.transform, Art.Ring(96, 8f),
                                     earned ? Pal.Cream : new Color(1f, .95f, .84f, .35f));
                UIKit.StretchTo((RectTransform)ring.transform, 0, 0, 0, 0);

                UIKit.Titled("G", disc.transform, milestone.Goal.ToString(), 30,
                             earned ? new Color(.20f, .10f, .24f) : new Color(1f, .95f, .84f, .55f),
                             TextAnchor.MiddleCenter, outline: 0f, shadow: 0f);

                UIKit.Titled("C" + i, host, Loc.Format("ui.event.reward", milestone.Credits), 24,
                             earned ? Pal.Gold : new Color(1f, .95f, .84f, .45f),
                             TextAnchor.MiddleCenter, new Vector2(140f, 30f),
                             new Vector2(.5f, .5f), new Vector2(x, -22f), 0f, 2f);

                if (!earned) continue;

                disc.transform.localScale = Vector3.zero;
                int step = i;
                Tween.Pop(disc.transform, 0f, .5f, .55f + step * .12f);
            }

            UIKit.Titled("Count", host,
                         Loc.Format("ui.event.progress", _progress.Finished, goal), 28,
                         Pal.Cream, TextAnchor.MiddleCenter, new Vector2(700f, 38f),
                         new Vector2(.5f, .5f), new Vector2(0f, -80f), 2f, 2f);
        }

        void BuildFooter()
        {
            string line = _progress.IsComplete
                ? Loc.Get("ui.event.complete")
                : Loc.Format("ui.event.to_next", _progress.ToNext);

            UIKit.Shrinkable(
                UIKit.Titled("Next", Panel, line, 27,
                             _progress.IsComplete ? Pal.Mint : new Color(.44f, .32f, .24f),
                             TextAnchor.MiddleCenter, new Vector2(720f, 40f), new Vector2(.5f, 1f),
                             new Vector2(0f, -790f), 0f, 0f), 19);

            var next = GroveEvents.NextGlade(_event);

            // No way in once the track is done: a button that reopens a finished event is a
            // button that wastes the tap of a player who has already given it everything.
            if (next.IsValid)
            {
                var play = UIKit.TextButton("Play", Panel, "btn_violet", Loc.Get("ui.event.cta"), 48,
                                            new Vector2(560f, 140f), new Vector2(.5f, 0f),
                                            new Vector2(0f, 232f),
                                            () => Close(() => Flow.Go<PlayScreen>(v => v.LevelId = next)));
                UIKit.Halo(play.transform, Pal.Bloom, 640f, .3f);
                Sheen.Attach((RectTransform)play.transform, 3.4f);
            }

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.got_it"), 44,
                             new Vector2(560f, 120f), new Vector2(.5f, 0f),
                             new Vector2(0f, next.IsValid ? 96f : 160f), () => Close());
        }

        public override bool OnBack() { Close(); return true; }
    }
}
