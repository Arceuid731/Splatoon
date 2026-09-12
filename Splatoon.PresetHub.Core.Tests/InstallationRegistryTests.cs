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

        Assert.Equal(17, repositories.Count);
        Assert.Equal("punishxiv-splatoon", repositories[0].Id);
        Assert.Equal(13, repositories.Count(x => x.Enabled));
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

    [Fact]
    public void RecognizesAnInstalledExactDuplicateThroughItsSourceAlias()
    {
        var registry = new InstallationRegistry(new PresetHubStore(directory));
        var mirror = CreatePreset("raw-a") with { Id = "mirror", Fingerprint = "canonical" };
        registry.MarkInstalled(mirror, "Layout");
        var preferred = CreatePreset("raw-b") with
        {
            Id = "original",
            Fingerprint = "canonical",
            AlternatePresetIds = ["mirror"],
        };

        Assert.Equal(InstallationStatus.Installed, registry.GetStatus(preferred));
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
