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
