using System.Text.Json;

namespace Splatoon.PresetHub.Core;

public sealed class PresetHubStore
{
    private const int CurrentCuratedCatalogVersion = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string rootDirectory;

    public PresetHubStore(string rootDirectory)
    {
        this.rootDirectory = rootDirectory;
        Directory.CreateDirectory(rootDirectory);
    }

    public IReadOnlyList<RepositoryDefinition> LoadRepositories()
    {
        var savedRepositories = Load<List<RepositoryDefinition>>("repositories.json");
        var repositories = savedRepositories ?? [];
        var catalogVersion = Load<int?>("repository-catalog-version.json") ?? 0;
        if(savedRepositories == null || catalogVersion < CurrentCuratedCatalogVersion)
        {
            foreach(var curated in RepositoryDefinition.CuratedCatalog())
            {
                var existingIndex = repositories.FindIndex(x =>
                    x.Id.Equals(curated.Id, StringComparison.OrdinalIgnoreCase) ||
                    x.FullName.Equals(curated.FullName, StringComparison.OrdinalIgnoreCase));
                if(existingIndex < 0)
                {
                    repositories.Add(curated);
                }
                else if(curated.Id == "hibiya615-splatoon-presets" &&
                        repositories[existingIndex].PathPrefixes.SequenceEqual(new[] { "[EN Set]/" }))
                {
                    // Expand the previous built-in restriction; retain enabled/script settings.
                    repositories[existingIndex] = repositories[existingIndex] with { PathPrefixes = [] };
                }
            }
            SaveRepositories(repositories);
            Save("repository-catalog-version.json", CurrentCuratedCatalogVersion);
        }
        return repositories;
    }

    public void SaveRepositories(IEnumerable<RepositoryDefinition> repositories) =>
        Save("repositories.json", repositories.ToArray());

    public RepositorySnapshot? LoadSnapshot(string repositoryId) =>
        Load<RepositorySnapshot>($"cache/{SafeName(repositoryId)}.json");

    public void SaveSnapshot(RepositorySnapshot snapshot) =>
        Save($"cache/{SafeName(snapshot.Repository.Id)}.json", snapshot);

    public IReadOnlyList<InstallationRecord> LoadInstallations() =>
        Load<List<InstallationRecord>>("installations.json") ?? [];

    public void SaveInstallations(IEnumerable<InstallationRecord> installations) =>
        Save("installations.json", installations.ToArray());

    public DutyPromptPreferences LoadDutyPromptPreferences() =>
        Load<DutyPromptPreferences>("duty-prompt.json") ?? new();

    public void SaveDutyPromptPreferences(DutyPromptPreferences preferences) =>
        Save("duty-prompt.json", preferences);

    public CoveragePreferences LoadCoveragePreferences() => Load<CoveragePreferences>("coverage-preferences.json") ?? new();
    public void SaveCoveragePreferences(CoveragePreferences preferences) => Save("coverage-preferences.json", preferences);
    public CoverageLibrary? LoadCoverageLibrary()
    {
        var library = Load<CoverageLibrary>("coverage-library.json");
        return library?.Version == CoverageLibrary.CurrentVersion ? library : null;
    }
    public void SaveCoverageLibrary(CoverageLibrary library) => Save("coverage-library.json", library);

    private T? Load<T>(string relativePath)
    {
        var path = GetPath(relativePath);
        if(!File.Exists(path)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOptions);
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return default;
        }
    }

    private void Save<T>(string relativePath, T value)
    {
        var path = GetPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temporaryPath, path, true);
    }

    private string GetPath(string relativePath) => Path.Combine(rootDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string SafeName(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
}
