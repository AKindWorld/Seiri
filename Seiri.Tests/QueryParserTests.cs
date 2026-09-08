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
}
