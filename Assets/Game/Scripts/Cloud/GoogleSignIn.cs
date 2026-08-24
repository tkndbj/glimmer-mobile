using System;
using System.Collections.Generic;
using System.Net.Http;
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
    /// Google sign-in driven natively on iOS, producing the id token
    /// <see cref="LinkCredential"/> carries.
    ///
    /// <para>
    /// <b>Why this exists.</b> Firebase's generic IDP path works for Google on Android and
    /// fails on iOS — not loudly, the way <c>apple.com</c> does with a <c>fatalError</c>, but
    /// by never returning at all. The consent screen appears, an account is chosen, and the
    /// app is left holding a blank web view with no error on either side. The same weakness
    /// as Apple's, failing quietly rather than crashing, which took longer to recognise
    /// because there is nothing to read.
    /// </para>
    /// <para>
    /// <b>PKCE, and therefore no client secret in the app.</b> The flow is the authorisation
    /// code flow with a proof key: a random verifier is generated here, its SHA-256 goes to
    /// Google with the authorisation request, and the verifier itself is presented when the
    /// code is exchanged. That is what makes a public client safe — an attacker who
    /// intercepts the redirect holds a code they cannot spend. A secret shipped in a binary
    /// is not a secret, which is why iOS OAuth clients are not issued one.
    /// </para>
    /// <para>
    /// The redirect comes back on the reversed client id, which is already registered in
    /// <c>Info.plist</c> by the Firebase build step — the same scheme the web flow used, so
    /// nothing about the project's configuration changes.
    /// </para>
    /// </summary>
    public static class GoogleSignIn
    {
        const string Authorise = "https://accounts.google.com/o/oauth2/v2/auth";
        const string Exchange = "https://oauth2.googleapis.com/token";

        /// <summary>Whether this build can present the native sheet.</summary>
        public static bool IsSupported
        {
#if UNITY_IOS && !UNITY_EDITOR
            get => true;
#else
            get => false;
#endif
        }

        /// <summary>
        /// The iOS OAuth client id out of the bundled <c>GoogleService-Info.plist</c>, which is
        /// the one file that already names it and the one the redirect scheme is derived from.
        /// </summary>
        public static string ClientId
        {
#if UNITY_IOS && !UNITY_EDITOR
            get => Read(GlimmerGoogleClientId());
#else
            get => string.Empty;
#endif
        }

        public enum Outcome { Succeeded, Cancelled, Failed, Unsupported }

        public readonly struct Result
        {
            public readonly Outcome Outcome;
            public readonly string IdToken;
            public readonly string AccessToken;
            public readonly string Error;

            public Result(Outcome outcome, string idToken, string accessToken, string error)
            {
                Outcome = outcome;
                IdToken = idToken;
                AccessToken = accessToken;
                Error = error;
            }

            public bool Ok => Outcome == Outcome.Succeeded;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int GlimmerGoogleSignInState();
        [DllImport("__Internal")] static extern IntPtr GlimmerGoogleSignInUrl();
        [DllImport("__Internal")] static extern IntPtr GlimmerGoogleSignInError();
        [DllImport("__Internal")] static extern void GlimmerGoogleSignInStart(string authUrl, string scheme);
        [DllImport("__Internal")] static extern IntPtr GlimmerGoogleClientId();

        static string Read(IntPtr pointer)
            => pointer == IntPtr.Zero ? string.Empty : (Marshal.PtrToStringAnsi(pointer) ?? string.Empty);
#endif

        /// <summary>
        /// Presents Google's consent sheet and exchanges the result for an id token.
        ///
        /// <paramref name="clientId"/> is the <em>iOS</em> OAuth client, which is the
        /// <c>CLIENT_ID</c> in <c>GoogleService-Info.plist</c> — not the web client the
        /// hosted handler used.
        /// </summary>
        public static async Task<Result> RequestAsync(string clientId,
                                                      CancellationToken cancellation = default)
        {
#if UNITY_IOS && !UNITY_EDITOR
            if (string.IsNullOrEmpty(clientId))
                return new Result(Outcome.Failed, null, null, "no Google client id configured");

            string scheme = Reversed(clientId);
            string redirect = scheme + ":/oauth2redirect";

            string verifier = NewVerifier();
            string challenge = Challenge(verifier);

            string url = Authorise
                       + "?client_id=" + Uri.EscapeDataString(clientId)
                       + "&redirect_uri=" + Uri.EscapeDataString(redirect)
                       + "&response_type=code"
                       + "&scope=" + Uri.EscapeDataString("openid email profile")
                       + "&code_challenge=" + Uri.EscapeDataString(challenge)
                       + "&code_challenge_method=S256";

            GlimmerGoogleSignInStart(url, scheme);

            while (true)
            {
                if (cancellation.IsCancellationRequested)
                    return new Result(Outcome.Cancelled, null, null, "cancelled");

                int state = GlimmerGoogleSignInState();

                if (state == 4) return new Result(Outcome.Cancelled, null, null, "cancelled");
                if (state == 3)
                    return new Result(Outcome.Failed, null, null, Read(GlimmerGoogleSignInError()));

                if (state == 2)
                {
                    string callback = Read(GlimmerGoogleSignInUrl());
                    string code = Parameter(callback, "code");

                    if (string.IsNullOrEmpty(code))
                    {
                        string denied = Parameter(callback, "error");
                        return string.IsNullOrEmpty(denied)
                            ? new Result(Outcome.Failed, null, null, "the redirect carried no code")
                            : new Result(Outcome.Cancelled, null, null, denied);
                    }

                    return await ExchangeAsync(clientId, redirect, code, verifier);
                }

                await Task.Yield();
            }
#else
            await Task.CompletedTask;
            return new Result(Outcome.Unsupported, null, null,
                              "the native Google sheet exists only on iOS devices");
#endif
        }

        /// <summary>
        /// Trades the authorisation code for tokens, presenting the PKCE verifier in place of
        /// a client secret.
        /// </summary>
        static async Task<Result> ExchangeAsync(string clientId, string redirect,
                                                string code, string verifier)
        {
            try
            {
                using (var http = new HttpClient())
                {
                    var form = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "client_id", clientId },
                        { "code", code },
                        { "code_verifier", verifier },
                        { "grant_type", "authorization_code" },
                        { "redirect_uri", redirect },
                    });

                    var reply = await http.PostAsync(Exchange, form);
                    string body = await reply.Content.ReadAsStringAsync();

                    if (!reply.IsSuccessStatusCode)
                    {
                        // Google's body names the actual fault (invalid_grant, redirect_uri
                        // mismatch, an unenabled client). Carrying it through is the whole
                        // difference between a diagnosable failure and a blank screen.
                        string named = Json(body, "error_description") ?? Json(body, "error");
                        return new Result(Outcome.Failed, null, null,
                                          named ?? $"token exchange failed ({(int)reply.StatusCode})");
                    }

                    string idToken = Json(body, "id_token");
                    string accessToken = Json(body, "access_token");

                    return string.IsNullOrEmpty(idToken)
                        ? new Result(Outcome.Failed, null, null, "the exchange returned no id token")
                        : new Result(Outcome.Succeeded, idToken, accessToken, null);
                }
            }
            catch (Exception e)
            {
                return new Result(Outcome.Failed, null, null, e.Message);
            }
        }

        /// <summary>
        /// The reversed client id, which is the URL scheme Google redirects to and the one
        /// already registered in <c>Info.plist</c>.
        /// </summary>
        static string Reversed(string clientId)
        {
            int at = clientId.IndexOf(".apps.googleusercontent.com", StringComparison.Ordinal);
            string id = at < 0 ? clientId : clientId.Substring(0, at);
            return "com.googleusercontent.apps." + id;
        }

        /// <summary>A high-entropy PKCE verifier, from the character set RFC 7636 allows.</summary>
        static string NewVerifier(int length = 64)
        {
            const string Alphabet =
                "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-._~";

            var bytes = new byte[length];
            using (var generator = RandomNumberGenerator.Create()) generator.GetBytes(bytes);

            var builder = new StringBuilder(length);
            foreach (byte value in bytes) builder.Append(Alphabet[value % Alphabet.Length]);
            return builder.ToString();
        }

        /// <summary>base64url(SHA-256(verifier)), unpadded, which is what S256 means.</summary>
        static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(verifier));
                return Convert.ToBase64String(hash)
                              .TrimEnd('=')
                              .Replace('+', '-')
                              .Replace('/', '_');
            }
        }

        /// <summary>One query parameter out of the redirect URL.</summary>
        static string Parameter(string url, string name)
        {
            if (string.IsNullOrEmpty(url)) return null;

            int query = url.IndexOf('?');
            if (query < 0) return null;

            foreach (string pair in url.Substring(query + 1).Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0) continue;
                if (!string.Equals(pair.Substring(0, equals), name, StringComparison.Ordinal)) continue;
                return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
            return null;
        }

        /// <summary>
        /// One string field out of a flat JSON reply, without pulling in a parser.
        ///
        /// Deliberately crude, and safe because of where it is used: every field read here is
        /// a flat string from Google's token endpoint, and the one that matters — the id
        /// token — is verified by Firebase against Google's public keys, which is the only
        /// place that check belongs.
        /// </summary>
        static string Json(string body, string name)
        {
            if (string.IsNullOrEmpty(body)) return null;

            string key = "\"" + name + "\"";
            int at = body.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) return null;

            int colon = body.IndexOf(':', at + key.Length);
            if (colon < 0) return null;

            int open = body.IndexOf('"', colon);
            if (open < 0) return null;

            var builder = new StringBuilder();
            for (int i = open + 1; i < body.Length; i++)
            {
                if (body[i] == '\\' && i + 1 < body.Length) { builder.Append(body[++i]); continue; }
                if (body[i] == '"') break;
                builder.Append(body[i]);
            }
            return builder.ToString();
        }
    }
}
