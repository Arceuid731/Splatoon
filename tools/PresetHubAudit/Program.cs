using System.Text.Json;
using Splatoon.PresetHub.Core;

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
        var families = PresetCatalog.Build(entries);
        var selections = families.SelectMany(x => x.TerritoryIds).Distinct().Order().Select(territory =>
        {
            var suggestions = DutyPresetMatcher.FindSuggestions(families, territory, _ => InstallationStatus.NotInstalled);
            var selected = DutyLayoutSelection.Recommend(suggestions, []);
            return new { territory, suggestions = suggestions.Count, selected = selected.Count,
                choices = suggestions.Where(x => selected.ContainsKey(x.Id)).Select(family =>
                {
                    var choice = (family.Variants.Count > 0 ? family.Variants : [family]).Single(x => x.Id == selected[family.Id]);
                    return new { choice.Title, choice.RepositoryName, choice.RelativePath };
                }).ToArray() };
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
