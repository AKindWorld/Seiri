using Microsoft.UI.Dispatching;

namespace Seiri.Services;

/// <summary>
/// WinRT BitmapDecoder is not agile. Create + GetPixelData must stay on one
/// thread. Thread-pool hops during tagging+scroll are a native crash with no
/// managed exception.
/// </summary>
internal static class ImagingWorker
{
    private static readonly DispatcherQueueController Controller = DispatcherQueueController.CreateOnDedicatedThread();
    private static readonly DispatcherQueue Queue = Controller.DispatcherQueue;

    public static bool HasAccess => Queue.HasThreadAccess;

    public static Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        if (HasAccess)
        {
            return work();
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var posted = Queue.TryEnqueue(async () =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.TrySetCanceled(cancellationToken);
                return;
            }

            try
            {
                tcs.TrySetResult(await work().ConfigureAwait(true));
            }
            catch (OperationCanceledException)
            {
                tcs.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        if (!posted)
        {
            tcs.TrySetException(new InvalidOperationException("Imaging queue is unavailable."));
        }

        return tcs.Task;
    }
}
