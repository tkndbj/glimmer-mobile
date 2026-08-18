using System;
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
    /// into the hub's own heart, coin and gem pills — which means the overlay has to know
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
    /// Registration overwrites, and every read tests the Unity object first — so a slot
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
        /// type describing what a chest can contain — a list that already includes one
        /// entry with no readout (the heart boost is a timer, not a balance) and will grow
        /// with the drop table rather than with the HUD.
        /// </summary>
        public enum Kind { Credits, Gems, Hearts }

        public sealed class Slot
        {
            /// <summary>What tokens fly into, and what punches when one arrives.</summary>
            public RectTransform Icon;

            /// <summary>The readout. May be counted up as tokens land — see <see cref="Land"/>.</summary>
            public Text Number;

            /// <summary>The soft light behind the icon, brightened on each arrival.</summary>
            public Image Glow;

            /// <summary>The currency's colour, for sparks and the flash.</summary>
            public Color Tint;

            /// <summary>How this currency writes a number — abbreviated, or "3/5" for hearts.</summary>
            public Func<long, string> Format;

            public bool Alive => Icon != null && Number != null;
        }

        static readonly Slot[] Slots = new Slot[3];

        public static void Register(Kind kind, RectTransform icon, Text number, Image glow,
                                    Color tint, Func<long, string> format)
            => Slots[(int)kind] = new Slot
            {
                Icon = icon, Number = number, Glow = glow, Tint = tint, Format = format
            };

        public static bool TryGet(Kind kind, out Slot slot)
        {
            slot = Slots[(int)kind];
            if (slot != null && slot.Alive) return true;
            slot = null;
            return false;
        }

        /// <summary>
        /// Rewinds a readout to what it said before a reward was banked.
        ///
        /// <para>
        /// Needed because a chest is granted the moment it is opened — deliberately, so a
        /// player who kills the app mid-animation has still opened it — and the hub rebuilds
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
                         if (slot.Glow) Tween.Tint(slot.Glow, Pal.A(slot.Tint, .30f), last ? .55f : .26f);
                     });
            }

            if (last) Burst.Sparks(slot.Icon, Vector2.zero, slot.Tint, 16, 250f, 22f, .55f);
        }
    }
}
