using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>
    /// Asks each source in turn and takes the first answer.
    ///
    /// The boot order is cache then bundled, so downloaded content shadows what
    /// shipped in the build without either knowing about the other. Ordering is the
    /// whole policy — there is no merging, no partial override and no ambiguity
    /// about which copy of a chapter won.
    /// </summary>
    public sealed class LayeredContentSource : IContentSource
    {
        readonly IContentSource[] _layers;

        public LayeredContentSource(params IContentSource[] layers) => _layers = layers;

        public string Name => "layered";

        public IReadOnlyList<IContentSource> Layers => _layers;

        public async Task<ContentFetchResult> FetchAsync(string relativePath, CancellationToken cancellation)
        {
            var failures = new StringBuilder();

            foreach (var layer in _layers)
            {
                if (cancellation.IsCancellationRequested)
                    return ContentFetchResult.Fail("cancelled", Name);

                var result = await layer.FetchAsync(relativePath, cancellation);
                if (result.Success) return result;

                if (failures.Length > 0) failures.Append("; ");
                failures.Append(layer.Name).Append(": ").Append(result.Error);
            }

            return ContentFetchResult.Fail(failures.ToString(), Name);
        }
    }
}
