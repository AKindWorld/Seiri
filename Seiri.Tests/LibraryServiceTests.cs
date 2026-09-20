using Seiri.Core;
using Seiri.Core.Models;
using Seiri.Infrastructure;

namespace Seiri.Tests;

public class LibraryServiceTests
{
    [Fact]
    public async Task Scan_reads_sidecars_and_skips_generated_folder()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            var info = await service.AddAsync(root);

            Assert.True(File.Exists(GeneratedLayout.GetMarkerPath(root)));
            Assert.True(File.Exists(GeneratedLayout.GetDatabasePath(root)));
            Assert.Equal(3, info.FileCount);

            var all = await service.QueryAsync(new MediaQuery());
            Assert.Equal(3, all.Count);
            var hutao = all.Single(m => m.FileName == "hutao.png");
            Assert.Equal(3, hutao.TagCount);
            var tags = await service.GetTagsAsync(hutao);
            Assert.Contains("1girl", tags);
            Assert.Contains("long hair", tags);

            var tagged = await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Tagged });
            Assert.Single(tagged);
            var untagged = await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Untagged });
            Assert.Equal(2, untagged.Count);
            Assert.Equal(3, await service.CountAsync(new MediaQuery()));
            Assert.Equal(1, await service.CountAsync(new MediaQuery { Tagged = TaggedFilter.Tagged }));
            Assert.Equal(2, await service.CountAsync(new MediaQuery { Tagged = TaggedFilter.Untagged }));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task SetTags_writes_sidecar_and_query_finds_it()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            var untagged = (await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Untagged }))
                .First(m => m.FileName == "untagged.png");

            var settings = new AppSettings();
            await service.SetTagsAsync(untagged, ["solo", "male"], settings);

            var sidecar = Path.Combine(root, "untagged.txt");
            Assert.True(File.Exists(sidecar));
            Assert.Contains("solo", File.ReadAllText(sidecar));

            var found = await service.QueryAsync(QueryParser.Parse("tag:solo"));
            Assert.Contains(found, m => m.FileName == "untagged.png");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Character_query_does_not_match_general_tag()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            var untagged = (await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Untagged }))
                .First(m => m.FileName == "untagged.png");

            await service.SetTagsAsync(
                untagged,
                [new TagRecord { Name = "1girl", Category = "general" }],
                new AppSettings());

            var asTag = await service.QueryAsync(QueryParser.Parse("tag:1girl"));
            Assert.Contains(asTag, m => m.FileName == "untagged.png");
            var asCharacter = await service.QueryAsync(QueryParser.Parse("character:1girl"));
            Assert.DoesNotContain(asCharacter, m => m.FileName == "untagged.png");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Folder_query_matches_path_segment()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            var inFolder = await service.QueryAsync(QueryParser.Parse("folder:characters"));
            Assert.Contains(inFolder, m => m.FileName == "zhongli.png");
            Assert.DoesNotContain(inFolder, m => m.FileName == "hutao.png");

            var libName = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
            var wholeLib = await service.QueryAsync(QueryParser.Parse($"folder:\"{libName}\""));
            Assert.True(wholeLib.Count >= 3);
            Assert.Contains(wholeLib, m => m.FileName == "hutao.png");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Auto_tag_sidecar_writes_categories()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            var untagged = (await service.QueryAsync(new MediaQuery { Tagged = TaggedFilter.Untagged }))
                .First(m => m.FileName == "untagged.png");

            await service.ApplyAutoTagsAsync(
                untagged,
                [
                    new ScoredTag { Name = "1girl", Category = "general", Score = 0.9f, ModelId = "wd" },
                    new ScoredTag { Name = "hutao", Category = "character", Score = 0.95f, ModelId = "wd" }
                ],
                "general",
                new AppSettings());

            var sidecar = File.ReadAllText(Path.Combine(root, "untagged.txt"));
            Assert.Contains("rating: general", sidecar);
            Assert.Contains("character:", sidecar);
            Assert.Contains("hutao", sidecar);
            Assert.Contains("1girl", sidecar);

            var records = await service.GetTagRecordsAsync(untagged);
            Assert.Contains(records, t => t.Name == "hutao" && t.Category == "character");
            var asCharacter = await service.QueryAsync(QueryParser.Parse("character:hutao"));
            Assert.Contains(asCharacter, m => m.FileName == "untagged.png");
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Nested_libraries_are_rejected()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.AddAsync(Path.Combine(root, "characters")));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task Missing_files_are_hidden_after_rescan()
    {
        var root = Fixture.CreateLibrary();
        try
        {
            var home = new TempHome();
            await using var service = new LibraryService(new JsonLibraryRegistry(home), new FileMediaScanner());
            await service.AddAsync(root);
            File.Delete(Path.Combine(root, "untagged.png"));
            var index = service.OpenIndexes.Single();
            await service.ScanAsync(index);
            var all = await service.QueryAsync(new MediaQuery());
            Assert.DoesNotContain(all, m => m.FileName == "untagged.png");
            Assert.Equal(2, all.Count);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Nesting_detects_parent_and_child()
    {
        Assert.True(LibraryNesting.Conflicts(@"D:\Art\chars", [@"D:\Art"]));
        Assert.True(LibraryNesting.Conflicts(@"D:\Art", [@"D:\Art\chars"]));
        Assert.False(LibraryNesting.Conflicts(@"D:\Art", [@"D:\Other"]));
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
