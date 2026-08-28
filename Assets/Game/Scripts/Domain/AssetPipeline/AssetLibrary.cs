using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// The game's one way to get hold of an asset.
    ///
    /// It caches by address and remembers which <em>scope</em> each address belongs to.
    /// Callers never say which — they ask for an address and the library knows, because
    /// the scope's asset set was registered when that part of the game was entered.
    /// That keeps every UI call site oblivious to memory management while still making
    /// it possible to drop a whole screen's art in one call.
    ///
    /// <para>
    /// Scopes are <b>named, and there can be any number of them</b>. That generality is
    /// the point rather than speculative flexibility: chapters were the first thing that
    /// needed loading and dropping as a unit, companions are the second, and a shop or a
    /// seasonal event will be the third. When the only transient scope was hardcoded as
    /// "the chapter", the second one had nowhere to go but a parallel copy of the same
    /// four fields and the same release logic — which is exactly how two caches drift
    /// until one of them leaks.
    /// </para>
    /// <para>
    /// An address already held globally stays global. A scope may ask for something the
    /// boot preload already warmed, and it must not become that scope's property — the
    /// scope would free it on exit and the chrome would vanish from a screen that never
    /// asked for anything.
    /// </para>
    /// </summary>
    public static class AssetLibrary
    {
        /// <summary>Art owned by the chapter currently being played.</summary>
        public const string ChapterScope = "chapter";

        /// <summary>
        /// The launch screen's picture. Its own scope rather than part of the global set,
        /// because it is a full-screen texture for the one screen in the game that is shown
        /// exactly once — see <see cref="Claim"/> for why it cannot use the ordinary path.
        /// </summary>
        public const string SplashScope = "splash";

        /// <summary>Companion portraits, held only while a screen is showing them.</summary>
        public const string CompanionScope = "companions";

        /// <summary>
        /// The grove's plots and decor, held only while the Grovement is open.
        ///
        /// The third caller of this mechanism, and the one it was predicted for. It is also
        /// the one whose set genuinely grows without bound: a chapter's art is fixed by the
        /// chapter, a roster's by the roster, but a shop gains pieces at every drop for the
        /// life of the game. Residents cost this scope nothing — they draw the board's own
        /// critter flipbooks, which are already global, and an address that is global stays
        /// global.
        /// </summary>
        /// <summary>
        /// The grove screen's art: the islands, the home ladder and whatever is placed.
        /// Bounded by the size of the player's grove, never by the size of the shop.
        /// </summary>
        public const string HomesteadScope = "grove";

        /// <summary>
        /// One kind of piece at a time — a shop tab, or the picker's list for one slot.
        ///
        /// Separate from <see cref="HomesteadScope"/> so browsing cannot cost what the grove
        /// costs: switching tabs replaces this and leaves the grove's own art alone. That is
        /// what keeps a catalog of four hundred pieces from being four hundred textures the
        /// moment somebody opens the shop.
        /// </summary>
        public const string HomesteadShopScope = "grove_shop";

        /// <summary>
        /// Somebody else's grove, while it is being visited.
        ///
        /// <para>
        /// A third scope rather than a reuse of <see cref="HomesteadScope"/>, and invariant 7b
        /// is the reason: a scope owns its addresses, so loading a stranger's grove into the
        /// player's would mean leaving their visit frees art the player's own grove screen is
        /// drawing — and coming back from a visit would land on a floor of white rectangles.
        /// It is bounded by what is standing in one grove, which is the same bound
        /// <see cref="HomesteadScope"/> has and for the same reason.
        /// </para>
        /// </summary>
        public const string GroveVisitScope = "grove_visit";

        /// <summary>
        /// The shop's tab row: eight little emblems in one atlas, held for as long as the shop
        /// is open rather than for as long as one shelf is.
        ///
        /// <para>
        /// <b>A fourth scope for one small atlas, and it is the flicker it was reported as.</b>
        /// The emblems used to ride in <see cref="HomesteadShopScope"/>, once per shelf — which
        /// is correct about what a tab <em>needs</em> and wrong about how long it needs it.
        /// <see cref="EnsureScopeAsync"/> releases before it loads, so every tap on a tab
        /// destroyed the atlas all eight tabs were drawing from and asked for the same file
        /// back: the whole row blinked for the frame or two the load took, every time anybody
        /// changed shelf.
        /// </para>
        /// <para>
        /// The alternative was making it global, and that is the wrong trade — global is "what
        /// the game needs before the menu appears", and this is one screen's furniture. A scope
        /// bounded by <em>the shop being open</em> is the honest bound, which is invariant 7b's
        /// whole question: not "is this small" but "what is it on screen for".
        /// </para>
        /// </summary>
        public const string HomesteadTabScope = "grove_tabs";
        sealed class Scope
        {
            public readonly HashSet<string> Addresses = new HashSet<string>(StringComparer.Ordinal);
            public readonly Dictionary<string, UnityEngine.Object> One = new Dictionary<string, UnityEngine.Object>();
            public readonly Dictionary<string, Sprite[]> Sets = new Dictionary<string, Sprite[]>();
        }

        static IAssetProvider _provider = new ResourcesAssetProvider();

        static readonly Dictionary<string, UnityEngine.Object> _globalOne = new Dictionary<string, UnityEngine.Object>();
        static readonly Dictionary<string, Sprite[]> _globalSets = new Dictionary<string, Sprite[]>();

        static readonly Dictionary<string, Scope> _scopes = new Dictionary<string, Scope>(StringComparer.Ordinal);

        /// <summary>Address to the scope holding it. Absent means global.</summary>
        static readonly Dictionary<string, Scope> _owner = new Dictionary<string, Scope>(StringComparer.Ordinal);

        public static IAssetProvider Provider => _provider;

        public static ChapterId LoadedChapter { get; private set; } = ChapterId.None;

        /// <summary>
        /// Swaps the backing provider. Call at boot, before anything loads — assets
        /// already cached from the old provider are dropped rather than migrated.
        /// </summary>
        public static void UseProvider(IAssetProvider provider)
        {
            if (provider == null || provider == _provider) return;

            ReleaseAllScopes();
            _globalOne.Clear();
            _globalSets.Clear();
            _provider = provider;

            Debug.Log($"[Assets] provider is now '{provider.Name}'");
        }

        // ------------------------------------------------------------- fetching
        public static Sprite Sprite(string address) => Get<Sprite>(address);

        public static AudioClip Clip(string address) => Get<AudioClip>(address);

        public static Font Font(string address) => Get<Font>(address);

        public static T Get<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address)) return null;

            var cache = OneCacheFor(address);
            if (cache.TryGetValue(address, out var cached)) return cached as T;

            var loaded = _provider.Load<T>(address);
            if (loaded == null) Debug.LogWarning($"[Assets] missing {address}");

            // Misses are cached too, so a bad address costs one failed load rather
            // than one per frame that asks for it.
            cache[address] = loaded;
            return loaded;
        }

        /// <summary>
        /// The asset if it is <em>already</em> loaded — never loading, never logging, never
        /// caching a miss.
        ///
        /// <para>
        /// <b>For art that is legitimately not there yet.</b> A screen inside a scoped feature
        /// is built in the frame it is asked for and paints before its scope has finished
        /// loading — that is the bargain <c>EnsureScopeAsync</c> makes, and it is why every
        /// such screen repaints on the callback. Asking through <see cref="Sprite"/> during
        /// that window is not a mistake and must not read like one: it printed ten
        /// "missing Art/Homestead/plot_*" warnings into the corner of the screen every time
        /// the Grovement was opened, which is how a console stops being worth reading.
        /// </para>
        /// <para>
        /// The genuine mistake — an address the content names and the build does not carry —
        /// is caught where it should be, by <c>AddressableAudit</c> and <c>Validate Art</c>
        /// walking the whole catalog at build time. A warning at runtime is a worse version of
        /// a check that already exists.
        /// </para>
        /// </summary>
        public static T Peek<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address)) return null;

            return OneCacheFor(address).TryGetValue(address, out var cached) ? cached as T : null;
        }

        // ---------------------------------------------------------------- atlases
        /// <summary>
        /// A named sprite out of an atlas that is <em>already</em> loaded, or null.
        ///
        /// <para>
        /// <b>Why this is not just <see cref="Peek{T}"/> plus a call.</b>
        /// <c>SpriteAtlas.GetSprite</c> builds a <em>new</em> <c>Sprite</c> object on every
        /// call and hands ownership to the caller — a documented allocation that a grid
        /// rebinding on every scroll frame would leak by the thousand. So each one is made once
        /// and kept, and every one an atlas produced is destroyed when that atlas is released.
        /// That bookkeeping has to live here, beside the release, because a screen cannot know
        /// when its atlas is dropped.
        /// </para>
        /// <para>
        /// Peeked rather than loaded, for <see cref="Peek{T}"/>'s reason: a browse screen paints
        /// in the frame it is built and repaints when its atlas lands, so a null here is the
        /// ordinary first answer rather than a fault worth logging.
        /// </para>
        /// </summary>
        public static Sprite AtlasSprite(string atlasAddress, string name)
        {
            if (string.IsNullOrEmpty(atlasAddress) || string.IsNullOrEmpty(name)) return null;

            if (_atlasSprites.TryGetValue(atlasAddress, out var made) &&
                made.TryGetValue(name, out var cached))
                return cached;

            var atlas = Peek<UnityEngine.U2D.SpriteAtlas>(atlasAddress);
            if (atlas == null) return null;

            var sprite = atlas.GetSprite(name);
            if (sprite == null) return null;

            // Unity appends "(Clone)" to whatever GetSprite hands back, which would then reach
            // anything reading sprite.name — including this project's own frame sorting.
            sprite.name = name;

            if (made == null) _atlasSprites[atlasAddress] = made = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            made[name] = sprite;
            return sprite;
        }

        /// <summary>True once an atlas is in hand, so a screen can tell "empty" from "not yet".</summary>
        public static bool IsAtlasLoaded(string atlasAddress)
            => Peek<UnityEngine.U2D.SpriteAtlas>(atlasAddress) != null;

        /// <summary>Sprites handed out by each atlas, so they can be destroyed with it.</summary>
        static readonly Dictionary<string, Dictionary<string, Sprite>> _atlasSprites =
            new Dictionary<string, Dictionary<string, Sprite>>(StringComparer.Ordinal);

        static void DropAtlasSprites(string address)
        {
            if (!_atlasSprites.TryGetValue(address, out var made)) return;

            foreach (var sprite in made.Values)
                if (sprite != null) UnityEngine.Object.Destroy(sprite);

            made.Clear();
            _atlasSprites.Remove(address);
        }

        /// <summary>Frames if they are already loaded. See <see cref="Peek{T}"/>.</summary>
        public static Sprite[] PeekFrames(string address)
        {
            if (string.IsNullOrEmpty(address)) return Array.Empty<Sprite>();

            return SetCacheFor(address).TryGetValue(address, out var cached)
                ? cached
                : Array.Empty<Sprite>();
        }

        /// <summary>Animation frames under a folder-like address, sorted by name.</summary>
        public static Sprite[] Frames(string address)
        {
            if (string.IsNullOrEmpty(address)) return Array.Empty<Sprite>();

            var cache = SetCacheFor(address);
            if (cache.TryGetValue(address, out var cached)) return cached;

            var loaded = _provider.LoadAll<Sprite>(address);
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning($"[Assets] missing frames {address}");
                loaded = Array.Empty<Sprite>();
            }

            cache[address] = loaded;
            return loaded;
        }

        // --------------------------------------------------------------- scopes
        /// <summary>
        /// Makes <paramref name="requests"/> the contents of a named scope, releasing
        /// whatever that scope held before. Loading the same set twice is not free —
        /// callers that can be re-entered should check <see cref="IsScopeLoaded"/>.
        /// </summary>
        public static async Task EnsureScopeAsync(string key,
                                                  IReadOnlyList<AssetRequest> requests,
                                                  IProgress<float> progress = null,
                                                  CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(key)) { progress?.Report(1f); return; }

            ReleaseScope(key);

            if (requests == null || requests.Count == 0) { progress?.Report(1f); return; }

            var scope = new Scope();
            _scopes[key] = scope;

            foreach (var request in requests)
            {
                // Already global: leave it there. Claiming it would mean freeing the
                // game's chrome the moment this scope closed. This is also what keeps
                // the worn companion's portrait alive on the hub after the profile —
                // which loaded the whole roster — is closed again.
                if (_globalOne.ContainsKey(request.Address) || _globalSets.ContainsKey(request.Address))
                    continue;

                // Owned by a different scope: leave it there too. Two scopes sharing an
                // address means whichever closed first would free it under the other.
                if (_owner.ContainsKey(request.Address)) continue;

                if (scope.Addresses.Add(request.Address)) _owner[request.Address] = scope;
            }

            await PreloadAsync(requests, progress, cancellation);
        }

        /// <summary>
        /// Adds to a scope without releasing what it already holds.
        ///
        /// <para>
        /// <b>Why this exists and <see cref="EnsureScopeAsync"/> is not enough.</b> A scope
        /// that is the answer to "what is on screen" grows while the screen is open: the
        /// grove holds the art of the pieces the player has placed, and placing one more must
        /// not tear down and rebuild the other twenty — that is a stall and a visible blink
        /// for the sake of one sprite. It also has to <em>claim</em> the new address, because
        /// the panel it was chosen from owns it right now and is about to close.
        /// </para>
        /// <para>
        /// A scope that does not exist yet is created, so a caller never has to check. The
        /// two ownership rules are the same ones <see cref="EnsureScopeAsync"/> applies, for
        /// the same reasons: global stays global, and another scope's address is left alone.
        /// </para>
        /// </summary>
        public static async Task AddToScopeAsync(string key,
                                                 IReadOnlyList<AssetRequest> requests,
                                                 CancellationToken cancellation = default)
        {
            if (string.IsNullOrEmpty(key) || requests == null || requests.Count == 0) return;

            if (!_scopes.TryGetValue(key, out var scope))
            {
                scope = new Scope();
                _scopes[key] = scope;
            }

            foreach (var request in requests)
            {
                if (_globalOne.ContainsKey(request.Address) || _globalSets.ContainsKey(request.Address))
                    continue;

                if (_owner.ContainsKey(request.Address)) continue;

                if (scope.Addresses.Add(request.Address)) _owner[request.Address] = scope;
            }

            await PreloadAsync(requests, null, cancellation);
        }

        /// <summary>Drops a scope's assets. Safe when it was never loaded.</summary>
        public static void ReleaseScope(string key)
        {
            if (string.IsNullOrEmpty(key) || !_scopes.TryGetValue(key, out var scope)) return;

            _scopes.Remove(key);
            foreach (var address in scope.Addresses)
            {
                // Before the owner mapping goes, because that is what tells a sprite which
                // cache it came out of. An atlas leaves behind every sprite it was asked for.
                DropAtlasSprites(address);
                _owner.Remove(address);
            }

            scope.One.Clear();
            scope.Sets.Clear();
            _provider.Release(scope.Addresses);
            scope.Addresses.Clear();
        }

        public static bool IsScopeLoaded(string key)
            => !string.IsNullOrEmpty(key) && _scopes.ContainsKey(key);

        /// <summary>
        /// Promotes an address out of whatever scope owns it and into the global set,
        /// keeping whatever is already cached.
        ///
        /// This exists for art that a screen loaded but the game goes on showing after
        /// that screen closes. The concrete case is choosing a companion: the picker
        /// loaded every portrait into its own scope, and the one just chosen is now
        /// wanted on the hub — without this, closing the picker would release the
        /// portrait the hub is about to draw, and the player's new companion would
        /// simply not appear.
        ///
        /// Cheap and safe to call for an address that is already global, or one nothing
        /// has loaded yet; the caller warms it afterwards either way.
        /// </summary>
        public static void Pin(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (!_owner.TryGetValue(address, out var scope)) return;

            if (scope.One.TryGetValue(address, out var one))
            {
                _globalOne[address] = one;
                scope.One.Remove(address);
            }

            if (scope.Sets.TryGetValue(address, out var set))
            {
                _globalSets[address] = set;
                scope.Sets.Remove(address);
            }

            // Dropped from the scope's address list as well, so releasing that scope no
            // longer tells the provider to free it.
            scope.Addresses.Remove(address);
            _owner.Remove(address);
        }

        /// <summary>
        /// Puts an address into a scope <em>before</em> anything has loaded it, so that a
        /// later synchronous <see cref="Get{T}"/> lands in that scope's cache rather than in
        /// the global one — and is therefore freed by <see cref="ReleaseScope"/>.
        ///
        /// <para>
        /// <b>This is the one case <see cref="EnsureScopeAsync"/> cannot serve.</b> A scope is
        /// normally claimed and warmed in the same call, which is right for everything that can
        /// wait a frame for its art. The launch screen cannot: it draws in the frame it is
        /// built, before the loader it is about to start has run at all, so its picture is
        /// fetched synchronously. Without this it would land in the global cache and stay
        /// resident for the life of the process — a full-screen texture for a screen that is
        /// shown once and never again.
        /// </para>
        /// <para>
        /// The two ownership rules are <see cref="EnsureScopeAsync"/>'s, for its reasons:
        /// something already global stays global, and an address another scope owns is left
        /// alone. Claiming an address that is <em>already loaded</em> globally would be worse
        /// than useless — the cached copy would be orphaned in a cache nothing reads again —
        /// so it is refused rather than honoured.
        /// </para>
        /// </summary>
        public static void Claim(string key, string address)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(address)) return;
            if (_globalOne.ContainsKey(address) || _globalSets.ContainsKey(address)) return;
            if (_owner.ContainsKey(address)) return;

            if (!_scopes.TryGetValue(key, out var scope))
            {
                scope = new Scope();
                _scopes[key] = scope;
            }

            if (scope.Addresses.Add(address)) _owner[address] = scope;
        }

        public static void ReleaseAllScopes()
        {
            var keys = new List<string>(_scopes.Keys);
            foreach (var key in keys) ReleaseScope(key);
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------- chapters
        /// <summary>
        /// Makes <paramref name="chapter"/> the resident one, loading its art and
        /// releasing the previous chapter's. Returns immediately when it is already
        /// resident, which is the common case of replaying a level.
        /// </summary>
        public static async Task EnsureChapterAsync(ChapterBody chapter,
                                                    IProgress<float> progress = null,
                                                    CancellationToken cancellation = default)
        {
            if (chapter == null) { progress?.Report(1f); return; }
            if (LoadedChapter == chapter.Id) { progress?.Report(1f); return; }

            LoadedChapter = chapter.Id;
            await EnsureScopeAsync(ChapterScope, AssetManifest.ChapterAssets(chapter), progress, cancellation);
        }

        /// <summary>Drops the resident chapter's art. Safe when none is loaded.</summary>
        public static void ReleaseChapter()
        {
            ReleaseScope(ChapterScope);
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------ preloading
        /// <summary>
        /// Warms a batch of assets, reporting 0..1 as it goes. Work is done in small
        /// batches so the frame drawing the progress bar still gets to run.
        /// </summary>
        public static async Task PreloadAsync(IReadOnlyList<AssetRequest> requests,
                                              IProgress<float> progress = null,
                                              CancellationToken cancellation = default,
                                              int batchSize = 8)
        {
            if (requests == null || requests.Count == 0) { progress?.Report(1f); return; }

            for (int i = 0; i < requests.Count; i += batchSize)
            {
                if (cancellation.IsCancellationRequested) return;

                int end = Mathf.Min(i + batchSize, requests.Count);
                var batch = new List<Task>(end - i);

                for (int k = i; k < end; k++)
                {
                    var request = requests[k];
                    if (AlreadyCached(request)) continue;
                    batch.Add(WarmAsync(request, cancellation));
                }

                if (batch.Count > 0) await Task.WhenAll(batch);
                progress?.Report(end / (float)requests.Count);
            }

            progress?.Report(1f);
        }

        static bool AlreadyCached(AssetRequest request)
            => request.Kind == AssetKind.SpriteSet
                ? SetCacheFor(request.Address).ContainsKey(request.Address)
                : OneCacheFor(request.Address).ContainsKey(request.Address);

        /// <summary>
        /// Loads one request into the right cache under its real type. Sprite sets go
        /// through the synchronous path because loading a whole folder has no
        /// streaming equivalent in either provider.
        /// </summary>
        static async Task WarmAsync(AssetRequest request, CancellationToken cancellation)
        {
            switch (request.Kind)
            {
                case AssetKind.SpriteSet:
                    Frames(request.Address);
                    return;

                case AssetKind.AudioClip:
                    await WarmOneAsync<AudioClip>(request.Address, cancellation);
                    return;

                case AssetKind.Font:
                    await WarmOneAsync<Font>(request.Address, cancellation);
                    return;

                case AssetKind.Atlas:
                    await WarmOneAsync<UnityEngine.U2D.SpriteAtlas>(request.Address, cancellation);
                    return;

                default:
                    await WarmOneAsync<Sprite>(request.Address, cancellation);
                    return;
            }
        }

        static async Task WarmOneAsync<T>(string address, CancellationToken cancellation)
            where T : UnityEngine.Object
        {
            var loaded = await _provider.LoadAsync<T>(address, cancellation);
            if (cancellation.IsCancellationRequested) return;

            if (loaded == null) Debug.LogWarning($"[Assets] missing {address}");
            OneCacheFor(address)[address] = loaded;
        }

        // ------------------------------------------------------------- internals
        static Dictionary<string, UnityEngine.Object> OneCacheFor(string address)
            => _owner.TryGetValue(address, out var scope) ? scope.One : _globalOne;

        static Dictionary<string, Sprite[]> SetCacheFor(string address)
            => _owner.TryGetValue(address, out var scope) ? scope.Sets : _globalSets;

        /// <summary>Diagnostics for the profiler and the dev overlay.</summary>
        public static string Describe()
        {
            int scoped = 0;
            foreach (var scope in _scopes.Values) scoped += scope.One.Count + scope.Sets.Count;

            return $"provider={_provider.Name} global={_globalOne.Count + _globalSets.Count} " +
                   $"scoped={scoped} in {_scopes.Count} scope(s) (chapter={LoadedChapter})";
        }
    }
}
