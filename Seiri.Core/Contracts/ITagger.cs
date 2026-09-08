using Seiri.Core.Models;

namespace Seiri.Core.Contracts;

public interface ITagger : IAsyncDisposable
{
    string Id { get; }
    Task EnsureReadyAsync(CancellationToken cancellationToken = default);
    Task<TagResult> TagAsync(TaggerImage image, TaggerOptions options, CancellationToken cancellationToken = default);
}
