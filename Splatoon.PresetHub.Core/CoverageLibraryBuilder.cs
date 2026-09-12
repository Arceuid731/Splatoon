namespace Splatoon.PresetHub.Core;

/// <summary>Incremental, single-worker compiler. Retains unchanged per-export analysis.</summary>
public sealed class CoverageLibraryBuilder
{
    private readonly Dictionary<string, (string Revision, CoverageAnalysis Analysis)> cache = new(StringComparer.Ordinal);

    public CoverageLibrary Build(IEnumerable<PresetEntry> entries, CancellationToken cancellationToken = default,
        CoverageLibrary? previous = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var presets = entries.Where(x => x.Kind == PresetKind.Layout)
            .SelectMany(x => x.Variants.Count > 0 ? x.Variants : [x])
            .DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var revision = ContentHash.Sha256(string.Join('\n', presets.Select(x => x.Id + ":" + Revision(x))));
        cancellationToken.ThrowIfCancellationRequested();
        // Compare against the persisted library before parsing layouts or solving area plans.
        if(previous?.Version == CoverageLibrary.CurrentVersion && previous.SourceRevision == revision) return previous;
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var contributions = new List<CoverageContribution>();
        var diagnostics = new List<string>();
        foreach(var preset in presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryRevision = Revision(preset);
            if(!cache.TryGetValue(preset.Id, out var saved) || saved.Revision != entryRevision)
            {
                saved = (entryRevision, LayoutCoverageAnalyzer.Analyze(preset));
                cache[preset.Id] = saved;
            }
            retained.Add(preset.Id);
            contributions.AddRange(saved.Analysis.Contributions);
            diagnostics.AddRange(saved.Analysis.Diagnostics);
        }
        foreach(var stale in cache.Keys.Where(x => !retained.Contains(x)).ToArray()) cache.Remove(stale);
        var distinct = contributions.DistinctBy(x => x.Id).OrderBy(x => x.TerritoryId).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var prepared = new Dictionary<uint, IReadOnlyList<string>>();
        foreach(var territory in distinct.GroupBy(x => x.TerritoryId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plan = CoveragePlanner.Compute(territory, territory.Key);
            prepared[territory.Key] = plan.Selected.Select(x => x.Id).ToArray();
            if(!plan.SearchComplete) diagnostics.Add($"Territory {territory.Key}: combination search reached its limit");
        }
        return new()
        {
            ComputedAt = DateTimeOffset.UtcNow,
            SourceRevision = revision,
            Contributions = distinct, PreparedSelections = prepared,
            Diagnostics = diagnostics,
        };
    }

    private static string Revision(PresetEntry preset) =>
        $"{preset.ContentHash}:{preset.ConfidenceScore}:{preset.Compatibility}:{preset.SourceUri}:{preset.LanguageDependent}";
}
