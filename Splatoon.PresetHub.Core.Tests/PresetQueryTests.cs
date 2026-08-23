using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class PresetQueryTests
{
    [Fact]
    public void FiltersByDutyAndSortsTitlesAscending()
    {
        var presets = new[]
        {
            Create("z", "Tender Valley"),
            Create("Eliminator", "Alexandria"),
            Create("Amalgam", "Alexandria"),
        };

        var result = PresetQuery.Apply(
            presets,
            _ => InstallationStatus.NotInstalled,
            new()
            {
                Duty = "Alexandria",
                SortColumn = PresetSortColumn.Title,
            });

        Assert.Equal(["Amalgam", "Eliminator"], result.Select(x => x.Title));
    }

    [Fact]
    public void SortsContentDescendingAndUsesTitleAsTieBreaker()
    {
        var presets = new[]
        {
            Create("Amalgam", "Alexandria"),
            Create("Barreltender", "Tender Valley"),
            Create("Anthracite", "Tender Valley"),
        };

        var result = PresetQuery.Apply(
            presets,
            _ => InstallationStatus.NotInstalled,
            new()
            {
                SortColumn = PresetSortColumn.Content,
                SortAscending = false,
            });

        Assert.Equal(["Barreltender", "Anthracite", "Amalgam"], result.Select(x => x.Title));
    }

    private static PresetEntry Create(string title, string duty) => new()
    {
        Id = $"{duty}-{title}",
        RepositoryId = "repo",
        RepositoryName = "Repo",
        Trust = RepositoryTrust.Official,
        Kind = PresetKind.Layout,
        Title = title,
        Expansion = "Dawntrail",
        Category = "Dungeons",
        Duty = duty,
        RelativePath = $"{duty}.md",
        SourceUri = "https://example.invalid",
        Content = "layout",
        ContentHash = "hash",
    };
}
