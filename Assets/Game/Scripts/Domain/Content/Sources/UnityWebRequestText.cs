using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GlimmerGrove.Content.Sources
{
    /// <summary>
    /// Bridges one UnityWebRequest to one Task.
    ///
    /// Kept in its own file because it is the single place the rest of the content
    /// system touches Unity's networking. Everything above it composes plain Tasks,
    /// which is what lets the loading logic be exercised without a player loop.
    /// </summary>
    internal static class UnityWebRequestText
    {
        public static Task<ContentFetchResult> FetchAsync(string url, string sourceName,
                                                          CancellationToken cancellation,
                                                          int timeoutSeconds = 15)
        {
            var tcs = new TaskCompletionSource<ContentFetchResult>();

            UnityWebRequest request;
            try
            {
                request = UnityWebRequest.Get(url);
                request.timeout = timeoutSeconds;
            }
            catch (Exception e)
            {
                return Task.FromResult(ContentFetchResult.Fail(e.Message, sourceName));
            }

            CancellationTokenRegistration registration = default;

            void Finish(ContentFetchResult result)
            {
                registration.Dispose();
                request.Dispose();
                tcs.TrySetResult(result);
            }

            try
            {
                var operation = request.SendWebRequest();

                if (cancellation.CanBeCanceled)
                    registration = cancellation.Register(() =>
                    {
                        if (!tcs.Task.IsCompleted) request.Abort();
                    });

                operation.completed += _ =>
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        Finish(ContentFetchResult.Fail("cancelled", sourceName));
                        return;
                    }

                    Finish(request.result == UnityWebRequest.Result.Success
                        ? ContentFetchResult.Ok(request.downloadHandler.text, sourceName)
                        : ContentFetchResult.Fail($"{request.result}: {request.error}", sourceName));
                };
            }
            catch (Exception e)
            {
                Finish(ContentFetchResult.Fail(e.Message, sourceName));
            }

            return tcs.Task;
        }
    }
}
