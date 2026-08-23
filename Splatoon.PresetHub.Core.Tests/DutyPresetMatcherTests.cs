using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class DutyPresetMatcherTests
{
    [Fact]
    public void ReturnsOnlyActionablePresetsForCurrentTerritory()
    {
        var matchingLayout = Preset("layout", PresetKind.Layout, 837);
        var installedScript = Preset("installed", PresetKind.Script, 837);
        var otherDuty = Preset("other", PresetKind.Layout, 1199);

        var result = DutyPresetMatcher.FindSuggestions(
            [otherDuty, installedScript, matchingLayout],
            837,
            preset => preset == installedScript ? InstallationStatus.Installed : InstallationStatus.NotInstalled);

        Assert.Equal([matchingLayout], result);
    }

    private static PresetEntry Preset(string id, PresetKind kind, params uint[] territories) => new()
    {
        Id = id,
        RepositoryId = "repo",
        RepositoryName = "Repo",
        Trust = RepositoryTrust.Official,
        Kind = kind,
        Title = id,
        RelativePath = $"{id}.md",
        SourceUri = "https://example.invalid",
        Content = "content",
        ContentHash = id,
        TerritoryIds = territories,
    };
}
