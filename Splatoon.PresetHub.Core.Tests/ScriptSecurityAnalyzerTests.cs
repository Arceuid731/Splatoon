using Splatoon.PresetHub.Core;

namespace Splatoon.PresetHub.Core.Tests;

public sealed class ScriptSecurityAnalyzerTests
{
    [Fact]
    public void FlagsNativeProcessAndFileAccess()
    {
        var source = """
            using System.Diagnostics;
            using System.IO;
            using System.Runtime.InteropServices;
            Process.Start("tool");
            File.Delete("file");
            """;

        var report = new ScriptSecurityAnalyzer().Analyze(source);

        Assert.Contains(report.Findings, finding => finding.Id == "namespace:System.Diagnostics");
        Assert.Contains(report.Findings, finding => finding.Id == "namespace:System.IO");
        Assert.Contains(report.Findings, finding => finding.Id == "call:Process.Start");
        Assert.Contains(report.Findings, finding => finding.Id == "call:File.");
        Assert.Equal(ContentHash.Sha256(source), report.ContentHash);
        Assert.Equal(SecuritySeverity.High, report.HighestSeverity);
    }

    [Fact]
    public void AlwaysExplainsThatStaticReviewIsNotASandbox()
    {
        var report = new ScriptSecurityAnalyzer().Analyze("class Example { }");

        Assert.Contains(report.Findings, finding => finding.Id == "not-a-sandbox");
    }
}
