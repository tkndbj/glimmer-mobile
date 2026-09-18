using System.Collections;
using System.Collections.Generic;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Wards;
using GlimmerGrove.AssetPipeline;
using System;
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
        /// <summary>
        /// A siege is fought in silence, and the bed does not merely change - it stops.
        ///
        /// <para>
        /// <b>Asked for by the owner, and the mode is the one here with an argument for it.</b>
        /// Every other screen in the game is something a player reads at their own pace; this one
        /// runs on a clock nothing stops (invariant 37), and what it asks of a player is to hear
        /// which colour they just fed, notice a tube filling and catch a wave arriving. A bed
        /// under all of that is one more thing in a mix that is already the busiest in the game -
        /// about eighteen bolts a second across four lit wards.
        /// </para>
        /// <para>
        /// <b><see cref="WantsSilence"/> rather than <c>Track</c>, because null does not mean
        /// quiet.</b> This screen inherits <c>ModeScreen</c>'s <c>mus_mode</c>; overriding that to
        /// null would leave whatever the map was playing running underneath, which is the opposite
        /// of the ask. See the property on <c>View</c>.
        /// </para>
        /// <para>
        /// <b>It is a property of this screen and not of running a level.</b> The hidden modes
        /// (invariant 38) are turn-based boards with no clock, and nothing about them asked for
        /// this - putting it on <c>ModeScreen</c> would silence three modes nobody played it on.
        /// </para>
        /// </summary>
        public override bool WantsSilence => true;

        SiegeView _siege;
        UtilityBar _bar;

        /// <summary>
        /// The four turrets this run draws, held for the length of the run.
        ///
        /// Four of the eighty the roster holds — the bound invariant 7b asks for, and the reason
        /// the shelf that browses them reads thumbnails instead.
        /// </summary>
        AssetHold _line;

        void OnDestroy() => _line?.Dispose();

        protected override ProtoView Attach(GameObject host)
        {
            _siege = host.AddComponent<SiegeView>();
            _siege.Rung = Rung();
            _siege.CastSet = CastFor();

            // The four turrets this player put on the line, and only those four (invariant 7b).
            // Asked for here rather than awaited: the board is built on the starter's art, which
            // is resident in the mode's own cast, and repaints itself when the scope lands - an
            // Image with a null sprite is a white rectangle rather than a blank, so a board that
            // waited would be a board that showed nothing at all on a slow load.
            Line();
            return _siege;
        }

        /// <summary>
        /// Which cast this level draws.
        ///
        /// <b>Asked of <see cref="SiegeMode.CastFor"/> rather than worked out here</b>, because the
        /// same answer decides what <c>SiegeMode.ArtFor</c> preloads: a screen with its own opinion
        /// would draw a cast the level never loaded, which is a white rectangle over every raider
        /// on the hill (invariant 7b). A level outside a chapter draws the insects.
        /// </summary>
        int CastFor()
        {
            if (Level == null) return SiegeMode.Insects;

            return SiegeMode.CastFor(GameContent.Index.TrackOf(Level.Chapter),
                                     GameContent.Index.ChapterOrderOf(Level.Chapter));
        }

        /// <summary>
        /// Loads the player's four turrets, and repaints the line when they arrive.
        ///
        /// <b>`async void` with the exception caught</b>, which is <c>CompanionArt.Load</c>'s
        /// shape and for its reason: a scope that failed to load must not vanish silently, and
        /// the board behind it is already drawing a working line.
        /// </summary>
        void Line() => Run(async token =>
        {
            _line = _line ?? AssetLibrary.Hold("siege_line");
            await _line.LoadAsync(WardLoadout.Line.Art(), null, token);

            if (_siege != null) _siege.Redress();
        });

        /// <summary>
        /// Records what this run can say about whether the hill was ever looked at.
        ///
        /// <para>
        /// Here rather than in <c>RunLedger</c> for the reason every mode-specific reading is:
        /// the ledger is mode-blind (invariant 20a), and a siege is the only board in this game
        /// with somewhere else to look.
        /// </para>
        /// </summary>
        protected override void RunEnded(bool won)
        {
            var board = _siege != null ? _siege.Siege : null;
            if (board == null || Level == null) return;

            LevelAnalytics.TrackSiegeAttention(Level, board.Attention, won);

            // The hill's half of the tasks, beside the analytics that already read the same
            // counters: what was felled, sprung, taken and tapped, and how many matches paid
            // for it. The run's own half (finished, won, starred) is the ledger's, one call
            // later, where every mode reports it.
            Tasks.TaskLedger.RecordSiege(board.Attention, board.WavesCleared,
                                         _siege.Run != null ? _siege.Run.Spent : 0);
        }

        /// <summary>Whether this rung's waves never stop.</summary>
        bool Endless
        {
            get
            {
                var board = _siege != null ? _siege.Siege : null;
                if (board == null) return false;

                // Bound to a local rather than reached through, which `compile.py`'s coarse
                // `.Layout.` guard also wants: it cannot tell a siege's own layout from a glade's
                // board, and the guard is deliberately coarse.
                var sends = board.Layout;
                return sends != null && sends.IsEndless;
            }
        }

        /// <summary>
        /// What an endless run is graded on: waves seen off, not matches spent.
        ///
        /// <b>The one lane in this game where a bigger count is a better run</b> — see
        /// <c>LevelTuning.Climbs</c> for why the direction is a property of the level rather than
        /// a special case at a call site.
        /// </summary>
        protected override int Scored(ProtoRun run)
        {
            if (!Endless) return base.Scored(run);

            var board = _siege.Siege;
            return board.WavesCleared;
        }

        /// <summary>
        /// Keeps how far this run got, and adds what it saw off to the lifetime tally.
        ///
        /// <para>
        /// <b>Two floors, and only one of them pays</b> (invariant 14a). The best is the number
        /// the public board is ordered on and it pays nothing, which is what keeps a figure the
        /// server cannot recompute safe to publish (invariant 19l). The tally pays XP, at a rate
        /// and under a ceiling that are both content — see <c>EndlessRewardTable</c>.
        /// </para>
        /// <para>
        /// <b>The tally is banked unconditionally and the best is not</b>, which is the whole
        /// reason they are two calls: a run that fell short of the best still happened, and
        /// folding the tally into <c>Record</c>'s early return is how every run after a good one
        /// would have paid nothing.
        /// </para>
        /// <para>
        /// <b>Banked from the board rather than from <paramref name="count"/>.</b> The count
        /// arrives floored at one by <c>ProtoScreen.Solve</c>, because a graded count of nought
        /// is not a grade — so paying on it would pay a wave for a run that saw off none, which
        /// is the cheapest thing in this game to repeat. <c>WavesCleared</c> is the truth, and it
        /// is what <see cref="Scored"/> handed over in the first place.
        /// </para>
        /// <para>
        /// Reached once per run, from the one place a run ends: an endless watch finishes when
        /// the ward line falls (<c>SiegeBoard.IsFinished</c>), a continue puts the line back up
        /// and the same run carries on, and <c>ProtoScreen</c>'s own latch makes the ending
        /// single.
        /// </para>
        /// </summary>
        protected override void Finished(int count)
        {
            if (!Endless || Level == null) return;

            EndlessLedger.Record(Level.Id, count);

            var board = _siege != null ? _siege.Siege : null;
            if (board != null) EndlessLedger.Bank(Level.Id, board.WavesCleared);
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
        /// <b>The bar hangs off the full-bleed layer rather than off the board host</b>, because
        /// the host is what a mode's view fills and a view that resized itself around a bar would
        /// be a view that has to know about one. What the board gives up for it is
        /// <see cref="HostInset"/>'s bottom margin, which is the only line either of them shares.
        /// </para>
        /// <para>
        /// <b><c>Content</c> rather than <c>Safe</c>, and that is what closed the gap.</b> Hung
        /// off the safe layer the shelf stopped where the layer did, so on any phone with a home
        /// indicator there was a band of backdrop under it with nothing in it — reported from a
        /// device as a gap at the foot of the screen. A flat plate under a home indicator is the
        /// full-bleed case the safe layer's own remarks describe; what belongs inside the inset is
        /// the cells, and <c>UtilityBar.Foot</c> is what they get. See <c>UtilityBar.Room</c> for
        /// why the board's inset is not the bar's height.
        /// </para>
        /// </summary>
        protected override void Play()
        {
            base.Play();
            if (_siege == null) return;

            if (_bar == null)
            {
                var node = UIKit.Node("Utilities", Content);
                _bar = node.gameObject.AddComponent<UtilityBar>();
                _bar.Build(Content);

                _bar.Aiming = Aim;
                _bar.Wanted = Offer;
            }

            _siege.Fire = Fire;
            _siege.Rejected = Refuse;

            // A tapped bomb throws what a firepot throws, so it borrows the firepot's own item
            // for its targeting layer and its magnitude - and pays for it in matches through a
            // path of its own, because nothing about it is owned. See `SiegeView.Salvo`.
            _siege.Blew = Blew;

            // The three moments this mode's remaining lessons hang on. Each fires once for the
            // life of the screen; `RunLessons.Teach` is what refuses one already seen.
            _siege.Brimmed = () => Teaching?.Teach(Mechanic.SiegeBrim);
            _siege.Salvaged = () => Teaching?.Teach(Mechanic.SiegeSalvage);
            _siege.Bombed = () => Teaching?.Teach(Mechanic.SiegeBomber);

            // **One lesson per charm, raised when one is dealt** - a charm arrives at a rate
            // rather than at a moment, so a panel offered at the opening would be about a thing
            // that is not on the screen (invariant 6b). Which lesson is `Taught`, so the mapping
            // lives in one place and a fourth charm cannot be half-added.
            _siege.Dealt = charm => Teaching?.Teach(Taught(charm));

            // **What a bomb hits for is the published firepot's number, not a constant.** The two
            // are the same blast and the player is told so; a second figure is a second thing a
            // content push can move half of.
            var pot = Firepot();
            if (pot != null && _siege.Siege != null) _siege.Siege.BombDamage = pot.Magnitude;

            // The bar owns what is armed; the board only mirrors it. Without this the view
            // disarmed itself and the slot kept its ring, which read as an item stuck on.
            _siege.Done = () => { if (_bar != null) _bar.Arm(null); };

            _bar.Arm(null);
            _bar.Cooled();
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
                          new Vector2(.5f, 0f), UtilityBar.Room + 90f);
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

            // **Asked here as well as on the bar**, for the reason the held check is: this is
            // the transaction, and a bar is a picture of what was true when it was last
            // painted. A cooling item reaching this point would be a bug rather than a race
            // today, and the honest answer to a bug that spends something is still to refuse.
            if (_bar != null && !_bar.Cooling.Ready(item)) return SiegeUse.Refused;

            var use = SiegeUtility.Apply(_siege.Siege, item, aim, strikes);
            if (!use.Landed) return SiegeUse.Refused;

            // Only now, and only once — the board moved, so the item is gone. Nothing can change
            // the stock between the check above and this, so the answer is not read: a `false`
            // here would mean the ledger disagreed with itself, and the honest response to that
            // is still to leave the board as it stands rather than to un-kill a raider.
            UtilityLedger.TryUse(item);

            // One line later and for the same reason: a use that landed is a use that is paid
            // for in both currencies, and one that was refused is paid for in neither.
            if (_bar != null) _bar.Spent(item);

            LevelAnalytics.TrackUtility(Level, item.Id, use.Matches, use.Delivered);
            return use;
        }

        /// <summary>
        /// What a bomb the player tapped delivers.
        ///
        /// <para>
        /// <b>The firepot's blast, charged the firepot's way, and spending nothing.</b> The three
        /// halves of a utility are the effect, the price in stock and the price in matches; a
        /// bomb was dropped on the board rather than bought, so it has no stock to spend and no
        /// cooldown to start — and it has exactly the same price in matches, because that price
        /// is what stops damage the player did not match for improving their grade (invariant 39,
        /// and 19a for why a grade here is not a private number).
        /// </para>
        /// <para>
        /// <b>A blast that reached nobody is refused</b>, exactly as a firepot's is — but the
        /// bomb is already off the board by then, which is the one place the two differ and it is
        /// deliberate: the alternative is putting a bomb back on a cell the field has since
        /// filled. What it costs is a wasted tap on an empty stretch of hill, which is the same
        /// thing a wasted firepot costs minus the firepot.
        /// </para>
        /// </summary>
        /// <summary>
        /// The firepot in the published catalog, whatever it is called there.
        ///
        /// <b>Found by <em>kind</em> and never by id</b>, because what a bomb throws is "the
        /// blast this build knows about" — the roster is content, so an id typed in here would be
        /// a second copy of a name a config push can change, and its failure is a bomb that does
        /// nothing when tapped.
        /// </summary>
        static UtilityItem Firepot()
        {
            var items = UtilityLedger.Catalog.Items;

            for (int i = 0; i < items.Count; i++)
                if (items[i].Kind == UtilityKind.Blast) return items[i];

            return null;
        }

        SiegeUse Blew(int id, List<SiegeStrike> strikes)
        {
            if (_siege == null || _siege.Siege == null) return SiegeUse.Refused;

            int absorbed = _siege.Siege.Detonate(id, strikes);
            if (absorbed <= 0) return SiegeUse.Refused;

            LevelAnalytics.TrackUtility(Level, "bomb", SiegeUtility.MatchesFor(absorbed), absorbed);

            return new SiegeUse(true, SiegeUtility.MatchesFor(absorbed), absorbed, -1);
        }

        /// <summary>
        /// Opens the shop behind an empty slot.
        ///
        /// <b>The raid stops while it is up, and not because of anything here.</b>
        /// <c>RunHold.Covered</c> holds any run behind any panel, asked once a frame by
        /// <c>RunScreen</c> — so this is one <c>Flow.Modal</c> call and stays one, and a panel
        /// added to this mode next year inherits the same answer without being told.
        /// </summary>
        void Offer(UtilityItem item)
        {
            if (item == null) return;

            if (!item.ForSale)
            {
                Scenery.Toast(Safe, Loc.Get("ui.utility.chest_only"), Pal.Cream, 2.4f,
                              new Vector2(.5f, 0f), UtilityBar.Room + 90f);
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
                          new Vector2(.5f, 0f), UtilityBar.Room + 90f);
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

            if (_bar != null)
            {
                _bar.Live = running;

                // **The bar is given the run's own seconds, not a wall clock**, so a cooldown
                // cannot be paid off by opening a panel: a modal holds the run
                // (`RunHold.Covered`), `running` goes false with it, and the counting stops with
                // everything else. Unscaled, because a modal also sets `Time.timeScale` to
                // nought and every board in this project is on the unscaled clock for it.
                if (running) _bar.Tick(Time.unscaledDeltaTime);
            }

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
            if (_bar == null) return;

            _bar.Arm(null);

            // And every cooldown with it. A restart is a new board, so a firepot thrown at the
            // one that was thrown away is not a debt this one inherits — which would make
            // restarting a thing the bar punished.
            _bar.Cooled();
        }

        public override void RetryAfterDefeat()
        {
            base.RetryAfterDefeat();
            if (_bar == null) return;

            _bar.Arm(null);
            _bar.Cooled();
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

            // **The cog's lesson is deferred, and it rings the cog.** It used to be dealt into
            // the gem field, so a rung either had one standing or would refill one within seconds
            // and a ring on the middle of the ward line was the best that could be said about a
            // thing that might not be there yet. It is dropped by a felled raider onto the hill
            // now, so `SiegeView.Salvaged` raises it at the moment one lands and there is a real
            // object to point at - which may be on the second rung of the chapter or never.
            into.Add(Lesson.Later(Mechanic.SiegeSalvage, _siege.LiveCog()));

            // **And the overcharge rings the turret holding one.** Same shape and same reason: the
            // charge is banked by play rather than dealt by the level, so there is no moment at
            // the opening when a full tube exists to ring - `SiegeView.Brimmed` raises it the
            // first time one fills.
            into.Add(Lesson.Later(Mechanic.SiegeBrim, _siege.ArmedWard()));

            // **Deferred, and pointed at the bomb rather than at the bomber that left it.** It
            // used to ring the raider and go up the moment one walked on, which put the panel in
            // front of a player seconds before the thing it tells them to tap existed - reported
            // from a device as the tip highlighting the wrong unit. `SiegeView.Bombed` raises it
            // on the *drop*, so the ring is round a live bomb and the sentence is something to
            // act on; `Later` keeps it out of the opening chain, and a player who never kills a
            // bomber is never told about a bomb they do not have.
            into.Add(Lesson.Later(Mechanic.SiegeBomber, _siege.LiveBomb()));

            // **And the rubble, pointed at the buried post.** Same shape as the bomb: the thing
            // to tap exists only once a colossus has landed a boulder, so `SiegeView.Buried`
            // raises it on the landing and the ring goes round a post that really is under
            // rubble. A player whose chapters never send a colossus is never told about it.
            into.Add(Lesson.Later(Mechanic.SiegeRubble, _siege.BuriedWard()));

            // **And one per charm this level actually deals.** A lesson is shown once in a
            // player's life, so offering the lance's on a chapter that deals no lance would spend
            // it on something that can never appear - which is the cog's own rule, and the reason
            // this list is a fact about *this board* rather than about the mode.
            // Held in a local rather than reached through `board.Layout.` at the call site: the
            // offline compile refuses that shape anywhere in a screen, because
            // `LevelDefinition.Layout` is null on any level that is not a glade and the check is
            // deliberately coarse about which `Layout` it is looking at.
            var plan = board.Layout;
            var charms = plan.Charms;

            for (int i = 0; charms != null && i < charms.Length; i++)
                into.Add(Lesson.Later(Taught(charms[i]), _siege.LiveCharm(charms[i])));
        }

        /// <summary>
        /// Which lesson a charm's arrival raises.
        ///
        /// <b>A table rather than a <c>default</c> that answers something</b>, which is invariant
        /// 44e: a fourth charm falling through here would quietly show a player the prism's panel
        /// and mark it seen, and a lesson id that has travelled in a save can never be re-pointed
        /// (invariant 6a). <c>default</c> is not a valid <c>Mechanic</c>, so it is refused by
        /// <c>Teach</c> rather than shown.
        /// </summary>
        static Mechanic Taught(SiegeCharm charm)
        {
            switch (charm)
            {
                case SiegeCharm.Prism: return Mechanic.SiegePrism;
                case SiegeCharm.Lance: return Mechanic.SiegeLance;
                case SiegeCharm.Storm: return Mechanic.SiegeStorm;
                case SiegeCharm.Furnace: return Mechanic.SiegeFurnace;
                case SiegeCharm.Hourglass: return Mechanic.SiegeHourglass;
                case SiegeCharm.Anvil: return Mechanic.SiegeAnvil;

                case SiegeCharm.None:
                default: return default;
            }
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
        /// <b>One number, in the middle, and it is the wave.</b> Invariant 33a says the number in
        /// the corner and the picture on the board have to be the same number, and 37v already
        /// applied that once — the ward line came off the header because the line itself
        /// <em>is</em> a picture, four turrets carrying health bars across the middle of the
        /// board for the whole run. The other two go for the same reason and it took a device to
        /// see it: <b>"left to clear" is the raiders still walking down the hill</b>, which is the
        /// largest thing on the screen, so it was a second copy of what the player was already
        /// looking at.
        /// </para>
        /// <para>
        /// <b>What is genuinely given up is the matches count, and that is a real trade rather
        /// than a tidy-up.</b> It is the number a siege is <em>graded</em> on (invariant 37a), so
        /// dropping it means a player cannot watch their own star line during a run — they meet it
        /// on the victory panel. The argument for going anyway is that it is the one reading here
        /// nobody can act on: a match is worth the colour it was, never the count, so there is no
        /// play a player would change on seeing it. If it comes back it belongs somewhere the eye
        /// is already going, not in a third column.
        /// </para>
        /// <para>
        /// So the row holds one, and <c>ReadoutRow.XFor</c> puts a row of one in the middle, where
        /// the eye already is. How far through the raid this is is the thing the board cannot say
        /// — it existed nowhere but in a banner that fades after a second and a half, so a player
        /// who looked away at the wrong moment had no way to find out whether the worst was over.
        /// </para>
        /// <para>
        /// The last wave is gold, because "this is the last one" is the one thing this number is
        /// really for — and on a level that ends with a warlord (<c>SiegeLayout.Boss</c>) it is
        /// also the warning that the last one is not like the others.
        /// </para>
        /// </summary>
        protected override void Readouts(List<Readout> into)
        {
            var board = _siege != null ? _siege.Siege : null;

            if (board == null)
            {
                // Asked once while the header is being built, before `Play` has made a board.
                into.Add(new Readout(Loc.Get("mode.cap.wave"), "-"));
                return;
            }

            // Nought before the first wave musters, and a run that says "wave 0" during its own
            // countdown is reading as broken rather than as early: what is true in that moment is
            // that wave one is coming.
            int wave = board.Wave < 1 ? 1 : board.Wave;

            // **The lemniscate rather than the count, and it is drawn rather than written.** An
            // endless lane's authored wave list is empty, so `Waves` is nought and the header read
            // "8/0" — a fraction whose denominator says the run is over. There is no last wave to
            // name, so the sign says so; and it is the one glyph here that needs no translating,
            // which is why it is a literal rather than a key.
            if (board.IsEndless)
            {
                into.Add(new Readout(Loc.Get("mode.cap.wave"), wave + "/\u221e"));
                return;
            }

            int last = board.Waves;

            into.Add(new Readout(Loc.Get("mode.cap.wave"), wave + "/" + last,
                                 wave >= last ? Pal.Gold : Pal.Cream));
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
        /// <b>`Room` rather than `Height`, because this host lives inside the safe layer and the
        /// bar does not.</b> The bar's rect starts at the bottom of the display; this margin is
        /// measured from the bottom of the safe area, which is already the display's own inset up
        /// from there. Insetting by the bar's whole height would count that inset twice and push
        /// the board's foot up behind the shelf — and on every device with nothing in the way the
        /// two answers are the same number, so nothing would say so.
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
        /// <para>
        /// <b>The top went 300 -> 236 when the readout row moved up beside the header keys</b>
        /// (<c>ModeScreen.ReadoutsY</c>), which is where the enemy ground's extra height came
        /// from. It is <em>six</em> units below the row's foot rather than the thirty-four it
        /// used to be *above* it, and that direction is the whole point: <c>ProtoView</c> sizes
        /// its plate to fill this host exactly, so 300 against a row ending at 334 meant the
        /// plate was drawn over the captions and they had been invisible on this mode since it
        /// shipped. A host inset here is a hard edge, not a margin — anything the row leaves
        /// below it is painted over.
        /// </para>
        /// </summary>
        protected override Vector4 HostInset
            => new Vector4(0f, UtilityBar.Room, 0f, 236f);
    }
}
