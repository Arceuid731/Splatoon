namespace Splatoon.PresetHub.Core;

public sealed record CoverageSignal(string Kind, string Value)
{
    public string Key => $"{Kind}:{Value}";
}

public sealed record CoverageClaim
{
    public required string MechanicId { get; init; }
    public required string AidId { get; init; }
    public required string Name { get; init; }
    public string ActorKey { get; init; } = "";
    public uint ActorNameId { get; init; }
    public IReadOnlyList<CoverageSignal> Signals { get; init; } = [];
    public string Role { get; init; } = "Drawing";
}

/// <summary>A self-contained layout fragment. Linked elements always travel together.</summary>
public sealed record CoverageContribution
{
    public required string Id { get; init; }
    public required uint TerritoryId { get; init; }
    public required string SourcePresetId { get; init; }
    public required string RepositoryId { get; init; }
    public required string RepositoryName { get; init; }
    public required string SourceUri { get; init; }
    public required string SourceTitle { get; init; }
    public required string LayoutContent { get; init; }
    public required string Fingerprint { get; init; }
    public required IReadOnlyList<CoverageClaim> Claims { get; init; }
    public bool Linked { get; init; }
    public bool Automatic { get; init; } = true;
    public bool LanguageDependent { get; init; }
    public int LanguageRank { get; init; }
    public int Confidence { get; init; }
    public int ElementCount { get; init; }
    public bool Local { get; init; }
    public string Detail { get; init; } = "";
}

public sealed record CoverageAnalysis(
    IReadOnlyList<CoverageContribution> Contributions,
    IReadOnlyList<string> Diagnostics);

public sealed record CoveragePreferences
{
    public bool Enabled { get; init; } = true;
    public HashSet<uint> DisabledTerritories { get; init; } = [];
    public HashSet<string> DisabledActors { get; init; } = [];
    public HashSet<string> DisabledMechanics { get; init; } = [];
    public Dictionary<string, string> SelectedAlternatives { get; init; } = [];
    public HashSet<string> MigratedInstallations { get; init; } = [];
    public HashSet<string> DisabledScripts { get; init; } = [];

    public bool Allows(CoverageContribution contribution) => Enabled &&
        !DisabledTerritories.Contains(contribution.TerritoryId) && contribution.Claims.All(claim =>
            !DisabledMechanics.Contains(claim.MechanicId) &&
            !DisabledActors.Contains(ActorPreferenceKey(contribution.TerritoryId, claim.ActorKey)));

    public static string ActorPreferenceKey(uint territoryId, string actorKey) => $"{territoryId}:{actorKey}";
}

public sealed record CoverageLibrary
{
    public const int CurrentVersion = 2;
    public int Version { get; init; } = CurrentVersion;
    public DateTimeOffset ComputedAt { get; init; }
    public string SourceRevision { get; init; } = "";
    public IReadOnlyList<CoverageContribution> Contributions { get; init; } = [];
    public IReadOnlyDictionary<uint, IReadOnlyList<string>> PreparedSelections { get; init; } = new Dictionary<uint, IReadOnlyList<string>>();
    public IReadOnlyList<string> Diagnostics { get; init; } = [];
}
