using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class InstallationRegistryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PresetHubTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReportsInstalledAndUpdateAvailableFromContentHash()
    {
        var store = new PresetHubStore(directory);
        var registry = new InstallationRegistry(store);
        var preset = CreatePreset("hash-a");

        Assert.Equal(InstallationStatus.NotInstalled, registry.GetStatus(preset));
        registry.MarkInstalled(preset, "Example@Script");
        Assert.Equal(InstallationStatus.Installed, registry.GetStatus(preset));
        Assert.Equal(InstallationStatus.UpdateAvailable, registry.GetStatus(preset with { ContentHash = "hash-b" }));

        var reloaded = new InstallationRegistry(store);
        Assert.Equal(InstallationStatus.Installed, reloaded.GetStatus(preset));
    }

    [Fact]
    public void FallsBackToOfficialRepositoryWhenConfigurationIsCorrupt()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "repositories.json"), "not json");

        var repositories = new PresetHubStore(directory).LoadRepositories();

        Assert.Equal("punishxiv-splatoon", Assert.Single(repositories).Id);
    }

    public void Dispose()
    {
        if(Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public void PersistsDutyPromptPreferences()
    {
        var store = new PresetHubStore(directory);
        store.SaveDutyPromptPreferences(new()
        {
            Enabled = false,
            SuppressedTerritoryIds = new HashSet<uint> { 837, 1199 },
        });

        var loaded = store.LoadDutyPromptPreferences();

        Assert.False(loaded.Enabled);
        Assert.Equal([837u, 1199u], loaded.SuppressedTerritoryIds.Order());
    }

    private static PresetEntry CreatePreset(string hash) => new()
    {
        Id = "preset",
        RepositoryId = "repo",
        RepositoryName = "Repo",
        Trust = RepositoryTrust.Official,
        Kind = PresetKind.Script,
        Title = "Script",
        RelativePath = "Script.cs",
        SourceUri = "https://example.invalid/Script.cs",
        Content = "source",
        ContentHash = hash,
    };
}
