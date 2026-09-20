using System.Security.Cryptography;
using Seiri.Core;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public sealed class ContentHasher
{
    private readonly LibraryService _libraries;

    public ContentHasher(LibraryService libraries)
    {
        _libraries = libraries;
    }

    public async Task HashMissingAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var pending = await _libraries.ListUnhashedAsync(cancellationToken).ConfigureAwait(false);
        var done = 0;
        foreach (var item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(item.FullPath))
            {
                continue;
            }

            progress?.Report($"Hashing {item.FileName} ({done + 1}/{pending.Count})");
            try
            {
                await using var stream = new FileStream(
                    item.FullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    256 * 1024,
                    useAsync: true);
                var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
                await _libraries.SetContentHashAsync(item, Convert.ToHexString(hash).ToLowerInvariant(), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error($"hash {item.FullPath}", ex);
            }

            done++;
        }
    }
}
