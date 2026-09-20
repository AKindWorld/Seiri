using Microsoft.ML.OnnxRuntime;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;

namespace Seiri.Services;

public sealed class OnnxEmbedder : IAsyncDisposable
{
    private readonly ModelCatalogEntry _entry;
    private readonly IAppHome _home;
    private readonly string _provider;
    private InferenceSession? _session;
    private string _inputName = "input";
    private string _outputName = "embedding";
    private readonly SemaphoreSlim _run = new(1, 1);

    public OnnxEmbedder(ModelCatalogEntry entry, IAppHome home, string executionProvider)
    {
        _entry = entry;
        _home = home;
        _provider = executionProvider;
    }

    public string Id => _entry.Id;

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_session is not null)
        {
            return Task.CompletedTask;
        }

        return OrtWorker.InvokeAsync(LoadSession, cancellationToken);
    }

    public async Task<float[]> EmbedAsync(TaggerImage image, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _run.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = _session ?? throw new InvalidOperationException("Embedding session is not loaded.");
            return await OrtWorker.InvokeAsync(() =>
            {
                var tensor = ClipPreprocess.FromBgra(image.Bgra, image.Width, image.Height);
                using var input = OrtValue.CreateTensorValueFromMemory(tensor, ClipPreprocess.Shape);
                var inputs = new Dictionary<string, OrtValue> { [_inputName] = input };
                using var runOptions = new RunOptions();
                using var results = session.Run(runOptions, inputs, [_outputName]);
                if (results.Count == 0)
                {
                    throw new InvalidOperationException($"Model '{_entry.Id}' returned no outputs.");
                }

                return ReadEmbedding(results[0].GetTensorDataAsSpan<float>());
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _run.Release();
        }
    }

    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var n = Math.Min(a.Length, b.Length);
        if (n == 0)
        {
            return 0;
        }

        double dot = 0;
        for (var i = 0; i < n; i++)
        {
            dot += a[i] * b[i];
        }

        return (float)dot;
    }

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _session = null;
        _run.Dispose();
        return ValueTask.CompletedTask;
    }

    private void LoadSession()
    {
        var onnx = ResolveOnnx();
        if (onnx is null)
        {
            throw new FileNotFoundException($"Similar model '{_entry.Id}' is not installed.");
        }

        // Quantized CLIP + DirectML often native-crashes; keep similar on CPU.
        AppLog.Write($"Loading similar model '{_entry.Id}' CPU {onnx}");
        _session = new InferenceSession(onnx, CreateSessionOptions());
        _inputName = _session.InputMetadata.Keys.First();
        var outputs = _session.OutputMetadata.Keys.ToList();
        _outputName = outputs.FirstOrDefault(n => n.Contains("embed", StringComparison.OrdinalIgnoreCase))
            ?? outputs.FirstOrDefault(n => n.Contains("pool", StringComparison.OrdinalIgnoreCase))
            ?? outputs.FirstOrDefault(n => n.Contains("last_hidden", StringComparison.OrdinalIgnoreCase))
            ?? outputs[^1];
        AppLog.Write($"Similar model '{_entry.Id}' loaded CPU input={_inputName} output={_outputName}");
    }

    private string? ResolveOnnx()
    {
        var dir = ModelDownloader.ModelDirectory(_home, _entry.Id);
        foreach (var file in _entry.Files.Where(f => f.Name.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase)))
        {
            var path = Path.Combine(dir, file.Name);
            if (File.Exists(path))
            {
                return path;
            }
        }

        var fallback = Path.Combine(dir, "model.onnx");
        if (File.Exists(fallback))
        {
            return fallback;
        }

        return Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*.onnx", SearchOption.AllDirectories).FirstOrDefault()
            : null;
    }

    private static SessionOptions CreateSessionOptions() => new()
    {
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
        EnableMemoryPattern = true
    };

    private static float[] ReadEmbedding(ReadOnlySpan<float> span)
    {
        var dim = span.Length >= 768 && span.Length % 768 == 0 ? 768
            : span.Length >= 512 && span.Length % 512 == 0 ? 512
            : span.Length;
        var vector = span[..dim].ToArray();
        Normalize(vector);
        return vector;
    }

    private static void Normalize(Span<float> vector)
    {
        double sum = 0;
        for (var i = 0; i < vector.Length; i++)
        {
            sum += vector[i] * vector[i];
        }

        var norm = Math.Sqrt(sum);
        if (norm < 1e-8)
        {
            return;
        }

        var scale = (float)(1.0 / norm);
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] *= scale;
        }
    }
}
