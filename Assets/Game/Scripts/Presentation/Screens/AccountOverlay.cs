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

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 880f), Loc.Get("ui.account.title"));

            // Linked, not merely signed in. An anonymous account has a uid and syncs
            // perfectly well, and calling that "safe" is exactly the false reassurance
            // this screen exists to prevent.
            bool linked = CloudSaveService.IsLinked;

            _status = UIKit.Titled("Status", Panel,
                                   linked ? Loc.Get("ui.account.linked") : Loc.Get("ui.account.guest"),
                                   34, linked ? Pal.Mint : Pal.Rose,
                                   TextAnchor.MiddleCenter, new Vector2(720f, 48f), new Vector2(.5f, 1f),
                                   new Vector2(0f, -250f), 2f, 2f);

            UIKit.Titled("Why", Panel,
                         Loc.Get(linked ? "ui.account.linked_body" : "ui.account.guest_body"),
                         26, new Color(.44f, .32f, .24f, .95f),
                         // -340 rather than -310: the box is anchored UpperCenter, so its
                         // top edge is where the first line lands, and at -310 that edge
                         // sat inside the status line above it. Invisible while the copy
                         // was one long unwrapped line running off both edges of the
                         // screen; obvious the moment it wrapped.
                         TextAnchor.UpperCenter, new Vector2(700f, 110f), new Vector2(.5f, 1f),
                         new Vector2(0f, -340f), 0f, 0f, wrap: true);

            // "Lives only on this device" is abstract; "47 glades and keeper level 12
            // live only on this device" is not. Shown only once there is something to
            // lose, so a brand-new player is not warned about nothing.
            if (!linked && PlayerProgression.ClearedGlades > 0)
            {
                UIKit.Titled("Stakes", Panel,
                             Loc.Format("ui.account.guest_stakes",
                                        PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                             28, Pal.Rose, TextAnchor.MiddleCenter,
                             new Vector2(700f, 44f), new Vector2(.5f, 1f), new Vector2(0f, -418f), 2f, 2f,
                             wrap: true);
            }

            if (!CloudSaveService.IsAvailable)
            {
                // No backend in this build. Say so rather than showing buttons that
                // cannot work.
                _status.text = Loc.Get("ui.account.unavailable");
            }
            else if (!linked)
            {
                _google = UIKit.TextButton("Google", Panel, "btn_green", Loc.Get("ui.account.google"), 36,
                                 new Vector2(600f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 430f),
                                 () => Begin(LinkCredential.ForGoogle())).transform;

                // Offered on both platforms, not only iOS. App Store Guideline 4.8
                // requires Apple wherever another third-party sign-in appears, and a
                // player with an Apple ID on Android should not be turned away either.
                _apple = UIKit.TextButton("Apple", Panel, "btn_blue", Loc.Get("ui.account.apple"), 36,
                                 new Vector2(600f, 118f), new Vector2(.5f, 0f), new Vector2(0f, 296f),
                                 () => Begin(LinkCredential.ForApple())).transform;
            }

            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46,
                             new Vector2(560f, 132f), new Vector2(.5f, 0f), new Vector2(0f, 118f),
                             () => { if (!_busy) Close(); });
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

            var button = UIKit.TextButton("Switch", Panel, "btn_red", Loc.Get("ui.account.switch"), 34,
                                          new Vector2(600f, 118f), new Vector2(.5f, 0f),
                                          new Vector2(0f, 430f), ConfirmSwitch);
            _switchButton = button.transform;

            // Named concretely rather than as "your progress". Somebody three weeks in
            // deserves to see the three weeks before they tap, and the other account's
            // contents cannot be shown at all — reading it requires signing in as it,
            // which is the irreversible step itself.
            UIKit.Titled("SwitchCost", Panel,
                         Loc.Format("ui.account.switch_cost",
                                    PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                         26, new Color(.62f, .26f, .24f), TextAnchor.MiddleCenter,
                         new Vector2(700f, 40f), new Vector2(.5f, 0f), new Vector2(0f, 386f), 0f, 0f);

            UIKit.Titled("SwitchWarn", Panel, Loc.Get("ui.account.switch_warning"), 24,
                         new Color(.62f, .26f, .24f, .9f), TextAnchor.MiddleCenter,
                         new Vector2(700f, 40f), new Vector2(.5f, 0f), new Vector2(0f, 350f), 0f, 0f);
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
