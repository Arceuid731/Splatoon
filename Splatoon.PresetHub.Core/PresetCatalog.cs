namespace Splatoon.PresetHub.Core;

public static class PresetCatalog
{
    public static IReadOnlyList<PresetEntry> Build(IEnumerable<PresetEntry> presets)
    {
        var deduplicated = presets
            .GroupBy(ExactKey, StringComparer.Ordinal)
            .Select(CollapseExactDuplicates)
            .ToArray();

        var variantCounts = deduplicated
            .GroupBy(FamilyKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        return deduplicated
            .Select(preset => preset with { VariantCount = variantCounts[FamilyKey(preset)] })
            .OrderBy(x => x.Expansion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Duty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.ConfidenceScore)
            .ToArray();
    }

    private static PresetEntry CollapseExactDuplicates(IGrouping<string, PresetEntry> group)
    {
        var representative = group
            .OrderByDescending(SourcePriority)
            .ThenBy(x => x.Compatibility)
            .ThenByDescending(x => x.ConfidenceScore)
            .ThenBy(x => x.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .First();
        var sources = group
            .SelectMany(SourceReferences)
            .GroupBy(x => x.RepositoryId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderByDescending(source => source.Priority).First())
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var alternateIds = group.SelectMany(x => x.AlternatePresetIds.Append(x.Id))
            .Where(x => x != representative.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return representative with
        {
            Sources = sources,
            AlternatePresetIds = alternateIds,
        };
    }

    private static IEnumerable<PresetSourceReference> SourceReferences(PresetEntry preset) =>
        preset.Sources.Count > 0
            ? preset.Sources
            : [new(preset.Id, preset.RepositoryId, preset.RepositoryName, preset.SourceUri,
                preset.Trust, RepositoryRole.Original, 50)];

    private static int SourcePriority(PresetEntry preset) =>
        SourceReferences(preset).Max(x => x.Priority);

    private static string ExactKey(PresetEntry preset) =>
        $"{preset.Kind}:{(preset.Fingerprint.Length > 0 ? preset.Fingerprint : preset.ContentHash)}";

    private static string FamilyKey(PresetEntry preset) =>
        preset.FamilyId.Length > 0 ? preset.FamilyId : preset.Id;
}
