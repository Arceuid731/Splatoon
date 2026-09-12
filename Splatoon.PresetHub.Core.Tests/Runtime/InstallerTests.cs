using Splatoon.Modules.PresetHub;
using Splatoon.PresetHub.Core;
using Splatoon.SplatoonScripting;

namespace Splatoon.PresetHub.Core.Tests.Runtime;

public sealed class InstallerTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PresetHubTests", Guid.NewGuid().ToString("N"));
    private readonly InstallationRegistry registry;
    private readonly PresetHubInstaller installer;
    public InstallerTests()
    {
        P.Config = new();
        ScriptingProcessor.Scripts.Clear();
        registry = new(new(directory));
        installer = new(registry);
    }
    private static PresetEntry Layout(string name) => new PresetIndexer().Index(
        new() { Id = "repo", Owner = "owner", Name = "repo" },
        [("preset.md", "~Lv2~{\"Name\":\"" + name + "\",\"ZoneLockH\":[837]}")]).Single();
    private static PresetEntry Script() => new PresetIndexer().Index(
        new() { Id = "repo", Owner = "owner", Name = "repo" },
        [("S.cs", "class S : SplatoonScript { }")]).Single();

    [Fact]
    public void FailedReplacementRestoresExactLayoutObjectsAndOrder()
    {
        var original = Layout("original");
        Assert.True(installer.InstallLayout(original, out _));
        P.Config.LayoutsL.Insert(0, new() { Name = "manual" });
        var before = P.Config.LayoutsL.ToArray();
        var rejected = Layout("reject");
        var family = original with { Variants = [original, rejected] };
        Assert.False(installer.InstallLayoutChoice(family, rejected, out _));
        Assert.Equal(before, P.Config.LayoutsL);
        Assert.Equal(original.Id, Assert.Single(registry.Records).PresetId);
    }

    [Fact]
    public void NameCollisionNeverClaimsManualLayout()
    {
        var manual = new Splatoon.Layout { Name = "existing" };
        P.Config.LayoutsL.Add(manual);
        Assert.False(installer.InstallLayout(Layout("existing"), out _));
        Assert.Same(manual, Assert.Single(P.Config.LayoutsL));
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void RenamingManagedLayoutPreservesOwnershipAndUninstallProtectsSameNameManualLayout()
    {
        var preset = Layout("original");
        Assert.True(installer.InstallLayout(preset, out _));
        P.Config.LayoutsL[0].Name = "renamed";
        var manual = new Splatoon.Layout { Name = "original" };
        P.Config.LayoutsL.Add(manual);
        Assert.True(installer.Uninstall(preset, out _));
        Assert.Same(manual, Assert.Single(P.Config.LayoutsL));
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void RemovingLayoutManuallyMakesItAvailableAgain()
    {
        var preset = Layout("original");
        Assert.True(installer.InstallLayout(preset, out _));
        P.Config.LayoutsL.Clear();
        Assert.Equal(InstallationStatus.NotInstalled, installer.GetStatus(preset));
    }

    [Fact]
    public void RegistryWriteFailureRollsBackImportedLayout()
    {
        Directory.CreateDirectory(Path.Combine(directory, "installations.json"));
        Assert.False(installer.InstallLayout(Layout("original"), out _));
        Assert.Empty(P.Config.LayoutsL);
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void BatchReportsPartialFailureAndKeepsSuccessfulInstalls()
    {
        var good = Layout("good");
        var bad = Layout("reject");
        var result = installer.InstallLayoutChoices([new(good, good), new(bad, bad)]);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Equal("good", Assert.Single(P.Config.LayoutsL).Name);
    }

    [Fact]
    public void ScriptIsRecordedOnlyAfterSuccessfulLoadingAndDuplicateQueueIsBlocked()
    {
        var preset = Script();
        var report = new ScriptSecurityAnalyzer().Analyze(preset.Content);
        Assert.True(installer.InstallReviewedScript(preset, report, out _));
        Assert.Empty(registry.Records);
        Assert.False(installer.InstallReviewedScript(preset, report, out _));
        ScriptingProcessor.Loaded!(preset.RuntimeIdentity);
        ScriptingProcessor.Finished!();
        Assert.Equal(InstallationStatus.Installed, installer.GetStatus(preset));
    }

    [Fact]
    public void CompilationFailureLeavesNoRecordAndCanBeRetried()
    {
        var preset = Script();
        var report = new ScriptSecurityAnalyzer().Analyze(preset.Content);
        Assert.True(installer.InstallReviewedScript(preset, report, out _));
        ScriptingProcessor.Finished!();
        Assert.Empty(registry.Records);
        Assert.True(installer.InstallReviewedScript(preset, report, out _));
    }

    [Fact]
    public void HashMismatchAndManualScriptCollisionAreRejected()
    {
        var preset = Script();
        var report = new ScriptSecurityAnalyzer().Analyze(preset.Content);
        Assert.False(installer.InstallReviewedScript(preset with { Content = "changed" }, report, out _));
        ScriptingProcessor.Scripts.Add(new() { InternalData = new() { FullName = preset.RuntimeIdentity } });
        Assert.False(installer.InstallReviewedScript(preset, report, out _));
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void DisposedInstallerDoesNotWriteLateScriptCompletion()
    {
        var preset = Script();
        Assert.True(installer.InstallReviewedScript(preset, new ScriptSecurityAnalyzer().Analyze(preset.Content), out _));
        installer.Dispose();
        ScriptingProcessor.Loaded!(preset.RuntimeIdentity);
        Assert.Empty(registry.Records);
    }

    public void Dispose() { installer.Dispose(); if(Directory.Exists(directory)) Directory.Delete(directory, true); }
}
