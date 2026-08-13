using System.Threading;
using GlimmerGrove.Content;
using GlimmerGrove.Content.Sources;

namespace GlimmerGrove.EditorTools
{
    /// <summary>
    /// Loads the bundled content synchronously for Editor tooling.
    ///
    /// In the Editor, StreamingAssets is an ordinary folder, so the bundled source
    /// completes without ever yielding — blocking on it here is safe and keeps the
    /// validation tools plain synchronous methods. It deliberately reads only what
    /// ships in the build, never the device cache, because a build must be judged on
    /// its own content rather than on whatever a previous run downloaded.
    /// </summary>
    public static class EditorContentLoader
    {
        public static ContentLoadResult Load()
        {
            var repository = new LevelRepository(new BundledContentSource());
            return repository.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
