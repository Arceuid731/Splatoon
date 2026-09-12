using System.Text.Json;
using Splatoon.PresetHub.Core;

if(args.Length >= 3 && args[0] == "--coverage")
{
    var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
    var snapshots = args.Skip(2).SelectMany(directory => Directory.EnumerateFiles(directory, "*.json"))
        .Select(path => JsonSerializer.Deserialize<RepositorySnapshot>(File.ReadAllText(path), jsonOptions)!)
        .Where(snapshot => snapshot?.Repository != null).ToArray();
    var definitions = RepositoryDefinition.CuratedCatalog();
    var presets = snapshots.SelectMany(snapshot =>
    {
        var definition = definitions.FirstOrDefault(x => x.FullName.Equals(snapshot.Repository.FullName, StringComparison.OrdinalIgnoreCase));
        return definition == null ? Array.Empty<PresetEntry>() : new PresetIndexer().Index(definition,
            snapshot.Presets.GroupBy(x => x.RelativePath).Select(group => (group.Key, string.Join('\n', group.Select(x => x.Content)))));
    }).ToArray();
    var watch = System.Diagnostics.Stopwatch.StartNew();
    var library = new CoverageLibraryBuilder().Build(presets);
    var analysisMs = watch.ElapsedMilliseconds;
    var plans = library.Contributions.Select(x => x.TerritoryId).Distinct().Order().Select(territory =>
    {
        var plan = CoveragePlanner.Compute(library.Contributions, territory);
        var conflicts = plan.Selected.SelectMany(x => x.Claims.Select(c => c.AidId)).GroupBy(x => x).Where(x => x.Count() > 1).Count();
        return new { territory, plan.AvailableAids, plan.CoveredAids, plan.SearchComplete, conflicts,
            active = plan.Selected.Count, options = plan.Alternatives.Count,
            mechanics = plan.Selected.SelectMany(x => x.Claims).Select(x => x.MechanicId).Distinct().Count() };
    }).ToArray();
    var directory = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "coverage-library.json"), JsonSerializer.Serialize(library, jsonOptions));
    File.WriteAllText(Path.Combine(directory, "coverage-audit.json"), JsonSerializer.Serialize(new
    {
        sourceEntries = presets.Length, contributions = library.Contributions.Count, analysisMs,
        totalMs = watch.ElapsedMilliseconds, diagnostics = library.Diagnostics, plans,
    }, jsonOptions));
    Console.WriteLine($"{presets.Length} entries -> {library.Contributions.Count} contributions in {analysisMs}ms; {plans.Length} territories; {plans.Sum(x => x.conflicts)} conflicts; {plans.Count(x => !x.SearchComplete)} bounded searches; {watch.ElapsedMilliseconds}ms total.");
    return plans.Any(x => x.conflicts > 0) ? 1 : 0;
}

if(args.Length < 2)
{
    Console.Error.WriteLine("Usage: PresetHubAudit <output-directory> <existing-cache-directory> [owner/repository ...]");
    return 1;
}
var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
var baseline = Directory.EnumerateFiles(args[1], "*.json")
    .Select(path => JsonSerializer.Deserialize<RepositorySnapshot>(File.ReadAllText(path), options)!)
    .ToArray();
