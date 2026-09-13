using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content;
using UnityEngine;

namespace GlimmerGrove.AssetPipeline
{
    /// <summary>
    /// The game's one way to get hold of an asset, and the only thing that decides when one is
    /// freed.
    ///
    /// <para>
    /// <b>One entry per address, and that is the whole of the bookkeeping.</b> This used to be
    /// five dictionaries — a global cache for single assets, a global cache for frame sets, a
    /// map of scope name to scope, a map of address to owning scope, and a map of atlas to the
    /// sprites it had handed out — with the invariant "an address appears in exactly one of
    /// these" maintained by hand at six call sites. It is not an invariant anybody can hold in
    /// their head, and the bug it produced was the worst kind: a load finishing after its scope
    /// had gone resolved its destination by address, found no owner, concluded "global", and
    /// wrote a <c>null</c> there for the life of the process. That art was then never drawn
    /// again, anywhere, with nothing in the log. There is now one place an address can be, so
    /// the question cannot be asked wrongly.
    /// </para>
    /// <para>
    /// <b>Lifetime is a reference count, not a name.</b> Whoever wants art takes an
    /// <see cref="AssetHold"/>; the art lives until the last holder lets go. See that type for
    /// what the named-scope version could not express and the three workarounds it grew.
    /// </para>
    /// <para>
    /// <b>A load knows where it belongs before it starts.</b> Every warm resolves its
    /// <c>Entry</c> up front and writes into that object, checking only whether it is still
    /// alive. Nothing is re-derived from global state after an <c>await</c>, which is the
    /// property the old code lacked.
    /// </para>
    /// </summary>
    public static class AssetLibrary
    {
        /// <summary>
        /// How long an address nobody holds is kept before it is really freed.
        ///
        /// <para>
        /// <b>Not a cache policy — a correctness one, which the count alone cannot supply.</b>
        /// Navigation is full of round trips that leave and come straight back: the grove to its
        /// shop and back, a level to its map, a visit to the board it was opened from. Freeing on
        /// the exact frame the last hold goes makes every one of those a full reload, and the old
        /// design's answer was a marker interface asking "does the screen replacing me draw this
        /// too?" — a question nobody can keep answering correctly as screens are added. A few
        /// seconds of grace answers it for every pair of screens at once, including ones that do
        /// not exist yet.
        /// </para>
        /// <para>
        /// Bounded on purpose: the window is short, it is measured on the unscaled clock, and
        /// <see cref="FlushIdle"/> empties it outright at the moments memory actually matters.
        /// </para>
        /// </summary>
        public const float GraceSeconds = 6f;

        /// <summary>
        /// Everything known about one address: what it is, what it holds, and who wants it.
        ///
        /// <b>The only place an address can be.</b> See the type's remarks.
        /// </summary>
        sealed class Entry
        {
            public readonly string Address;

            public AssetKind Kind;

            /// <summary>How many <see cref="AssetHold"/>s want this. Nought means idle.</summary>
            public int Holds;

            /// <summary>Never freed, whatever the count. Boot's art, and anything pinned.</summary>
            public bool Pinned;

            /// <summary>A load has completed — including one that found nothing.</summary>
            public bool Loaded;

            /// <summary>Still in the table. False the instant it is freed, which is what a load
            /// finishing late checks before it writes anything.</summary>
            public bool Alive = true;

            public UnityEngine.Object One;
            public Sprite[] Set;

            /// <summary>Sprites this atlas has handed out, so they can be destroyed with it.</summary>
            public Dictionary<string, Sprite> AtlasSprites;

            /// <summary>Frame runs read out of this atlas, so a grid cell is not rebuilding one
            /// on every rebind. See <see cref="AtlasRun"/>.</summary>
            public Dictionary<string, Sprite[]> AtlasRuns;

            /// <summary>The load in flight, so a second asker joins it rather than starting
            /// a second.</summary>
            public Task Loading;

            /// <summary>When the last hold let go, on the library's own clock.</summary>
            public float IdleSince;

            public Entry(string address, AssetKind kind)
            {
                Address = address;
                Kind = kind;
            }
        }

        static IAssetProvider _provider = new ResourcesAssetProvider();

