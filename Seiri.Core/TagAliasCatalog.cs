using Seiri.Core.Models;

namespace Seiri.Core;

public sealed class TagAliasCatalog
{
    private readonly Dictionary<string, string> _aliasToCanonical = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _canonicalToAliases = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoaded => _aliasToCanonical.Count > 0;
    public int AliasCount => _aliasToCanonical.Count;
    public int ImplicationCount { get; set; }

    public void Clear()
    {
        _aliasToCanonical.Clear();
        _canonicalToAliases.Clear();
        ImplicationCount = 0;
    }

    public void AddAlias(string antecedent, string consequent)
    {
        antecedent = Normalize(antecedent);
        consequent = Normalize(consequent);
        if (antecedent.Length == 0 || consequent.Length == 0 || antecedent.Equals(consequent, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _aliasToCanonical[antecedent] = consequent;
        if (!_canonicalToAliases.TryGetValue(consequent, out var list))
        {
            list = [];
            _canonicalToAliases[consequent] = list;
        }

        if (!list.Contains(antecedent, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(antecedent);
        }
    }

    public void AddImplication(string antecedent, string consequent)
    {
        antecedent = Normalize(antecedent);
        consequent = Normalize(consequent);
        if (antecedent.Length == 0 || consequent.Length == 0)
        {
            return;
        }

        ImplicationCount++;
    }

    public IReadOnlyList<string> Expand(string name)
    {
        name = Normalize(name);
        if (name.Length == 0)
        {
            return [];
        }

        var canonical = _aliasToCanonical.TryGetValue(name, out var mapped) ? mapped : name;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name, canonical };
        if (_canonicalToAliases.TryGetValue(canonical, out var aliases))
        {
            foreach (var alias in aliases)
            {
                set.Add(alias);
            }
        }

        set.Remove(name);
        return [.. set];
    }

    public MediaQuery ExpandQuery(MediaQuery query)
    {
        if (!IsLoaded || query.Tags.Count == 0)
        {
            return query;
        }

        var clone = query.Clone();
        clone.Tags = query.Tags.Select(t => new TagClause
        {
            Name = t.Name,
            Exclude = t.Exclude,
            Category = t.Category,
            Aliases = Expand(t.Name)
        }).ToList();
        return clone;
    }

    public static string Normalize(string raw) =>
        raw.Replace('_', ' ').Trim().ToLowerInvariant();
}
