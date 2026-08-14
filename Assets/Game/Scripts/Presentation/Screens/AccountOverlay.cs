using System;
using System.Collections;
using System.Threading.Tasks;
using GlimmerGrove.Cloud;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Where a player turns a guest account into a permanent one.
    ///
    /// The whole design of this screen follows from one fact: an anonymous account dies
    /// with the installation. Everything a player has done lives under a uid that exists
    /// nowhere but this device, so a reinstall — or a lost phone — takes all of it. That
    /// is why the guest state is stated plainly rather than hidden, and why this is
    /// offered after a chapter is finished rather than on the first launch, when there is
    /// nothing to protect and the ask only costs a player.
    ///
    /// Signing in is never required. The game is fully playable having never seen this.
    /// </summary>
    public sealed class AccountOverlay : ModalView
    {
        Text _status;
        Transform _switchButton;
        Transform _google, _apple;
        bool _busy;

        /// <summary>Set when the provider is already attached to another grove.</summary>
        LinkCredential _contested;
        bool _armed;

        // Laid out by stacking downward from the top edge. Everything here is anchored
        // to the panel top and positioned by its own centre — UIKit.Box pivots every box
        // centrally whatever its anchor — so a row's position is computed rather than
        // guessed. Mixing top-anchored text with bottom-anchored buttons is what let the
        // stakes line and the sign-in buttons occupy the same forty pixels.
        const float TopMargin = 100f, BottomMargin = 56f;
        const float StatusH = 52f, WhyH = 150f, StakesH = 56f, ButtonH = 118f, CloseH = 124f;
        const float AfterStatus = 18f, AfterWhy = 16f, AfterStakes = 22f;
        const float BetweenButtons = 14f, BeforeClose = 26f;

        /// <summary>Where the sign-in buttons start, so the switch prompt can reuse the slot.</summary>
        float _signInTop;

        protected override void Build()
        {
            // Linked, not merely signed in. An anonymous account has a uid and syncs
            // perfectly well, and calling that "safe" is exactly the false reassurance
            // this screen exists to prevent.
            bool linked = CloudSaveService.IsLinked;
            bool available = CloudSaveService.IsAvailable;

            bool showStakes = !linked && PlayerProgression.ClearedGlades > 0;
            bool showSignIn = available && !linked;

            // The panel is sized to what it will actually hold, so the linked state does
            // not open a tall box with a hole where two buttons would have been.
            float height = TopMargin + StatusH + AfterStatus + WhyH + AfterWhy;
            if (showStakes) height += StakesH + AfterStakes;
            if (showSignIn) height += ButtonH + BetweenButtons + ButtonH + BeforeClose;
            height += CloseH + BottomMargin;

            MakePanel(new Vector2(860f, height), Loc.Get("ui.account.title"));

            float y = TopMargin;

            _status = Row("Status", linked ? Loc.Get("ui.account.linked") : Loc.Get("ui.account.guest"),
                          34, linked ? Pal.Mint : Pal.Rose, y, StatusH, TextAnchor.MiddleCenter, 2f);
            Fit(_status, 26, 34);
            y += StatusH + AfterStatus;

            var why = Row("Why", Loc.Get(linked ? "ui.account.linked_body" : "ui.account.guest_body"),
                          26, new Color(.44f, .32f, .24f, .95f), y, WhyH, TextAnchor.UpperCenter, 0f);
            Fit(why, 20, 26);
            y += WhyH + AfterWhy;

            // "Lives only on this device" is abstract; "47 glades and keeper level 12
            // live only on this device" is not. Shown only once there is something to
            // lose, so a brand-new player is not warned about nothing.
            if (showStakes)
            {
                var stakes = Row("Stakes",
                                 Loc.Format("ui.account.guest_stakes",
                                            PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                                 28, Pal.Rose, y, StakesH, TextAnchor.MiddleCenter, 2f);
                Fit(stakes, 22, 28);
                y += StakesH + AfterStakes;
            }

            // No backend in this build. Say so rather than showing buttons that cannot work.
            if (!available) _status.text = Loc.Get("ui.account.unavailable");

            if (showSignIn)
            {
                _signInTop = y;

                _google = Button("Google", "btn_green", Loc.Get("ui.account.google"), y,
                                 () => Begin(LinkCredential.ForGoogle()));
                y += ButtonH + BetweenButtons;

                // Offered on both platforms, not only iOS. App Store Guideline 4.8
                // requires Apple wherever another third-party sign-in appears, and a
                // player with an Apple ID on Android should not be turned away either.
                _apple = Button("Apple", "btn_blue", Loc.Get("ui.account.apple"), y,
                                () => Begin(LinkCredential.ForApple()));
                y += ButtonH + BeforeClose;
            }

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46,
                             new Vector2(560f, CloseH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(y + CloseH * .5f)),
                             () => { if (!_busy) Close(); });
        }

        /// <summary>One stacked row of text, positioned by its top edge.</summary>
        Text Row(string name, string text, int size, Color colour, float top, float height,
                 TextAnchor anchor, float shadow)
            => UIKit.Titled(name, Panel, text, size, colour, anchor,
                            new Vector2(700f, height), new Vector2(.5f, 1f),
                            new Vector2(0f, -(top + height * .5f)),
                            outline: 0f, shadow: shadow, wrap: true);

        Transform Button(string name, string skin, string label, float top, Action onTap)
            => UIKit.TextButton(name, Panel, skin, label, 36,
                                new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                new Vector2(0f, -(top + ButtonH * .5f)), onTap).transform;

        /// <summary>
        /// Lets a label shrink rather than spill. Every string here is translated, and
        /// German runs half as long again — an overflow that only appears in one market
        /// is the kind nobody sees until a review mentions it.
        /// </summary>
        static void Fit(Text label, int min, int max)
        {
            if (!label) return;

            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
        }

        // ------------------------------------------------------------- linking
        void Begin(LinkCredential credential)
        {
            if (_busy) return;
            _busy = true;
            _armed = false;

            Say("ui.account.working", Pal.Cream);
            StartCoroutine(RunLink(credential));
        }

        IEnumerator RunLink(LinkCredential credential)
        {
            var task = CloudSaveService.LinkAsync(credential);
            while (!task.IsCompleted) yield return null;

            _busy = false;

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                Say("ui.account.failed", Pal.Rose);
                yield break;
            }

            var result = task.Result;

            if (result.Ok)
            {
                Say("ui.account.linked_ok", Pal.Mint);
                Audio.Sfx("chime2", .55f);
                Tween.After(1.6f, () => { if (this != null) Close(); }, this);
                yield break;
            }

            if (result.Failure == CloudFailure.AlreadyLinkedElsewhere)
            {
                OfferSwitch(credential);
                yield break;
            }

            Say(result.Failure == CloudFailure.Offline ? "ui.account.offline" : "ui.account.failed",
                Pal.Rose);
        }

        /// <summary>
        /// The player linked this provider on another device, so their grove is already
        /// somewhere else.
        ///
        /// This is the one place in the game that shows a destructive prompt, and it is
        /// unavoidable: the two accounts cannot be merged, because currency was granted
        /// and spent separately against each and no join can reconcile that without
        /// inventing or destroying money. So it says exactly what it will cost and makes
        /// the player tap twice, the same way erasing progress does.
        /// </summary>
        void OfferSwitch(LinkCredential credential)
        {
            _contested = credential;
            Say("ui.account.taken", Pal.Rose);
            Audio.Sfx("nope", .5f);

            if (_switchButton != null) return;

            // The sign-in buttons sit in exactly this space, and neither can help now:
            // this provider belongs to another grove, and the choice left is to adopt it
            // or walk away. Leaving them would stack two buttons in one slot and run the
            // cost line straight through the other. Trying the other provider instead is
            // still reachable — the profile screen reopens this from scratch.
            if (_google != null) _google.gameObject.SetActive(false);
            if (_apple != null) _apple.gameObject.SetActive(false);

            // Takes the slot the two sign-in buttons just vacated, measured from the same
            // cursor Build used. Hard-coding a second set of coordinates here is what
            // would let the cost line drift back over the button the next time either
            // layout moved.
            float y = _signInTop;

            _switchButton = UIKit.TextButton("Switch", Panel, "btn_red", Loc.Get("ui.account.switch"), 34,
                                             new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                             new Vector2(0f, -(y + ButtonH * .5f)), ConfirmSwitch).transform;
            y += ButtonH + BetweenButtons;

            // Named concretely rather than as "your progress". Somebody three weeks in
            // deserves to see the three weeks before they tap, and the other account's
            // contents cannot be shown at all — reading it requires signing in as it,
            // which is the irreversible step itself.
            var cost = Row("SwitchCost",
                           Loc.Format("ui.account.switch_cost",
                                      PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                           26, new Color(.62f, .26f, .24f), y, 62f, TextAnchor.UpperCenter, 0f);
            Fit(cost, 20, 26);
            y += 62f + 8f;

            var warn = Row("SwitchWarn", Loc.Get("ui.account.switch_warning"), 24,
                           new Color(.62f, .26f, .24f, .9f), y, 40f, TextAnchor.UpperCenter, 0f);
            Fit(warn, 18, 24);
        }

        void ConfirmSwitch()
        {
            if (_busy) return;

            var label = _switchButton != null ? _switchButton.Find("Text")?.GetComponent<Text>() : null;

            if (!_armed)
            {
                _armed = true;
                if (label) label.text = Loc.Get("ui.account.switch_confirm");
                Audio.Sfx("nope", .5f);
                Tween.Shake((RectTransform)_switchButton, 9f, .3f);

                Tween.After(3.2f, () =>
                {
                    if (this == null) return;
                    _armed = false;
                    if (label) label.text = Loc.Get("ui.account.switch");
                }, this);
                return;
            }

            _busy = true;
            Say("ui.account.working", Pal.Cream);
            StartCoroutine(RunSwitch());
        }

        IEnumerator RunSwitch()
        {
            var task = CloudSaveService.AdoptLinkedAccountAsync(_contested);
            while (!task.IsCompleted) yield return null;

            _busy = false;

            if (task.IsFaulted || !task.Result.Ok)
            {
                if (task.IsFaulted) Debug.LogException(task.Exception);
                Say("ui.account.failed", Pal.Rose);
                yield break;
            }

            Audio.Sfx("chime2", .55f);

            // What they actually got. Without this the screen just says "welcome back"
            // and the player has no way to tell whether they adopted the grove they
            // meant to until they have already lost the other one.
            _status.text = Loc.Format("ui.account.switched_found", PlayerProgression.ClearedGlades);
            _status.color = Pal.Mint;

            // The grove on screen belongs to somebody else now, so nothing behind this
            // overlay is still describing the truth.
            Tween.After(1.4f, () => { if (this != null) Close(() => Flow.Go<HomeScreen>()); }, this);
        }

        void Say(string key, Color colour)
        {
            if (_status == null) return;
            _status.text = Loc.Get(key);
            _status.color = colour;
        }

        public override bool OnBack()
        {
            if (_busy) return true;      // a sign-in flow is mid-air; swallow the back
            Close();
            return true;
        }

        // ---------------------------------------------------------- the nudge
        /// <summary>
        /// Whether it is worth asking the player to protect their grove.
        ///
        /// Asked at most twice, ever. The count lives in PlayerPrefs rather than the save
        /// file on purpose: it is a record of what this installation has shown a person,
        /// not something about their progress, and it must not travel to another device
        /// through the cloud or survive a progress wipe as a reason to stay silent.
        /// </summary>
        const string NagKey = "account_prompt_count";
        const int MaxNags = 2;

        public static bool ShouldOffer()
            => CloudSaveService.IsAvailable
               && !CloudSaveService.IsLinked
               && PlayerPrefs.GetInt(NagKey, 0) < MaxNags;

        public static void NoteOffered()
            => PlayerPrefs.SetInt(NagKey, PlayerPrefs.GetInt(NagKey, 0) + 1);
    }
}
