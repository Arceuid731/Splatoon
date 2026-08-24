using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Splatoon.PresetHub.Core;

public sealed partial class PresetIndexer
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".yaml", ".yml", ".cfg", ".conf", ".preset", "",
    };

    public IReadOnlyList<PresetEntry> Index(
        RepositoryDefinition repository,
        IEnumerable<(string RelativePath, string Content)> files)
    {
        var presets = new List<PresetEntry>();
        foreach(var (relativePath, content) in files)
        {
            var normalizedPath = relativePath.Replace('\\', '/');
            if(!IsIncluded(repository, normalizedPath)) continue;

            var extension = Path.GetExtension(normalizedPath);
            if(extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
            {
                presets.Add(IndexScript(repository, normalizedPath, content));
            }
            else if(TextExtensions.Contains(extension))
            {
                presets.AddRange(IndexLayouts(repository, normalizedPath, content));
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

    private static IEnumerable<PresetEntry> IndexLayouts(
        RepositoryDefinition repository,
        string relativePath,
        string content)
    {
        var location = LocationFromPath(relativePath);
        var parsed = new List<ParsedLayout>();

        var cursor = 0;
        while((cursor = content.IndexOf("~Lv2~", cursor, StringComparison.Ordinal)) >= 0)
        {
            var marker = cursor;
            var brace = SkipWhitespace(content, marker + 5);
            cursor = marker + 5;
            if(brace >= content.Length || content[brace] != '{' ||
               !TryReadObject(content, brace, out var root, out var json, out var end)) continue;

            if(IsLayout(root)) parsed.Add(new(marker, PresetFormat.ModernLayout, root, json, ""));
            cursor = end;
        }

        foreach(Match match in LegacyLayoutStartRegex().Matches(content))
        {
            var marker = match.Index + match.Length - 1;
            if(marker >= 4 && content.AsSpan(marker - 4, 5).SequenceEqual("~Lv2~")) continue;
            var brace = SkipWhitespace(content, marker + 1);
            if(brace >= content.Length || content[brace] != '{' ||
               !TryReadObject(content, brace, out var root, out var json, out _)) continue;
            if(!IsLayout(root)) continue;

            var externalName = match.Groups["name"].Value.Trim().TrimStart('-', '*').Trim();
            if(externalName.Length == 0) continue;
            parsed.Add(new(match.Index, PresetFormat.LegacyLayout, root, json, externalName));
        }

        var ordinal = 0;
        foreach(var layout in parsed.OrderBy(x => x.Position))
        {
            var identity = ReadLayoutIdentity(layout.Root, relativePath, ordinal, layout.ExternalName);
            var title = string.IsNullOrWhiteSpace(identity.Name) ? location.Duty : identity.Name;
            var payload = layout.Format == PresetFormat.ModernLayout
                ? "~Lv2~" + layout.Json
                : $"{identity.Name}~{layout.Json}";
            var fingerprint = LayoutFingerprint(layout.Root, identity.Name);
            var languageDependent = IsLanguageDependent(layout.Root);
            var familyId = BuildFamilyId(PresetKind.Layout, identity.TerritoryIds, title);
            var summary = SummarizeLayout(layout.Root);
            var (score, notes) = Score(repository, PresetKind.Layout, layout.Format,
                PresetCompatibility.Compatible, identity.TerritoryIds.Count > 0, languageDependent);

            yield return CreateEntry(
                repository,
                relativePath,
                PresetKind.Layout,
                title,
                payload,
                runtimeIdentity: identity.Name,
                location with { Duty = string.IsNullOrWhiteSpace(identity.Group) ? location.Duty : identity.Group },
                ordinal++,
                territoryIds: identity.TerritoryIds,
                format: layout.Format,
                fingerprint: fingerprint,
                familyId: familyId,
                confidenceScore: score,
                confidenceNotes: notes,
                languageDependent: languageDependent,
                summary: summary);
        }
    }

    private static PresetEntry IndexScript(
        RepositoryDefinition repository,
        string relativePath,
        string content)
    {
        var location = LocationFromPath(relativePath);
        var tree = CSharpSyntaxTree.ParseText(content);
        var root = tree.GetRoot();
        var scriptClass = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(x => x.BaseList?.Types.Any(type =>
                type.Type.ToString().EndsWith("SplatoonScript", StringComparison.Ordinal)) == true);
        var scriptNamespace = scriptClass?.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString() ?? "";
        var className = scriptClass?.Identifier.ValueText ?? "";
        var runtimeIdentity = className.Length == 0
            ? ""
            : $"{(scriptNamespace.Length == 0 ? "Default" : scriptNamespace)}@{className}";
        var author = AuthorRegex().Match(content).Groups[1].Value.Trim();
        var title = Path.GetFileNameWithoutExtension(relativePath);
        var territoryIds = ReadScriptTerritories(root);
        var (compatibility, compatibilityDetail) = ScriptCompatibility(repository, content, scriptClass != null);
        var fingerprint = ContentHash.Sha256(content.Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
        var familyId = BuildFamilyId(PresetKind.Script, territoryIds, title);
        var (score, notes) = Score(repository, PresetKind.Script, PresetFormat.NativeScript,
            compatibility, territoryIds.Count > 0, false);

        return CreateEntry(
            repository,
            relativePath,
            PresetKind.Script,
            title,
            content,
            runtimeIdentity,
            location,
            0,
            author,
            territoryIds,
            PresetFormat.NativeScript,
            compatibility,
            compatibilityDetail,
            fingerprint,
            familyId,
            score,
            notes);
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
        string author = "",
        IReadOnlyList<uint>? territoryIds = null,
        PresetFormat format = PresetFormat.ModernLayout,
        PresetCompatibility compatibility = PresetCompatibility.Compatible,
        string compatibilityDetail = "",
        string fingerprint = "",
        string familyId = "",
        int confidenceScore = 0,
        IReadOnlyList<string>? confidenceNotes = null,
        bool languageDependent = false,
        PresetContentSummary? summary = null)
    {
        var stableSource = $"github:{repository.FullName}:{relativePath}:{kind}:{ordinal}";
        var id = ContentHash.Sha256(stableSource);
        var repositoryName = repository.DisplayName.Length == 0 ? repository.FullName : repository.DisplayName;
        var sourceUri = $"https://github.com/{repository.FullName}/blob/{repository.Ref}/{Uri.EscapeDataString(relativePath).Replace("%2F", "/")}";
        return new()
        {
            Id = id,
            RepositoryId = repository.Id,
            RepositoryName = repositoryName,
            Trust = repository.Trust,
            Kind = kind,
            Title = title,
            Author = author,
            Expansion = location.Expansion,
            Category = location.Category,
            Duty = location.Duty,
            TerritoryIds = territoryIds ?? [],
            RelativePath = relativePath,
            SourceUri = sourceUri,
            Content = content,
            ContentHash = ContentHash.Sha256(content),
            RuntimeIdentity = runtimeIdentity,
            Format = format,
            Compatibility = compatibility,
            CompatibilityDetail = compatibilityDetail,
            Fingerprint = fingerprint,
            FamilyId = familyId,
            ConfidenceScore = confidenceScore,
            LanguageDependent = languageDependent,
            ConfidenceNotes = confidenceNotes ?? [],
            Sources = [new(id, repository.Id, repositoryName, sourceUri, repository.Trust, repository.Role, repository.SourcePriority)],
            Summary = summary ?? new(),
        };
    }

    private static (string Name, string Group, IReadOnlyList<uint> TerritoryIds) ReadLayoutIdentity(
        JsonElement root,
        string path,
        int ordinal,
        string externalName)
    {
        var name = externalName.Length > 0
            ? externalName
            : root.TryGetProperty("Name", out var nameProperty) ? nameProperty.GetString() ?? "" : "";
        var group = root.TryGetProperty("Group", out var groupProperty) ? groupProperty.GetString() ?? "" : "";
        var territoryIds = root.TryGetProperty("ZoneLockH", out var zoneLock) && zoneLock.ValueKind == JsonValueKind.Array
            ? zoneLock.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number && x.TryGetUInt32(out _))
                .Select(x => x.GetUInt32())
                .Distinct()
                .ToArray()
            : [];
        if(name.Length == 0) name = $"{Path.GetFileNameWithoutExtension(path)} #{ordinal + 1}";
        return (name, group, territoryIds);
    }

    private static IReadOnlyList<uint> ReadScriptTerritories(Microsoft.CodeAnalysis.SyntaxNode root)
    {
        var property = root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .FirstOrDefault(x => x.Identifier.ValueText == "ValidTerritories");
        if(property == null) return [];
        return property.DescendantNodes().OfType<LiteralExpressionSyntax>()
            .Select(x => x.Token.Value switch
            {
                uint value => value,
                int value when value > 0 => (uint)value,
                long value when value is > 0 and <= uint.MaxValue => (uint)value,
                _ => 0u,
            })
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
    }

    private static (PresetCompatibility Status, string Detail) ScriptCompatibility(
        RepositoryDefinition repository,
        string content,
        bool hasScriptClass)
    {
        if(!hasScriptClass) return (PresetCompatibility.Incompatible, "No SplatoonScript class was found.");
        if(!repository.AllowScriptInstallation)
            return (PresetCompatibility.Incompatible, "Script installation is disabled for this source.");
        if(content.Contains("using Splatoon.Utils", StringComparison.Ordinal))
            return (PresetCompatibility.Incompatible, "Uses the removed Splatoon.Utils namespace.");
        return repository.Trust == RepositoryTrust.Official
            ? (PresetCompatibility.Compatible, "Maintained with the current official Splatoon source.")
            : (PresetCompatibility.NeedsReview, "Community scripts require source review and an in-game compilation check.");
    }

    private static bool IsLayout(JsonElement root) => root.ValueKind == JsonValueKind.Object &&
        (root.TryGetProperty("ElementsL", out _) || root.TryGetProperty("Elements", out _) ||
         root.TryGetProperty("ZoneLockH", out _) || root.TryGetProperty("Name", out _));

    private static bool TryReadObject(
        string content,
        int start,
        out JsonElement root,
        out string json,
        out int end)
    {
        root = default;
        json = "";
        end = start + 1;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(content[start..]);
            var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
            using var document = JsonDocument.ParseValue(ref reader);
            if(document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var consumed = Encoding.UTF8.GetString(bytes.AsSpan(0, checked((int)reader.BytesConsumed))).Length;
            end = start + consumed;
            json = content[start..end];
            root = document.RootElement.Clone();
            return true;
        }
        catch(JsonException)
        {
            return false;
        }
    }

    private static string LayoutFingerprint(JsonElement root, string name)
    {
        using var stream = new MemoryStream();
        using(var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("$name", name);
            foreach(var property in root.EnumerateObject().Where(x => x.Name is not "Name" and not "Group").OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return ContentHash.Sha256(Encoding.UTF8.GetString(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch(element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach(var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach(var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static bool IsLanguageDependent(JsonElement root)
    {
        if(root.ValueKind == JsonValueKind.Object)
        {
            var hasActorName = root.TryGetProperty("refActorName", out var actorName) &&
                               actorName.ValueKind == JsonValueKind.String &&
                               actorName.GetString() is { Length: > 0 } value && value != "*";
            var hasIntlActor = root.TryGetProperty("refActorNameIntl", out var actorIntl) && actorIntl.ValueKind == JsonValueKind.Object;
            var hasActorId = new[] { "refActorNPCNameID", "refActorNPCID", "refActorDataID", "refActorModelID" }
                .Any(key => root.TryGetProperty(key, out var id) && id.ValueKind is JsonValueKind.Number or JsonValueKind.Array);
            if(hasActorName && !hasIntlActor && !hasActorId) return true;

            var hasMatch = root.TryGetProperty("Match", out var match) && match.ValueKind == JsonValueKind.String &&
                           !string.IsNullOrWhiteSpace(match.GetString());
            var hasIntlMatch = root.TryGetProperty("MatchIntl", out var matchIntl) && matchIntl.ValueKind == JsonValueKind.Object;
            if(hasMatch && !hasIntlMatch) return true;

            return root.EnumerateObject().Any(x => IsLanguageDependent(x.Value));
        }
        return root.ValueKind == JsonValueKind.Array && root.EnumerateArray().Any(IsLanguageDependent);
    }

    private static PresetContentSummary SummarizeLayout(JsonElement root)
    {
        var elements = root.TryGetProperty("ElementsL", out var modern) && modern.ValueKind == JsonValueKind.Array
            ? modern.EnumerateArray().ToArray()
            : root.TryGetProperty("Elements", out var legacy) && legacy.ValueKind == JsonValueKind.Object
                ? legacy.EnumerateObject().Select(x => x.Value).ToArray()
                : [];
        var triggers = root.TryGetProperty("Triggers", out var triggerArray) && triggerArray.ValueKind == JsonValueKind.Array
            ? triggerArray.GetArrayLength()
            : 0;
        var names = elements
            .Select(x => x.TryGetProperty("Name", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        CollectMechanicIdentifiers(root, identifiers);
        return new()
        {
            ElementCount = elements.Length,
            TriggerCount = triggers,
            ElementNames = names,
            MechanicIdentifiers = identifiers.Order(StringComparer.Ordinal).ToArray(),
        };
    }

    private static void CollectMechanicIdentifiers(JsonElement node, HashSet<string> result)
    {
        if(node.ValueKind == JsonValueKind.Object)
        {
            foreach(var property in node.EnumerateObject())
            {
                var prefix = property.Name switch
                {
                    "refActorCastId" => "Cast",
                    "refActorNPCNameID" => "NPC name",
                    "refActorNPCID" => "NPC",
                    "refActorBuffId" => "Status",
                    "refActorDataID" => "Data",
                    "refActorVFXPath" => "VFX",
                    _ => "",
                };
                if(prefix.Length > 0)
                {
                    if(property.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach(var value in property.Value.EnumerateArray()) AddIdentifier(prefix, value, result);
                    }
                    else
                    {
                        AddIdentifier(prefix, property.Value, result);
                    }
                }
                CollectMechanicIdentifiers(property.Value, result);
            }
        }
        else if(node.ValueKind == JsonValueKind.Array)
        {
            foreach(var value in node.EnumerateArray()) CollectMechanicIdentifiers(value, result);
        }
    }

    private static void AddIdentifier(string prefix, JsonElement value, HashSet<string> result)
    {
        if(value.ValueKind == JsonValueKind.Number) result.Add($"{prefix} {value.GetRawText()}");
        else if(value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            result.Add($"{prefix} {value.GetString()}");
    }

    private static string BuildFamilyId(PresetKind kind, IReadOnlyList<uint> territories, string title)
    {
        var normalizedTitle = new string(title.Normalize(NormalizationForm.FormKC).ToLowerInvariant()
            .Where(char.IsLetterOrDigit).ToArray());
        return ContentHash.Sha256($"{kind}:{string.Join(',', territories.Order())}:{normalizedTitle}");
    }

    private static (int Score, IReadOnlyList<string> Notes) Score(
        RepositoryDefinition repository,
        PresetKind kind,
        PresetFormat format,
        PresetCompatibility compatibility,
        bool hasTerritory,
        bool languageDependent)
    {
        var score = 30;
        var notes = new List<string>();
        if(repository.Role == RepositoryRole.Original) { score += 15; notes.Add("Original source"); }
        else if(repository.Role == RepositoryRole.Aggregator) { score -= 5; notes.Add("Aggregator source"); }
        if(repository.Trust == RepositoryTrust.Official) { score += 15; notes.Add("Official source"); }
        else if(repository.Trust == RepositoryTrust.Community) { score += 8; notes.Add("Curated community source"); }
        if(hasTerritory) { score += 20; notes.Add("Exact territory ID"); }
        if(format == PresetFormat.ModernLayout) { score += 10; notes.Add("Modern layout format"); }
        if(format == PresetFormat.LegacyLayout) { score -= 10; notes.Add("Legacy layout format"); }
        if(!languageDependent && kind == PresetKind.Layout) { score += 10; notes.Add("Language-independent matching"); }
        if(languageDependent) { score -= 15; notes.Add("Potentially language-dependent"); }
        if(compatibility == PresetCompatibility.NeedsReview) { score -= 10; notes.Add("Compatibility needs review"); }
        if(compatibility == PresetCompatibility.Incompatible) { score -= 50; notes.Add("Known incompatible"); }
        return (Math.Clamp(score, 0, 100), notes);
    }

    private static int SkipWhitespace(string value, int position)
    {
        while(position < value.Length && char.IsWhiteSpace(value[position])) position++;
        return position;
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
        var expansion = directories.Select(ExpansionFromSegment).FirstOrDefault(x => x.Length > 0) ?? "";
        var category = directories.Select(CategoryFromSegment).FirstOrDefault(x => x.Length > 0) ?? "";
        if(rootIndex >= 0 && parts[rootIndex].Equals("SplatoonScripts", StringComparison.OrdinalIgnoreCase))
        {
            var duty = directories.Length > 2 ? directories[^1] : fileName;
            return new(expansion.Length > 0 ? expansion : directories.ElementAtOrDefault(1) ?? "",
                category.Length > 0 ? category : directories.ElementAtOrDefault(0) ?? "", duty);
        }

        return new(expansion, category, fileName);
    }

    private static string ExpansionFromSegment(string segment) => segment.Trim().ToLowerInvariant() switch
    {
        "arr" or "a realm reborn" => "A Realm Reborn",
        "hw" or "heavensward" => "Heavensward",
        "sb" or "stormblood" => "Stormblood",
        "shb" or "shadowbringer" or "shadowbringers" => "Shadowbringers",
        "ew" or "endwalker" => "Endwalker",
        "dt" or "dawntrail" => "Dawntrail",
        _ => "",
    };

    private static string CategoryFromSegment(string segment)
    {
        var normalized = segment.Trim().ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        if(normalized.Contains("deep dungeon", StringComparison.Ordinal)) return "Deep Dungeons";
        if(normalized.Contains("variant", StringComparison.Ordinal) || normalized.Contains("criterion", StringComparison.Ordinal))
            return "Variant Dungeons";
        if(normalized.Contains("dungeon", StringComparison.Ordinal)) return "Dungeons";
        if(normalized.Contains("alliance", StringComparison.Ordinal)) return "Alliance Raids";
        if(normalized.Contains("raid", StringComparison.Ordinal) || normalized.Contains("savage", StringComparison.Ordinal) ||
           normalized.Contains("ultimate", StringComparison.Ordinal)) return "Raids";
        if(normalized.Contains("trial", StringComparison.Ordinal) || normalized.Contains("extreme", StringComparison.Ordinal) ||
           normalized.Contains("unreal", StringComparison.Ordinal)) return "Trials";
        if(normalized.Contains("hunt", StringComparison.Ordinal)) return "The Hunt";
        if(normalized.Contains("eureka", StringComparison.Ordinal) || normalized.Contains("bozja", StringComparison.Ordinal) ||
           normalized.Contains("field operation", StringComparison.Ordinal)) return "Field Operations";
        if(normalized.Contains("pvp", StringComparison.Ordinal)) return "PvP";
        if(normalized is "general" or "generic") return "General";
        if(normalized is "duties") return "Duties";
        return "";
    }

    private sealed record PresetLocation(string Expansion, string Category, string Duty);
    private sealed record ParsedLayout(int Position, PresetFormat Format, JsonElement Root, string Json, string ExternalName);

    [GeneratedRegex(@"(?m)^(?<name>[^\r\n`~]{1,200})~(?=\s*\{)", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyLayoutStartRegex();

    [GeneratedRegex("new(?:\\s+Metadata)?\\s*\\([^\\)]*?author\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorRegex();
}
