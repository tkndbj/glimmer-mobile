using System.Collections.Generic;
using GlimmerGrove.AssetPipeline;
using GlimmerGrove.Content;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// The one place that knows how an asset on disk maps to an address, and which
    /// group that address belongs in.
    ///
    /// Everything else in the Addressables tooling — the importer hook, the repair
    /// sweep, the build-time audit — reads its answers from here. That matters because
    /// the three have to agree exactly: a hook that files an asset one way and an audit
    /// that expects another would report a project as sound while shipping it broken.
    ///
    /// Deliberately free of UnityEditor types so it stays a statement of the convention
    /// rather than a piece of machinery.
    /// </summary>
    public static class AddressableAddresses
    {
        /// <summary>Assets live under here, and their address is their path below it.</summary>
        public const string AssetRoot = "Assets/Game/";

        /// <summary>Folders whose contents the game loads. Anything else is not ours.</summary>
        public static readonly string[] ManagedFolders = { "Art/", "Audio/", "Fonts/" };

        public const string GlobalGroup = "Glimmer Global";
        public const string ChapterGroupPrefix = "Glimmer Chapter ";

        /// <summary>
        /// Companion portraits. Their own bundle rather than the global one because the
        /// whole roster is wanted on one screen and nowhere else — putting them in the
        /// global group would decode every companion at launch to show a picker most
        /// sessions never open, and would grow that cost with every content drop.
        /// </summary>
        public const string CompanionGroup = "Glimmer Companions";

        /// <summary>
        /// Art that belongs to no chapter in particular. Branding is the clear case: the
        /// launcher icon is consumed by the build pipeline and never loaded at runtime,
        /// so giving it an address would put a texture in a bundle nothing ever opens.
        /// </summary>
        public static readonly string[] ExcludedFolders = { "Branding/" };

        public static string ChapterGroup(ChapterId chapter) => ChapterGroupPrefix + chapter.Value;

        /// <summary>
        /// "Assets/Game/Art/Ui/btn_green.png" becomes "Art/Ui/btn_green".
        ///
        /// The address is the asset's old Resources path, which is what made the
        /// migration off Resources a change to the asset database rather than to a
        /// single line of game code.
        /// </summary>
        public static bool TryAddressFor(string assetPath, out string address)
        {
            address = null;
            if (string.IsNullOrEmpty(assetPath)) return false;

            string path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(AssetRoot, System.StringComparison.Ordinal)) return false;

            string relative = path.Substring(AssetRoot.Length);

            foreach (var excluded in ExcludedFolders)
                if (relative.StartsWith(excluded, System.StringComparison.Ordinal)) return false;

            bool managed = false;
            foreach (var folder in ManagedFolders)
                if (relative.StartsWith(folder, System.StringComparison.Ordinal)) { managed = true; break; }

            if (!managed) return false;

            int dot = relative.LastIndexOf('.');
            if (dot > 0) relative = relative.Substring(0, dot);

            address = relative;
            return true;
        }

        public static bool IsManaged(string assetPath) => TryAddressFor(assetPath, out _);

        /// <summary>
        /// Folder-shaped addresses the game loads with <c>LoadAll</c>. Addressables has
        /// no notion of a folder, so each becomes a label shared by its frames, and the
        /// label is exactly the address the code asks for.
        ///
        /// Read back from <see cref="AssetManifest"/> rather than listed here. A hand
        /// written copy would fall out of step the moment a critter variant was added —
        /// the constant would go up, the manifest would ask for the new folder, and the
        /// label nobody remembered to add would leave it unloadable.
        /// </summary>
        public static HashSet<string> FrameFolders(IEnumerable<ChapterBody> chapters)
        {
            var folders = new HashSet<string>();

            foreach (var request in AssetManifest.GlobalAssets())
                if (request.Kind == AssetKind.SpriteSet) folders.Add(request.Address);

            foreach (var request in AssetManifest.AllChapterAssets(chapters))
                if (request.Kind == AssetKind.SpriteSet) folders.Add(request.Address);

            return folders;
        }

        /// <summary>The frame folder an address belongs to, or null.</summary>
        public static string FrameLabelFor(string address, HashSet<string> frameFolders)
        {
            if (address == null) return null;

            foreach (var folder in frameFolders)
                if (address.StartsWith(folder + "/", System.StringComparison.Ordinal)) return folder;

            return null;
        }

        /// <summary>
        /// Which chapter owns each address, for assets that exactly one chapter uses.
        ///
        /// An address wanted by two chapters is left out, which puts it in the global
        /// group. That is the correct answer rather than a convenient one: filing a
        /// shared backdrop under whichever chapter happened to be processed last would
        /// make every other chapter's bundle depend on that one, and entering chapter
        /// seven would quietly download chapter one. Shared art belongs to nobody.
        /// </summary>
        public static Dictionary<string, ChapterId> ChapterOwnership(IEnumerable<ChapterBody> chapters)
        {
            var claims = new Dictionary<string, ChapterId>();
            var shared = new HashSet<string>();

            if (chapters != null)
            {
                foreach (var chapter in chapters)
                    foreach (var request in AssetManifest.ChapterAssets(chapter))
                    {
                        if (claims.TryGetValue(request.Address, out var owner))
                        {
                            if (owner != chapter.Id) shared.Add(request.Address);
                            continue;
                        }
                        claims[request.Address] = chapter.Id;
                    }
            }

            foreach (var address in shared) claims.Remove(address);
            return claims;
        }

        /// <summary>Addresses under here are companion portraits, whoever asks for them.</summary>
        public const string CompanionPrefix = "Art/Companions/";

        /// <summary>The group an address belongs in, given who owns what.</summary>
        public static string GroupFor(string address, Dictionary<string, ChapterId> ownership)
        {
            if (address != null && address.StartsWith(CompanionPrefix, System.StringComparison.Ordinal))
                return CompanionGroup;

            return ownership != null && ownership.TryGetValue(address, out var chapter)
                ? ChapterGroup(chapter)
                : GlobalGroup;
        }
    }
}
