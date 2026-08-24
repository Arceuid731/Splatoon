using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class PresetVariantComparisonTests
{
    [Fact]
    public void DescribesHumanReadableContentDifferences()
    {
        var recommended = Preset("recommended", 3, 1, ["Cone", "Circle"], ["Cast 100"]);
        var candidate = Preset("candidate", 4, 0, ["Cone", "Line"], ["Cast 100", "Cast 200"]);

        var differences = PresetVariantComparison.DescribeDifferences(candidate, recommended);

        Assert.Contains("+1 element", differences);
        Assert.Contains("-1 trigger", differences);
        Assert.Contains("Adds: Line", differences);
        Assert.Contains("Omits: Circle", differences);
        Assert.Contains("1 mechanic ID difference", differences);
    }

    private static PresetEntry Preset(
        string id,
        int elements,
        int triggers,
        IReadOnlyList<string> names,
        IReadOnlyList<string> identifiers) => new()
    {
        Id = id,
        RepositoryId = id,
        RepositoryName = id,
        Trust = RepositoryTrust.Community,
        Kind = PresetKind.Layout,
        Title = "Mechanic",
        RelativePath = "preset.md",
        SourceUri = "https://example.invalid",
        Content = "content",
        ContentHash = id,
        Summary = new()
        {
            ElementCount = elements,
            TriggerCount = triggers,
            ElementNames = names,
            MechanicIdentifiers = identifiers,
        },
    };
}
