namespace Splatoon.PresetHub.Core;

using System.Text.Json;

public static class PresetVariantComparison
{
    public static string ContentSummary(PresetEntry preset)
    {
        if(preset.Kind == PresetKind.Script) return "Native C# script";
        var parts = new List<string> { Plural(preset.Summary.ElementCount, "element") };
        if(preset.Summary.TriggerCount > 0) parts.Add(Plural(preset.Summary.TriggerCount, "trigger"));
        if(preset.Summary.MechanicIdentifiers.Count > 0)
            parts.Add(Plural(preset.Summary.MechanicIdentifiers.Count, "mechanic ID"));
        return string.Join(" · ", parts);
    }

    public static IReadOnlyList<string> DescribeDifferences(PresetEntry candidate, PresetEntry recommended)
    {
        if(candidate.Id == recommended.Id) return ["Recommended version"];
        if(candidate.Kind == PresetKind.Script || recommended.Kind == PresetKind.Script)
            return ["Different source code; review the exact script before installing"];

        var differences = new List<string>();
        var elementDelta = candidate.Summary.ElementCount - recommended.Summary.ElementCount;
        if(elementDelta != 0)
            differences.Add($"{(elementDelta > 0 ? "+" : "")}{elementDelta} element{(Math.Abs(elementDelta) == 1 ? "" : "s")}");
        var triggerDelta = candidate.Summary.TriggerCount - recommended.Summary.TriggerCount;
        if(triggerDelta != 0)
            differences.Add($"{(triggerDelta > 0 ? "+" : "")}{triggerDelta} trigger{(Math.Abs(triggerDelta) == 1 ? "" : "s")}");

        var added = candidate.Summary.ElementNames.Except(recommended.Summary.ElementNames, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        var removed = recommended.Summary.ElementNames.Except(candidate.Summary.ElementNames, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if(added.Length > 0) differences.Add($"Adds: {string.Join(", ", added)}");
        if(removed.Length > 0) differences.Add($"Omits: {string.Join(", ", removed)}");

        var identifierChanges = candidate.Summary.MechanicIdentifiers
            .SymmetricDifference(recommended.Summary.MechanicIdentifiers, StringComparer.Ordinal).Count;
        if(identifierChanges > 0) differences.Add($"{identifierChanges} mechanic ID difference{(identifierChanges == 1 ? "" : "s")}");
        if(candidate.LanguageDependent != recommended.LanguageDependent)
            differences.Add(candidate.LanguageDependent ? "Potentially language-dependent" : "Language-independent matching");
        differences.AddRange(DescribeValueDifferences(candidate, recommended));
        return differences.Count == 0 ? ["Internal values differ"] : differences.Distinct().ToArray();
    }

    private static IEnumerable<string> DescribeValueDifferences(PresetEntry candidate, PresetEntry recommended)
    {
        var candidateValues = FlattenPayload(candidate.Content);
        var recommendedValues = FlattenPayload(recommended.Content);
        if(candidateValues.Count == 0 || recommendedValues.Count == 0) yield break;

        var changed = candidateValues.Keys.Union(recommendedValues.Keys, StringComparer.Ordinal)
            .Where(key => !candidateValues.GetValueOrDefault(key, "<missing>")
                .Equals(recommendedValues.GetValueOrDefault(key, "<missing>"), StringComparison.Ordinal))
            .Select(key => new
            {
                Key = key,
                Candidate = candidateValues.GetValueOrDefault(key, "<missing>"),
                Recommended = recommendedValues.GetValueOrDefault(key, "<missing>"),
                Category = DifferenceCategory(key),
            })
            .ToArray();

        foreach(var category in changed.GroupBy(x => x.Category).OrderBy(x => x.Key))
        {
            var examples = category.Where(x => IsUsefulExample(x.Key))
                .Select(x => FormatExample(x.Key, x.Recommended, x.Candidate))
                .Distinct(StringComparer.Ordinal)
                .Take(2)
                .ToArray();
            var detail = examples.Length == 0 ? "" : $" ({string.Join("; ", examples)})";
            yield return $"{category.Key}: {category.Count()} differing value{(category.Count() == 1 ? "" : "s")}{detail}";
        }
    }

    private static Dictionary<string, string> FlattenPayload(string content)
    {
        var brace = content.IndexOf('{');
        if(brace < 0) return [];
        try
        {
            using var document = JsonDocument.Parse(content[brace..]);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(document.RootElement, "", result);
            return result;
        }
        catch(JsonException)
        {
            return [];
        }
    }

    private static void Flatten(JsonElement node, string path, IDictionary<string, string> result)
    {
        if(node.ValueKind == JsonValueKind.Object)
        {
            foreach(var property in node.EnumerateObject())
            {
                if(path.Length == 0 && property.Name is "Name" or "Group") continue;
                Flatten(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}", result);
            }
        }
        else if(node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach(var value in node.EnumerateArray()) Flatten(value, $"{path}[{index++}]", result);
        }
        else
        {
            result[path] = node.GetRawText();
        }
    }

    private static string DifferenceCategory(string path)
    {
        var lower = path.ToLowerInvariant();
        if(lower.Contains("color", StringComparison.Ordinal) || lower.Contains("overlay", StringComparison.Ordinal) ||
           lower.Contains("filled", StringComparison.Ordinal)) return "Visual styling";
        if(lower.Contains("trigger", StringComparison.Ordinal) || lower.Contains("refactor", StringComparison.Ordinal) ||
           lower.Contains("only", StringComparison.Ordinal) || lower.Contains("dcond", StringComparison.Ordinal) ||
           lower.Contains("phase", StringComparison.Ordinal) || lower.Contains("display", StringComparison.Ordinal) ||
           lower.Contains("zonelock", StringComparison.Ordinal)) return "Activation/matching";
        return "Geometry/behavior";
    }

    private static bool IsUsefulExample(string path)
    {
        var name = LeafName(path).ToLowerInvariant();
        return name is "radius" or "donut" or "refx" or "refy" or "offx" or "offy" or "thicc" or
            "coneanglemin" or "coneanglemax" or "additionalrotation" or "duration" or "type";
    }

    private static string FormatExample(string path, string recommended, string candidate) =>
        $"{LeafName(path)} {TrimValue(recommended)} → {TrimValue(candidate)}";

    private static string LeafName(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path[(dot + 1)..];
    }

    private static string TrimValue(string value) => value.Length <= 24 ? value : value[..21] + "...";

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static IReadOnlyCollection<T> SymmetricDifference<T>(
        this IEnumerable<T> left,
        IEnumerable<T> right,
        IEqualityComparer<T> comparer)
    {
        var result = left.ToHashSet(comparer);
        result.SymmetricExceptWith(right);
        return result;
    }
}
