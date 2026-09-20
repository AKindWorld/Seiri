using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Infrastructure;

namespace Seiri.Services;

public sealed class ThumbnailQueue
{
    private readonly ThumbnailGenerator _generator;
    private readonly LibraryService _libraries;
    private readonly SemaphoreSlim _gate = new(4, 4);
    private int _pauseHeavy;

    public ThumbnailQueue(ThumbnailGenerator generator, LibraryService libraries)
    {
        _generator = generator;
        _libraries = libraries;
    }

    public void PauseHeavyWork() => Interlocked.Increment(ref _pauseHeavy);

    public void ResumeHeavyWork() => Interlocked.Decrement(ref _pauseHeavy);

    public async Task EnsureAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        var dest = item.ThumbFullPath;
        var thumbReady = !string.IsNullOrEmpty(dest) && File.Exists(dest) && new FileInfo(dest).Length > 0;
        if (thumbReady && !string.IsNullOrEmpty(item.ColorBucket))
        {
            return;
        }

        if (Volatile.Read(ref _pauseHeavy) > 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            dest = item.ThumbFullPath;
            thumbReady = !string.IsNullOrEmpty(dest) && File.Exists(dest) && new FileInfo(dest).Length > 0;
            if (thumbReady && !string.IsNullOrEmpty(item.ColorBucket))
            {
                return;
            }

            if (item.Kind == MediaKind.Image && item.Width is null)
            {
                var size = ImageDimensions.TryRead(item.FullPath);
                if (size is { } dims)
                {
                    await _libraries.SetDimensionsAsync(item, dims.Width, dims.Height, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            string? bucket;
            if (thumbReady && string.IsNullOrEmpty(item.ColorBucket))
            {
                bucket = await ThumbnailGenerator.ColorFromFileAsync(dest!).ConfigureAwait(false);
            }
            else
            {
                bucket = await _generator.GenerateAsync(item.LibraryRoot, item.RelPath, cancellationToken, verifyShape: false)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrEmpty(bucket))
            {
                await _libraries.SetColorBucketAsync(item, bucket, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"thumb queue {item.FileName}", ex);
        }
        finally
        {
            _gate.Release();
        }
    }
}
