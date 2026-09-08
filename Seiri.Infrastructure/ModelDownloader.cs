using System.Net.Http.Headers;
using System.Security.Cryptography;
using Seiri.Core.Contracts;
using Seiri.Core.Models;

namespace Seiri.Infrastructure;

public sealed class ModelDownloader : IDisposable
{
    private readonly IAppHome _home;
    private readonly HttpClient _http;

    public ModelDownloader(IAppHome home, HttpClient? http = null)
    {
        _home = home;
        _http = http ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Seiri/1.0 (WinUI; local tagger)");
        }
    }

    public static string ModelDirectory(IAppHome home, string id) =>
        Path.Combine(home.ModelsDirectory, id);

    public static string FilePath(IAppHome home, string id, string fileName) =>
        Path.Combine(ModelDirectory(home, id), fileName);

    public static bool IsInstalled(IAppHome home, ModelCatalogEntry entry)
    {
        var dir = ModelDirectory(home, entry.Id);
        if (!Directory.Exists(dir))
        {
            return false;
        }

        foreach (var file in entry.Files)
        {
            var path = Path.Combine(dir, file.Name);
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
            {
                return false;
            }
        }

        return entry.Files.Count > 0;
    }

    public async Task DownloadAsync(
        ModelCatalogEntry entry,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dir = ModelDirectory(_home, entry.Id);
        Directory.CreateDirectory(dir);

        foreach (var file in entry.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dest = Path.Combine(dir, file.Name);
            var partial = dest + ".partial";
            var url = $"https://huggingface.co/{entry.Repo}/resolve/main/{file.Name}?download=true";
            var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0L;

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0)
            {
                request.Headers.Range = new RangeHeaderValue(existing, null);
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var remaining = response.Content.Headers.ContentLength;
            var total = remaining is { } r ? existing + r : (long?)null;
            await using var output = new FileStream(
                partial,
                existing > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                256 * 1024,
                useAsync: true);
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new byte[256 * 1024];
            var received = existing;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new ModelDownloadProgress
                {
                    FileName = file.Name,
                    BytesReceived = received,
                    TotalBytes = total
                });
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);

            if (!string.IsNullOrEmpty(file.Sha256))
            {
                var hash = await HashFileAsync(partial, cancellationToken).ConfigureAwait(false);
                if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(partial);
                    throw new InvalidDataException($"Checksum mismatch for {file.Name}.");
                }
            }

            File.Move(partial, dest, overwrite: true);
        }
    }

    public Task DeleteAsync(ModelCatalogEntry entry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dir = ModelDirectory(_home, entry.Id);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        return Task.CompletedTask;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public void Dispose() => _http.Dispose();
}
