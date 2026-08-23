using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Splatoon.PresetHub.Core;

public sealed class ScriptSecurityAnalyzer
{
    private static readonly (string Prefix, SecuritySeverity Severity, string Detail)[] SensitiveNamespaces =
    [
        ("System.IO", SecuritySeverity.Warning, "Can read, write, move, or delete local files."),
        ("System.Net", SecuritySeverity.Warning, "Can communicate with remote systems."),
        ("System.Diagnostics", SecuritySeverity.High, "Can start processes or inspect the host system."),
        ("System.Reflection", SecuritySeverity.High, "Can inspect or invoke code dynamically."),
        ("System.Runtime.InteropServices", SecuritySeverity.High, "Can call native operating-system APIs."),
        ("Microsoft.Win32", SecuritySeverity.High, "Can access the Windows registry."),
    ];

    private static readonly (string Token, SecuritySeverity Severity, string Detail)[] SensitiveCalls =
    [
        ("Process.Start", SecuritySeverity.High, "Starts an external process."),
        ("Assembly.Load", SecuritySeverity.High, "Loads executable code dynamically."),
        ("DllImport", SecuritySeverity.High, "Calls native code."),
        ("NativeLibrary", SecuritySeverity.High, "Loads native libraries."),
        ("Marshal.", SecuritySeverity.High, "Uses native memory or interop."),
        ("File.", SecuritySeverity.Warning, "Accesses local files."),
        ("Directory.", SecuritySeverity.Warning, "Accesses local directories."),
        ("HttpClient", SecuritySeverity.Warning, "Makes network requests."),
        ("WebClient", SecuritySeverity.Warning, "Makes network requests."),
        ("Socket", SecuritySeverity.Warning, "Uses network sockets."),
        ("Environment.", SecuritySeverity.Warning, "Reads or changes process environment data."),
        ("DalamudReflector", SecuritySeverity.High, "Reflects into other Dalamud plugins."),
    ];

    public ScriptSecurityReport Analyze(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var root = tree.GetRoot();
        var findings = new List<SecurityFinding>();

        foreach(var diagnostic in tree.GetDiagnostics().Where(x => x.Severity == DiagnosticSeverity.Error).Take(5))
        {
            findings.Add(new(
                "syntax-error",
                SecuritySeverity.High,
                "Script has syntax errors",
                diagnostic.GetMessage(),
                diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1));
        }

        foreach(var usingDirective in root.DescendantNodes().OfType<UsingDirectiveSyntax>())
        {
            var name = usingDirective.Name?.ToString() ?? "";
            foreach(var sensitive in SensitiveNamespaces.Where(x => name.StartsWith(x.Prefix, StringComparison.Ordinal)))
            {
                findings.Add(new(
                    $"namespace:{sensitive.Prefix}",
                    sensitive.Severity,
                    $"Sensitive namespace: {sensitive.Prefix}",
                    sensitive.Detail,
                    usingDirective.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
        }

        foreach(var node in root.DescendantNodesAndSelf())
        {
            var text = node switch
            {
                InvocationExpressionSyntax invocation => invocation.Expression.ToString(),
                AttributeSyntax attribute => attribute.Name.ToString(),
                ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
                _ => null,
            };
            if(text is null) continue;

            foreach(var sensitive in SensitiveCalls.Where(x => text.Contains(x.Token, StringComparison.Ordinal)))
            {
                findings.Add(new(
                    $"call:{sensitive.Token}",
                    sensitive.Severity,
                    $"Sensitive API: {sensitive.Token}",
                    sensitive.Detail,
                    node.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
            }
        }

        foreach(var unsafeNode in root.DescendantNodes().Where(x =>
                    x is UnsafeStatementSyntax ||
                    x is PointerTypeSyntax ||
                    x.ChildTokens().Any(token => token.IsKind(SyntaxKind.UnsafeKeyword))))
        {
            findings.Add(new(
                "unsafe-code",
                SecuritySeverity.High,
                "Unsafe code",
                "Can work directly with memory pointers.",
                unsafeNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1));
        }

        var distinct = findings
            .GroupBy(x => (x.Id, x.Line))
            .Select(x => x.First())
            .OrderByDescending(x => x.Severity)
            .ThenBy(x => x.Line)
            .ToList();
        distinct.Insert(0, new(
            "not-a-sandbox",
            SecuritySeverity.Information,
            "Static review is not a sandbox",
            "A script with no warning can still execute with the same access as Splatoon. Review its source and repository before installing."));

        return new(distinct, ContentHash.Sha256(source));
    }
}
