using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class PresetIndexerTests
{
    [Fact]
    public void IndexesLayoutsAndDerivesLocationFromMarkdownPath()
    {
        var markdown = """
            # Duty
            ```
            ~Lv2~{"Name":"First mechanic","Group":"Alexandria","ZoneLockH":[1199]}
            ~Lv2~{"Name":"Second mechanic","Group":"Alexandria","ZoneLockH":[1199,1200]}
            ```
            """;

        var result = new PresetIndexer().Index(
            RepositoryDefinition.OfficialSplatoon(),
            [("Presets/Dawntrail/Dungeons/100 - Alexandria.md", markdown)]);

        Assert.Equal(2, result.Count);
        Assert.All(result, preset => Assert.Equal(PresetKind.Layout, preset.Kind));
        Assert.Equal("Dawntrail", result[0].Expansion);
        Assert.Equal("Dungeons", result[0].Category);
        Assert.Equal("Alexandria", result[0].Duty);
        Assert.Equal([1199u], result[0].TerritoryIds);
        Assert.StartsWith("~Lv2~", result[0].Content);
    }

    [Fact]
    public void IndexesScriptIdentityAndIgnoresFilesOutsidePrefixes()
    {
        var source = """
            namespace Sample.Duty;
            public class SafeTiles : SplatoonScript
            {
                public override HashSet<uint>? ValidTerritories => [1199, 1200];
                public override Metadata Metadata => new(1, author: "Yann");
            }
            """;

        var result = new PresetIndexer().Index(
            RepositoryDefinition.OfficialSplatoon(),
            [
                ("SplatoonScripts/Duties/Dawntrail/Safe Tiles.cs", source),
                ("SplatoonScripts/Tests/Should Not Appear.cs", source),
                ("Splatoon/Internal.cs", source),
            ]);

        var preset = Assert.Single(result);
        Assert.Equal(PresetKind.Script, preset.Kind);
        Assert.Equal("Sample.Duty@SafeTiles", preset.RuntimeIdentity);
        Assert.Equal("Yann", preset.Author);
        Assert.Equal("Dawntrail", preset.Expansion);
        Assert.Equal("Duties", preset.Category);
        Assert.Equal([1199u, 1200u], preset.TerritoryIds);
    }

    [Fact]
    public void FindsFencedInlineAndMultipleLayoutsInTextFiles()
    {
        var text = """
            ```~Lv2~{"Name":"日本語","ZoneLockH":[170],"ElementsL":[]}```
            prose ~Lv2~ {"Name":"Second","ZoneLockH":[170],"ElementsL":[]}
            """;

        var result = new PresetIndexer().Index(
            new RepositoryDefinition { Id = "repo", Owner = "owner", Name = "repo" },
            [("nested/presets.txt", text)]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.Title == "日本語");
        Assert.All(result, x => Assert.Equal([170u], x.TerritoryIds));
    }

    [Fact]
    public void FindsLegacyLayoutsAndFlagsLanguageDependentMatching()
    {
        const string text = "Legacy overlay~{\"ZoneLockH\":[996],\"Elements\":{\"Boss\":{\"refActorName\":\"Hydaelyn\"}}}";

        var preset = Assert.Single(new PresetIndexer().Index(
            new RepositoryDefinition { Id = "repo", Owner = "owner", Name = "repo" },
            [("exports.md", text)]));

        Assert.Equal("Legacy overlay", preset.Title);
        Assert.Equal(PresetFormat.LegacyLayout, preset.Format);
        Assert.True(preset.LanguageDependent);
        Assert.StartsWith("Legacy overlay~{", preset.Content);
    }

    [Fact]
    public void ReadsTerritoriesFromLegacyCollectionInitializerAndBlocksKnownLegacyScripts()
    {
        const string source = """
            using Splatoon.Utils;
            class OldScript : SplatoonScript
            {
                public override HashSet<uint> ValidTerritories => new() { 1122, 1123 };
            }
            """;
        var repository = new RepositoryDefinition
        {
            Id = "repo", Owner = "owner", Name = "repo", AllowScriptInstallation = false,
        };

        var preset = Assert.Single(new PresetIndexer().Index(repository, [("Old.cs", source)]));

        Assert.Equal([1122u, 1123u], preset.TerritoryIds);
        Assert.Equal(PresetCompatibility.Incompatible, preset.Compatibility);
    }

    [Fact]
    public void RecognizesExpansionAndCategoryRegardlessOfDirectoryOrder()
    {
        const string layout = "~Lv2~{\"Name\":\"Boss\",\"ZoneLockH\":[1041],\"ElementsL\":[]}";
        var preset = Assert.Single(new PresetIndexer().Index(
            new RepositoryDefinition { Id = "repo", Owner = "owner", Name = "repo" },
            [("Presets/Dungeons/ARR/Brayflox.md", layout)]));

        Assert.Equal("A Realm Reborn", preset.Expansion);
        Assert.Equal("Dungeons", preset.Category);
    }
}
