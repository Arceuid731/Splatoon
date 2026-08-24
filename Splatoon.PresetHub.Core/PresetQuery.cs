namespace Splatoon.PresetHub.Core;

public enum PresetSortColumn
{
    Title,
    Kind,
    Content,
    Repository,
    Status,
}

public sealed record PresetQueryOptions
{
    public string Search { get; init; } = "";
    public PresetKind? Kind { get; init; }
    public InstallationStatus? Status { get; init; }
    public string Expansion { get; init; } = "";
    public string Category { get; init; } = "";
    public string Duty { get; init; } = "";
    public bool InstalledOnly { get; init; }
    public PresetSortColumn SortColumn { get; init; } = PresetSortColumn.Content;
    public bool SortAscending { get; init; } = true;
}

public static class PresetQuery
{
    private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static IReadOnlyList<PresetEntry> Apply(
        IEnumerable<PresetEntry> presets,
        Func<PresetEntry, InstallationStatus> getStatus,
        PresetQueryOptions options)
    {
        var filtered = presets.Where(preset => Matches(preset, getStatus(preset), options));
        return Sort(filtered, getStatus, options.SortColumn, options.SortAscending).ToArray();
    }

    private static bool Matches(PresetEntry preset, InstallationStatus status, PresetQueryOptions options)
    {
        if(options.InstalledOnly && status == InstallationStatus.NotInstalled) return false;
        if(options.Kind != null && preset.Kind != options.Kind) return false;
        if(options.Status != null && status != options.Status) return false;
        if(options.Expansion.Length > 0 && !Comparer.Equals(preset.Expansion, options.Expansion)) return false;
        if(options.Category.Length > 0 && !Comparer.Equals(preset.Category, options.Category)) return false;
        if(options.Duty.Length > 0 && !Comparer.Equals(preset.Duty, options.Duty)) return false;
        if(options.Search.Length == 0) return true;

        var values = new[] { preset.Title, preset.Author, preset.Duty, preset.Category, preset.Expansion, preset.RepositoryName }
            .Concat(preset.Variants.SelectMany(x => new[] { x.RepositoryName, x.RelativePath, x.Author }));
        return values
            .Any(value => value.Contains(options.Search, StringComparison.OrdinalIgnoreCase));
    }

    private static IOrderedEnumerable<PresetEntry> Sort(
        IEnumerable<PresetEntry> presets,
        Func<PresetEntry, InstallationStatus> getStatus,
        PresetSortColumn column,
        bool ascending)
    {
        IOrderedEnumerable<PresetEntry> ordered = column switch
        {
            PresetSortColumn.Title => Order(presets, preset => preset.Title, ascending),
            PresetSortColumn.Kind => Order(presets, preset => preset.Kind.ToString(), ascending),
            PresetSortColumn.Repository => Order(presets, preset => preset.RepositoryName, ascending),
            PresetSortColumn.Status => Order(presets, preset => getStatus(preset).ToString(), ascending),
            _ => Order(presets, ContentKey, ascending),
        };

        return ascending
            ? ordered.ThenBy(preset => preset.Title, Comparer).ThenBy(preset => preset.Id, Comparer)
            : ordered.ThenByDescending(preset => preset.Title, Comparer).ThenByDescending(preset => preset.Id, Comparer);
    }

    private static IOrderedEnumerable<PresetEntry> Order(
        IEnumerable<PresetEntry> presets,
        Func<PresetEntry, string> keySelector,
        bool ascending) => ascending
            ? presets.OrderBy(keySelector, Comparer)
            : presets.OrderByDescending(keySelector, Comparer);

    private static string ContentKey(PresetEntry preset) =>
        $"{preset.Expansion}\u001f{preset.Category}\u001f{preset.Duty}";
}
