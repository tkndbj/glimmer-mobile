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
    }
}
