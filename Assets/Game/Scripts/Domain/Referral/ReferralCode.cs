using System;
using System.Text;

namespace GlimmerGrove.Referral
{
    /// <summary>
    /// What a referral code looks like, and the one fold a typed one goes through.
    ///
    /// <para>
    /// <b>Eight symbols from an alphabet with no look-alikes.</b> No <c>0</c>/<c>O</c>, no
    /// <c>1</c>/<c>I</c>/<c>L</c>: a code is read off a friend's screen, said aloud or typed
    /// from a message, and every one of those pairs is a support ticket. Thirty-one symbols to
    /// the eighth is about eight hundred billion codes, which is not a security bound — a
    /// guessed code only lets a stranger bind themselves to somebody, which pays the stranger
    /// nothing — but is what keeps a mint from ever colliding in practice, and the document id
    /// is what refuses it when it does (invariant 19d).
    /// </para>
    /// <para>
    /// <b>The fold is contract with the server.</b> <c>referral.ts</c> normalises exactly this
    /// way before it looks a code up, so a code typed with a hyphen, in lower case or with a
    /// space in it reaches the same document. Both sides run the shared vectors as a test.
    /// </para>
    /// </summary>
    public static class ReferralCode
    {
        public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
        public const int Length = 8;

        /// <summary>
        /// The most characters a typed code is allowed to be before it is folded. A field is
        /// bounded on the client and the body is bounded again on the server, so an oversized
        /// paste cannot be used to make either side do work.
        /// </summary>
        public const int MaxTyped = 32;

        /// <summary>
        /// The most of a pasted text <see cref="Extract"/> will search. A clipboard can hold a
        /// document; a share message with a code in it is under two hundred characters.
        /// </summary>
        public const int MaxPasted = 2048;

        /// <summary>
        /// Folds what somebody typed into what the server keys on: upper case, separators gone.
        /// Never validates — a fold that also judged would be two rules in one method.
        /// </summary>
        public static string Normalise(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return string.Empty;
            if (typed.Length > MaxTyped) typed = typed.Substring(0, MaxTyped);

            var sb = new StringBuilder(typed.Length);
            for (int i = 0; i < typed.Length; i++)
            {
                char c = typed[i];
                if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.') continue;
                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }

        /// <summary>Whether a <em>folded</em> string is a code this game could have minted.</summary>
        public static bool IsValid(string code)
        {
            if (code == null || code.Length != Length) return false;

            for (int i = 0; i < code.Length; i++)
                if (Alphabet.IndexOf(code[i]) < 0) return false;

            return true;
        }

        /// <summary>The code as a screen prints it: two groups of four, so it can be read aloud.</summary>
        public static string Display(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != Length) return code ?? string.Empty;
            return code.Substring(0, 4) + "-" + code.Substring(4);
        }

        /// <summary>Where <see cref="Display"/> puts the hyphen, counting symbols from nought.</summary>
        public const int Break = 4;

        /// <summary>
        /// The most characters the entry field may hold: a whole code as <see cref="Display"/>
        /// prints it, hyphen included. The field's own limit, so a paste can never overrun it.
        /// </summary>
        public const int MaxShown = Length + 1;

        /// <summary>
        /// What the entry field shows for what has been typed so far: upper case, separators
        /// gone, at most <see cref="Length"/> symbols, and the hyphen put in after the fourth
        /// the moment there is a fifth — so a code read off a friend's screen looks the same in
        /// the field as it did there, whatever case it was typed in and whether or not the
        /// hyphen was.
        ///
        /// <para>
        /// <b>It accepts any letter or digit, not only the alphabet.</b> A key that visibly does
        /// nothing reads as a broken keyboard; a wrong letter that appears and is then refused
        /// by name at redeem (<see cref="IsValid"/>) reads as a mistake the player can see and
        /// fix. The one rule it applies is the one <see cref="Normalise"/> applies, so what the
        /// field shows always folds to what the server would be asked.
        /// </para>
        /// <para>
        /// Idempotent: <c>Present(Present(s)) == Present(s)</c>, which is what lets a field
        /// rewrite its own text on every change without ever fighting itself.
        /// </para>
        /// </summary>
        public static string Present(string typed)
        {
            string folded = Symbols(typed);
            if (folded.Length <= Break) return folded;
            return folded.Substring(0, Break) + "-" + folded.Substring(Break);
        }

        /// <summary>
        /// One keystroke into the entry field: the character to insert, or <c>'\0'</c> to
        /// refuse it. <paramref name="shown"/> is what the field holds and <paramref name="at"/>
        /// where the caret is.
        ///
        /// <para>
        /// A letter or digit is upper-cased. A hyphen is accepted only where
        /// <see cref="Display"/> would put one, so a code pasted in its printed form arrives
        /// whole and a hyphen typed anywhere else is dropped rather than folded away later.
        /// Anything past <see cref="Length"/> symbols is refused: the field is full.
        /// </para>
        /// </summary>
        public static char Key(string shown, int at, char typed)
        {
            if (typed == '-')
                return Symbols(shown).Length == Break && at == Break && (shown == null || shown.IndexOf('-') < 0)
                    ? '-' : '\0';

            if (!IsSymbol(typed)) return '\0';
            if (Symbols(shown).Length >= Length) return '\0';
            return char.ToUpperInvariant(typed);
        }

        /// <summary>
        /// Finds a code inside whatever is on the clipboard.
        ///
        /// <para>
        /// A friend who copies the share sheet's whole sentence and pastes it is the common
        /// case, not the edge: <c>"Play Gemfire with me! Enter my code K7PQ-2XM9 …"</c> folded
        /// as one string is <c>PLAYGEMF</c>, which is eight symbols and not a code. So the text
        /// is walked word by word and the first word that folds to a valid code is the answer;
        /// only when no word does is the whole thing folded, so a bare code pasted with a space
        /// in the middle still lands. Answers empty when nothing code-shaped is there.
        /// </para>
        /// </summary>
        public static string Extract(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            if (text.Length > MaxPasted) text = text.Substring(0, MaxPasted);

            int start = -1;
            for (int i = 0; i <= text.Length; i++)
            {
                bool inWord = i < text.Length && !char.IsWhiteSpace(text[i]);
                if (inWord) { if (start < 0) start = i; continue; }
                if (start < 0) continue;

                string word = Normalise(text.Substring(start, i - start));
                start = -1;
                if (IsValid(word)) return word;
            }

            string whole = Normalise(text);
            return IsValid(whole) ? whole : string.Empty;
        }

        /// <summary>The letters and digits of <paramref name="typed"/>, upper-cased and capped at <see cref="Length"/>.</summary>
        static string Symbols(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return string.Empty;

            var sb = new StringBuilder(Length);
            for (int i = 0; i < typed.Length && sb.Length < Length; i++)
            {
                char c = typed[i];
                if (IsSymbol(c)) sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>ASCII letters and digits only — the code's own script, whatever keyboard is up.</summary>
        static bool IsSymbol(char c)
            => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }
}
