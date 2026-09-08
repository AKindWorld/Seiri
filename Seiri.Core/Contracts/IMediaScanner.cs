using Seiri.Core.Models;

namespace Seiri.Core.Contracts;

public interface IMediaScanner
{
    IAsyncEnumerable<MediaItem> ScanAsync(string libraryRoot, string generatedFolderName, CancellationToken cancellationToken = default);
}
