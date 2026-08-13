using System.Threading;
using System.Threading.Tasks;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>The outcome of asking one source for one file.</summary>
    public readonly struct ContentFetchResult
    {
        public readonly bool Success;
        public readonly string Text;
        public readonly string Error;

        /// <summary>Which source answered, for diagnostics and analytics.</summary>
        public readonly string SourceName;

        ContentFetchResult(bool success, string text, string error, string sourceName)
        {
            Success = success;
            Text = text;
            Error = error;
            SourceName = sourceName;
        }

        public static ContentFetchResult Ok(string text, string sourceName)
            => new ContentFetchResult(true, text, null, sourceName);

        public static ContentFetchResult Fail(string error, string sourceName)
            => new ContentFetchResult(false, null, error, sourceName);
    }

    /// <summary>
    /// Somewhere content can be read from.
    ///
    /// Deliberately free of Unity types so the loading logic above it can be tested
    /// without an Editor, and so a new delivery mechanism — a CDN, a publisher SDK,
    /// a test double — only has to satisfy this one method.
    /// </summary>
    public interface IContentSource
    {
        string Name { get; }

        Task<ContentFetchResult> FetchAsync(string relativePath, CancellationToken cancellation);
    }

    /// <summary>A source that can also be written to, i.e. the on-device cache.</summary>
    public interface IWritableContentSource : IContentSource
    {
        Task<bool> WriteAsync(string relativePath, string text, CancellationToken cancellation);

        bool Has(string relativePath);
    }
}
