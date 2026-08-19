namespace GlimmerGrove.AssetPipeline
{
    /// <summary>What kind of thing lives at an address.</summary>
    public enum AssetKind
    {
        Sprite = 0,

        /// <summary>A folder of numbered frames, loaded together and sorted by name.</summary>
        SpriteSet = 1,

        AudioClip = 2,
        Font = 3,

        /// <summary>
        /// A <c>SpriteAtlas</c>: one texture holding many named sprites, asked for by name
        /// through <see cref="AssetLibrary.AtlasSprite"/>.
        ///
        /// Its own kind rather than a sprite, because what loads is neither a Texture2D nor a
        /// Sprite and asking for either would hand back null — the same trap the kind field
        /// exists for. See <c>AssetManifest.BrowseAtlas</c> for why the grove browses through
        /// one.
        /// </summary>
        Atlas = 4,
    }

    /// <summary>
    /// One thing to load, and what it is.
    ///
    /// The kind is not decoration. A texture imported as a sprite has the Texture2D
    /// as its main asset and the Sprite hanging off it, so loading it untyped and
    /// caching the result would hand a Texture2D to everything that asked for a
    /// Sprite. Preloading has to know the type it is warming.
    /// </summary>
    public readonly struct AssetRequest
    {
        public readonly string Address;
        public readonly AssetKind Kind;

        public AssetRequest(string address, AssetKind kind)
        {
            Address = address;
            Kind = kind;
        }

        public static AssetRequest Sprite(string address) => new AssetRequest(address, AssetKind.Sprite);
        public static AssetRequest SpriteSet(string address) => new AssetRequest(address, AssetKind.SpriteSet);
        public static AssetRequest Clip(string address) => new AssetRequest(address, AssetKind.AudioClip);
        public static AssetRequest Font(string address) => new AssetRequest(address, AssetKind.Font);
        public static AssetRequest Atlas(string address) => new AssetRequest(address, AssetKind.Atlas);

        public override string ToString() => $"{Kind}:{Address}";
    }
}
