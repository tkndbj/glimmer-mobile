using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlimmerGrove.Content.Sources;

namespace GlimmerGrove.Content
{
    /// <summary>The body of one chapter, plus anything that went wrong reading it.</summary>
    public sealed class ChapterLoadResult
    {
        public readonly ChapterBody Body;
        public readonly IReadOnlyList<string> Problems;

        public ChapterLoadResult(ChapterBody body, IReadOnlyList<string> problems)
        {
            Body = body;
            Problems = problems;
        }

        public bool Success => Body != null;
        public bool HasProblems => Problems.Count > 0;
    }

    /// <summary>
    /// Fetches one chapter body and maps it. Knows nothing about caching, ordering or
    /// what else exists.
    ///
    /// Kept apart from <see cref="ChapterResidency"/> on purpose: this is the I/O, that
    /// is the policy about how long a result is kept. Splitting them is what lets the
    /// Editor load every chapter eagerly and the game load one lazily using the very
    /// same code, and what lets the residency rule be changed — prefetch a neighbour,
    /// keep two, keep none — without touching a line that reads a file.
    /// </summary>
    public sealed class ChapterLoader
    {
        readonly IContentSource _source;

        public ChapterLoader(IContentSource source) => _source = source;

        public async Task<ChapterLoadResult> LoadAsync(ChapterId chapterId,
                                                       CancellationToken cancellation = default)
        {
            var problems = new List<string>();

            if (!chapterId.IsValid)
            {
                problems.Add("asked for a chapter with no id");
                return new ChapterLoadResult(null, problems);
            }

            string path = ContentPaths.Chapter(chapterId);
            var fetch = await _source.FetchAsync(path, cancellation);

            if (!fetch.Success)
            {
                problems.Add($"chapter '{chapterId}' is listed in the manifest but unreadable ({fetch.Error})");
                return new ChapterLoadResult(null, problems);
            }

            if (!ContentMapper.TryReadChapter(fetch.Text, problems, out var body))
                return new ChapterLoadResult(null, problems);

            // The file naming itself something else means the manifest and the body
            // disagree about identity, which would silently file levels under the
            // wrong chapter and pay them at the wrong rate.
            if (body.Id != chapterId)
            {
                problems.Add($"chapter file at {path} calls itself '{body.Id}'");
                return new ChapterLoadResult(null, problems);
            }

            return new ChapterLoadResult(body, problems);
        }
    }
}
