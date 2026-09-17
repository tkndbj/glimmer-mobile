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

        static readonly Color Muted = new Color(.48f, .36f, .27f, .9f);
        static readonly Color Good = new Color(.24f, .50f, .26f, 1f);
        static readonly Color Bad = new Color(.68f, .26f, .20f, 1f);

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 640f), Loc.Get("ui.referral.code_title").ToUpperInvariant());

            var box = UIKit.Img("Field", Panel, Art.Round(22), new Color(1f, .98f, .93f, .96f),
                                new Vector2(640f, 120f), new Vector2(.5f, 1f), new Vector2(0f, -250f));
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
            _field.characterLimit = ReferralCode.Length + 4;      // room for a typed hyphen or two
            _field.lineType = InputField.LineType.SingleLine;
            _field.contentType = InputField.ContentType.Alphanumeric;

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

        /// <summary>Clears a stale verdict the moment the field changes, for the rename panel's reason.</summary>
        void Update()
        {
            if (_field == null || _claiming) return;

            string typed = _field.text ?? string.Empty;
            if (string.Equals(typed, _lastTyped, StringComparison.Ordinal)) return;

            _lastTyped = typed;
            Say(string.Empty, Muted);
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
