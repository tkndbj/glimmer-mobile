using System;
using System.Text;
using System.Threading;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using GlimmerGrove.Social;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Renaming the grovekeeper.
    ///
    /// <para>
    /// The name is stored locally and travels with the save, so on this device it is still a
    /// private label a player picks for themselves — <see cref="Clean"/> is deliberately only
    /// what a text field owes a database: bounded length, no control characters, no leading or
    /// trailing space. Two further rules sit on top of it and neither can live here.
    /// <c>GroveNames</c> decides what a <em>stranger</em> may be shown, and moderation is
    /// server-side because a filter shipped in a client is a filter that can be read out of it.
    /// <c>KeeperNames</c> decides whether anybody else already has the name, which no device
    /// can know on its own.
    /// </para>
    /// <para>
    /// <b>Uniqueness is checked while typing and decided when saving, and the split is a cost
    /// decision.</b> The hint under the field is one Firestore document read, debounced by
    /// <see cref="NameCheckScheduler"/> so a name typed at speed costs one read rather than one
    /// per keystroke; the claim is a transaction on the server and is the only answer that
    /// counts. Two players can be typing one name at the same moment, so a hint that says free
    /// and a claim that says taken is an ordinary sequence rather than a bug — which is exactly
    /// why the cheap half is allowed to be approximate.
    /// </para>
    /// <para>
    /// <b>A rename never fails.</b> Offline, signed out, or in a build with no backend, the
    /// name is stored and the panel closes: the reservation is attempted by the next publish
    /// instead (see <c>functions/src/index.ts</c>). Making the one thing a player does about
    /// their own identity the only thing in the game that needs a signal would be a strange
    /// place to draw that line, and the board simply keeps showing the previous name until the
    /// claim lands.
    /// </para>
    /// </summary>
    public sealed class RenameOverlay : ModalView
    {
        /// <summary>Raised after a name is committed, so the screen behind can redraw.</summary>
        public Action OnRenamed;

        public const int MaxLength = 16;

        readonly NameCheckScheduler _names = new NameCheckScheduler();

        /// <summary>
        /// Cancelled when the panel goes, so a reply that lands afterwards knows to stop.
        ///
        /// A token rather than a null check on this component: a destroyed <c>MonoBehaviour</c>
        /// compares equal to null and reads as alive to anything holding a plain reference, and
        /// the continuation here runs on the main thread where that distinction is exactly the
        /// one that produces a <c>MissingReferenceException</c> two frames later.
        /// </summary>
        CancellationTokenSource _alive = new CancellationTokenSource();

        InputField _field;
        Text _status;
        Btn _save;
        string _lastTyped = string.Empty;
        bool _claiming;

        /// <summary>
        /// What the panel is saying that the availability check did not decide: a claim in
        /// flight, or its verdict. Cleared the moment the field changes, because a message
        /// about a name nobody is typing any more is worse than no message.
        /// </summary>
        string _override = string.Empty;
        Color _overrideColour;

        static readonly Color Muted = new Color(.48f, .36f, .27f, .9f);
        static readonly Color Good = new Color(.24f, .50f, .26f, 1f);
        static readonly Color Bad = new Color(.68f, .26f, .20f, 1f);

        protected override void Build()
        {
            MakePanel(new Vector2(860f, 660f), Loc.Get("ui.profile.rename").ToUpperInvariant());

            var box = UIKit.Img("Field", Panel, Art.Round(22), new Color(1f, .98f, .93f, .96f),
                                new Vector2(640f, 120f), new Vector2(.5f, 1f), new Vector2(0f, -250f));
            box.raycastTarget = true;
            var edge = UIKit.Img("Edge", box.transform, Art.RoundOutline(22, 3f), new Color(.52f, .38f, .26f, .55f));
            UIKit.StretchTo((RectTransform)edge.transform, 0, 0, 0, 0);

            var text = UIKit.Label("Text", box.transform, string.Empty, 44, new Color(.28f, .20f, .13f),
                                   TextAnchor.MiddleLeft);
            UIKit.StretchTo((RectTransform)text.transform, 28, 10, 28, 10);

            var hint = UIKit.Label("Placeholder", box.transform, Loc.Get("ui.profile.name_hint"), 40,
                                   new Color(.42f, .32f, .24f, .55f), TextAnchor.MiddleLeft);
            UIKit.StretchTo((RectTransform)hint.transform, 28, 10, 28, 10);

            _field = box.gameObject.AddComponent<InputField>();
            _field.textComponent = text;
            _field.placeholder = hint;
            _field.characterLimit = MaxLength;
            _field.lineType = InputField.LineType.SingleLine;
            _field.text = Profile.Name;

            UIKit.Titled("Rule", Panel, Loc.Format("ui.profile.name_rule", MaxLength), 25,
                         Muted, TextAnchor.MiddleCenter,
                         new Vector2(700f, 36f), new Vector2(.5f, 1f), new Vector2(0f, -330f), 0f, 0f);

            // Shrinkable because it holds a translated sentence and UIKit.Label overflows
            // rather than clipping — an over-long line here would simply keep drawing over the
            // buttons underneath it.
            _status = UIKit.Shrinkable(
                UIKit.Titled("Status", Panel, string.Empty, 30, Muted, TextAnchor.MiddleCenter,
                             new Vector2(700f, 40f), new Vector2(.5f, 1f), new Vector2(0f, -392f), 0f, 0f),
                20);

            _save = UIKit.TextButton("Save", Panel, "btn_green", Loc.Get("ui.common.done"), 44,
                                     new Vector2(500f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 168f), Commit);
            UIKit.TextButton("Cancel", Panel, Skins.Alternate, Loc.Get("ui.profile.cancel"), 36,
                             new Vector2(360f, 100f), new Vector2(.5f, 0f), new Vector2(0f, 54f), () => Close());

            // The name already in the field is the one this account is presumed to hold, so
            // opening the panel and pressing save asks the server nothing at all.
            _names.Hold(Profile.Name);
            _lastTyped = _field.text ?? string.Empty;
            _names.Typed(_lastTyped);
            PaintStatus();

            // The field is focused rather than waiting for a second tap: this panel has
            // exactly one thing to do and the keyboard is the whole of it.
            _field.Select();
            _field.ActivateInputField();
        }

        /// <summary>
        /// Polls the field rather than subscribing to <c>onValueChanged</c>.
        ///
        /// The scheduler has to be handed elapsed time every frame regardless — that is what
        /// makes the debounce a debounce — so a subscription would be a second path into the
        /// same state and one more thing to unwind on close. Same reasoning as the run clock's
        /// start edge, which polls the move count for the same reason.
        /// </summary>
        void Update()
        {
            if (_field == null) return;

            string typed = _field.text ?? string.Empty;

            if (!string.Equals(typed, _lastTyped, StringComparison.Ordinal))
            {
                _lastTyped = typed;
                _override = string.Empty;
                _names.Typed(typed);
                PaintStatus();
            }

            if (_claiming) return;

            if (_names.Tick(Time.unscaledDeltaTime, out string key))
                BeginCheck(key);
        }

        /// <summary>
        /// Asks the server about one name.
        ///
        /// <c>async void</c> because it is fired from <c>Update</c> and nothing waits on it —
        /// which makes the try/catch mandatory rather than tidy: an exception escaping an
        /// <c>async void</c> has no caller to reach and is raised on the synchronisation
        /// context, where in a player build it is simply lost.
        /// </summary>
        async void BeginCheck(string key)
        {
            var token = _alive.Token;

            try
            {
                var (result, taken, mine) = await KeeperNames.CheckAsync(key, token);

                if (token.IsCancellationRequested) return;

                if (result.Ok) _names.Answered(key, taken, mine);
                else _names.Failed(key);

                PaintStatus();
            }
            catch (OperationCanceledException)
            {
                // The panel closed while the read was out. Not a fault.
            }
            catch (Exception e)
            {
                if (token.IsCancellationRequested) return;

                // A hint is the one thing here allowed to fail quietly: the claim still
                // adjudicates, so the player loses a line of text and nothing else.
                Debug.LogException(e);
                _names.Failed(key);
                PaintStatus();
            }
        }

        void PaintStatus()
        {
            if (_status == null) return;

            if (_override.Length > 0)
            {
                _status.text = _override;
                _status.color = _overrideColour;
                if (_save != null) _save.Interactable = !_claiming;
                return;
            }

            // Every branch that used to live here is `RenameRules.LineFor`, so this method draws
            // and decides nothing. See that class for why.
            var line = RenameRules.LineFor(_names.Availability, _lastTyped.Trim().Length == 0);

            _status.text = line.Key.Length == 0
                ? string.Empty
                : line.TakesMinimum ? Loc.Format(line.Key, GroveNames.MinLength) : Loc.Get(line.Key);

            _status.color = Paint(line.Tone);

            if (_save != null) _save.Interactable = line.CanSave && !_claiming;
        }

        static Color Paint(NameTone tone)
        {
            switch (tone)
            {
                case NameTone.Good: return Good;
                case NameTone.Bad: return Bad;
                default: return Muted;
            }
        }

        void Say(string line, Color colour)
        {
            _override = line ?? string.Empty;
            _overrideColour = colour;
            PaintStatus();
        }

        /// <summary>
        /// Saving.
        ///
        /// <c>async void</c> because it is a button handler; the try/catch is therefore
        /// mandatory for <see cref="BeginCheck"/>'s reason, and the <c>finally</c> more so — a
        /// throw that left <c>_claiming</c> set would leave the save button dead for as long as
        /// the panel was open, with nothing on screen to explain it.
        /// </summary>
        async void Commit()
        {
            if (_claiming) return;

            string chosen = Clean(_field != null ? _field.text : null);

            // Unchanged is the commonest press of this button by a wide margin — somebody
            // opened the panel and thought better of it — and it must cost nothing at all.
            if (string.Equals(chosen, Profile.Name, StringComparison.Ordinal))
            {
                Dismiss();
                return;
            }

            // With nothing to adjudicate against there is no uniqueness to enforce, so the
            // rename is immediate. A build with no backend behaves exactly as it always did.
            if (!KeeperNames.IsAvailable || !GroveNames.IsPublishable(chosen))
            {
                Apply(chosen);
                return;
            }

            var token = _alive.Token;

            _claiming = true;
            Say(Loc.Get("ui.profile.name_saving"), Muted);

            NameClaim claim;

            try
            {
                claim = await KeeperNames.ClaimAsync(chosen, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Debug.LogException(e);

                // A rename must never be lost to a fault. Nothing was adjudicated, so this is
                // the offline answer: keep the name and let the next publish claim it.
                claim = NameClaim.Unavailable;
            }
            finally
            {
                _claiming = false;
            }

            if (token.IsCancellationRequested) return;

            var step = RenameRules.ResolveClaim(claim.Outcome);

            if (step.IsSetback) Audio.Sfx("blocked", .5f);

            if (step.StoresName)
            {
                Profile.Name = chosen;
                if (!step.IsSetback) Audio.Sfx("chime2", .5f);
            }
            else
            {
                // The hint believed this name was free a moment ago and it is not, so the
                // claim's answer replaces it — otherwise pressing save twice would report two
                // different things about one name.
                _names.Adopt(GroveNames.Key(chosen), NameAvailability.Taken);
            }

            if (step.MessageKey.Length == 0) _override = string.Empty;
            else Say(step.TakesCooldown
                        ? Loc.Format(step.MessageKey, Mathf.Max(1, claim.CooldownSeconds))
                        : Loc.Get(step.MessageKey),
                     Paint(step.Tone));

            PaintStatus();

            if (!step.Closes) return;

            // A message on the way out is held for a beat rather than handed to a toast: a
            // toast belongs to the view that raises it, and this view is closing.
            if (step.MessageKey.Length > 0) Tween.After(1.6f, () => { if (this != null) Dismiss(); });
            else Dismiss();
        }

        void Apply(string chosen)
        {
            Profile.Name = chosen;
            Audio.Sfx("chime2", .5f);
            Dismiss();
        }

        void Dismiss()
        {
            var after = OnRenamed;
            Close(() => after?.Invoke());
        }

        /// <summary>
        /// What a text field owes storage: trimmed, bounded, and free of characters that
        /// would break a layout or a log line. Anything left empty falls back to the
        /// default name rather than being stored blank.
        /// </summary>
        public static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Wallet.DefaultName;

            var builder = new StringBuilder(MaxLength);
            foreach (char c in raw.Trim())
            {
                if (char.IsControl(c)) continue;
                builder.Append(c);
                if (builder.Length >= MaxLength) break;
            }

            string cleaned = builder.ToString().Trim();
            return cleaned.Length == 0 ? Wallet.DefaultName : cleaned;
        }

        public override bool OnBack()
        {
            // A claim in flight is not a reason to trap somebody in a panel: the call finishes
            // on its own and its continuation sees a cancelled token.
            Close();
            return true;
        }

        void OnDestroy()
        {
            // Cancelled before disposal, so a continuation already queued reads cancellation
            // rather than racing a disposed source.
            _alive.Cancel();
            _alive.Dispose();
        }
    }
}