        static readonly Dictionary<string, Entry> _entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>Addresses nobody holds, waiting out their grace. See <see cref="Tick"/>.</summary>
        static readonly List<Entry> _idle = new List<Entry>();

        static float _now;

        public static IAssetProvider Provider => _provider;

        public static ChapterId LoadedChapter { get; private set; } = ChapterId.None;

        /// <summary>
        /// Swaps the backing provider. Call at boot, before anything loads — assets already
        /// cached from the old provider are dropped rather than migrated.
        /// </summary>
        public static void UseProvider(IAssetProvider provider)
        {
            if (provider == null || provider == _provider) return;

            ReleaseAll();
            _provider = provider;

            Debug.Log($"[Assets] provider is now '{provider.Name}'");
        }

        /// <summary>Opens a hold. Dispose it when whatever took it goes away.</summary>
        public static AssetHold Hold(string name) => new AssetHold(name);

        // ------------------------------------------------------------- fetching
        public static Sprite Sprite(string address) => Get<Sprite>(address);

        public static AudioClip Clip(string address) => Get<AudioClip>(address);

        public static Font Font(string address) => Get<Font>(address);

        /// <summary>
        /// The asset, loading it synchronously if it is not already in hand.
        ///
        /// <para>
        /// An address fetched this way with nobody holding it is <em>global</em>: it is the boot
        /// preload's path and the fallback for chrome, and it stays for the session. A hold that
        /// wants to own something it fetches synchronously claims it first — see
        /// <see cref="AssetHold.Claim"/>.
        /// </para>
        /// </summary>
        public static T Get<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address)) return null;

            var entry = Resolve(address, KindOf<T>());
            if (entry.Loaded) return entry.One as T;

            var loaded = _provider.Load<T>(address);
            if (loaded == null) Debug.LogWarning($"[Assets] missing {address}");

            // Misses are cached too, so a bad address costs one failed load rather than one per
            // frame that asks for it. Only a load that genuinely ran may do this — see
            // WarmEntryAsync for the case that must not.
            entry.One = loaded;
            entry.Loaded = true;

            // Nobody asked for this under a hold, so it is the game's own and never freed.
            if (entry.Holds == 0) entry.Pinned = true;

            return loaded;
        }

        /// <summary>
        /// The asset if it is <em>already</em> loaded — never loading, never logging, never
        /// caching a miss.
        ///
        /// <para>
        /// <b>For art that is legitimately not there yet.</b> A screen is built in the frame it
        /// is asked for and paints before its hold has finished loading — that is the bargain,
        /// and it is why every such screen repaints on the callback. Asking through
        /// <see cref="Get{T}"/> during that window is not a mistake and must not read like one:
        /// it printed a screenful of warnings every time the Grovement was opened, which is how
        /// a console stops being worth reading.
        /// </para>
        /// </summary>
        public static T Peek<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(address)) return null;

            return _entries.TryGetValue(address, out var entry) ? entry.One as T : null;
        }

        /// <summary>Frames if they are already loaded. See <see cref="Peek{T}"/>.</summary>
        public static Sprite[] PeekFrames(string address)
        {
            if (string.IsNullOrEmpty(address)) return Array.Empty<Sprite>();

            return _entries.TryGetValue(address, out var entry) && entry.Set != null
                ? entry.Set
                : Array.Empty<Sprite>();
        }

        /// <summary>
        /// Animation frames under a folder-like address, sorted by name, loading them
        /// synchronously if they are not in hand.
        ///
        /// <b>Prefer warming them through a hold.</b> This is the last resort for a draw-time
        /// call site that genuinely cannot wait a frame.
        /// </summary>
        public static Sprite[] Frames(string address)
        {
            if (string.IsNullOrEmpty(address)) return Array.Empty<Sprite>();

            var entry = Resolve(address, AssetKind.SpriteSet);
            if (entry.Set != null) return entry.Set;

            var loaded = _provider.LoadAll<Sprite>(address);
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning($"[Assets] missing frames {address}");
                loaded = Array.Empty<Sprite>();
            }

            entry.Set = loaded;
            entry.Loaded = true;
            if (entry.Holds == 0) entry.Pinned = true;

            return loaded;
        }

        // ---------------------------------------------------------------- atlases
        /// <summary>
        /// A named sprite out of an atlas that is <em>already</em> loaded, or null.
        ///
        /// <para>
        /// <b>Why this is not just <see cref="Peek{T}"/> plus a call.</b>
        /// <c>SpriteAtlas.GetSprite</c> builds a <em>new</em> <c>Sprite</c> object on every call
        /// and hands ownership to the caller — a documented allocation that a grid rebinding on
        /// every scroll frame would leak by the thousand. So each one is made once and kept on
        /// the atlas's own entry, and destroyed with it.
        /// </para>
        /// </summary>
        public static Sprite AtlasSprite(string atlasAddress, string name)
        {
            if (string.IsNullOrEmpty(atlasAddress) || string.IsNullOrEmpty(name)) return null;

            if (!_entries.TryGetValue(atlasAddress, out var entry)) return null;

            return SpriteFrom(entry, name);
        }

        static Sprite SpriteFrom(Entry entry, string name)
        {
            if (entry.AtlasSprites != null && entry.AtlasSprites.TryGetValue(name, out var cached))
                return cached;

            if (!(entry.One is UnityEngine.U2D.SpriteAtlas atlas)) return null;

            var sprite = atlas.GetSprite(name);
            if (sprite == null) return null;

            // Unity appends "(Clone)" to whatever GetSprite hands back, which would then reach
            // anything reading sprite.name — including this project's own frame sorting.
            sprite.name = name;

            entry.AtlasSprites = entry.AtlasSprites
                                 ?? new Dictionary<string, Sprite>(StringComparer.Ordinal);
            entry.AtlasSprites[name] = sprite;
            return sprite;
        }

        /// <summary>True once an atlas is in hand, so a screen can tell "empty" from "not yet".</summary>
        public static bool IsAtlasLoaded(string atlasAddress)
            => Peek<UnityEngine.U2D.SpriteAtlas>(atlasAddress) != null;

        /// <summary>
        /// A run of numbered frames out of one atlas, built once and kept.
        ///
        /// <para>
        /// <b>This exists because the browse grid was allocating per rebind.</b> The caller used
        /// to walk the frames itself: a <c>List</c>, a <c>ToArray</c>, and a fresh name string
        /// per index — for an animated piece, a dozen allocations every time a cell scrolled
        /// into view, on the one screen built around recycling cells so that it would not.
        /// Counting by asking rather than being told is still right (a second number saying how
        /// long a loop is would be a number for a regenerated flipbook to put out of step with
        /// the atlas), so the walk is kept and the <em>answer</em> is cached against the atlas
        /// that produced it — and goes when that atlas does, which is the half a caller-side
        /// cache could not get right.
        /// </para>
        /// </summary>
        /// <param name="frameName">Names frame <c>i</c> of <paramref name="key"/>. Hold it in a
        /// static field: a method group converted inline allocates a delegate per call, which is
        /// the cost this is here to remove.</param>
        public static Sprite[] AtlasRun(string atlasAddress, string key,
                                        Func<string, int, string> frameName, int maxFrames)
        {
            if (string.IsNullOrEmpty(atlasAddress) || string.IsNullOrEmpty(key) || frameName == null)
                return Array.Empty<Sprite>();

            if (!_entries.TryGetValue(atlasAddress, out var entry)) return Array.Empty<Sprite>();

            if (entry.AtlasRuns != null && entry.AtlasRuns.TryGetValue(key, out var made)) return made;
            if (!(entry.One is UnityEngine.U2D.SpriteAtlas)) return Array.Empty<Sprite>();

            var found = new List<Sprite>(4);
            for (int i = 0; i < maxFrames; i++)
            {
                var sprite = SpriteFrom(entry, frameName(key, i));
                if (sprite == null) break;

                found.Add(sprite);
            }

            var run = found.Count == 0 ? Array.Empty<Sprite>() : found.ToArray();

            entry.AtlasRuns = entry.AtlasRuns ?? new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
            entry.AtlasRuns[key] = run;
            return run;
        }

        // ------------------------------------------------------------------ holds
        internal static void Acquire(string address, AssetKind kind)
        {
            var entry = Resolve(address, kind);

            entry.Holds++;
            if (entry.Holds == 1) _idle.Remove(entry);
        }

        internal static void Let(string address)
        {
            if (!_entries.TryGetValue(address, out var entry)) return;

            if (entry.Holds > 0) entry.Holds--;
            if (entry.Holds > 0 || entry.Pinned) return;

            entry.IdleSince = _now;
            if (!_idle.Contains(entry)) _idle.Add(entry);
        }

        /// <summary>
        /// Fills a hold, warming anything it does not already have.
        ///
        /// <para>
        /// The acquire happens <em>before</em> the warm and the release of what is no longer
        /// wanted happens <em>after</em> it, which is not fussiness: doing it the other way round
        /// would drop an address to nought holds and back again for a set that merely changed
        /// shape, and an entry that reaches nought starts its grace clock.
        /// </para>
        /// </summary>
        internal static async Task FillHoldAsync(AssetHold hold, IReadOnlyList<AssetRequest> requests,
                                                 IProgress<float> progress, CancellationToken cancellation,
                                                 bool replace)
        {
            if (hold == null) { progress?.Report(1f); return; }

            requests = requests ?? Array.Empty<AssetRequest>();

            var wanted = new HashSet<string>(StringComparer.Ordinal);

            foreach (var request in requests)
            {
                if (string.IsNullOrEmpty(request.Address)) continue;
                if (!wanted.Add(request.Address)) continue;

                hold.Take(request.Address, request.Kind);
            }

            if (replace) hold.Trim(wanted);

            await WarmAsync(requests, progress, cancellation);
        }

        // ------------------------------------------------------------ preloading
        /// <summary>
        /// How many addresses are in flight at once. Small enough that the frame drawing the
        /// progress bar still gets to run between batches, large enough that a hold of forty
        /// pieces is not forty round trips end to end.
        /// </summary>
        const int DefaultBatch = 8;

        /// <summary>
        /// Warms a batch of assets with nobody holding them — the boot preload's path, so
        /// everything it touches is global for the session.
        /// </summary>
        public static Task PreloadAsync(IReadOnlyList<AssetRequest> requests,
                                        IProgress<float> progress = null,
                                        CancellationToken cancellation = default,
                                        int batchSize = DefaultBatch)
        {
            if (requests != null)
                foreach (var request in requests)
                    if (!string.IsNullOrEmpty(request.Address))
                        Resolve(request.Address, request.Kind).Pinned = true;

            return WarmAsync(requests, progress, cancellation, batchSize);
        }

        /// <summary>
        /// Warms every request, reporting 0..1 as it goes, in batches so the frame drawing a
        /// readout still gets to run.
        ///
        /// <para>
        /// <b>The caller's token governs the wait, never the load.</b> A load belongs to an
        /// address rather than to whoever happened to ask for it first — two screens can want
        /// the same sprite — so handing one caller's cancellation to the provider would let the
        /// first to leave abort a fetch the second is waiting on, and the abort would come back
        /// as a null and be cached as a miss. Cancelling therefore stops this walking the list;
        /// what is already in flight finishes and is cached properly.
        /// </para>
        /// </summary>
        static async Task WarmAsync(IReadOnlyList<AssetRequest> requests, IProgress<float> progress,
                                    CancellationToken cancellation, int batchSize = DefaultBatch)
        {
            if (requests == null || requests.Count == 0) { progress?.Report(1f); return; }
            if (batchSize < 1) batchSize = DefaultBatch;

            for (int i = 0; i < requests.Count; i += batchSize)
            {
                if (cancellation.IsCancellationRequested) return;

                int end = Mathf.Min(i + batchSize, requests.Count);
                List<Task> batch = null;

                for (int k = i; k < end; k++)
                {
                    var task = WarmOneAsync(requests[k]);
                    if (task == null) continue;

                    batch = batch ?? new List<Task>(end - i);
                    batch.Add(task);
                }

                if (batch != null) await Task.WhenAll(batch);
                progress?.Report(end / (float)requests.Count);
            }

            progress?.Report(1f);
        }

        /// <summary>
        /// One address, or null when there is nothing to do.
        ///
        /// A load already in flight is <em>joined</em> rather than started again, which is the
        /// only correct answer when two holds want one address: starting a second would leave
        /// the provider holding two handles for one asset, and reading <c>Result</c> off an
        /// unfinished handle yields null.
        /// </summary>
        static Task WarmOneAsync(AssetRequest request)
        {
            if (string.IsNullOrEmpty(request.Address)) return null;

            var entry = Resolve(request.Address, request.Kind);

            if (request.Kind == AssetKind.SpriteSet ? entry.Set != null : entry.Loaded) return null;
            if (entry.Loading != null) return entry.Loading;

            return entry.Loading = WarmEntryAsync(entry);
        }

        /// <summary>
        /// Loads one entry and writes the answer into <em>that entry</em>.
        ///
        /// <para>
        /// <b>The entry is resolved before the await and never looked up again, which is the
        /// fix.</b> The old code re-derived its destination from a global map after awaiting,
        /// so a hold released mid-load resolved to "nobody owns this, therefore global" and wrote
        /// the <c>null</c> that a released Addressables handle hands back. That null then
        /// answered every later request for the address, for the life of the process: the art
        /// was silently never drawn again. Here a late load can only write into the object it
        /// was started for, and only while that object is still in the table.
        /// </para>
        /// </summary>
        static async Task WarmEntryAsync(Entry entry)
        {
            try
            {
                if (entry.Kind == AssetKind.SpriteSet)
                {
                    var set = await _provider.LoadAllAsync<Sprite>(entry.Address, CancellationToken.None);

                    if (!entry.Alive) return;

                    if (set == null || set.Length == 0)
                    {
                        Debug.LogWarning($"[Assets] missing frames {entry.Address}");
                        set = Array.Empty<Sprite>();
                    }

                    entry.Set = set;
                    entry.Loaded = true;
                    return;
                }

                UnityEngine.Object loaded;

                switch (entry.Kind)
                {
                    case AssetKind.AudioClip:
                        loaded = await _provider.LoadAsync<AudioClip>(entry.Address, CancellationToken.None);
                        break;

                    case AssetKind.Font:
                        loaded = await _provider.LoadAsync<Font>(entry.Address, CancellationToken.None);
                        break;

                    case AssetKind.Atlas:
                        loaded = await _provider.LoadAsync<UnityEngine.U2D.SpriteAtlas>(
                            entry.Address, CancellationToken.None);
                        break;

                    default:
                        loaded = await _provider.LoadAsync<Sprite>(entry.Address, CancellationToken.None);
                        break;
                }

                if (!entry.Alive) return;

                if (loaded == null) Debug.LogWarning($"[Assets] missing {entry.Address}");

                entry.One = loaded;
                entry.Loaded = true;
            }
            finally
            {
                entry.Loading = null;
            }
        }

        // --------------------------------------------------------------- freeing
        /// <summary>
        /// Ages the idle list and frees whatever has waited out its grace.
        ///
        /// <para>
        /// Handed the elapsed time rather than reading a clock, for <c>GroveBoard.Tick</c>'s
        /// reason: a device clock can jump, and a grace measured against one would either never
        /// expire or expire on every frame. Called from <c>Boot</c>.
        /// </para>
        /// </summary>
        public static void Tick(float deltaSeconds)
        {
            if (deltaSeconds > 0f) _now += deltaSeconds;
            if (_idle.Count == 0) return;

            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                var entry = _idle[i];

                if (entry.Holds > 0 || entry.Pinned || !entry.Alive)
                {
                    _idle.RemoveAt(i);
                    continue;
                }

                if (_now - entry.IdleSince < GraceSeconds) continue;

                _idle.RemoveAt(i);
                Free(entry);
            }
        }

        /// <summary>
        /// Frees everything idle right now, without waiting out the grace.
        ///
        /// For the moments memory genuinely matters — entering a chapter is the one that exists —
        /// where holding a previous screen's art for a few more seconds is the wrong trade.
        /// </summary>
        public static void FlushIdle()
        {
            for (int i = _idle.Count - 1; i >= 0; i--)
            {
                var entry = _idle[i];
                _idle.RemoveAt(i);

                if (entry.Holds == 0 && !entry.Pinned && entry.Alive) Free(entry);
            }
        }

        static void Free(Entry entry)
        {
            entry.Alive = false;
            _entries.Remove(entry.Address);

            if (entry.AtlasSprites != null)
            {
                foreach (var sprite in entry.AtlasSprites.Values)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);

                entry.AtlasSprites.Clear();
            }

            entry.AtlasRuns?.Clear();
            entry.One = null;
            entry.Set = null;

            // A fresh array rather than a shared buffer: freeing is rare, and a provider is
            // entitled to hold on to what it is handed.
            _provider.Release(new[] { entry.Address });
        }

        /// <summary>
        /// Promotes an address to global, so nothing frees it however many holds come and go.
        ///
        /// This exists for art that a screen loaded but the game goes on showing after that
        /// screen closes. The concrete case is choosing a companion: the picker loaded every
        /// portrait, and the one just chosen is now wanted on the hub.
        /// </summary>
        public static void Pin(string address)
        {
            if (string.IsNullOrEmpty(address)) return;
            if (!_entries.TryGetValue(address, out var entry)) return;

            entry.Pinned = true;
            _idle.Remove(entry);
        }

        /// <summary>Frees everything, held or not. For the provider swap and for tests.</summary>
        public static void ReleaseAll()
        {
            var all = new List<Entry>(_entries.Values);

            _entries.Clear();
            _idle.Clear();

            foreach (var entry in all) Free(entry);

            _entries.Clear();
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------- chapters
        static AssetHold _chapter;

        /// <summary>
        /// Makes <paramref name="chapter"/> the resident one, loading its art and letting the
        /// previous chapter's go. Returns immediately when it is already resident, which is the
        /// common case of replaying a level.
        /// </summary>
        public static async Task EnsureChapterAsync(ChapterBody chapter,
                                                    IProgress<float> progress = null,
                                                    CancellationToken cancellation = default)
        {
            if (chapter == null) { progress?.Report(1f); return; }
            if (LoadedChapter == chapter.Id) { progress?.Report(1f); return; }

            LoadedChapter = chapter.Id;

            _chapter = _chapter ?? Hold("chapter");
            await _chapter.LoadAsync(AssetManifest.ChapterAssets(chapter), progress, cancellation);

            // Entering a chapter is the moment this game is closest to its memory ceiling — a
            // board, its cast and its effects all at once — so the previous screen's art gives
            // up its grace here rather than lingering into the run.
            FlushIdle();
        }

        /// <summary>Lets the resident chapter's art go. Safe when none is loaded.</summary>
        public static void ReleaseChapter()
        {
            _chapter?.Dispose();
            _chapter = null;
            LoadedChapter = ChapterId.None;
        }

        // ------------------------------------------------------------- internals
        static Entry Resolve(string address, AssetKind kind)
        {
            if (_entries.TryGetValue(address, out var entry))
            {
                // Two requests for one address disagreeing about what lives there is a content
                // fault, and a silent one: the second kind would load the wrong type and hand
                // back null. The first answer stands, because something may already be drawing it.
                if (entry.Kind != kind && entry.Loaded)
                    Debug.LogWarning($"[Assets] {address} was loaded as {entry.Kind}, now asked for as {kind}");
                else if (entry.Kind != kind)
                    entry.Kind = kind;

                return entry;
            }

            entry = new Entry(address, kind);
            _entries[address] = entry;
            return entry;
        }

        static AssetKind KindOf<T>() where T : UnityEngine.Object
        {
            if (typeof(T) == typeof(AudioClip)) return AssetKind.AudioClip;
            if (typeof(T) == typeof(Font)) return AssetKind.Font;
            if (typeof(T) == typeof(UnityEngine.U2D.SpriteAtlas)) return AssetKind.Atlas;

            return AssetKind.Sprite;
        }

        /// <summary>Diagnostics for the profiler and the dev overlay.</summary>
        public static string Describe()
        {
            int held = 0, pinned = 0;

            foreach (var entry in _entries.Values)
            {
                if (entry.Pinned) pinned++;
                else if (entry.Holds > 0) held++;
            }

            return $"provider={_provider.Name} addresses={_entries.Count} " +
                   $"(global={pinned} held={held} idle={_idle.Count}) chapter={LoadedChapter}";
        }
    }
}
