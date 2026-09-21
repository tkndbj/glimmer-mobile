using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Asset lifetime: who keeps what alive, and what survives a screen closing.
    ///
    /// <para>
    /// These are memory-management bugs, and memory-management bugs do not announce themselves:
    /// releasing something still on screen shows up as a character silently missing, weeks
    /// later, on one navigation path nobody retested. Every case here is one that has actually
    /// shipped or was one edit away from shipping.
    /// </para>
    /// </summary>
    public sealed class AssetScopeTests
    {
        /// <summary>Records what it was asked to load and release, and hands back nothing.</summary>
        sealed class SpyProvider : IAssetProvider
        {
            public readonly List<string> Released = new List<string>();
            public int Loads;

            public string Name => "spy";
            public bool IsAsynchronous => false;

            public T Load<T>(string address) where T : Object { Loads++; return null; }
            public T[] LoadAll<T>(string address) where T : Object { Loads++; return new T[0]; }

            public Task<T> LoadAsync<T>(string address, CancellationToken cancellation) where T : Object
            {
                Loads++;
                return Task.FromResult<T>(null);
            }

            public Task<T[]> LoadAllAsync<T>(string address, CancellationToken cancellation)
                where T : Object
            {
                Loads++;
                return Task.FromResult(new T[0]);
            }

            public void Release(IEnumerable<string> addresses)
            {
                if (addresses != null) Released.AddRange(addresses);
            }
        }

        /// <summary>
        /// Hands nothing back until it is told to, so a release can be staged in the middle of a
        /// load — which is one tap in the game and impossible to reach with a provider that
        /// answers immediately.
        /// </summary>
        sealed class GatedProvider : IAssetProvider
        {
            readonly List<TaskCompletionSource<bool>> _gates = new List<TaskCompletionSource<bool>>();

            public readonly List<string> Released = new List<string>();
            public int SyncLoads, SyncSetLoads, AsyncLoads, AsyncSetLoads;

            public string Name => "gated";
            public bool IsAsynchronous => true;

            public T Load<T>(string address) where T : Object { SyncLoads++; return null; }
            public T[] LoadAll<T>(string address) where T : Object { SyncSetLoads++; return new T[0]; }

            public async Task<T> LoadAsync<T>(string address, CancellationToken cancellation)
                where T : Object
            {
                AsyncLoads++;
                await Gate();
                return null;
            }

            public async Task<T[]> LoadAllAsync<T>(string address, CancellationToken cancellation)
                where T : Object
            {
                AsyncSetLoads++;
                await Gate();
                return null;
            }

            Task Gate()
            {
                var gate = new TaskCompletionSource<bool>();
                _gates.Add(gate);
                return gate.Task;
            }

            public void Finish()
            {
                var waiting = _gates.ToArray();
                _gates.Clear();
                foreach (var gate in waiting) gate.TrySetResult(true);
            }

            public void Release(IEnumerable<string> addresses)
            {
                if (addresses != null) Released.AddRange(addresses);
            }
        }

        SpyProvider _spy;

        [SetUp]
        public void UseSpy()
        {
            _spy = new SpyProvider();
            AssetLibrary.UseProvider(_spy);
        }

        [TearDown]
        public void DropEverything() => AssetLibrary.ReleaseAll();

        static IReadOnlyList<AssetRequest> One(string address)
            => new[] { AssetRequest.Sprite(address) };

        static IReadOnlyList<AssetRequest> Two(string a, string b)
            => new[] { AssetRequest.Sprite(a), AssetRequest.Sprite(b) };

        static void Load(AssetHold hold, IReadOnlyList<AssetRequest> requests)
            => hold.LoadAsync(requests).GetAwaiter().GetResult();

        /// <summary>Ages past the grace, so anything idle is actually freed.</summary>
        static void PastGrace() => AssetLibrary.Tick(AssetLibrary.GraceSeconds + 1f);

        // ------------------------------------------------------------------ owning
        [Test]
        public void AHoldFreesExactlyWhatItOwns()
        {
            var a = AssetLibrary.Hold("a");
            var b = AssetLibrary.Hold("b");

            Load(a, One("Art/one"));
            Load(b, One("Art/two"));

            a.Dispose();
            PastGrace();

            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray());
        }

        /// <summary>
        /// <b>The case a named scope could not express at all.</b> Two screens can legitimately
        /// want the same picture, and the old library handed it to whichever asked first and
        /// silently let the second share it — so the second lost its art the moment the first
        /// closed, which is a white rectangle appearing on a screen nobody had touched.
        /// </summary>
        [Test]
        public void TwoHoldsOnOneAddressBothCount()
        {
            var a = AssetLibrary.Hold("a");
            var b = AssetLibrary.Hold("b");

            Load(a, One("Art/shared"));
            Load(b, One("Art/shared"));

            a.Dispose();
            PastGrace();
            Assert.IsEmpty(_spy.Released, "one holder letting go must not free what another is drawing");

            b.Dispose();
            PastGrace();
            Assert.AreEqual(new[] { "Art/shared" }, _spy.Released.ToArray());
        }

        /// <summary>
        /// <b>The hand-over, which is the ordering hazard three screens used to work around.</b>
        /// Unity destroys the outgoing screen at the end of the frame, so the incoming one has
        /// already built and painted before the outgoing one lets go. The old answer was a marker
        /// interface read off <c>Flow.Current</c> — a question that had to be re-answered every
        /// time a screen was added, and that was wrong twice.
        /// </summary>
        [Test]
        public void ArtHandedFromOneScreenToTheNextIsNeverFreed()
        {
            var outgoing = AssetLibrary.Hold("outgoing");
            Load(outgoing, One("Art/Props/bench"));

            // The incoming screen builds and takes its hold first; the outgoing one's OnDestroy
            // runs afterwards, in the same frame.
            var incoming = AssetLibrary.Hold("incoming");
            Load(incoming, One("Art/Props/bench"));
            outgoing.Dispose();

            PastGrace();

            Assert.IsEmpty(_spy.Released, "the incoming screen's art was freed under it");
        }

        // ------------------------------------------------------------------- grace
        /// <summary>
        /// The other half of the hand-over, and the one that covers a screen change in the
        /// <em>other</em> order — leave, then come back. Freeing on the frame the last hold goes
        /// makes every round trip a full reload.
        /// </summary>
        [Test]
        public void AnAddressComingStraightBackIsNeverReloaded()
        {
            var first = AssetLibrary.Hold("grove");
            Load(first, One("Art/Props/bench"));
            first.Dispose();

            // A moment passes — less than the grace — and the player comes back.
            AssetLibrary.Tick(1f);

            var second = AssetLibrary.Hold("grove");
            Load(second, One("Art/Props/bench"));

            PastGrace();

            Assert.IsEmpty(_spy.Released, "art was freed between leaving a screen and returning");
            Assert.AreEqual(1, _spy.Loads, "the address was fetched twice for one round trip");
        }

        [Test]
        public void AnIdleAddressIsFreedOnceItsGraceIsUp()
        {
            var hold = AssetLibrary.Hold("screen");
            Load(hold, One("Art/one"));
            hold.Dispose();

            AssetLibrary.Tick(AssetLibrary.GraceSeconds - 1f);
            Assert.IsEmpty(_spy.Released, "freed before its grace was up");

            AssetLibrary.Tick(2f);
            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray());
        }

        [Test]
        public void FlushingFreesWhatIsIdleWithoutWaiting()
        {
            var hold = AssetLibrary.Hold("screen");
            Load(hold, One("Art/one"));
            hold.Dispose();

            AssetLibrary.FlushIdle();

            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray());
        }

        [Test]
        public void FlushingNeverTakesWhatSomebodyIsHolding()
        {
            var hold = AssetLibrary.Hold("screen");
            Load(hold, One("Art/one"));

            AssetLibrary.FlushIdle();

            Assert.IsEmpty(_spy.Released, "a flush freed art a live screen is holding");
        }

        // ------------------------------------------------------------------ global
        [Test]
        public void GlobalArtIsNeverFreedByAHold()
        {
            // Warmed the way the boot preload warms it: nobody holding, so it is the game's own.
            AssetLibrary.PreloadAsync(One("Art/Ui/button")).GetAwaiter().GetResult();

            var hold = AssetLibrary.Hold("screen");
            Load(hold, One("Art/Ui/button"));
            hold.Dispose();

            PastGrace();

            Assert.IsEmpty(_spy.Released, "closing a screen must not free the game's chrome");
        }

        /// <summary>
        /// The companion case: the picker loads the whole roster, the player chooses one, and the
        /// hub goes on drawing it after the picker is gone.
        /// </summary>
        [Test]
        public void APinnedAddressSurvivesEveryHolderLettingGo()
        {
            var hold = AssetLibrary.Hold("roster");
            Load(hold, One("Art/Companions/chosen"));

            AssetLibrary.Pin("Art/Companions/chosen");
            hold.Dispose();
            PastGrace();

            Assert.IsEmpty(_spy.Released, "the pinned portrait must not be freed under the hub");
        }

        [Test]
        public void PinningIsHarmlessForAddressesNobodyHolds()
        {
            Assert.DoesNotThrow(() => AssetLibrary.Pin("Art/never/loaded"));
            Assert.DoesNotThrow(() => AssetLibrary.Pin(null));
            Assert.DoesNotThrow(() => AssetLibrary.Pin(string.Empty));
        }

        /// <summary>
        /// The launch screen's case: a screen that draws in the frame it is built has to fetch
        /// synchronously, so the address must already belong to a hold by the time it is asked
        /// for. Claimed late, the picture is treated as the game's own and stays resident for
        /// the whole session — a full-screen texture for a screen shown once.
        /// </summary>
        [Test]
        public void AClaimedAddressIsFreedEvenThoughItWasFetchedSynchronously()
        {
            var hold = AssetLibrary.Hold("splash");

            hold.Claim("Art/Bg/splash_cover");
            AssetLibrary.Sprite("Art/Bg/splash_cover");

            hold.Dispose();
            PastGrace();

            Assert.AreEqual(new[] { "Art/Bg/splash_cover" }, _spy.Released.ToArray());
        }

        // ----------------------------------------------------------------- filling
        /// <summary>
        /// A hold is refilled rather than rebuilt as a screen's contents change — the grove when
        /// a piece is placed, the shop when the tab changes. What it still wants is not fetched
        /// again, and what it no longer wants is let go.
        /// </summary>
        [Test]
        public void RefillingAHoldKeepsWhatIsStillWantedAndDropsTheRest()
        {
            var hold = AssetLibrary.Hold("shelf");

            Load(hold, Two("Art/one", "Art/two"));
            Assert.AreEqual(2, _spy.Loads);

            Load(hold, Two("Art/two", "Art/three"));
            PastGrace();

            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray(),
                            "the address it no longer wants should have gone, and only that one");
            Assert.AreEqual(3, _spy.Loads, "an address it already held was fetched again");
        }

        [Test]
        public void AddingToAHoldKeepsEverythingItHad()
        {
            var hold = AssetLibrary.Hold("grove");

            Load(hold, One("Art/one"));
            hold.AddAsync(One("Art/two")).GetAwaiter().GetResult();

            PastGrace();
            Assert.IsEmpty(_spy.Released, "adding one piece let go of the rest of the grove");

            hold.Dispose();
            PastGrace();
            Assert.AreEqual(2, _spy.Released.Count);
        }

        // ----------------------------------------------------------------- loading
        /// <summary>
        /// <b>The poisoned cache.</b> A hold released while its art is arriving must leave no
        /// trace of the load behind. The destination used to be resolved by address, at
        /// completion time — so a released scope's address, owned by nothing, resolved to the
        /// <em>global</em> cache, and the value written there was <c>null</c>, because releasing
        /// the scope had already freed the handle the load was holding. <c>Peek</c> and
        /// <c>Get</c> both answer from that cache for the life of the process, so the art was
        /// never drawn again: invariant 7b's white rectangle, with nothing in the log. Opening a
        /// stranger's grove and tapping straight back out was enough, and the pieces it poisoned
        /// were ones the player's own grove draws.
        /// </summary>
        [Test]
        public void AHoldReleasedMidLoadDoesNotPoisonTheCache()
        {
            var gated = new GatedProvider();
            AssetLibrary.UseProvider(gated);

            var hold = AssetLibrary.Hold("visit");
            var loading = hold.LoadAsync(One("Art/Props/bench"));

            hold.Dispose();
            AssetLibrary.FlushIdle();

            gated.Finish();
            loading.GetAwaiter().GetResult();

            Assert.AreEqual(1, gated.AsyncLoads, "the hold's own load should have run once");

            // The load's answer must not be sitting in a cache. Asking again has to reach the
            // provider; answered from a cached miss it never would, for ever.
            AssetLibrary.Get<Sprite>("Art/Props/bench");
            Assert.AreEqual(1, gated.SyncLoads, "a released hold's load left a cached miss behind");
        }

        /// <summary>
        /// Frame folders are the bulk of this game's art — sixty-eight of the eighty-six grove
        /// pieces, every turret reel, every baked effect — and warming one used to go through the
        /// synchronous path, so a screen's whole art set landed inside one frame however
        /// carefully the batching was written. A busy screen opening on a full scope was
        /// that stall.
        /// </summary>
        [Test]
        public void WarmingAFrameFolderNeverBlocksTheFrame()
        {
            var gated = new GatedProvider();
            AssetLibrary.UseProvider(gated);

            var hold = AssetLibrary.Hold("grove");
            var loading = hold.LoadAsync(new[] { AssetRequest.SpriteSet("Art/Props/cottage") });

            Assert.IsFalse(loading.IsCompleted, "a frame folder was warmed synchronously");
            Assert.AreEqual(0, gated.SyncSetLoads, "the blocking LoadAll was used to preload");
            Assert.AreEqual(1, gated.AsyncSetLoads);

            gated.Finish();
            loading.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Two holds wanting one address fetch it once. Starting a second load would leave the
        /// provider holding two handles for one asset, and reading a result off an unfinished
        /// handle yields null — the same white rectangle by a slower route.
        /// </summary>
        [Test]
        public void TwoHoldsWantingOneAddressFetchItOnce()
        {
            var gated = new GatedProvider();
            AssetLibrary.UseProvider(gated);

            var a = AssetLibrary.Hold("a");
            var b = AssetLibrary.Hold("b");

            var first = a.LoadAsync(One("Art/shared"));
            var second = b.LoadAsync(One("Art/shared"));

            Assert.AreEqual(1, gated.AsyncLoads, "the second holder started a second fetch");

            gated.Finish();
            first.GetAwaiter().GetResult();
            second.GetAwaiter().GetResult();

            Assert.IsTrue(a.IsLoaded && b.IsLoaded, "both holders should be told when it lands");
        }

        /// <summary>
        /// "Is it loaded" must mean loaded, not started. The old <c>IsScopeLoaded</c> went true
        /// the instant a load began, so a second caller asked it, was told the art was ready, and
        /// painted a screenful of blanks.
        /// </summary>
        [Test]
        public void AHoldIsNotLoadedUntilItIsLoaded()
        {
            var gated = new GatedProvider();
            AssetLibrary.UseProvider(gated);

            var hold = AssetLibrary.Hold("shelf");
            var loading = hold.LoadAsync(One("Art/one"));

            Assert.IsFalse(hold.IsLoaded, "a hold reported itself loaded while it was still loading");

            gated.Finish();
            loading.GetAwaiter().GetResult();

            Assert.IsTrue(hold.IsLoaded);
        }

        [Test]
        public void DisposingTwiceIsHarmless()
        {
            var hold = AssetLibrary.Hold("screen");
            Load(hold, One("Art/one"));

            hold.Dispose();
            Assert.DoesNotThrow(() => hold.Dispose());

            PastGrace();
            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray(),
                            "a second dispose released the address twice");
        }
    }
}
