using Seiri.Core;
using Seiri.Core.Models;

namespace Seiri.Tests;

public class QueryParserTests
{
    [Fact]
    public void Parse_include_and_exclude_tags()
    {
        var q = QueryParser.Parse("tag:\"long hair\" -tag:monochrome type:image");
        Assert.Equal(MediaKindFilter.Images, q.Kind);
        Assert.Contains(q.Tags, t => t.Name == "long hair" && !t.Exclude);
        Assert.Contains(q.Tags, t => t.Name == "monochrome" && t.Exclude);
    }

    [Fact]
    public void Parse_underscores_become_spaces()
    {
        var q = QueryParser.Parse("tag:long_hair");
        Assert.Equal("long hair", q.Tags[0].Name);
    }

    [Fact]
    public void Suggest_empty_shows_prefixes()
    {
        var suggestions = QueryParser.Suggest("", []);
        Assert.Contains(suggestions, s => s.Label == "tag:");
        Assert.Contains(suggestions, s => s.Kind == "prefix");
    }

    [Fact]
    public void Suggest_include_exclude_for_exact_tag()
    {
        var catalog = new List<TagRecord> { new() { Name = "1girl", UseCount = 12 } };
        var suggestions = QueryParser.Suggest("tag:1girl", catalog);
        Assert.Contains(suggestions, s => s.Kind == "action" && s.Label.StartsWith("Include"));
        Assert.Contains(suggestions, s => s.Kind == "action" && s.Label.StartsWith("Exclude"));
        Assert.Contains("-tag:", suggestions[1].ApplyText);
    }

    [Fact]
    public void Parse_character_sets_category()
    {
        var q = QueryParser.Parse("character:frieren tag:1girl");
        Assert.Contains(q.Tags, t => t.Name == "frieren" && t.Category == "character" && !t.Exclude);
        Assert.Contains(q.Tags, t => t.Name == "1girl" && t.Category is null);
    }

    [Fact]
    public void Parse_date_and_folder()
    {
        var q = QueryParser.Parse("folder:characters taken:>2024-01-01 modified:<2025-12-31");
        Assert.Equal("characters", q.Folder);
        Assert.Equal(DateField.Modified, q.DateField);
        Assert.Equal(new DateTime(2025, 12, 31), q.DateBefore);

        var taken = QueryParser.Parse("taken:>2024-01-01");
        Assert.Equal(DateField.Taken, taken.DateField);
        Assert.Equal(new DateTime(2024, 1, 1), taken.DateAfter);

        var exact = QueryParser.Parse("date:2024-06-03");
        Assert.Equal(DateField.Taken, exact.DateField);
        Assert.Equal(new DateTime(2024, 6, 3), exact.DateExact);

        var added = QueryParser.Parse("added:2024-01-02");
        Assert.Equal(DateField.Added, added.DateField);
        Assert.Equal(new DateTime(2024, 1, 2), added.DateExact);
    }

    [Fact]
    public void Parse_unquoted_multiword_folder()
    {
        var q = QueryParser.Parse("folder:Art Dump tag:1girl");
        Assert.Equal("art dump", q.Folder);
        Assert.Contains(q.Tags, t => t.Name == "1girl");

        var quoted = QueryParser.Parse("folder:\"Art Dump\"");
        Assert.Equal("art dump", quoted.Folder);
    }

    [Fact]
    public void Suggest_folder_lists_libraries()
    {
        var folders = new[] { "Art Dump", "test-tagging", "characters" };
        var suggestions = QueryParser.Suggest("folder:", [], folders);
        Assert.Contains(suggestions, s => s.Kind == "folder" && s.Label == "Art Dump");
        Assert.Contains(suggestions, s => s.ApplyText.Contains("Art Dump"));
    }

    [Fact]
    public void ToRaw_emits_category_prefixes()
    {
        var q = new MediaQuery
        {
            Tags =
            [
                new TagClause { Name = "frieren", Category = "character" },
                new TagClause { Name = "1girl" }
            ],
            Folder = "art dump"
        };
        var raw = QueryParser.ToRaw(q);
        Assert.Contains("character:frieren", raw);
        Assert.Contains("tag:1girl", raw);
        Assert.Contains("folder:\"art dump\"", raw);
    }

    [Fact]
    public void ParseManualTag_character_prefix()
    {
        Assert.True(QueryParser.TryParseManualTag("character:kasane teto", out var name, out var category));
        Assert.Equal("kasane teto", name);
        Assert.Equal("character", category);
        Assert.True(QueryParser.TryParseManualTag("rating:explicit", out name, out category));
        Assert.Equal("explicit", name);
        Assert.Equal("rating", category);
        Assert.True(QueryParser.TryParseManualTag("1girl", out name, out category));
        Assert.Equal("1girl", name);
        Assert.Equal("general", category);
    }

    [Fact]
    public void Parse_character_with_space_after_colon()
    {
        var q = QueryParser.Parse("character: teto");
        Assert.Contains(q.Tags, t => t.Name == "teto" && t.Category == "character");
    }

    [Fact]
    public void Suggest_matches_teto_inside_kasane_teto()
    {
        var catalog = new List<TagRecord>
        {
            new() { Name = "kasane teto", Category = "character", UseCount = 40 },
            new() { Name = "teto skull", Category = "general", UseCount = 2 }
        };
        var suggestions = QueryParser.Suggest("character: teto", catalog);
        Assert.Contains(suggestions, s => s.Label == "kasane teto");
        Assert.Contains(suggestions, s => s.ApplyText.Contains("character:\"kasane teto\"") || s.ApplyText.Contains("character:kasane"));
    }

    [Fact]
    public void Suggest_matches_tokens_in_any_order()
    {
        var catalog = new List<TagRecord> { new() { Name = "kasane teto", UseCount = 10 } };
        var suggestions = QueryParser.Suggest("teto kasane", catalog);
        Assert.Contains(suggestions, s => s.Label == "kasane teto");
    }

    [Fact]
    public void CompactCount_shortens_thousands()
    {
        Assert.Equal("980", CompactCount.Format(980));
        Assert.Equal("1.2k", CompactCount.Format(1239));
        Assert.Equal("14.8k", CompactCount.Format(14782));
        Assert.Equal("1k", CompactCount.Format(1000));
    }

    [Fact]
    public void ToRaw_emits_explicit_date_field()
    {
        var q = new MediaQuery
        {
            DateField = DateField.Modified,
            DateExact = new DateTime(2024, 6, 3)
        };
        Assert.Contains("modified:2024-06-03", QueryParser.ToRaw(q));
        Assert.DoesNotContain("date:", QueryParser.ToRaw(q));
    }
}
