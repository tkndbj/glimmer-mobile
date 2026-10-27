using System;
using System.Threading;
using UnityEngine;

namespace GlimmerGrove
{
    /// <summary>
    /// The store's own rating box, asked for in the game (invariant 68): Apple's
    /// <c>SKStoreReviewController</c> through Unity's <see cref="UnityEngine.iOS.Device.RequestStoreReview"/>,
    /// and Google Play's In-App Review API (<c>com.google.android.play:review</c>, declared in
    /// <c>Assets/Game/Editor/StoreReviewDependencies.xml</c>).
    ///
    /// <para>
    /// <b>Fire and forget, by the stores' design.</b> Neither platform says whether the box was drawn or
    /// whether the player rated, and both may decline to draw it (Apple at most three times a year, and
    /// never to someone who rated; Google on its own quota, and never to someone who reviewed). So this
    /// reports only whether the request was <em>made</em>; nothing may branch on what the player did.
    /// </para>
    /// <para>
    /// <b>The store's box, never one of ours before it</b>: Apple requires its API for a rating request
    /// and refuses a custom one, so there is no "Enjoying Gemfire?" panel. Off a phone (the Editor, a
    /// desktop build) the request is logged and nothing is drawn.
    /// </para>
    /// </summary>
    public static class StoreReview
    {
        /// <summary>Whether this platform has a store box to ask for.</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_EDITOR
                return false;
#elif UNITY_IOS || UNITY_ANDROID
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Asks the store for its rating box. Answers whether the request was made; never throws. Call on
        /// the main thread.
        /// </summary>
        public static bool Request()
        {
            try
            {
#if UNITY_EDITOR
                Debug.Log("[StoreReview] the store's rating box would be asked for here (not drawn in the Editor)");
                return false;
#elif UNITY_IOS
                return UnityEngine.iOS.Device.RequestStoreReview();
#elif UNITY_ANDROID
                return PlayReview.Request();
#else
                return false;
#endif
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// Google Play In-App Review: ask for a <c>ReviewInfo</c>, then launch the flow with it. Two Play
        /// <c>Task</c>s, each answered on a Java thread, so the second step is posted back to Unity's main
        /// thread before it touches the activity. A failure at either step is logged and ends quietly.
        /// </summary>
        static class PlayReview
        {
            public static bool Request()
            {
                var main = SynchronizationContext.Current;

                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var factory = new AndroidJavaClass("com.google.android.play.core.review.ReviewManagerFactory");
                var manager = factory.CallStatic<AndroidJavaObject>("create", activity);

                using var asked = manager.Call<AndroidJavaObject>("requestReviewFlow");
                asked.Call<AndroidJavaObject>("addOnCompleteListener", new Completed(task =>
                {
                    bool ok = task.Call<bool>("isSuccessful");
                    var info = ok ? task.Call<AndroidJavaObject>("getResult") : null;
                    void Launch()
                    {
                        try
                        {
                            if (info == null)
                            {
                                Debug.LogWarning("[StoreReview] Play declined to prepare the review flow");
                                return;
                            }
                            using var flow = manager.Call<AndroidJavaObject>("launchReviewFlow", activity, info);
                            flow.Call<AndroidJavaObject>("addOnCompleteListener", new Completed(_ => { }));
                        }
                        catch (Exception e)
                        {
                            Debug.LogException(e);
                        }
                    }

                    if (main != null) main.Post(_ => Launch(), null);
                    else Launch();
                }));
                return true;
            }

            /// <summary><c>com.google.android.gms.tasks.OnCompleteListener</c>, which Play Review 2.x's tasks take.</summary>
            sealed class Completed : AndroidJavaProxy
            {
                readonly Action<AndroidJavaObject> _done;

                public Completed(Action<AndroidJavaObject> done) : base("com.google.android.gms.tasks.OnCompleteListener")
                    => _done = done;

                // The Java interface's one method; the name is Java's, not ours.
                public void onComplete(AndroidJavaObject task)
                {
                    try { _done(task); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
        }
#endif
    }
}
