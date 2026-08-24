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

public enum RepositoryRole
{
    Original,
    Aggregator,
    Mirror,
    Archive,
}

public enum PresetFormat
{
    ModernLayout,
    LegacyLayout,
    NativeScript,
}

public enum PresetCompatibility
{
    Compatible,
    NeedsReview,
    Incompatible,
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
    public RepositoryRole Role { get; init; } = RepositoryRole.Original;
    public int SourcePriority { get; init; } = 50;
    public bool AllowScriptInstallation { get; init; } = true;
    public IReadOnlyList<string> PathPrefixes { get; init; } = [];
    public IReadOnlyList<string> ExcludedPathPrefixes { get; init; } = [];

    public string FullName => $"{Owner}/{Name}";

    public static RepositoryDefinition OfficialSplatoon() => new()
    {
        Id = "punishxiv-splatoon",
        Owner = "PunishXIV",
        Name = "Splatoon",
        DisplayName = "Official Splatoon",
        Trust = RepositoryTrust.Official,
        Role = RepositoryRole.Original,
        SourcePriority = 90,
        PathPrefixes = ["Presets/", "SplatoonScripts/"],
        ExcludedPathPrefixes = ["SplatoonScripts/Tests/"],
    };

    public static IReadOnlyList<RepositoryDefinition> CuratedCatalog() =>
    [
        OfficialSplatoon(),
        Community("adamchris1992-ffxivsplat", "adamchris1992", "ffxivsplat", true, ["Presets/"]),
        Community("ksirashi-presets", "Ksirashi", "Presets", true),
        Community("lechuckxiv-xivstuff", "LeChuckXIV", "xivstuff", true, ["Splatoon/"]),
        Community("buddiman-presets", "buddiman", "SplatoonPresets", true),
        Community("ungeho-mydalamudpresets", "ungeho", "MyDalamudPresets", false, ["splatoon/"]),
        Community("cptjabberwock-splatoonpresetslist", "cptjabberwock", "SplatoonPresetsList", false,
            ["Community Presets/"], RepositoryRole.Aggregator, 40),
        Community("errerer-ffxiv-splpresets", "Errerer", "FFXIV_SPLPresets", false,
            allowScriptInstallation: false),
        Community("thakyz-spl-presets", "thakyZ", "SPL_Presets", false),
    ];

    private static RepositoryDefinition Community(
        string id,
        string owner,
        string name,
        bool enabled,
        IReadOnlyList<string>? prefixes = null,
        RepositoryRole role = RepositoryRole.Original,
        int priority = 100,
        bool allowScriptInstallation = true) => new()
    {
        Id = id,
        Owner = owner,
        Name = name,
        DisplayName = $"{owner}/{name}",
        Enabled = enabled,
        Trust = RepositoryTrust.Community,
        Role = role,
        SourcePriority = priority,
        AllowScriptInstallation = allowScriptInstallation,
        PathPrefixes = prefixes ?? [],
    };
}

public sealed record PresetSourceReference(
    string PresetId,
    string RepositoryId,
    string RepositoryName,
    string SourceUri,
    RepositoryTrust Trust,
    RepositoryRole Role,
    int Priority);

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
    public IReadOnlyList<uint> TerritoryIds { get; init; } = [];
    public required string RelativePath { get; init; }
    public required string SourceUri { get; init; }
    public required string Content { get; init; }
    public required string ContentHash { get; init; }
    public string Fingerprint { get; init; } = "";
    public string RuntimeIdentity { get; init; } = "";
    public PresetFormat Format { get; init; }
    public PresetCompatibility Compatibility { get; init; } = PresetCompatibility.Compatible;
    public string CompatibilityDetail { get; init; } = "";
    public string FamilyId { get; init; } = "";
    public int VariantCount { get; init; } = 1;
    public int ConfidenceScore { get; init; }
    public bool LanguageDependent { get; init; }
    public IReadOnlyList<string> ConfidenceNotes { get; init; } = [];
    public IReadOnlyList<PresetSourceReference> Sources { get; init; } = [];
    public IReadOnlyList<string> AlternatePresetIds { get; init; } = [];
}

public sealed record DutyPromptPreferences
{
    public bool Enabled { get; init; } = true;
    public HashSet<uint> SuppressedTerritoryIds { get; init; } = [];
}

public sealed record RepositorySnapshot
{
    public int IndexVersion { get; init; }
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
    public string Fingerprint { get; init; } = "";
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
