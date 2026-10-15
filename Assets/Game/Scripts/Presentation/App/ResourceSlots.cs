using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where each currency is drawn right now, so something on top of the screen can pay
    /// into it.
    ///
    /// <para>
    /// This exists for one reason: a reward that lands somewhere is worth more than a
    /// reward that is merely granted. The daily chest's prizes fly out of the panel and
    /// into the hub's own heart, coin and gem pills - which means the overlay has to know
    /// where those pills are, and the overlay belongs to a different view entirely.
    /// </para>
    /// <para>
    /// A registry rather than a reach through <see cref="Flow"/> into <c>HomeScreen</c>'s
    /// fields. The hub is the only screen with a resource row today and it will not be
    /// the last, and a cast to a concrete screen would make every future one either a
    /// second cast or an exception. Whoever draws a currency says so; whoever pays one
    /// asks. Neither knows the other exists.
    /// </para>
    /// <para>
    /// <b>Entries are allowed to go stale and nothing needs to clean them up.</b>
    /// Registration overwrites, and every read tests the Unity object first - so a slot
    /// belonging to a screen that has since been destroyed simply fails to resolve, and
    /// the caller falls back to paying with no flight at all. That is the correct
    /// behaviour anyway: if the pill is not on screen there is nowhere to fly to.
    /// </para>
    /// </summary>
    public static class ResourceSlots
    {
        /// <summary>
        /// The currencies that have a permanent home on the hub.
        ///
        /// Written out rather than reused from <c>ChestDropKind</c>, which is a Domain
        /// type describing what a chest can contain - a list that already includes one
        /// entry with no readout (the heart boost is a timer, not a balance) and will grow
        /// with the drop table rather than with the HUD.
        /// </summary>
        public enum Kind { Credits, Gems, Hearts }

        public sealed class Slot
        {
            /// <summary>What tokens fly into, and what punches when one arrives.</summary>
            public RectTransform Icon;

            /// <summary>The readout. May be counted up as tokens land - see <see cref="Land"/>.</summary>
            public Text Number;

            /// <summary>The soft light behind the icon, brightened on each arrival.</summary>
            public Image Glow;

            /// <summary>
            /// What that light sits at when nothing is landing on it.
            ///
            /// Captured from the glow itself rather than assumed, because the two rows that
            /// register are drawn differently - the hub's pills carry a wide halo and the
            /// shop's a narrower one - and a flare that returned to a number this file made
            /// up would leave whichever row disagreed permanently brighter or dimmer than it
            /// was built.
            /// </summary>
            public Color Rest;

            /// <summary>The currency's colour, for sparks and the flash.</summary>
            public Color Tint;

            /// <summary>How this currency writes a number - abbreviated, or "3/5" for hearts.</summary>
            public Func<long, string> Format;

            public bool Alive => Icon != null && Number != null;
        }

        static readonly Slot[] Slots = new Slot[3];

        /// <summary>
        /// Readouts a panel raised over a screen draws for as long as it is up - the gem shelf's
        /// own purse - stacked above the screen's row, newest on top. See <see cref="RegisterOver"/>.
        /// </summary>
        static readonly List<Layer>[] Over = { new List<Layer>(), new List<Layer>(), new List<Layer>() };

        sealed class Layer
        {
            public UnityEngine.Object Owner;
            public Slot Slot;
        }

        /// <summary>
        /// The payouts walking each readout, oldest snapshot first. See <see cref="Claim"/>.
        /// </summary>
        static readonly List<Payout>[] Paying = { new List<Payout>(), new List<Payout>(), new List<Payout>() };

        public static void Register(Kind kind, RectTransform icon, Text number, Image glow,
                                    Color tint, Func<long, string> format)
        {
            Slots[(int)kind] = new Slot
            {
                Icon = icon, Number = number, Glow = glow, Tint = tint, Format = format,
                Rest = glow ? glow.color : Pal.A(tint, .30f)
            };

            // A row rebuilt while a payout is walking it - the page redrawing itself under a
            // cascade, now that a cascade no longer holds the page still - starts on the walked
            // figure, not on the balance the screen just printed into it. Payouts are no longer
            // forgotten here: every one is released by its own flight (a lease on whatever draws
            // it, and an unowned deadline), so a row rebuilt mid-flight keeps being paid into.
            if (Paying[(int)kind].Count > 0) Show(kind, Walked(kind));
        }

        /// <summary>
        /// What this currency's readout would say if it were drawn right now.
        ///
        /// Here rather than beside whichever panel is paying, because "which balance does the
        /// heart pill show" is a fact about the row and two answers to it could disagree -
        /// which is exactly how a reward would count up to the wrong figure.
        /// </summary>
        public static long Balance(Kind kind)
        {
            switch (kind)
            {
                case Kind.Credits: return Profile.Coins;
                case Kind.Gems: return Profile.Gems;
                default: return Profile.Hearts;
            }
        }

        /// <summary>
        /// One reward's share of one readout: what the balance stood at when its snapshot was
        /// taken, and how much of it has landed.
        ///
        /// <para>
        /// <b>Why a readout holds a list of these rather than a flag.</b> A payout rewinds a
        /// pill to what it read before the reward was granted and walks it forward one token at
        /// a time, so for a second or two the number on screen is deliberately behind the
        /// balance. While a chest panel stayed up until its last token landed, only one payout
        /// could ever be walking a pill and a flag said enough. Now the page answers the next
        /// tap while tokens are still in the air (<see cref="Flow.Depart"/>), so a second chest
        /// can be granted and held while the first is still landing - and a flag released by
        /// the first to finish would hand the readout back with the second's prize still
        /// owed, which is a pill jumping to the total and then being walked up to it again.
        /// </para>
        /// <para>
        /// <b>Each payout owes the gap between its snapshot and the next one.</b> Snapshots are
        /// taken in the order the grants happen, and each is the true balance at that instant,
        /// so consecutive snapshots partition what the readout has still to show into one
        /// segment per reward - whatever order they finish in, and with no payout having to
        /// know what any other is worth. The newest owes up to the live balance, which is how a
        /// grant that only lands later (an ad's coins from the server, invariant 10d) is still
        /// walked in rather than jumped to. See <see cref="Owed"/>.
        /// </para>
        /// </summary>
        public sealed class Payout
        {
            /// <summary>Orders snapshots by when they were taken, not when they were claimed.</summary>
            public readonly long Sequence;

            /// <summary>The balance at the snapshot - or, for a grant already applied, derived from it.</summary>
            public readonly long Before;

            /// <summary>Tokens that have landed out of <see cref="Tokens"/>. Nought tokens is a held panel: all of it owed.</summary>
            public int Landed, Tokens;

            /// <summary>Paid in full, or abandoned. Kept only while an older payout still measures to it.</summary>
            public bool Done;

            public Payout(long sequence, long before)
            {
                Sequence = sequence;
                Before = before;
            }

            /// <summary>
            /// What this payout has still to show, given where its segment ends. Integer
            /// arithmetic (no float may decide a figure) and exact at the ends: nothing landed
            /// owes the whole segment, the last token owes nothing.
            /// </summary>
            public long Outstanding(long end)
            {
                long segment = end - Before;
                if (Done || segment <= 0) return 0L;
                if (Tokens <= 0) return segment;
                if (Landed >= Tokens) return 0L;
                return segment - segment * Math.Max(0, Landed) / Tokens;
            }
        }

        /// <summary>
        /// What a readout shows while <paramref name="payouts"/> (oldest snapshot first) are
        /// walking it: the live balance less everything still in the air. The pure half of
        /// <see cref="Walked"/>, public so the arithmetic is pinned without a screen.
        /// </summary>
        public static long Owed(long live, IList<Payout> payouts)
        {
            long owed = 0L;
            for (int i = 0; i < payouts.Count; i++)
            {
                long end = i + 1 < payouts.Count ? payouts[i + 1].Before : live;
                owed += payouts[i].Outstanding(end);
            }

            return Math.Max(0L, live - owed);
        }

        /// <summary>What this readout should say right now: the balance, less what is still flying into it.</summary>
        public static long Walked(Kind kind) => Owed(Balance(kind), Paying[(int)kind]);

        /// <summary>
        /// Enters a payout on a readout, which from now on shows the balance less what that
        /// payout has still to land, until <see cref="Release"/>.
        ///
        /// <para>
        /// The hub repaints on every wallet change and a payout's figure must not be written
        /// over from underneath, so every writer goes through <see cref="Walked"/>: the screen's
        /// <see cref="Repaint"/> and the payout's landings alike. That is what lets a balance
        /// move mid-cascade - an ad's credits arriving, a second chest granted - without a pill
        /// ever jumping ahead of its tokens or being dragged back behind them.
        /// </para>
        /// </summary>
        public static Payout Claim(Kind kind, long sequence, long before)
        {
            var list = Paying[(int)kind];
            var payout = new Payout(sequence, before);

            // In snapshot order, which is the order the grants happened in - a panel can hold
            // its pills a moment after a later reward has already claimed them.
            int at = list.Count;
            while (at > 0 && list[at - 1].Sequence > sequence) at--;
            list.Insert(at, payout);

            Show(kind, Walked(kind));
            return payout;
        }

        /// <summary>
        /// The payout has finished with this readout, paid or abandoned; the readout is
        /// repainted with whatever is still owed to anything else. Idempotent.
        /// </summary>
        public static void Release(Kind kind, Payout payout)
        {
            if (payout == null) return;
            payout.Done = true;
            Prune(Paying[(int)kind]);
            Show(kind, Walked(kind));
        }

        /// <summary>
        /// Drops finished payouts nothing measures to any more. A finished payout is kept only
        /// while an older one still owes - its snapshot is where the older one's segment ends,
        /// and removing it would hand the older one this payout's prize to walk in a second time.
        /// </summary>
        static void Prune(List<Payout> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Done && (i == 0 || list[i - 1].Done)) list.RemoveAt(i);

            // Every survivor finished means nothing measures to anything.
            for (int i = 0; i < list.Count; i++) if (!list[i].Done) return;
            list.Clear();
        }

        /// <summary>
        /// Forgets every payout on every readout. For a fixture's teardown only: in the game
        /// each payout is released by its own flight.
        /// </summary>
        public static void ResetPayouts()
        {
            for (int k = 0; k < Paying.Length; k++) Paying[k].Clear();
        }

        /// <summary>True while any payout is walking this readout - see <see cref="Claim"/>.</summary>
        public static bool IsPaying(Kind kind) => Paying[(int)kind].Count > 0;

        /// <summary>
        /// The screen's own repaint. With nothing in the air this writes the balance; with a
        /// payout walking the readout it writes the walked figure instead, so a wallet change
        /// landing mid-cascade moves the pill by exactly what is no longer owed and never past
        /// the tokens. <paramref name="value"/> is the balance the screen read, and is what is
        /// written when nothing is paying.
        /// </summary>
        public static void Repaint(Kind kind, long value)
        {
            Show(kind, Paying[(int)kind].Count > 0 ? Walked(kind) : value);
        }

        /// <summary>
        /// A readout drawn by a panel over the screen, which payouts land in while the panel is
        /// up, and which hands the currency back to the screen's own row the moment the panel goes.
        ///
        /// <para>
        /// <b>A stack rather than <see cref="Register"/>'s overwrite</b>, because <see cref="Register"/>
        /// is right for a screen and wrong for a panel: a screen's row is rebuilt on every
        /// navigation, so overwriting the last one loses nothing, but a panel closes back onto a
        /// screen that will not rebuild - and an overwritten slot would leave that screen's pill
        /// unreachable (no flight lands in it) and unpainted (its watch writes to the panel's) for
        /// the rest of the visit. Here the screen's slot is never touched; this one only sits above
        /// it, and a screen rebuilt while the panel is up still loses to the panel.
        /// </para>
        /// <para>
        /// <b>Taken down by the owner's own destruction</b> (a lease component), however the panel
        /// ends - closed, dismissed, or swept away by a screen change - and the row underneath is
        /// repainted with the live balance then, unless a payout owns it (<see cref="Claim"/>).
        /// </para>
        /// </summary>
        public static void RegisterOver(Component owner, Kind kind, RectTransform icon, Text number, Image glow,
                                        Color tint, Func<long, string> format)
        {
            if (!owner) return;

            var slot = new Slot
            {
                Icon = icon, Number = number, Glow = glow, Tint = tint, Format = format,
                Rest = glow ? glow.color : Pal.A(tint, .30f)
            };
            Over[(int)kind].Add(new Layer { Owner = owner, Slot = slot });

            var lease = owner.gameObject.AddComponent<SlotLease>();
            lease.Kind = kind;
            lease.Slot = slot;
        }

        /// <summary>Takes a panel's readout down and repaints the one it was covering.</summary>
        static void Drop(Kind kind, Slot slot)
        {
            var layers = Over[(int)kind];
            for (int i = layers.Count - 1; i >= 0; i--)
                if (ReferenceEquals(layers[i].Slot, slot)) layers.RemoveAt(i);

            Show(kind, Walked(kind));
        }

        /// <summary>Dies with the panel that drew the readout - see <see cref="RegisterOver"/>.</summary>
        sealed class SlotLease : MonoBehaviour
        {
            public Kind Kind;
            public Slot Slot;

            void OnDestroy()
            {
                var slot = Slot;
                Slot = null;
                if (slot != null) Drop(Kind, slot);
            }
        }

        public static bool TryGet(Kind kind, out Slot slot)
        {
            // A panel's readout first, newest first; a layer whose panel has gone without its
            // lease running (edit mode sends no OnDestroy) is skipped and swept.
            var layers = Over[(int)kind];
            for (int i = layers.Count - 1; i >= 0; i--)
            {
                var layer = layers[i];
                if (layer.Owner && layer.Slot.Alive) { slot = layer.Slot; return true; }
                if (!layer.Owner) layers.RemoveAt(i);
            }

            slot = Slots[(int)kind];
            if (slot != null && slot.Alive) return true;
            slot = null;
            return false;
        }

        /// <summary>
        /// Rewinds a readout to what it said before a reward was banked.
        ///
        /// <para>
        /// Needed because a chest is granted the moment it is opened - deliberately, so a
        /// player who kills the app mid-animation has still opened it - and the hub rebuilds
        /// its pills the instant the wallet changes. By the time the prizes are on screen the
        /// number behind the scrim is already the new one, so tokens would fly into a total
        /// that had nothing left to add. The player has not seen it yet (the scrim was over
        /// it the whole time), so showing the old figure for a second is not a lie; it is the
        /// report arriving in the order the player experienced the events.
        /// </para>
        /// </summary>
        public static void Show(Kind kind, long value)
        {
            if (!TryGet(kind, out var slot)) return;
            slot.Number.text = slot.Format != null ? slot.Format(value) : value.ToString();
        }

        /// <summary>
        /// A token has arrived: bump the readout to <paramref name="value"/>, punch the icon,
        /// flare the glow, and spark on the last one.
        /// </summary>
        public static void Land(Kind kind, long value, bool last)
        {
            if (!TryGet(kind, out var slot)) return;

            slot.Number.text = slot.Format != null ? slot.Format(value) : value.ToString();

            // Reset before punching, for the reason Payout.Land gives: Punch shares a
            // channel, and one cancelled mid-swing leaves the scale where it stopped. Six
            // coins landing in a second is six cancellations, and the pill visibly shrinks.
            slot.Number.transform.localScale = Vector3.one;
            Tween.Punch(slot.Number.transform, last ? .34f : .15f, last ? .44f : .24f);

            slot.Icon.localScale = Vector3.one;
            Tween.Punch(slot.Icon, last ? .38f : .18f, last ? .46f : .26f);

            if (slot.Glow)
            {
                var lit = Pal.A(slot.Tint, last ? .85f : .58f);
                Tween.Tint(slot.Glow, lit, .08f)
                     .OnDone(() =>
                     {
                         if (slot.Glow) Tween.Tint(slot.Glow, slot.Rest, last ? .55f : .26f);
                     });
            }

            if (last) Burst.Sparks(slot.Icon, Vector2.zero, slot.Tint, 16, 250f, 22f, .55f);
        }
    }
}
