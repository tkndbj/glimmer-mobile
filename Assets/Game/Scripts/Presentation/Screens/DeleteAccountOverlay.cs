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
    /// "Delete this account?" — the third confirmation in a game that deliberately has two.
    ///
    /// <para>
    /// <b>Why it earns one.</b> <c>ForfeitOverlay</c> guards the act that costs a heart and
    /// <c>ReportNameOverlay</c> guards the act taken against another person; the rule behind
    /// both is that a confirmation belongs on what cannot be undone from the screen that did
    /// it. Nothing else in this game qualifies as completely as this does. There is no store
    /// to re-deliver it, no archive to restore it from and no support path that can bring it
    /// back — the server is asked to erase the account precisely so that nobody, including us,
    /// holds a copy afterwards.
    /// </para>
    /// <para>
    /// <b>The copy is the feature.</b> Everything here is written to be read by somebody who
    /// is about to lose years of play, so it names what goes rather than gesturing at it, and
    /// every failure sentence ends with "nothing has been deleted" — which is a promise
    /// <c>CloudSaveService.DeleteAccountAsync</c> actually keeps: the local grove is not
    /// touched until the server has confirmed, so every outcome except
    /// <see cref="AccountDeletion.Outcome.Deleted"/> genuinely changed nothing. That is
    /// <see cref="AccountDeletion.Untouched"/>, and it is why the panel can say so without
    /// hedging.
    /// </para>
    /// <para>
    /// <b>The affirmative is red, second, and the safe answer takes the resting position</b> —
    /// <c>ReportNameOverlay</c>'s layout rather than <c>ForfeitOverlay</c>'s, for its reason:
    /// the cheap answer here is to walk away, so walking away gets the green. It is also the
    /// same rule that puts the free way back above the paid one on the defeat panel; a
    /// destructive control drawn above the harmless one, on the screen where somebody has just
    /// been asked the most consequential question in the game, is the shape a store reviewer is
    /// right to object to.
    /// </para>
    /// <para>
    /// <b>The second tap is spent only where there is something to lose</b>, which is
    /// <c>AccountOverlay.ConfirmAdopt</c>'s rule word for word: arming a button over an empty
    /// grove is what teaches a player to tap through it on a full one.
    /// </para>
    /// </summary>
    public sealed class DeleteAccountOverlay : ModalView
    {
        /// <summary>What the panel is asking for.</summary>
        enum Stage
        {
            /// <summary>The warning, and the two answers to it.</summary>
            Warning,

            /// <summary>
            /// Confirmed, and the account has a provider on it — so the provider is asked to
            /// vouch for whoever is holding the phone before anything is removed. See
            /// <see cref="AccountDeletion.Verdict.Reauthenticate"/>.
            /// </summary>
            Verifying,
        }

        Stage _stage;
        Text _status;
        Transform _deleteButton;
        bool _busy, _armed, _finished;

        // Anchored to the top edge and positioned by their own centres, because UIKit.Box
        // pivots centrally whatever it is anchored to. AccountOverlay's arrangement, and for
        // its reason: this panel's height depends on what it is saying, so the rows are
        // measured before anything is built.
        const float TopMargin = 96f, BottomMargin = 52f;
        const float StatusH = 50f, BodyH = 150f, LineH = 76f, NoteH = 58f;
        const float ButtonH = 118f, BeforeButtons = 10f;
        const float AfterStatus = 14f, AfterBody = 14f, AfterLine = 12f, AfterNote = 16f;
        const float BetweenButtons = 14f;

        readonly List<float> _gaps = new List<float>();
        float _cursor;
        int _row;

        protected override void Build()
        {
            bool available = CloudSaveService.IsAvailable;
            bool linked = available && CloudSaveService.IsLinked;

            bool verifying = _stage == Stage.Verifying;

            // PlayerProgress rather than PlayerProgression, for AccountOverlay's reason: the
            // derived total drops any glade whose chapter is not loaded, which is right for
            // reward arithmetic and wrong for a sentence. A panel that told somebody they had
            // nothing to lose because the content index was a moment late would be wrong in the
            // one direction this screen must never be wrong in.
            int cleared = PlayerProgress.ClearedCount;
            bool costly = cleared > 0;

            // ------------------------------------------------------------------ measure
            _gaps.Clear();
            float height = TopMargin + BottomMargin;

            void Row(float h, float gap)
            {
                _gaps.Add(gap);
                height += h + gap;
            }

            Row(StatusH, AfterStatus);
            Row(BodyH, AfterBody);
            if (costly) Row(LineH, AfterLine);
            Row(LineH, AfterLine);
            if (linked) Row(NoteH, AfterNote);

            height += BeforeButtons;

            if (verifying) { Row(ButtonH, BetweenButtons); Row(ButtonH, BetweenButtons); }
            else Row(ButtonH, BetweenButtons);

            Row(ButtonH, 0f);

            // No scrim dismissal, for ForfeitOverlay's reason: this is a question with a
            // consequence, and a stray tap outside the panel is not an answer to it.
            MakePanel(new Vector2(880f, height), Loc.Get("ui.delete.title"), dismissOnScrim: false);
            _cursor = TopMargin;
            _row = 0;

            // ------------------------------------------------------------------- status
            _status = Line("Status", Loc.Get(verifying ? "ui.delete.verify" : "ui.delete.title"),
                           32, verifying ? new Color(.44f, .32f, .24f, .95f) : Pal.Rose,
                           StatusH, TextAnchor.MiddleCenter, 2f);
            Fit(_status, 24, 32);

            // --------------------------------------------------------------------- body
            Fit(Line("Body", Loc.Get("ui.delete.body"), 27, new Color(.44f, .32f, .24f, .95f),
                     BodyH, TextAnchor.UpperCenter, 0f), 20, 27);

            // Named concretely rather than as "your progress". Somebody three weeks in
            // deserves to see the three weeks before they tap.
            if (costly)
                Fit(Line("Stakes", Loc.Format("ui.delete.stakes", cleared), 28,
                         new Color(.62f, .26f, .24f), LineH, TextAnchor.UpperCenter, 0f), 20, 28);

            // Said on every account, whether or not this one has ever paid. A player who has
            // bought nothing loses nothing by reading it, and one who has must not find out
            // afterwards — a receipt is recorded against its transaction globally and can never
            // be redeemed again under a new account, so this is the one loss no amount of
            // playing earns back.
            Fit(Line("Purchases", Loc.Get("ui.delete.purchases"), 25,
                     new Color(.44f, .32f, .24f, .88f), LineH, TextAnchor.UpperCenter, 0f), 19, 25);

            // Warned about before the first tap rather than sprung at the second. A provider
            // sheet appearing unannounced after "delete everything" reads as the deletion
            // having gone wrong.
            if (linked)
                Fit(Line("Verify", Loc.Get("ui.delete.verify"), 24,
                         new Color(.44f, .32f, .24f, .78f), NoteH, TextAnchor.UpperCenter, 0f), 18, 24);

            // ------------------------------------------------------------------ buttons
            _cursor += BeforeButtons;

            if (verifying)
            {
                // Both offered rather than only the one this account was linked with. Which
                // provider it is is not a fact this panel holds, and guessing wrong would put a
                // dead button in front of somebody mid-deletion — where picking the wrong one
                // costs nothing at all, because a credential belonging to another account is
                // refused by Firebase's own mismatch check with the session left exactly as it
                // was. See ICloudSaveBackend.ReauthenticateAsync.
                Button("Google", "btn_blue", Loc.Get("ui.account.google"),
                       () => Begin(LinkCredential.ForGoogle()));

                Button("Apple", "btn_blue", Loc.Get("ui.account.apple"),
                       () => Begin(LinkCredential.ForApple()));
            }
            else
            {
                _deleteButton = Button("Delete", "btn_red", Loc.Get("ui.delete.confirm"), Confirm);
            }

            // Last and green, and it stays live through the provider stage: backing out is
            // free right up until the server is called, and a panel that removes the way out
            // the moment somebody taps once is a panel that has stopped asking a question.
            Button("Keep", "btn_green", Loc.Get("ui.delete.keep"), Dismiss);
        }

        // ------------------------------------------------------------------- layout
        Text Line(string name, string text, int size, Color colour, float height,
                  TextAnchor anchor, float shadow)
        {
            var label = UIKit.Titled(name, Panel, text, size, colour, anchor,
                                     new Vector2(720f, height), new Vector2(.5f, 1f),
                                     new Vector2(0f, -(_cursor + height * .5f)),
                                     outline: 0f, shadow: shadow, wrap: true);
            Advance(height);
            return label;
        }

        Transform Button(string name, string skin, string label, Action onTap)
        {
            var button = UIKit.TextButton(name, Panel, skin, label, 36,
                                          new Vector2(620f, ButtonH), new Vector2(.5f, 1f),
                                          new Vector2(0f, -(_cursor + ButtonH * .5f)), onTap);
            Advance(ButtonH);
            return button.transform;
        }

        /// <summary>
        /// Steps past the row just drawn, taking the gap the measure recorded rather than
        /// deciding one here. The measure and the build read the same flags a few dozen lines
        /// apart, and keeping them in step is the one thing to check when editing this.
        /// </summary>
        void Advance(float height)
            => _cursor += height + (_row < _gaps.Count ? _gaps[_row++] : 0f);

        static void Fit(Text label, int min, int max)
        {
            if (!label) return;

            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = min;
            label.resizeTextMaxSize = max;
        }

        // -------------------------------------------------------------------- the flow
        /// <summary>
        /// The red button. Arms first on an account with something to lose, then either asks
        /// the provider to vouch for the player or goes straight to the server.
        /// </summary>
        void Confirm()
        {
            if (_busy || _finished) return;

            if (PlayerProgress.ClearedCount > 0 && !_armed)
            {
                _armed = true;

                var label = _deleteButton != null
                    ? _deleteButton.Find("Text")?.GetComponent<Text>() : null;

                if (label) label.text = Loc.Get("ui.delete.confirm");
                Say("ui.delete.armed", Pal.Rose);

                Tween.Shake((RectTransform)_deleteButton, 9f, .3f);

                // Disarmed on a timer, so a panel left open on a table cannot be finished by
                // somebody walking past it — and so the second tap is always a deliberate one
                // rather than the tail of a double tap on the first.
                Tween.After(3.2f, () => { if (this != null) _armed = false; }, this);
                return;
            }

            switch (AccountDeletion.Required(CloudSaveService.IsAvailable, CloudSaveService.IsLinked))
            {
                case AccountDeletion.Verdict.Reauthenticate:
                    _armed = false;
                    _stage = Stage.Verifying;
                    Rebuild();
                    return;

                case AccountDeletion.Verdict.ConfirmOnly:
                    Begin(default);
                    return;

                default:
                    // Unreachable from a drawn control — the profile card asks
                    // AccountDeletion.Offered before it draws the button at all — and answered
                    // rather than thrown, because a panel that can be opened by a deep link or
                    // a stale screen should say something true instead of crashing.
                    Say("ui.delete.failed", Pal.Rose);
                    return;
            }
        }

        void Begin(LinkCredential credential)
        {
            if (_busy || _finished) return;

            _busy = true;
            _armed = false;

            Say(credential.IsValid ? "ui.delete.verifying" : "ui.delete.working", Pal.Cream);
            StartCoroutine(Run(CloudSaveService.DeleteAccountAsync(credential)));
        }

        /// <summary>
        /// Reports one attempt. Every branch but the first left the account exactly as it was,
        /// and says so — see <see cref="AccountDeletion.Untouched"/>.
        /// </summary>
        IEnumerator Run(Task<DeleteResult> task)
        {
            while (!task.IsCompleted) yield return null;

            _busy = false;

            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception);
                Say("ui.delete.failed", Pal.Rose);
                yield break;
            }

            var result = task.Result;

            if (result.Ok)
            {
                _finished = true;

                Say("ui.delete.done", new Color(.44f, .32f, .24f, .95f));

                // Home rather than back to the profile. Everything that screen draws — the
                // keeper, the record, the companions, the boards, the account — belongs to an
                // account that no longer exists, and rebuilding it in place would be five
                // cards quietly resetting themselves in front of somebody. The hub is where a
                // fresh grove starts.
                Tween.After(1.8f, () =>
                {
                    if (this == null) return;
                    Close(() => Flow.Go<HomeScreen>());
                }, this);

                yield break;
            }

            Debug.LogWarning($"[Account] delete ended as {result.Outcome} " +
                             $"({result.Failure}: {result.Message})");

            switch (result.Outcome)
            {
                case AccountDeletion.Outcome.Cancelled:
                    Say("ui.delete.cancelled", new Color(.44f, .32f, .24f, .9f));
                    break;

                case AccountDeletion.Outcome.Offline:
                    Say("ui.delete.offline", Pal.Rose);
                    break;

                case AccountDeletion.Outcome.WrongAccount:
                    Say("ui.delete.wrong_account", Pal.Rose);
                    break;

                case AccountDeletion.Outcome.Busy:
                    Say("ui.delete.busy", Pal.Rose);
                    break;

                default:
                    Say("ui.delete.failed", Pal.Rose);
                    break;
            }
        }

        void Dismiss()
        {
            if (_busy || _finished) return;
            Close();
        }

        void Say(string key, Color colour)
        {
            if (_status == null) return;
            _status.text = Loc.Get(key);
            _status.color = colour;
        }

        public override bool OnBack()
        {
            if (_busy || _finished) return true;    // mid-flight, or already gone; swallow it

            // Backing out of the provider stage returns to the question rather than out of the
            // panel — it is a step in, so back is a step out of it. AccountOverlay's reading,
            // and the one a hardware key is expected to have.
            if (_stage == Stage.Verifying)
            {
                _stage = Stage.Warning;
                Rebuild();
                return true;
            }

            Close();
            return true;
        }
    }
}
