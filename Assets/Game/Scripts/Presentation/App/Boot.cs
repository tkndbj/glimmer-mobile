using GlimmerGrove.Ads;
using GlimmerGrove.Analytics;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Cloud;
using GlimmerGrove.Content;
using GlimmerGrove.Persistence;
using GlimmerGrove.Store;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GlimmerGrove
{
    /// <summary>
    /// Entry point. The game builds its entire scene graph in code, so any scene
    /// (even an empty one) boots straight into Glimmer Grove.
    /// </summary>
    public static class Boot
    {
        /// <summary>
        /// Taken from <see cref="ChapterMap.Width"/> rather than written again here.
        /// The map validator measures glade collisions against that number, so the two
        /// have to be the same one: a canvas widened without it would leave the gate
        /// passing layouts that overlap on a real screen.
        /// </summary>
        public const int RefWidth = (int)ChapterMap.Width;

        public const int RefHeight = 1920;

        /// <summary>
        /// The canvas's width in its own reference units on the display this build is running
        /// on: <see cref="RefWidth"/> on every phone, and wider on anything squarer.
        ///
        /// <para>
        /// <see cref="RefWidth"/> is the width the game is <em>designed</em> at and is a
        /// compile-time contract shared with the map validator; this is the width it is
        /// <em>drawn</em> at, and on a tablet the two differ. Anything converting between
        /// device pixels and canvas units, or assuming it knows how much canvas there is
        /// across, wants this one — see <see cref="Layout.CanvasFit"/> for why they part.
        /// </para>
        /// <para>
        /// A pure function of <see cref="UnityEngine.Screen"/>, deliberately, so it is correct
        /// in the frame the canvas is created in. <c>CanvasScaler</c> does not apply until
        /// <c>Canvas.willRenderCanvases</c>, which runs after every <c>Update</c> in the frame,
        /// so a caller that measured a rect instead would be a frame behind — which is the trap
        /// <c>SplashScreen.Fit</c> exists to document.
        /// </para>
        /// </summary>
        public static float CanvasWidth => Layout.CanvasFit.WidthFor(UnityEngine.Screen.width, UnityEngine.Screen.height);

        /// <summary>The canvas's height in reference units, by the same reckoning.</summary>
        public static float CanvasHeight => Layout.CanvasFit.HeightFor(UnityEngine.Screen.width, UnityEngine.Screen.height);

        /// <summary>
        /// Whether this display is squarer than a phone — a tablet, a foldable, a window in
        /// split view — and so one the canvas has been widened for.
        ///
        /// <para>
        /// Very little should ask. The widening is what makes a tablet need no second layout,
        /// and a screen reaching for this is a screen about to grow a case. There are two
        /// callers, and they are the two shapes that can legitimately need one. Lightfall's well
        /// is the only board in the game bound by <em>height</em>: a short display leaves it a
        /// sixth of the room a phone does, so its band and its tray give some back — see
        /// <c>FallBand</c>. The loadout shelf is bound by <em>width</em> in the other direction:
        /// it is a scrolling grid, so what a wider canvas buys it is columns rather than a
        /// bigger cell — and because this is a threshold rather than a ramp, every tablet draws
        /// the same shelf. See <c>LoadoutScreen.Columns</c>.
        /// </para>
        /// <para>
        /// <b>Neither is a second layout, which is the line.</b> Both ask this one question and
        /// answer it with one number that everything else is derived from; a screen that asked
        /// it in five places would be the tablet variant this rule exists to prevent.
        /// </para>
        /// </summary>
        public static bool ShortCanvas
            => Layout.CanvasFit.IsShort(UnityEngine.Screen.width, UnityEngine.Screen.height);

        static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Launch()
        {
            if (_started) return;
            _started = true;

            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Input.multiTouchEnabled = false;

            var root = new GameObject("Glimmer Grove");
            Object.DontDestroyOnLoad(root);

            EnsureCamera(root.transform);
            EnsureEventSystem(root.transform);

            var canvas = BuildCanvas(root.transform);

            // Made to apply itself before anything is built on it, and this is not tidiness.
            // `CanvasScaler` sets the canvas's scale factor from `Canvas.willRenderCanvases`,
            // which runs *after* every `Update` in the frame — and the splash screen is raised
            // further down this same method. Left alone, the first frame the player ever sees
            // is drawn at a scale factor of 1 and the second at the real one, so the whole
            // interface arrives oversized and settles: a launch that visibly lurches, on every
            // device whose screen is not exactly `RefWidth` wide and on no others. One forced
            // update costs nothing here and is the only moment in the app's life where the gap
            // between creating a canvas and drawing on it is a single statement.
            Canvas.ForceUpdateCanvases();

            // Progress first: the splash and every screen after it read from it, and
            // it is cheap enough to be worth having ready before anything draws.
            SaveService.Load();

            // Then settle any run the last launch never finished — a force-quit, a crash, a
            // flat battery. Immediately after the save loads, because the heart has to come
            // out of a wallet that exists, and before anything can start a new run, because
            // one marker can only describe one run. See RunGuard.
            RunGuard.Claim();

            ContentConfig.AppVersion = Release.AppVersion.Parse(Application.version);

            // Asset delivery is chosen once, here, before anything loads. Everything
            // downstream goes through AssetLibrary and never learns which it got.
#if GLIMMER_ADDRESSABLES
            AssetLibrary.UseProvider(new AddressablesAssetProvider());
#endif

            // Cloud save is chosen the same way, and ships inert until a backend is
            // installed. The game is fully playable through the null one.
#if GLIMMER_FIREBASE
            CloudSaveService.UseBackend(new FirebaseCloudSaveBackend());
#endif

            // A name, a companion, a purchase or a piece put down is worth a sync of its own
            // rather than waiting for the app to be backgrounded — which is the least reliable
            // moment there is to start a network call, since the process is being frozen as
            // it goes out. Hung on the events rather than called from the panels so a second
            // way to do any of them cannot forget it; the request is debounced and retried, so
            // this is cheap however often it fires. The list lives in SyncTriggers.
            SyncTriggers.Attach();

            // The public boards rebuild this account's card after a sync has put the grove on
            // the server — never before, because the server builds the card from the pushed
            // save. Wired once, here, for the reason above; a no-op in a build with no
            // backend, because everything behind it checks IsAvailable.
            Social.GroveBoard.Attach();
            Referral.ReferralLedger.Attach();

            // Rewarded ads, chosen the same way and inert by the same default. Two gates,
            // not one: the SDK has to be compiled in *and* a real app key has to exist.
            // Without the second, LevelPlay would start, fail, and leave the game showing
            // offers that can never fill — which is worse than showing none.
            // The consent platform and Apple's tracking prompt, installed before anything can
            // ask what the player agreed to. Installed, not resolved: asking is a network
            // round trip and possibly a native dialog, neither of which belongs before the
            // first scene has loaded — the splash starts that, through RewardedAds.StartAsync.
            Privacy.PrivacySetup.Install();

            // Measurement, installed immediately after the consent platform and before
            // anything that raises an event. It starts with collection off and turns it on
            // only once the gateway has answered, so the ordering here is the rule rather
            // than a convention: privacy first, then the thing that would measure somebody
            // who had not been asked. The sinks hold events until the native SDK resolves.
            AnalyticsSetup.Install();

#if GLIMMER_ADS
            if (AdConfig.IsConfigured)
            {
                RewardedAds.Install(new LevelPlayAdProvider(AdConfig.AppKey, AdConfig.AdUnits()));

                // Deliberately *not* started here, which is the one thing about this block
                // worth reading twice. Mediation may not be initialised until the player's
                // consent is known — an SDK that starts first has already decided what it may
                // collect and has already auctioned on that decision, and telling it
                // afterwards changes only the next request. RewardedAds.StartAsync owns that
                // ordering and the splash calls it.
            }
#endif

            // The shop, chosen the same way and inert by the same default. One gate here
            // rather than two, unlike the ads above: a store product costs nothing to ask
            // about and a store that has never heard of one simply leaves its card out, so
            // there is no equivalent of an ad unit that fails silently for ever. The
            // connection itself is started from the splash, not here — see SplashScreen —
            // because it is a network round trip and the boot path may not wait on one.
#if GLIMMER_IAP
            StoreService.UseBackend(new Iap.UnityIapBackend());
#endif

            // What a purchase bought, wherever the player happens to be standing.
            //
            // Hung on the event here rather than raised by the shop, for the reason the
            // rename sync is: a grant does not arrive while somebody is looking at the shop.
            // The payment sheet outlives the screen that opened it — on Android it outlives
            // the process — and a purchase interrupted by a crash is credited on the *next*
            // launch, from the splash, with the hub on screen. Both of those are the moments
            // a player most needs telling, and a shop screen listening for itself would miss
            // exactly them.
            //
            // Through the queue rather than straight to a panel, because two grants can land
            // seconds apart — a purchase interrupted by a crash is redeemed on the next launch
            // beside a fresh one — and two receipts must be shown one after another rather than
            // on top of each other or, worse, one instead of the other. See ReceiptQueue.
            //
            // The prompt is chained behind the last of them rather than raised beside it, and
            // the ordering is the whole reason this is the right moment to ask. The sale is
            // banked, the goods are on screen, and "keep what you just bought" is the easiest
            // sentence in the game to agree with — where the same words in front of the payment
            // sheet would be a dialog talking somebody out of the purchase it exists to protect.
            ReceiptQueue.WhenSettled = () => AccountPrompts.Offer(AccountPromptTrigger.Purchase);
            StoreService.Granted += ReceiptQueue.Show;

            // And the beat before that one: a transaction has landed, the money has moved, and
            // the server has not finished honouring it yet. Hung here rather than on the shop
            // for the very reason the receipt is — the sheet outlives the screen that opened it
            // — and scoped by StoreService to a checkout this process opened, so a re-delivery
            // arriving out of the store's own queue at launch says nothing. See
            // ShopArrivalOverlay, which owes the player a way out rather than a wait.
            StoreService.CheckoutLanded += ShopArrivalOverlay.Show;

            Audio.Boot(root.transform);
            Flow.Init(canvas);
            root.AddComponent<Pump>();

            // Content is loaded on the splash, where there is already a progress bar.
            Flow.Go<SplashScreen>(instant: true);
        }

        static void EnsureCamera(Transform parent)
        {
            if (Camera.main != null) return;
            var go = new GameObject("MainCamera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Pal.Slate;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.cullingMask = 0;
        }

        static void EnsureEventSystem(Transform parent)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(parent, false);
        }

        static Canvas BuildCanvas(Transform parent)
        {
            var go = new GameObject("UICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = false;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;      // portrait: width is the constant
            scaler.referencePixelsPerUnit = 100f;

            // ...and *which* width is the constant is a fact about the display, not a fact
            // about the game. See Layout.CanvasFit: a phone gets RefWidth, and anything
            // squarer than a phone gets a wider canvas so that the vertical stack every
            // screen here is made of has the room it was laid out against. The fitter is
            // what keeps that answer current — a tablet in split view is resized while the
            // app is running, and `Expand` would have covered that for free where a typed
            // reference width does not. SafeAreaFitter is the same bargain one layer down.
            go.AddComponent<CanvasFitter>().Bind(scaler);

            return canvas;
        }

        /// <summary>
        /// Routes the hardware back button, and makes sure progress reaches disk.
        ///
        /// Being backgrounded is the last moment a mobile app is reliably told
        /// anything — Android may kill the process afterwards without another
        /// callback — so the save is flushed there rather than only on quit.
        /// </summary>
        sealed class Pump : MonoBehaviour
        {
            void Update()
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Flow.HandleBack();

                // Connectivity is read here rather than inside the sync, because
                // Application is a Presentation type and the policy it feeds is meant to
                // be runnable in the test suite without one. Unscaled, since a paused game
                // is still a device that may have just found a signal.
                bool online = Application.internetReachability != NetworkReachability.NotReachable;

                CloudSaveService.Tick(Time.unscaledDeltaTime, online);

                // A purchase that has been paid for and not yet credited is retried on the
                // same clock. It has to be driven from here rather than from the shop: the
                // player who is owed gems is quite often not standing in the shop, and on
                // Google an unacknowledged purchase is refunded after three days.
                StoreService.Tick(Time.unscaledDeltaTime, online);

                // Art nobody holds any more is freed a few seconds later rather than on the
                // frame the last screen let go, so a player who leaves a screen and comes
                // straight back does not pay for the round trip — see AssetLibrary.GraceSeconds.
                // Nothing else drives that clock, so without this line the game would never free
                // a texture at all.
                AssetLibrary.Tick(Time.unscaledDeltaTime);

                // And whether this build is still one the deployment allows to be played. Last,
                // after the sync above has had its frame, because the wall must never be what a
                // player's last session is waiting on to reach the server. It is a poll rather
                // than an event for the reason UpdateGate spells out: a screen change destroys
                // every modal in the stack, so a wall that was merely *raised* would be
                // dismissed by the first piece of code that navigated.
                UpdateGate.Tick();
            }

            void OnApplicationPause(bool paused)
            {
                if (paused) Persist();
                else
                {
                    // Returning: pick up another device's work, and drop any backoff the
                    // last failure left. A player who reopens the game has quite often
                    // just done something about the connection.
                    CloudSaveService.ResetBackoff();
                    CloudSaveService.BeginSync();

                    // Same reasoning, and it matters more here: a device that has been
                    // asleep overnight is sitting at the retry backoff's ceiling, and the
                    // one thing worse than a purchase that has not landed is one that has
                    // not landed and is five minutes from being looked at again.
                    StoreService.Resumed();

                    // And re-ask what the deployment requires. Two opposite moments want this
                    // and both are ordinary: a device that launched with no signal has never
                    // been told anything, and a device that *has* been walled out is coming
                    // back from the store it was sent to. Rate-limited inside ReleaseWatch, so
                    // a player flicking between apps does not pay a document read per flick.
                    CloudSaveService.ReleaseResumed();

                    // And the shade is stale the moment they are here: every reminder this
                    // game sends says "come and look", and they are looking. The schedule
                    // itself is left exactly as it is — it is rewritten on the way out, which
                    // is the only moment the state it is built from is final.
                    Notifications.Notify.Resumed();
                }
            }

            /// <summary>
            /// Losing focus is not leaving, so this flushes and does nothing else.
            ///
            /// <para>
            /// <b>It used to call <see cref="Persist"/>, and that ran the whole of it twice on
            /// every single backgrounding.</b> Android raises <c>OnApplicationFocus(false)</c>
            /// and <c>OnApplicationPause(true)</c> together on the way out, so a rewarded video
            /// — which is a fullscreen Activity taking the foreground — armed the reminder
            /// schedule twice: two <c>CancelAllScheduledNotifications</c> and twice the plan's
            /// alarms handed to <c>AlarmManager</c>, 28 of them on a shipped schedule, on the
            /// main thread during <c>onPause</c>.
            /// </para>
            /// <para>
            /// <b>What that cost is not the work, it is the window.</b> <c>Arm</c> cancels the
            /// whole schedule and then writes it again, so between those two calls the device
            /// has <em>no</em> reminders pending — and nothing else in the game ever arms one
            /// (invariant 50d: this is the only caller). A process killed inside that gap loses
            /// the player's entire schedule silently, and it does not come back until they open
            /// the game again and background it again, which is precisely the player a reminder
            /// exists to reach. Backgrounding under a video ad is the likeliest moment in the
            /// whole app to be killed, so running the cancel-and-rewrite twice there doubled the
            /// exposure for no benefit whatever: <c>Rearm</c> is a pure function of the save and
            /// the clock (invariant 50c), so the second pass could only ever land the schedule
            /// the first one had already written.
            /// </para>
            /// <para>
            /// Splitting them is what invariant 50d already says — <b>armed when the app is
            /// backgrounded and at no other time</b> — and focus is the wrong question for it,
            /// because plenty of things take focus without the app going anywhere: the
            /// notification shade, a runtime permission dialog, the UMP consent form and the
            /// store's own payment sheet all raise this and never raise a pause. Re-arming three
            /// weeks of reminders because somebody glanced at their notifications is work that
            /// should never have been asked for.
            /// </para>
            /// <para>
            /// The flush stays, and it is the half worth keeping here: it is the cheapest
            /// insurance in the app, it is dirty-gated so it costs nothing when nothing changed,
            /// and a focus loss really can be the last callback before the process is killed.
            /// Which of the two callbacks lands first does not matter — the first one to write
            /// clears <c>_dirty</c> and the second returns immediately.
            /// </para>
            /// </summary>
            void OnApplicationFocus(bool focused)
            {
                if (!focused) SaveService.Flush();
            }

            void OnApplicationQuit() => SaveService.Flush();

            /// <summary>
            /// Disk first, then the network. The write is synchronous and certain; the
            /// sync may not survive the process being killed a moment later, and must
            /// never be what the local save is waiting on.
            /// </summary>
            static void Persist()
            {
                SaveService.Flush();
                CloudSaveService.BeginSync();

                // And what the phone will say while nobody is playing, written from the state
                // the player is leaving behind.
                //
                // Last, and after the flush rather than before it, for two reasons that pull
                // the same way. This is the only moment the state is final — a plan built
                // earlier would be a plan about a run that had not finished — and a reminder
                // nobody will read for ten hours must never be what the save is queued behind.
                // Notify.Rearm swallows its own exceptions for the same reason.
                //
                // Not on a timer and not on every event: three slots a day over seven days is
                // one cancel-and-write, and the plan is a pure function of the save, so doing
                // it again changes nothing. There is no state to reconcile because the feature
                // deliberately stores none.
                Notifications.Notify.Rearm();
            }
        }
    }
}
