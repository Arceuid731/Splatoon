namespace Splatoon.PresetHub.Core;

public static class PresetCatalog
{
    public static IReadOnlyList<PresetEntry> Build(IEnumerable<PresetEntry> presets) => Create(presets).Families;

    public static PresetCatalogResult Create(IEnumerable<PresetEntry> presets)
    {
        var sourceEntries = presets.ToArray();
        var deduplicated = sourceEntries
            .GroupBy(ExactKey, StringComparer.Ordinal)
            .Select(CollapseExactDuplicates)
            .ToArray();

        var families = deduplicated
            .GroupBy(FamilyKey, StringComparer.Ordinal)
            .Select(BuildFamily)
            .OrderBy(x => x.Expansion, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Duty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(x => x.ConfidenceScore)
            .ToArray();

        return new(families, sourceEntries.Length - deduplicated.Length, deduplicated.Length - families.Length,
            deduplicated.Length);
    }

    private static PresetEntry BuildFamily(IGrouping<string, PresetEntry> group)
    {
        var choices = group
            .OrderBy(x => x.Compatibility)
            .ThenByDescending(x => x.ConfidenceScore)
            .ThenByDescending(SourcePriority)
            .ThenBy(x => x.RepositoryName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x with { VariantCount = group.Count(), Variants = [] })
            .ToArray();
        return choices[0] with { VariantCount = choices.Length, Variants = choices };
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
            Variants = [],
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

public sealed record PresetCatalogResult(
    IReadOnlyList<PresetEntry> Families,
    int ExactDuplicatesCollapsed,
    int VariantChoicesGrouped,
    int DistinctChoices);
