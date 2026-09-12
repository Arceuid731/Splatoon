using System.Text.Json.Nodes;
using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class CoverageTests
{
    private static PresetEntry Preset(string name, string elements, string extra = "") => new PresetIndexer().Index(
        new() { Id = name, Owner = name, Name = "source" },
        [("duty.md", "~Lv2~{\"Name\":\"" + name + "\",\"ZoneLockH\":[387],\"ElementsL\":[" + elements + "]" + extra + "}")]).Single();
    private static string Element(uint cast, string extra = "") =>
        "{\"Name\":\"Attack\",\"type\":1,\"radius\":5,\"refActorComparisonType\":4,\"refActorNPCID\":3014,\"refActorRequireCast\":true,\"refActorCastId\":[" + cast + "]" + extra + "}";

    [Fact]
    public void InactiveAndInvertedFieldsAreNotAdvertisedAsCoverage()
    {
        Assert.Empty(LayoutCoverageAnalyzer.ActiveSignals("{\"refActorCastId\":[3060],\"refActorBuffId\":[420],\"refActorVFXPath\":\"vfx\"}"));
        Assert.Empty(LayoutCoverageAnalyzer.ActiveSignals("{\"refActorRequireCast\":true,\"refActorCastReverse\":true,\"refActorCastId\":[3060]}"));
        Assert.Equal(new CoverageSignal("Action", "3060"), Assert.Single(LayoutCoverageAnalyzer.ActiveSignals(Element(3060))));
    }

    [Fact]
    public void FiveMechanicsReplaceThreeWithoutBlockingAnAdditionalSource()
    {
        var three = Preset("three", string.Join(',', Enumerable.Range(1, 3).Select(x => Element((uint)x))));
        var five = Preset("five", string.Join(',', Enumerable.Range(1, 5).Select(x => Element((uint)x))));
        var sixth = Preset("six", Element(6));
        var contributions = new[] { three, five, sixth }.SelectMany(x => LayoutCoverageAnalyzer.Analyze(x).Contributions).ToArray();
        var plan = CoveragePlanner.Compute(contributions, 387);
        Assert.Equal(6, plan.CoveredAids);
        Assert.Equal(6, plan.Selected.Count);
        Assert.Equal(6, plan.Selected.SelectMany(x => x.Claims).Select(x => x.MechanicId).Distinct().Count());
    }

    [Fact]
    public void SameAttackCanHaveBothAnAreaAndTextReminder()
    {
        var source = Preset("combined", Element(3060) + "," + Element(3060).Replace("\"radius\":5", "\"radius\":0,\"overlayText\":\"Stun\""));
        var plan = CoveragePlanner.Compute(LayoutCoverageAnalyzer.Analyze(source).Contributions, 387);
        Assert.Equal(2, plan.Selected.Count);
        Assert.Single(plan.Selected.SelectMany(x => x.Claims).Select(x => x.MechanicId).Distinct());
    }

    [Fact]
    public void EnglishIsPreferredOutsideEnSetAndEquivalentDrawingsAreNotDuplicated()
    {
        var english = Preset("english", Element(3060, ",\"overlayText\":\"Stun\""));
        var chinese = Preset("chinese", Element(3060, ",\"overlayText\":\"踢晕\""));
        var entries = new[] { chinese, english }.SelectMany(x => LayoutCoverageAnalyzer.Analyze(x).Contributions);
        var plan = CoveragePlanner.Compute(entries, 387);
        Assert.Equal(english.Id, Assert.Single(plan.Selected).SourcePresetId);
    }

    [Fact]
    public void LinkedElementsAndRootPredicatesSurviveTogether()
    {
        var source = Preset("conditional", Element(3060, ",\"Conditional\":true") + "," + Element(3061),
            ",\"DCond\":3,\"JobLockH\":[19]");
        var contribution = Assert.Single(LayoutCoverageAnalyzer.Analyze(source).Contributions);
        Assert.True(contribution.Linked);
        var generated = JsonNode.Parse(contribution.LayoutContent[5..])!;
        Assert.Equal(2, generated["ElementsL"]!.AsArray().Count);
        Assert.Equal(3, generated["DCond"]!.GetValue<int>());
        Assert.Equal(19, generated["JobLockH"]![0]!.GetValue<int>());
        Assert.True(generated["ElementsL"]![0]!["Conditional"]!.GetValue<bool>());
    }

    [Fact]
    public void CastOrListsSplitButReverseTestsRemainIntact()
    {
        var element = Element(3060).Replace("[3060]", "[3060,3061]");
        Assert.Equal(2, LayoutCoverageAnalyzer.Analyze(Preset("or", element)).Contributions.Count);
        Assert.Single(LayoutCoverageAnalyzer.Analyze(Preset("inverse", element.Replace("\"refActorRequireCast\":true", "\"refActorRequireCast\":true,\"refActorCastReverse\":true"))).Contributions);
    }

    [Fact]
    public void StatusOnPlayerDoesNotInheritAnInactiveBossId()
    {
        var source = Preset("prey", "{\"type\":1,\"refActorType\":1,\"refActorNPCID\":3014,\"refActorRequireBuff\":true,\"refActorBuffId\":[420]}");
        var claim = Assert.Single(Assert.Single(LayoutCoverageAnalyzer.Analyze(source).Contributions).Claims);
        Assert.Equal("", claim.ActorKey);
        Assert.Equal(0u, claim.ActorNameId);
        Assert.Equal(new CoverageSignal("Status", "420"), Assert.Single(claim.Signals));
    }

    [Fact]
    public void DisabledSourceAndDisabledMechanicRemainVisibleButAreNotActivated()
    {
        var source = Preset("disabled", Element(3060), ",\"Enabled\":false");
        var contribution = Assert.Single(LayoutCoverageAnalyzer.Analyze(source).Contributions);
        Assert.Empty(CoveragePlanner.Compute([contribution], 387).Selected);
        var active = contribution with { Automatic = true };
        var preferences = new CoveragePreferences { DisabledMechanics = [active.Claims[0].MechanicId] };
        var plan = CoveragePlanner.Compute([active], 387, preferences);
        Assert.Empty(plan.Selected);
        Assert.Single(plan.Alternatives);
    }

    [Fact]
    public void GeometryUpdateRetainsMechanicIdentityAndOptOut()
    {
        var before = Assert.Single(LayoutCoverageAnalyzer.Analyze(Preset("update", Element(3060))).Contributions);
        var after = Assert.Single(LayoutCoverageAnalyzer.Analyze(Preset("update", Element(3060).Replace("\"radius\":5", "\"radius\":8"))).Contributions);
        Assert.Equal(before.Claims[0].MechanicId, after.Claims[0].MechanicId);
        Assert.Equal(before.Claims[0].AidId, after.Claims[0].AidId);
        Assert.NotEqual(before.Fingerprint, after.Fingerprint);
        Assert.False(new CoveragePreferences { DisabledMechanics = [before.Claims[0].MechanicId] }.Allows(after));
    }

    [Fact]
    public void SetPackingFindsBroaderUnionThanLargestPackFirst()
    {
        var seed = Assert.Single(LayoutCoverageAnalyzer.Analyze(Preset("seed", Element(1))).Contributions);
        CoverageContribution Item(string id, params string[] keys) => seed with
        {
            Id = id, Claims = keys.Select(key => seed.Claims[0] with { AidId = key, MechanicId = key }).ToArray(), Linked = true,
        };
        var plan = CoveragePlanner.Compute([Item("largest", "a", "b", "c"), Item("left", "a", "d"), Item("right", "b", "c", "e")], 387);
        Assert.Equal(5, plan.CoveredAids);
        Assert.True(plan.SearchComplete);
        Assert.DoesNotContain(plan.Selected, x => x.Id == "largest");
    }

    [Fact]
    public void ConditionalTailDoesNotPreventMergingIndependentPrefix()
    {
        var source = Preset("tail", Element(1) + "," + Element(2, ",\"Conditional\":true") + "," + Element(3));
        var result = LayoutCoverageAnalyzer.Analyze(source);
        Assert.Equal(2, result.Contributions.Count);
        Assert.Single(result.Contributions, x => x.Linked);
        Assert.Equal(2, result.Contributions.Single(x => x.Linked).ElementCount);
        Assert.Equal(1, result.Contributions.Single(x => !x.Linked).ElementCount);
    }

    [Fact]
    public void HiddenUntilTriggerIsStillAnAvailableAid()
    {
        var source = Preset("trigger", Element(3060), ",\"DCond\":5,\"UseTriggers\":true,\"Triggers\":[{\"Type\":0,\"TimeBegin\":5}]");
        var contribution = Assert.Single(LayoutCoverageAnalyzer.Analyze(source).Contributions);
        Assert.True(contribution.Automatic);
        Assert.True(contribution.Linked);
    }

    [Fact]
    public void CaptureReferencesKeepTheOriginalLayoutAndElementNames()
    {
        var source = Preset("capture", Element(3060, ",\"IsCapturing\":true") + "," +
            Element(3061, ",\"RotationOverrideFaceModePlaceholders\":[\"<element:capture:Attack>\"]"));
        var contribution = Assert.Single(LayoutCoverageAnalyzer.Analyze(source).Contributions);
        Assert.True(contribution.Linked);
        var root = JsonNode.Parse(contribution.LayoutContent[5..])!;
        Assert.Equal("capture", root["Name"]!.GetValue<string>());
        Assert.Equal("Attack", root["ElementsL"]![0]!["Name"]!.GetValue<string>());
    }

    [Fact]
    public void BuilderRemovesDeletedSourcesAndKeepsUnchangedContributions()
    {
        var builder = new CoverageLibraryBuilder();
        var first = Preset("first", Element(1));
        var second = Preset("second", Element(2));
        var before = builder.Build([first, second], TestContext.Current.CancellationToken);
        var after = builder.Build([first], TestContext.Current.CancellationToken);
        Assert.Same(before.Contributions.Single(x => x.SourcePresetId == first.Id), Assert.Single(after.Contributions));
        Assert.NotEqual(before.SourceRevision, after.SourceRevision);
        Assert.Throws<OperationCanceledException>(() => builder.Build([first], new CancellationToken(true)));
    }

    [Fact]
    public void PreferencesAndLibrarySurviveRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "coverage-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PresetHubStore(directory);
            var library = new CoverageLibraryBuilder().Build([Preset("saved", Element(1))], TestContext.Current.CancellationToken);
            var claim = library.Contributions[0].Claims[0];
            store.SaveCoverageLibrary(library);
            store.SaveCoveragePreferences(new() { DisabledMechanics = [claim.MechanicId], DisabledTerritories = [851] });
            var restarted = new PresetHubStore(directory);
            var saved = restarted.LoadCoverageLibrary()!;
            Assert.Equal(library.SourceRevision, saved.SourceRevision);
            Assert.False(restarted.LoadCoveragePreferences().Allows(saved.Contributions[0]));
        }
        finally { Directory.Delete(directory, true); }
    }
}
