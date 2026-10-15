using System;
using System.Collections.Generic;
using GlimmerGrove.Daily;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// A handful of rewards being paid into the hub's resource pills: each prize breaks
    /// into tokens, they arc out of the card that announced them, and every landing steps
    /// that pill's own number.
    ///
    /// <para>
    /// <b>Why this is a class and not forty lines in an overlay.</b> It was forty lines in
    /// <c>ChestOverlay</c>, and then a second panel needed the same thing - the rewarded ad,
    /// which pays the same three currencies into the same three pills and had been closing
    /// with nothing but a card pop to show for it. That is the argument
    /// <see cref="TokenFlight"/> already makes one level down, and it applies harder here:
    /// the cascade is not a bezier, it is a <em>promise</em> - that the pills are rewound
    /// before the first token leaves, that the last token of a pill writes the true figure,
    /// and that the caller is told exactly once even if every landing callback is lost. Two
    /// copies of that would be two chances to leave a player looking at a hub whose numbers
    /// are lower than their balance, with no button to press.
    /// </para>
    /// <para>
    /// <b>The snapshot is why this is instantiated rather than static.</b> <see cref="Begin"/>
    /// has to run <em>before</em> the reward is granted and <see cref="Play"/> long after -
    /// the chest snapshots as it is built and pays when the button is pressed, the ad
    /// snapshots before <c>RewardedAds.Redeem</c> and pays when the player taps COLLECT.
    /// Deriving "what the pill said before" at collect time by subtracting the prize is wrong
    /// in the case that matters: a heart drop landing at the ceiling grants nothing, so the
    /// subtraction would rewind the pill below where it ever stood and then count it up to a
    /// gain nobody received.
    /// </para>
    /// <para>
    /// <b>The target is read live, at every landing, and that is what makes it safe for
    /// currency.</b> A chest's credits are in the local ledger before the animation starts;
    /// an ad's are not and must not be - invariant 10d, the client credits hearts only and
    /// asks the server for the rest. So the number is never walked towards a figure this code
    /// invented. It is walked towards whatever the balance says at the instant a token
    /// arrives: hearts climb immediately, credits climb the moment the sync lands - usually
    /// mid-cascade, because <c>Redeem</c> starts that sync a beat before the player finds the
    /// COLLECT button - and if the server has not answered yet the tokens still arrive, still
    /// flash, and the figure catches up when it does. Nothing here can tell a player they
    /// were paid something they were not.
    /// </para>
    /// </summary>
    public sealed class RewardFlight
    {
        /// <summary>How many tokens one prize throws, however much it is worth.</summary>
        public const int TokensPerDrop = 7;

        /// <summary>
        /// The rhythm, and it is the design - lifted unchanged from the chest, which is where
        /// the numbers were tuned.
        ///
        /// <para>
        /// <see cref="ClearAt"/> is how long the caller has to get its chrome out of the way
        /// before the first token leaves, and it is public because the chrome is the caller's:
        /// a token cannot be seen landing on a pill that is still behind a scrim.
        /// <see cref="TokenGap"/> spaces one prize's tokens at about fourteen a second -
        /// tighter and the landings stop being separable and the rising run of notes turns
        /// into a rattle, thinner and a five-token prize outlasts the player's interest. The
        /// cards overlap on purpose, so the screen is never empty and the run never breaks.
        /// </para>
        /// </summary>
        public const float ClearAt = 0.26f;
        const float CardGap = 0.17f, TokenGap = 0.072f, Flight = 0.54f;

        /// <summary>The beat between the last token landing and the caller being let go.</summary>
        const float FinishBeat = 0.42f;

        /// <summary>
        /// How long past the scheduled end the safety net waits before finishing anyway.
        ///
        /// A landing callback that never arrives - a token whose tween was interrupted, a
        /// pill destroyed at the wrong instant - would otherwise leave the player looking at
        /// a panel with no button on it. The reward is already banked, so there is never a
        /// reason to keep them there.
        /// </summary>
        const float Safety = 1.4f;

        readonly struct Item
        {
            public readonly ChestDrop Drop;
            public readonly RectTransform Source;
            public readonly ResourceSlots.Kind Kind;

            public Item(ChestDrop drop, RectTransform source, ResourceSlots.Kind kind)
            {
                Drop = drop;
                Source = source;
                Kind = kind;
            }
        }

        readonly List<Item> _items = new List<Item>();
        readonly long[] _before = new long[3];
        readonly int[] _tokens = new int[3];
        readonly int[] _landed = new int[3];

        /// <summary>
        /// This flight's entry on each readout, null where it has none. See
        /// <see cref="ResourceSlots.Payout"/> for why a readout keeps one per reward.
        /// </summary>
        readonly ResourceSlots.Payout[] _payouts = new ResourceSlots.Payout[3];

        /// <summary>Where each prize's tokens last left from - see <see cref="Throw"/>.</summary>
        Vector2[] _from;

        /// <summary>
        /// When the snapshot was taken, relative to every other flight's. Readouts order their
        /// payouts by it, because that is the order the grants were made in.
        /// </summary>
        readonly long _sequence;
        static long _sequences;

        int _thrown, _arrived;
        bool _played, _finished, _held;
        Action _done;
        readonly List<Lease> _leases = new List<Lease>();

        RewardFlight() { _sequence = ++_sequences; }

        /// <summary>
        /// Snapshots what every pill reads right now. Call this <em>before</em> granting the
        /// reward - see the class remarks.
        ///
        /// <para>
        /// The balance itself, not what the pill happens to be drawing: with another reward
        /// still landing the pill reads less than the balance, and a snapshot of the drawing
        /// would make this reward owe the other one's tokens as well.
        /// </para>
        /// </summary>
        public static RewardFlight Begin()
        {
            var flight = new RewardFlight();
            for (int k = 0; k < 3; k++)
                flight._before[k] = ResourceSlots.Balance((ResourceSlots.Kind)k);
            return flight;
        }

        /// <summary>
        /// The same, for a reward that has <em>already</em> been applied: the snapshot is
        /// today's balance with the grant taken back off it.
        ///
        /// <para>
        /// This exists for the shop, and only for the shop. Every other payout here is opened
        /// before its grant, which is the shape <see cref="Begin"/> insists on because a
        /// snapshot taken afterwards has to be <em>derived</em>, and deriving it is wrong the
        /// moment a rule can clamp what was granted - a heart drop landing at the ceiling
        /// grants nothing, so subtracting its amount would rewind the pill below where it ever
        /// stood and count it up to a gain nobody received. A real-money purchase cannot be
        /// opened before its grant: <c>StoreService.Granted</c> is raised <em>by</em> the
        /// grant, from a receipt that may have been validated while the app was not even
        /// running.
        /// </para>
        /// <para>
        /// So the parameters are two currency amounts rather than a set of drops, and that is
        /// the constraint expressed as a type. Credits and gems are the only two things in
        /// this game that nothing clamps - no ceiling, no cap, no rule between the server's
        /// figure and the balance - so subtracting them is exact. Hearts are not, which is why
        /// there is no way to pass one in here.
        /// </para>
        /// </summary>
        public static RewardFlight AfterGrant(long credits, long gems)
        {
            var flight = Begin();

            int c = (int)ResourceSlots.Kind.Credits, g = (int)ResourceSlots.Kind.Gems;

            // Clamped, because the balance is read now and the grant was applied a moment ago
            // - a spend landing in between would make the subtraction overshoot, and a pill
            // rewound below zero would count up from a figure that has never been true.
            flight._before[c] = Math.Max(0L, flight._before[c] - Math.Max(0L, credits));
            flight._before[g] = Math.Max(0L, flight._before[g] - Math.Max(0L, gems));

            return flight;
        }

        /// <summary>
        /// Freezes every pill at the snapshot from now until the payout plays, so nothing moves
        /// behind the panel while it is up. Call it the moment the panel that will pay exists.
        ///
        /// <para>
        /// Without this the pills were claimed only when COLLECT was pressed, and a balance that
        /// moved while the panel stood - a rewarded video's coins arriving from the server a few
        /// seconds after it closed, or a shop grant raised before its panel - repainted the pill
        /// to the new total behind the scrim; COLLECT then rewound it and counted it up again, so
        /// the player watched the number go up, down and up (reported on 2026-09-28).
        /// </para>
        /// <para>
        /// The hold is released when the cascade finishes - or, through a lease on
        /// <paramref name="owner"/>, whenever that panel is destroyed without playing, which is
        /// what keeps a pill from ever standing frozen below the balance. Idempotent.
        /// </para>
        /// <para>
        /// "Frozen" is this reward's share only: a pill another reward is still landing in goes
        /// on climbing by that one's tokens, and stops short of this one's prize.
        /// </para>
        /// </summary>
        public void Hold(Component owner)
        {
            if (_held || _finished || owner == null) return;
            _held = true;

            for (int k = 0; k < 3; k++)
                _payouts[k] = ResourceSlots.Claim((ResourceSlots.Kind)k, _sequence, _before[k]);

            TieTo(owner);
        }

        /// <summary>
        /// Ties the flight to <paramref name="owner"/>'s lifetime: if it is destroyed first, the
        /// flight lets every readout go. A flight can hold several (the panel that held the pills,
        /// the space the tokens are drawn in), and the first to go ends it.
        /// </summary>
        void TieTo(Component owner)
        {
            if (owner == null) return;
            var lease = owner.gameObject.AddComponent<Lease>();
            lease.Flight = this;
            _leases.Add(lease);
        }

        /// <summary>Releases a flight whose panel or space went away before it finished. The
        /// reward is banked either way; this only puts the pills back on the live balance.</summary>
        void Abandon()
        {
            _done = null;
            Finish(0f);
        }

        /// <summary>Dies with whatever the flight depends on - see <see cref="TieTo"/>.</summary>
        sealed class Lease : MonoBehaviour
        {
            public RewardFlight Flight;

            void OnDestroy()
            {
                var flight = Flight;
                Flight = null;
                flight?.Abandon();
            }
        }

        /// <summary>
        /// Where a token leaves from once its own card has gone. The chest hands over the row
        /// its cards were laid out in; a panel with a single card needs nothing here.
        /// </summary>
        public RectTransform Fallback { get; set; }

        /// <summary>True once at least one prize has a pill on screen to fly into.</summary>
        public bool Any => _items.Count > 0;

        /// <summary>
        /// Adds one prize, thrown from <paramref name="source"/>.
        ///
        /// Returns false - and adds nothing - for a prize with no readout on the hub (seconds
        /// on a run, a hint) or when the hub is not the screen underneath. Both are ordinary,
        /// and the caller falls back to simply closing, which is what it did before this
        /// existed: a reward that has already been banked must never depend on an animation
        /// being able to run.
        /// </summary>
        public bool Add(ChestDrop drop, RectTransform source)
        {
            if (_played || !drop.IsValid) return false;
            if (!RewardArt.Slot(drop.Kind, out var kind)) return false;
            if (!ResourceSlots.TryGet(kind, out _)) return false;

            _items.Add(new Item(drop, source, kind));
            return true;
        }

        /// <summary>
        /// The budget, or the prize itself when the prize is smaller. Three hearts throw
        /// three hearts - throwing seven and landing them in fractions is the one case where
        /// the count in the air and the count on the pill visibly disagree. A boost is one
        /// token because it is one thing, however many hours it runs for.
        /// </summary>
        public static int TokenCount(ChestDrop drop)
            => !drop.IsValid ? 0
             : drop.Kind == ChestDropKind.HeartBoost ? 1
             : Mathf.Clamp(drop.Amount, 1, TokensPerDrop);

        /// <summary>
        /// How long the cascade takes if every token flies and lands as scheduled. Public so a
        /// caller can hold for exactly this rather than for a number somebody guessed and then
        /// had to keep in step.
        /// </summary>
        public float Duration
        {
            get
            {
                int widest = 1;
                for (int i = 0; i < _items.Count; i++)
                    widest = Mathf.Max(widest, TokenCount(_items[i].Drop));

                return ClearAt
                     + Mathf.Max(0, _items.Count - 1) * CardGap
                     + (widest - 1) * TokenGap
                     + Flight;
            }
        }

        /// <summary>
        /// Rewinds the pills, empties the cards into them, and calls <paramref name="done"/>
        /// exactly once when it is over.
        ///
        /// <para>
        /// <paramref name="space"/> is what the tokens are drawn in - the caller's own content
        /// node, so they sit above the panel they came from - and it is also the owner every
        /// tween here is scheduled against, so the cascade dies with the panel rather than
        /// outliving it.
        /// </para>
        /// <para>
        /// <b>Nothing the player does next has to wait for this.</b> The reward is banked
        /// before the first token leaves, the readouts are a ledger rather than a lock (see
        /// <see cref="ResourceSlots.Payout"/>), and every way the cascade can end - the last
        /// landing, the space being destroyed, the deadline - lets the readouts go exactly once.
        /// So a panel paying through this can step out of the player's way the moment it starts
        /// (<see cref="Flow.Depart"/>), and a page can take its next tap while tokens are still
        /// in the air.
        /// </para>
        /// </summary>
        public void Play(RectTransform space, Action done)
        {
            if (_played) return;
            _played = true;

            // A hold already released by its panel going away: the pills are back on the live
            // balance and must not be claimed again with nothing left to release them.
            if (_finished) { done?.Invoke(); return; }

            _done = done;

            if (space == null || _items.Count == 0) { Finish(0f); return; }

            for (int i = 0; i < _items.Count; i++)
                _tokens[(int)_items[i].Kind] += TokenCount(_items[i].Drop);

            // Rewound to what the pills said before the grant, and entered on the readouts
            // while they are there. The grant already happened - deliberately, so a player who
            // kills the app has still opened the chest and has still been paid for the video -
            // and the hub repaints the instant the wallet moves, so by now the pills already
            // read the new totals. The player has been looking at a scrim this whole time and
            // has seen neither figure, so nothing is being faked: the report simply arrives in
            // the order the events were experienced.
            //
            // The entry is what stops the hub writing over the rewind from underneath: every
            // writer asks the readout's ledger, so a wallet change landing mid-cascade moves the
            // pill only by what is no longer owed. A pill this reward pays nothing into is let
            // go now rather than at the end, so it does not stand still for the whole cascade.
            for (int k = 0; k < 3; k++)
            {
                var kind = (ResourceSlots.Kind)k;

                if (_tokens[k] <= 0)
                {
                    ResourceSlots.Release(kind, _payouts[k]);
                    _payouts[k] = null;
                    continue;
                }

                if (_payouts[k] == null) _payouts[k] = ResourceSlots.Claim(kind, _sequence, _before[k]);
                _payouts[k].Tokens = _tokens[k];
                _payouts[k].Landed = 0;
                ResourceSlots.Show(kind, ResourceSlots.Walked(kind));
            }

            // Where each prize leaves from, read now while every card is certainly there. A
            // page that pays from its own rows may redraw them while tokens are still leaving -
            // it no longer waits for the cascade - and a token whose card has gone leaves from
            // where the card last stood rather than from the middle of the screen.
            _from = new Vector2[_items.Count];
            for (int i = 0; i < _items.Count; i++) _from[i] = SourceIn(space, _items[i].Source);

            // Ended by the space as well as by the panel: tokens drawn in a node that is
            // destroyed never land, and their readouts must not wait on them.
            TieTo(space);

            for (int i = 0; i < _items.Count; i++) _thrown += Throw(space, i);

            if (_thrown == 0) { Finish(0f); return; }

            // Nothing is played here. The button that was pressed has already made its one
            // sound, and the cascade starts a quarter-second later as its own event - see
            // Btn.ClickSfx for why a tap does not get to make two noises.
            //
            // Unowned on purpose: this is the net under every other ending, so it may not die
            // with the space whose loss it exists to survive. Finish is latched, so a cascade
            // that ended properly makes it a no-op.
            Tween.After(Duration + Safety, () => Finish(0f));
        }

        Vector2 SourceIn(RectTransform space, RectTransform card)
            => card ? TokenFlight.LocalIn(space, card)
             : Fallback ? TokenFlight.LocalIn(space, Fallback)
             : Vector2.zero;

        // --------------------------------------------------------------- internals
        /// <summary>
        /// Breaks one prize's card into tokens and sends them at its pill. Returns how many
        /// were thrown, so the cascade knows when the last one has landed.
        /// </summary>
        int Throw(RectTransform space, int index)
        {
            var item = _items[index];
            var card = item.Source;

            int count = TokenCount(item.Drop);
            int slot = (int)item.Kind;
            float start = ClearAt + index * CardGap;

            RewardArt.Token(item.Drop.Kind, item.Drop.Item, out var sprite, out var tint);
            var tone = RewardArt.Tint(item.Drop.Kind, item.Drop.Item);

            // The card leaves as its tokens do, so the prize is not still sitting there while
            // copies of it fly away.
            if (card) Tween.After(start, () =>
            {
                if (!card) return;
                Tween.Punch(card, .22f, .26f);
                Burst.Sparks(card, Vector2.zero, tone, 12, 240f, 22f, .55f);

                // Delayed past the punch rather than overlapping it. Both write localScale,
                // and two tweens writing one value fight for it every frame they share - the
                // card would jitter as it left. The overlap is not wasted time either: the
                // first tokens leave while the card is still there, which is what makes it
                // read as being emptied rather than as being deleted.
                var group = UIKit.Group(card);
                Tween.Run(.30f, Ease.InQuad, t =>
                {
                    if (!card) return;
                    card.localScale = Vector3.one * Mathf.Lerp(1f, .1f, t);
                    if (group) group.alpha = 1f - t;
                }, card, "leave").Delay(.26f);
            }, space);

            for (int j = 0; j < count; j++)
            {
                int step = j;

                Tween.After(start + step * TokenGap, () =>
                {
                    if (space == null) return;

                    // The card is still shrinking as its tokens leave, so its position is read
                    // now rather than captured. Once it has gone the token leaves from where it
                    // last stood (read at Play, refreshed on every throw) - a page redrawing its
                    // rows under the cascade must not send the rest of a handful from the
                    // middle of the screen. Either way the token still flies and lands, or the
                    // cascade would wait for its deadline.
                    if (card) _from[index] = TokenFlight.LocalIn(space, card);
                    Vector2 from = _from[index];

                    // Resolved late for the same reason, and this one is load-bearing: the hub
                    // can replace the pill a token was aimed at while it is in the air.
                    Vector2 to = ResourceSlots.TryGet(item.Kind, out var live) && live.Icon
                        ? TokenFlight.LocalIn(space, live.Icon)
                        : from;

                    TokenFlight.Throw(space, from, to, sprite, tint, 56f, step, 0f, Flight,
                                      () => Land(slot));
                }, space);
            }

            return count;
        }

        /// <summary>
        /// One token has arrived. The only place a pill's number moves, for the reason
        /// <c>Payout.Land</c> gives: a roll running on its own clock beside a particle effect
        /// drifts on a slow frame and reads as two unrelated animations.
        /// </summary>
        void Land(int slot)
        {
            if (_finished) return;

            _landed[slot]++;
            _arrived++;

            bool lastOfPill = _landed[slot] >= _tokens[slot];
            bool lastOfAll = _arrived >= _thrown;

            var kind = (ResourceSlots.Kind)slot;

            // The ledger's figure, read now rather than captured at Play. See the class
            // remarks: for currency the balance may only have moved a moment ago, and it may not
            // have moved yet at all - and another reward may be landing in this pill too. The
            // last token of a pill lets this flight's entry go, so what it writes owes this
            // reward nothing: exactly the balance, less only what other rewards still have in
            // the air.
            var payout = _payouts[slot];
            if (payout != null)
            {
                payout.Landed = _landed[slot];
                if (lastOfPill)
                {
                    _payouts[slot] = null;
                    ResourceSlots.Release(kind, payout);
                }
            }

            ResourceSlots.Land(kind, ResourceSlots.Walked(kind), lastOfPill);

            // The run of notes climbs across the whole cascade rather than restarting per
            // prize, so six coins and two gems are one ascending phrase instead of two. It is
            // driven off the landing counter, not a timer, so the ear hears the rhythm the eye
            // is seeing however long the flight took on the day.
            float k = _thrown <= 1 ? 1f : (_arrived - 1) / (float)(_thrown - 1);
            Audio.Sfx("coin", .46f, Mathf.Lerp(.92f, 1.88f, k));

            if (!lastOfAll) return;

            // No haptic anywhere in here, and it is a decision rather than an omission.
            // Handheld.Vibrate is a single fixed-length pulse on Android that cannot be
            // shortened or softened, so several inside a few seconds do not read as several
            // taps - they overlap into one continuous rumble. Payout.Land refused one for a
            // milder version of the same reason.
            // No capstone. The token run is already an ascending phrase and it lands on its
            // own highest note, so a chime on top of the last one was a second ending for a
            // thing that had just ended.
            Finish(FinishBeat);
        }

        /// <summary>
        /// Settles every pill on the live balance, hands the readouts back to the hub, and
        /// tells the caller. Latched, because two of the ways in here can both happen: the
        /// last landing and the safety net.
        /// </summary>
        void Finish(float beat)
        {
            if (_finished) return;
            _finished = true;

            // Settled before letting go. The pills stand at rewound figures until the tokens
            // walk them forward, so a cascade that did not finish would leave the hub reading
            // less than the truth until something happened to repaint it - which, on a screen
            // the player may now just sit on, could be a long time.
            // Only this flight's own entries: another reward still landing keeps its share.
            for (int k = 0; k < 3; k++)
            {
                var payout = _payouts[k];
                if (payout == null) continue;
                _payouts[k] = null;
                ResourceSlots.Release((ResourceSlots.Kind)k, payout);
            }

            // Disarmed rather than left to fire into a finished flight, and taken off a page
            // that lives on (a screen paying several nights in a visit) so they do not pile up.
            for (int i = 0; i < _leases.Count; i++)
            {
                var lease = _leases[i];
                if (lease == null) continue;
                lease.Flight = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(lease);
            }
            _leases.Clear();

            var done = _done;
            _done = null;
            if (done == null) return;

            // Unowned on purpose: the reward is banked and the caller has to hear about it
            // even if the panel it belongs to is torn down in this same frame.
            if (beat > 0f) Tween.After(beat, done);
            else done();
        }
    }
}
