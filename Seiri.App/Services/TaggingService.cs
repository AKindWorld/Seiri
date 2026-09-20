using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;

namespace Seiri.Services;

public sealed class TaggingService : IAsyncDisposable
{
    private readonly IAppHome _home;
    private readonly LibraryService _libraries;
    private readonly Dictionary<string, OnnxTagger> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public TaggingService(IAppHome home, LibraryService libraries)
    {
        _home = home;
        _libraries = libraries;
    }

    public async Task UnloadAsync(string id)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_loaded.Remove(id, out var tagger))
            {
                await tagger.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TaggingRunResult> RunAsync(
        IReadOnlyList<MediaItem> items,
        IReadOnlyList<ModelCatalogEntry> models,
        AppSettings settings,
        IProgress<TaggingProgress>? progress,
        CancellationToken cancellationToken,
        bool overwrite = false)
    {
        if (models.Count == 0)
        {
            throw new InvalidOperationException("Enable at least one installed model in Settings → Models.");
        }

        progress?.Report(new TaggingProgress { Total = items.Count, Phase = "loading", CurrentFile = "Loading models…" });
        var taggers = new List<(ModelCatalogEntry Entry, OnnxTagger Tagger)>();
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            taggers.Add((model, await GetOrCreateAsync(model, settings.ExecutionProvider, cancellationToken).ConfigureAwait(false)));
        }

        var wantDml = ExecutionProviders.WantsDirectMl(settings.ExecutionProvider);
        if (wantDml && taggers.Any(t => !t.Tagger.IsDirectMl))
        {
            AppLog.Write("GPU requested but at least one tagger is on CPU (DirectML failed to load).");
        }
        else if (wantDml)
        {
            AppLog.Write($"Tagging on DirectML with {taggers.Count} model(s).");
        }

        var tagged = 0;
        var failed = 0;
        var skipped = 0;
        var done = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new TaggingProgress
            {
                Done = done,
                Total = items.Count,
                CurrentFile = item.FileName,
                Tagged = tagged,
                Failed = failed,
                Skipped = skipped,
                Phase = "tagging"
            });

            if (item.Kind != MediaKind.Image)
            {
                skipped++;
                done++;
                continue;
            }

            var sidecarRel = item.SidecarRel ?? SidecarFormat.SidecarRelative(item.RelPath);
            var sidecarPath = GeneratedLayout.ToFullPath(item.LibraryRoot, sidecarRel);
            if (!overwrite && item.TagCount == 0 && File.Exists(sidecarPath) && item.TagError is null)
            {
                skipped++;
                done++;
                continue;
            }

            try
            {
                AppLog.Heartbeat($"tag {done + 1}/{items.Count} {item.FileName}");
                var image = await ImagePixelLoader.LoadScaledAsync(item.FullPath, 448, cancellationToken)
                    .ConfigureAwait(false);
                AppLog.Heartbeat($"decoded {item.FileName} {image.Width}x{image.Height}");
                var results = new List<TagResult>();
                foreach (var (entry, tagger) in taggers)
                {
                    var preset = settings.ModelPresets.TryGetValue(entry.Id, out var p) ? p : ThresholdPreset.Balanced;
                    var (general, character) = entry.Thresholds(preset);
                    var options = new TaggerOptions
                    {
                        GeneralThreshold = general,
                        CharacterThreshold = character,
                        MaxTags = Math.Clamp(settings.MaxTags, 1, 128)
                    };
                    results.Add(await tagger.TagAsync(image, options, cancellationToken).ConfigureAwait(false));
                }

                var merged = TagMerge.UnionMax(results, Math.Clamp(settings.MaxTags, 1, 128));
                await _libraries.ApplyAutoTagsAsync(item, [.. merged.AllScored()], merged.Rating?.Name, settings, cancellationToken)
                    .ConfigureAwait(false);
                tagged++;
                AppLog.Heartbeat($"tagged {item.FileName}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error($"tag {item.FullPath}", ex);
                try
                {
                    await _libraries.SetTagErrorAsync(item, ex.Message, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception markEx)
                {
                    AppLog.Error($"tag error mark {item.FileName}", markEx);
                }

                failed++;
            }

            done++;
        }

        progress?.Report(new TaggingProgress
        {
            Done = done,
            Total = items.Count,
            CurrentFile = string.Empty,
            Tagged = tagged,
            Failed = failed,
            Skipped = skipped,
            Phase = "done"
        });

        return new TaggingRunResult { Tagged = tagged, Failed = failed, Skipped = skipped, Total = items.Count };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var tagger in _loaded.Values)
        {
            await tagger.DisposeAsync().ConfigureAwait(false);
        }

        _loaded.Clear();
        _gate.Dispose();
    }

    private async Task<OnnxTagger> GetOrCreateAsync(ModelCatalogEntry entry, string provider, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded.TryGetValue(entry.Id, out var existing))
            {
                if (string.Equals(existing.RequestedProvider, provider, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }

                await existing.DisposeAsync().ConfigureAwait(false);
                _loaded.Remove(entry.Id);
            }

            var tagger = new OnnxTagger(entry, _home, provider);
            await tagger.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            _loaded[entry.Id] = tagger;
            return tagger;
        }
        finally
        {
            _gate.Release();
        }
    }
}
