using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Everything about which account this device is.
    ///
    /// The whole design of this screen follows from one fact: an anonymous account dies
    /// with the installation. Everything a player has done lives under a uid that exists
    /// nowhere but this device, so a reinstall — or a lost phone — takes all of it. That
    /// is why the guest state is stated plainly rather than hidden, and why linking is
    /// offered after a chapter is finished rather than on the first launch, when there is
    /// nothing to protect and the ask only costs a player.
    ///
    /// <para>
    /// Signing in is never required. The game is fully playable having never seen this.
    /// </para>
    ///
    /// <para>
    /// <b>One panel, four states.</b> Guest, linked, choosing an account to switch to, and
    /// the destructive prompt for a provider that belongs to somebody else's grove — plus a
    /// fifth the player should never see, a device caught between two accounts. They are all
    /// answers to one question, "which grove is this phone", so they are one panel that
    /// rebuilds rather than four overlays: this project has already paid for the alternative
    /// twice, once when a second panel slipped in front of the victory screen's Next button
    /// and once when the hub's plus buttons led three different places.
    /// </para>
    /// <para>
    /// <b>There is deliberately no sign-out.</b> Signing out of a game with no login screen
    /// leaves a device holding a grove that nothing owns, and the two honest resolutions are
    /// both worse than the button: keep the save and the next sync clones a paid-for account
    /// into a fresh anonymous one (see <c>AccountGate</c> for why that is a faucet, not a
    /// mix-up), or erase it and a player who only wanted to stop syncing has lost everything.
    /// What people actually want from that button is this: to play as somebody else on this
    /// phone, and to be able to come back. So it is a switch, it saves the outgoing grove
    /// before it does anything at all, and it is reversible by signing in again.
    /// </para>
    /// </summary>
    public sealed class AccountOverlay : ModalView
    {
        /// <summary>What the panel is currently asking.</summary>
        enum Stage
        {
            /// <summary>Whatever the account is: guest, linked, or between two accounts.</summary>
            Resting,

            /// <summary>A linked player has asked to switch and is choosing a provider.</summary>
            Choosing,

            /// <summary>
            /// A provider was offered and belongs to another grove. The one destructive
            /// prompt in the game.
            /// </summary>
            Contested,
        }

        Stage _stage;
        Text _status;
        Transform _adoptButton;
        bool _busy;

        /// <summary>Set when the provider is already attached to another grove.</summary>
        LinkCredential _contested;
        bool _armed;

        /// <summary>
        /// The credential the running flow is about.
        ///
        /// Held because one outcome has to re-ask with it: a device recovering from an
        /// interrupted switch that is offered a third account gets the destructive prompt, and
        /// that prompt has to be about the account they just chose.
        /// </summary>
        LinkCredential _pending;

        /// <summary>
        /// Whether this attempt began from a device caught between two accounts.
        ///
        /// Held because the same outcome means two different things: landing on the account
        /// this device already holds is "nothing to do" after tapping switch, and "you are
        /// back, and your progress is saving again" after tapping the same button on a panel
        /// that just told somebody their grove was not being saved.
        /// </summary>
        bool _recovering;

        // Laid out by stacking downward from the top edge. Everything here is anchored
        // to the panel top and positioned by its own centre — UIKit.Box pivots every box
        // centrally whatever its anchor — so a row's position is computed rather than
        // guessed. Mixing top-anchored text with bottom-anchored buttons is what let the
        // stakes line and the sign-in buttons occupy the same forty pixels.
        const float TopMargin = 100f, BottomMargin = 56f;
        const float StatusH = 52f, WhyH = 150f, LineH = 60f, ButtonH = 118f, WarnH = 44f, CloseH = 124f;
        const float AfterStatus = 18f, AfterWhy = 16f, AfterLine = 20f;
        const float BetweenButtons = 14f, AfterButtons = 22f, BeforeClose = 12f;

        /// <summary>
        /// The rows this state will draw, in order, so the panel can be sized to hold exactly
        /// them.
        ///
        /// <para>
        /// Measured before anything is built, the way <c>WinOverlay</c> measures a victory:
        /// the height of this panel depends on what it is saying, and the alternative —
        /// reserving room for the tallest state — leaves a visible hole in the three states
        /// that are shorter, of which the linked one is what most players see most often.
        /// </para>
        /// </summary>
        readonly List<float> _gaps = new List<float>();

        float _cursor;
        int _row;

        protected override void Build()
        {
            bool available = CloudSaveService.IsAvailable;

            // Read once. IsLinked reaches the SDK and AccountMismatched is set by a background
            // sync, so sampling either twice inside one layout invites a panel built half in
            // one state and half in another.
            bool mismatched = available && CloudSaveService.AccountMismatched;
            bool linked = available && !mismatched && CloudSaveService.IsLinked;

            bool contested = _stage == Stage.Contested;
            bool choosing = _stage == Stage.Choosing;

            // The destructive prompt is only destructive when there is something to destroy.
            // A player who has just installed the game to get their account back meets it with
            // an empty grove, and a warning that cries wolf there is one nobody reads on the
            // grove that has everything.
            bool costly = contested && CloudSaveService.HoldsAGrove;

            bool showStakes = !linked && !contested && !choosing && !mismatched
                              && PlayerProgression.ClearedGlades > 0;
            bool showSwitch = linked && !choosing;
            bool showProviders = available && !contested && (choosing || mismatched || !linked);

            // ------------------------------------------------------------------ measure
            _gaps.Clear();
            float height = TopMargin + BottomMargin + BeforeClose;

            // Each row records the gap that follows it and adds itself to the total, so the
            // panel is sized by the same statements that decide what is in it. Two lists —
            // one to measure, one to lay out — is how they come to disagree.
            void Row(float h, float gap)
            {
                _gaps.Add(gap);
                height += h + gap;
            }

            Row(StatusH, AfterStatus);
            Row(WhyH, AfterWhy);
            if (showStakes || costly) Row(LineH, AfterLine);
            if (choosing) Row(LineH, AfterLine);
            if (contested) Row(ButtonH, costly ? BetweenButtons : AfterButtons);
            if (costly) Row(WarnH, AfterButtons);
            if (showSwitch) Row(ButtonH, AfterButtons);
            if (showProviders) { Row(ButtonH, BetweenButtons); Row(ButtonH, AfterButtons); }
            Row(CloseH, 0f);

            MakePanel(new Vector2(860f, height), Loc.Get("ui.account.title"));
            _cursor = TopMargin;
            _row = 0;

            // ------------------------------------------------------------------- status
            // Contested outranks mismatched, and the order is the point: this prompt is
            // reachable from a device that is already between two accounts, and "signed in as
            // someone else" is true there but useless. The player is being asked one question
            // and it is the one the prompt is about.
            string statusKey = !available ? "ui.account.unavailable"
                             : contested ? "ui.account.taken"
                             : mismatched ? "ui.account.mismatch"
                             : linked ? "ui.account.linked"
                             : "ui.account.guest";

            _status = Line("Status", Loc.Get(statusKey), 34,
                           linked ? Pal.Mint : available ? Pal.Rose : new Color(.52f, .40f, .31f, .9f),
                           StatusH, TextAnchor.MiddleCenter, 2f);
            Fit(_status, 26, 34);

            // --------------------------------------------------------------------- body
            string bodyKey = contested ? "ui.account.taken_body"
                           : mismatched ? "ui.account.mismatch_body"
                           : choosing ? "ui.account.switch_body"
                           : linked ? "ui.account.linked_body"
                           : "ui.account.guest_body";

            Fit(Line("Why", Loc.Get(bodyKey), 26, new Color(.44f, .32f, .24f, .95f),
                     WhyH, TextAnchor.UpperCenter, 0f), 20, 26);

            // "Lives only on this device" is abstract; "47 glades and keeper level 12
            // live only on this device" is not. Shown only once there is something to
            // lose, so a brand-new player is not warned about nothing.
            if (showStakes)
                Fit(Line("Stakes",
                         Loc.Format("ui.account.guest_stakes",
                                    PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                         28, Pal.Rose, LineH, TextAnchor.MiddleCenter, 2f), 22, 28);

            // Named concretely rather than as "your progress". Somebody three weeks in
            // deserves to see the three weeks before they tap, and the other account's
            // contents cannot be shown at all — reading it requires signing in as it,
            // which is the irreversible step itself.
            if (costly)
                Fit(Line("AdoptCost",
                         Loc.Format("ui.account.adopt_cost",
                                    PlayerProgression.ClearedGlades, PlayerProgression.Level.Level),
                         26, new Color(.62f, .26f, .24f), LineH, TextAnchor.UpperCenter, 0f), 20, 26);

            // The one thing a player needs to believe before tapping a provider on this
            // panel, and the one thing that distinguishes this from signing out: the grove
            // they are leaving is kept, and comes back.
            if (choosing)
                Fit(Line("Safe", Loc.Get("ui.account.switch_safe"), 26, Pal.Mint,
                         LineH, TextAnchor.MiddleCenter, 0f), 20, 26);

            // ------------------------------------------------------------------ buttons
            if (contested)
            {
                _adoptButton = Button("Adopt", "btn_red", Loc.Get("ui.account.adopt"), ConfirmAdopt);

                if (costly)
                    Fit(Line("AdoptWarn", Loc.Get("ui.account.adopt_warning"), 24,
                             new Color(.62f, .26f, .24f, .9f), WarnH, TextAnchor.UpperCenter, 0f), 18, 24);
            }

            if (showSwitch)
                Button("Switch", "btn_blue", Loc.Get("ui.account.switch"), OfferSwitch);

            if (showProviders)
            {
                Button("Google", "btn_green", Loc.Get("ui.account.google"),
                       () => Begin(LinkCredential.ForGoogle()));

                // Offered on both platforms, not only iOS. App Store Guideline 4.8
                // requires Apple wherever another third-party sign-in appears, and a
                // player with an Apple ID on Android should not be turned away either.
                Button("Apple", "btn_blue", Loc.Get("ui.account.apple"),
                       () => Begin(LinkCredential.ForApple()));
            }

            _cursor += BeforeClose;
            UIKit.TextButton("Close", Panel, "btn_green", Loc.Get("ui.common.done"), 46,
                             new Vector2(560f, CloseH), new Vector2(.5f, 1f),
                             new Vector2(0f, -(_cursor + CloseH * .5f)),
                             () => { if (!_busy) Close(); });
        }

        // ------------------------------------------------------------------- layout
        /// <summary>One stacked row of text, taking the next slot the measure reserved.</summary>
        Text Line(string name, string text, int size, Color colour, float height,
                  TextAnchor anchor, float shadow)
        {
            var label = UIKit.Titled(name, Panel, text, size, colour, anchor,
                                     new Vector2(700f, height), new Vector2(.5f, 1f),
                                     new Vector2(0f, -(_cursor + height * .5f)),
                                     outline: 0f, shadow: shadow, wrap: true);
            Advance(height);
            return label;
        }

        Transform Button(string name, string skin, string label, Action onTap)
        {
            var button = UIKit.TextButton(name, Panel, skin, label, 36,
                                          new Vector2(600f, ButtonH), new Vector2(.5f, 1f),
                                          new Vector2(0f, -(_cursor + ButtonH * .5f)), onTap);
            Advance(ButtonH);
            return button.transform;
        }

        /// <summary>
        /// Steps the cursor past the row just drawn, taking the gap the measure recorded for
        /// it rather than deciding one here.
        ///
        /// <para>
        /// The rows are walked in the order they were measured, so the measure and the build
        /// have to agree about which rows exist — they read the same flags a dozen lines apart
        /// and that is the one thing to check when editing this. Reading the gaps back rather
        /// than repeating them is what keeps the panel's height and its contents from drifting
        /// apart, which is the failure this file has already had once, when a cost line ended
        /// up printed through a button.
        /// </para>
        /// </summary>
        void Advance(float height)
            => _cursor += height + (_row < _gaps.Count ? _gaps[_row++] : 0f);

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

        // ------------------------------------------------------------- the flows
        /// <summary>
        /// Opens the account chooser. Nothing has happened yet and nothing will until a
        /// provider is tapped — the grove is saved as the first step of that, not of this.
        /// </summary>
        void OfferSwitch()
        {
            if (_busy) return;

            _stage = Stage.Choosing;
            Rebuild();
        }

        /// <summary>
        /// A provider was tapped. Which of the three things that means is decided here, once,
        /// from the state the panel is in — never by the button, because the same two buttons
        /// appear in three states and a handler per state is three places to get it wrong.
        /// </summary>
        void Begin(LinkCredential credential)
        {
            if (_busy) return;

            _busy = true;
            _armed = false;
            _pending = credential;
            _recovering = CloudSaveService.AccountMismatched;

            if (_recovering)
            {
                Say("ui.account.working", Pal.Cream);
                StartCoroutine(RunBecome(CloudSaveService.ResumeAccountAsync(credential)));
                return;
            }

            if (_stage == Stage.Choosing)
            {
                // Says what is happening first, because this is the step that takes the time
                // and the one the player is trusting: their grove is being put somewhere safe
                // before anything is handed over.
                Say("ui.account.securing", Pal.Cream);
                StartCoroutine(RunBecome(CloudSaveService.SwitchAccountAsync(credential)));
                return;
            }

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
                Contest(credential);
                yield break;
            }

            Say(result.Failure == CloudFailure.Offline ? "ui.account.offline" : "ui.account.failed",
                Pal.Rose);
        }

        /// <summary>
        /// Reports one attempt to make this device a different account.
        ///
        /// <para>
        /// Every branch here is a different sentence on purpose. Three of the outcomes are not
        /// failures at all, two of them leave the device exactly as it was, and one leaves it
        /// signed in but not syncing — telling a player "something went wrong" for any of those
        /// is how somebody decides their grove is gone when it is sitting safely on a server.
        /// </para>
        /// </summary>
        IEnumerator RunBecome(Task<SwitchResult> task)
        {
            while (!task.IsCompleted) yield return null;

            _busy = false;

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                Say("ui.account.failed", Pal.Rose);
                yield break;
            }

            var result = task.Result;

            switch (result.Outcome)
            {
                case SwitchOutcome.SameAccount:
                    Audio.Sfx("chime2", .55f);
                    Say(_recovering ? "ui.account.resumed" : "ui.account.same_account", Pal.Mint);

                    // Recovery changed something real — this device is saving again — so the
                    // panel goes back to the resting state rather than closing on a message.
                    if (_recovering) Tween.After(1.4f, () => { if (this != null) Settle(); }, this);
                    break;

                case SwitchOutcome.Adopted:
                    Audio.Sfx("chime2", .55f);

                    // What they actually got. Without this the screen just says "welcome back"
                    // and the player has no way to tell whether they arrived at the grove they
                    // meant to until they have already left the other one.
                    _status.text = Loc.Format("ui.account.adopted_found", PlayerProgression.ClearedGlades);
                    _status.color = Pal.Mint;
                    Leave();
                    break;

                case SwitchOutcome.Started:
                    Audio.Sfx("chime2", .55f);
                    Say("ui.account.switched_new", Pal.Mint);
                    Leave();
                    break;

                case SwitchOutcome.NotSecured:
                    // The one refusal a player will actually meet, and the only one that has to
                    // make clear nothing happened: they tapped a button expecting to change
                    // accounts and are still in their own grove.
                    Say(result.Failure == CloudFailure.Offline
                            ? "ui.account.offline" : "ui.account.not_secured", Pal.Rose);
                    break;

                case SwitchOutcome.DifferentAccount:
                    // They asked to be let back in and offered somebody else's account. The
                    // destructive prompt is the honest place for that, so it is what they get —
                    // priced, and asking twice.
                    Contest(_pending);
                    break;

                case SwitchOutcome.Interrupted:
                    Say("ui.account.interrupted", Pal.Rose);
                    break;

                default:
                    Say(result.Failure == CloudFailure.Offline
                            ? "ui.account.offline" : "ui.account.failed", Pal.Rose);
                    break;
            }
        }

        /// <summary>Back to the resting state, having changed something.</summary>
        void Settle()
        {
            _stage = Stage.Resting;
            _recovering = false;
            Rebuild();
        }

        /// <summary>
        /// The grove behind this overlay belongs to somebody else now, so nothing on the
        /// screen underneath is still describing the truth.
        /// </summary>
        void Leave()
            => Tween.After(1.4f, () => { if (this != null) Close(() => Flow.Go<HomeScreen>()); }, this);

        // ------------------------------------------------------- the one hard prompt
        /// <summary>
        /// The player's provider belongs to a grove that is not this one, so the two cannot
        /// both survive.
        ///
        /// <para>
        /// This is the one place in the game that shows a destructive prompt, and it is
        /// unavoidable: the two accounts cannot be merged, because currency was granted and
        /// spent separately against each and no join can reconcile that without inventing or
        /// destroying money. So it says exactly what it will cost and makes the player tap
        /// twice, the same way erasing progress does.
        /// </para>
        /// <para>
        /// Note what is <em>not</em> here any more. It used to hide the sign-in buttons and
        /// squeeze a prompt into the space they left, which worked only as long as the two
        /// layouts stayed the same height. The panel is rebuilt for this state instead, so the
        /// cost line cannot drift back over the button the next time either one moves.
        /// </para>
        /// </summary>
        void Contest(LinkCredential credential)
        {
            _contested = credential;
            _armed = false;
            _stage = Stage.Contested;
            Rebuild();
        }

        void ConfirmAdopt()
        {
            if (_busy) return;

            // Nothing to lose, so nothing to confirm. The double tap is reserved for the case
            // it was invented for; spending it on an empty grove is what teaches players to
            // tap through it on a full one.
            if (!CloudSaveService.HoldsAGrove)
            {
                RunAdopt();
                return;
            }

            var label = _adoptButton != null ? _adoptButton.Find("Text")?.GetComponent<Text>() : null;

            if (!_armed)
            {
                _armed = true;
                if (label) label.text = Loc.Get("ui.account.adopt_confirm");
                Tween.Shake((RectTransform)_adoptButton, 9f, .3f);

                Tween.After(3.2f, () =>
                {
                    if (this == null) return;
                    _armed = false;
                    if (label) label.text = Loc.Get("ui.account.adopt");
                }, this);
                return;
            }

            RunAdopt();
        }

        void RunAdopt()
        {
            _busy = true;
            _recovering = false;
            _pending = _contested;
            Say("ui.account.working", Pal.Cream);
            StartCoroutine(RunBecome(CloudSaveService.AdoptLinkedAccountAsync(_contested)));
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

            // Backing out of the chooser returns to the account, not out of the panel. It is a
            // step in, so back is a step out of it — the same reading the route bubble on the
            // victory panel gets, and the one a player expects from a hardware key.
            if (_stage == Stage.Choosing)
            {
                Settle();
                return true;
            }

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
        ///
        /// <para>
        /// Silent while the device is caught between two accounts. The player is signed in,
        /// so the nudge would be wrong, and they have a more urgent thing to be told — which
        /// the profile card is already saying.
        /// </para>
        /// </summary>
        const string NagKey = "account_prompt_count";
        const int MaxNags = 2;

        public static bool ShouldOffer()
            => CloudSaveService.IsAvailable
               && !CloudSaveService.IsLinked
               && !CloudSaveService.AccountMismatched
               && PlayerPrefs.GetInt(NagKey, 0) < MaxNags;

        public static void NoteOffered()
            => PlayerPrefs.SetInt(NagKey, PlayerPrefs.GetInt(NagKey, 0) + 1);
    }
}
