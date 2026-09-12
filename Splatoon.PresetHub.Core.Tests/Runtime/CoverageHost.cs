using Splatoon.PresetHub.Core;

namespace Splatoon.Modules.PresetHub;

// Compile the production runtime adapter against a minimal deterministic host.
internal sealed partial class PresetHubModule
{
    private readonly object gate = new();
    private readonly PresetHubStore store;
    private readonly List<RepositoryDefinition> repositories = [];
    private readonly Dictionary<string, RepositorySnapshot> snapshots = [];
    private readonly CancellationTokenSource lifetime = new();
    internal PresetHubInstaller Installer { get; }
    internal IReadOnlyList<RepositoryDefinition> Repositories => repositories;
    internal IReadOnlyList<PresetEntry> Presets { get; private set; } = [];

    internal PresetHubModule(PresetHubStore store, InstallationRegistry registry, params PresetEntry[] sources)
    {
        this.store = store;
        Installer = new(registry);
        InitializeCoverage();
        TestSetSources(sources);
    }

    internal void TestTick() => UpdateCoverageRuntime();
    internal void TestExpireLocalCache() => nextLocalCheck = 0;
    internal void TestSetSources(params PresetEntry[] entries)
    {
        Presets = entries;
        repositories.Clear();
        repositories.AddRange(entries.DistinctBy(x => x.RepositoryId).Select(x => new RepositoryDefinition
            { Id = x.RepositoryId, Owner = x.RepositoryName, Name = "test" }));
        coverageLibrary = coverageBuilder.Build(entries);
    }
    internal void TestSetLibrary(CoverageLibrary library) => coverageLibrary = library;
    internal void TestEnableSource(string id, bool enabled)
    {
        var index = repositories.FindIndex(x => x.Id == id);
        repositories[index] = repositories[index] with { Enabled = enabled };
    }
}
