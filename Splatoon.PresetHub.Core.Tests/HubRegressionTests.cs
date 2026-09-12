using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class HubRegressionTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PresetHubTests", Guid.NewGuid().ToString("N"));
    private static readonly RepositoryDefinition Repository = new() { Id = "repo", Owner = "owner", Name = "repo" };
    private static IReadOnlyList<PresetEntry> Index(string content, string path = "preset.md") =>
        new PresetIndexer().Index(Repository, [(path, content)]);

    [Fact]
    public void InsertingAndReorderingExportsDoesNotChangeExistingIdentities()
    {
        const string a = "~Lv2~{\"Name\":\"A\",\"ZoneLockH\":[837]}";
        const string b = "~Lv2~{\"Name\":\"B\",\"ZoneLockH\":[837]}";
        var before = Index(a + "\n" + b);
        var after = Index("~Lv2~{\"Name\":\"New\"}\n" + b + "\n" + a);
        Assert.All(before, preset => Assert.Equal(preset.Id, after.Single(x => x.Title == preset.Title).Id));
        Assert.Equal(before[0].Id, Index(a.Replace("837]", "837],\"Enabled\":false")).Single().Id);
    }

    [Fact]
    public void SameNameVariantsKeepIdentityWhenReordered()
    {
        const string first = "~Lv2~{\"Name\":\"Boss\",\"ZoneLockH\":[837],\"Enabled\":true}";
        const string second = "~Lv2~{\"Name\":\"Boss\",\"ZoneLockH\":[837],\"Enabled\":false}";
        var before = Index(first + "\n" + second);
        var after = Index(second + "\n" + first);
        Assert.All(before, entry => Assert.Equal(entry.Id, after.Single(x => x.Fingerprint == entry.Fingerprint).Id));
    }

    [Fact]
    public void ReadsInlineLegacyExportInsideMarkdownBackticks()
    {
        Assert.Equal("Boss", Assert.Single(Index("`Boss~{\"ZoneLockH\":[837],\"Elements\":{}}`")).Title);
    }

    [Theory]
    [InlineData("~Lv2~{\n\"Name\":\"Boss\",\n\"ZoneLockH\":[837]\n}")]
    [InlineData("Boss~{\n\"ZoneLockH\":[837],\n\"Elements\":{}\n}")]
    public void MultilineLayoutsBecomeSingleLineImportPayloads(string content)
    {
        var preset = Assert.Single(Index(content));
        Assert.DoesNotContain('\n', preset.Content);
        Assert.Equal("Boss", preset.RuntimeIdentity);
    }

    [Theory]
    [InlineData("{\"Name\":5}")]
    [InlineData("{\"Name\":\"Invalid\",\"Group\":false}")]
    [InlineData("{\"Name\":\"Invalid\",\"ElementsL\":[null]}")]
    [InlineData("{\"Name\":\"Invalid\",\"ZoneLockH\":[\"837\"]}")]
    public void BadMetadataDoesNotDiscardOtherPresets(string invalid)
    {
        var result = Index("~Lv2~" + invalid + "\n~Lv2~{\"Name\":\"Valid\"}");
        Assert.Equal("Valid", Assert.Single(result).Title);
    }

    [Fact]
    public void ZoneBlacklistIsNotTreatedAsSupportedDuties()
    {
        Assert.Empty(Assert.Single(Index("~Lv2~{\"Name\":\"Elsewhere\",\"ZoneLockH\":[837],\"IsZoneBlacklist\":true}")).TerritoryIds);
    }

    [Theory]
    [InlineData("[837 + 1]")]
    [InlineData("[837, ..Other]")]
    [InlineData("new HashSet<uint>(837) { 838 }")]
    [InlineData("Flag ? [837] : [838]")]
    public void DoesNotGuessTerritoriesFromComputedScriptExpressions(string expression)
    {
        var preset = Assert.Single(Index($"class S : SplatoonScript {{ public override HashSet<uint> ValidTerritories => {expression}; }}", "S.cs"));
        Assert.Empty(preset.TerritoryIds);
    }

    [Fact]
    public void ReadsOnlyTheScriptPropertyAndResolvesNestedNamespaces()
    {
        var preset = Assert.Single(Index("class Helper { uint ValidTerritories => 1; } namespace A { namespace B { class S : SplatoonScript { public override HashSet<uint> ValidTerritories { get { return new() { 837 }; } } } } }", "S.cs"));
        Assert.Equal("A.B@S", preset.RuntimeIdentity);
        Assert.Equal([837u], preset.TerritoryIds);
    }

    [Fact]
    public void EmptyTranslationsAndZeroIdsDoNotHideLanguageDependency()
    {
        var preset = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\",\"ElementsL\":[{\"refActorName\":\"Leviathan\",\"refActorNameIntl\":{},\"refActorNPCID\":0}]}"));
        Assert.True(preset.LanguageDependent);
    }

    [Theory]
    [InlineData("SplatoonScript<Config>")]
    [InlineData("Splatoon.SplatoonScripting.SplatoonScript<Config>")]
    public void IndexesGenericScriptBaseUsedByRecentUpstreamScripts(string baseType)
    {
        var preset = Assert.Single(Index($"namespace Example; class S : {baseType} {{ public override HashSet<uint> ValidTerritories {{ get; }} = [1346]; }}", "S.cs"));
        Assert.Equal("Example@S", preset.RuntimeIdentity);
        Assert.NotEqual(PresetCompatibility.Incompatible, preset.Compatibility);
        Assert.Equal([1346u], preset.TerritoryIds);
    }

    [Fact]
    public void CatalogUpgradePreservesAllExistingSourcePreferences()
    {
        var store = new PresetHubStore(directory);
        var custom = RepositoryDefinition.OfficialSplatoon() with
        {
            Enabled = false, Ref = "custom", AllowScriptInstallation = false,
            PathPrefixes = [], ExcludedPathPrefixes = ["private/"], SourcePriority = 12,
        };
        store.SaveRepositories([custom]);
        File.WriteAllText(Path.Combine(directory, "repository-catalog-version.json"), "1");
        var repositories = store.LoadRepositories();
        Assert.Equal(custom, repositories.Single(x => x.Id == custom.Id) with
        {
            PathPrefixes = custom.PathPrefixes, ExcludedPathPrefixes = custom.ExcludedPathPrefixes,
        });
        Assert.Single(repositories, x => x.FullName == "Hibiya615/Splatoon_Presets");
    }

    [Fact]
    public void DoesNotMergeUnrelatedGlobalPresetsWithTheSameName()
    {
        var first = Index("~Lv2~{\"Name\":\"Boss\",\"ElementsL\":[]}", "a.md");
        var second = Index("~Lv2~{\"Name\":\"Boss\",\"ElementsL\":[{\"radius\":5}]}", "b.md");
        Assert.Equal(2, PresetCatalog.Build(first.Concat(second)).Count);
    }

    [Fact]
    public void MigratesOrdinalRecordsWithoutClaimingTheNewExportAtThatOrdinal()
    {
        var store = new PresetHubStore(directory);
        var original = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\",\"ZoneLockH\":[837]}"));
        var registry = new InstallationRegistry(store);
        registry.MarkInstalled(original with { Id = "old-ordinal-id" }, "Boss");
        var changed = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\",\"ZoneLockH\":[837],\"Enabled\":false}"));
        var inserted = Assert.Single(Index("~Lv2~{\"Name\":\"New export\",\"ZoneLockH\":[837]}"));
        Assert.Equal(InstallationStatus.UpdateAvailable, registry.GetStatus(changed));
        Assert.Equal(InstallationStatus.NotInstalled, registry.GetStatus(inserted));
        registry.MarkInstalled(changed, "hub:new-guid");
        Assert.Equal(changed.Id, Assert.Single(registry.Records).PresetId);
    }

    [Fact]
    public void UpdatingAnAliasLeavesOneRecordAndUninstallRemovesIt()
    {
        var preset = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\"}"));
        var registry = new InstallationRegistry(new(directory));
        registry.MarkInstalled(preset with { Id = "mirror" }, "Boss");
        preset = preset with { AlternatePresetIds = ["mirror"] };
        registry.MarkInstalled(preset, "Boss");
        Assert.Single(registry.Records);
        registry.Remove(preset);
        Assert.Empty(new InstallationRegistry(new(directory)).Records);
    }

    [Fact]
    public void InstallationsStayAccessibleAfterSourceRemovalAndDoNotMaskUpdates()
    {
        var preset = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\"}"));
        var registry = new InstallationRegistry(new(directory));
        registry.MarkInstalled(preset, "Boss");
        Assert.Equal(preset.Id, Assert.Single(registry.IncludeUnavailableInstallations([])).Id);
        var updated = preset with { ContentHash = "updated", Fingerprint = "updated" };
        var entries = registry.IncludeUnavailableInstallations([updated]);
        Assert.Equal(InstallationStatus.UpdateAvailable, registry.GetStatus(Assert.Single(entries)));
    }

    [Fact]
    public void CorruptRepositoryFileRecoversEvenWhenCatalogVersionIsCurrent()
    {
        var store = new PresetHubStore(directory);
        store.LoadRepositories();
        File.WriteAllText(Path.Combine(directory, "repositories.json"), "broken");
        Assert.Equal(RepositoryDefinition.CuratedCatalog().Count, store.LoadRepositories().Count);
    }

    [Fact]
    public void FailedRegistryWriteDoesNotChangeInMemoryInstallation()
    {
        var registry = new InstallationRegistry(new(directory));
        Directory.CreateDirectory(Path.Combine(directory, "installations.json"));
        var preset = Assert.Single(Index("~Lv2~{\"Name\":\"Boss\"}"));
        var error = Record.Exception(() => registry.MarkInstalled(preset, "Boss"));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Empty(registry.Records);
    }

    [Fact]
    public void PromptUsesCacheDuringSyncAndOnlyAppearsOncePerVisit()
    {
        var scheduler = new DutyPromptScheduler();
        scheduler.Schedule(837);
        Assert.False(scheduler.ShouldCheck(837, false, true));
        Assert.True(scheduler.ShouldCheck(837, true, true));
        scheduler.CompleteCheck(true, true);
        Assert.False(scheduler.ShouldCheck(837, true, true));
        scheduler.Schedule(837);
        Assert.True(scheduler.ShouldCheck(837, true, true));
    }

    [Fact]
    public void EmptyCacheWaitsForSyncAndTerritoryChangeCancelsOldPrompt()
    {
        var scheduler = new DutyPromptScheduler();
        scheduler.Schedule(837);
        scheduler.CompleteCheck(false, true);
        Assert.True(scheduler.ShouldCheck(837, true, true));
        Assert.False(scheduler.ShouldCheck(838, true, true));
        scheduler.Schedule(838);
        scheduler.CompleteCheck(false, false);
        Assert.False(scheduler.ShouldCheck(838, true, true));
    }

    public void Dispose() { if(Directory.Exists(directory)) Directory.Delete(directory, true); }
}
