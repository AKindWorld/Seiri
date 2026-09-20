using System.Collections.Concurrent;

namespace Seiri.Services;

/// <summary>
/// DirectML / ORT native code is not safe on random thread-pool threads.
/// Load and Run always happen on this one background thread.
/// </summary>
internal static class OrtWorker
{
    private static readonly BlockingCollection<Action> Queue = new();

    static OrtWorker()
    {
        var thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "Seiri.ONNX",
            Priority = ThreadPriority.Normal
        };
        thread.Start();
    }

    public static Task<T> InvokeAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Queue.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    tcs.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            tcs.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    public static Task InvokeAsync(Action work, CancellationToken cancellationToken) =>
        InvokeAsync(() =>
        {
            work();
            return 0;
        }, cancellationToken);

    private static void Loop()
    {
        foreach (var work in Queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch
            {
            }
        }
    }
}
