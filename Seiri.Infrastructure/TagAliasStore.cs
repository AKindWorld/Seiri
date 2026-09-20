using System.Text.Json;
using Seiri.Core;
using Seiri.Core.Contracts;

namespace Seiri.Infrastructure;

public sealed class TagAliasStore
{
    private readonly IAppHome _home;
    private readonly HttpClient _http;

    public TagAliasStore(IAppHome home, HttpClient? http = null)
    {
        _home = home;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Seiri/1.0");
        if (!_http.DefaultRequestHeaders.Accept.Any())
        {
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/csv,*/*");
        }
    }

    public string AliasesPath => Path.Combine(_home.Root, "tag-aliases.json");

    public TagAliasCatalog Load()
    {
        var catalog = new TagAliasCatalog();
        if (!File.Exists(AliasesPath))
        {
            return catalog;
        }

        try
        {
            var json = File.ReadAllText(AliasesPath);
            var file = JsonSerializer.Deserialize<AliasFile>(json, JsonUtil.Options);
            if (file?.Aliases is null)
            {
                return catalog;
            }

            foreach (var pair in file.Aliases)
            {
                catalog.AddAlias(pair.Key, pair.Value);
            }

            catalog.ImplicationCount = file.ImplicationCount;
        }
        catch (Exception ex)
        {
            AppLog.Error("tag-aliases.json", ex);
        }

        return catalog;
    }

    public void Save(TagAliasCatalog catalog, IReadOnlyDictionary<string, string> aliases, int implicationCount)
    {
        var file = new AliasFile
        {
            Aliases = new Dictionary<string, string>(aliases, StringComparer.OrdinalIgnoreCase),
            ImplicationCount = implicationCount
        };
        File.WriteAllText(AliasesPath, JsonSerializer.Serialize(file, JsonUtil.Options));
    }

    public async Task<TagAliasCatalog> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var catalog = new TagAliasCatalog();
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Exception? last = null;
        foreach (var loader in new Func<CancellationToken, Task>[]
                 {
                     ct => LoadDanbooruJsonAsync(
                         "https://danbooru.donmai.us/tag_aliases.json?search[status]=active",
                         (a, c) =>
                         {
                             catalog.AddAlias(a, c);
                             aliases[TagAliasCatalog.Normalize(a)] = TagAliasCatalog.Normalize(c);
                         },
                         ct),
                     ct => LoadTagCompleteCsvAsync(
                         "https://raw.githubusercontent.com/DominikDoom/a1111-sd-webui-tagcomplete/main/tags/danbooru.csv",
                         (a, c) =>
                         {
                             catalog.AddAlias(a, c);
                             aliases[TagAliasCatalog.Normalize(a)] = TagAliasCatalog.Normalize(c);
                         },
                         ct)
                 })
        {
            try
            {
                await loader(cancellationToken).ConfigureAwait(false);
                if (aliases.Count > 0)
                {
                    last = null;
                    break;
                }
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        if (aliases.Count == 0)
        {
            throw last ?? new InvalidDataException("Could not download tag aliases.");
        }

        var implications = 0;
        try
        {
            await LoadDanbooruJsonAsync(
                "https://danbooru.donmai.us/tag_implications.json?search[status]=active",
                (_, _) => implications++,
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            implications = catalog.ImplicationCount;
        }

        catalog.ImplicationCount = implications;
        Save(catalog, aliases, implications);
        return catalog;
    }

    private async Task LoadDanbooruJsonAsync(
        string baseUrl,
        Action<string, string> add,
        CancellationToken cancellationToken)
    {
        for (var page = 1; page <= 40; page++)
        {
            var url = baseUrl.Contains('?', StringComparison.Ordinal)
                ? $"{baseUrl}&limit=1000&page={page}"
                : $"{baseUrl}?limit=1000&page={page}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode == 403)
            {
                throw new HttpRequestException("Danbooru returned 403 Forbidden.");
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return;
            }

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                var ant = row.TryGetProperty("antecedent_name", out var a) ? a.GetString() : null;
                var cons = row.TryGetProperty("consequent_name", out var c) ? c.GetString() : null;
                var status = row.TryGetProperty("status", out var s) ? s.GetString() : "active";
                if (string.IsNullOrWhiteSpace(ant) || string.IsNullOrWhiteSpace(cons))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(status) && !status.Equals("active", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                add(ant, cons);
            }
        }
    }

    private async Task LoadTagCompleteCsvAsync(
        string url,
        Action<string, string> add,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return;
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = SplitCsv(line);
            if (parts.Length < 4)
            {
                continue;
            }

            var canonical = parts[0];
            for (var i = 3; i < parts.Length; i++)
            {
                foreach (var alias in parts[i].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!alias.Equals(canonical, StringComparison.OrdinalIgnoreCase))
                    {
                        add(alias, canonical);
                    }
                }
            }
        }
    }

    private async Task LoadCsvAsync(
        string url,
        Action<string, string> add,
        CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (header is null)
        {
            return;
        }

        var cols = header.Split(',');
        var ant = IndexOf(cols, "antecedent_name");
        var cons = IndexOf(cols, "consequent_name");
        var status = IndexOf(cols, "status");
        if (ant < 0 || cons < 0)
        {
            throw new InvalidDataException("Alias CSV is missing antecedent_name/consequent_name.");
        }

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = SplitCsv(line);
            if (status >= 0 && status < parts.Length
                && !parts[status].Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ant >= parts.Length || cons >= parts.Length)
            {
                continue;
            }

            add(parts[ant], parts[cons]);
        }
    }

    private static int IndexOf(string[] cols, string name)
    {
        for (var i = 0; i < cols.Length; i++)
        {
            if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string[] SplitCsv(string line)
    {
        var parts = new List<string>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                parts.Add(Unquote(line[start..i]));
                start = i + 1;
            }
        }

        parts.Add(Unquote(line[start..]));
        return [.. parts];
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            return value[1..^1].Replace("\"\"", "\"");
        }

        return value;
    }

    private sealed class AliasFile
    {
        public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public int ImplicationCount { get; set; }
    }
}
