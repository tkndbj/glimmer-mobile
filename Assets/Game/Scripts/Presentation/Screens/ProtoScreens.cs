using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Modes;
using GlimmerGrove.Analytics;
using GlimmerGrove.Progression;
using GlimmerGrove.Utilities;
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
        UtilityBar _bar;

        protected override ProtoView Attach(GameObject host)
        {
            _siege = host.AddComponent<SiegeView>();
            _siege.Rung = Rung();
            return _siege;
        }

        /// <summary>
        /// Which rung of its own chapter this level is, which is the only thing deciding its
        /// ground (invariant 7c, and <see cref="SiegeMode.Ground"/>).
        ///
        /// Read off the catalog rather than carried on the level, because a level's place is a
        /// fact about the chapter that holds it and nothing about the level itself — and because
        /// a chapter body naming its own floors is exactly the per-chapter choice 7c refuses.
        /// </summary>
        int Rung()
        {
            if (Level == null) return 0;

            var levels = GameContent.Index.LevelsOf(Level.Chapter);
            if (levels == null) return 0;

            for (int i = 0; i < levels.Count; i++)
                if (levels[i].Equals(Level.Id)) return i;

            return 0;
        }

        // ------------------------------------------------------------------ the action bar
        /// <summary>
        /// Builds the board and then the bar under it, and wires the one transaction between
        /// them.
        ///
        /// <para>
        /// <b>The bar hangs off the safe area rather than off the board host</b>, because the
        /// host is what a mode's view fills and a view that resized itself around a bar would be
        /// a view that has to know about one. What the board gives up for it is
        /// <see cref="HostInset"/>'s bottom margin, which is the only line either of them shares.
        /// </para>
        /// </summary>
        protected override void Play()
        {
            base.Play();
            if (_siege == null) return;

            if (_bar == null)
            {
                var node = UIKit.Node("Utilities", Safe);
                _bar = node.gameObject.AddComponent<UtilityBar>();
                _bar.Build(Safe);

                _bar.Aiming = Aim;
                _bar.Wanted = Offer;
            }

            _siege.Fire = Fire;
            _siege.Rejected = Refuse;

            // The bar owns what is armed; the board only mirrors it. Without this the view
            // disarmed itself and the slot kept its ring, which read as an item stuck on.
            _siege.Done = () => { if (_bar != null) _bar.Arm(null); };

            _bar.Arm(null);
            _bar.Paint();
        }

        /// <summary>
        /// Arms or disarms a utility, and says what to do with it.
        ///
        /// <para>
        /// <b>The one sentence this feature cannot show on the board.</b> Everything else about a
        /// utility answers on the board — the ring says how far a firepot reaches, the ward
        /// targets say which wards will take a mending — but whether the thing in your hand is
        /// dragged or tapped is not a fact anything on screen can carry. Said on arming rather
        /// than taught as a lesson, because it is a reminder about *this* item rather than a rule
        /// about the mode, and a player who already knows it has still asked for it by tapping.
        /// </para>
        /// </summary>
        void Aim(UtilityItem item)
        {
            // **An unaimed utility is used on the tap that arms it, and never armed.** There is
            // nothing for it to be pointed at, so leaving it armed would put the player in a
            // targeting mode with no target - a control that accepts any tap and ignores where it
            // was. The bar is disarmed first, or a use that refuses would leave a slot ringing.
            if (item != null && item.Target == UtilityTarget.Everywhere)
            {
                _bar?.Arm(null);
                if (_siege != null) _siege.Arming = null;

                _siege?.Loose(item);
                return;
            }

            if (_siege != null) _siege.Arming = item;
            if (item == null || _bar == null) return;

            Scenery.Toast(Safe, _bar.AimingNote, Pal.Cream, 1.8f,
                          new Vector2(.5f, 0f), UtilityBar.Height + 90f);
        }

        /// <summary>
        /// Uses one utility: the board first, the ledger second, and nothing at all if the board
        /// refused.
        ///
        /// <para>
        /// <b>The whole transaction, in one place, in that order.</b> The board is asked what the
        /// utility would do before anything is taken, so a firepot that reached nobody and a
        /// mending on a ward at full health cost the player nothing — which is invariant 23's
        /// rule about a continue that does not continue, applied to a consumable. Charging the
        /// run is the view's, because only a <c>ProtoView</c> may move the allowance; drawing it
        /// is the view's for the same reason it draws everything else.
        /// </para>
        /// <para>
        /// The held check is here rather than on the bar: a bar painted a moment ago is a bar
        /// that can be one sync behind, and the ledger is the only thing that can answer for
        /// certain.
        /// </para>
        /// </summary>
        SiegeUse Fire(UtilityItem item, SiegeAim aim, List<SiegeStrike> strikes)
        {
            if (item == null || _siege == null || _siege.Siege == null) return SiegeUse.Refused;
            if (UtilityLedger.WhyNotUse(item) != UtilityRefusal.None) return SiegeUse.Refused;

            var use = SiegeUtility.Apply(_siege.Siege, item, aim, strikes);
            if (!use.Landed) return SiegeUse.Refused;

            // Only now, and only once — the board moved, so the item is gone. Nothing can change
            // the stock between the check above and this, so the answer is not read: a `false`
            // here would mean the ledger disagreed with itself, and the honest response to that
            // is still to leave the board as it stands rather than to un-kill a raider.
            UtilityLedger.TryUse(item);

            LevelAnalytics.TrackUtility(Level, item.Id, use.Matches, use.Delivered);
            return use;
        }

        /// <summary>Opens the shop behind an empty slot.</summary>
        void Offer(UtilityItem item)
        {
            if (item == null) return;

            if (!item.ForSale)
            {
                Scenery.Toast(Safe, Loc.Get("ui.utility.chest_only"), Pal.Cream, 2.4f,
                              new Vector2(.5f, 0f), UtilityBar.Height + 90f);
                return;
            }

            Flow.Modal<UtilityBuyOverlay>(v =>
            {
                v.Item = item;
                v.Bought = () => { if (_bar != null) _bar.Paint(); };
            });
        }

        /// <summary>
        /// Says why a target was refused.
        ///
        /// One sentence for every kind, because the three refusals a player can actually meet —
        /// a firepot that reached nobody, a mending on an unhurt ward, a surge into a full one —
        /// are all the same news: nothing happened and nothing was taken.
        /// </summary>
        void Refuse()
        {
            Scenery.Toast(Safe, Loc.Get("ui.utility.no_target"), Pal.Cream, 2.2f,
                          new Vector2(.5f, 0f), UtilityBar.Height + 90f);
        }

        /// <summary>
        /// The bar follows the board: it takes input exactly when the run does, and arming is
        /// dropped the moment it does not.
        ///
        /// Read from <see cref="Runnable"/> rather than tracked separately, so the bar and the
        /// board cannot come to disagree about whether a run is under way — which is the second
        /// thing this screen would otherwise have to remember, and the first one is what
        /// invariant 24a is about.
        /// </summary>
        protected internal override void Running(bool running)
        {
            base.Running(running);

            if (_bar != null) _bar.Live = running;
            if (!running && _siege != null) _siege.Arming = null;
        }

        /// <summary>
        /// A fresh board is a fresh bar: whatever was armed is put down.
        ///
        /// <c>SiegeView.Compose</c> drops its own half when the board is rebuilt, and this is the
        /// other half - without it the slot would still be ringed over a board that had forgotten
        /// what it was aiming.
        /// </summary>
        protected override void Rewind()
        {
            base.Rewind();
            if (_bar != null) _bar.Arm(null);
        }

        public override void RetryAfterDefeat()
        {
            base.RetryAfterDefeat();
            if (_bar != null) _bar.Arm(null);
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

        // A siege says nothing when a gem is tapped, deliberately. It carried the drag notice
        // Prismvale still carries and the owner withdrew it: a tap that lines nothing up is
        // answered on the board already - the gem leans and comes back - and a sentence over the
        // top of a run that is *still walking* is a panel explaining a gesture the player has
        // already been shown. `RefusalKey` is null here, which is the base's own answer.

        protected override Mechanic Verb => Mechanic.SiegeFuel;
        /// <summary>
        /// None, and that is a withdrawal rather than an omission.
        ///
        /// <b>It was <c>SiegeLine</c> — "the line is your life" — and the board already says it.</b>
        /// Four turrets carrying health bars fill the middle of the screen for the whole run and
        /// visibly take hits; a panel explaining that they matter is a panel explaining a picture
        /// the player is looking at. See <see cref="Mechanic.SiegeLine"/>, whose id is spent.
        /// </summary>
        protected override Mechanic Friend => default;

        /// <summary>
        /// The two the shared screen declares, plus the one only some boards hold.
        ///
        /// <para>
        /// <b>Declared as a fact about <em>this</em> board</b>, which is what
        /// <c>ProtoScreen.Lessons</c> is for: a cog is dealt by the level rather than by the mode,
        /// so a lesson about one on a level that deals none would be spent on something that is
        /// not on the screen — and a lesson is shown once in a player's life. The first rung of
        /// this chapter deals no cogs on purpose (invariant 24's argument about attention), so
        /// this is genuinely per level and not per mode.
        /// </para>
        /// <para>
        /// It goes up <b>after</b> the two the mode already teaches, because a cog is only worth
        /// anything to somebody who already knows what a match is for.
        /// </para>
        /// </summary>
        protected internal override void Lessons(List<Lesson> into)
        {
            base.Lessons(into);

            var board = _siege != null ? _siege.Siege : null;
            if (board == null) return;

            var anchor = _siege.WardAnchor;

            // **The ring goes on a turret and the cog is drawn in the panel.** Ringing the cog
            // itself was tried and taken back out: a cog is dealt at a rate rather than authored,
            // so a rung can open with none standing - and a lesson is offered once in a player's
            // life, so one that waits for a board that may never come may never be given. What a
            // ring round a turret cannot say is what a cog *looks like*, so the panel says it.
            if (board.Upgrades && anchor != null)
                into.Add(Lesson.At(Mechanic.SiegeCog, anchor, icon: _siege.CogArt));

            // The shield is pointed at the *line* rather than at the hill, because what it is
            // really about is which ward to feed - and a bulwark is walking, so a ring drawn round
            // where it happened to be would be round bare ground a second later.
            if (board.Shielded && anchor != null)
                into.Add(Lesson.At(Mechanic.SiegeShield, anchor));
        }

        /// <summary>
        /// A siege does not run out of board — its line falls while the hill is still full, which
        /// is a different piece of news and wants different words. See
        /// <c>ProtoScreen.StuckReason</c>.
        /// </summary>
        protected override DefeatReason StuckReason => DefeatReason.WardsLost;

        // ------------------------------------------------------------------ one more go
        /// <summary>
        /// A siege is lost when its ward line falls, so the line is what a continue puts back —
        /// not moves, which this mode does not count.
        ///
        /// <para>
        /// The shortfall the shared screen computes is nought here and honestly so: every ward is
        /// down by the time the offer is made, so what is handed over is the whole allowance
        /// rather than room above a deficit. See <c>ContinueUnit.Wards</c>.
        /// </para>
        /// </summary>
        protected internal override ContinueUnit MeasuredIn => ContinueUnit.Wards;

        /// <summary>
        /// Puts the line back up, and charges the run for it in the unit the run is graded in.
        ///
        /// <para>
        /// <b>The charge is the half no other mode needs, and leaving it out would have been a
        /// silent economy hole.</b> Everywhere else a run reaches its fail state by exhausting
        /// the very counter it is graded on, so invariant 23's promise — a bought run scores one
        /// star at most — costs no code. A siege is graded in matches and lost when its ward line
        /// falls, and the two are unrelated: a player outpaced on the fourth wave may have spent
        /// five matches against a three-star line of fourteen, so twenty gems would buy a
        /// top-rung clear, and stars are what a grove's public worth is derived from (19a).
        /// <c>RunContinue.Toll</c> is that promise said out loud, and it is charged before the
        /// board comes back so that nothing can win in between.
        /// </para>
        /// <para>
        /// Not <c>base</c>, which sounds the shared whoosh: the line standing up has a sound of
        /// its own and two of them a frame apart is a flam rather than a bigger moment
        /// (invariant 37q).
        /// </para>
        /// </summary>
        protected internal override void ContinueWith(int wards)
        {
            if (_siege == null || Level == null) return;

            var run = _siege.Run;
            if (run != null)
                run.Charged(RunContinue.Toll(run.Spent, Level.Tuning.SilverThreshold));

            _siege.Grant(wards);

            // A board handed back is a board with nothing armed, exactly as a rewind and a retry
            // leave it — the panel that was up cost the player a beat, and an item still ringed
            // from before the line fell is one they did not choose to be holding.
            if (_bar != null) _bar.Arm(null);
        }

        /// <summary>
        /// Three numbers, and the middle one is not the one every other mode on this shape shows.
        ///
        /// <para>
        /// <b>The allowance is replaced by how far through the raid this is</b>, because this mode
        /// has no move allowance and the shared readout would print "free" over the one screen
        /// where something really is running out.
        /// </para>
        /// <para>
        /// <b>It used to be the ward line, and moving it is invariant 33a read the other way
        /// round.</b> That rule says the number in the corner and the picture on the board have to
        /// be the same number — and the line already <em>is</em> a picture: four turrets, each
        /// carrying a health bar, filling the middle band of the board for the whole run. A corner
        /// reading of it was a second copy of something a player was already looking at. How many
        /// waves are left is the opposite: it existed nowhere but in a banner that fades after a
        /// second and a half, so a player who looked away at the wrong moment had no way to find
        /// out whether the worst was over. What a siege owes the corner is the thing the board
        /// cannot say.
        /// </para>
        /// <para>
        /// The last wave is gold, because "this is the last one" is the one thing this number is
        /// really for — and on a level that ends with a warlord (<c>SiegeLayout.Boss</c>) it is
        /// also the warning that the last one is not like the others.
        /// </para>
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var run = _siege != null ? _siege.Run : null;
            var board = _siege != null ? _siege.Siege : null;

            into.Add(new Readout(Loc.Get(GoalCaption), run == null ? "0" : run.Left.ToString()));

            if (board == null)
            {
                into.Add(new Readout(Loc.Get("mode.cap.wave"), "-"));
            }
            else
            {
                // Nought before the first wave musters, and a run that says "wave 0" during its own
                // countdown is reading as broken rather than as early: what is true in that moment
                // is that wave one is coming.
                int wave = board.Wave < 1 ? 1 : board.Wave;
                int last = board.Waves;

                into.Add(new Readout(Loc.Get("mode.cap.wave"), wave + "/" + last,
                                     wave >= last ? Pal.Gold : Pal.Cream));
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
        /// <summary>
        /// Room at the foot for the action bar, and the number is smaller than it looks.
        ///
        /// <para>
        /// <b>Exactly the bar, and no gap at all.</b> The bar is a shelf that meets the board's
        /// own plate rather than a strip floating under it — the room this mode used to leave
        /// empty at the foot is the room it fills. `ProtoView` already insets its plate by
        /// `Margin` inside this host, so the two are separated without a number here saying so.
        /// </para>
        /// <para>
        /// Two earlier cuts, both caught by a render and neither by anything numeric (invariant
        /// 37g): the first left 190 points of nothing between the field and three loose squares,
        /// and the one before that added the bar's whole height to a margin that already very
        /// nearly held it.
        /// </para>
        /// <para>
        /// <b>And nothing at the sides either.</b> The hill, the ward line and the field run to
        /// the edges of the screen, so the board and the shelf under it are one column rather
        /// than a panel with a tray beside it. What tells them apart is that the board's plate
        /// has rounded corners and the shelf does not.
        /// </para>
        /// <para>
        /// The board is still tall because it holds a hill, a line and a field, and the hill is
        /// the half a player spends the run looking at.
        /// </para>
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(0f, UtilityBar.Height, 0f, 300f);
    }
}
