using System;
using GlimmerGrove.Content;
using GlimmerGrove.Localization;
using GlimmerGrove.Referral;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Typing a friend's code.
    ///
    /// <para>
    /// One field, one key, one line of status. The fold and the shape check run on the
    /// device (<see cref="ReferralCode"/>), so a string that could never be a code is refused
    /// without a call; everything else is the server's answer, and every answer it gives has
    /// a sentence here — including <em>too late</em>, which is the one a veteran typing a
    /// friend's code will meet, and which has to say why rather than "no".
    /// </para>
    /// <para>
    /// A bound code closes the panel and hands the word to the screen behind it, which says
    /// what happens next. A refused one keeps the panel open with the field intact: a
    /// mistyped letter is the commonest outcome and retyping eight symbols is the wrong
    /// price for it.
    /// </para>
    /// <para>
    /// <b>The field shows the code the way the friend's screen showed it, whatever was
    /// typed</b> — upper case, the hyphen after the fourth symbol, nothing else — and it
    /// owns none of that rule: every keystroke goes through <see cref="ReferralCode.Key"/>
    /// and what is shown is <see cref="ReferralCode.Present"/> of what was typed, both pure
    /// and both tested. A code is read off another phone, said aloud or copied out of a
    /// message, and a field that lets <c>k7pq2xm9</c> stand beside a friend's
    /// <c>K7PQ-2XM9</c> is a field asking the player to decide whether those are the same
    /// thing. The PASTE key is the other half of that: a code copied from the share
    /// sentence arrives as the whole sentence, so <see cref="ReferralCode.Extract"/> finds
    /// the code inside it rather than folding the sentence into eight wrong letters.
    /// </para>
    /// </summary>
    public sealed class ReferralCodeOverlay : ModalView
    {
        /// <summary>Raised after a code is bound, so the screen behind can say so and redraw.</summary>
        public Action OnRedeemed;

        InputField _field;
        Text _status;
        Btn _redeem;
        bool _claiming;
        string _lastTyped = string.Empty;

        /// <summary>The field and the PASTE key beside it, on one row inside the panel.</summary>
        const float FieldW = 520f, FieldH = 120f, PasteW = 200f, PasteH = 104f, RowGap = 16f;

        static readonly Color Muted = new Color(.48f, .36f, .27f, .9f);
        static readonly Color Good = new Color(.24f, .50f, .26f, 1f);
        static readonly Color Bad = new Color(.68f, .26f, .20f, 1f);

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 640f), Loc.Get("ui.referral.code_title").ToUpperInvariant());

            // The field and the key share one row and are centred as a pair, so the pair
            // stands where the field alone used to.
            float rowW = FieldW + RowGap + PasteW;
            float fieldX = -rowW * .5f + FieldW * .5f;
            float pasteX = rowW * .5f - PasteW * .5f;

            var box = UIKit.Img("Field", Panel, Art.Round(22), new Color(1f, .98f, .93f, .96f),
                                new Vector2(FieldW, FieldH), new Vector2(.5f, 1f), new Vector2(fieldX, -250f));
            box.raycastTarget = true;
            var edge = UIKit.Img("Edge", box.transform, Art.RoundOutline(22, 3f), new Color(.52f, .38f, .26f, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var text = UIKit.Label("Text", box.transform, string.Empty, 48, new Color(.28f, .20f, .13f),
                                   TextAnchor.MiddleCenter);
            UIKit.StretchTo((RectTransform)text.transform, 28, 10, 28, 10);

            var hint = UIKit.Label("Placeholder", box.transform, Loc.Get("ui.referral.code_hint"), 44,
                                   new Color(.42f, .32f, .24f, .40f), TextAnchor.MiddleCenter);
            UIKit.StretchTo((RectTransform)hint.transform, 28, 10, 28, 10);

            _field = box.gameObject.AddComponent<InputField>();
            _field.textComponent = text;
            _field.placeholder = hint;
            _field.lineType = InputField.LineType.SingleLine;

            // Every keystroke is folded as it lands — a lower-case letter is shown upper, a
            // hyphen is kept only where the printed code has one, and anything past eight
            // symbols is refused. `Custom` is what makes `onValidateInput` the rule rather
            // than the built-in alphanumeric filter, which would have dropped the hyphen and
            // kept the case. The same delegate runs on the touch keyboard's text, so a phone
            // gets the rule and not just a desktop keyboard.
            _field.contentType = InputField.ContentType.Custom;
            _field.characterLimit = ReferralCode.MaxShown;
            _field.onValidateInput = (shown, at, typed) => ReferralCode.Key(shown, at, typed);

            // A code is never a word, so the keyboard's own guesses are noise on this field.
            _field.keyboardType = TouchScreenKeyboardType.ASCIICapable;

            UIKit.OneLine(
                UIKit.TextButton("Paste", Panel, Skins.Alternate, Loc.Get("ui.referral.paste"), 30,
                                 new Vector2(PasteW, PasteH), new Vector2(.5f, 1f), new Vector2(pasteX, -250f),
                                 Paste), 16);

            UIKit.Titled("Rule", Panel, Loc.Format("ui.referral.code_rule", ReferralCode.Length), 25,
                         Muted, TextAnchor.MiddleCenter,
                         new Vector2(700f, 36f), new Vector2(.5f, 1f), new Vector2(0f, -330f), 0f, 0f);

            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 28, Muted, TextAnchor.MiddleCenter,
                             new Vector2(700f, 72f), new Vector2(.5f, 1f), new Vector2(0f, -400f), 0f, 0f,
                             wrap: true),
                18);

            _redeem = UIKit.TextButton("Redeem", Panel, Skins.Affirm, Loc.Get("ui.referral.redeem"), 44,
                                       new Vector2(500f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 168f), Commit);
            UIKit.TextButton("Cancel", Panel, Skins.Alternate, Loc.Get("ui.common.cancel"), 36,
                             new Vector2(360f, 100f), new Vector2(.5f, 0f), new Vector2(0f, 54f), () => Close());

            _field.Select();
            _field.ActivateInputField();
        }

        /// <summary>
        /// Keeps the field showing the code as a screen prints it, and clears a stale verdict
        /// the moment the field changes. Polled rather than subscribed, for the rename panel's
        /// reason.
        ///
        /// <para>
        /// The validator has already folded each keystroke; what is left for this to do is put
        /// the hyphen in once there is a fifth symbol, which a per-character rule cannot see.
        /// <see cref="ReferralCode.Present"/> is idempotent, so writing it back can never start
        /// a loop, and the caret goes to the end because the field only ever grows there.
        /// </para>
        /// </summary>
        void Update()
        {
            if (_field == null || _claiming) return;

            string typed = _field.text ?? string.Empty;
            if (string.Equals(typed, _lastTyped, StringComparison.Ordinal)) return;

            string shown = ReferralCode.Present(typed);
            if (!string.Equals(shown, typed, StringComparison.Ordinal))
            {
                _field.text = shown;
                _field.MoveTextEnd(false);
            }

            _lastTyped = shown;
            Say(string.Empty, Muted);
        }

        /// <summary>
        /// Puts whatever is on the clipboard into the field, as a code.
        ///
        /// <para>
        /// The code is looked for inside the text rather than the text being folded whole,
        /// because what a friend copies is the share sheet's sentence and the code is eight
        /// symbols in the middle of it. When nothing code-shaped is there, the field shows the
        /// fold of what was pasted so the player can see what arrived, and the status says so.
        /// Nothing is submitted: the REDEEM key is still the player's, so a paste can never
        /// spend a one-shot bind on text they did not mean.
        /// </para>
        /// </summary>
        void Paste()
        {
            if (_claiming || _field == null) return;

            string clip = ShareSheet.Paste();
            string code = ReferralCode.Extract(clip);
            string shown = code.Length > 0 ? ReferralCode.Display(code) : ReferralCode.Present(clip);

            if (shown.Length == 0)
            {
                Say(Loc.Get("ui.referral.paste_empty"), Bad);
                Tween.Shake((RectTransform)_field.transform, 10f, .3f);
                return;
            }

            _field.text = shown;
            _field.MoveTextEnd(false);
            _lastTyped = shown;
            Audio.Sfx("click", .5f);

            Say(code.Length > 0 ? string.Empty : Loc.Get("ui.referral.redeem_bad_code"),
                code.Length > 0 ? Muted : Bad);

            _field.Select();
            _field.ActivateInputField();
        }

        void Commit()
        {
            if (_claiming || _field == null) return;

            string typed = _field.text ?? string.Empty;
            string code = ReferralCode.Normalise(typed);

            if (!ReferralCode.IsValid(code))
            {
                Say(Loc.Get("ui.referral.redeem_bad_code"), Bad);
                Tween.Shake((RectTransform)_field.transform, 10f, .3f);
                return;
            }

            if (Net.Offline)
            {
                Say(Loc.Get("ui.referral.redeem_unavailable"), Bad);
                return;
            }

            _claiming = true;
            if (_redeem) _redeem.Interactable = false;
            Say(Loc.Get("ui.referral.redeeming"), Muted);

            Run(async token =>
            {
                ReferralRedeemOutcome outcome;
                try { outcome = await ReferralLedger.RedeemAsync(code, token); }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    Debug.LogException(e);
                    outcome = ReferralRedeemOutcome.Unavailable;
                }

                if (!Living) return;

                _claiming = false;
                if (_redeem) _redeem.Interactable = true;

                if (outcome == ReferralRedeemOutcome.Bound)
                {
                    Audio.Sfx("collect", .6f);
                    var done = OnRedeemed;
                    OnRedeemed = null;
                    Close();
                    done?.Invoke();
                    return;
                }

                Say(Loc.Format(KeyFor(outcome), ChapterName), Bad);
                if (_field) Tween.Shake((RectTransform)_field.transform, 10f, .3f);
            });
        }

        static string ChapterName
        {
            get
            {
                var chapter = GameContent.Index?.FindChapter(ReferralLedger.Table.Milestone);
                return chapter != null ? Loc.Get(chapter.NameKey) : ReferralLedger.Table.Milestone.Value;
            }
        }

        /// <summary>Every refusal has a sentence, and the sentence names the reason.</summary>
        static string KeyFor(ReferralRedeemOutcome outcome)
        {
            switch (outcome)
            {
                case ReferralRedeemOutcome.BadCode: return "ui.referral.redeem_bad_code";
                case ReferralRedeemOutcome.UnknownCode: return "ui.referral.redeem_unknown";
                case ReferralRedeemOutcome.OwnCode: return "ui.referral.redeem_own";
                case ReferralRedeemOutcome.AlreadyReferred: return "ui.referral.redeem_already";
                case ReferralRedeemOutcome.Full: return "ui.referral.redeem_full";
                case ReferralRedeemOutcome.TooLate: return "ui.referral.redeem_too_late";
                case ReferralRedeemOutcome.NoSave: return "ui.referral.redeem_no_save";
                default: return "ui.referral.redeem_unavailable";
            }
        }

        void Say(string message, Color colour)
        {
            if (!_status) return;
            _status.text = message;
            _status.color = colour;
        }

        public override bool OnBack()
        {
            Close();
            return true;
        }
    }
}
