using Microsoft.ML.OnnxRuntime;
using Seiri.Core;
using Seiri.Core.Contracts;
using Seiri.Core.Models;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;

namespace Seiri.Services;

public sealed class OnnxTagger : ITagger
{
    private readonly ModelCatalogEntry _entry;
    private readonly IAppHome _home;
    private readonly string _provider;
    private readonly SemaphoreSlim _run = new(1, 1);
    private InferenceSession? _session;
    private IReadOnlyList<ModelTag> _tags = [];
    private string _inputName = "input";
    private string _outputName = "prediction";
    private bool _dml;
    private bool _warmed;
    public bool IsDirectMl => _dml;
    public string RequestedProvider => _provider;

    public OnnxTagger(ModelCatalogEntry entry, IAppHome home, string executionProvider)
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

    public async Task<TagResult> TagAsync(TaggerImage image, TaggerOptions options, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
        await _run.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await OrtWorker.InvokeAsync(() => Infer(image, options), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _run.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _session = null;
        _run.Dispose();
        return ValueTask.CompletedTask;
    }

    private TagResult Infer(TaggerImage image, TaggerOptions options)
    {
        if (!_warmed)
        {
            AppLog.Heartbeat($"first infer '{_entry.Id}' provider={(_dml ? "DML" : "CPU")} {image.Width}x{image.Height}");
            _warmed = true;
        }

        try
        {
            return RunOnce(image, options);
        }
        catch (Exception ex) when (_dml && IsProviderFailure(ex))
        {
            AppLog.Error($"DML infer '{_entry.Id}', falling back to CPU", ex);
            RecreateOnCpu();
            return RunOnce(image, options);
        }
    }

    private TagResult RunOnce(TaggerImage image, TaggerOptions options)
    {
        var session = _session ?? throw new InvalidOperationException("ONNX session is not loaded.");
        var pixai = _entry.Preprocess.Equals("PixaiV09", StringComparison.OrdinalIgnoreCase);
        var tensorData = pixai
            ? PixaiPreprocess.FromBgra(image.Bgra, image.Width, image.Height)
            : WdV3Preprocess.FromBgra(image.Bgra, image.Width, image.Height);
        var shape = pixai ? PixaiPreprocess.Shape : WdV3Preprocess.Shape;

        using var input = OrtValue.CreateTensorValueFromMemory(tensorData, shape);
        var inputs = new Dictionary<string, OrtValue> { [_inputName] = input };
        using var runOptions = new RunOptions();
        using var results = session.Run(runOptions, inputs, [_outputName]);
        if (results.Count == 0)
        {
            throw new InvalidOperationException($"Model '{_entry.Id}' returned no outputs.");
        }

        var scores = results[0].GetTensorDataAsSpan<float>();
        return TagCsvParser.ToResult(_entry.Id, scores, _tags, options);
    }

    private void LoadSession()
    {
        var onnx = ModelDownloader.FilePath(_home, _entry.Id, "model.onnx");
        var csv = ModelDownloader.FilePath(_home, _entry.Id, "selected_tags.csv");
        if (!File.Exists(onnx) || !File.Exists(csv))
        {
            throw new FileNotFoundException($"Model '{_entry.Id}' is not installed.");
        }

        _tags = TagCsvParser.Parse(File.ReadAllText(csv));
        var wantDml = ExecutionProviders.WantsDirectMl(_provider);
        if (wantDml)
        {
            try
            {
                AppLog.Heartbeat($"Loading tagger '{_entry.Id}' DirectML");
                _session = new InferenceSession(onnx, CreateSessionOptions(dml: true));
                _dml = true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"DirectML load '{_entry.Id}', falling back to CPU", ex);
                _session?.Dispose();
                _session = new InferenceSession(onnx, CreateSessionOptions(dml: false));
                _dml = false;
            }
        }
        else
        {
            AppLog.Heartbeat($"Loading tagger '{_entry.Id}' CPU");
            _session = new InferenceSession(onnx, CreateSessionOptions(dml: false));
            _dml = false;
        }

        BindNames(_session);
        AppLog.Heartbeat($"Tagger '{_entry.Id}' ready provider={ExecutionProviders.Describe(_dml)} input={_inputName} output={_outputName}");
    }

    private void RecreateOnCpu()
    {
        var onnx = ModelDownloader.FilePath(_home, _entry.Id, "model.onnx");
        _session?.Dispose();
        _session = new InferenceSession(onnx, CreateSessionOptions(dml: false));
        _dml = false;
        BindNames(_session);
        AppLog.Heartbeat($"Tagger '{_entry.Id}' recreated CPU");
    }

    private void BindNames(InferenceSession session)
    {
        _inputName = session.InputMetadata.Keys.First();
        var outputs = session.OutputMetadata.Keys.ToList();
        _outputName = outputs.FirstOrDefault(n => n.Equals("prediction", StringComparison.OrdinalIgnoreCase))
            ?? outputs.FirstOrDefault(n => n.Equals("output", StringComparison.OrdinalIgnoreCase))
            ?? outputs.First(n => !n.Equals("embedding", StringComparison.OrdinalIgnoreCase)
                && !n.Equals("logits", StringComparison.OrdinalIgnoreCase));
    }

    private static SessionOptions CreateSessionOptions(bool dml)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_BASIC,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            EnableMemoryPattern = false
        };

        if (dml)
        {
            options.EnableCpuMemArena = false;
            try
            {
                options.AddSessionConfigEntry("ep.dml.enable_graph_capture", "0");
                options.AddSessionConfigEntry("ep.dml.enable_dynamic_graph_fusion", "0");
            }
            catch
            {
            }

            options.AppendExecutionProvider_DML(0);
        }

        return options;
    }

    private static bool IsProviderFailure(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains("DML", StringComparison.OrdinalIgnoreCase)
            || text.Contains("DirectML", StringComparison.OrdinalIgnoreCase)
            || text.Contains("DXGI", StringComparison.OrdinalIgnoreCase)
            || text.Contains("HRESULT", StringComparison.OrdinalIgnoreCase);
    }
}
