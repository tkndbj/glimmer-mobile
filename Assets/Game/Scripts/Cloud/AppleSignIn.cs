using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GlimmerGrove.Cloud
{
    /// <summary>
    /// Sign in with Apple through Apple's own sheet, producing the identity token that
    /// <see cref="LinkCredential"/> carries.
    ///
    /// <para>
    /// <b>Why this exists at all.</b> The design note this project shipped with said Firebase
    /// drives the OAuth flow itself, so neither Apple's nor Google's plugin is a dependency —
    /// one path, both providers, both platforms. That is true for Google on both platforms and
    /// for Apple on Android, and it is false for Apple on iOS. FirebaseAuth's generic IDP path
    /// does not merely fail there, it calls <c>fatalError</c>:
    /// <c>"Sign in with Apple is not supported via generic IDP; You must use the Apple SDK for
    /// Sign in with Apple."</c> A Swift <c>fatalError</c> is not an exception — no managed
    /// <c>catch</c> runs, the process is killed, and the player sees the game vanish the
    /// instant they tap the button. Nothing in a build or a validator can see it, because the
    /// refusal lives inside Apple's framework and only fires on a device.
    /// </para>
    /// <para>
    /// <b>The rest of the flow was already right.</b> <c>FirebaseCloudSaveBackend</c> has
    /// always branched on <see cref="LinkCredential.HasToken"/> and called
    /// <c>LinkWithCredentialAsync</c> when one is present. Nothing on iOS ever produced a
    /// token for Apple, so it fell to the generic branch. This fills that gap and changes no
    /// other path: Google is untouched, Android is untouched, and the Editor is untouched.
    /// </para>
    /// <para>
    /// <b>The nonce is the whole security of it.</b> A random value is generated here, its
    /// SHA-256 goes to Apple (which signs it into the JWT), and the <em>raw</em> one goes to
    /// Firebase, which hashes it again and compares. That is what stops a token lifted from
    /// one sign-in being replayed into another account. The two must come from the same call,
    /// which is why this returns both rather than letting a caller pair them up.
    /// </para>
    /// <para>
    /// No managed callback crosses into native — the plugin is polled, for the reason
    /// <c>AppTrackingPrompt</c> gives: a managed function pointer held across a native
    /// callback needs a static <c>MonoPInvokeCallback</c> and is a documented way to crash
    /// under IL2CPP.
    /// </para>
    /// </summary>
    public static class AppleSignIn
    {
        /// <summary>Whether this build can present Apple's sheet at all.</summary>
        public static bool IsSupported
        {
#if UNITY_IOS && !UNITY_EDITOR
            get => true;
#else
            get => false;
#endif
        }

        /// <summary>What came back from the sheet.</summary>
        public enum Outcome { Succeeded, Cancelled, Failed, Unsupported }

        public readonly struct Result
        {
            public readonly Outcome Outcome;
            public readonly string IdToken;
            public readonly string RawNonce;

            /// <summary>
            /// Apple's authorization code, which Firebase requires alongside the identity
            /// token. Firebase's parameter for it is named <c>accessToken</c>, which is why it
            /// is easy to believe Apple has no use for it — and a credential without it is
            /// refused with the same message a malformed token gets.
            /// </summary>
            public readonly string AuthorizationCode;

            public readonly string Error;

            public Result(Outcome outcome, string idToken, string rawNonce,
                          string authorizationCode, string error)
            {
                Outcome = outcome;
                IdToken = idToken;
                RawNonce = rawNonce;
                AuthorizationCode = authorizationCode;
                Error = error;
            }

            public bool Ok => Outcome == Outcome.Succeeded;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int GlimmerAppleSignInState();
        [DllImport("__Internal")] static extern IntPtr GlimmerAppleSignInToken();
        [DllImport("__Internal")] static extern IntPtr GlimmerAppleSignInCode();
        [DllImport("__Internal")] static extern IntPtr GlimmerAppleSignInError();
        [DllImport("__Internal")] static extern void GlimmerAppleSignInStart(string hashedNonce);

        static string Read(IntPtr pointer)
            => pointer == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(pointer) ?? string.Empty);
#endif

        /// <summary>
        /// Presents Apple's sheet and waits for the player.
        ///
        /// <para>
        /// There is deliberately no timeout. The sheet is modal and owns the screen until it is
        /// answered or dismissed, and both of those report — a timeout could only fire while a
        /// player was still reading it, and would leave the native side about to write a result
        /// nobody is waiting for.
        /// </para>
        /// </summary>
        public static async Task<Result> RequestAsync(CancellationToken cancellation = default)
        {
#if UNITY_IOS && !UNITY_EDITOR
            string rawNonce = NewNonce();

            GlimmerAppleSignInStart(Sha256Hex(rawNonce));

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                    return new Result(Outcome.Cancelled, null, null, null, "cancelled");

                int state = GlimmerAppleSignInState();

                if (state == 2)
                {
                    string token = Read(GlimmerAppleSignInToken());
                    if (string.IsNullOrEmpty(token))
                        return new Result(Outcome.Failed, null, null, null,
                                          "Apple returned an empty token");

                    string code = Read(GlimmerAppleSignInCode());

                    Describe(token, rawNonce, code);
                    return new Result(Outcome.Succeeded, token, rawNonce, code, null);
                }

                if (state == 4)
                    return new Result(Outcome.Cancelled, null, null, null, "cancelled");

                if (state == 3)
                    return new Result(Outcome.Failed, null, null, null, Read(GlimmerAppleSignInError()));

                await Task.Yield();
            }
#else
            await Task.CompletedTask;
            return new Result(Outcome.Unsupported, null, null, null,
                              "Sign in with Apple's native sheet exists only on iOS devices");
#endif
        }


        /// <summary>
        /// Reports what Apple actually signed, so a rejection from Firebase can be read rather
        /// than guessed at.
        ///
        /// <para>
        /// Firebase answers a bad Apple credential with <c>"Invalid OAuth response from
        /// apple.com"</c> and nothing else — the same sentence whether the nonce disagrees, the
        /// audience is a different app, or the token has expired. Each of those has a different
        /// repair and none of them is visible from the managed side, so this decodes the JWT and
        /// says which one it is. It costs one log line on a path taken once or twice in the life
        /// of an account.
        /// </para>
        /// <para>
        /// <b>The nonce is compared but never printed.</b> It is single-use, so logging it is not
        /// a serious exposure, but a device log is copied into bug reports and chat windows, and
        /// a habit of printing credentials is the thing that eventually prints one that matters.
        /// The boolean answers the question on its own.
        /// </para>
        /// </summary>
        static void Describe(string token, string rawNonce, string code)
        {
            string[] parts = token.Split('.');
            if (parts.Length < 2)
            {
                Debug.LogWarning($"[AppleSignIn] token is not a JWT ({token.Length} chars)");
                return;
            }

            string payload = Base64Url(parts[1]);
            if (payload == null)
            {
                Debug.LogWarning("[AppleSignIn] token payload could not be decoded");
                return;
            }

            string aud = Claim(payload, "aud");
            string iss = Claim(payload, "iss");
            string nonce = Claim(payload, "nonce");
            string expected = Sha256Hex(rawNonce);

            Debug.Log($"[AppleSignIn] aud='{aud}' iss='{iss}' " +
                      $"nonceInToken={(string.IsNullOrEmpty(nonce) ? "ABSENT" : "present")} " +
                      $"nonceMatches={string.Equals(nonce, expected, StringComparison.Ordinal)} " +
                      $"tokenChars={token.Length} " +
                      $"authCode={(string.IsNullOrEmpty(code) ? "ABSENT" : code.Length + " chars")}");
        }

        /// <summary>Base64url with the padding JWTs omit, decoded to UTF-8.</summary>
        static string Base64Url(string value)
        {
            try
            {
                string padded = value.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            }
            catch { return null; }
        }

        /// <summary>
        /// One string claim out of a JSON payload, without pulling in a parser.
        ///
        /// Deliberately crude — this reads a diagnostic, never a decision, and every claim it
        /// looks for is a flat string. Anything that needs to be trusted is verified by Firebase
        /// against Apple's public keys, which is the only place that check belongs.
        /// </summary>
        static string Claim(string json, string name)
        {
            string key = "\"" + name + "\"";
            int at = json.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;

            int colon = json.IndexOf(':', at + key.Length);
            if (colon < 0) return null;

            int open = json.IndexOf('"', colon);
            if (open < 0) return null;

            int close = json.IndexOf('"', open + 1);
            return close < 0 ? null : json.Substring(open + 1, close - open - 1);
        }

        /// <summary>
        /// A fresh random nonce.
        ///
        /// Drawn from a cryptographic generator rather than <see cref="UnityEngine.Random"/>,
        /// because this value is the replay protection — a predictable one is the same as none.
        /// The character set is deliberately URL-safe so nothing downstream has to escape it.
        /// </summary>
        static string NewNonce(int length = 32)
        {
            const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-._";

            var bytes = new byte[length];
            using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);

            var builder = new StringBuilder(length);
            foreach (byte value in bytes) builder.Append(Alphabet[value % Alphabet.Length]);
            return builder.ToString();
        }

        /// <summary>Lowercase hex SHA-256, which is the form Apple expects for the nonce.</summary>
        static string Sha256Hex(string value)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) builder.Append(b.ToString("x2"));
                return builder.ToString();
            }
        }
    }
}
