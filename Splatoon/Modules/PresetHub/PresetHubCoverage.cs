using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Splatoon.PresetHub.Core;
using Splatoon.Utility;
using Splatoon.SplatoonScripting;

namespace Splatoon.Modules.PresetHub;

internal sealed partial class PresetHubModule
{
    private readonly CoverageLibraryBuilder coverageBuilder = new();
    private CoverageLibrary coverageLibrary = new();
    private CoveragePreferences coveragePreferences = new();
    private CoverageLibrary? runtimeLibrary;
    private CoveragePreferences? runtimePreferences;
    private uint runtimeTerritory;
    private long nextLocalCheck;
    private string localRevision = "";
    private readonly Dictionary<uint, CoveragePlan> coveragePlans = [];
    private Dictionary<uint, CoverageContribution[]> contributionsByTerritory = [];
    private CoverageLibrary? indexedLibrary;
    private string indexedSources = "";
    private string runtimeSources = "";
    private IReadOnlyList<CoverageContribution> localContributions = [];
    private HashSet<string> overriddenSourceIds = [];
    private HashSet<Layout> replacedLayouts = [];
    private IReadOnlyList<Layout> coverageLayouts = [];
    private Dictionary<string, (string Fingerprint, Layout Layout)> activeCoverage = [];
    private IReadOnlyList<PresetEntry>? scriptCatalogSource;
    private string scriptInstallationsKey = "";
    private Dictionary<uint, PresetEntry[]> scriptsByTerritory = [];
    private PresetEntry[] globalScripts = [];
    internal IReadOnlyCollection<uint> CoverageTerritories { get { IndexCoverageSources(); return contributionsByTerritory.Keys; } }
    internal string CoverageMessage { get; private set; } = "Preparing coverage...";
    internal CoverageLibrary Coverage { get { lock(gate) return coverageLibrary; } }
    internal CoveragePreferences CoveragePreferences => coveragePreferences;
    internal IReadOnlyList<Layout> CoverageLayouts => coverageLayouts;
    internal bool ReplacesLayout(Layout layout) => replacedLayouts.Contains(layout);

    internal bool AllowsScript(string identity, uint territory) =>
        !Installer.Records.Any(x => x.Kind == PresetKind.Script && x.RuntimeIdentity == identity) ||
        coveragePreferences.Enabled && !coveragePreferences.DisabledTerritories.Contains(territory) &&
        !coveragePreferences.DisabledScripts.Contains($"{territory}:{identity}");

    internal void SetScriptEnabled(uint territory, string identity, bool enabled)
    {
        var disabled = coveragePreferences.DisabledScripts.ToHashSet();
        var key = $"{territory}:{identity}";
        if(enabled) disabled.Remove(key); else disabled.Add(key);
        SetCoveragePreferences(coveragePreferences with { DisabledScripts = disabled });
    }

    private void InitializeCoverage()
    {
        coveragePreferences = store.LoadCoveragePreferences();
        coverageLibrary = store.LoadCoverageLibrary() ?? new();
        if(coverageLibrary.SourceRevision.Length > 0) CoverageMessage = DescribeCoverage(coverageLibrary);
    }

    private static string DescribeCoverage(CoverageLibrary library) => library.Contributions.Count == 0
        ? "No drawing aids found in the enabled sources."
        : $"Coverage ready · {library.Contributions.Select(x => x.TerritoryId).Distinct().Count()} areas · updated {library.ComputedAt.LocalDateTime:g}";

    // Called by the repository worker. Only immutable core data crosses threads.
    private void RebuildCoverage()
    {
        PresetEntry[] entries;
        lock(gate)
        {
            var enabled = repositories.Where(x => x.Enabled).Select(x => x.Id).ToHashSet();
            entries = snapshots.Values.Where(x => enabled.Contains(x.Repository.Id)).SelectMany(x => x.Presets).ToArray();
        }
        var compiled = coverageBuilder.Build(entries, lifetime.Token, Coverage);
        lifetime.Token.ThrowIfCancellationRequested();
        lock(gate)
        {
            if(ReferenceEquals(coverageLibrary, compiled))
            {
                CoverageMessage = DescribeCoverage(compiled);
                return;
            }
        }
        store.SaveCoverageLibrary(compiled);
        lock(gate)
        {
            coverageLibrary = compiled;
            CoverageMessage = DescribeCoverage(compiled);
        }
    }

    internal IReadOnlyList<CoverageContribution> ContributionsFor(uint territory)
    {
        IndexCoverageSources();
        return contributionsByTerritory.GetValueOrDefault(territory) ?? [];
    }

