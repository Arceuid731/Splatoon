namespace Splatoon.PresetHub.Core;

public enum PresetKind
{
    Layout,
    Script,
}

public enum RepositoryTrust
{
    Untrusted,
    Community,
    Official,
}

public enum InstallationStatus
{
    NotInstalled,
    Installed,
    UpdateAvailable,
}

public enum SecuritySeverity
{
    Information,
    Warning,
    High,
}

public sealed record RepositoryDefinition
{
    public required string Id { get; init; }
    public required string Owner { get; init; }
    public required string Name { get; init; }
    public string Ref { get; init; } = "main";
    public string DisplayName { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public RepositoryTrust Trust { get; init; } = RepositoryTrust.Untrusted;
    public IReadOnlyList<string> PathPrefixes { get; init; } = [];

    public string FullName => $"{Owner}/{Name}";

    public static RepositoryDefinition OfficialSplatoon() => new()
    {
        Id = "punishxiv-splatoon",
        Owner = "PunishXIV",
        Name = "Splatoon",
        DisplayName = "Official Splatoon",
        Trust = RepositoryTrust.Official,
        PathPrefixes = ["Presets/", "SplatoonScripts/"],
    };
}

public sealed record PresetEntry
{
    public required string Id { get; init; }
    public required string RepositoryId { get; init; }
    public required string RepositoryName { get; init; }
    public required RepositoryTrust Trust { get; init; }
    public required PresetKind Kind { get; init; }
    public required string Title { get; init; }
    public string Author { get; init; } = "";
    public string Description { get; init; } = "";
    public string Expansion { get; init; } = "";
    public string Category { get; init; } = "";
    public string Duty { get; init; } = "";
    public required string RelativePath { get; init; }
    public required string SourceUri { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public string RuntimeIdentity { get; init; } = "";
}

public sealed record RepositorySnapshot
{
    public required RepositoryDefinition Repository { get; init; }
    public required string Revision { get; init; }
    public required DateTimeOffset SyncedAt { get; init; }
    public required IReadOnlyList<PresetEntry> Presets { get; init; }
}

public sealed record InstallationRecord
{
    public required string PresetId { get; init; }
    public required PresetKind Kind { get; init; }
    public required string ContentHash { get; init; }
    public required string RuntimeIdentity { get; init; }
    public required string RepositoryName { get; init; }
    public required string SourceUri { get; init; }
    public required DateTimeOffset InstalledAt { get; init; }
}

public sealed record SecurityFinding(
    string Id,
    SecuritySeverity Severity,
    string Title,
    string Detail,
    int? Line = null);

public sealed record ScriptSecurityReport(
    IReadOnlyList<SecurityFinding> Findings,
    string ContentHash)
{
    public SecuritySeverity HighestSeverity => Findings.Count == 0
        ? SecuritySeverity.Information
        : Findings.Max(x => x.Severity);
}
