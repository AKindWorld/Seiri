using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Core.Tagging;
using Seiri.Infrastructure;

namespace Seiri.Tests;

public class TaggingTests
{
    [Fact]
    public void GPU_and_Auto_request_DirectML_CPU_does_not()
    {
        Assert.True(ExecutionProviders.WantsDirectMl("GPU"));
        Assert.True(ExecutionProviders.WantsDirectMl("Auto"));
        Assert.True(ExecutionProviders.WantsDirectMl(null));
        Assert.False(ExecutionProviders.WantsDirectMl("CPU"));
        Assert.Equal("DirectML", ExecutionProviders.Describe(true));
        Assert.Equal("CPU", ExecutionProviders.Describe(false));
    }

    [Fact]
    public void Tagging_progress_throttles_ui_except_start_and_end()
    {
        var last = 0L;
        var loading = new TaggingProgress { Phase = "loading", Total = 100 };
        Assert.True(loading.ShouldPublishUi(ref last, minIntervalMs: 10_000));

        last = Environment.TickCount64;
        var mid = new TaggingProgress { Phase = "tagging", Done = 1, Total = 100 };
        Assert.False(mid.ShouldPublishUi(ref last, minIntervalMs: 10_000));

        var done = new TaggingProgress { Phase = "tagging", Done = 100, Total = 100 };
        Assert.True(done.ShouldPublishUi(ref last, minIntervalMs: 10_000));
    }

    [Fact]
    public void FlattenAndLimit_shrinks_huge_images()
    {
        const int w = 2000;
        const int h = 1000;
        var bgra = new byte[w * h * 4];
        var limited = ImageResize.FlattenAndLimit(bgra, w, h, maxEdge: 500);
        Assert.True(limited.Width <= 500);
        Assert.True(limited.Height <= 500);
        Assert.Equal(limited.Width * limited.Height * 3, limited.Rgb.Length);
    }

    [Fact]
    public void WdV3_white_pixel_stays_white_bgr()
    {
        var bgra = new byte[] { 255, 255, 255, 255 };
        var tensor = WdV3Preprocess.FromBgra(bgra, 1, 1);
        Assert.Equal(448 * 448 * 3, tensor.Length);
        Assert.True(tensor.All(v => v is > 250 and <= 255));
        Assert.Equal(WdV3Preprocess.Shape, new long[] { 1, 448, 448, 3 });
    }

    [Fact]
    public void WdV3_red_pixel_is_bgr_not_rgb()
    {
        var bgra = new byte[] { 0, 0, 255, 255 };
        var tensor = WdV3Preprocess.FromBgra(bgra, 1, 1);
        var b = tensor[0];
        var g = tensor[1];
        var r = tensor[2];
        Assert.True(r > 250);
        Assert.True(g < 5);
        Assert.True(b < 5);
    }

    [Fact]
    public void WdV3_hutao_size_pads_to_448()
    {
        var path = HutaoPath();
        var size = ImageDimensions.TryRead(path);
        Assert.NotNull(size);
        Assert.Equal(50, size.Value.Width);
        Assert.Equal(50, size.Value.Height);

        var bgra = new byte[50 * 50 * 4];
        for (var i = 0; i < 50 * 50; i++)
        {
            bgra[i * 4] = 10;
            bgra[i * 4 + 1] = 20;
            bgra[i * 4 + 2] = 30;
            bgra[i * 4 + 3] = 255;
        }

        var tensor = WdV3Preprocess.FromBgra(bgra, 50, 50);
        Assert.Equal(448 * 448 * 3, tensor.Length);
        var mid = ((448 / 2) * 448 + (448 / 2)) * 3;
        Assert.True(tensor[mid + 2] > 20);
        Assert.True(tensor[mid] < 20);
    }

    [Fact]
    public void WdV3_alpha_flattens_onto_white()
    {
        var bgra = new byte[] { 0, 0, 0, 0 };
        var tensor = WdV3Preprocess.FromBgra(bgra, 1, 1);
        Assert.True(tensor.All(v => v is > 250 and <= 255));
    }

    [Fact]
    public void Pixai_is_nchw_normalized()
    {
        var bgra = new byte[] { 255, 255, 255, 255 };
        var tensor = PixaiPreprocess.FromBgra(bgra, 1, 1);
        Assert.Equal(3 * 448 * 448, tensor.Length);
        Assert.Equal(PixaiPreprocess.Shape, new long[] { 1, 3, 448, 448 });
        Assert.True(tensor.All(v => v is > 0.99f and <= 1.01f));
    }

    [Fact]
    public void Pixai_black_is_minus_one()
    {
        var bgra = new byte[] { 0, 0, 0, 255 };
        var tensor = PixaiPreprocess.FromBgra(bgra, 1, 1);
        Assert.True(tensor.All(v => v is < -0.99f and >= -1.01f));
    }

    [Fact]
    public void Tag_csv_parses_wd_header()
    {
        const string csv = """
            tag_id,name,category,count
            9999999,general,9,10
            470575,1girl,0,100
            123,long_hair,0,50
            9,hu_tao_(genshin_impact),4,3
            """;
        var tags = TagCsvParser.Parse(csv);
        Assert.Equal(4, tags.Count);
        Assert.Equal("general", tags[0].Name);
        Assert.Equal(9, tags[0].Category);
        Assert.Equal("1girl", tags[1].Name);
        Assert.Equal("long hair", tags[2].Name);
        Assert.Equal("character", tags[3].CategoryName);
    }

    [Fact]
    public void Tag_csv_parses_pixai_header()
    {
        const string csv = """
            id,tag_id,name,category,count,ips
            0,470575,1girl,0,10,[]
            1,13197,long_hair,0,8,[]
            """;
        var tags = TagCsvParser.Parse(csv);
        Assert.Equal(2, tags.Count);
        Assert.Equal(0, tags[0].Index);
        Assert.Equal("1girl", tags[0].Name);
        Assert.Equal("long hair", tags[1].Name);
    }

