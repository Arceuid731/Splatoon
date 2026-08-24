using System.Text.Json;

namespace Splatoon.PresetHub.Core;

public sealed class PresetHubStore
{
    private const int CurrentCuratedCatalogVersion = 1;
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
        var repositories = Load<List<RepositoryDefinition>>("repositories.json") ?? [];
        var catalogVersion = Load<int?>("repository-catalog-version.json") ?? 0;
        if(catalogVersion < CurrentCuratedCatalogVersion)
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
                else
                {
                    var existing = repositories[existingIndex];
                    repositories[existingIndex] = curated with
                    {
                        Id = existing.Id,
                        Enabled = existing.Enabled,
                        Ref = existing.Ref,
                        PathPrefixes = existing.PathPrefixes.Count == 0 && curated.PathPrefixes.Count > 0
                            ? curated.PathPrefixes
                            : existing.PathPrefixes,
                        ExcludedPathPrefixes = existing.ExcludedPathPrefixes.Count == 0
                            ? curated.ExcludedPathPrefixes
                            : existing.ExcludedPathPrefixes,
                    };
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
