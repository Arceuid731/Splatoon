namespace Splatoon.PresetHub.Core;

public static class DutyPresetMatcher
{
    public static IReadOnlyList<PresetEntry> FindSuggestions(
        IEnumerable<PresetEntry> presets,
        uint territoryId,
        Func<PresetEntry, InstallationStatus> getStatus) =>
        presets
            .Where(x => x.TerritoryIds.Contains(territoryId))
            .Where(x => getStatus(x) != InstallationStatus.Installed)
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