    private bool IndexCoverageSources()
    {
        var library = Coverage;
        var enabled = Repositories.Where(x => x.Enabled).Select(x => x.Id).Order().ToArray();
        var sourceKey = string.Join('|', enabled);
        if(ReferenceEquals(indexedLibrary, library) && indexedSources == sourceKey) return false;
        var ids = enabled.ToHashSet();
        contributionsByTerritory = library.Contributions.Where(x => ids.Contains(x.RepositoryId) && !overriddenSourceIds.Contains(x.SourcePresetId))
            .Concat(localContributions).GroupBy(x => x.TerritoryId).ToDictionary(x => x.Key, x => x.ToArray());
        coveragePlans.Clear();
        indexedLibrary = library;
        indexedSources = sourceKey;
        return true;
    }

    internal CoveragePlan CoveragePlanFor(uint territory)
    {
        IndexCoverageSources();
        if(!coveragePlans.TryGetValue(territory, out var plan))
            coveragePlans[territory] = plan = CoveragePlanner.Compute(ContributionsFor(territory), territory, coveragePreferences);
        return plan;
    }

    internal IReadOnlyList<PresetEntry> ScriptSourcesFor(uint territory)
    {
        var catalog = Presets;
        var installationKey = string.Join('|', Installer.Records.Where(x => x.Kind == PresetKind.Script)
            .Select(x => x.PresetId + ":" + x.ContentHash));
        if(!ReferenceEquals(scriptCatalogSource, catalog) || installationKey != scriptInstallationsKey)
        {
            var scripts = Installer.IncludeUnavailableInstallations(catalog.Where(x => x.Kind == PresetKind.Script).ToArray())
                .Where(x => x.Kind == PresetKind.Script).DistinctBy(x => x.RuntimeIdentity).ToArray();
            scriptsByTerritory = scripts.SelectMany(script => script.TerritoryIds.Select(id => (id, script)))
                .GroupBy(x => x.id).ToDictionary(x => x.Key, x => x.Select(y => y.script).ToArray());
            globalScripts = scripts.Where(x => x.TerritoryIds.Count == 0 && Installer.GetStatus(x) != InstallationStatus.NotInstalled).ToArray();
            scriptCatalogSource = catalog;
            scriptInstallationsKey = installationKey;
        }
        return (scriptsByTerritory.GetValueOrDefault(territory) ?? []).Concat(globalScripts).ToArray();
    }

    internal void SetCoveragePreferences(CoveragePreferences preferences)
    {
        store.SaveCoveragePreferences(preferences);
        coveragePreferences = preferences;
        coveragePlans.Clear();
        UpdateCoverageRuntime();
    }

    internal void SetMechanicEnabled(string mechanicId, bool enabled)
    {
        var disabled = coveragePreferences.DisabledMechanics.ToHashSet();
        if(enabled) disabled.Remove(mechanicId); else disabled.Add(mechanicId);
        SetCoveragePreferences(coveragePreferences with { DisabledMechanics = disabled });
    }

    internal void SetActorEnabled(uint territory, string actorKey, bool enabled)
    {
        var disabled = coveragePreferences.DisabledActors.ToHashSet();
        var key = CoveragePreferences.ActorPreferenceKey(territory, actorKey);
        if(enabled) disabled.Remove(key); else disabled.Add(key);
        SetCoveragePreferences(coveragePreferences with { DisabledActors = disabled });
    }

    internal void SetTerritoryEnabled(uint territory, bool enabled)
    {
        var disabled = coveragePreferences.DisabledTerritories.ToHashSet();
        if(enabled) disabled.Remove(territory); else disabled.Add(territory);
        SetCoveragePreferences(coveragePreferences with { DisabledTerritories = disabled });
    }

