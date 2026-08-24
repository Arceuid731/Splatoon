using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class PresetCatalogTests
{
    [Fact]
    public void CollapsesExactMirrorsAndPrefersOriginalSource()
    {
        var aggregator = Preset("aggregator", "same", "family", 40, RepositoryRole.Aggregator);
        var original = Preset("original", "same", "family", 100, RepositoryRole.Original);

        var result = Assert.Single(PresetCatalog.Build([aggregator, original]));

        Assert.Equal("original", result.RepositoryId);
        Assert.Equal(2, result.Sources.Count);
        Assert.Contains("aggregator-id", result.AlternatePresetIds);
        Assert.Single(result.Variants);
    }

    [Fact]
    public void KeepsDifferentVersionsInTheSameFamily()
    {
        var first = Preset("first", "hash-a", "family", 100, RepositoryRole.Original);
        var second = Preset("second", "hash-b", "family", 90, RepositoryRole.Original);

        var result = Assert.Single(PresetCatalog.Build([first, second]));

        Assert.Equal(2, result.VariantCount);
        Assert.Equal(2, result.Variants.Count);
        Assert.Equal("first", result.RepositoryId);
    }

    [Fact]
    public void RecommendsTheHighestConfidenceCompatibleVersion()
    {
        var original = Preset("original", "hash-a", "family", 100, RepositoryRole.Original) with { ConfidenceScore = 70 };
        var official = Preset("official", "hash-b", "family", 90, RepositoryRole.Original) with { ConfidenceScore = 95 };

        var family = Assert.Single(PresetCatalog.Build([original, official]));

        Assert.Equal("official", family.RepositoryId);
        Assert.Equal(["official", "original"], family.Variants.Select(x => x.RepositoryId));
    }

    private static PresetEntry Preset(
        string repository,
        string fingerprint,
        string family,
        int priority,
        RepositoryRole role) => new()
    {
        Id = repository + "-id",
        RepositoryId = repository,
        RepositoryName = repository,
        Trust = RepositoryTrust.Community,
        Kind = PresetKind.Layout,
        Title = "Mechanic",
        RelativePath = "preset.md",
        SourceUri = "https://example.invalid/" + repository,
        Content = "content",
        ContentHash = fingerprint,
        Fingerprint = fingerprint,
        FamilyId = family,
        Sources = [new(repository + "-id", repository, repository, "https://example.invalid/" + repository,
            RepositoryTrust.Community, role, priority)],
    };
}