    [Fact]
    public void Tag_csv_to_result_uses_thresholds_and_rating_argmax()
    {
        const string csv = """
            tag_id,name,category,count
            1,general,9,1
            2,explicit,9,1
            3,1girl,0,1
            4,solo,0,1
            5,hutao,4,1
            """;
        var tags = TagCsvParser.Parse(csv);
        float[] scores = [0.2f, 0.9f, 0.8f, 0.1f, 0.9f];
        var result = TagCsvParser.ToResult("wd", scores, tags, new TaggerOptions
        {
            GeneralThreshold = 0.35f,
            CharacterThreshold = 0.85f,
            MaxTags = 128
        });
        Assert.Equal("explicit", result.Rating?.Name);
        Assert.Contains(result.General, t => t.Name == "1girl");
        Assert.DoesNotContain(result.General, t => t.Name == "solo");
        Assert.Contains(result.Character, t => t.Name == "hutao");
    }

    [Fact]
    public void Union_max_keeps_highest_score()
    {
        var a = new TagResult
        {
            ModelId = "a",
            General = [new ScoredTag { Name = "1girl", Score = 0.6f, ModelId = "a" }]
        };
        var b = new TagResult
        {
            ModelId = "b",
            General = [new ScoredTag { Name = "1girl", Score = 0.9f, ModelId = "b" }, new ScoredTag { Name = "hat", Score = 0.4f, ModelId = "b" }]
        };
        var merged = TagMerge.UnionMax([a, b], 128);
        var girl = merged.General.Single(t => t.Name == "1girl");
        Assert.Equal(0.9f, girl.Score);
        Assert.Equal("b", girl.ModelId);
        Assert.Contains(merged.General, t => t.Name == "hat");
    }

    [Fact]
    public void Union_max_caps_tags_and_keeps_rating()
    {
        var result = new TagResult
        {
            ModelId = "a",
            Rating = new RatingScore { Name = "general", Score = 0.8f, ModelId = "a" },
            General =
            [
                new ScoredTag { Name = "a", Score = 0.9f, Category = "general" },
                new ScoredTag { Name = "b", Score = 0.8f, Category = "general" },
                new ScoredTag { Name = "c", Score = 0.1f, Category = "general" }
            ]
        };
        var capped = TagMerge.UnionMax([result], 2);
        Assert.Equal("general", capped.Rating?.Name);
        Assert.Equal(2, capped.General.Count);
        Assert.DoesNotContain(capped.General, t => t.Name == "c");
    }

    [Fact]
    public void Catalog_has_v1_models()
    {
        var catalog = ModelCatalog.Load(null);
        Assert.Equal(4, catalog.Count);
        Assert.Contains(catalog, m => m.Id == "wd-eva02-large-tagger-v3");
        Assert.Contains(catalog, m => m.Id == "wd-swinv2-tagger-v3");
        Assert.Contains(catalog, m => m.Id == "pixai-tagger-v0.9");
        Assert.Contains(catalog, m => m.Id == "clip-vit-b32-openai" && m.Preprocess == "Clip224");
        Assert.Equal("WdV3", catalog[0].Preprocess);
        Assert.Equal("PixaiV09", catalog[2].Preprocess);
    }

    [Fact]
    public void Kaomoji_keeps_underscores()
    {
        Assert.Equal("^_^", TagDisplay.ToCanonical("^_^"));
        Assert.Equal("long hair", TagDisplay.ToCanonical("long_hair"));
    }

    [Fact]
    public async Task Failed_filter_and_auto_tags_roundtrip()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            var untagged = (await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Untagged }))
                .First(m => m.FileName == "untagged.png");

            await service.SetTagErrorAsync(untagged, "boom");
            var failed = await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Failed });
            Assert.Contains(failed, m => m.FileName == "untagged.png");

            await service.ApplyAutoTagsAsync(
                untagged,
                [new ScoredTag { Name = "1girl", Category = "general", Score = 0.9f, ModelId = "wd-eva02-large-tagger-v3" }],
                "general",
                new AppSettings());

            var sidecar = Path.Combine(root, "untagged.txt");
            Assert.True(File.Exists(sidecar));
            Assert.Contains("1girl", File.ReadAllText(sidecar));

            var tagged = await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Tagged });
            Assert.Contains(tagged, m => m.FileName == "untagged.png");
            var stillFailed = await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Failed });
            Assert.DoesNotContain(stillFailed, m => m.FileName == "untagged.png");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Downloader_is_installed_false_when_missing()
    {
        var home = new TempHome();
        var entry = ModelCatalog.BuiltIn[0];
        Assert.False(ModelDownloader.IsInstalled(home, entry));
    }

    private static string HutaoPath()
    {
        var path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "test-library", "hutao.png"));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine("..", "..", "..", "..", "..", "test-library", "hutao.png"));
        }

        return path;
    }

    private static void TryDelete(string root)
    {
        for (var i = 0; i < 5; i++)
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }

                return;
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
        }
    }

    private sealed class TempHome : Seiri.Core.Contracts.IAppHome
    {
        public TempHome()
        {
            Root = Path.Combine(Path.GetTempPath(), "seiri-home", Guid.NewGuid().ToString("N"));
            EnsureCreated();
        }

        public string Root { get; }
        public string SettingsPath => Path.Combine(Root, "settings.json");
        public string LibrariesPath => Path.Combine(Root, "libraries.json");
        public string ModelsDirectory => Path.Combine(Root, "models");
        public string LogsDirectory => Path.Combine(Root, "logs");

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(ModelsDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }
    }
}
