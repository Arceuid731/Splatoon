using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class DutyLayoutSelectionTests
{
    private static PresetEntry Preset(string title, params uint[] casts) => new PresetIndexer().Index(
        new() { Id = title, Owner = title, Name = "repo" },
        [("preset.md", "~Lv2~" + System.Text.Json.JsonSerializer.Serialize(new
        {
            Name = title, ZoneLockH = new[] { 851 },
            ElementsL = new[] { new { refActorNPCID = 8486, refActorCastId = casts } },
        }))]).Single();

    [Fact]
    public void OverlappingAlternativesWithDifferentNamesAreNotBothSelected()
    {
        var first = Preset("first", 16326);
        var translated = Preset("translated", 16326);
        Assert.Single(DutyLayoutSelection.Recommend([first, translated], []));
    }

    [Fact]
    public void ComplementaryMechanicsForTheSameBossRemainSelected()
    {
        Assert.Equal(2, DutyLayoutSelection.Recommend([Preset("tankbuster", 16326), Preset("quake", 16337)], []).Count);
    }

    [Fact]
    public void BroaderPackIsPreferredToMultipleOverlappingFragments()
    {
        var pack = Preset("pack", 16326, 16337, 16339);
        var selection = DutyLayoutSelection.Recommend([Preset("tankbuster", 16326), Preset("quake", 16337), pack], []);
        Assert.Equal(pack.Id, Assert.Single(selection).Key);
    }

    [Fact]
    public void ExistingInstallationIsAccountedFor()
    {
        var selection = DutyLayoutSelection.Recommend([Preset("duplicate", 16326), Preset("new", 16337)], [Preset("installed", 16326)]);
        Assert.Equal(Preset("new", 16337).Id, Assert.Single(selection).Key);
    }

    [Fact]
    public void EnglishEditionWinsOverTranslatedCopyAndScriptsAreExcluded()
    {
        var english = Preset("english", 16326) with { RelativePath = "[EN Set]/Normal Raids.md" };
        var other = Preset("other", 16326, 16337);
        var script = Preset("script", 1234) with { Kind = PresetKind.Script };
        Assert.Equal(english.Id, Assert.Single(DutyLayoutSelection.Recommend([other, english, script], [])).Key);
    }

    [Fact]
    public void CanChooseNonOverlappingAlternativeWithinFamily()
    {
        var covered = Preset("covered", 16326);
        var newChoice = Preset("new", 16337);
        var family = covered with { Variants = [covered, newChoice] };
        Assert.Equal(newChoice.Id, Assert.Single(DutyLayoutSelection.Recommend([family], [Preset("installed", 16326)])).Value);
    }

    [Fact]
    public void ManualMarkersWithoutMechanicIdsRemainAvailable()
    {
        Assert.Equal(2, DutyLayoutSelection.Recommend([Preset("north"), Preset("south")], []).Count);
    }
}
