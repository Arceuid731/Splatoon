using Newtonsoft.Json;
using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests.Runtime;

[Collection("Plugin runtime")]
public sealed class CoverageRuntimeTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "coverage-runtime-" + Guid.NewGuid().ToString("N"));
    private readonly PresetHubStore store;
    private readonly InstallationRegistry registry;
    public CoverageRuntimeTests()
    {
        P.Config = new();
        Svc.ClientState.TerritoryType = 387;
        store = new(directory);
        registry = new(store);
    }
    public void Dispose() => Directory.Delete(directory, true);

    private static PresetEntry Preset(string source, uint action = 3060, float radius = 5) => new PresetIndexer().Index(
        new() { Id = source, Owner = source, Name = "source" },
        [("test.md", "~Lv2~" + System.Text.Json.JsonSerializer.Serialize(new
        {
            Name = "Boss", ZoneLockH = new[] { 387 }, ElementsL = new[] { new
                { type = 1, radius, refActorComparisonType = 4, refActorNPCID = 3014, refActorRequireCast = true, refActorCastId = new[] { action } } },
        }))]).Single();

    private Layout Install(PresetEntry preset)
    {
        var layout = JsonConvert.DeserializeObject<Layout>(preset.Content[5..])!;
        layout.PresetHubInstallationId = "owned";
        P.Config.LayoutsL.Add(layout);
        registry.MarkInstalled(preset, "hub:owned");
        return layout;
    }

    [Fact]
    public void SavedCoverageIsReadyBeforeNetworkRefreshAndUnchangedRebuildPreservesIt()
    {
        var source = Preset("cached");
        store.SaveCoverageLibrary(new CoverageLibraryBuilder().Build([source], TestContext.Current.CancellationToken));
        var hub = new PresetHubModule(store, registry, source);
        hub.TestReloadCoverage();
        var loaded = hub.Coverage;
        Assert.StartsWith("Coverage ready", hub.CoverageMessage);
        hub.TestTick();
        Assert.Single(hub.CoverageLayouts);
        hub.TestRebuildCoverage(source);
        Assert.Same(loaded, hub.Coverage);
        Assert.StartsWith("Coverage ready", hub.CoverageMessage);
    }

    [Fact]
    public void DisablingASourceTakesEffectEvenIfUiReadTheNewIndexFirst()
    {
        var source = Preset("remote");
        var hub = new PresetHubModule(store, registry, source);
        hub.TestTick();
        Assert.Single(hub.CoverageLayouts);
        hub.TestEnableSource(source.RepositoryId, false);
        Assert.Empty(hub.ContributionsFor(387));
        hub.TestTick();
        Assert.Empty(hub.CoverageLayouts);
    }

    [Fact]
    public void ExistingManagedLayoutIsReplacedInRuntimeWithoutDeletingItsBackup()
    {
        var old = Preset("remote");
        var saved = Install(old);
        var hub = new PresetHubModule(store, registry, Preset("remote", radius: 8));
        hub.TestTick();
        Assert.True(hub.ReplacesLayout(saved));
        Assert.Contains(saved, P.Config.LayoutsL);
        Assert.Equal(8, (float)Assert.Single(hub.CoverageLayouts).ElementsL[0]["radius"]!);
    }

    [Fact]
    public void LocalGeometryChangesSurviveRemoteUpdate()
    {
        var old = Preset("remote");
        var saved = Install(old);
        saved.ElementsL[0]["radius"] = 12;
        var hub = new PresetHubModule(store, registry, Preset("remote", radius: 8));
        hub.TestTick();
        Assert.Equal(12, (float)Assert.Single(hub.CoverageLayouts).ElementsL[0]["radius"]!);
        Assert.True(Assert.Single(hub.CoveragePlanFor(387).Selected).Local);
    }

    [Fact]
    public void OldDisabledGroupBecomesPersistedMechanicOptOut()
    {
        var old = Preset("remote");
        var saved = Install(old);
        saved.Group = "disabled";
        P.Config.DisabledGroups.Add(saved.Group);
        var hub = new PresetHubModule(store, registry, old);
        hub.TestTick();
        Assert.Empty(hub.CoverageLayouts);
        Assert.Single(store.LoadCoveragePreferences().DisabledMechanics);
        var mechanic = store.LoadCoveragePreferences().DisabledMechanics.Single();
        hub.SetMechanicEnabled(mechanic, true);
        hub.TestExpireLocalCache();
        hub.TestTick();
        Assert.Single(hub.CoverageLayouts);
    }

    [Fact]
    public void FailedPreparationLeavesPreviousRenderingAndOwnershipTogether()
    {
        var source = Preset("remote");
        var saved = Install(source);
        var hub = new PresetHubModule(store, registry, source);
        hub.TestTick();
        var previous = Assert.Single(hub.CoverageLayouts);
        var broken = hub.Coverage.Contributions[0] with { LayoutContent = "~Lv2~{broken", Fingerprint = "broken" };
        hub.TestSetLibrary(hub.Coverage with { Contributions = [broken] });
        hub.TestTick();
        Assert.Same(previous, Assert.Single(hub.CoverageLayouts));
        Assert.True(hub.ReplacesLayout(saved));
        Assert.Contains("could not", hub.CoverageMessage);
    }

    [Fact]
    public void TogglingOneMechanicKeepsOtherRuntimeObjectsAndTheirTriggerState()
    {
        var hub = new PresetHubModule(store, registry, Preset("one", 1), Preset("two", 2));
        hub.TestTick();
        var existing = hub.CoverageLayouts.ToArray();
        var claim = hub.ContributionsFor(387).First().Claims[0];
        hub.SetMechanicEnabled(claim.MechanicId, false);
        Assert.Contains(Assert.Single(hub.CoverageLayouts), existing);
        hub.SetTerritoryEnabled(387, false);
        Assert.Empty(hub.CoverageLayouts);
    }

    [Fact]
    public void DisablingLibraryDoesNotReactivateTheLegacyBackup()
    {
        var source = Preset("remote");
        var saved = Install(source);
        var hub = new PresetHubModule(store, registry, source);
        hub.TestTick();
        hub.SetCoveragePreferences(hub.CoveragePreferences with { Enabled = false });
        Assert.Empty(hub.CoverageLayouts);
        Assert.True(hub.ReplacesLayout(saved));
    }

    [Fact]
    public void ManagedScriptOptOutIsPerTerritoryAndLeavesUnmanagedScriptsAlone()
    {
        var script = Preset("remote") with { Kind = PresetKind.Script, RuntimeIdentity = "Test@Script" };
        registry.MarkInstalled(script, script.RuntimeIdentity);
        var hub = new PresetHubModule(store, registry);
        hub.SetScriptEnabled(387, script.RuntimeIdentity, false);
        Assert.False(hub.AllowsScript(script.RuntimeIdentity, 387));
        Assert.True(hub.AllowsScript(script.RuntimeIdentity, 851));
        Assert.True(hub.AllowsScript("Manual@Script", 387));
    }
}