    private void UpdateCoverageRuntime()
    {
        var managedScripts = Installer.Records.Where(x => x.Kind == PresetKind.Script).Select(x => x.RuntimeIdentity).ToHashSet();
        foreach(var script in ScriptingProcessor.Scripts.Where(x => managedScripts.Contains(x.InternalData.FullName))) script.UpdateState();
        var library = Coverage;
        var territory = (uint)Svc.ClientState.TerritoryType;
        IndexCoverageSources();
        var changed = !ReferenceEquals(runtimeLibrary, library) || runtimeSources != indexedSources || runtimeTerritory != territory;
        if(changed || Environment.TickCount64 >= nextLocalCheck)
        {
            nextLocalCheck = Environment.TickCount64 + 2000;
            var installations = Installer.CoverageInstallations().ToArray();
            var serialized = installations.Select(x => x.Layout.Serialize()).ToArray();
            var revision = ContentHash.Sha256(string.Join('\n', serialized));
            if(changed || revision != localRevision)
            {
                localRevision = revision;
                RefreshLocalCoverage(library, installations, serialized);
                changed = true;
            }
        }
        if(!changed && ReferenceEquals(runtimePreferences, coveragePreferences) && runtimeTerritory == territory) return;
        coveragePlans.Clear();
        var plan = CoveragePlanFor(territory);
        var prepared = new List<Layout>();
        var preparedById = new Dictionary<string, (string, Layout)>();
        try
        {
            foreach(var contribution in plan.Selected)
            {
                var layout = activeCoverage.TryGetValue(contribution.Id, out var previous) && previous.Fingerprint == contribution.Fingerprint
                    ? previous.Layout
                    : JsonConvert.DeserializeObject<Layout>(contribution.LayoutContent[5..])
                      ?? throw new InvalidDataException("An aid could not be prepared.");
                prepared.Add(layout);
                preparedById[contribution.Id] = (contribution.Fingerprint, layout);
            }
            coverageLayouts = prepared;
            activeCoverage = preparedById;
            replacedLayouts = pendingReplacements;
            runtimeLibrary = library;
            runtimePreferences = coveragePreferences;
            runtimeTerritory = territory;
            runtimeSources = indexedSources;
        }
        catch(Exception exception)
        {
            exception.Log();
            CoverageMessage = "Coverage could not be activated. Refresh and try again.";
            // Keep the previous compiled set and its territory restrictions.
        }
    }

    private void RefreshLocalCoverage(CoverageLibrary library,
        (InstallationRecord Record, Layout Layout)[] installations, string[] serialized)
    {
        var replacements = new HashSet<Layout>();
        var local = new List<CoverageContribution>();
        var overridden = new HashSet<string>();
        var migrated = coveragePreferences.MigratedInstallations.ToHashSet();
        var disabled = coveragePreferences.DisabledMechanics.ToHashSet();
        var sourceIds = library.Contributions.Select(x => x.SourcePresetId).ToHashSet();
        for(var i = 0; i < installations.Length; i++)
        {
            var (record, layout) = installations[i];
            if(record.Preset == null) continue;
            var latestExists = sourceIds.Contains(record.Preset.Id) || library.Contributions.Any(x =>
                x.SourceUri == record.SourceUri && x.SourceTitle == record.Preset.Title);
            var original = ReadSourceLayout(record.Preset.Content);
            var modified = original == null || !JToken.DeepEquals(LayoutBehavior(original), LayoutBehavior(layout));
            if(migrated.Add(record.PresetId) && (!layout.Enabled || P.Config.DisabledGroups.Contains(layout.Group)))
                disabled.UnionWith(LayoutCoverageAnalyzer.Analyze(record.Preset).Contributions.SelectMany(x => x.Claims).Select(x => x.MechanicId));
            if(!modified && latestExists)
            {
                replacements.Add(layout);
                continue;
            }
            var repository = new RepositoryDefinition { Id = "local-installations", Owner = "Local", Name = "Saved aids" };
            var entry = new PresetIndexer().Index(repository, [(record.PresetId + ".md", serialized[i])]).FirstOrDefault();
            if(entry == null) continue;
            var analysis = LayoutCoverageAnalyzer.AnalyzeInstalled(entry, Svc.ClientState.TerritoryType);
            if(analysis.Contributions.Count == 0) continue;
            overridden.UnionWith(library.Contributions.Where(x => x.SourcePresetId == record.Preset.Id ||
                x.SourceUri == record.SourceUri && x.SourceTitle == record.Preset.Title).Select(x => x.SourcePresetId));
            local.AddRange(analysis.Contributions.Select(x => x with { Local = true, Detail = modified ? "Local changes" : "Saved source" }));
            replacements.Add(layout);
        }
        localContributions = local;
        overriddenSourceIds = overridden;
        if(!migrated.SetEquals(coveragePreferences.MigratedInstallations))
        {
            var preferences = coveragePreferences with { MigratedInstallations = migrated, DisabledMechanics = disabled };
            store.SaveCoveragePreferences(preferences);
            coveragePreferences = preferences;
        }
        pendingReplacements = replacements;
        indexedLibrary = null;
    }

    private HashSet<Layout> pendingReplacements = [];

    private static Layout? ReadSourceLayout(string content)
    {
        try { return JsonConvert.DeserializeObject<Layout>(content[content.IndexOf('{')..]); }
        catch { return null; }
    }

    private static JObject LayoutBehavior(Layout layout)
    {
        var obj = JObject.Parse(layout.Serialize()[5..]);
        obj.Remove("PresetHubInstallationId");
        // Names/groups are user organization; capture references keep their
        // original layout name in the compiler.
        obj.Remove("Group");
        return obj;
    }
}
