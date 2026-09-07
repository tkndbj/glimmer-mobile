using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// A prototype mode whose levels <em>talk</em>: the run, plus the band at the foot of the
    /// screen and the rules about when it may hold the board.
    ///
    /// <para>
    /// <b>The story costs the save file, the wire and the server nothing.</b> A cue is raised off
    /// the move that was just resolved, a beat is looked up in content, and a band says it
    /// (invariant 30d). It was built for Deep Orbit, kept when that was withdrawn, re-cast for
    /// Nova Raid, re-cast again for Hollowmarch, and lifted into a class of its own the day a
    /// second mode wanted it — which is what a seam is for, and it is now the fifth time this one
    /// has paid.
    /// </para>
    /// <para>
    /// <b>What a subclass supplies is two things beyond the run's four</b>: its own view, and
    /// which folder its portraits come from. The cast <em>ids</em> are shared vocabulary
    /// (<c>StoryCast</c>) and the pixels are not, because an address owned by one chapter's scope
    /// is never re-claimed by another (invariant 7b).
    /// </para>
    /// </summary>
    public abstract class StoryScreen : ProtoScreen
    {
        ProtoView _board;
        StoryBubble _bubble;
        Coroutine _opening;

        readonly Dictionary<StoryCue, int> _told = new Dictionary<StoryCue, int>();

        /// <summary>
        /// Which folder the portraits come from, ending in a slash.
        ///
        /// <b>The cast is vocabulary and the pixels are not.</b> <c>StoryCast</c> names who may
        /// speak and two modes set in the same raid share every one of those ids, while each
        /// draws them from its own scoped folder - an address owned by one chapter's scope is
        /// never re-claimed by another (invariant 7b).
        /// </summary>
        protected abstract string CastFolder { get; }

        /// <summary>The mode's own view, already built. Wire its <c>Say</c> before returning it.</summary>
        protected abstract ProtoView Board(GameObject host);

        protected override ProtoView Attach(GameObject host)
        {
            _board = Board(host);
            return _board;
        }

        // ------------------------------------------------------------------ the story
        protected override void Play()
        {
            base.Play();

            _told.Clear();

            if (_bubble == null) _bubble = StoryBubble.Attach(Safe);
            else _bubble.Silence();

            _bubble.Root = CastFolder;

            Recite();
        }

        /// <summary>The longest an opening scene may hold the board, whatever the band believes.</summary>
        const float MostToSay = 40f;

        void Recite()
        {
            if (_opening != null) StopCoroutine(_opening);
            _opening = StartCoroutine(Open());
        }

        /// <summary>
        /// The opening lines, over a board that is not yet taking input - the one place the
        /// story is allowed to hold the game up, once per level. Real seconds throughout (a
        /// modal sets <c>timeScale</c> to nought, invariant 30h), the lessons go first, and the
        /// latch is bounded so a run held by a sentence is never a run held for good (30g).
        /// </summary>
        IEnumerator Open()
        {
            var beat = Beat(StoryCue.Intro);
            if (beat == null || _board == null) { _opening = null; yield break; }

            yield return new WaitForSecondsRealtime(ProtoView.Entrance);
            if (_board == null || _bubble == null) { _opening = null; yield break; }

            while (Teaching.Teaching) yield return null;
            yield return new WaitForSecondsRealtime(.2f);
            if (_board == null || _bubble == null) { _opening = null; yield break; }

            _board.Locked = true;
            Repaint();

            bool talking = true;
            _bubble.Speak(beat, () => talking = false);

            float until = Time.unscaledTime + MostToSay;
            while (talking && _bubble != null && Time.unscaledTime < until) yield return null;

            if (_board != null && !RunOver) _board.Locked = false;
            _opening = null;
        }

        protected void Cue(StoryCue cue)
        {
            if (_bubble == null) return;
            var beat = Beat(cue);
            if (beat != null) _bubble.Speak(beat);
        }

        StoryBeat Beat(StoryCue cue)
        {
            var story = Level != null ? Level.Presentation.Story : null;
            if (story == null || !story.Any) return null;

            _told.TryGetValue(cue, out int nth);
            var beat = story.Take(cue, nth);
            if (beat != null) _told[cue] = nth + 1;
            return beat;
        }

        // ------------------------------------------------------------------ upkeep
        protected override void Rewind()
        {
            _told.Clear();
            if (_bubble != null) _bubble.Silence();
            base.Rewind();
            Recite();
        }

        public override void RetryAfterDefeat()
        {
            _told.Clear();
            if (_bubble != null) _bubble.Silence();
            base.RetryAfterDefeat();
            Recite();
        }
    }

    /// <summary>
    /// <b>Hollowmarch.</b> Fire a core into the line; three alike go off and the gap closes
    /// behind them. Break the convoy open and bring the critters home.
    ///
    /// <para>
    /// Everything about being a run comes from <see cref="ProtoScreen"/> and everything about a
    /// level that talks comes from <see cref="StoryScreen"/>; what is left here is which board to
    /// build, which cast to draw and the four answers invariant 20b asks of a mode.
    /// </para>
    /// </summary>
    public sealed class MarchScreen : StoryScreen
    {
        MarchView _road;

        protected override ProtoView Board(GameObject host)
        {
            _road = host.AddComponent<MarchView>();
            _road.Say = Cue;
            return _road;
        }

        protected override string CastFolder => "March/";

        protected override string GoalCaption => "mode.cap.raid";

        /// <summary>
        /// The one refusal the board cannot show for itself: a core has to go somewhere its own
        /// colour already is, so tapping a pod of another colour is a rule rather than a dropped
        /// input. Everything else — a full road, a run already going off — answers on the board.
        /// </summary>
        protected override string RefusalKey => "mode.march.nomatch";

        protected override Mechanic Verb => Mechanic.MarchFire;
        protected override Mechanic Friend => Mechanic.MarchSpark;

        /// <summary>
        /// Room at the foot for the story band, and for the launcher that sits under the road
        /// holding the next two cores — which is a readout the player looks at before every shot
        /// and so may not be shared with the header.
        /// Derived from the band's own height rather than typed, for <c>ModeScreen.ShadeDrop</c>'s reason.
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(14f, StoryBubble.Height + 120f, 14f, 300f);
    }

    /// <summary>
    /// <b>Emberforge.</b> Fuse three alike into an ember; tap it and a cross of light goes down
    /// its whole row and column. Break the smelter open and get them out.
    ///
    /// <para>
    /// <b>It inherits the whole run and the whole band.</b> Everything about being a run - the
    /// heart, the stake, the record, the chests, the streak, the continue, what a restart costs,
    /// which latch holds the board while a lesson is up - comes from <see cref="ProtoScreen"/>,
    /// and everything about a level that talks comes from <see cref="StoryScreen"/>. What is
    /// left here is four answers, which is what invariant 20b asks of a mode: bring your own
    /// board, share the run.
    /// </para>
    /// </summary>
    public sealed class EmberScreen : StoryScreen
    {
        EmberView _wall;

        protected override ProtoView Board(GameObject host)
        {
            _wall = host.AddComponent<EmberView>();
            _wall.Say = Cue;
            return _wall;
        }

        protected override string CastFolder => "Ember/";

        protected override string GoalCaption => "mode.cap.trapped";

        /// <summary>
        /// The one refusal the wall cannot show for itself: an ember is <em>tapped</em> and a
        /// shard is <em>dragged</em>. A swap that lines nothing up answers on the board - the two
        /// pieces lean into each other and come back, which is the genre's own answer and needs
        /// no sentence.
        /// </summary>
        protected override string RefusalKey => "mode.ember.notap";

        protected override Mechanic Verb => Mechanic.EmberFuse;
        protected override Mechanic Friend => Mechanic.EmberStar;

        /// <summary>
        /// Room at the foot for the story band, and nothing else - this mode has no readout
        /// under the board, because everything it counts is already on it.
        /// Derived from the band's own height rather than typed, for <c>ModeScreen.ShadeDrop</c>'s
        /// reason.
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(14f, StoryBubble.Height + 120f, 14f, 260f);
    }

    /// <summary>
    /// <b>Kindlewake.</b> Join two embers of the same colour and light burns between them; cross
    /// two strands on a sleeping critter and it wakes to a colour neither of them carried.
    ///
    /// <para>
    /// <b>It inherits the whole run and the whole band.</b> Everything about being a run — the
    /// heart, the stake, the record, the chests, the streak, the continue, what a restart costs,
    /// which latch holds the board while a lesson is up — comes from <see cref="ProtoScreen"/>,
    /// and everything about a level that talks comes from <see cref="StoryScreen"/>. What is left
    /// here is four answers, which is what invariant 20b asks of a mode: bring your own board,
    /// share the run.
    /// </para>
    /// </summary>
    public sealed class KindleScreen : ProtoScreen
    {
        KindleView _hollow;

        /// <summary>
        /// <b>It takes <see cref="ProtoScreen"/> rather than <see cref="StoryScreen"/>, and that
        /// is a decision rather than an omission.</b> The band is a seam and it is paid for, but
        /// its cast are raiders — Bolt and the Collector belong to the smelter and the haul-road,
        /// and a grove mode borrowing them would be two stories wearing one set of ids. Every
        /// mode set in the grove ships silent (four chapters of glades, three of Lightfall, two of
        /// Budburst) and this one joins them; a chapter that wants a voice later can change this
        /// one line and author a `story` block, because nothing else here would move.
        /// </summary>
        protected override ProtoView Attach(GameObject host)
        {
            _hollow = host.AddComponent<KindleView>();
            return _hollow;
        }

        protected override string GoalCaption => "mode.cap.asleep";

        /// <summary>
        /// The one refusal the hollow cannot show for itself: light comes out of <em>embers</em>,
        /// so a tap on the critter is the first thing a new player does and the one thing the
        /// board cannot answer by moving. Everything else answers on the board — a held ember
        /// lights its partners, and a tap on bare moss puts it down.
        /// </summary>
        protected override string RefusalKey => "mode.kindle.notcritter";

        protected override Mechanic Verb => Mechanic.KindleJoin;
        protected override Mechanic Friend => Mechanic.KindleCross;

        /// <summary>
        /// The board sits low and wide, because this mode has no band under it and no readout
        /// of its own — everything it counts is drawn on the hollow itself.
        /// </summary>
        protected override Vector4 HostInset => new Vector4(14f, 250f, 14f, 300f);
    }
}