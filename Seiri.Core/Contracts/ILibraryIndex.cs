using Seiri.Core.Models;

namespace Seiri.Core.Contracts;

public interface ILibraryIndex : IAsyncDisposable
{
    string RootPath { get; }
    string GeneratedFolderName { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpsertMediaAsync(IReadOnlyList<MediaItem> items, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> QueryAsync(MediaQuery query, CancellationToken cancellationToken = default);
    Task<int> CountAsync(MediaQuery query, CancellationToken cancellationToken = default);
    Task<LibraryInfo> GetInfoAsync(CancellationToken cancellationToken = default);
    Task SetFavoriteAsync(long id, bool isFavorite, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetTagsAsync(long mediaId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagRecord>> GetTagRecordsAsync(long mediaId, CancellationToken cancellationToken = default);
    Task SetTagsAsync(long mediaId, IReadOnlyList<string> tags, CancellationToken cancellationToken = default);
    Task SetTagsAsync(long mediaId, IReadOnlyList<TagRecord> tags, CancellationToken cancellationToken = default);
    Task ApplyAutoTagsAsync(long mediaId, IReadOnlyList<ScoredTag> tags, string? rating, CancellationToken cancellationToken = default);
    Task SetTagErrorAsync(long mediaId, string? error, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagRecord>> SuggestTagsAsync(string? prefix, int limit = 20, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TagRecord>> ListTagsAsync(int limit = 5000, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListExtensionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListFolderNamesAsync(CancellationToken cancellationToken = default);
    Task SetDimensionsAsync(string relPath, int width, int height, CancellationToken cancellationToken = default);
    Task SetColorBucketAsync(string relPath, string bucket, CancellationToken cancellationToken = default);
    Task SetContentHashAsync(long id, string hash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> ListUnhashedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<MediaItem>>> ListDuplicateGroupsAsync(CancellationToken cancellationToken = default);
    Task UpsertEmbeddingAsync(long mediaId, string modelId, float[] vector, CancellationToken cancellationToken = default);
    Task<float[]?> GetEmbeddingAsync(long mediaId, string modelId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(MediaItem Item, float[] Vector)>> ListEmbeddingsAsync(string modelId, CancellationToken cancellationToken = default);
    Task MarkMissingExceptAsync(IReadOnlyCollection<string> presentRelPaths, CancellationToken cancellationToken = default);
    Task DeleteMediaAsync(long id, CancellationToken cancellationToken = default);
    Task UpdatePathsAsync(long id, RenamePlan plan, CancellationToken cancellationToken = default);
    Task CheckpointAsync(CancellationToken cancellationToken = default);
}
