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
            ~Lv2~{"Name":"First mechanic","Group":"Alexandria"}
            ~Lv2~{"Name":"Second mechanic","Group":"Alexandria"}
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
        Assert.StartsWith("~Lv2~", result[0].Content);
    }

    [Fact]
    public void IndexesScriptIdentityAndIgnoresFilesOutsidePrefixes()
    {
        var source = """
            namespace Sample.Duty;
            public class SafeTiles : SplatoonScript
            {
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
    }
}
