namespace Splatoon.PresetHub.Core;

/// <summary>Incremental, single-worker compiler. Retains unchanged per-export analysis.</summary>
public sealed class CoverageLibraryBuilder
{
    private readonly Dictionary<string, (string Revision, CoverageAnalysis Analysis)> cache = new(StringComparer.Ordinal);

    public CoverageLibrary Build(IEnumerable<PresetEntry> entries, CancellationToken cancellationToken = default)
    {
        var presets = entries.Where(x => x.Kind == PresetKind.Layout)
            .SelectMany(x => x.Variants.Count > 0 ? x.Variants : [x])
            .DistinctBy(x => x.Id).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var retained = new HashSet<string>(StringComparer.Ordinal);
        var contributions = new List<CoverageContribution>();
        var diagnostics = new List<string>();
        foreach(var preset in presets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var revision = $"{preset.ContentHash}:{preset.ConfidenceScore}:{preset.Compatibility}:{preset.SourceUri}:{preset.LanguageDependent}";
            if(!cache.TryGetValue(preset.Id, out var saved) || saved.Revision != revision)
            {
                saved = (revision, LayoutCoverageAnalyzer.Analyze(preset));
                cache[preset.Id] = saved;
            }
            retained.Add(preset.Id);
            contributions.AddRange(saved.Analysis.Contributions);
            diagnostics.AddRange(saved.Analysis.Diagnostics);
        }
        foreach(var stale in cache.Keys.Where(x => !retained.Contains(x)).ToArray()) cache.Remove(stale);
        return new()
        {
            ComputedAt = DateTimeOffset.UtcNow,
            SourceRevision = ContentHash.Sha256(string.Join('\n', presets.Select(x => x.Id + ":" + cache[x.Id].Revision))),
            Contributions = contributions.DistinctBy(x => x.Id).OrderBy(x => x.TerritoryId).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray(),
            Diagnostics = diagnostics,
        };
    }
}
