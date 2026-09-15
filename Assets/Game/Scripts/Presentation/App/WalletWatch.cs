using System.Collections.Generic;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// Keeps a screen's balance readouts honest for as long as it is open: whoever draws a
    /// currency says which ones, and the repaint is not theirs to remember.
    ///
    /// <para>
    /// <b>It exists because "draw a pill" and "repaint the pill" were two decisions, and seven
    /// screens made them separately.</b> A readout is built once out of <c>Profile.Coins</c> and
    /// is a photograph from that moment on; what makes it a readout is a subscription to the two
    /// events that can move it. Every screen here wrote that subscription by hand, and by the
    /// time there were seven of them <b>four were wrong in four different ways</b>: the loadout
    /// registered nothing and repainted nothing, so a turret bought or upgraded left the purse
    /// above it showing what the player had before they spent it until they left the screen and
    /// came back; the tasks page registered all three pills and repainted none; and the streak
    /// and season pages repainted credits and gems while drawing a hearts pill neither of them
    /// ever touched. <b>Nothing was wrong with any of the four drawings</b>, which is why none of
    /// it showed up in a render, a validator or a test — a stale number is a correct number that
    /// has stopped being true.
    /// </para>
    /// <para>
    /// <b>So the cue is attached rather than written.</b> A screen names the currencies it draws
    /// once, beside the pills it built, and gets both events, every kind, and the unsubscribe.
    /// That is the same trade <c>ScreenLessons</c> makes for a queue of modals and
    /// <c>AssetLibrary.Hold</c> makes for a scope: the thing that can be forgotten stops being a
    /// thing anybody writes.
    /// </para>
    /// <para>
    /// <b>Two events rather than one, and that is not tidiness.</b> Credits and gems move through
    /// <see cref="PlayerProgression.Changed"/> — every debit in the game goes through
    /// <c>TrySpend</c>, which invalidates — while hearts are a produced/spent ledger that moves on
    /// its own clock and raises <see cref="Wallet.HeartsChanged"/>. A screen subscribing to one of
    /// the two draws a pill that is live for two currencies and frozen for the third, which is
    /// exactly what the streak and season pages shipped.
    /// </para>
    /// <para>
    /// <b>It writes through <see cref="ResourceSlots.Repaint"/> and never onto a label</b>, which
    /// keeps the registry the one writer of these readouts. A payout owns a pill while its tokens
    /// are in the air — it rewinds the number to what it read before the grant and walks it
    /// forward one token at a time — so a wallet change landing mid-cascade (an ad's credits
    /// confirmed by the server is exactly one) would jump the pill to the truth and have the next
    /// token drag it back down. <see cref="ResourceSlots.Claim"/> is what refuses that, and it can
    /// only refuse a write that comes through the registry.
    /// </para>
    /// <para>
    /// <b>And the figure comes from <see cref="ResourceSlots.Balance"/> rather than from the call
    /// site</b>, for that method's own reason: "which balance does the heart pill show" is a fact
    /// about the row, and two answers to it could disagree — which is how a reward counts up to
    /// the wrong number.
    /// </para>
    /// <para>
    /// <b>Registration is what it follows, not a list of objects.</b> It holds no pills, so a
    /// screen that rebuilds its chrome — the loadout swapping shelves, the streak page redrawing a
    /// lap — re-registers and this keeps working with nothing to re-attach. A kind nothing has
    /// registered repaints nothing at all, which is <see cref="ResourceSlots"/>'s own rule about
    /// stale entries: if the pill is not on screen there is nowhere to write.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WalletWatch : MonoBehaviour
    {
        readonly List<ResourceSlots.Kind> _kinds = new List<ResourceSlots.Kind>(3);

        bool _hooked;

        /// <summary>
        /// Watches <paramref name="kinds"/> on <paramref name="host"/>'s behalf for as long as it
        /// lives, and repaints them now.
        ///
        /// <para>
        /// <b>Safe to call more than once</b>, and it has to be: a screen that rebuilds its
        /// chrome calls this again from the same place it registers the pills, and a second
        /// component would be a second subscription writing the same number twice. The kinds
        /// named are added to what is already watched rather than replacing it, so two rows on one
        /// screen cannot take each other's readouts away.
        /// </para>
        /// </summary>
        public static WalletWatch Attach(Component host, params ResourceSlots.Kind[] kinds)
        {
            if (host == null || kinds == null || kinds.Length == 0) return null;

            var watch = host.GetComponent<WalletWatch>();
            if (watch == null) watch = host.gameObject.AddComponent<WalletWatch>();

            for (int i = 0; i < kinds.Length; i++)
                if (!watch._kinds.Contains(kinds[i])) watch._kinds.Add(kinds[i]);

            // AddComponent has already run OnEnable with an empty list, so the subscription is
            // standing and this paint is the one that matters.
            watch.Hook();
            watch.Repaint();

            return watch;
        }

        void OnEnable() => Hook();

        void OnDisable() => Unhook();

        /// <remarks>
        /// Belt and braces: <c>OnDisable</c> runs ahead of <c>OnDestroy</c> on anything that was
        /// active, and <see cref="Unhook"/> is idempotent, so this only ever matters for a screen
        /// destroyed while its object was already off.
        /// </remarks>
        void OnDestroy() => Unhook();

        void Hook()
        {
            if (_hooked) return;
            _hooked = true;

            PlayerProgression.Changed += Repaint;
            Wallet.HeartsChanged += OnHearts;
        }

        void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;

            PlayerProgression.Changed -= Repaint;
            Wallet.HeartsChanged -= OnHearts;
        }

        /// <remarks>
        /// A heart's capacity moves through this event too — <c>Wallet.AnnounceCapacity</c>
        /// re-raises it rather than adding a second one, which is what makes "3 / 20" appear
        /// everywhere the moment a container is bought.
        /// </remarks>
        void OnHearts(Hearts hearts) => Repaint();

        void Repaint()
        {
            for (int i = 0; i < _kinds.Count; i++)
                ResourceSlots.Repaint(_kinds[i], ResourceSlots.Balance(_kinds[i]));
        }
    }
}
