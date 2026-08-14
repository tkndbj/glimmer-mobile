using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.AssetPipeline;
using NUnit.Framework;
using UnityEngine;

namespace GlimmerGrove.Tests
{
    /// <summary>
    /// Scoped asset lifetime — which screen owns what, and what survives a screen
    /// closing.
    ///
    /// These are memory-management bugs, and memory-management bugs do not announce
    /// themselves: releasing something still on screen shows up as a character that is
    /// silently missing, weeks later, on one navigation path nobody retested.
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
        public void DropScopes() => AssetLibrary.ReleaseAllScopes();

        static IReadOnlyList<AssetRequest> One(string address)
            => new[] { AssetRequest.Sprite(address) };

        [Test]
        public void AScopeReleasesExactlyWhatItOwns()
        {
            AssetLibrary.EnsureScopeAsync("a", One("Art/one")).GetAwaiter().GetResult();
            AssetLibrary.EnsureScopeAsync("b", One("Art/two")).GetAwaiter().GetResult();

            AssetLibrary.ReleaseScope("a");

            Assert.AreEqual(new[] { "Art/one" }, _spy.Released.ToArray());
            Assert.IsFalse(AssetLibrary.IsScopeLoaded("a"));
            Assert.IsTrue(AssetLibrary.IsScopeLoaded("b"), "one scope closing must not take another with it");
        }

        [Test]
        public void TwoScopesNeverShareAnAddress()
        {
            AssetLibrary.EnsureScopeAsync("a", One("Art/shared")).GetAwaiter().GetResult();
            AssetLibrary.EnsureScopeAsync("b", One("Art/shared")).GetAwaiter().GetResult();

            AssetLibrary.ReleaseScope("b");
            Assert.IsEmpty(_spy.Released, "the second scope must not have claimed what the first owns");

            AssetLibrary.ReleaseScope("a");
            Assert.AreEqual(new[] { "Art/shared" }, _spy.Released.ToArray());
        }

        [Test]
        public void APinnedAddressSurvivesItsScopeClosing()
        {
            // The companion case: the picker loads the whole roster, the player chooses
            // one, and the hub goes on drawing it after the picker is gone.
            AssetLibrary.EnsureScopeAsync("roster", One("Art/Companions/chosen")).GetAwaiter().GetResult();

            AssetLibrary.Pin("Art/Companions/chosen");
            AssetLibrary.ReleaseScope("roster");

            Assert.IsEmpty(_spy.Released, "the pinned portrait must not be freed under the hub");
        }

        [Test]
        public void PinningIsHarmlessForAddressesNobodyOwns()
        {
            Assert.DoesNotThrow(() => AssetLibrary.Pin("Art/never/loaded"));
            Assert.DoesNotThrow(() => AssetLibrary.Pin(null));
            Assert.DoesNotThrow(() => AssetLibrary.Pin(string.Empty));
        }

        [Test]
        public void GlobalArtIsNeverClaimedByAScope()
        {
            // Warmed the way the boot preload warms it: no scope open, so it is global.
            AssetLibrary.PreloadAsync(One("Art/Ui/button")).GetAwaiter().GetResult();

            AssetLibrary.EnsureScopeAsync("screen", One("Art/Ui/button")).GetAwaiter().GetResult();
            AssetLibrary.ReleaseScope("screen");

            Assert.IsEmpty(_spy.Released, "closing a screen must not free the game's chrome");
        }

        [Test]
        public void ReloadingAScopeReleasesThePreviousContents()
        {
            AssetLibrary.EnsureScopeAsync("chapter", One("Art/Bg/one")).GetAwaiter().GetResult();
            AssetLibrary.EnsureScopeAsync("chapter", One("Art/Bg/two")).GetAwaiter().GetResult();

            Assert.AreEqual(new[] { "Art/Bg/one" }, _spy.Released.ToArray(),
                            "entering a second chapter drops the first one's art");
        }
    }
}
