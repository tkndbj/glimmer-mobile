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
    /// <c>ChestOverlay</c>, and then a second panel needed the same thing — the rewarded ad,
    /// which pays the same three currencies into the same three pills and had been closing
    /// with nothing but a card pop to show for it. That is the argument
    /// <see cref="TokenFlight"/> already makes one level down, and it applies harder here:
    /// the cascade is not a bezier, it is a <em>promise</em> — that the pills are rewound
    /// before the first token leaves, that the last token of a pill writes the true figure,
    /// and that the caller is told exactly once even if every landing callback is lost. Two
    /// copies of that would be two chances to leave a player looking at a hub whose numbers
    /// are lower than their balance, with no button to press.
    /// </para>
    /// <para>
    /// <b>The snapshot is why this is instantiated rather than static.</b> <see cref="Begin"/>
    /// has to run <em>before</em> the reward is granted and <see cref="Play"/> long after —
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
    /// an ad's are not and must not be — invariant 10d, the client credits hearts only and
    /// asks the server for the rest. So the number is never walked towards a figure this code
    /// invented. It is walked towards whatever the balance says at the instant a token
    /// arrives: hearts climb immediately, credits climb the moment the sync lands — usually
    /// mid-cascade, because <c>Redeem</c> starts that sync a beat before the player finds the
    /// COLLECT button — and if the server has not answered yet the tokens still arrive, still
    /// flash, and the figure catches up when it does. Nothing here can tell a player they
    /// were paid something they were not.
    /// </para>
    /// </summary>
    public sealed class RewardFlight
    {
        /// <summary>How many tokens one prize throws, however much it is worth.</summary>
        public const int TokensPerDrop = 7;

        /// <summary>
        /// The rhythm, and it is the design — lifted unchanged from the chest, which is where
        /// the numbers were tuned.
        ///
        /// <para>
        /// <see cref="ClearAt"/> is how long the caller has to get its chrome out of the way
        /// before the first token leaves, and it is public because the chrome is the caller's:
        /// a token cannot be seen landing on a pill that is still behind a scrim.
        /// <see cref="TokenGap"/> spaces one prize's tokens at about fourteen a second —
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
        /// A landing callback that never arrives — a token whose tween was interrupted, a
        /// pill destroyed at the wrong instant — would otherwise leave the player looking at
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

        int _thrown, _arrived;
        bool _played, _finished;
        Action _done;

        RewardFlight() { }

        /// <summary>
        /// Snapshots what every pill reads right now. Call this <em>before</em> granting the
        /// reward — see the class remarks.
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
        /// moment a rule can clamp what was granted — a heart drop landing at the ceiling
        /// grants nothing, so subtracting its amount would rewind the pill below where it ever
        /// stood and count it up to a gain nobody received. A real-money purchase cannot be
        /// opened before its grant: <c>StoreService.Granted</c> is raised <em>by</em> the
        /// grant, from a receipt that may have been validated while the app was not even
        /// running.
        /// </para>
        /// <para>
        /// So the parameters are two currency amounts rather than a set of drops, and that is
        /// the constraint expressed as a type. Credits and gems are the only two things in
        /// this game that nothing clamps — no ceiling, no cap, no rule between the server's
        /// figure and the balance — so subtracting them is exact. Hearts are not, which is why
        /// there is no way to pass one in here.
        /// </para>
        /// </summary>
        public static RewardFlight AfterGrant(long credits, long gems)
        {
            var flight = Begin();

            int c = (int)ResourceSlots.Kind.Credits, g = (int)ResourceSlots.Kind.Gems;

            // Clamped, because the balance is read now and the grant was applied a moment ago
            // — a spend landing in between would make the subtraction overshoot, and a pill
            // rewound below zero would count up from a figure that has never been true.
            flight._before[c] = Math.Max(0L, flight._before[c] - Math.Max(0L, credits));
            flight._before[g] = Math.Max(0L, flight._before[g] - Math.Max(0L, gems));

            return flight;
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
        /// Returns false — and adds nothing — for a prize with no readout on the hub (seconds
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
        /// three hearts — throwing seven and landing them in fractions is the one case where
        /// the count in the air and the count on the pill visibly disagree. A boost is one
        /// token because it is one thing, however many hours it runs for.
        /// </summary>
        public static int TokenCount(ChestDrop drop)
            => !drop.IsValid ? 0
             : drop.Kind == ChestDropKind.HeartBoost ? 1
             : Mathf.Clamp(drop.Amount, 1, TokensPerDrop);

        /// <summary>
        /// What a pill should read once <paramref name="landed"/> of its
        /// <paramref name="tokens"/> have arrived, given what it stood at before the grant
        /// and what the balance says now.
        ///
        /// <para>
        /// The last token writes <paramref name="live"/> itself rather than the
        /// interpolation, so a rounding error can never leave a pill reading one short of the
        /// balance — the same guarantee <c>Roll.Number</c> and <c>Payout.Land</c> make. The
        /// clamp under it is for the currency case: <paramref name="live"/> can rise
        /// <em>during</em> the cascade when a sync lands, and a reading that went backwards on
        /// the next token would be the one thing worse than a reading that had not moved.
        /// </para>
        /// </summary>
        public static long Shown(long before, long live, int landed, int tokens)
        {
            if (tokens <= 0 || landed >= tokens) return live;

            long stepped = before + (long)Mathf.Round((live - before) * (landed / (float)tokens));
            return live >= before ? Math.Max(before, stepped) : Math.Min(before, stepped);
        }

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
        /// <paramref name="space"/> is what the tokens are drawn in — the caller's own content
        /// node, so they sit above the panel they came from — and it is also the owner every
        /// tween here is scheduled against, so the cascade dies with the panel rather than
        /// outliving it.
        /// </para>
        /// </summary>
        public void Play(RectTransform space, Action done)
        {
            if (_played) return;
            _played = true;
            _done = done;

            if (space == null || _items.Count == 0) { Finish(0f); return; }

            for (int i = 0; i < _items.Count; i++)
                _tokens[(int)_items[i].Kind] += TokenCount(_items[i].Drop);

            // Rewound to what the pills said before the grant, and claimed while they are
            // there. The grant already happened — deliberately, so a player who kills the app
            // has still opened the chest and has still been paid for the video — and the hub
            // repaints the instant the wallet moves, so by now the pills already read the new
            // totals. The player has been looking at a scrim this whole time and has seen
            // neither figure, so nothing is being faked: the report simply arrives in the
            // order the events were experienced.
            //
            // The claim is what stops the hub writing over the rewind from underneath. A
            // wallet change landing mid-cascade — an ad's credits arriving from the server is
            // exactly that — would otherwise jump the pill to the true figure, and the next
            // token would drag it back down.
            for (int k = 0; k < 3; k++)
            {
                if (_tokens[k] <= 0) continue;
                ResourceSlots.Claim((ResourceSlots.Kind)k);
                ResourceSlots.Show((ResourceSlots.Kind)k, _before[k]);
            }

            for (int i = 0; i < _items.Count; i++) _thrown += Throw(space, i);

            if (_thrown == 0) { Finish(0f); return; }

            // Nothing is played here. The button that was pressed has already made its one
            // sound, and the cascade starts a quarter-second later as its own event — see
            // Btn.ClickSfx for why a tap does not get to make two noises.
            Tween.After(Duration + Safety, () => Finish(0f), space);
        }

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

            RewardArt.Token(item.Drop.Kind, out var sprite, out var tint);
            var tone = RewardArt.Tint(item.Drop.Kind);

            // The card leaves as its tokens do, so the prize is not still sitting there while
            // copies of it fly away.
            if (card) Tween.After(start, () =>
            {
                if (!card) return;
                Tween.Punch(card, .22f, .26f);
                Burst.Sparks(card, Vector2.zero, tone, 12, 240f, 22f, .55f);

                // Delayed past the punch rather than overlapping it. Both write localScale,
                // and two tweens writing one value fight for it every frame they share — the
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
                    // now rather than captured. Both fallbacks matter: the row survives the
                    // card, and if neither is there the token still has to fly and land, or
                    // the cascade never finishes and the panel never closes.
                    Vector2 from = card ? TokenFlight.LocalIn(space, card)
                                 : Fallback ? TokenFlight.LocalIn(space, Fallback)
                                 : Vector2.zero;

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

            // Read now, not captured at Play. See the class remarks: for currency the balance
            // may only have moved a moment ago, and it may not have moved yet at all.
            long live = ResourceSlots.Balance(kind);

            ResourceSlots.Land(kind, Shown(_before[slot], live, _landed[slot], _tokens[slot]),
                               lastOfPill);

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
            // taps — they overlap into one continuous rumble. Payout.Land refused one for a
            // milder version of the same reason.
            Audio.Sfx("chime2", .5f, 1.14f, .06f);
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
            // less than the truth until something happened to repaint it — which, on a screen
            // the player may now just sit on, could be a long time.
            for (int k = 0; k < 3; k++)
            {
                if (_tokens[k] <= 0) continue;
                var kind = (ResourceSlots.Kind)k;
                ResourceSlots.Release(kind);
                ResourceSlots.Show(kind, ResourceSlots.Balance(kind));
            }

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
