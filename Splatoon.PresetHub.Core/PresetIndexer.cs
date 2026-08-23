using System.Text.Json;
using System.Text.RegularExpressions;

namespace Splatoon.PresetHub.Core;

public sealed partial class PresetIndexer
{
    public IReadOnlyList<PresetEntry> Index(
        RepositoryDefinition repository,
        IEnumerable<(string RelativePath, string Content)> files)
    {
        var presets = new List<PresetEntry>();
        foreach(var (relativePath, content) in files)
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            if(!IsIncluded(repository, normalizedPath)) continue;

            if(normalizedPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                presets.AddRange(IndexMarkdown(repository, normalizedPath, content));
            }
            else if(normalizedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                presets.Add(IndexScript(repository, normalizedPath, content));
            }
        }

        return presets
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Select(x => x.First())
            .OrderBy(x => x.Expansion)
            .ThenBy(x => x.Category)
            .ThenBy(x => x.Duty)
            .ThenBy(x => x.Title)
            .ToArray();
    }

    private static bool IsIncluded(RepositoryDefinition repository, string path) =>
        (repository.PathPrefixes.Count == 0 ||
         repository.PathPrefixes.Any(prefix => path.StartsWith(
             prefix.TrimStart('/').Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))) &&
        !repository.ExcludedPathPrefixes.Any(prefix => path.StartsWith(
            prefix.TrimStart('/').Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<PresetEntry> IndexMarkdown(
        RepositoryDefinition repository,
        string relativePath,
        string content)
    {
        var location = LocationFromPath(relativePath);
        var ordinal = 0;
        foreach(Match match in LayoutLineRegex().Matches(content))
        {
            var payload = "~Lv2~" + match.Groups["json"].Value.Trim();
            var (name, group) = ReadLayoutIdentity(match.Groups["json"].Value, relativePath, ordinal);
            var title = string.IsNullOrWhiteSpace(name) ? location.Duty : name;
            yield return CreateEntry(
                repository,
                relativePath,
                PresetKind.Layout,
                title,
                payload,
                runtimeIdentity: name,
                location with { Duty = string.IsNullOrWhiteSpace(group) ? location.Duty : group },
                ordinal++);
        }
    }

    private static PresetEntry IndexScript(
        RepositoryDefinition repository,
        string relativePath,
        string content)
    {
        var location = LocationFromPath(relativePath);
        var scriptNamespace = NamespaceRegex().Match(content).Groups[1].Value;
        var scriptClass = ScriptClassRegex().Match(content).Groups[1].Value;
        var runtimeIdentity = string.IsNullOrWhiteSpace(scriptClass)
            ? ""
            : $"{(string.IsNullOrWhiteSpace(scriptNamespace) ? "Default" : scriptNamespace)}@{scriptClass}";
        var author = AuthorRegex().Match(content).Groups[1].Value.Trim();
        var title = Path.GetFileNameWithoutExtension(relativePath);

        return CreateEntry(
            repository,
            relativePath,
            PresetKind.Script,
            title,
            content,
            runtimeIdentity,
            location,
            0,
            author);
    }

    private static PresetEntry CreateEntry(
        RepositoryDefinition repository,
        string relativePath,
        PresetKind kind,
        string title,
        string content,
        string runtimeIdentity,
        PresetLocation location,
        int ordinal,
        string author = "")
    {
        var stableSource = $"github:{repository.FullName}:{relativePath}:{kind}:{ordinal}";
        return new()
        {
            Id = ContentHash.Sha256(stableSource),
            RepositoryId = repository.Id,
            RepositoryName = repository.DisplayName.Length == 0 ? repository.FullName : repository.DisplayName,
            Trust = repository.Trust,
            Kind = kind,
            Title = title,
            Author = author,
            Expansion = location.Expansion,
            Category = location.Category,
            Duty = location.Duty,
            RelativePath = relativePath,
            SourceUri = $"https://github.com/{repository.FullName}/blob/{repository.Ref}/{Uri.EscapeDataString(relativePath).Replace("%2F", "/")}",
            Content = content,
            ContentHash = ContentHash.Sha256(content),
            RuntimeIdentity = runtimeIdentity,
        };
    }

    private static (string Name, string Group) ReadLayoutIdentity(string json, string path, int ordinal)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var name = root.TryGetProperty("Name", out var nameProperty) ? nameProperty.GetString() ?? "" : "";
            var group = root.TryGetProperty("Group", out var groupProperty) ? groupProperty.GetString() ?? "" : "";
            return (name, group);
        }
        catch(JsonException)
        {
            return ($"{Path.GetFileNameWithoutExtension(path)} #{ordinal + 1}", "");
        }
    }

    private static PresetLocation LocationFromPath(string relativePath)
    {
        var parts = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rootIndex = Array.FindIndex(parts, part =>
            part.Equals("Presets", StringComparison.OrdinalIgnoreCase) ||
            part.Equals("SplatoonScripts", StringComparison.OrdinalIgnoreCase));
        var start = rootIndex < 0 ? 0 : rootIndex + 1;
        var directories = parts.Skip(start).SkipLast(1).ToArray();
        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        if(rootIndex >= 0 && parts[rootIndex].Equals("SplatoonScripts", StringComparison.OrdinalIgnoreCase))
        {
            var category = directories.ElementAtOrDefault(0) ?? "";
            var expansion = directories.ElementAtOrDefault(1) ?? "";
            var duty = directories.Length > 2 ? directories[^1] : fileName;
            return new(expansion, category, duty);
        }

        return new(directories.ElementAtOrDefault(0) ?? "", directories.ElementAtOrDefault(1) ?? "", fileName);
    }

    private sealed record PresetLocation(string Expansion, string Category, string Duty);

    [GeneratedRegex(@"(?m)^\s*~Lv2~(?<json>\{.*\})\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex LayoutLineRegex();

    [GeneratedRegex(@"\bnamespace\s+([A-Za-z_][A-Za-z0-9_.]*)", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"\bclass\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*SplatoonScript\b", RegexOptions.CultureInvariant)]
    private static partial Regex ScriptClassRegex();

    [GeneratedRegex("new(?:\\s+Metadata)?\\s*\\([^\\)]*?author\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorRegex();
}
