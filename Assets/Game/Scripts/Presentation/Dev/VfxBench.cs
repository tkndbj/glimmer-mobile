namespace GlimmerGrove.Dev
{
    /// <summary>
    /// Where the bought VFX pack lives and what its prefabs are addressed as.
    ///
    /// <para>
    /// <b>Compiled unconditionally, unlike everything else about the bench.</b>
    /// <see cref="VfxDemoScreen"/> and the switcher row that reaches it are both behind
    /// <c>GLIMMER_BENCH</c>, but the Editor tool that files these prefabs into Addressables has
    /// to be able to run when that define is <em>off</em> — that is precisely when it has work
    /// to do, because turning the bundle back off is what keeps two hundred megabytes of
    /// somebody else's particle art out of a store build. A constants class either side of the
    /// define would be two copies of the one thing that must never disagree: the string the
    /// group is built under and the string the screen asks for.
    /// </para>
    /// <para>
    /// It costs a shipped build a handful of literals and no assets, because an address is only
    /// a string until something loads it.
    /// </para>
    /// </summary>
    public static class VfxBench
    {
        /// <summary>
        /// The pack's prefab folders on disk, read by <c>VfxBenchGroup</c> in the Editor.
        ///
        /// A runtime file naming an <c>Assets/</c> path is unusual and deliberate: this is the
        /// one fact the Editor tool and the screen have to agree on, and the alternative is the
        /// Editor owning it and the screen guessing.
        /// </summary>
        public const string PackRoot = "Assets/GabrielAguiarProductions/UniqueProjectilesVol_5/Prefabs";

        /// <summary>
        /// The three kinds, which are folders on disk and labels in Addressables. Order is the
        /// order the bench's tab cycles through them.
        /// </summary>
        public static readonly string[] Kinds = { "Hits", "Muzzles", "Projectiles" };

        /// <summary>What each kind is called on the bench's own control.</summary>
        public static readonly string[] KindNames = { "HITS", "MUZZLES", "SHOTS" };

        /// <summary>
        /// Addresses begin here — outside <c>Art/</c> on purpose, so nothing about this pack can
        /// ever be picked up by <c>AddressableAutoRegister</c> and filed into the global group,
        /// which is the one mistake that would put it in every player's download.
        /// </summary>
        public const string AddressRoot = "Fx/Bench/";

        /// <summary>The Addressables group these prefabs live in, and nothing else does.</summary>
        public const string GroupName = "Glimmer VFX Bench";

        /// <summary>
        /// The label a whole kind is loaded by. Addressables has no folders, so a folder-shaped
        /// request is a label — the same convention <c>AssetLibrary.Frames</c> already uses for
        /// animation frames, and what makes <c>IAssetProvider.LoadAll</c> answer with the set.
        /// </summary>
        public static string LabelFor(int kind) => AddressRoot + Kinds[kind];

        /// <summary>One prefab's address: its label, then its file name.</summary>
        public static string AddressFor(int kind, string name) => LabelFor(kind) + "/" + name;

    }
}
