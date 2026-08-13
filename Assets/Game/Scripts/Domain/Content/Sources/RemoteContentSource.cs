using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>
    /// Content served from a CDN.
    ///
    /// Nothing on the boot path ever reads from here — the refresher pulls into the
    /// cache in the background and the next launch picks it up. Keeping the network
    /// off the critical path is what stops a slow connection in a weak-network market
    /// from turning into a slow launch, and it means the CDN being down is invisible.
    ///
    /// Serve these files over HTTPS only, and keep them immutable per version so a
    /// long-lived CDN cache is a feature rather than a bug.
    /// </summary>
    public sealed class RemoteContentSource : IContentSource
    {
        readonly string _baseUrl;
        readonly int _timeoutSeconds;

        public RemoteContentSource(string baseUrl, int timeoutSeconds = 15)
        {
            _baseUrl = string.IsNullOrEmpty(baseUrl) ? string.Empty : baseUrl.TrimEnd('/');
            _timeoutSeconds = timeoutSeconds;
        }

        public string Name => "remote";

        public bool IsConfigured => !string.IsNullOrEmpty(_baseUrl);

        public Task<ContentFetchResult> FetchAsync(string relativePath, CancellationToken cancellation)
        {
            if (!IsConfigured)
                return Task.FromResult(ContentFetchResult.Fail("no remote content url configured", Name));

            return UnityWebRequestText.FetchAsync($"{_baseUrl}/{relativePath}", Name, cancellation, _timeoutSeconds);
        }
    }
}
