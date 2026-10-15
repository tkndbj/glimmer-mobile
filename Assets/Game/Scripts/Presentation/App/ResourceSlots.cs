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

        public static void Register(Kind kind, RectTransform icon, Text number, Image glow,
                                    Color tint, Func<long, string> format)
        {
            Slots[(int)kind] = new Slot
            {
                Icon = icon, Number = number, Glow = glow, Tint = tint, Format = format,
                Rest = glow ? glow.color : Pal.A(tint, .30f)
            };

            // A row rebuilt while a reward is walking it - the page redrawing itself under a
            // cascade, now that a cascade no longer holds the page still - starts on the walked
            // figure, not on the balance the screen just printed into it. Nothing is forgotten
            // here: every entry ends by itself (ReadoutLedger), so a row rebuilt mid-flight keeps
            // being paid into.
            if (Ledgers[(int)kind].Count > 0) Show(kind, Walked(kind));
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
        /// The rewards each readout is still walking in - see <see cref="ReadoutLedger"/> for the
        /// rule, and <see cref="RewardFlight"/> for who enters them.
        ///
        /// <para>
        /// <b>A ledger rather than a flag.</b> While a chest panel stayed up until its last token
        /// landed, only one reward could ever be walking a pill. Now a paying panel steps out of
        /// the player's way (<see cref="Flow.Depart"/>), so a second chest can be granted while the
        /// first is still landing - and each owes exactly what it banked, so the pill climbs by the
        /// first's tokens and then the second's, never jumping to the total or being walked back.
        /// </para>
        /// </summary>
        static readonly ReadoutLedger[] Ledgers = { new ReadoutLedger(), new ReadoutLedger(), new ReadoutLedger() };

        /// <summary>
        /// The clock every deadline in a ledger is set on: the tween engine's, so a deadline and the
        /// tokens it stands behind cannot drift apart (see <see cref="Tween.Clock"/>).
        /// </summary>
        public static double Now => Tween.Clock;

        /// <summary>What this readout should say right now: the balance, less what is still flying into it.</summary>
        public static long Walked(Kind kind) => Ledgers[(int)kind].Shown(Balance(kind), Now);

        /// <summary>
        /// Enters a reward on a readout, which from now on shows the balance less what that reward
        /// has still to land, and repaints it.
        ///
        /// <para>
        /// The hub repaints on every wallet change and a reward's figure must not be written over
        /// from underneath, so every writer goes through <see cref="Walked"/>: the screen's
        /// <see cref="Repaint"/> and the landings alike. That is what lets a balance move
        /// mid-cascade - an ad's credits arriving, a second chest granted, a spend - without a pill
        /// ever jumping ahead of its tokens or being dragged back behind them.
        /// </para>
        /// </summary>
        public static void Enter(Kind kind, ReadoutLedger.Entry entry)
        {
            Ledgers[(int)kind].Add(entry);
            Settle(kind);
        }

        /// <summary>
        /// Drops whatever can no longer owe and repaints the readout with what is left. Called
        /// whenever an entry changes; harmless at any other time.
        /// </summary>
        public static void Settle(Kind kind)
        {
            Ledgers[(int)kind].Prune(Balance(kind), Now);
            Show(kind, Walked(kind));
        }

        /// <summary>
        /// Forgets every reward on every readout. For a fixture's teardown only: in the game each
        /// entry ends by itself.
        /// </summary>
        public static void ResetPayouts()
        {
            for (int k = 0; k < Ledgers.Length; k++) Ledgers[k].Clear();
        }

        /// <summary>True while any reward still owes this readout, or is held over it.</summary>
        public static bool IsPaying(Kind kind) => Ledgers[(int)kind].Busy(Now);

        /// <summary>
        /// The screen's own repaint. With nothing in the air this writes the balance; with a reward
        /// walking the readout it writes the walked figure instead, so a wallet change landing
        /// mid-cascade moves the pill by exactly what is no longer owed and never past the tokens.
        /// <paramref name="value"/> is the balance the screen read, and is what is written when
        /// nothing is paying.
        /// </summary>
        public static void Repaint(Kind kind, long value)
        {
            var ledger = Ledgers[(int)kind];
            if (ledger.Count > 0) ledger.Prune(Balance(kind), Now);
            Show(kind, ledger.Count > 0 ? Walked(kind) : value);
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
        /// repainted then, with whatever a reward still in the air leaves it (<see cref="Walked"/>).
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
