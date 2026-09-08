using System.Text.Json;
using Seiri.Core.Models;

namespace Seiri.Core.Tagging;

public static class ModelCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static IReadOnlyList<ModelCatalogEntry> BuiltIn { get; } =
    [
        new()
        {
            Id = "wd-eva02-large-tagger-v3",
            DisplayName = "WD EVA02-Large v3",
            Repo = "SmilingWolf/wd-eva02-large-tagger-v3",
            License = "Apache-2.0",
            Description = "Best WD accuracy. Trained on Danbooru-style anime and illustration — it will mis-tag vacation photos. ~1.26 GB; a discrete GPU is more comfortable than CPU.",
            Preprocess = "WdV3",
            SizeBytes = 1_260_000_000,
            Files =
            [
                new ModelFile { Name = "model.onnx", Sha256 = "9e768793060c7939b277ccb382783e8670e8a042d29d77aa736be0c8cc898bfc" },
                new ModelFile { Name = "selected_tags.csv" }
            ],
            BalancedGeneral = 0.35f,
            BalancedCharacter = 0.85f,
            PreciseGeneral = 0.5296f,
            PreciseCharacter = 0.85f,
            RecallGeneral = 0.25f,
            RecallCharacter = 0.70f
        },
        new()
        {
            Id = "wd-swinv2-tagger-v3",
            DisplayName = "WD SwinV2 v3",
            Repo = "SmilingWolf/wd-swinv2-tagger-v3",
            License = "Apache-2.0",
            Description = "Faster WD default for weaker GPUs and CPU. Same Danbooru-style vocabulary as EVA02. ~450 MB.",
            Preprocess = "WdV3",
            SizeBytes = 467_460_978,
            Files =
            [
                new ModelFile { Name = "model.onnx", Sha256 = "e6774bff34d43bd49f75a47db4ef217dce701c9847b546523eb85ff6dbba1db1" },
                new ModelFile { Name = "selected_tags.csv" }
            ],
            BalancedGeneral = 0.35f,
            BalancedCharacter = 0.85f,
            PreciseGeneral = 0.5296f,
            PreciseCharacter = 0.85f,
            RecallGeneral = 0.25f,
            RecallCharacter = 0.70f
        },
        new()
        {
            Id = "pixai-tagger-v0.9",
            DisplayName = "PixAI Tagger v0.9",
            Repo = "deepghs/pixai-tagger-v0.9-onnx",
            License = "Apache-2.0",
            Description = "Newer Danbooru (2025-01) with stronger character tags. ONNX export from DeepGHS; finetuned head on WD EVA02. ~1.27 GB.",
            Preprocess = "PixaiV09",
            SizeBytes = 1_271_365_854,
            Files =
            [
                new ModelFile { Name = "model.onnx", Sha256 = "a8d479098b5e23f253543c93df42391736abbb77c21c2efd3a513b9cda7b3657" },
                new ModelFile { Name = "selected_tags.csv" }
            ],
            BalancedGeneral = 0.30f,
            BalancedCharacter = 0.75f,
            PreciseGeneral = 0.50f,
            PreciseCharacter = 0.90f,
            RecallGeneral = 0.20f,
            RecallCharacter = 0.60f
        }
    ];

    public static IReadOnlyList<ModelCatalogEntry> Load(string? jsonPath)
    {
        if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath))
        {
            return BuiltIn;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);
            var file = JsonSerializer.Deserialize<ModelCatalogFile>(json, Json);
            if (file?.Models is { Count: > 0 })
            {
                return file.Models;
            }
        }
        catch (JsonException)
        {
        }

        return BuiltIn;
    }

    public static ModelCatalogEntry? Find(IReadOnlyList<ModelCatalogEntry> catalog, string id) =>
        catalog.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