var known = baseline.SelectMany(x => x.Presets).Select(x => x.Fingerprint).ToHashSet();
var territories = baseline.SelectMany(x => x.Presets).SelectMany(x => x.TerritoryIds).ToHashSet();
var output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var baselineChecks = baseline.Select(snapshot =>
{
    var indexer = new PresetIndexer();
    var checks = snapshot.Presets.Select(preset =>
    {
        var indexed = indexer.Index(snapshot.Repository, [(preset.RelativePath, preset.Content)]);
        return new
        {
            preset.Id, preset.Title, preset.RelativePath,
            count = indexed.Count,
            territoryChanged = indexed.Count != 1 || !preset.TerritoryIds.Order().SequenceEqual(indexed[0].TerritoryIds.Order()),
        };
    }).ToArray();
    return new { source = snapshot.Repository.FullName, before = checks.Length,
        after = checks.Sum(x => x.count), changes = checks.Where(x => x.count != 1 || x.territoryChanged).ToArray() };
}).ToArray();
File.WriteAllText(Path.Combine(output, "baseline-validation.json"), JsonSerializer.Serialize(baselineChecks, options));
Console.WriteLine($"Reindexed {baselineChecks.Sum(x => x.before)} cached entries: {baselineChecks.Sum(x => x.after)} retained.");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
var store = new PresetHubStore(output);
var service = new RepositorySyncService(new(http), new(), store);
var summaries = new List<object>();
foreach(var fullName in args.Skip(2))
{
    if(Directory.Exists(fullName))
    {
        var candidateSnapshots = Directory.EnumerateFiles(fullName, "*.json")
            .Select(path => JsonSerializer.Deserialize<RepositorySnapshot>(File.ReadAllText(path), options)!).ToArray();
        var definitions = RepositoryDefinition.CuratedCatalog();
        var entries = baseline.Concat(candidateSnapshots).SelectMany(snapshot =>
        {
            var definition = definitions.FirstOrDefault(x => x.FullName.Equals(snapshot.Repository.FullName, StringComparison.OrdinalIgnoreCase));
            if(definition == null) return Array.Empty<PresetEntry>();
            return new PresetIndexer().Index(definition, snapshot.Presets.GroupBy(x => x.RelativePath)
                .Select(group => (group.Key, string.Join('\n', group.Select(x => x.Content)))));
        }).ToArray();
        var library = new CoverageLibraryBuilder().Build(entries);
        var selections = library.Contributions.GroupBy(x => x.TerritoryId).OrderBy(x => x.Key).Select(group =>
        {
            var plan = CoveragePlanner.Compute(group, group.Key);
            return new { territory = group.Key, plan.AvailableAids, plan.CoveredAids,
                choices = plan.Selected.Select(choice => new { choice.SourceTitle, choice.RepositoryName, choice.SourceUri }).ToArray() };
        }).ToArray();
        File.WriteAllText(Path.Combine(output, "duty-selection.json"), JsonSerializer.Serialize(selections, options));
        Console.WriteLine($"Checked default selection for {selections.Length} territories; E3: {JsonSerializer.Serialize(selections.Single(x => x.territory == 851))}");
        continue;
    }
    var parts = fullName.Split('/');
    if(parts.Length != 2) throw new ArgumentException("Expected owner/repository");
    var repo = RepositoryDefinition.CuratedCatalog().FirstOrDefault(x => x.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase)) ??
        new RepositoryDefinition { Id = fullName.Replace('/', '-').ToLowerInvariant(), Owner = parts[0], Name = parts[1] };
    try
    {
        var snapshot = await service.SyncAsync(repo);
        var presets = snapshot.Presets;
        var summary = new
        {
            source = fullName, snapshot.Revision,
            layouts = presets.Count(x => x.Kind == PresetKind.Layout),
            scripts = presets.Count(x => x.Kind == PresetKind.Script && x.RuntimeIdentity.Length > 0),
            newFingerprints = presets.Where(x => !known.Contains(x.Fingerprint)).Select(x => x.Fingerprint).Distinct().Count(),
            newTerritories = presets.SelectMany(x => x.TerritoryIds).Except(territories).Order().ToArray(),
            territories = presets.SelectMany(x => x.TerritoryIds).Distinct().Order().ToArray(),
            eden = presets.Where(x => x.TerritoryIds.Any(id => id is 851 or 855) ||
                System.Text.RegularExpressions.Regex.IsMatch(x.Title + " " + x.RelativePath, @"(?i)inundation|\be3[ns]?\b|leviathan"))
                .Select(x => new { x.Title, x.TerritoryIds, x.SourceUri }).ToArray(),
        };
        summaries.Add(summary);
        Console.WriteLine($"{fullName}: {summary.layouts} layouts, {summary.scripts} scripts, {summary.newFingerprints} new fingerprints, {summary.newTerritories.Length} additional territory IDs; E3 matches: {summary.eden.Length}.");
    }
    catch(Exception exception)
    {
        summaries.Add(new { source = fullName, error = exception.Message });
        Console.WriteLine($"FAILED {fullName}: {exception.Message}");
    }
}
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summaries, options));
return 0;
