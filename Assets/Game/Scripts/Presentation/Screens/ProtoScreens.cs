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
    /// <b>Prismvale.</b> Drag a gem onto its neighbour; line the lantern's own colour up all the
    /// way to a sleeping critter, and the vein between them lights.
    ///
    /// <para>
    /// <b>It inherits the whole run.</b> Everything about being a run — the heart, the stake, the
    /// record, the chests, the streak, the continue, what a restart costs, which latch holds the
    /// board while a lesson is up — comes from <see cref="ProtoScreen"/>. What is left here is
    /// four answers, which is what invariant 20b asks of a mode: bring your own board, share the
    /// run.
    /// </para>
    /// </summary>
    public sealed class PrismScreen : ProtoScreen
    {
        PrismView _field;

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
            _field = host.AddComponent<PrismView>();
            return _field;
        }

        protected override string GoalCaption => "mode.cap.asleep";

        /// <summary>
        /// The one refusal the board cannot show for itself: a lantern and a critter are
        /// <em>fixed</em>, so the first thing a new player does is try to drag one of them.
        /// Everything else answers on the board — two gems of a colour lean into each other and
        /// come back, which is the genre's own answer and needs no sentence.
        /// </summary>
        protected override string RefusalKey => "mode.prism.nodrag";

        protected override Mechanic Verb => Mechanic.PrismDrag;
        protected override Mechanic Friend => Mechanic.PrismVein;

        /// <summary>
        /// The board sits low and wide, because this mode has no band under it and no readout of
        /// its own — everything it counts is drawn on the field itself.
        /// </summary>
        protected override Vector4 HostInset => new Vector4(14f, 250f, 14f, 300f);
    }

    /// <summary>
    /// <b>Thornwatch.</b> The raiders are coming down the hill. Match a colour and the ward of
    /// that colour fuels up and looses bolts; let them through and they break the line.
    ///
    /// <para>
    /// <b>It inherits the whole run.</b> Everything about being a run - the heart, the stake, the
    /// record, the chests, the streak, what a restart costs, which latch holds the board while a
    /// lesson is up - comes from <see cref="ProtoScreen"/>, which is invariant 20b's whole demand
    /// of a mode: bring your own board, share the run. What is left here is four answers and one
    /// override, and the override is the readouts.
    /// </para>
    /// <para>
    /// <b>It takes <see cref="ProtoScreen"/> rather than <see cref="StoryScreen"/>, and that is a
    /// decision rather than an omission.</b> The band is a seam and it is paid for, but its cast
    /// are the raiders of the smelter and the haul-road; a chapter that wants a voice later can
    /// change this one line and author a `story` block, because nothing else here would move.
    /// </para>
    /// </summary>
    public sealed class SiegeScreen : ProtoScreen
    {
        SiegeView _siege;

        protected override ProtoView Attach(GameObject host)
        {
            _siege = host.AddComponent<SiegeView>();
            return _siege;
        }

        protected override string GoalCaption => "mode.cap.raid";

        /// <summary>
        /// Whether this run may advance - and it is <em>not</em> whether the board is taking
        /// input, which is what every other mode on this shape answers.
        ///
        /// <para>
        /// A cascade latches the board so a second swap cannot land while the first is still
        /// falling, and it is half a second long. Answering the shared question would therefore
        /// stop the hill on every match, which both freezes the one thing this mode is racing and
        /// hands the player a way to hold time still by swapping. It also keeps
        /// <c>RunScreen.Played</c> counting through a cascade, which is right: a siege is running
        /// whether or not a finger would do anything.
        /// </para>
        /// </summary>
        protected internal override bool Runnable => _siege != null && _siege.Advancing;

        /// <summary>
        /// The one refusal the board cannot show for itself: a gem is <em>dragged</em>, and
        /// nothing here is tapped at all. Everything else answers on the board - a swap that lines
        /// nothing up leans and comes back, which is the genre's own answer and needs no sentence.
        /// </summary>
        protected override string RefusalKey => "mode.siege.nodrag";

        protected override Mechanic Verb => Mechanic.SiegeFuel;
        protected override Mechanic Friend => Mechanic.SiegeLine;

        /// <summary>
        /// A siege does not run out of board — its line falls while the hill is still full, which
        /// is a different piece of news and wants different words. See
        /// <c>ProtoScreen.StuckReason</c>.
        /// </summary>
        protected override DefeatReason StuckReason => DefeatReason.WardsLost;

        /// <summary>
        /// Three numbers, and the middle one is not the one every other mode on this shape shows.
        ///
        /// <para>
        /// <b>The allowance is replaced by the ward line</b>, because this mode has no move
        /// allowance and the shared readout would print "free" over the one screen where something
        /// really is running out. What is running out is the line, and it is coloured for the same
        /// reason the allowance is elsewhere: it is the only one of the three that can end the run.
        /// </para>
        /// <para>
        /// The line is drawn on the board as well, with a health pip per blow, which is
        /// Hollowmarch's rule kept (invariant 33a) - the number in the corner and the picture on
        /// the board are the same number, so the two can never disagree.
        /// </para>
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var run = _siege != null ? _siege.Run : null;
            var board = _siege != null ? _siege.Siege : null;

            into.Add(new Readout(Loc.Get(GoalCaption), run == null ? "0" : run.Left.ToString()));

            if (board == null)
            {
                into.Add(new Readout(Loc.Get("mode.cap.wards"), "0"));
            }
            else
            {
                int standing = board.WardsStanding;

                var tint = standing <= 1 ? Pal.Ember
                         : standing <= 2 ? Pal.Gold
                         : Pal.Cream;

                into.Add(new Readout(Loc.Get("mode.cap.wards"),
                                     standing + "/" + board.Wards.Count, tint));
            }

            into.Add(new Readout(Loc.Get("mode.cap.matches"),
                                 run == null ? "0" : run.Spent.ToString()));
        }

        /// <summary>
        /// Room at the foot for nothing at all: this mode counts one thing the header does not
        /// already carry, and it is drawn on the ward line itself. The board is tall because it
        /// holds three bands - a hill, a line and a field - and the hill is the half a player
        /// spends the run looking at.
        /// </summary>
        protected override Vector4 HostInset => new Vector4(10f, 190f, 10f, 300f);
    }
}
