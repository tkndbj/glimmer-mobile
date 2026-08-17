using System;
using System.Text;
using GlimmerGrove.Localization;
using GlimmerGrove.Persistence;
using UnityEngine;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Renaming the grovekeeper.
    ///
    /// <para>
    /// The name is stored locally and travels with the save, so today it is a private
    /// label a player picks for themselves. The moment it becomes visible to anyone
    /// else — ranks are the obvious first surface — it needs moderation, and that
    /// moderation has to be <em>server</em> side: a filter shipped in the client is a
    /// filter that can be read, and a name accepted here would already be in Firestore
    /// before anything could reject it. <see cref="Clean"/> is therefore deliberately
    /// only what a text field owes a database: bounded length, no control characters,
    /// no leading or trailing space.
    /// </para>
    /// </summary>
    public sealed class RenameOverlay : ModalView
    {
        /// <summary>Raised after a name is committed, so the screen behind can redraw.</summary>
        public Action OnRenamed;

        public const int MaxLength = 16;

        InputField _field;

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
                         new Color(.48f, .36f, .27f, .9f), TextAnchor.MiddleCenter,
                         new Vector2(700f, 36f), new Vector2(.5f, 1f), new Vector2(0f, -336f), 0f, 0f);

            UIKit.TextButton("Save", Panel, "btn_green", Loc.Get("ui.common.done"), 44,
                             new Vector2(500f, 126f), new Vector2(.5f, 0f), new Vector2(0f, 168f), Commit);
            UIKit.TextButton("Cancel", Panel, Skins.Alternate, Loc.Get("ui.profile.cancel"), 36,
                             new Vector2(360f, 100f), new Vector2(.5f, 0f), new Vector2(0f, 54f), () => Close());

            // The field is focused rather than waiting for a second tap: this panel has
            // exactly one thing to do and the keyboard is the whole of it.
            _field.Select();
            _field.ActivateInputField();
        }

        void Commit()
        {
            string chosen = Clean(_field != null ? _field.text : null);
            if (!string.Equals(chosen, Profile.Name, StringComparison.Ordinal))
            {
                Profile.Name = chosen;
                Audio.Sfx("chime2", .5f);
            }

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

        public override bool OnBack() { Close(); return true; }
    }
}
